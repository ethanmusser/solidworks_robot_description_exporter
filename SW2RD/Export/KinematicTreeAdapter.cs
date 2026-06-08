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
using SW2RD.Input;
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2RD.Export
{
    /// <summary>
    /// Boundary adapter between the format-neutral Core records (KinematicTree)
    /// and the SolidWorks edit/compute model (SW2RD.Input: Robot / Link / Joint).
    /// The translation stays explicit so the records-native URDF / MJCF writers
    /// consume KinematicTree while the PMPage and export-time mesh / inertial /
    /// kinematics computation continue to operate on the editable Input model.
    /// </summary>
    public static class KinematicTreeAdapter
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        /// <summary>
        /// Convenience wrapper for callers that only have a single-tree
        /// legacy Robot. Synthesizes an empty world <see cref="LinkModel"/>
        /// (global origin inherited from the base link's coord-sys so STL
        /// anchoring + LocalizeJoint behavior are unchanged) and a single
        /// Welded top-level body. Multi-tree / world geometry callers should
        /// build the <see cref="KinematicTree"/> directly.
        /// </summary>
        public static KinematicTree ToCore(Robot robot)
        {
            if (robot == null)
            {
                throw new ArgumentNullException(nameof(robot));
            }

            LinkModel topLevel = ToCore(robot.BaseLink);
            string globalOrigin = robot.BaseLink?.Joint?.CoordinateSystemName ?? "";
            LinkModel worldBody = CreateWorldBody(new[] { topLevel });
            return new KinematicTree(robot.Name ?? "", globalOrigin, worldBody);
        }

        private static LinkModel CreateWorldBody(IReadOnlyList<LinkModel> topLevelBodies)
        {
            return new LinkModel(
                WorldNode.DefaultName,
                EmptyInertial(),
                new MaterialModel("", new RgbaModel(1, 1, 1, 1), ""),
                Array.Empty<MeshGroupModel>(),
                Array.Empty<MeshGroupModel>(),
                false,
                InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                null,
                topLevelBodies ?? Array.Empty<LinkModel>());
        }

        /// <summary>
        /// Converts a fully-built LinkNode tree (rooted at a <see cref="WorldNode"/>)
        /// into a <see cref="KinematicTree"/>. The WorldNode's underlying Link
        /// becomes the world body - its visual / collision / site groups are
        /// persisted on <see cref="KinematicTree.WorldBody"/>, and its
        /// <c>Joint.CoordinateSystemName</c> becomes
        /// <see cref="KinematicTree.GlobalOriginCoordinateSystemName"/>.
        ///
        /// For backwards compatibility, this method also accepts a plain
        /// <see cref="LinkNode"/> root (no WorldNode) - in which case it
        /// synthesizes a Welded single-body tree wrapped in an empty world
        /// body whose global origin name is taken from
        /// the legacy root's joint coord-sys.
        /// </summary>
        public static KinematicTree ToCore(LinkNode rootNode, string treeName)
        {
            if (rootNode == null)
            {
                throw new ArgumentNullException(nameof(rootNode));
            }

            if (rootNode is WorldNode worldNode)
            {
                // Refresh Link.Children from the LinkNode hierarchy so the
                // recursive ToCore(Link) walks the live tree shape.
                worldNode.UpdateLinkTree(null);

                LinkModel worldBody = ToCore(worldNode.Link, isRoot: true);
                return new KinematicTree(
                    treeName ?? "",
                    worldNode.GlobalOriginCoordinateSystemName ?? "",
                    worldBody);
            }

            // Legacy single-tree LinkNode (no WorldNode wrapper). Wrap as a
            // Welded single-body tree under an empty World whose global
            // origin name inherits from the root's joint coord-sys.
            rootNode.UpdateLinkTree(null);
            LinkModel legacyTopLevel = ToCore(rootNode.Link);
            string globalOrigin = rootNode.Link?.Joint?.CoordinateSystemName ?? "";
            return new KinematicTree(treeName ?? "", globalOrigin, CreateWorldBody(new[] { legacyTopLevel }));
        }

        /// <summary>
        /// Reverse of <see cref="ToCore(LinkNode, string)"/>: builds a LinkNode
        /// tree rooted at a <see cref="WorldNode"/> from a <see cref="KinematicTree"/>.
        /// The world's geometry / global origin are unpacked onto the WorldNode's
        /// underlying Link, and each top-level body becomes a child LinkNode of
        /// the WorldNode.
        /// </summary>
        public static WorldNode ToWorldNode(KinematicTree tree)
        {
            if (tree == null)
            {
                throw new ArgumentNullException(nameof(tree));
            }

            WorldNode worldNode = new WorldNode();
            LinkModel worldModel = tree.WorldBody ?? CreateWorldBody(Array.Empty<LinkModel>());

            // Repurpose the WorldNode's Link as the world-geometry container.
            Link worldLink = ToLegacyLink(worldModel, null);
            worldNode.Link = worldLink;
            worldLink.Name = WorldNode.DefaultName;
            worldNode.Text = WorldNode.DefaultName;
            worldNode.Name = WorldNode.DefaultName;

            // Global origin coord-sys lives in Joint.CoordinateSystemName on
            // the world's Link (matching pre-refactor base-link semantics so
            // the STL / LocalizeJoint anchor reads work unchanged).
            if (worldLink.Joint != null)
            {
                worldLink.Joint.CoordinateSystemName = tree.GlobalOriginCoordinateSystemName ?? "";
            }

            // Rebuild the TreeNode children from the Link children, then clear
            // the embedded Link.Children list so the PMPage's LinkNode tree
            // remains the editable source of truth.
            foreach (Link topLevelLink in new List<Link>(worldLink.Children))
            {
                worldNode.Nodes.Add(new LinkNode(topLevelLink));
            }
            worldLink.Children.Clear();

            return worldNode;
        }

        public static Link ToLegacyLink(LinkModel model, Link parent)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            Link link = new Link(parent)
            {
                Name = model.Name ?? "",
                CollisionUsesVisual = model.CollisionUsesVisual,
                InertialSource = ToLegacyInertialSource(model.InertialSource),
                isFixedFrame = model.IsFixedFrame,
                STLQualityFine = model.StlQualityFine,
                VisualGroups = ToLegacyMeshGroups(model.VisualGroups),
                CollisionGroups = ToLegacyMeshGroups(model.CollisionGroups),
                Sites = ToLegacySites(model.Sites),
                WorldAttachment = model.WorldAttachment,
            };

            PopulateLegacyRefs(
                model.InertialComponents,
                link.InertialComponentPIDs,
                link.InertialComponentNames,
                link.InertialComponentPaths);

            ApplyInertial(model.Inertial, link.Inertial);
            ApplyMaterial(model.Material, link.Visual.Material);
            ApplyJoint(model.Joint, link.Joint);

            if (model.Children != null)
            {
                foreach (LinkModel childModel in model.Children)
                {
                    Link child = ToLegacyLink(childModel, link);
                    link.Children.Add(child);
                }
            }

            return link;
        }

        public static LinkModel ToCore(Link link, bool isRoot = false)
        {
            if (link == null)
            {
                throw new ArgumentNullException(nameof(link));
            }

            link.MigrateLegacyComponents();
            return new LinkModel(
                link.Name ?? "",
                ToCoreInertial(link.Inertial),
                ToCoreMaterial(link.Visual?.Material),
                ToCoreMeshGroups(link.VisualGroups),
                ToCoreMeshGroups(link.CollisionGroups),
                link.CollisionUsesVisual,
                ToCoreInertialSource(link.InertialSource),
                ToComponentReferences(
                    link.InertialComponentPIDs,
                    link.InertialComponentNames,
                    link.InertialComponentPaths),
                ToCoreSites(link.Sites),
                isRoot ? null : ToCoreJoint(link.Joint),
                link.Children?.Select(child => ToCore(child)).ToList() ?? new List<LinkModel>(),
                link.isFixedFrame,
                link.STLQualityFine,
                link.WorldAttachment);
        }

        private static void ApplyInertial(InertialModel source, Inertial target)
        {
            if (source == null || target == null)
            {
                return;
            }

            ApplyPose(source.Origin, target.Origin);
            target.Mass.Value = source.Mass;
            target.Inertia.Ixx = source.Inertia?.Ixx ?? 0.0;
            target.Inertia.Ixy = source.Inertia?.Ixy ?? 0.0;
            target.Inertia.Ixz = source.Inertia?.Ixz ?? 0.0;
            target.Inertia.Iyy = source.Inertia?.Iyy ?? 0.0;
            target.Inertia.Iyz = source.Inertia?.Iyz ?? 0.0;
            target.Inertia.Izz = source.Inertia?.Izz ?? 0.0;
        }

        private static void ApplyMaterial(MaterialModel source, Material target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.Name = source.Name ?? "";
            if (source.Color != null)
            {
                target.Color.SetColor(new[]
                {
                    source.Color.Red,
                    source.Color.Green,
                    source.Color.Blue,
                    source.Color.Alpha,
                });
            }
            target.Texture.Filename = source.TextureFilename ?? "";
            target.Texture.wFilename = source.TextureFilename ?? "";
        }

        private static void ApplyJoint(JointModel source, Joint target)
        {
            if (source == null || target == null)
            {
                return;
            }

            // Angularity keys off the canonical (source) type, which still
            // carries "revolute"/"continuous" before NormalizeJointTypeForUi
            // collapses continuous->revolute for the UI.
            bool angular = Joint.UsesAngularUnits(source.Type);

            target.Name = source.Name ?? "";
            target.Type = NormalizeJointTypeForUi(source.Type);
            target.Parent.Name = source.ParentLinkName ?? "";
            target.Child.Name = source.ChildLinkName ?? "";
            target.CoordinateSystemName = source.CoordinateSystemName ?? "";
            target.AxisName = source.AxisName ?? "";
            target.AxisFlipped = source.AxisFlipped;
            target.AutoComputeLimits = source.AutoComputeLimits;
            target.AutoDeriveAxis = source.AutoDeriveAxis;
            target.Reference = angular ? RadiansToDegrees(source.Reference) : source.Reference;
            target.Armature = source.Armature;

            // "Automatically Generate" axis sentinel migration on the Config
            // path. Pre-AutoDeriveAxis JSON saves stored the sentinel literal
            // in AxisName; map it onto the AutoDeriveAxis boolean here so the
            // SelectionBox-only UI sees a clean (true, empty) pair.
            if (target.AxisName == "Automatically Generate")
            {
                target.AutoDeriveAxis = true;
                target.AxisName = "";
            }
            ApplyPose(source.Origin, target.Origin);
            if (source.Axis != null)
            {
                target.Axis.SetXYZ(new[] { source.Axis.X, source.Axis.Y, source.Axis.Z });
            }
            if (source.Limit != null)
            {
                // rad -> deg for angular position/velocity; effort (torque)
                // and prismatic (meters) pass through.
                target.Limit.SetLower(angular ? RadiansToDegrees(source.Limit.Lower) : source.Limit.Lower);
                target.Limit.SetUpper(angular ? RadiansToDegrees(source.Limit.Upper) : source.Limit.Upper);
                target.Limit.SetEffort(source.Limit.Effort);
                target.Limit.SetVelocity(angular ? RadiansToDegrees(source.Limit.Velocity) : source.Limit.Velocity);
            }
            // Damping / Friction live on Joint.Dynamics in the legacy
            // graph; null on the source means the writer should omit the
            // attribute, otherwise we set it (converting damping back to the
            // legacy per-degree basis for angular joints).
            if (source.Damping.HasValue)
            {
                target.Dynamics.SetDamping(angular
                    ? DampingPerRadianToPerDegree(source.Damping)
                    : source.Damping);
            }
            if (source.Friction.HasValue)
            {
                target.Dynamics.SetFriction(source.Friction);
            }
        }

        private static string NormalizeJointTypeForUi(string jointType)
        {
            if (jointType == "continuous")
            {
                return "revolute";
            }
            if (jointType == "Automatically Detect" || jointType == "Automatically Generate")
            {
                return "";
            }
            return jointType ?? "";
        }

        // Canonical -> legacy pose. The canonical rotation is a quaternion;
        // the legacy Origin stores roll-pitch-yaw (radians, extrinsic XYZ),
        // so we convert through MathOps.QuaternionToRPY which shares the
        // angle-sequence definition with MathOps.GetRPY.
        private static void ApplyPose(PoseModel source, Origin target)
        {
            if (source == null || target == null)
            {
                return;
            }
            if (source.Position != null)
            {
                target.SetXYZ(new[] { source.Position.X, source.Position.Y, source.Position.Z });
            }
            if (source.Rotation != null)
            {
                double[] rpy = MathOps.QuaternionToRPY(new[]
                {
                    source.Rotation.W, source.Rotation.X, source.Rotation.Y, source.Rotation.Z,
                });
                target.SetRPY(rpy);
            }
        }

        private static List<MeshGroup> ToLegacyMeshGroups(IReadOnlyList<MeshGroupModel> groups)
        {
            List<MeshGroup> result = new List<MeshGroup>();
            if (groups == null)
            {
                return result;
            }

            foreach (MeshGroupModel group in groups)
            {
                MeshGroup meshGroup = new MeshGroup(group.Name)
                {
                    MeshFilename = group.MeshFilename ?? "",
                };
                PopulateLegacyRefs(
                    group.Components,
                    meshGroup.ComponentPIDs,
                    meshGroup.ComponentNames,
                    meshGroup.ComponentPaths);
                result.Add(meshGroup);
            }
            return result;
        }

        // Fills the index-aligned (PID, name, path) legacy lists from a list of
        // ComponentReferenceModel records. Skips entries with no persistent ID so
        // the three lists stay aligned. The lists are cleared first so this is
        // safe to call against a freshly-constructed (empty) MeshGroup / Link.
        private static void PopulateLegacyRefs(
            IReadOnlyList<ComponentReferenceModel> references,
            List<byte[]> pids,
            List<string> names,
            List<string> paths)
        {
            pids.Clear();
            names.Clear();
            paths.Clear();
            if (references == null)
            {
                return;
            }
            foreach (ComponentReferenceModel reference in references)
            {
                if (reference?.PersistentId == null)
                {
                    continue;
                }
                pids.Add((byte[])reference.PersistentId.Clone());
                names.Add(reference.DisplayName ?? "");
                paths.Add(reference.Path ?? "");
            }
        }

        private static List<SiteSpec> ToLegacySites(IReadOnlyList<SiteModel> sites)
        {
            List<SiteSpec> result = new List<SiteSpec>();
            if (sites == null)
            {
                return result;
            }
            foreach (SiteModel site in sites)
            {
                result.Add(new SiteSpec(site.Name, site.CoordinateSystemName));
            }
            return result;
        }

        private static InertialSource ToLegacyInertialSource(InertialSourceModel source)
        {
            return (InertialSource)(int)source;
        }

        private static InertialModel ToCoreInertial(Inertial inertial)
        {
            if (inertial == null)
            {
                return EmptyInertial();
            }
            return new InertialModel(
                ToCorePose(inertial.Origin),
                inertial.Mass?.Value ?? 0.0,
                new InertiaTensorModel(
                    inertial.Inertia?.Ixx ?? 0.0,
                    inertial.Inertia?.Ixy ?? 0.0,
                    inertial.Inertia?.Ixz ?? 0.0,
                    inertial.Inertia?.Iyy ?? 0.0,
                    inertial.Inertia?.Iyz ?? 0.0,
                    inertial.Inertia?.Izz ?? 0.0));
        }

        private static MaterialModel ToCoreMaterial(Material material)
        {
            if (material == null)
            {
                return new MaterialModel("", new RgbaModel(1, 1, 1, 1), "");
            }
            double[] rgba = material.Color?.GetColor() ?? new[] { 1.0, 1.0, 1.0, 1.0 };
            return new MaterialModel(
                material.Name ?? "",
                new RgbaModel(rgba[0], rgba[1], rgba[2], rgba[3]),
                material.Texture?.wFilename ?? "");
        }

        private static JointModel ToCoreJoint(Joint joint)
        {
            if (joint == null)
            {
                return null;
            }
            double[] axis = joint.Axis?.GetXYZ() ?? new[] { 0.0, 0.0, 0.0 };
            bool angular = Joint.UsesAngularUnits(joint.Type);
            double? damping = joint.Dynamics?.DampingOrNull;
            double? friction = joint.Dynamics?.FrictionOrNull;
            double? reference = joint.Reference;
            if (angular)
            {
                // deg -> rad for the angular scalars. Friction (static
                // friction force/torque) and Armature (rotor inertia) are not
                // angle-dependent and pass through. Effort is handled inside
                // ToCoreLimit (also angle-independent).
                damping = DampingPerDegreeToPerRadian(damping);
                reference = DegreesToRadians(reference);
            }
            return new JointModel(
                joint.Name ?? "",
                joint.Type ?? "",
                joint.Parent?.Name ?? "",
                joint.Child?.Name ?? "",
                ToCorePose(joint.Origin),
                new Vector3Model(axis[0], axis[1], axis[2]),
                ToCoreLimit(joint.Limit, angular),
                joint.CoordinateSystemName ?? "",
                joint.AxisName ?? "",
                joint.AxisFlipped,
                joint.AutoComputeLimits,
                damping,
                friction,
                joint.Armature,
                reference,
                joint.AutoDeriveAxis);
        }

        // Translates a legacy Limit element into the format-neutral
        // JointLimitModel record. Returns null when the limit element
        // carries no data so the writer pipeline can omit <limit>
        // entirely (URDF requires it for revolute/prismatic but not
        // continuous; MJCF emits a `range=` only when both lower and
        // upper are present). Reads field-by-field via the *OrNull
        // accessors because individual URDFAttribute.Value entries can
        // be null on a partially-populated Limit (e.g. the Effort and
        // Velocity attributes default to null in the constructor).
        private static JointLimitModel ToCoreLimit(Limit limit, bool angular)
        {
            if (limit == null)
            {
                return null;
            }
            double? lower = limit.LowerOrNull;
            double? upper = limit.UpperOrNull;
            double? effort = limit.EffortOrNull;
            double? velocity = limit.VelocityOrNull;
            if (!lower.HasValue && !upper.HasValue && !effort.HasValue && !velocity.HasValue)
            {
                return null;
            }
            if (angular)
            {
                // Lower/Upper (position) and Velocity convert deg -> rad for
                // angular joints. Effort is a torque (N*m) and is not
                // angle-dependent, so it passes through.
                lower = DegreesToRadians(lower);
                upper = DegreesToRadians(upper);
                velocity = DegreesToRadians(velocity);
            }
            return new JointLimitModel(lower, upper, effort, velocity);
        }

        private static List<MeshGroupModel> ToCoreMeshGroups(List<MeshGroup> groups)
        {
            if (groups == null)
            {
                return new List<MeshGroupModel>();
            }
            return groups.Select(group => new MeshGroupModel(
                group.Name ?? "",
                group.MeshFilename ?? "",
                ToComponentReferences(
                    group.ComponentPIDs,
                    group.ComponentNames,
                    group.ComponentPaths))).ToList();
        }

        private static List<SiteModel> ToCoreSites(List<SiteSpec> sites)
        {
            if (sites == null)
            {
                return new List<SiteModel>();
            }
            return sites.Select(site => new SiteModel(
                site.Name ?? "",
                site.CoordinateSystemName ?? "",
                EmptyPose())).ToList();
        }

        // Builds the format-neutral ComponentReferenceModel list from the
        // index-aligned legacy (PID, name, path) lists. The name/path lists may
        // be shorter than the PID list for configs / migrations that predate
        // them, so each lookup is index-guarded.
        private static List<ComponentReferenceModel> ToComponentReferences(
            List<byte[]> persistentIds,
            List<string> names,
            List<string> paths)
        {
            List<ComponentReferenceModel> result = new List<ComponentReferenceModel>();
            if (persistentIds == null)
            {
                return result;
            }
            for (int i = 0; i < persistentIds.Count; i++)
            {
                byte[] pid = persistentIds[i];
                if (pid == null)
                {
                    continue;
                }
                string name = (names != null && i < names.Count) ? names[i] : "";
                string path = (paths != null && i < paths.Count) ? paths[i] : "";
                result.Add(new ComponentReferenceModel(
                    name ?? "", (byte[])pid.Clone(), path ?? ""));
            }
            return result;
        }

        private static InertialSourceModel ToCoreInertialSource(InertialSource source)
        {
            return (InertialSourceModel)(int)source;
        }

        // Legacy -> canonical pose. The legacy Origin stores roll-pitch-yaw
        // (radians, extrinsic XYZ); the canonical PoseModel stores a unit
        // quaternion, so we convert through MathOps.RPYToQuaternion.
        private static PoseModel ToCorePose(Origin origin)
        {
            if (origin == null)
            {
                return EmptyPose();
            }
            double[] xyz = origin.GetXYZ();
            double[] rpy = origin.GetRPY();
            double[] q = MathOps.RPYToQuaternion(rpy);
            return new PoseModel(
                new Vector3Model(xyz[0], xyz[1], xyz[2]),
                new QuaternionModel(q[0], q[1], q[2], q[3]));
        }

        private static InertialModel EmptyInertial()
        {
            return new InertialModel(EmptyPose(), 0.0, new InertiaTensorModel(0, 0, 0, 0, 0, 0));
        }

        private static PoseModel EmptyPose()
        {
            return new PoseModel(
                new Vector3Model(0, 0, 0),
                QuaternionModel.Identity);
        }

        // --- Angular unit conversions (legacy degree-basis <-> canonical radian-basis) ---
        // The legacy Joint/Limit/Dynamics model carries angular quantities in
        // degrees (the SolidWorks PMP convention); the canonical JointModel is
        // radians. These nullable wrappers convert only when the joint is
        // angular (revolute/continuous); linear (prismatic) values are meters
        // and pass through untouched.
        private static double? DegreesToRadians(double? degrees)
        {
            return degrees.HasValue ? (double?)Joint.DegreesToRadians(degrees.Value) : null;
        }

        private static double? RadiansToDegrees(double? radians)
        {
            return radians.HasValue ? (double?)Joint.RadiansToDegrees(radians.Value) : null;
        }

        private static double? DampingPerDegreeToPerRadian(double? perDegree)
        {
            return perDegree.HasValue
                ? (double?)Joint.AngularDampingPerDegreeToPerRadian(perDegree.Value)
                : null;
        }

        private static double? DampingPerRadianToPerDegree(double? perRadian)
        {
            return perRadian.HasValue ? (double?)(perRadian.Value * Math.PI / 180.0) : null;
        }
    }
}
