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
    // A site attached to a body, located by a position + quaternion expressed in
    // the body's local frame. Sites are used by MuJoCo for sensors, mounting points,
    // markers, etc.
    internal class Site
    {
        public string Name { get; set; }
        public double[] Position { get; set; } = new double[] { 0, 0, 0 };
        public double[] Quaternion { get; set; } = new double[] { 1, 0, 0, 0 };
        public double Size { get; set; } = 0.005;

        public void WriteMJCF(XmlWriter writer)
        {
            writer.WriteStartElement("site");
            if (!string.IsNullOrEmpty(Name))
            {
                writer.WriteAttributeString("name", Name);
            }
            writer.WriteAttributeString("pos", MJCFFormat.FormatTriple(Position));
            writer.WriteAttributeString("quat", MJCFFormat.FormatQuat(Quaternion));
            writer.WriteAttributeString("size", MJCFFormat.FormatDouble(Size));
            writer.WriteEndElement();
        }
    }
}
