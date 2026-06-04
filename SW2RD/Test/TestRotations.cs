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

using SW2RD.Core;
using SW2RD.Utilities;

namespace SW2RD.Test
{
    // Test-only helpers for constructing canonical rotations. The canonical
    // KinematicTree stores rotation as a quaternion (PoseModel.Rotation), so
    // tests that want to express a pose in roll/pitch/yaw radians go through
    // here to get the matching QuaternionModel via the same MathOps conversion
    // the production adapter uses.
    internal static class TestRotations
    {
        // Roll/pitch/yaw radians -> canonical (w, x, y, z) quaternion.
        public static QuaternionModel Quat(double roll, double pitch, double yaw)
        {
            double[] q = MathOps.RPYToQuaternion(new[] { roll, pitch, yaw });
            return new QuaternionModel(q[0], q[1], q[2], q[3]);
        }
    }
}
