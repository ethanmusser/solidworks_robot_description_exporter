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

using SW2RD.Utilities;
using System;
using System.Globalization;
using System.Xml;

namespace SW2RD.MJCF
{
    // Centralized number formatting for MJCF emission. Reuses URDF's en-US convention so
    // both writers produce identical decimal separators.
    internal static class MJCFFormat
    {
        private const double RadiansToDegrees = 180.0 / Math.PI;
        private const double DegreesToRadians = Math.PI / 180.0;

        // Converts an angle stored internally in RADIANS to the chosen MJCF
        // output unit. Used for orientation angles (axisangle / euler), whose
        // source is the canonical radian quaternion.
        public static double AngleFromRadians(double radians, MJCFAngleUnit unit)
        {
            return unit == MJCFAngleUnit.Radian ? radians : radians * RadiansToDegrees;
        }

        // Converts an angle stored internally in DEGREES to the chosen MJCF
        // output unit. Used for hinge-joint range / ref, which the data model
        // carries in degrees (the Joint Properties UI convention).
        public static double AngleFromDegrees(double degrees, MJCFAngleUnit unit)
        {
            return unit == MJCFAngleUnit.Radian ? degrees * DegreesToRadians : degrees;
        }

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

        // Formats a (w, x, y, z) quaternion as MJCF axisangle="x y z a", with
        // the angle in the chosen unit (degrees unless <compiler angle="radian">
        // is written for the model).
        public static string FormatAxisAngle(double[] wxyz, MJCFAngleUnit unit)
        {
            double[] aa = MathOps.QuaternionToAxisAngle(wxyz);
            return FormatDouble(aa[0]) + " " +
                   FormatDouble(aa[1]) + " " +
                   FormatDouble(aa[2]) + " " +
                   FormatDouble(AngleFromRadians(aa[3], unit));
        }

        // Formats a (w, x, y, z) quaternion as MJCF euler="r p y" in the chosen
        // unit. The angle sequence (extrinsic XYZ = URDF rpy) is selected via the
        // <compiler eulerseq="XYZ"> attribute the builder emits for this mode.
        public static string FormatEuler(double[] wxyz, MJCFAngleUnit unit)
        {
            double[] rpy = MathOps.QuaternionToRPY(wxyz);
            return FormatDouble(AngleFromRadians(rpy[0], unit)) + " " +
                   FormatDouble(AngleFromRadians(rpy[1], unit)) + " " +
                   FormatDouble(AngleFromRadians(rpy[2], unit));
        }

        // Writes the frame-orientation attribute for the given quaternion using
        // the user-selected representation and angle unit. All three forms are
        // mutually exclusive in MJCF and normalize internally to the same
        // quaternion, so this is purely a readability choice. The angle unit is
        // irrelevant for quaternions (unitless) but applies to axisangle / euler.
        // Shared by <body> and <site> emission.
        public static void WriteOrientation(
            XmlWriter writer, double[] quat, MJCFRotationFormat format, MJCFAngleUnit unit)
        {
            switch (format)
            {
                case MJCFRotationFormat.AxisAngle:
                    writer.WriteAttributeString("axisangle", FormatAxisAngle(quat, unit));
                    break;
                case MJCFRotationFormat.Euler:
                    writer.WriteAttributeString("euler", FormatEuler(quat, unit));
                    break;
                default:
                    writer.WriteAttributeString("quat", FormatQuat(quat));
                    break;
            }
        }
    }
}
