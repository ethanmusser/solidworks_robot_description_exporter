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

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SW2RD.Core
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

    /// <remarks>
    /// All angular quantities on this record are canonical RADIANS, and all
    /// linear quantities are canonical METERS (SI). Specifically:
    /// <list type="bullet">
    /// <item><see cref="JointLimitModel.Lower"/> / <see cref="JointLimitModel.Upper"/>
    /// are radians for revolute/continuous joints and meters for prismatic
    /// (slide) joints.</item>
    /// <item><see cref="Reference"/> (MJCF <c>ref</c>) is radians for hinge
    /// joints and meters for slide joints.</item>
    /// <item><see cref="Damping"/> is radian-based for hinge joints
    /// (N*m*s/rad) and meter-based for slide joints (N*s/m).
    /// <see cref="Friction"/> follows the same per-type unit.</item>
    /// </list>
    /// Conversions from the degree/RPY input model happen exclusively at the
    /// <c>KinematicTreeAdapter</c> boundary; writers consume these values
    /// as-is.
    /// </remarks>
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
        bool AutoComputeLimits = false,
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

    /// <remarks>
    /// <see cref="Lower"/> / <see cref="Upper"/> are radians for
    /// revolute/continuous joints and meters for prismatic (slide) joints.
    /// <see cref="Velocity"/> is rad/s or m/s respectively. <see cref="Effort"/>
    /// is N*m or N.
    /// </remarks>
    public sealed record JointLimitModel(
        double? Lower,
        double? Upper,
        double? Effort,
        double? Velocity);

    /// <summary>
    /// A rigid pose in the canonical model: position in METERS and rotation
    /// as a unit quaternion (w, x, y, z). Storing rotation as a quaternion
    /// keeps the canonical representation free of any Euler-sequence ambiguity;
    /// writers convert to their own convention (URDF rpy, MJCF quat/euler/axisangle)
    /// at emit time.
    /// </summary>
    public sealed record PoseModel(
        Vector3Model Position,
        QuaternionModel Rotation);

    public sealed record Vector3Model(
        double X,
        double Y,
        double Z);

    /// <summary>
    /// Unit quaternion in (w, x, y, z) order - the canonical rotation
    /// representation for <see cref="PoseModel"/>. Matches the MuJoCo
    /// quaternion ordering; <c>MathOps.RPYToQuaternion</c> /
    /// <c>QuaternionToRPY</c> bridge it to URDF roll-pitch-yaw.
    /// </summary>
    public sealed record QuaternionModel(
        double W,
        double X,
        double Y,
        double Z)
    {
        public static QuaternionModel Identity => new QuaternionModel(1, 0, 0, 0);
    }

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
    /// persistent ID is base64-ready so the JSON configuration layer can store
    /// it without binding to SolidWorks.Interop.sldworks.Component2.
    ///
    /// <see cref="DisplayName"/> (the component instance Name2) and
    /// <see cref="Path"/> (its document path) are persisted alongside the
    /// persistent ID so that a stale reference - e.g. after a PDM pull
    /// invalidates the persist reference even though the component still exists
    /// in the assembly - can be re-bound by name/path on load instead of being
    /// silently dropped. <see cref="Path"/> defaults to null so configs written
    /// before this field existed still deserialize.
    /// </summary>
    public sealed record ComponentReferenceModel(
        string DisplayName,
        byte[] PersistentId,
        string Path = null);

    public enum InertialSourceModel
    {
        Visual = 0,
        Collision = 1,
        Custom = 2,
    }
}
