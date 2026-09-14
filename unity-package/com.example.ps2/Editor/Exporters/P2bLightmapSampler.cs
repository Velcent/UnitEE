// Baked lighting -> vertex colours (ADR-014).
//
// Unity's lightmapper has already done the expensive part: every static
// surface has a lightmap texel that says how bright it is, direct and
// indirect, shadows included. A PS2 cannot afford that texture (there is no
// second texture unit and no VRAM for it; plan M8 task 6), but it draws
// every vertex with a colour anyway, so the lightmap is sampled at each
// vertex here and travels as that colour. Runtime cost: none.
//
// The only losses are resolution (a shadow edge lands where the vertices
// are, which is why the mesh exporter subdivides baked meshes to the
// profile's spacing) and the realtime part of Mixed lights, which Unity
// would add per pixel and this path does not add at all.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ps2.Editor
{
    internal sealed class P2bLightmapSampler
    {
        private static readonly Dictionary<int, P2bLightmapSampler> s_Cache =
            new Dictionary<int, P2bLightmapSampler>();
        private static readonly HashSet<int> s_Failed = new HashSet<int>();
        private static int s_RenderersSampled;
        private static int s_VerticesShaded;

        private Color[] m_Pixels; // decoded LINEAR radiance, bottom-up rows
        private int m_W, m_H;

        internal static void BeginExport()
        {
            s_Cache.Clear();
            s_Failed.Clear();
            s_RenderersSampled = 0;
            s_VerticesShaded = 0;
        }

        internal static void CountRenderer() => s_RenderersSampled++;

        internal static void LogSummary()
        {
            if (s_RenderersSampled > 0)
            {
                Debug.Log("[PS2] baked lighting: " + s_RenderersSampled +
                          " renderers sampled from " + s_Cache.Count +
                          " lightmaps into " + s_VerticesShaded +
                          " vertex colours (ADR-014).");
            }
        }

        // The sampler for one lightmap index, decoded once per export, or
        // null (with one warning) when the lightmap cannot be read.
        internal static P2bLightmapSampler For(int index, string ownerName)
        {
            if (s_Cache.TryGetValue(index, out P2bLightmapSampler cached))
                return cached;
            if (s_Failed.Contains(index))
                return null;

            LightmapData[] maps = LightmapSettings.lightmaps;
            Texture2D source = index >= 0 && index < maps.Length
                                   ? maps[index].lightmapColor
                                   : null;
            if (source == null)
            {
                s_Failed.Add(index);
                Debug.LogWarning(
                    "[PS2] '" + ownerName + "' names lightmap " + index +
                    " but the scene has " + maps.Length + " lightmap(s). " +
                    "Generate Lighting (Window > Rendering > Lighting) and " +
                    "export again; the renderer keeps realtime lighting.");
                return null;
            }

            var sampler = new P2bLightmapSampler();
            if (!sampler.Read(source))
            {
                s_Failed.Add(index);
                Debug.LogWarning(
                    "[PS2] lightmap " + index + " (" + source.name + ") could " +
                    "not be read back; the renderers it lights keep realtime " +
                    "lighting.");
                return null;
            }
            s_Cache[index] = sampler;
            return sampler;
        }

        // The vertex colour for a lightmap UV: the decoded radiance, taken to
        // the gamma space the console's framebuffer lives in, times the
        // material tint. Textured layouts get GS modulate units (128 = 1.0,
        // 255 = 2.0, so sunlit overbright survives); untextured ones get
        // 0..255 where the vertex colour IS the pixel.
        internal Color32 VertexColour(Vector2 uv, Vector4 scaleOffset,
                                      Color material, bool gsUnits)
        {
            Vector2 st = new Vector2(uv.x * scaleOffset.x + scaleOffset.z,
                                     uv.y * scaleOffset.y + scaleOffset.w);
            Color linear = SampleBilinear(st);
            Color gamma = ToOutputSpace(linear);
            float r = gamma.r * material.r;
            float g = gamma.g * material.g;
            float b = gamma.b * material.b;
            s_VerticesShaded++;
            if (gsUnits)
            {
                return new Color32(ToByte(r * 127.5f), ToByte(g * 127.5f),
                                   ToByte(b * 127.5f), 255);
            }
            return new Color32(ToByte(r * 255f), ToByte(g * 255f),
                               ToByte(b * 255f), 255);
        }

        private static byte ToByte(float v)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);
        }

        // Radiance is linear; the console has no linear pipeline, so vertex
        // colours are what Unity would DISPLAY: the sRGB transfer for a
        // linear project, the value itself for a gamma one (where the
        // decoded lightmap is already in gamma). Values above 1.0 keep
        // growing through the same transfer, which is what makes a sunlit
        // floor brighter than its texture.
        private static Color ToOutputSpace(Color linear)
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                return linear;
            return new Color(Mathf.LinearToGammaSpace(linear.r),
                             Mathf.LinearToGammaSpace(linear.g),
                             Mathf.LinearToGammaSpace(linear.b), 1f);
        }

        private Color SampleBilinear(Vector2 st)
        {
            float fx = Mathf.Clamp(st.x * m_W - 0.5f, 0f, m_W - 1f);
            float fy = Mathf.Clamp(st.y * m_H - 0.5f, 0f, m_H - 1f);
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            int x1 = Mathf.Min(x0 + 1, m_W - 1);
            int y1 = Mathf.Min(y0 + 1, m_H - 1);
            float tx = fx - x0;
            float ty = fy - y0;
            Color a = Color.Lerp(m_Pixels[y0 * m_W + x0], m_Pixels[y0 * m_W + x1], tx);
            Color b = Color.Lerp(m_Pixels[y1 * m_W + x0], m_Pixels[y1 * m_W + x1], tx);
            return Color.Lerp(a, b, ty);
        }

        // Lightmaps are not CPU-readable and are stored ENCODED. A float
        // render-target round trip (the texture exporter's move for
        // unreadable art) yields the stored values; the decode below is the
        // one Unity's own DecodeLightmap does for the platform's encoding.
        private bool Read(Texture2D source)
        {
            int w = source.width, h = source.height;
            RenderTexture rt = RenderTexture.GetTemporary(
                w, h, 0, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Color[] stored;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(w, h, TextureFormat.RGBAFloat,
                                             false, true);
                readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readable.Apply();
                stored = readable.GetPixels();
                Object.DestroyImmediate(readable);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PS2] lightmap readback failed: " + e.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            int quality = EncodingQuality(source);
            bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
            m_Pixels = new Color[stored.Length];
            for (int i = 0; i < stored.Length; i++)
            {
                Color s = stored[i];
                Color d;
                switch (quality)
                {
                    case kEncodingNormal:
                        // RGBM: unity_Lightmap_HDR is (5, 1) in gamma and
                        // (5^2.2, 2.2) in linear.
                        d = linear
                                ? s * (34.493f * Mathf.Pow(Mathf.Max(s.a, 0f), 2.2f))
                                : s * (5.0f * s.a);
                        break;
                    case kEncodingLow:
                        // dLDR: a plain 2x (4.59 in linear) range.
                        d = s * (linear ? 4.59479f : 2.0f);
                        break;
                    default:
                        d = s; // HDR: the radiance itself
                        break;
                }
                d.a = 1f;
                m_Pixels[i] = d;
            }
            m_W = w;
            m_H = h;
            return true;
        }

        // PlayerSettings' lightmap encoding enum: Low (dLDR), Normal (RGBM),
        // High (HDR). The enum type is internal in Unity 6, so the value is
        // read through reflection; when even the method is missing, the
        // texture's own format says whether it holds HDR radiance, and a
        // non-HDR lightmap is taken as RGBM, the desktop default.
        private const int kEncodingLow = 0;
        private const int kEncodingNormal = 1;
        private const int kEncodingHigh = 2;

        private static int EncodingQuality(Texture2D source)
        {
            try
            {
                BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(
                    EditorUserBuildSettings.activeBuildTarget);
                System.Reflection.MethodInfo method = typeof(PlayerSettings).GetMethod(
                    "GetLightmapEncodingQualityForPlatformGroup",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (method != null)
                {
                    object result = method.Invoke(null, new object[] { group });
                    if (result != null)
                        return System.Convert.ToInt32(result);
                }
            }
            catch (System.Exception)
            {
                // Fall through to the format heuristic.
            }
            switch (source.format)
            {
                case TextureFormat.BC6H:
                case TextureFormat.RGBAHalf:
                case TextureFormat.RGBAFloat:
                case TextureFormat.RGB9e5Float:
                    return kEncodingHigh;
                default:
                    return kEncodingNormal;
            }
        }

        // The scene's ambient term in output (gamma) space, for the light
        // payload's ambient extension. Flat and gradient ambients are
        // authored colours and travel as they are; a skybox ambient is a
        // probe of linear radiance, averaged over the six axes.
        internal static Color SceneAmbient()
        {
            Color c;
            switch (RenderSettings.ambientMode)
            {
                case AmbientMode.Flat:
                    c = RenderSettings.ambientLight;
                    break;
                case AmbientMode.Trilight:
                    c = (RenderSettings.ambientSkyColor +
                         RenderSettings.ambientEquatorColor +
                         RenderSettings.ambientGroundColor) / 3f;
                    break;
                default:
                {
                    SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
                    var dirs = new[]
                    {
                        Vector3.up, Vector3.down, Vector3.left, Vector3.right,
                        Vector3.forward, Vector3.back,
                    };
                    var results = new Color[dirs.Length];
                    probe.Evaluate(dirs, results);
                    c = Color.black;
                    foreach (Color r in results) c += r;
                    c /= dirs.Length;
                    if (c.maxColorComponent < 1e-4f)
                    {
                        // No probe data (nothing baked yet): the flat colour
                        // the settings show is the honest fallback.
                        c = RenderSettings.ambientLight;
                    }
                    else
                    {
                        c = ToOutputSpace(c);
                    }
                    break;
                }
            }
            c.a = 1f;
            return c;
        }
    }
}
