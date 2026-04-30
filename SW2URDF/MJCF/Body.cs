using System.Collections.Generic;
using System.Xml;

namespace SW2URDF.MJCF
{
    // Recursive MJCF body element. The root body acts as the worldbody contents
    // (its name is conventionally the URDF base_link name) but is written inside
    // <worldbody> by MJCFModel.
    public class Body
    {
        public string Name { get; set; }
        public double[] Position { get; set; } = new double[] { 0, 0, 0 };
        public double[] Quaternion { get; set; } = new double[] { 1, 0, 0, 0 };

        public MJCFInertial Inertial { get; set; }
        public MJCFJoint Joint { get; set; } // null for the root body or a fixed joint
        public List<Geom> Geoms { get; }
        public List<Site> Sites { get; }
        public List<Body> Children { get; }

        // If true, suppress pos/quat attributes on this <body>. This is what we do
        // for the worldbody-level emission of the root link, where the transform is
        // implicit (worldbody origin == base link origin).
        public bool SuppressTransform { get; set; } = false;

        public Body()
        {
            Geoms = new List<Geom>();
            Sites = new List<Site>();
            Children = new List<Body>();
        }

        public void WriteMJCF(XmlWriter writer)
        {
            writer.WriteStartElement("body");
            if (!string.IsNullOrEmpty(Name))
            {
                writer.WriteAttributeString("name", Name);
            }
            if (!SuppressTransform)
            {
                writer.WriteAttributeString("pos", MJCFFormat.FormatTriple(Position));
                writer.WriteAttributeString("quat", MJCFFormat.FormatQuat(Quaternion));
            }

            // Order: inertial, joint, geoms, sites, child bodies. This mirrors the
            // canonical MuJoCo example layout and keeps the diff against URDF easy to
            // follow.
            if (Inertial != null)
            {
                Inertial.WriteMJCF(writer);
            }
            if (Joint != null)
            {
                Joint.WriteMJCF(writer);
            }
            foreach (Geom geom in Geoms)
            {
                geom.WriteMJCF(writer);
            }
            foreach (Site site in Sites)
            {
                site.WriteMJCF(writer);
            }
            foreach (Body child in Children)
            {
                child.WriteMJCF(writer);
            }

            writer.WriteEndElement();
        }
    }
}
