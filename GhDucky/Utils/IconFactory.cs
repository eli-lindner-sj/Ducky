using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

// System.Drawing is Windows-targeted in .NET 8 analyzers, but Grasshopper itself
// is built on top of it (the Icon contract returns Bitmap), so we already require
// a GDI+-capable host. Silencing the platform warnings keeps the build clean.
#pragma warning disable CA1416

namespace GhDucky.Utils
{
    /// Produces small per-component icons by drawing a coloured rounded square
    /// with one or two letters in the centre. Bitmaps are cached for the life
    /// of the AppDomain so Grasshopper's frequent Icon getter calls are cheap.
    internal static class IconFactory
    {
        // Category palette — colours echo the tab subcategories so toolbar
        // sections are scannable at a glance.
        public static readonly Color Connect = ColorFromHex("#2ac3de");
        public static readonly Color Disconnect = ColorFromHex("#7dcfff");
        public static readonly Color Grasshopper = ColorFromHex("#9ece6a");
        public static readonly Color ImportFile = ColorFromHex("#ff9e64");
        public static readonly Color Query = ColorFromHex("#bb9af7");
        public static readonly Color Export = ColorFromHex("#f7768e");
        public static readonly Color Spatial = ColorFromHex("#7aa2f7");
        public static readonly Color Excel = ColorFromHex("#73dacb");
        public static readonly Color Neutral = ColorFromHex("#e0af68");

        private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();

        public static Bitmap Build(string label, Color color)
        {
            var key = (label ?? "?") + "|" + color.ToArgb();
            return Cache.GetOrAdd(key, _ => DrawIcon(label ?? "?", color));
        }

        private static Bitmap DrawIcon(string label, Color color)
        {
            const int size = 24;
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Transparent background.
            g.Clear(Color.Transparent);

            // Rounded body.
            using (var path = RoundedRect(new RectangleF(1, 1, size - 2, size - 2), 5))
            using (var fill = new SolidBrush(color))
            using (var stroke = new Pen(DarkenColor(color, 0.45f), 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(stroke, path);
            }

            // Letter(s) or emoji.
            var info = new StringInfo(label);
            var textElements = info.LengthInTextElements;
            var emoji = IsEmoji(label);

            string text;
            float fontSize;

            if (emoji)
            {
                // Take the first emoji grapheme cluster.
                text = info.SubstringByTextElements(0, 1);
                fontSize = 14f;
            }
            else
            {
                text = textElements <= 2 ? label : info.SubstringByTextElements(0, 2);
                fontSize = textElements == 1 ? 12f : 9f;
            }

            using var font = emoji
                ? new Font("Segoe UI Emoji", fontSize, FontStyle.Regular, GraphicsUnit.Pixel)
                : new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.Black);
            using var format = new StringFormat();
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            g.DrawString(text, font, textBrush, new RectangleF(0, 0, size, size), format);

            return bmp;
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color ColorFromHex(string hex)
        {
            var s = hex.TrimStart('#');
            var r = System.Convert.ToInt32(s.Substring(0, 2), 16);
            var gg = System.Convert.ToInt32(s.Substring(2, 2), 16);
            var b = System.Convert.ToInt32(s.Substring(4, 2), 16);
            return Color.FromArgb(255, r, gg, b);
        }

        private static Color DarkenColor(Color c, float factor)
        {
            return Color.FromArgb(
                c.A,
                (int)(c.R * factor),
                (int)(c.G * factor),
                (int)(c.B * factor));
        }

        /// <summary>
        /// Returns true if the label starts with an emoji or symbol character
        /// (code point at or above U+2600 — Miscellaneous Symbols and beyond).
        /// </summary>
        private static bool IsEmoji(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var codePoint = char.ConvertToUtf32(s, 0);
            // Covers Misc Symbols (U+2600), Dingbats, Emoticons, Transport/Map,
            // Supplemental Symbols, and all surrogate-pair emoji (U+1F000+).
            return codePoint >= 0x2600;
        }
    }
}
