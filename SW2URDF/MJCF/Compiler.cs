using System.Xml;

namespace SW2URDF.MJCF
{
    // Emits the <compiler ...> element. The compiler tag governs how MuJoCo parses
    // the rest of the document. We default to settings that match how URDF is exported:
    // angles in radians (matching URDF), automatic limits, and balanced inertia.
    public class Compiler
    {
        public string MeshDir { get; set; } = "meshes/";

        // Path written into <compiler texturedir="..."> -- analogous to MeshDir.
        // Null/empty omits the attribute. MJCFBuilder sets this to "../textures/"
        // so it sits next to the model XML in the package layout.
        public string TextureDir { get; set; }

        public string Angle { get; set; } = "radian";
        public bool BalanceInertia { get; set; } = true;
        public bool AutoLimits { get; set; } = true;

        public void WriteMJCF(XmlWriter writer)
        {
            writer.WriteStartElement("compiler");
            writer.WriteAttributeString("meshdir", MeshDir);
            if (!string.IsNullOrEmpty(TextureDir))
            {
                writer.WriteAttributeString("texturedir", TextureDir);
            }
            writer.WriteAttributeString("angle", Angle);
            writer.WriteAttributeString("balanceinertia", BalanceInertia ? "true" : "false");
            writer.WriteAttributeString("autolimits", AutoLimits ? "true" : "false");
            writer.WriteEndElement();
        }
    }
}
