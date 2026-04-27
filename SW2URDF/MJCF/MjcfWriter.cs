/*
Copyright (c) 2015 Stephen Brawner

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

using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace SW2URDF.MJCF
{
    // Writes a MuJoCo MJCF (.xml) file from the same URDF Robot tree the rest of the exporter
    // already builds. The translator is deliberately decoupled from SolidWorks: anything that
    // needs the CAD runtime (coord-system pose resolution, mesh export) is prepared upstream by
    // ExportHelper and fed in through plain data structures. Keep this file free of SolidWorks
    // interop references so it remains easy to unit-test.
    public static class MjcfWriter
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        /// <summary>
        /// Serializes the given Robot tree to MJCF XML at <paramref name="savePath"/>.
        /// </summary>
        /// <param name="robot">Robot tree built by the URDF flow.</param>
        /// <param name="options">User-selected MJCF knobs.</param>
        /// <param name="savePath">Full path to the target .xml file.</param>
        /// <param name="linkSites">
        /// Optional map from link name to pre-resolved sites (pose already expressed in the link
        /// frame). Missing entries mean the link has no sites.
        /// </param>
        /// <param name="linkMeshFilenames">
        /// Optional map from link name to the basename of its mesh file relative to
        /// <c>options.MeshDir</c>. Missing entries fall back to "<c>{linkName}.STL</c>".
        /// </param>
        public static void Write(
            Robot robot,
            MjcfOptions options,
            string savePath,
            IDictionary<string, List<MjcfSite>> linkSites = null,
            IDictionary<string, string> linkMeshFilenames = null)
        {
            if (robot == null)
            {
                throw new ArgumentNullException(nameof(robot));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineOnAttributes = false,
            };

            using (XmlWriter writer = XmlWriter.Create(savePath, settings))
            {
                WriteTo(robot, options, writer, linkSites, linkMeshFilenames);
            }
        }

        /// <summary>
        /// Same as <see cref="Write(Robot, MjcfOptions, string, IDictionary{string, List{MjcfSite}}, IDictionary{string, string})"/>
        /// but writes into a caller-supplied <see cref="XmlWriter"/>. Primarily for tests.
        /// </summary>
        public static void WriteTo(
            Robot robot,
            MjcfOptions options,
            XmlWriter writer,
            IDictionary<string, List<MjcfSite>> linkSites = null,
            IDictionary<string, string> linkMeshFilenames = null)
        {
            linkSites = linkSites ?? new Dictionary<string, List<MjcfSite>>();
            linkMeshFilenames = linkMeshFilenames ?? new Dictionary<string, string>();

            writer.WriteStartDocument();
            writer.WriteComment(
                " This MJCF was automatically created by the SolidWorks to URDF/MJCF Exporter. " +
                "Originally created by Stephen Brawner (brawner@gmail.com).\r\n" +
                string.Format(
                    "     Commit Version: {0}  Build Version: {1}\r\n",
                    Versioning.Version.GetCommitVersion(),
                    Versioning.Version.GetBuildVersion()) +
                "     For MJCF reference, see https://mujoco.readthedocs.io/en/stable/XMLreference.html ");

            writer.WriteStartElement("mujoco");
            writer.WriteAttributeString("model", string.IsNullOrWhiteSpace(robot.Name) ? "robot" : robot.Name);

            WriteCompiler(writer, options);
            WriteOption(writer, options);
            WriteAsset(writer, robot.BaseLink, linkMeshFilenames);

            writer.WriteStartElement("worldbody");
            WriteBody(writer, robot.BaseLink, options, linkSites, linkMeshFilenames, isRoot: true);
            writer.WriteEndElement(); // worldbody

            if (options.EmitMimicEqualities)
            {
                WriteEqualities(writer, robot.BaseLink);
            }

            if (options.ExcludeAdjacentContacts)
            {
                WriteContacts(writer, robot.BaseLink);
            }

            if (options.ActuatorType != MjcfActuatorType.None)
            {
                WriteActuators(writer, robot.BaseLink, options);
            }

            writer.WriteEndElement(); // mujoco
            writer.WriteEndDocument();
        }

        #region Header sections

        private static void WriteCompiler(XmlWriter writer, MjcfOptions options)
        {
            writer.WriteStartElement("compiler");
            writer.WriteAttributeString("angle", "radian");
            if (!string.IsNullOrWhiteSpace(options.MeshDir))
            {
                writer.WriteAttributeString("meshdir", options.MeshDir);
            }
            writer.WriteAttributeString("autolimits", "true");
            writer.WriteEndElement();
        }

        private static void WriteOption(XmlWriter writer, MjcfOptions options)
        {
            writer.WriteStartElement("option");
            writer.WriteAttributeString("timestep", FormatDouble(options.Timestep));
            writer.WriteAttributeString("integrator", options.IntegratorToMjcf());
            if (options.Gravity != null && options.Gravity.Length == 3)
            {
                writer.WriteAttributeString("gravity", FormatVec3(options.Gravity));
            }
            writer.WriteEndElement();
        }

        private static void WriteAsset(
            XmlWriter writer,
            Link baseLink,
            IDictionary<string, string> linkMeshFilenames)
        {
            List<Link> meshLinks = new List<Link>();
            CollectMeshLinks(baseLink, meshLinks);
            if (meshLinks.Count == 0)
            {
                return;
            }

            writer.WriteStartElement("asset");
            HashSet<string> written = new HashSet<string>();
            foreach (Link link in meshLinks)
            {
                string meshName = SanitizeName(link.Name);
                if (!written.Add(meshName))
                {
                    continue;
                }

                writer.WriteStartElement("mesh");
                writer.WriteAttributeString("name", meshName);
                writer.WriteAttributeString("file", ResolveMeshFileName(link, linkMeshFilenames));
                writer.WriteEndElement();
            }
            writer.WriteEndElement(); // asset
        }

        private static void CollectMeshLinks(Link link, List<Link> results)
        {
            if (!link.isFixedFrame && HasMesh(link))
            {
                results.Add(link);
            }
            foreach (Link child in link.Children)
            {
                CollectMeshLinks(child, results);
            }
        }

        private static bool HasMesh(Link link)
        {
            return link.Visual != null
                && link.Visual.Geometry != null
                && link.Visual.Geometry.Mesh != null;
        }

        private static string ResolveMeshFileName(Link link, IDictionary<string, string> linkMeshFilenames)
        {
            if (linkMeshFilenames.TryGetValue(link.Name, out string overrideName)
                && !string.IsNullOrWhiteSpace(overrideName))
            {
                return overrideName;
            }
            string filename = link.Visual?.Geometry?.Mesh?.Filename;
            if (!string.IsNullOrWhiteSpace(filename))
            {
                // Filename may be a package:// URL or a Windows path; extract the basename so the
                // compiler resolves it relative to <compiler meshdir="...">.
                string cleaned = filename.Replace('\\', '/');
                int slash = cleaned.LastIndexOf('/');
                if (slash >= 0 && slash < cleaned.Length - 1)
                {
                    return cleaned.Substring(slash + 1);
                }
                return cleaned;
            }
            return SanitizeName(link.Name) + ".STL";
        }

        #endregion Header sections

        #region Body tree

        private static void WriteBody(
            XmlWriter writer,
            Link link,
            MjcfOptions options,
            IDictionary<string, List<MjcfSite>> linkSites,
            IDictionary<string, string> linkMeshFilenames,
            bool isRoot)
        {
            writer.WriteStartElement("body");
            writer.WriteAttributeString("name", SanitizeName(link.Name));

            if (!isRoot && link.Joint != null && link.Joint.Origin != null)
            {
                double[] xyz = link.Joint.Origin.GetXYZ() ?? new double[] { 0, 0, 0 };
                double[] rpy = link.Joint.Origin.GetRPY() ?? new double[] { 0, 0, 0 };
                if (!IsZero(xyz))
                {
                    writer.WriteAttributeString("pos", FormatVec3(xyz));
                }
                if (!IsZero(rpy))
                {
                    writer.WriteAttributeString("quat", FormatQuat(RpyToQuat(rpy)));
                }
            }

            if (!isRoot && link.Joint != null)
            {
                WriteJoint(writer, link.Joint);
            }
            else if (isRoot && link.Joint != null && link.Joint.Type == "floating")
            {
                // Floating base: MuJoCo requires a single free joint at the root.
                writer.WriteStartElement("joint");
                writer.WriteAttributeString("name", SanitizeName(link.Joint.Name));
                writer.WriteAttributeString("type", "free");
                writer.WriteEndElement();
            }

            if (!link.isFixedFrame)
            {
                WriteInertial(writer, link);
                WriteGeoms(writer, link, linkMeshFilenames);
            }

            WriteSites(writer, link, linkSites);

            foreach (Link child in link.Children)
            {
                WriteBody(writer, child, options, linkSites, linkMeshFilenames, isRoot: false);
            }

            writer.WriteEndElement(); // body
        }

        private static void WriteJoint(XmlWriter writer, Joint joint)
        {
            string type = joint.Type ?? "fixed";
            if (type == "fixed")
            {
                // MJCF expresses "fixed" by omitting the <joint>; the nested <body> is rigidly
                // attached to its parent.
                return;
            }

            switch (type)
            {
                case "revolute":
                case "continuous":
                    WriteScalarJoint(writer, joint, "hinge", withRange: (type == "revolute"));
                    break;
                case "prismatic":
                    WriteScalarJoint(writer, joint, "slide", withRange: true);
                    break;
                case "floating":
                    writer.WriteStartElement("joint");
                    writer.WriteAttributeString("name", SanitizeName(joint.Name));
                    writer.WriteAttributeString("type", "free");
                    writer.WriteEndElement();
                    break;
                case "planar":
                    // Approximation: emit two orthogonal slide joints in the plane normal to the
                    // URDF joint axis. MuJoCo has no single planar primitive.
                    WritePlanarJoint(writer, joint);
                    break;
                default:
                    logger.Warn("Unhandled joint type " + type + " for joint " + joint.Name
                        + "; writing as a hinge.");
                    WriteScalarJoint(writer, joint, "hinge", withRange: false);
                    break;
            }
        }

        private static void WriteScalarJoint(XmlWriter writer, Joint joint, string mjcfType, bool withRange)
        {
            writer.WriteStartElement("joint");
            writer.WriteAttributeString("name", SanitizeName(joint.Name));
            writer.WriteAttributeString("type", mjcfType);

            if (joint.Axis != null)
            {
                double[] axis = joint.Axis.GetXYZ();
                if (axis != null && axis.Length == 3 && !IsZero(axis))
                {
                    writer.WriteAttributeString("axis", FormatVec3(axis));
                }
            }

            if (withRange && joint.Limit != null && joint.Limit.AreRequiredFieldsSatisfied())
            {
                writer.WriteAttributeString(
                    "range",
                    FormatDouble(joint.Limit.Lower) + " " + FormatDouble(joint.Limit.Upper));
            }

            if (joint.Dynamics != null)
            {
                if (HasValue(joint.Dynamics, "Damping"))
                {
                    writer.WriteAttributeString("damping", FormatDouble(joint.Dynamics.Damping));
                }
                if (HasValue(joint.Dynamics, "Friction"))
                {
                    writer.WriteAttributeString("frictionloss", FormatDouble(joint.Dynamics.Friction));
                }
            }

            writer.WriteEndElement();
        }

        private static void WritePlanarJoint(XmlWriter writer, Joint joint)
        {
            double[] axis = joint.Axis?.GetXYZ() ?? new double[] { 0, 0, 1 };
            double[] u;
            double[] v;
            OrthogonalBasis(axis, out u, out v);

            writer.WriteStartElement("joint");
            writer.WriteAttributeString("name", SanitizeName(joint.Name) + "_x");
            writer.WriteAttributeString("type", "slide");
            writer.WriteAttributeString("axis", FormatVec3(u));
            writer.WriteEndElement();

            writer.WriteStartElement("joint");
            writer.WriteAttributeString("name", SanitizeName(joint.Name) + "_y");
            writer.WriteAttributeString("type", "slide");
            writer.WriteAttributeString("axis", FormatVec3(v));
            writer.WriteEndElement();
        }

        private static void WriteInertial(XmlWriter writer, Link link)
        {
            if (link.Inertial == null)
            {
                return;
            }
            Mass mass = link.Inertial.Mass;
            Inertia inertia = link.Inertial.Inertia;
            Origin origin = link.Inertial.Origin;

            if (mass == null || inertia == null)
            {
                return;
            }

            // Only emit if at least mass is non-zero; an all-zero inertial would make MuJoCo reject
            // the model.
            double massValue = GetDoubleOrZero(mass, "Value");
            if (massValue <= 0)
            {
                return;
            }

            writer.WriteStartElement("inertial");

            double[] xyz = origin?.GetXYZ() ?? new double[] { 0, 0, 0 };
            double[] rpy = origin?.GetRPY() ?? new double[] { 0, 0, 0 };

            writer.WriteAttributeString("pos", FormatVec3(xyz));
            if (!IsZero(rpy))
            {
                writer.WriteAttributeString("quat", FormatQuat(RpyToQuat(rpy)));
            }

            writer.WriteAttributeString("mass", FormatDouble(massValue));

            double ixx = GetDoubleOrZero(inertia, "Ixx");
            double iyy = GetDoubleOrZero(inertia, "Iyy");
            double izz = GetDoubleOrZero(inertia, "Izz");
            double ixy = GetDoubleOrZero(inertia, "Ixy");
            double ixz = GetDoubleOrZero(inertia, "Ixz");
            double iyz = GetDoubleOrZero(inertia, "Iyz");

            // MJCF fullinertia order is (M00, M11, M22, M01, M02, M12) = (ixx, iyy, izz, ixy, ixz, iyz).
            writer.WriteAttributeString(
                "fullinertia",
                string.Join(" ", new[]
                {
                    FormatDouble(ixx),
                    FormatDouble(iyy),
                    FormatDouble(izz),
                    FormatDouble(ixy),
                    FormatDouble(ixz),
                    FormatDouble(iyz),
                }));

            writer.WriteEndElement();
        }

        private static void WriteGeoms(
            XmlWriter writer,
            Link link,
            IDictionary<string, string> linkMeshFilenames)
        {
            string meshName = SanitizeName(link.Name);

            // Visual geom: render-only, not part of contact.
            if (link.Visual != null && HasMesh(link))
            {
                writer.WriteStartElement("geom");
                writer.WriteAttributeString("name", meshName + "_visual");
                writer.WriteAttributeString("type", "mesh");
                writer.WriteAttributeString("mesh", meshName);
                writer.WriteAttributeString("group", "1");
                writer.WriteAttributeString("contype", "0");
                writer.WriteAttributeString("conaffinity", "0");
                double[] xyz = link.Visual.Origin?.GetXYZ();
                double[] rpy = link.Visual.Origin?.GetRPY();
                if (xyz != null && !IsZero(xyz))
                {
                    writer.WriteAttributeString("pos", FormatVec3(xyz));
                }
                if (rpy != null && !IsZero(rpy))
                {
                    writer.WriteAttributeString("quat", FormatQuat(RpyToQuat(rpy)));
                }
                WriteVisualColor(writer, link);
                writer.WriteEndElement();
            }

            // Collision geom: takes part in contact, hidden from normal rendering.
            if (link.Collision != null && HasMesh(link))
            {
                writer.WriteStartElement("geom");
                writer.WriteAttributeString("name", meshName + "_collision");
                writer.WriteAttributeString("type", "mesh");
                writer.WriteAttributeString("mesh", meshName);
                writer.WriteAttributeString("group", "3");
                double[] xyz = link.Collision.Origin?.GetXYZ();
                double[] rpy = link.Collision.Origin?.GetRPY();
                if (xyz != null && !IsZero(xyz))
                {
                    writer.WriteAttributeString("pos", FormatVec3(xyz));
                }
                if (rpy != null && !IsZero(rpy))
                {
                    writer.WriteAttributeString("quat", FormatQuat(RpyToQuat(rpy)));
                }
                writer.WriteEndElement();
            }
        }

        private static void WriteVisualColor(XmlWriter writer, Link link)
        {
            // Only emit rgba if the user actually set a material color; a missing material leaves
            // MuJoCo free to choose its default render color.
            Material material = link.Visual?.Material;
            if (material == null || material.Color == null)
            {
                return;
            }

            double? r = TryGetDouble(material.Color, "Red");
            double? g = TryGetDouble(material.Color, "Green");
            double? b = TryGetDouble(material.Color, "Blue");
            double? a = TryGetDouble(material.Color, "Alpha");
            if (r.HasValue && g.HasValue && b.HasValue && a.HasValue)
            {
                writer.WriteAttributeString(
                    "rgba",
                    string.Join(" ", new[]
                    {
                        FormatDouble(r.Value),
                        FormatDouble(g.Value),
                        FormatDouble(b.Value),
                        FormatDouble(a.Value),
                    }));
            }
        }

        private static void WriteSites(
            XmlWriter writer,
            Link link,
            IDictionary<string, List<MjcfSite>> linkSites)
        {
            if (link?.Name == null || !linkSites.TryGetValue(link.Name, out List<MjcfSite> sites))
            {
                return;
            }
            foreach (MjcfSite site in sites ?? new List<MjcfSite>())
            {
                if (site == null || string.IsNullOrWhiteSpace(site.Name))
                {
                    continue;
                }
                writer.WriteStartElement("site");
                writer.WriteAttributeString("name", SanitizeName(site.Name));
                if (site.XYZ != null && site.XYZ.Length == 3 && !IsZero(site.XYZ))
                {
                    writer.WriteAttributeString("pos", FormatVec3(site.XYZ));
                }
                if (site.RPY != null && site.RPY.Length == 3 && !IsZero(site.RPY))
                {
                    writer.WriteAttributeString("quat", FormatQuat(RpyToQuat(site.RPY)));
                }
                writer.WriteEndElement();
            }
        }

        #endregion Body tree

        #region Tail sections

        private static void WriteEqualities(XmlWriter writer, Link baseLink)
        {
            List<Tuple<Joint, Joint>> mimicPairs = new List<Tuple<Joint, Joint>>();
            Dictionary<string, Joint> jointsByName = new Dictionary<string, Joint>();
            CollectJoints(baseLink, jointsByName);
            CollectMimicPairs(baseLink, jointsByName, mimicPairs);
            if (mimicPairs.Count == 0)
            {
                return;
            }

            writer.WriteStartElement("equality");
            foreach (Tuple<Joint, Joint> pair in mimicPairs)
            {
                Joint dependent = pair.Item1;
                Joint source = pair.Item2;
                double multiplier = HasValue(dependent.Mimic, "Multiplier")
                    ? dependent.Mimic.Multiplier : 1.0;
                double offset = HasValue(dependent.Mimic, "Offset")
                    ? dependent.Mimic.Offset : 0.0;

                // dependent = multiplier * source + offset  <=>
                //    dependent - (offset + multiplier * source + 0*source^2 ... ) = 0
                // which matches MJCF polycoef = "offset multiplier 0 0 0".
                writer.WriteStartElement("joint");
                writer.WriteAttributeString("joint1", SanitizeName(dependent.Name));
                writer.WriteAttributeString("joint2", SanitizeName(source.Name));
                writer.WriteAttributeString(
                    "polycoef",
                    string.Join(" ", new[]
                    {
                        FormatDouble(offset),
                        FormatDouble(multiplier),
                        FormatDouble(0),
                        FormatDouble(0),
                        FormatDouble(0),
                    }));
                writer.WriteEndElement();
            }
            writer.WriteEndElement(); // equality
        }

        private static void WriteContacts(XmlWriter writer, Link baseLink)
        {
            List<Tuple<string, string>> adjacencies = new List<Tuple<string, string>>();
            CollectAdjacentPairs(baseLink, adjacencies);
            if (adjacencies.Count == 0)
            {
                return;
            }

            writer.WriteStartElement("contact");
            foreach (Tuple<string, string> pair in adjacencies)
            {
                writer.WriteStartElement("exclude");
                writer.WriteAttributeString("body1", SanitizeName(pair.Item1));
                writer.WriteAttributeString("body2", SanitizeName(pair.Item2));
                writer.WriteEndElement();
            }
            writer.WriteEndElement(); // contact
        }

        private static void WriteActuators(XmlWriter writer, Link baseLink, MjcfOptions options)
        {
            List<Joint> actuatable = new List<Joint>();
            CollectActuatableJoints(baseLink, actuatable);
            if (actuatable.Count == 0)
            {
                return;
            }

            writer.WriteStartElement("actuator");
            foreach (Joint joint in actuatable)
            {
                switch (options.ActuatorType)
                {
                    case MjcfActuatorType.Position:
                        writer.WriteStartElement("position");
                        writer.WriteAttributeString("name", SanitizeName(joint.Name) + "_act");
                        writer.WriteAttributeString("joint", SanitizeName(joint.Name));
                        writer.WriteAttributeString("kp", FormatDouble(options.ActuatorGain));
                        writer.WriteEndElement();
                        break;
                    case MjcfActuatorType.Velocity:
                        writer.WriteStartElement("velocity");
                        writer.WriteAttributeString("name", SanitizeName(joint.Name) + "_act");
                        writer.WriteAttributeString("joint", SanitizeName(joint.Name));
                        writer.WriteAttributeString("kv", FormatDouble(options.ActuatorGain));
                        writer.WriteEndElement();
                        break;
                    case MjcfActuatorType.Motor:
                    default:
                        writer.WriteStartElement("motor");
                        writer.WriteAttributeString("name", SanitizeName(joint.Name) + "_act");
                        writer.WriteAttributeString("joint", SanitizeName(joint.Name));
                        writer.WriteAttributeString("gear", FormatDouble(options.ActuatorGain));
                        writer.WriteEndElement();
                        break;
                }
            }
            writer.WriteEndElement();
        }

        #endregion Tail sections

        #region Tree traversal helpers

        private static void CollectJoints(Link link, Dictionary<string, Joint> joints)
        {
            if (link.Joint != null && !string.IsNullOrWhiteSpace(link.Joint.Name))
            {
                joints[link.Joint.Name] = link.Joint;
            }
            foreach (Link child in link.Children)
            {
                CollectJoints(child, joints);
            }
        }

        private static void CollectMimicPairs(
            Link link,
            Dictionary<string, Joint> jointsByName,
            List<Tuple<Joint, Joint>> results)
        {
            if (link.Joint != null
                && link.Joint.Mimic != null
                && !string.IsNullOrWhiteSpace(link.Joint.Mimic.JointName)
                && jointsByName.TryGetValue(link.Joint.Mimic.JointName, out Joint source))
            {
                results.Add(Tuple.Create(link.Joint, source));
            }
            foreach (Link child in link.Children)
            {
                CollectMimicPairs(child, jointsByName, results);
            }
        }

        private static void CollectAdjacentPairs(Link link, List<Tuple<string, string>> results)
        {
            foreach (Link child in link.Children)
            {
                results.Add(Tuple.Create(link.Name, child.Name));
                CollectAdjacentPairs(child, results);
            }
        }

        private static void CollectActuatableJoints(Link link, List<Joint> joints)
        {
            if (link.Joint != null
                && !string.IsNullOrWhiteSpace(link.Joint.Name)
                && link.Joint.Type != null
                && link.Joint.Type != "fixed"
                && link.Joint.Type != "floating")
            {
                // Mimic'd joints are driven by their source, not by an actuator.
                bool isMimic = link.Joint.Mimic != null
                    && !string.IsNullOrWhiteSpace(link.Joint.Mimic.JointName);
                if (!isMimic)
                {
                    joints.Add(link.Joint);
                }
            }
            foreach (Link child in link.Children)
            {
                CollectActuatableJoints(child, joints);
            }
        }

        #endregion Tree traversal helpers

        #region Formatting / math

        private static string FormatDouble(double value)
        {
            // MJCF is parsed with the C locale; always use invariant culture and strip redundant
            // trailing zeros while keeping enough precision for CAD-scale models.
            return value.ToString("G6", CultureInfo.InvariantCulture);
        }

        private static string FormatVec3(double[] v)
        {
            return FormatDouble(v[0]) + " " + FormatDouble(v[1]) + " " + FormatDouble(v[2]);
        }

        private static string FormatQuat(double[] q)
        {
            return FormatDouble(q[0]) + " "
                 + FormatDouble(q[1]) + " "
                 + FormatDouble(q[2]) + " "
                 + FormatDouble(q[3]);
        }

        private static bool IsZero(double[] v)
        {
            foreach (double x in v)
            {
                if (Math.Abs(x) > 1e-12)
                {
                    return false;
                }
            }
            return true;
        }

        // URDF rpy is R = Rz(yaw) * Ry(pitch) * Rx(roll) (extrinsic X-Y-Z / intrinsic Z-Y-X).
        // The returned quaternion is [w, x, y, z] matching MJCF's default quat order.
        internal static double[] RpyToQuat(double[] rpy)
        {
            double roll = rpy[0];
            double pitch = rpy[1];
            double yaw = rpy[2];
            double cr = Math.Cos(roll * 0.5);
            double sr = Math.Sin(roll * 0.5);
            double cp = Math.Cos(pitch * 0.5);
            double sp = Math.Sin(pitch * 0.5);
            double cy = Math.Cos(yaw * 0.5);
            double sy = Math.Sin(yaw * 0.5);
            return new double[]
            {
                cr * cp * cy + sr * sp * sy,
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy,
            };
        }

        private static void OrthogonalBasis(double[] axis, out double[] u, out double[] v)
        {
            double ax = axis[0], ay = axis[1], az = axis[2];
            double mag = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (mag < 1e-9)
            {
                u = new double[] { 1, 0, 0 };
                v = new double[] { 0, 1, 0 };
                return;
            }
            ax /= mag; ay /= mag; az /= mag;

            // Pick the world axis least aligned with `axis` to seed u.
            double[] seed = (Math.Abs(ax) < 0.9)
                ? new double[] { 1, 0, 0 }
                : new double[] { 0, 1, 0 };
            // u = normalize(axis x seed)
            double ux = ay * seed[2] - az * seed[1];
            double uy = az * seed[0] - ax * seed[2];
            double uz = ax * seed[1] - ay * seed[0];
            double umag = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            u = new double[] { ux / umag, uy / umag, uz / umag };
            // v = axis x u
            v = new double[]
            {
                ay * u[2] - az * u[1],
                az * u[0] - ax * u[2],
                ax * u[1] - ay * u[0],
            };
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unnamed";
            }
            // MuJoCo identifiers allow letters, digits, '_', '-', and '.'. Slashes and spaces are
            // the most common offenders coming from SolidWorks defaults.
            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }
            return sb.ToString();
        }

        // Small reflection-based helpers let us gracefully ignore attributes that aren't populated
        // on the URDF tree. The alternative is per-type null checks on Color.Red, Mass.Value, etc.;
        // we keep it reflection-based because the CSV import path leaves several values as literal
        // null and blowing up during export is worse than silently omitting a detail.

        private static bool HasValue(object obj, string propertyName)
        {
            if (obj == null)
            {
                return false;
            }
            System.Reflection.PropertyInfo prop = obj.GetType().GetProperty(propertyName);
            if (prop == null)
            {
                return false;
            }
            try
            {
                object value = prop.GetValue(obj, null);
                return value != null;
            }
            catch
            {
                return false;
            }
        }

        private static double GetDoubleOrZero(object obj, string propertyName)
        {
            double? value = TryGetDouble(obj, propertyName);
            return value ?? 0.0;
        }

        private static double? TryGetDouble(object obj, string propertyName)
        {
            if (obj == null)
            {
                return null;
            }
            System.Reflection.PropertyInfo prop = obj.GetType().GetProperty(propertyName);
            if (prop == null)
            {
                return null;
            }
            try
            {
                object value = prop.GetValue(obj, null);
                if (value == null)
                {
                    return null;
                }
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        #endregion Formatting / math
    }
}
