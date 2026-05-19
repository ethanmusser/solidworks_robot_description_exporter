/*
Copyright (c) 2026 Ethan J. Musser

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

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
