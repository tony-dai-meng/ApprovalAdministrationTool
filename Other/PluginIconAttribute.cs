using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ApprovalAdministrationTool
{
    /// <summary>
    /// Supplies XrmToolBox's "SmallImageBase64" (32x32) and "BigImageBase64" (80x80) plugin metadata from one PNG
    /// embedded in this assembly, so the icon is an image file in the repo instead of long base64 strings in code.
    /// </summary>
    /// <remarks>
    /// XrmToolBox discovers tools with MEF, and MEF exposes the public properties of any [MetadataAttribute] on an export
    /// as metadata, so these properties stand in for ExportMetadata("SmallImageBase64", ...) / ExportMetadata("BigImageBase64", ...).
    /// The image can be any size or aspect ratio: its transparent margin is trimmed, it is centred on a square and
    /// resized. Add it to the project as an EmbeddedResource and pass its file name.
    /// </remarks>
    [MetadataAttribute]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class PluginIconAttribute : Attribute
    {
        private readonly string imageFile;

        /// <param name="imageFile">File name of the embedded PNG, e.g. "ThumbsUp.png".</param>
        public PluginIconAttribute(string imageFile)
        {
            this.imageFile = imageFile;
        }

        public string SmallImageBase64 => CreateIconBase64(imageFile, 32);

        public string BigImageBase64 => CreateIconBase64(imageFile, 80);

        /// <returns>A size x size PNG as base64, or null if the resource is missing (XrmToolBox then shows its default icon).</returns>
        private static string CreateIconBase64(string fileName, int size)
        {
            var assembly = typeof(PluginIconAttribute).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                return null;
            }

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var source = new Bitmap(stream))
            using (var icon = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                var content = GetVisibleBounds(source);
                var scale = (float)size / Math.Max(content.Width, content.Height);
                var width = (int)Math.Round(content.Width * scale);
                var height = (int)Math.Round(content.Height * scale);
                var target = new Rectangle((size - width) / 2, (size - height) / 2, width, height);

                using (var graphics = Graphics.FromImage(icon))
                using (var attributes = new ImageAttributes())
                {
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY); // avoids a dark fringe along the edges when downscaling
                    graphics.DrawImage(source, target, content.X, content.Y, content.Width, content.Height, GraphicsUnit.Pixel, attributes);
                }

                using (var png = new MemoryStream())
                {
                    icon.Save(png, ImageFormat.Png);
                    return Convert.ToBase64String(png.ToArray());
                }
            }
        }

        /// <summary>Smallest rectangle containing all pixels that are not (almost) fully transparent.</summary>
        private static Rectangle GetVisibleBounds(Bitmap image)
        {
            var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pixels = new byte[data.Stride * image.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            image.UnlockBits(data);

            int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;
            for (var y = 0; y < image.Height; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < image.Width; x++)
                {
                    if (pixels[row + x * 4 + 3] > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            return maxX < 0
                ? new Rectangle(0, 0, image.Width, image.Height)
                : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }
    }
}
