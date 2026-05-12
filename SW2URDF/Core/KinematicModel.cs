using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SW2URDF.Core
{
    /// <summary>
    /// Format-neutral robot description extracted from SolidWorks and consumed
    /// by URDF / MJCF writers. This namespace intentionally has no dependency on
    /// SolidWorks.Interop.*, URDFElement, MJCF writer types, or DataContract.
    ///
    /// The tree root is an explicit world <see cref="LinkModel"/> that owns the
    /// global frame and any worldbody-direct geometry (MJCF idiom: ground planes,
    /// scene fiducials). Its children are the top-level bodies - each is the root
    /// of an independent kinematic tree. URDF export takes the first top-level
    /// body as <c>base_link</c> and warns when there is more than one, since URDF
    /// describes a single robot in isolation; MJCF emits all of them as direct
    /// children of <c>&lt;worldbody&gt;</c>.
    /// </summary>
    public sealed record KinematicTree(
        string Name,
        string GlobalOriginCoordinateSystemName,
        LinkModel WorldBody)
    {
        [JsonIgnore]
        public IReadOnlyList<LinkModel> TopLevelBodies =>
            WorldBody?.Children ?? Array.Empty<LinkModel>();
    }

    /// <summary>
    /// How a top-level body attaches to the world. Only meaningful on the
    /// immediate children of <see cref="KinematicTree.WorldBody"/>; ignored for nested
    /// links (which carry an explicit <see cref="JointModel"/> instead).
    /// </summary>
    public enum WorldAttachmentModel
    {
        /// <summary>Body is rigidly fixed to world (no joint emitted).</summary>
        Welded = 0,

        /// <summary>Body has a 6-DoF freejoint attaching it to world (MJCF only).</summary>
        Free = 1,
    }

    public sealed record LinkModel(
        string Name,
        InertialModel Inertial,
        MaterialModel Material,
        IReadOnlyList<MeshGroupModel> VisualGroups,
        IReadOnlyList<MeshGroupModel> CollisionGroups,
        bool CollisionUsesVisual,
        InertialSourceModel InertialSource,
        IReadOnlyList<ComponentReferenceModel> InertialComponents,
        IReadOnlyList<SiteModel> Sites,
        JointModel Joint,
        IReadOnlyList<LinkModel> Children,
        bool IsFixedFrame = false,
        bool StlQualityFine = false,
        WorldAttachmentModel WorldAttachment = WorldAttachmentModel.Welded);

    public sealed record JointModel(
        string Name,
        string Type,
        string ParentLinkName,
        string ChildLinkName,
        PoseModel Origin,
        Vector3Model Axis,
        JointLimitModel Limit,
        string CoordinateSystemName,
        string AxisName,
        bool AxisFlipped,
        bool AutoComputeLimits = true,
        double? Damping = null,
        double? Friction = null,
        double? Armature = null,
        double? Reference = null,
        bool AutoDeriveAxis = false);

    public sealed record MeshGroupModel(
        string Name,
        string MeshFilename,
        IReadOnlyList<ComponentReferenceModel> Components);

    public sealed record SiteModel(
        string Name,
        string CoordinateSystemName,
        PoseModel Pose);

    public sealed record MaterialModel(
        string Name,
        RgbaModel Color,
        string TextureFilename);

    public sealed record InertialModel(
        PoseModel Origin,
        double Mass,
        InertiaTensorModel Inertia);

    public sealed record JointLimitModel(
        double? Lower,
        double? Upper,
        double? Effort,
        double? Velocity);

    public sealed record PoseModel(
        Vector3Model Position,
        RpyModel Rotation);

    public sealed record Vector3Model(
        double X,
        double Y,
        double Z);

    public sealed record RpyModel(
        double Roll,
        double Pitch,
        double Yaw);

    public sealed record RgbaModel(
        double Red,
        double Green,
        double Blue,
        double Alpha);

    public sealed record InertiaTensorModel(
        double Ixx,
        double Ixy,
        double Ixz,
        double Iyy,
        double Iyz,
        double Izz);

    /// <summary>
    /// SolidWorks component identity without carrying a live COM object. The
    /// persistent ID is base64-ready so the upcoming JSON configuration layer
    /// can store it without binding to SolidWorks.Interop.sldworks.Component2.
    /// </summary>
    public sealed record ComponentReferenceModel(
        string DisplayName,
        byte[] PersistentId);

    public enum InertialSourceModel
    {
        Visual = 0,
        Collision = 1,
        Custom = 2,
    }
}
