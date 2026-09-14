using UnityEngine;
using UnityEngine.Internal;

namespace TMPro
{
    // TextMeshPro on this platform (ADR-013). A TextMeshProUGUI is a text
    // element of the baked-font UI path: at export its font asset resolves
    // to the source TTF, which Unity's own rasteriser bakes at the
    // component's size and style, and the console draws the result the way
    // it draws a Text. Everything TMP does with SDF shading, outlines,
    // gradients, auto-sizing and rich text markup is a bake-time or
    // never-time property here (deviation 45 in docs/supported-api.md):
    // what is offered is what works at runtime, text, colour, alignment and
    // visibility, with the same names and values as TMP's.
    //
    // The enums carry TMP's numeric values so that serialised or switch-based
    // user code keeps its meaning.

    public enum HorizontalAlignmentOptions
    {
        Left = 0x1,
        Center = 0x2,
        Right = 0x4,
        Justified = 0x8,
        Flush = 0x10,
        Geometry = 0x20,
    }

    public enum VerticalAlignmentOptions
    {
        Top = 0x100,
        Middle = 0x200,
        Bottom = 0x400,
        Baseline = 0x800,
        Geometry = 0x1000,
        Capline = 0x2000,
    }

    public enum TextAlignmentOptions
    {
        TopLeft = HorizontalAlignmentOptions.Left | VerticalAlignmentOptions.Top,
        Top = HorizontalAlignmentOptions.Center | VerticalAlignmentOptions.Top,
        TopRight = HorizontalAlignmentOptions.Right | VerticalAlignmentOptions.Top,
        TopJustified = HorizontalAlignmentOptions.Justified | VerticalAlignmentOptions.Top,
        TopFlush = HorizontalAlignmentOptions.Flush | VerticalAlignmentOptions.Top,
        TopGeoAligned = HorizontalAlignmentOptions.Geometry | VerticalAlignmentOptions.Top,

        Left = HorizontalAlignmentOptions.Left | VerticalAlignmentOptions.Middle,
        Center = HorizontalAlignmentOptions.Center | VerticalAlignmentOptions.Middle,
        Right = HorizontalAlignmentOptions.Right | VerticalAlignmentOptions.Middle,
        Justified = HorizontalAlignmentOptions.Justified | VerticalAlignmentOptions.Middle,
        Flush = HorizontalAlignmentOptions.Flush | VerticalAlignmentOptions.Middle,
        CenterGeoAligned = HorizontalAlignmentOptions.Geometry | VerticalAlignmentOptions.Middle,

        BottomLeft = HorizontalAlignmentOptions.Left | VerticalAlignmentOptions.Bottom,
        Bottom = HorizontalAlignmentOptions.Center | VerticalAlignmentOptions.Bottom,
        BottomRight = HorizontalAlignmentOptions.Right | VerticalAlignmentOptions.Bottom,
        BottomJustified = HorizontalAlignmentOptions.Justified | VerticalAlignmentOptions.Bottom,
        BottomFlush = HorizontalAlignmentOptions.Flush | VerticalAlignmentOptions.Bottom,
        BottomGeoAligned = HorizontalAlignmentOptions.Geometry | VerticalAlignmentOptions.Bottom,

        BaselineLeft = HorizontalAlignmentOptions.Left | VerticalAlignmentOptions.Baseline,
        Baseline = HorizontalAlignmentOptions.Center | VerticalAlignmentOptions.Baseline,
        BaselineRight = HorizontalAlignmentOptions.Right | VerticalAlignmentOptions.Baseline,
        BaselineJustified = HorizontalAlignmentOptions.Justified | VerticalAlignmentOptions.Baseline,
        BaselineFlush = HorizontalAlignmentOptions.Flush | VerticalAlignmentOptions.Baseline,
        BaselineGeoAligned = HorizontalAlignmentOptions.Geometry | VerticalAlignmentOptions.Baseline,

        MidlineLeft = HorizontalAlignmentOptions.Left | VerticalAlignmentOptions.Geometry,
        Midline = HorizontalAlignmentOptions.Center | VerticalAlignmentOptions.Geometry,
        MidlineRight = HorizontalAlignmentOptions.Right | VerticalAlignmentOptions.Geometry,
        MidlineJustified = HorizontalAlignmentOptions.Justified | VerticalAlignmentOptions.Geometry,
        MidlineFlush = HorizontalAlignmentOptions.Flush | VerticalAlignmentOptions.Geometry,
        MidlineGeoAligned = HorizontalAlignmentOptions.Geometry | VerticalAlignmentOptions.Geometry,

        CaplineLeft = HorizontalAlignmentOptions.Left | VerticalAlignmentOptions.Capline,
        Capline = HorizontalAlignmentOptions.Center | VerticalAlignmentOptions.Capline,
        CaplineRight = HorizontalAlignmentOptions.Right | VerticalAlignmentOptions.Capline,
        CaplineJustified = HorizontalAlignmentOptions.Justified | VerticalAlignmentOptions.Capline,
        CaplineFlush = HorizontalAlignmentOptions.Flush | VerticalAlignmentOptions.Capline,
        CaplineGeoAligned = HorizontalAlignmentOptions.Geometry | VerticalAlignmentOptions.Capline,

        Converted = 0xFFFF,
    }

    public enum FontStyles
    {
        Normal = 0x0,
        Bold = 0x1,
        Italic = 0x2,
        Underline = 0x4,
        LowerCase = 0x8,
        UpperCase = 0x10,
        SmallCaps = 0x20,
        Strikethrough = 0x40,
        Superscript = 0x80,
        Subscript = 0x100,
        Highlight = 0x200,
    }

    public abstract class TMP_Text : UnityEngine.UI.Graphic
    {
        private string m_Text = "";
        private bool m_RichText = true;

        public string text
        {
            get => m_Text;
            set => Apply(value);
        }

        public void SetText(string sourceText)
        {
            Apply(sourceText);
        }

        // With rich text on (TMP's default) markup is STRIPPED before the
        // string reaches the console: the baked font has no bold, colour
        // or size variants to switch to, so "<b>Go</b>" shows "Go", which
        // is closer to the author's intent than showing the tags.
        public bool richText
        {
            get => m_RichText;
            set
            {
                if (m_RichText != value)
                {
                    m_RichText = value;
                    Apply(m_Text);
                }
            }
        }

        // The six TMP horizontal and six vertical options fold onto the
        // three the draw table has: Justified and Flush draw as Left,
        // Geometry as Center; Baseline draws as Bottom, Capline as Top,
        // Geometry (midline) as Middle. Reading back returns the folded
        // value.
        public TextAlignmentOptions alignment
        {
            get => (TextAlignmentOptions)((int)horizontalAlignment |
                                          (int)verticalAlignment);
            set
            {
                int v = (int)value;
                AlignH = FoldHorizontal(v & 0xFF);
                AlignV = FoldVertical(v & 0xFF00);
                PushAlign();
            }
        }

        public HorizontalAlignmentOptions horizontalAlignment
        {
            get
            {
                switch (AlignH)
                {
                    case 1: return HorizontalAlignmentOptions.Center;
                    case 2: return HorizontalAlignmentOptions.Right;
                    default: return HorizontalAlignmentOptions.Left;
                }
            }
            set
            {
                AlignH = FoldHorizontal((int)value);
                PushAlign();
            }
        }

        public VerticalAlignmentOptions verticalAlignment
        {
            get
            {
                switch (AlignV)
                {
                    case 1: return VerticalAlignmentOptions.Middle;
                    case 2: return VerticalAlignmentOptions.Bottom;
                    default: return VerticalAlignmentOptions.Top;
                }
            }
            set
            {
                AlignV = FoldVertical((int)value);
                PushAlign();
            }
        }

        private static int FoldHorizontal(int bits)
        {
            if ((bits & (int)HorizontalAlignmentOptions.Right) != 0) return 2;
            if ((bits & ((int)HorizontalAlignmentOptions.Center |
                         (int)HorizontalAlignmentOptions.Geometry)) != 0) return 1;
            return 0;
        }

        private static int FoldVertical(int bits)
        {
            if ((bits & ((int)VerticalAlignmentOptions.Bottom |
                         (int)VerticalAlignmentOptions.Baseline)) != 0) return 2;
            if ((bits & ((int)VerticalAlignmentOptions.Middle |
                         (int)VerticalAlignmentOptions.Geometry)) != 0) return 1;
            return 0;
        }

        private void Apply(string value)
        {
            m_Text = value ?? "";
            if (Element >= 0)
            {
                // 48-byte native cap (deviation 30).
                Native.ps2ur_ui_set_text(Element,
                                         m_RichText ? StripTags(m_Text) : m_Text);
            }
        }

        // Removes <tag>, </tag> and <tag=value> runs. A lone '<' that does
        // not close, or one followed by something that is not a tag start,
        // is kept as text, so "a < b" survives.
        internal static string StripTags(string s)
        {
            if (s.IndexOf('<') < 0)
            {
                return s;
            }
            var sb = new System.Text.StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '<' && i + 1 < s.Length && IsTagStart(s[i + 1]))
                {
                    int close = s.IndexOf('>', i + 1);
                    int reopen = s.IndexOf('<', i + 1);
                    if (close > i && (reopen < 0 || reopen > close))
                    {
                        i = close + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool IsTagStart(char c)
        {
            return c == '/' || c == '#' || (c >= 'a' && c <= 'z') ||
                   (c >= 'A' && c <= 'Z');
        }
    }

    public sealed class TextMeshProUGUI : TMP_Text
    {
    }
}
