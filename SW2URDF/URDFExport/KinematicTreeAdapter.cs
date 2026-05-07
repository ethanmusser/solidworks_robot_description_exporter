using SW2URDF.Core;
using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Boundary adapter between the new format-neutral Core records and the
    /// legacy URDFElement-backed model. Phase 2 keeps this translation explicit
    /// so writers can grow KinematicTree entry points while existing export-time
    /// mesh generation and serialization code continues to compile.
    /// </summary>
    public static class KinematicTreeAdapter
    {
        public static Robot ToLegacyRobot(KinematicTree tree)
        {
            if (tree == null)
            {
                throw new ArgumentNullException(nameof(tree));
            }

            Robot robot = new Robot
            {
                Name = tree.Name ?? "",
            };
            robot.SetBaseLink(ToLegacyLink(tree.BaseLink, null));
            return robot;
        }

        public static KinematicTree ToCore(Robot robot)
        {
            if (robot == null)
            {
                throw new ArgumentNullException(nameof(robot));
            }

            return new KinematicTree(robot.Name ?? "", ToCore(robot.BaseLink, isRoot: true));
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
                InertialComponentPIDs = ToPersistentIds(model.InertialComponents),
            };

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
                ToComponentReferences(link.InertialComponentPIDs),
                ToCoreSites(link.Sites),
                isRoot ? null : ToCoreJoint(link.Joint),
                link.Children?.Select(child => ToCore(child)).ToList() ?? new List<LinkModel>(),
                link.isFixedFrame,
                link.STLQualityFine);
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

            target.Name = source.Name ?? "";
            target.Type = source.Type ?? "";
            target.Parent.Name = source.ParentLinkName ?? "";
            target.Child.Name = source.ChildLinkName ?? "";
            target.CoordinateSystemName = source.CoordinateSystemName ?? "";
            target.AxisName = source.AxisName ?? "";
            target.AxisFlipped = source.AxisFlipped;
            target.AutoComputeLimits = source.AutoComputeLimits;
            target.AutoDeriveAxis = source.AutoDeriveAxis;
            target.Reference = source.Reference;
            target.Armature = source.Armature;

            // Legacy "Automatically Generate" axis sentinel migration on
            // the ConfigV2 path. Pre-AutoDeriveAxis JSON saves stored the
            // sentinel literal in AxisName; map it onto the new boolean
            // here so the SelectionBox-only UI sees a clean (true,
            // empty) pair without depending on the DataContract
            // [OnDeserialized] callback (which doesn't run for the JSON
            // path).
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
                target.Limit.Lower = source.Limit.Lower ?? 0.0;
                target.Limit.Upper = source.Limit.Upper ?? 0.0;
                target.Limit.Effort = source.Limit.Effort ?? 0.0;
                target.Limit.Velocity = source.Limit.Velocity ?? 0.0;
            }
            // Damping / Friction live on Joint.Dynamics in the legacy
            // graph; null on the source means the writer should omit the
            // attribute, otherwise we set the underlying URDFAttribute
            // value directly.
            if (source.Damping.HasValue)
            {
                target.Dynamics.Damping = source.Damping.Value;
            }
            if (source.Friction.HasValue)
            {
                target.Dynamics.Friction = source.Friction.Value;
            }
        }

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
                target.SetRPY(new[] { source.Rotation.Roll, source.Rotation.Pitch, source.Rotation.Yaw });
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
                result.Add(new MeshGroup(group.Name)
                {
                    MeshFilename = group.MeshFilename ?? "",
                    ComponentPIDs = ToPersistentIds(group.Components),
                });
            }
            return result;
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

        private static List<byte[]> ToPersistentIds(IReadOnlyList<ComponentReferenceModel> references)
        {
            List<byte[]> result = new List<byte[]>();
            if (references == null)
            {
                return result;
            }
            foreach (ComponentReferenceModel reference in references)
            {
                if (reference?.PersistentId != null)
                {
                    result.Add((byte[])reference.PersistentId.Clone());
                }
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
            double? damping = joint.Dynamics?.DampingOrNull;
            double? friction = joint.Dynamics?.FrictionOrNull;
            return new JointModel(
                joint.Name ?? "",
                joint.Type ?? "",
                joint.Parent?.Name ?? "",
                joint.Child?.Name ?? "",
                ToCorePose(joint.Origin),
                new Vector3Model(axis[0], axis[1], axis[2]),
                ToCoreLimit(joint.Limit),
                joint.CoordinateSystemName ?? "",
                joint.AxisName ?? "",
                joint.AxisFlipped,
                joint.AutoComputeLimits,
                damping,
                friction,
                joint.Armature,
                joint.Reference,
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
        private static JointLimitModel ToCoreLimit(Limit limit)
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
            return new JointLimitModel(lower, upper, effort ?? 0.0, velocity ?? 0.0);
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
                ToComponentReferences(group.ComponentPIDs))).ToList();
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

        private static List<ComponentReferenceModel> ToComponentReferences(List<byte[]> persistentIds)
        {
            List<ComponentReferenceModel> result = new List<ComponentReferenceModel>();
            if (persistentIds == null)
            {
                return result;
            }
            foreach (byte[] pid in persistentIds)
            {
                if (pid != null)
                {
                    result.Add(new ComponentReferenceModel("", (byte[])pid.Clone()));
                }
            }
            return result;
        }

        private static InertialSourceModel ToCoreInertialSource(InertialSource source)
        {
            return (InertialSourceModel)(int)source;
        }

        private static PoseModel ToCorePose(Origin origin)
        {
            if (origin == null)
            {
                return EmptyPose();
            }
            double[] xyz = origin.GetXYZ();
            double[] rpy = origin.GetRPY();
            return new PoseModel(
                new Vector3Model(xyz[0], xyz[1], xyz[2]),
                new RpyModel(rpy[0], rpy[1], rpy[2]));
        }

        private static InertialModel EmptyInertial()
        {
            return new InertialModel(EmptyPose(), 0.0, new InertiaTensorModel(0, 0, 0, 0, 0, 0));
        }

        private static PoseModel EmptyPose()
        {
            return new PoseModel(
                new Vector3Model(0, 0, 0),
                new RpyModel(0, 0, 0));
        }
    }
}
