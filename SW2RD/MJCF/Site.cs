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
