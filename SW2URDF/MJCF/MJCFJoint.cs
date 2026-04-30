using System.Xml;

namespace SW2URDF.MJCF
{
    // The MJCF joint type. URDF revolute/continuous map to "hinge"; URDF prismatic
    // maps to "slide". URDF "fixed" is represented by omitting the joint element so
    // the body becomes rigid relative to its parent (MJCF default).
    public enum MJCFJointType
    {
        None,
        Hinge,
        Slide,
        Free,
        Ball,
    }

    public static class MJCFJointTypeExtensions
    {
        public static string ToMJCFString(this MJCFJointType type)
        {
            switch (type)
            {
                case MJCFJointType.Hinge: return "hinge";
                case MJCFJointType.Slide: return "slide";
                case MJCFJointType.Free: return "free";
                case MJCFJointType.Ball: return "ball";
                default: return "hinge";
            }
        }

        // Maps a URDF joint type string (as emitted by the existing exporter) to its
        // MJCF equivalent. Returns false for "fixed" (which has no MJCF analogue and
        // is represented by omitting the <joint> tag).
        public static bool TryFromURDFType(string urdfType, out MJCFJointType result)
        {
            result = MJCFJointType.Hinge;
            if (string.IsNullOrEmpty(urdfType))
            {
                return false;
            }
            string normalized = urdfType.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "revolute":
                case "continuous":
                    result = MJCFJointType.Hinge;
                    return true;
                case "prismatic":
                    result = MJCFJointType.Slide;
                    return true;
                case "floating":
                    result = MJCFJointType.Free;
                    return true;
                case "ball":
                    result = MJCFJointType.Ball;
                    return true;
                case "fixed":
                default:
                    return false;
            }
        }
    }

    // A joint inside an MJCF body. The body's pos/quat already places the joint at
    // the right location, so MJCF joints always sit at the body origin (pos = 0).
    public class MJCFJoint
    {
        public string Name { get; set; }
        public MJCFJointType Type { get; set; } = MJCFJointType.Hinge;
        public double[] Axis { get; set; } = new double[] { 0, 0, 1 };
        public double[] Position { get; set; } = new double[] { 0, 0, 0 };

        public bool HasLimits { get; set; } = false;
        public double LowerLimit { get; set; }
        public double UpperLimit { get; set; }

        public bool HasDamping { get; set; } = false;
        public double Damping { get; set; }
        public bool HasFriction { get; set; } = false;
        public double Friction { get; set; }

        public void WriteMJCF(XmlWriter writer)
        {
            // "Free" joints don't take an axis and only need a name; they let the body
            // float in the world. We still emit position 0 0 0 implicitly.
            writer.WriteStartElement("joint");
            if (!string.IsNullOrEmpty(Name))
            {
                writer.WriteAttributeString("name", Name);
            }
            writer.WriteAttributeString("type", Type.ToMJCFString());

            if (Type != MJCFJointType.Free && Type != MJCFJointType.Ball)
            {
                writer.WriteAttributeString("axis", MJCFFormat.FormatTriple(Axis));
            }
            writer.WriteAttributeString("pos", MJCFFormat.FormatTriple(Position));

            if (HasLimits && Type != MJCFJointType.Free)
            {
                writer.WriteAttributeString(
                    "range",
                    MJCFFormat.FormatDouble(LowerLimit) + " " + MJCFFormat.FormatDouble(UpperLimit));
            }
            if (HasDamping)
            {
                writer.WriteAttributeString("damping", MJCFFormat.FormatDouble(Damping));
            }
            if (HasFriction)
            {
                writer.WriteAttributeString("frictionloss", MJCFFormat.FormatDouble(Friction));
            }

            writer.WriteEndElement();
        }
    }
}
