using System.Globalization;

namespace SW2RD.Input
{
    // Number parsing / formatting helpers shared by the plain-C# input model
    // classes (Origin, Axis, Limit, Dynamics, Mass, Inertia, Color, ...).
    // The invariant culture (en-US) keeps the PMPage textbox round-trip
    // locale-independent.
    internal static class InvariantNumberFormat
    {
        public static readonly NumberFormatInfo NumberFormat =
            CultureInfo.CreateSpecificCulture("en-US").NumberFormat;

        public static readonly NumberStyles NumberStyle = NumberStyles.Any;

        // Formats an optional scalar for display; null -> "" (the empty textbox
        // state, matching the "unset attribute" rendering).
        public static string Format(double? value, string format = "G")
        {
            return value.HasValue ? value.Value.ToString(format, NumberFormat) : "";
        }

        public static string[] FormatArray(double[] values, string format = "G")
        {
            if (values == null)
            {
                return null;
            }
            string[] result = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = values[i].ToString(format, NumberFormat);
            }
            return result;
        }

        public static bool TryParse(string text, out double value)
        {
            return double.TryParse(text, NumberStyle, NumberFormat, out value);
        }

        // Parses a textbox array, substituting 0 for any entry that does not
        // parse (the required double[] attribute behavior).
        public static double[] ParseArray(string[] texts)
        {
            double[] result = new double[texts.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                result[i] = TryParse(texts[i], out double parsed) ? parsed : 0.0;
            }
            return result;
        }
    }
}
