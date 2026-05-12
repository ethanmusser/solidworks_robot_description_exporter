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
