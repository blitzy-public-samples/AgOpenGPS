// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Properties;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System.IO;

namespace AgOpenGPS.Classes
{
    // [XPLAT] Lazy-init holder of the vehicle OpenGL textures. Behaviour is frozen: the seven texture
    // identities, their lazy "create on first access" semantics, and the backing Resources.* images are
    // unchanged, so the rendered vehicle stays pixel-identical to the net48/WinForms build. Only the
    // image plumbing is re-platformed: the Core Texture2D now consumes a portable, tightly-packed RGBA
    // byte buffer (it no longer references the Windows-only System.Drawing.Bitmap), and the Resources.*
    // members now return cross-platform Avalonia bitmaps packaged as avares:// assets. LoadTexture()
    // bridges those two contracts using the SkiaSharp normalization shared across the migration (see
    // AgOpenGPS.Core.Streamers.BingMapStreamer), keeping AgOpenGPS.Core free of any UI-framework dependency.
    public class VehicleTextures
    {
        private Texture2D _tractor;
        private Texture2D _harvester;
        private Texture2D _articulatedFront;
        private Texture2D _articulatedRear;

        private Texture2D _frontWheel;
        private Texture2D _tire;
        private Texture2D _toolAxle;

        public VehicleTextures()
        {
        }

        public Texture2D Tractor
        {
            get
            {
                // [XPLAT] Null pixels => deferred/empty texture (no GL upload), behaviourally identical to
                // the former new Texture2D(null) placeholder; width/height are ignored while pixels are
                // null. The trailing (0, 0) only satisfies the migrated 3-argument Texture2D constructor.
                if (_tractor == null) _tractor = new Texture2D(null, 0, 0);
                return _tractor;
            }
        }

        public Texture2D Harvester
        {
            get
            {
                if (_harvester == null) _harvester = new Texture2D(null, 0, 0);
                return _harvester;
            }
        }

        public Texture2D ArticulatedFront
        {
            get
            {
                if (_articulatedFront == null) _articulatedFront = new Texture2D(null, 0, 0);
                return _articulatedFront;
            }
        }

        public Texture2D ArticulatedRear
        {
            get
            {
                if (_articulatedRear == null) _articulatedRear = new Texture2D(null, 0, 0);
                return _articulatedRear;
            }
        }

        public Texture2D FrontWheel
        {
            get
            {
                if (_frontWheel == null) _frontWheel = LoadTexture(Resources.z_FrontWheels);
                return _frontWheel;
            }
        }

        public Texture2D Tire
        {
            get
            {
                if (_tire == null) _tire = LoadTexture(Resources.z_Tire);
                return _tire;
            }
        }

        public Texture2D ToolAxle
        {
            get
            {
                if (_toolAxle == null) _toolAxle = LoadTexture(Resources.z_Tool);
                return _toolAxle;
            }
        }

        // [XPLAT] Bridges a cross-platform Avalonia bitmap (as returned by Resources.*) to the portable
        // RGBA contract of the migrated Core Texture2D. The bitmap is re-encoded to PNG (lossless) and
        // decoded with SkiaSharp, then normalized to a tightly-packed RGBA8888, unpremultiplied byte
        // buffer (4 bytes/pixel, row-major) — the exact byte order the Texture2D.SetPixels GL upload path
        // expects (PixelFormat.Rgba). This mirrors the normalization in
        // AgOpenGPS.Core.Streamers.BingMapStreamer so texture colours stay pixel-identical to the net48
        // build, while keeping AgOpenGPS.Core agnostic of any UI framework.
        private static Texture2D LoadTexture(Bitmap image)
        {
            using (MemoryStream png = new MemoryStream())
            {
                image.Save(png);
                png.Position = 0;
                using (SKBitmap decoded = SKBitmap.Decode(png))
                {
                    SKImageInfo info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    using (SKBitmap rgba = new SKBitmap(info))
                    {
                        using (SKCanvas canvas = new SKCanvas(rgba))
                        {
                            canvas.Clear(SKColors.Transparent);
                            canvas.DrawBitmap(decoded, 0, 0);
                        }
                        // rgba.Bytes returns a fresh managed copy, so it remains valid after the SKBitmap
                        // is disposed at the end of this using block.
                        return new Texture2D(rgba.Bytes, info.Width, info.Height);
                    }
                }
            }
        }

    }
}
