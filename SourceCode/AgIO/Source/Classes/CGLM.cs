// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;

namespace AgIO
{
    // [XPLAT] CS8981 suppressed: the all-lowercase type name 'glm' is preserved deliberately — it mirrors the
    // OpenGL-Mathematics (glm) C++ library naming used throughout AgOpenGPS and is referenced by name at many
    // behavior-frozen call sites, so renaming is out of CP2 scope. The suppression keeps the in-scope AgIO
    // project building under Release (TreatWarningsAsErrors). See MIGRATION_DOCS/TRANSITION_MAP.md.
#pragma warning disable CS8981
    public static class glm
    {
        //Regex file expression
        public const string fileRegex = "(^(PRN|AUX|NUL|CON|COM[1-9]|LPT[1-9]|(\\.+)$)(\\..*)?$)|(([\\x00-\\x1f\\\\?*:\";‌​|/<>])+)|([\\.]+)";

        //Degrees Radians Conversions
        public static double toDegrees(double radians)
        {
            return radians * 57.295779513082325225835265587528;
        }

        public static double toRadians(double degrees)
        {
            return degrees * 0.01745329251994329576923690768489;
        }

        //Distance calcs of all kinds
        public static double DistanceLonLat(double lon1, double lat1, double lon2, double lat2)
        {
            const int EarthMeanRadius = 6371;

            double dlon = toRadians(lon2 - lon1);
            double dlat = toRadians(lat2 - lat1);

            double a = (Math.Sin(dlat / 2) * Math.Sin(dlat / 2)) + Math.Cos(toRadians(lat1)) * Math.Cos(toRadians(lat2)) * (Math.Sin(dlon / 2) * Math.Sin(dlon / 2));
            double angle = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return angle * EarthMeanRadius;
        }

    }
#pragma warning restore CS8981
}