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

using System.Xml;

namespace SW2RD.MJCF
{
    // The MJCF inertial element. The frame is the body's local frame, so the
    // center-of-mass is expressed relative to the parent body's origin.
    internal class MJCFInertial
    {
        public double[] Position { get; set; } = new double[] { 0, 0, 0 };
        public double Mass { get; set; }

        // Full inertia tensor in body frame: [ixx, iyy, izz, ixy, ixz, iyz].
        public double[] FullInertia { get; set; } = new double[] { 0, 0, 0, 0, 0, 0 };

        public bool HasInertia { get; set; } = false;

        public void WriteMJCF(XmlWriter writer)
        {
            if (!HasInertia)
            {
                return;
            }
            writer.WriteStartElement("inertial");
            writer.WriteAttributeString("pos", MJCFFormat.FormatTriple(Position));
            writer.WriteAttributeString("mass", MJCFFormat.FormatDouble(Mass));
            writer.WriteAttributeString(
                "fullinertia",
                MJCFFormat.FormatDouble(FullInertia[0]) + " " +
                MJCFFormat.FormatDouble(FullInertia[1]) + " " +
                MJCFFormat.FormatDouble(FullInertia[2]) + " " +
                MJCFFormat.FormatDouble(FullInertia[3]) + " " +
                MJCFFormat.FormatDouble(FullInertia[4]) + " " +
                MJCFFormat.FormatDouble(FullInertia[5]));
            writer.WriteEndElement();
        }
    }
}
