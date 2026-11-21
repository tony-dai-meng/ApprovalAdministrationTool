using System.Drawing;
using System.Drawing.Text;
using System.Linq;

namespace ApprovalAdministrationTool
{
    /// <summary>
    /// Small icons drawn from the Windows icon font (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10),
    /// so they match the icons Windows itself uses and need no image files.
    /// </summary>
    internal static class SymbolIcons
    {
        /// <summary>The Windows "Refresh" symbol (circular arrow).</summary>
        public const char Refresh = '';

        /// <summary>The Windows "Switch" symbol (two opposite arrows).</summary>
        public const char Switch = '';

        /// <summary>The Windows "Filter" symbol (funnel).</summary>
        public const char Filter = '';

        /// <summary>The Windows "Search" symbol (magnifying glass).</summary>
        public const char Search = '';

        private static readonly string[] IconFonts = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };

        /// <summary>
        /// Draws <paramref name="glyph"/> as a <paramref name="size"/> x <paramref name="size"/> image,
        /// or returns null when neither icon font is installed (callers then fall back to text).
        /// </summary>
        public static Image Create(char glyph, int size, Color color)
        {
            string fontName;
            using (var installed = new InstalledFontCollection())
            {
                fontName = IconFonts.FirstOrDefault(name => installed.Families.Any(family => family.Name == name));
            }

            if (fontName == null)
            {
                return null;
            }

            var image = new Bitmap(size, size);
            using (var graphics = Graphics.FromImage(image))
            using (var font = new Font(fontName, size * 0.7f, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), format);
            }

            return image;
        }
    }
}
