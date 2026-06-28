// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md

namespace AgOpenGPS.Core.Models
{
    public class BingMap
    {
        // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
        // The background-imagery pixels are now held as a portable, tightly-packed RGBA byte
        // buffer (4 bytes/pixel, row-major) with explicit dimensions, replacing the Windows-only
        // GDI+ raster image type. The BackPic.png wire format is unchanged — BingMapStreamer
        // decodes/encodes it with a cross-platform PNG codec — so the field-file PNG contract is preserved.
        public BingMap(
            GeoBoundingBox geoBoundingBox,
            byte[] rgbaPixels,
            int width,
            int height)
        {
            GeoBoundingBox = geoBoundingBox;
            RgbaPixels = rgbaPixels;
            Width = width;
            Height = height;
        }

        public GeoBoundingBox GeoBoundingBox { get; }

        // Tightly-packed RGBA pixels (4 bytes/pixel, row-major), or null when no imagery is present.
        public byte[] RgbaPixels { get; }
        public int Width { get; }
        public int Height { get; }

    }
}
