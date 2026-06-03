/*
Copyright (c) 2015 Stephen Brawner

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

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;

namespace SW2RD.Utilities
{
    public static class MathOps
    {
        public static double epsilon = 1e-15;

        public static T Max<T>(T d1, T d2, T d3) where T : IComparable<T>
        {
            return Max(new T[] { d1, d2, d3 });
        }

        public static T Max<T>(T[] array) where T : IComparable<T>
        {
            T result = default;
            if (array.Length > 0)
            {
                result = array[0];
                foreach (T t in array)
                {
                    result = Comparer<T>.Default.Compare(t, result) > 0 ? t : result;
                }
            }
            return result;
        }

        public static T Min<T>(T d1, T d2, T d3) where T : IComparable<T>
        {
            return Min(new T[] { d1, d2, d3 });
        }

        public static T Min<T>(T[] array) where T : IComparable<T>
        {
            T result = default;
            if (array.Length > 0)
            {
                result = array[0];
                foreach (T t in array)
                {
                    result = Comparer<T>.Default.Compare(t, result) < 0 ? t : result;
                }
            }
            return result;
        }

        public static T Envelope<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (Comparer<T>.Default.Compare(value, max) > 0)
            {
                return max;
            }
            else if (Comparer<T>.Default.Compare(value, min) < 0)
            {
                return min;
            }
            else
            {
                return value;
            }
        }

        public static double[] ClosestPointOnLineToPoint(double[] point, double[] line, double[] pointOnLine)
        {
            if (point.Length != line.Length || point.Length != pointOnLine.Length)
            {
                throw new Exception("Points and line vectors are not the same length");
            }

            double denominator = 0;
            double numerator = 0;
            for (int i = 0; i < point.Length; i++)
            {
                denominator += line[i] * line[i];
                numerator += line[i] * (point[i] - pointOnLine[i]);
            }
            double k = numerator / denominator;
            double[] result = new double[point.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = pointOnLine[i] + k * line[i];
            }
            return result;
        }

        public static double[] ClosestPointOnLineWithinBox(
            double xMin, double xMax, double yMin, double yMax, double zMin, double zMax,
            double[] line, double[] pointOnLine)
        {
            if (pointOnLine[0] > xMin &&
                pointOnLine[0] < xMax &&
                pointOnLine[1] > yMin &&
                pointOnLine[1] < yMax &&
                pointOnLine[2] > zMin &&
                pointOnLine[2] < zMax)
            {
                return pointOnLine;
            }
            double[] point1 =
                ClosestPointOnLineToPoint(new double[] { xMax, yMax, zMax }, line, pointOnLine);
            double[] point2 =
                ClosestPointOnLineToPoint(new double[] { xMin, yMin, zMin }, line, pointOnLine);

            if (Distance2(pointOnLine, point1) < Distance2(pointOnLine, point2))
            {
                return point1;
            }
            else
            {
                return point2;
            }
        }

       public static double[] GetXYZ(Matrix<double> m)
        {
            double[] XYZ = new double[3];
            XYZ[0] = m[0, 3]; XYZ[1] = m[1, 3]; XYZ[2] = m[2, 3];
            return XYZ;
        }

        public static double[] GetXYZ(MathTransform transform)
        {
            double[] XYZ = new double[3];
            XYZ[0] = transform.ArrayData[9];
            XYZ[1] = transform.ArrayData[10];
            XYZ[2] = transform.ArrayData[11];
            return XYZ;
        }

        public static double[] GetRPY(Matrix<double> m)
        {
            double roll, pitch, yaw;
            if (Math.Abs(m[2, 0]) >= 1.0)
            {
                // Gimbal Lock
                pitch = -Math.Asin(Math.Sign(m[2, 0]) * 1.0);
                roll = Math.Atan2(-m[1, 2], m[1, 1]);
                yaw = 0;
            }
            else
            {
                pitch = -Math.Asin(m[2, 0]);
                roll = Math.Atan2(m[2, 1], m[2, 2]);
                yaw = Math.Atan2(m[1, 0], m[0, 0]);
            }

            return new double[] { roll, pitch, yaw };
        }

        public static double[] GetRPY(MathTransform transform)
        {
            Matrix m = GetRotationMatrix(transform);
            return GetRPY(m);
        }

        public static Matrix<double> GetRotation(double[] RPY)
        {
            Matrix<double> RX = DenseMatrix.CreateIdentity(4);
            Matrix<double> RY = DenseMatrix.CreateIdentity(4);
            Matrix<double> RZ = DenseMatrix.CreateIdentity(4);

            RX[1, 1] = Math.Cos(RPY[0]);
            RX[1, 2] = -Math.Sin(RPY[0]);
            RX[2, 1] = Math.Sin(RPY[0]);
            RX[2, 2] = Math.Cos(RPY[0]);

            RY[0, 0] = Math.Cos(RPY[1]);
            RY[0, 2] = Math.Sin(RPY[1]);
            RY[2, 0] = -Math.Sin(RPY[1]);
            RY[2, 2] = Math.Cos(RPY[1]);

            RZ[0, 0] = Math.Cos(RPY[2]);
            RZ[0, 1] = -Math.Sin(RPY[2]);
            RZ[1, 0] = Math.Sin(RPY[2]);
            RZ[1, 1] = Math.Cos(RPY[2]);

            return RZ * RY * RX;
        }

        public static Matrix<double> GetTranslation(double[] XYZ)
        {
            Matrix<double> m = DenseMatrix.CreateIdentity(4);
            m[0, 3] = XYZ[0]; m[1, 3] = XYZ[1]; m[2, 3] = XYZ[2];
            return m;
        }

        public static Matrix<double> GetTransformation(double[] XYZ, double[] RPY)
        {
            Matrix<double> translation = GetTranslation(XYZ);
            Matrix<double> rotation = GetRotation(RPY);
            return translation * rotation;
        }

        public static Matrix GetRotationMatrix(MathTransform transform)
        {
            Matrix rot = new DenseMatrix(3);
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    rot.At(i, j, transform.ArrayData[i + 3 * j]);
                }
            }

            return rot;
        }

        public static Matrix<double> GetTransformation(MathTransform transform)
        {
            Matrix<double> m = new DenseMatrix(4);

            m[0, 0] = transform.ArrayData[0];
            m[1, 0] = transform.ArrayData[1];
            m[2, 0] = transform.ArrayData[2];
            m[0, 1] = transform.ArrayData[3];
            m[1, 1] = transform.ArrayData[4];
            m[2, 1] = transform.ArrayData[5];
            m[0, 2] = transform.ArrayData[6];
            m[1, 2] = transform.ArrayData[7];
            m[2, 2] = transform.ArrayData[8];

            m[0, 3] = transform.ArrayData[9];
            m[1, 3] = transform.ArrayData[10];
            m[2, 3] = transform.ArrayData[11];
            m[3, 3] = transform.ArrayData[12];
            return m;
        }

        public static double[] PNorm(double[] array, double power)
        {
            double magnitude = 0;
            for (int i = 0; i < array.Length; i++)
            {
                magnitude += Math.Pow(array[i], power);
            }
            if (magnitude != 0)
            {
                magnitude = Math.Pow(magnitude, 1 / power);
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] /= magnitude;
                }
            }
            return array;
        }

        public static double Distance2(double[] array1, double[] array2)
        {
            double sqrdmag = 0;
            for (int i = 0; i < array1.Length; i++)
            {
                double d = array1[i] - array2[i];
                sqrdmag += d * d;
            }
            return sqrdmag;
        }

        public static double[] Threshold(double[] array, double minValue)
        {
            double[] result = (double[])array.Clone();
            for (int i = 0; i < array.Length; i++)
            {
                result[i] = (Math.Abs(array[i]) >= minValue) ? array[i] : 0;
            }
            return result;
        }

        // Converts URDF roll-pitch-yaw (intrinsic XYZ Tait-Bryan angles, R = Rz * Ry * Rx)
        // to a unit quaternion in the MJCF (w, x, y, z) order.
        public static double[] RPYToQuaternion(double[] rpy)
        {
            if (rpy == null || rpy.Length < 3)
            {
                return new double[] { 1, 0, 0, 0 };
            }
            double cr = Math.Cos(rpy[0] * 0.5);
            double sr = Math.Sin(rpy[0] * 0.5);
            double cp = Math.Cos(rpy[1] * 0.5);
            double sp = Math.Sin(rpy[1] * 0.5);
            double cy = Math.Cos(rpy[2] * 0.5);
            double sy = Math.Sin(rpy[2] * 0.5);

            double w = cr * cp * cy + sr * sp * sy;
            double x = sr * cp * cy - cr * sp * sy;
            double y = cr * sp * cy + sr * cp * sy;
            double z = cr * cp * sy - sr * sp * cy;

            // Convention in MuJoCo: keep w >= 0 to canonicalize.
            if (w < 0)
            {
                w = -w; x = -x; y = -y; z = -z;
            }
            return new double[] { w, x, y, z };
        }

        // Convenience: convert a 4x4 homogeneous transform's rotation portion to a
        // (w, x, y, z) quaternion. Equivalent to GetRPY then RPYToQuaternion but
        // less roundabout.
        public static double[] RotationMatrixToQuaternion(Matrix<double> m)
        {
            double r00 = m[0, 0], r01 = m[0, 1], r02 = m[0, 2];
            double r10 = m[1, 0], r11 = m[1, 1], r12 = m[1, 2];
            double r20 = m[2, 0], r21 = m[2, 1], r22 = m[2, 2];

            double trace = r00 + r11 + r22;
            double w, x, y, z;
            if (trace > 0)
            {
                double s = Math.Sqrt(trace + 1.0) * 2;
                w = 0.25 * s;
                x = (r21 - r12) / s;
                y = (r02 - r20) / s;
                z = (r10 - r01) / s;
            }
            else if (r00 > r11 && r00 > r22)
            {
                double s = Math.Sqrt(1.0 + r00 - r11 - r22) * 2;
                w = (r21 - r12) / s;
                x = 0.25 * s;
                y = (r01 + r10) / s;
                z = (r02 + r20) / s;
            }
            else if (r11 > r22)
            {
                double s = Math.Sqrt(1.0 + r11 - r00 - r22) * 2;
                w = (r02 - r20) / s;
                x = (r01 + r10) / s;
                y = 0.25 * s;
                z = (r12 + r21) / s;
            }
            else
            {
                double s = Math.Sqrt(1.0 + r22 - r00 - r11) * 2;
                w = (r10 - r01) / s;
                x = (r02 + r20) / s;
                y = (r12 + r21) / s;
                z = 0.25 * s;
            }

            if (w < 0)
            {
                w = -w; x = -x; y = -y; z = -z;
            }
            return new double[] { w, x, y, z };
        }

        // Converts a (w, x, y, z) quaternion to MJCF axisangle form:
        // { x, y, z, angle } where angle is in RADIANS (the writer converts
        // to degrees) and (x, y, z) is the unit rotation axis. For the
        // identity / near-identity rotation the axis is undefined, so we
        // return (0, 0, 1) with angle 0 to keep the vector well-defined.
        public static double[] QuaternionToAxisAngle(double[] wxyz)
        {
            if (wxyz == null || wxyz.Length < 4)
            {
                return new double[] { 0, 0, 1, 0 };
            }
            double w = wxyz[0], x = wxyz[1], y = wxyz[2], z = wxyz[3];

            // Normalize defensively; the inputs are already unit quaternions
            // in normal operation but a stray denorm would yield a bogus axis.
            double norm = Math.Sqrt(w * w + x * x + y * y + z * z);
            if (norm < epsilon)
            {
                return new double[] { 0, 0, 1, 0 };
            }
            w /= norm; x /= norm; y /= norm; z /= norm;

            // Clamp w into [-1, 1] so Acos never returns NaN on rounding.
            w = Envelope(w, -1.0, 1.0);
            double angle = 2.0 * Math.Acos(w);
            double sinHalf = Math.Sqrt(1.0 - w * w);
            if (sinHalf < epsilon)
            {
                // Angle is ~0 (or ~2pi); axis is undefined. Use +Z, angle 0.
                return new double[] { 0, 0, 1, 0 };
            }
            return new double[] { x / sinHalf, y / sinHalf, z / sinHalf, angle };
        }

        // Converts a (w, x, y, z) quaternion to extrinsic XYZ roll-pitch-yaw
        // (radians), the SAME convention as GetRPY / GetRotation and URDF's
        // rpy. Builds the rotation matrix from the quaternion and defers to
        // GetRPY so the two paths share one definition of the angle sequence.
        public static double[] QuaternionToRPY(double[] wxyz)
        {
            if (wxyz == null || wxyz.Length < 4)
            {
                return new double[] { 0, 0, 0 };
            }
            double w = wxyz[0], x = wxyz[1], y = wxyz[2], z = wxyz[3];
            double norm = Math.Sqrt(w * w + x * x + y * y + z * z);
            if (norm < epsilon)
            {
                return new double[] { 0, 0, 0 };
            }
            w /= norm; x /= norm; y /= norm; z /= norm;

            Matrix<double> m = new DenseMatrix(3);
            m[0, 0] = 1 - 2 * (y * y + z * z);
            m[0, 1] = 2 * (x * y - w * z);
            m[0, 2] = 2 * (x * z + w * y);
            m[1, 0] = 2 * (x * y + w * z);
            m[1, 1] = 1 - 2 * (x * x + z * z);
            m[1, 2] = 2 * (y * z - w * x);
            m[2, 0] = 2 * (x * z - w * y);
            m[2, 1] = 2 * (y * z + w * x);
            m[2, 2] = 1 - 2 * (x * x + y * y);
            return GetRPY(m);
        }
    }
}