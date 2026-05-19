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
    // The role a <geom> plays for a body. We declare matching <default class="..."> blocks
    // at the top of the model so visual geoms get rendered but not collided with, and
    // collision geoms participate in physics.
    internal enum GeomRole
    {
        Visual,
        Collision,
    }

    // A simple <geom type="mesh" mesh="..." class="..."/>. Meshes are STL-exported in
    // body-local coordinates (the exporter uses the body's coordinate system as the
    // STL save origin), so the geom never needs an explicit pos/quat.
    internal class Geom
    {
        public string Name { get; set; }
        public string MeshName { get; set; }
        public GeomRole Role { get; set; }
        public string Material { get; set; }
        public double[] Rgba { get; set; } // optional rgba override

        public Geom(string name, string meshName, GeomRole role)
        {
            Name = name;
            MeshName = meshName;
            Role = role;
        }

        public string ClassName
        {
            get { return Role == GeomRole.Visual ? "visual" : "collision"; }
        }

        public void WriteMJCF(XmlWriter writer)
        {
            writer.WriteStartElement("geom");
            if (!string.IsNullOrEmpty(Name))
            {
                writer.WriteAttributeString("name", Name);
            }
            writer.WriteAttributeString("type", "mesh");
            writer.WriteAttributeString("mesh", MeshName);
            writer.WriteAttributeString("class", ClassName);
            if (!string.IsNullOrEmpty(Material))
            {
                writer.WriteAttributeString("material", Material);
            }
            if (Rgba != null && Rgba.Length == 4)
            {
                writer.WriteAttributeString(
                    "rgba",
                    MJCFFormat.FormatDouble(Rgba[0]) + " " +
                    MJCFFormat.FormatDouble(Rgba[1]) + " " +
                    MJCFFormat.FormatDouble(Rgba[2]) + " " +
                    MJCFFormat.FormatDouble(Rgba[3]));
            }
            writer.WriteEndElement();
        }
    }
}
