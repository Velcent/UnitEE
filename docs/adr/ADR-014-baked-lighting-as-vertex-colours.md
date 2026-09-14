# ADR-014: Baked lighting travels as vertex colours sampled at export

Status: accepted. Plan M8 task 6 ("optional baked vertex-colour lighting
from the Unity lightmapper; there is no room for lightmap textures").

## Decision

A build profile whose Lighting is Baked exports every lightmapped
`MeshRenderer` with its vertex colours sampled from Unity's lightmaps: each
vertex's lightmap UV (uv2, scaled and offset per renderer) is read from the
decoded lightmap, taken to the framebuffer's gamma space, multiplied by the
material tint, and written where the vertex colour already goes. The
console draws the result through the programs it has:

- textured surfaces keep the textured layout, with the baked colour as the
  GS modulate in 128-is-one units, so a sunlit texel legitimately brighter
  than its texture reaches 2.0 instead of clipping;
- untextured surfaces drop to the unlit layout, where the vertex colour is
  the pixel; in a fogged scene they keep the lit-fog layout with the
  material's new baked flag, which makes the runtime feed the program no
  light and an ambient of 1.0 so the colours pass through and fog still
  applies.

Everything the lightmapper did not touch, dynamic objects above all, keeps
the realtime path: one directional light plus ambient per vertex on VU1.
The ambient is the scene's own, which M14 already exports with the
camera.

Baked meshes are subdivided at export until no edge exceeds the profile's
spacing in world units (1.5 by default), because a shadow edge can only
land where there is a vertex to carry it. Each lightmapped renderer gets
its own copy of its mesh, as Unity gives it its own lightmap texels.

## Why vertex colours and not lightmap textures

The GS has one texture unit. A lightmapped textured surface would be a
second pass per triangle with a multiply blend, doubling draw and fill
cost for exactly the large surfaces that dominate a frame, and the
lightmaps themselves would compete with the art for the ~1.2 MB of VRAM
left beside the framebuffers. Per-vertex lighting is what the platform's
own titles shipped and what its vertex format already carries: the baked
colour costs nothing at runtime and nothing in VRAM. The cost is
resolution, paid in vertices where the profile asks for it, and the loss
is the realtime half of Mixed lights, which is recorded as deviation 46.

## Why sample at export and not bake in a DCC tool

The Unity lightmapper is the bake the author already has, with shadows,
bounce and ambient occlusion in it, and it is per instance. Sampling it
at export means no second tool, no texture to hand-map, and a scene that
looks like its Editor view with the lights set to Baked.

## Consequences

- Runtime: `LoadedMaterial.baked` (MATL flags bit5; bits 3 and 4 are
  M14's clamp and sky) and one branch in the lit-constants path. No new
  microprogram, batch format or material kind. A baked renderer is never
  static batched: a merged mesh would lose its per-instance colours.
- Exporter: a lightmap sampler that decodes the platform's encoding (HDR,
  RGBM or dLDR, in linear or gamma projects), a per-renderer mesh key, and
  the spacing subdivision. Profile fields `lighting` and
  `bakedVertexSpacing`.
- Validator: a Baked profile with no lightmaps warns and exports realtime;
  a Mixed or Realtime directional light in a Baked build warns about the
  missing realtime term.
- Verification: `PS2ExportMenu.ExportBakedLightScene` bakes and exports a
  wall-and-floor scene; the scene-debug sample's luminance map shows the
  shadow on target.
