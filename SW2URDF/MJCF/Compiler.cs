using System.Xml;

namespace SW2URDF.MJCF
{
    // Emits the <compiler ...> element. The compiler tag governs how MuJoCo parses
    // the rest of the document. We default to settings that match how URDF is exported:
    // angles in radians (matching URDF), automatic limits, and balanced inertia.
    public class Compiler
    {
        public string MeshDir { get; set; } = "meshes/";
        public string Angle { get; set; } = "radian";
        public bool BalanceInertia { get; set; } = true;
        public bool AutoLimits { get; set; } = true;

        public void WriteMJCF(XmlWriter writer)
        {
            writer.WriteStartElement("compiler");
            writer.WriteAttributeString("meshdir", MeshDir);
            writer.WriteAttributeString("angle", Angle);
            writer.WriteAttributeString("balanceinertia", BalanceInertia ? "true" : "false");
            writer.WriteAttributeString("autolimits", AutoLimits ? "true" : "false");
            writer.WriteEndElement();
        }
    }
}
