// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Properties;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System.IO;

namespace AgOpenGPS.Classes
{
    // [XPLAT] Lazy-init holder of the on-screen HUD/overlay OpenGL textures (compass, speedo, steer
    // pointer, you-turn glyphs, zoom controls, headland icons, etc.). Behaviour is frozen: the 23
    // texture identities, their lazy "create on first access" semantics, and the backing Resources.*
    // images are unchanged, so the rendered overlays stay pixel-identical to the net48/WinForms build.
    // Only the image plumbing is re-platformed: the Core Texture2D now consumes a portable,
    // tightly-packed RGBA byte buffer (it no longer references the Windows-only System.Drawing.Bitmap),
    // and the Resources.* members now return cross-platform Avalonia bitmaps packaged as avares://
    // assets. LoadTexture() bridges those two contracts using the SkiaSharp normalization shared across
    // the migration (identical to Classes/VehicleTextures.cs), keeping AgOpenGPS.Core free of any
    // UI-framework dependency.
    public class ScreenTextures
    {
        private Texture2D _compass;
        private Texture2D _crossTrackBackGround;
        private Texture2D _font;
        private Texture2D _lateralManual;
        private Texture2D _lift;
        private Texture2D _menuShowHide;
        private Texture2D _noGps;
        private Texture2D _pan;
        private Texture2D _speedo;
        private Texture2D _speedoNeedle;
        private Texture2D _steerDot;
        private Texture2D _steerPointer;
        private Texture2D _tramDot;
        private Texture2D _turn;
        private Texture2D _turnCancel;
        private Texture2D _turnManuel;
        private Texture2D _uTurnU;
        private Texture2D _uTurnH;
        private Texture2D _questionMark;
        private Texture2D _zoomIn;
        private Texture2D _zoomOut;
        private Texture2D _headlandLight;
        private Texture2D _headlandDark;

        public ScreenTextures()
        {
        }

        public Texture2D Compass
        {
            get
            {
                if (_compass == null) _compass = LoadTexture(Resources.z_Compass);
                return _compass;
            }
        }

        public Texture2D CrossTrackBackground
        {
            get
            {
                if (_crossTrackBackGround == null) _crossTrackBackGround = LoadTexture(Resources.CrossTrackBackground);
                return _crossTrackBackGround;
            }
        }

        public Texture2D Font
        {
            get
            {
                if (_font == null) _font = LoadTexture(Resources.z_Font);
                return _font;
            }
        }

        public Texture2D LateralManual
        {
            get
            {
                if (_lateralManual == null) _lateralManual = LoadTexture(Resources.z_LateralManual);
                return _lateralManual;
            }
        }

        public Texture2D Lift
        {
            get
            {
                if (_lift == null) _lift = LoadTexture(Resources.z_Lift);
                return _lift;
            }
        }

        public Texture2D MenuShowHide
        {
            get
            {
                if (_menuShowHide == null) _menuShowHide = LoadTexture(Resources.MenuHideShow);
                return _menuShowHide;
            }
        }

        public Texture2D NoGps
        {
            get
            {
                if (_noGps == null) _noGps = LoadTexture(Resources.z_NoGPS);
                return _noGps;
            }
        }

        public Texture2D Pan
        {
            get
            {
                if (_pan == null) _pan = LoadTexture(Resources.Pan);
                return _pan;
            }
        }

        public Texture2D Speedo
        {
            get
            {
                if (_speedo == null) _speedo = LoadTexture(Resources.z_Speedo);
                return _speedo;
            }
        }

        public Texture2D SpeedoNeedle
        {
            get
            {
                if (_speedoNeedle == null) _speedoNeedle = LoadTexture(Resources.z_SpeedoNeedle);
                return _speedoNeedle;
            }
        }

        public Texture2D SteerDot
        {
            get
            {
                if (_steerDot == null) _steerDot = LoadTexture(Resources.z_SteerDot);
                return _steerDot;
            }
        }

        public Texture2D SteerPointer
        {
            get
            {
                if (_steerPointer == null) _steerPointer = LoadTexture(Resources.z_SteerPointer);
                return _steerPointer;
            }
        }

        public Texture2D TramDot
        {
            get
            {
                if (_tramDot == null) _tramDot = LoadTexture(Resources.z_TramOnOff);
                return _tramDot;
            }
        }

        public Texture2D Turn
        {
            get
            {
                if (_turn == null) _turn = LoadTexture(Resources.z_Turn);
                return _turn;
            }
        }

        public Texture2D TurnCancel
        {
            get
            {
                if (_turnCancel == null) _turnCancel = LoadTexture(Resources.z_TurnCancel);
                return _turnCancel;
            }
        }

        public Texture2D TurnManual
        {
            get
            {
                if (_turnManuel == null) _turnManuel = LoadTexture(Resources.z_TurnManual);
                return _turnManuel;
            }
        }

        public Texture2D UTurnU
        {
            get
            {
                if (_uTurnU == null) _uTurnU = LoadTexture(Resources.YouTurnU);
                return _uTurnU;
            }
        }

        public Texture2D UTurnH
        {
            get
            {
                if (_uTurnH == null) _uTurnH = LoadTexture(Resources.YouTurnH);
                return _uTurnH;
            }
        }

        public Texture2D QuestionMark
        {
            get
            {
                if (_questionMark == null) _questionMark = LoadTexture(Resources.z_QuestionMark);
                return _questionMark;
            }
        }

        public Texture2D ZoomIn
        {
            get
            {
                if (_zoomIn == null) _zoomIn = LoadTexture(Resources.ZoomIn48);
                return _zoomIn;
            }
        }

        public Texture2D ZoomOut
        {
            get
            {
                if (_zoomOut == null) _zoomOut = LoadTexture(Resources.ZoomOut48);
                return _zoomOut;
            }
        }

        public Texture2D HeadlandLight
        {
            get
            {
                if (_headlandLight == null) _headlandLight = LoadTexture(Resources.z_HeadlandLight);
                return _headlandLight;
            }
        }

        public Texture2D HeadlandDark
        {
            get
            {
                if (_headlandDark == null) _headlandDark = LoadTexture(Resources.z_HeadlandDark);
                return _headlandDark;
            }
        }

        // [XPLAT] Bridges a cross-platform Avalonia bitmap (as returned by Resources.*) to the portable
        // RGBA contract of the migrated Core Texture2D. The bitmap is re-encoded to PNG (lossless) and
        // decoded with SkiaSharp, then normalized to a tightly-packed RGBA8888, unpremultiplied byte
        // buffer (4 bytes/pixel, row-major) — the exact byte order the Texture2D.SetPixels GL upload path
        // expects (PixelFormat.Rgba). This mirrors the normalization in Classes/VehicleTextures.cs and
        // AgOpenGPS.Core.Streamers so texture colours stay pixel-identical to the net48 build, while
        // keeping AgOpenGPS.Core agnostic of any UI framework.
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
