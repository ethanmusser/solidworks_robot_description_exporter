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

using SW2RD.Core;
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;

namespace SW2RD.Input
{
    // Records-native URDF writer. Consumes the format-neutral KinematicTree
    // (SW2RD.Core) directly - the same canonical model MJCFBuilder consumes -
    // and emits a single-robot URDF document. All angular quantities on the
    // tree are canonical RADIANS (the KinematicTreeAdapter converts the legacy
    // degree-basis edit model at the ToCore boundary), and URDF expresses
    // angles in radians, so this writer emits scalar joint values and rpy
    // angles as-is with no unit conversion.
    //
    // URDF describes a single robot in isolation, so the multi-tree
    // KinematicTree is reduced to its first top-level body here (with advisory
    // warnings for any dropped bodies, a floating base, or world-level
    // geometry that URDF cannot represent). MJCF, by contrast, emits every
    // top-level body and world geometry.
    internal static class URDFBuilder
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        // Mirrors URDFAttribute's number formatting so output is
        // byte-compatible with the retired embedded writer.
        private static readonly NumberFormatInfo Number = URDFNumberFormat();

        private static NumberFormatInfo URDFNumberFormat()
        {
            return CultureInfo.CreateSpecificCulture("en-US").NumberFormat;
        }

        public static void Write(KinematicTree tree, XmlWriter writer)
        {
            if (tree == null)
            {
                throw new ArgumentNullException(nameof(tree));
            }
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            LinkModel baseLink = SelectBaseLink(tree);

            writer.WriteStartDocument();
            WriteHeaderComment(writer);

            writer.WriteStartElement("robot");
            writer.WriteAttributeString("name", tree.Name ?? "");
            WriteLink(writer, baseLink);
            writer.WriteEndElement(); // robot

            writer.WriteEndDocument();
        }

        // Reduces the multi-tree, world-aware KinematicTree down to the single
        // body URDF can describe. Warns (advisory only) on the three URDF
        // degradation cases the legacy KinematicTreeAdapter.ToLegacyRobot used
        // to surface.
        private static LinkModel SelectBaseLink(KinematicTree tree)
        {
            IReadOnlyList<LinkModel> topLevels = tree.TopLevelBodies ?? Array.Empty<LinkModel>();
            if (topLevels.Count == 0)
            {
                throw new InvalidOperationException(
                    "KinematicTree has no top-level bodies; cannot write a URDF <robot>.");
            }

            LinkModel chosen = topLevels[0];
            if (topLevels.Count > 1)
            {
                List<string> dropped = new List<string>();
                for (int i = 1; i < topLevels.Count; i++)
                {
                    dropped.Add(topLevels[i]?.Name ?? "<null>");
                }
                logger.Warn("URDF: model has " + topLevels.Count + " top-level bodies; URDF describes a single " +
                    "robot in isolation, so only '" + (chosen.Name ?? "") +
                    "' will be written as <robot>'s base_link. Dropped: " +
                    string.Join(", ", dropped) + ". Use MJCF or pair with an external SDFormat/.world file " +
                    "if you need multiple bodies.");
            }

            if (chosen.WorldAttachment == WorldAttachmentModel.Free)
            {
                logger.Warn("URDF: top-level body '" + (chosen.Name ?? "") +
                    "' has WorldAttachment=Free; URDF cannot express a floating base in a way most loaders " +
                    "honor. Emitting a fixed-base URDF instead.");
            }

            if (LinkHasGeometry(tree.WorldBody))
            {
                logger.Warn("URDF: world-level visual/collision/site geometry is dropped on URDF export. " +
                    "URDF describes the robot only; pair with an SDFormat .world file or use MJCF if you need " +
                    "scene geometry.");
            }

            return chosen;
        }

        private static bool LinkHasGeometry(LinkModel link)
        {
            if (link == null)
            {
                return false;
            }
            return (link.VisualGroups != null && link.VisualGroups.Count > 0)
                || (link.CollisionGroups != null && link.CollisionGroups.Count > 0)
                || (link.Sites != null && link.Sites.Count > 0);
        }

        private static void WriteHeaderComment(XmlWriter writer)
        {
            string buildVersion = Versioning.Version.GetBuildVersion();
            string commitVersion = Versioning.Version.GetCommitVersion();
            writer.WriteComment(
                " This URDF was automatically created by the SolidWorks Robot Description Exporter (SW2RD). " +
                "Originally created by Stephen Brawner (brawner@gmail.com) as the SolidWorks to URDF Exporter. \r\n" +
                string.Format(CultureInfo.InvariantCulture,
                    "     Commit Version: {0}  Build Version: {1}\r\n", commitVersion, buildVersion) +
                "     For more information, please see https://github.com/ethanmusser/solidworks_robot_description_exporter ");
        }

        private static void WriteLink(XmlWriter writer, LinkModel link)
        {
            writer.WriteStartElement("link");
            writer.WriteAttributeString("name", link.Name ?? "");

            WriteInertial(writer, link.Inertial);
            WriteVisuals(writer, link);
            WriteCollisions(writer, link);

            writer.WriteEndElement(); // link

            // The joint connecting this link to its parent is emitted as a
            // sibling of <link>, after the link closes (matching the legacy
            // writer's element order). The root has no incoming joint.
            if (HasJointData(link.Joint))
            {
                WriteJoint(writer, link.Joint);
            }

            if (link.Children != null)
            {
                foreach (LinkModel child in link.Children)
                {
                    if (child != null)
                    {
                        WriteLink(writer, child);
                    }
                }
            }
        }

        private static void WriteInertial(XmlWriter writer, InertialModel inertial)
        {
            if (inertial == null)
            {
                return;
            }
            writer.WriteStartElement("inertial");
            WriteOrigin(writer, inertial.Origin);
            writer.WriteStartElement("mass");
            writer.WriteAttributeString("value", FormatDouble(inertial.Mass));
            writer.WriteEndElement();

            InertiaTensorModel t = inertial.Inertia ?? new InertiaTensorModel(0, 0, 0, 0, 0, 0);
            writer.WriteStartElement("inertia");
            writer.WriteAttributeString("ixx", FormatDouble(t.Ixx));
            writer.WriteAttributeString("ixy", FormatDouble(t.Ixy));
            writer.WriteAttributeString("ixz", FormatDouble(t.Ixz));
            writer.WriteAttributeString("iyy", FormatDouble(t.Iyy));
            writer.WriteAttributeString("iyz", FormatDouble(t.Iyz));
            writer.WriteAttributeString("izz", FormatDouble(t.Izz));
            writer.WriteEndElement();
            writer.WriteEndElement(); // inertial
        }

        private static void WriteVisuals(XmlWriter writer, LinkModel link)
        {
            if (link.VisualGroups == null)
            {
                return;
            }
            foreach (MeshGroupModel group in link.VisualGroups)
            {
                if (group == null)
                {
                    continue;
                }
                writer.WriteStartElement("visual");
                WriteIdentityOrigin(writer);
                WriteGeometry(writer, group.MeshFilename);
                WriteMaterial(writer, link.Material);
                writer.WriteEndElement(); // visual
            }
        }

        // Collision emission mirrors the legacy ProcessLinkMeshes fallback:
        // when the link has explicit collision groups (and is not reusing
        // visual), each group emits a <collision>; otherwise a single
        // <collision> reuses the first visual mesh.
        private static void WriteCollisions(XmlWriter writer, LinkModel link)
        {
            bool hasExplicitCollision = !link.CollisionUsesVisual
                && link.CollisionGroups != null && link.CollisionGroups.Count > 0;
            if (hasExplicitCollision)
            {
                foreach (MeshGroupModel group in link.CollisionGroups)
                {
                    if (group != null)
                    {
                        WriteCollision(writer, group.MeshFilename);
                    }
                }
            }
            else if (link.VisualGroups != null && link.VisualGroups.Count > 0
                && link.VisualGroups[0] != null)
            {
                WriteCollision(writer, link.VisualGroups[0].MeshFilename);
            }
        }

        private static void WriteCollision(XmlWriter writer, string meshFilename)
        {
            writer.WriteStartElement("collision");
            WriteIdentityOrigin(writer);
            WriteGeometry(writer, meshFilename);
            writer.WriteEndElement(); // collision
        }

        private static void WriteGeometry(XmlWriter writer, string meshFilename)
        {
            writer.WriteStartElement("geometry");
            writer.WriteStartElement("mesh");
            writer.WriteAttributeString("filename", meshFilename ?? "");
            writer.WriteEndElement(); // mesh
            writer.WriteEndElement(); // geometry
        }

        private static void WriteMaterial(XmlWriter writer, MaterialModel material)
        {
            MaterialModel m = material ?? new MaterialModel("", new RgbaModel(1, 1, 1, 1), "");
            writer.WriteStartElement("material");
            writer.WriteAttributeString("name", m.Name ?? "");

            RgbaModel c = m.Color ?? new RgbaModel(1, 1, 1, 1);
            writer.WriteStartElement("color");
            writer.WriteAttributeString("rgba", FormatQuad(c.Red, c.Green, c.Blue, c.Alpha));
            writer.WriteEndElement(); // color

            // Texture export is dormant in normal operation (TextureFilename
            // is empty); emit a <texture> only when a filename is configured.
            if (!string.IsNullOrWhiteSpace(m.TextureFilename))
            {
                writer.WriteStartElement("texture");
                writer.WriteAttributeString("filename", m.TextureFilename);
                writer.WriteEndElement(); // texture
            }
            writer.WriteEndElement(); // material
        }

        private static void WriteJoint(XmlWriter writer, JointModel joint)
        {
            string type = ResolveUrdfJointType(joint);

            writer.WriteStartElement("joint");
            writer.WriteAttributeString("name", joint.Name ?? "");
            writer.WriteAttributeString("type", type);

            WriteOrigin(writer, joint.Origin);

            writer.WriteStartElement("parent");
            writer.WriteAttributeString("link", joint.ParentLinkName ?? "");
            writer.WriteEndElement();

            writer.WriteStartElement("child");
            writer.WriteAttributeString("link", joint.ChildLinkName ?? "");
            writer.WriteEndElement();

            Vector3Model axis = joint.Axis ?? new Vector3Model(0, 0, 0);
            writer.WriteStartElement("axis");
            writer.WriteAttributeString("xyz", FormatTriple(axis.X, axis.Y, axis.Z));
            writer.WriteEndElement();

            WriteLimit(writer, joint.Limit);
            WriteDynamics(writer, joint);

            writer.WriteEndElement(); // joint
        }

        // URDF defines a revolute joint with no range as "continuous". The
        // legacy writer performed this demotion at emit time; replicate it so
        // a revolute joint that never had limits configured still serializes
        // as continuous (and therefore needs no <limit>).
        private static string ResolveUrdfJointType(JointModel joint)
        {
            string type = joint.Type ?? "";
            if (string.Equals(type, "revolute", StringComparison.Ordinal))
            {
                bool hasRange = joint.Limit != null
                    && (joint.Limit.Lower.HasValue || joint.Limit.Upper.HasValue);
                if (!hasRange)
                {
                    return "continuous";
                }
            }
            return type;
        }

        private static void WriteLimit(XmlWriter writer, JointLimitModel limit)
        {
            if (limit == null)
            {
                return;
            }
            if (!limit.Lower.HasValue && !limit.Upper.HasValue
                && !limit.Effort.HasValue && !limit.Velocity.HasValue)
            {
                return;
            }
            writer.WriteStartElement("limit");
            if (limit.Lower.HasValue)
            {
                writer.WriteAttributeString("lower", FormatDouble(limit.Lower.Value));
            }
            if (limit.Upper.HasValue)
            {
                writer.WriteAttributeString("upper", FormatDouble(limit.Upper.Value));
            }
            if (limit.Effort.HasValue)
            {
                writer.WriteAttributeString("effort", FormatDouble(limit.Effort.Value));
            }
            if (limit.Velocity.HasValue)
            {
                writer.WriteAttributeString("velocity", FormatDouble(limit.Velocity.Value));
            }
            writer.WriteEndElement(); // limit
        }

        private static void WriteDynamics(XmlWriter writer, JointModel joint)
        {
            if (!joint.Damping.HasValue && !joint.Friction.HasValue)
            {
                return;
            }
            writer.WriteStartElement("dynamics");
            if (joint.Damping.HasValue)
            {
                writer.WriteAttributeString("damping", FormatDouble(joint.Damping.Value));
            }
            if (joint.Friction.HasValue)
            {
                writer.WriteAttributeString("friction", FormatDouble(joint.Friction.Value));
            }
            writer.WriteEndElement(); // dynamics
        }

        private static void WriteOrigin(XmlWriter writer, PoseModel pose)
        {
            Vector3Model pos = pose?.Position ?? new Vector3Model(0, 0, 0);
            QuaternionModel rot = pose?.Rotation ?? QuaternionModel.Identity;
            double[] rpy = MathOps.QuaternionToRPY(new[] { rot.W, rot.X, rot.Y, rot.Z });
            writer.WriteStartElement("origin");
            writer.WriteAttributeString("xyz", FormatTriple(pos.X, pos.Y, pos.Z));
            writer.WriteAttributeString("rpy", FormatTriple(rpy[0], rpy[1], rpy[2]));
            writer.WriteEndElement();
        }

        private static void WriteIdentityOrigin(XmlWriter writer)
        {
            writer.WriteStartElement("origin");
            writer.WriteAttributeString("xyz", "0 0 0");
            writer.WriteAttributeString("rpy", "0 0 0");
            writer.WriteEndElement();
        }

        private static bool HasJointData(JointModel joint)
        {
            return joint != null
                && !string.IsNullOrWhiteSpace(joint.Name)
                && !string.IsNullOrWhiteSpace(joint.Type);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString(Number);
        }

        private static string FormatTriple(double a, double b, double c)
        {
            return FormatDouble(a) + " " + FormatDouble(b) + " " + FormatDouble(c);
        }

        private static string FormatQuad(double a, double b, double c, double d)
        {
            return FormatDouble(a) + " " + FormatDouble(b) + " " + FormatDouble(c) + " " + FormatDouble(d);
        }
    }
}
