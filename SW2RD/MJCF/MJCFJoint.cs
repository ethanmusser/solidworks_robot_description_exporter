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

using System.Diagnostics.CodeAnalysis;
using System.Xml;

namespace SW2RD.MJCF
{
    // The MJCF joint type. URDF revolute/continuous map to "hinge"; URDF prismatic
    // maps to "slide". URDF "fixed" is represented by omitting the joint element so
    // the body becomes rigid relative to its parent (MJCF default).
    internal enum MJCFJointType
    {
        None,
        Hinge,
        Slide,
        Free,
        Ball,
    }

    internal static class MJCFJointTypeExtensions
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
        //
        // CA1308 suggests ToUpperInvariant. We deliberately keep ToLowerInvariant
        // because the URDF specification defines joint type strings ("revolute",
        // "prismatic", "fixed", ...) in lowercase; matching against the lowercase
        // form keeps this code aligned with the spec it implements.
        [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
            Justification = "URDF joint-type strings are spec-defined as lowercase.")]
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
    internal class MJCFJoint
    {
        public string Name { get; set; }
        public MJCFJointType Type { get; set; } = MJCFJointType.Hinge;
        public double[] Axis { get; set; } = new double[] { 0, 0, 1 };
        public double[] Position { get; set; } = new double[] { 0, 0, 0 };

        // Unit for ANGULAR attributes (hinge range / ref). The data model stores
        // these in canonical RADIANS, so Radian emits them as-is and Degree
        // converts to degrees. Slide (prismatic) range / ref are lengths and are
        // never converted. Defaults to Degree (MuJoCo default).
        public MJCFAngleUnit AngleUnit { get; set; } = MJCFAngleUnit.Degree;

        public bool HasLimits { get; set; } = false;
        public double LowerLimit { get; set; }
        public double UpperLimit { get; set; }

        public bool HasDamping { get; set; } = false;
        public double Damping { get; set; }
        public bool HasFriction { get; set; } = false;
        public double Friction { get; set; }

        // MJCF armature (equivalent rotor inertia of the actuator). No
        // URDF analog; populated only on MJCF export when the user sets
        // the Joint Properties Armature textbox.
        public bool HasArmature { get; set; } = false;
        public double Armature { get; set; }

        // MJCF ref (joint position assumed by the model when MuJoCo
        // loads it). No URDF analog. 0 is a valid value distinct from
        // "unset", so this is gated on a separate flag.
        public bool HasRef { get; set; } = false;
        public double Ref { get; set; }

        // MJCF actuatorfrcrange = [-Effort, +Effort]. Mirrors URDF's
        // single-magnitude <limit effort>. Only emitted when the user
        // supplies a finite Effort on the Joint Properties UI.
        public bool HasEffort { get; set; } = false;
        public double Effort { get; set; }

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

            // Hinge range / ref are angular and honour the angle unit; slide
            // (prismatic) range / ref are lengths (meters) and are emitted raw
            // regardless of the angle unit, matching MuJoCo's interpretation.
            bool angular = Type == MJCFJointType.Hinge;
            if (HasLimits && Type != MJCFJointType.Free)
            {
                double lower = angular ? MJCFFormat.AngleFromRadians(LowerLimit, AngleUnit) : LowerLimit;
                double upper = angular ? MJCFFormat.AngleFromRadians(UpperLimit, AngleUnit) : UpperLimit;
                writer.WriteAttributeString(
                    "range",
                    MJCFFormat.FormatDouble(lower) + " " + MJCFFormat.FormatDouble(upper));
            }
            if (HasRef && Type != MJCFJointType.Free && Type != MJCFJointType.Ball)
            {
                double refValue = angular ? MJCFFormat.AngleFromRadians(Ref, AngleUnit) : Ref;
                writer.WriteAttributeString("ref", MJCFFormat.FormatDouble(refValue));
            }
            if (HasDamping)
            {
                writer.WriteAttributeString("damping", MJCFFormat.FormatDouble(Damping));
            }
            if (HasFriction)
            {
                writer.WriteAttributeString("frictionloss", MJCFFormat.FormatDouble(Friction));
            }
            if (HasArmature)
            {
                writer.WriteAttributeString("armature", MJCFFormat.FormatDouble(Armature));
            }
            if (HasEffort && Type != MJCFJointType.Free && Type != MJCFJointType.Ball)
            {
                // MJCF actuatorfrcrange is a symmetric range around zero
                // matching the URDF effort magnitude convention.
                writer.WriteAttributeString(
                    "actuatorfrcrange",
                    MJCFFormat.FormatDouble(-Effort) + " " + MJCFFormat.FormatDouble(Effort));
            }

            writer.WriteEndElement();
        }
    }
}
