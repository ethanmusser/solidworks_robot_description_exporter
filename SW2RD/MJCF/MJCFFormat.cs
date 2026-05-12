using System.Globalization;

namespace SW2RD.MJCF
{
    // Centralized number formatting for MJCF emission. Reuses URDF's en-US convention so
    // both writers produce identical decimal separators.
    internal static class MJCFFormat
    {
        public static readonly NumberFormatInfo Number =
            CultureInfo.CreateSpecificCulture("en-US").NumberFormat;

        public const string DefaultFormat = "G";

        public static string FormatDouble(double value)
        {
            return value.ToString(DefaultFormat, Number);
        }

        public static string FormatTriple(double[] xyz)
        {
            if (xyz == null || xyz.Length < 3)
            {
                return "0 0 0";
            }
            return FormatDouble(xyz[0]) + " " +
                   FormatDouble(xyz[1]) + " " +
                   FormatDouble(xyz[2]);
        }

        public static string FormatQuat(double[] wxyz)
        {
            if (wxyz == null || wxyz.Length < 4)
            {
                return "1 0 0 0";
            }
            return FormatDouble(wxyz[0]) + " " +
                   FormatDouble(wxyz[1]) + " " +
                   FormatDouble(wxyz[2]) + " " +
                   FormatDouble(wxyz[3]);
        }
    }
}
