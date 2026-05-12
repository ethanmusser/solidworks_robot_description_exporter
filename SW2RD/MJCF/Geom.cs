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
