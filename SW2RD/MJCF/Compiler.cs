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
    // Emits the <compiler ...> element. By default we do not write an `angle`
    // attribute, so MuJoCo's default degree units apply to angular XML attributes
    // such as joint range/ref and orientation axisangle/euler. When the user
    // selects radian output, Angle is set to Radian and we emit
    // `angle="radian"`; the builder converts the angular quantities to match.
    // The Link/Joint tab data model stores those values in degrees.
    //
    // Named MJCFCompiler rather than Compiler so the unqualified type does not
    // collide with the System.CodeDom.Compiler namespace (CA1724).
    internal class MJCFCompiler
    {
        public string MeshDir { get; set; } = "meshes/";

        // Angular unit for the whole model. Degree is MuJoCo's default and emits
        // no attribute; Radian emits angle="radian". Set by MJCFBuilder.
        public MJCFAngleUnit Angle { get; set; } = MJCFAngleUnit.Degree;

        // Path written into <compiler texturedir="..."> -- analogous to MeshDir.
        // Null/empty omits the attribute. MJCFBuilder sets this only when the
        // model declares texture assets.
        public string TextureDir { get; set; }

        // Sequence written into <compiler eulerseq="..."> -- selects the axis
        // order for every euler attribute in the model. Null/empty omits the
        // attribute (MuJoCo defaults to intrinsic "xyz"). MJCFBuilder sets this
        // to "XYZ" (extrinsic = URDF roll-pitch-yaw) only when frame
        // orientations are emitted as euler angles.
        public string EulerSeq { get; set; }

        public void WriteMJCF(XmlWriter writer)
        {
            writer.WriteStartElement("compiler");
            writer.WriteAttributeString("meshdir", MeshDir);
            if (Angle == MJCFAngleUnit.Radian)
            {
                writer.WriteAttributeString("angle", "radian");
            }
            if (!string.IsNullOrEmpty(TextureDir))
            {
                writer.WriteAttributeString("texturedir", TextureDir);
            }
            if (!string.IsNullOrEmpty(EulerSeq))
            {
                writer.WriteAttributeString("eulerseq", EulerSeq);
            }
            writer.WriteEndElement();
        }
    }
}
