// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.Drawing;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Core.Models;

namespace AgOpenGPS.Core.Visuals
{
    public class BingMapVisual
    {
        private BingMap _bingMap;
        private GeoTexture2D _bingMapTexture;

        public BingMapVisual(BingMap bingMap)
        {
            _bingMap = bingMap;
            // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
            // Build the GL texture from the portable, tightly-packed RGBA buffer + dimensions per the
            // cross-platform image contract. F-021 graceful skip: construct the texture only when the
            // online-imagery pixel buffer is present; otherwise leave _bingMapTexture null so Draw()
            // becomes a no-op and the field still renders without background imagery.
            if (bingMap?.RgbaPixels != null)
            {
                _bingMapTexture = new GeoTexture2D(bingMap.RgbaPixels, bingMap.Width, bingMap.Height);
            }
        }

        public void Draw()
        {
            if (_bingMap != null && _bingMapTexture != null)
            {
                GLW.SetColor(Colors.BingMapBackgroundColor);
                GeoCoord u0v0Map = new GeoCoord(_bingMap.GeoBoundingBox.MinEasting, _bingMap.GeoBoundingBox.MaxNorthing);
                GeoCoord u1v1Map = new GeoCoord(_bingMap.GeoBoundingBox.MaxEasting, _bingMap.GeoBoundingBox.MinNorthing);
                _bingMapTexture.DrawZ(u0v0Map, u1v1Map, -0.05);
            }
        }

    }

}
