using SW2URDF.Configuration;
using SW2URDF.Core;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace SW2URDF.Test
{
    // SW-less coverage for the ConfigV2 JSON persistence path. These tests
    // protect the System.Text.Json record-deserialization invariant: every
    // ConfigV2 / KinematicTree / LinkModel / JointModel record must be
    // reconstructible from its own JSON output. A regression here breaks
    // configuration save/load on every assembly that uses the exporter.
    public class TestConfigV2RoundTrip
    {
        [Fact]
        public void TestRoundTripPreservesScalarFields()
        {
            ConfigV2 original = SampleConfig();
            string json = ConfigV2JsonSerializer.Serialize(original);
            ConfigV2 read = ConfigV2JsonSerializer.Deserialize(json);

            Assert.Equal(ConfigV2.CurrentSchemaVersion, read.SchemaVersion);
            Assert.Equal(original.ExporterVersion, read.ExporterVersion);
            Assert.Equal(original.SavedAtUtc, read.SavedAtUtc);
            Assert.NotNull(read.Tree);
            Assert.Equal(original.Tree.Name, read.Tree.Name);
        }

        [Fact]
        public void TestRoundTripPreservesLinkAndJointShape()
        {
            ConfigV2 original = SampleConfig();
            string json = ConfigV2JsonSerializer.Serialize(original);
            ConfigV2 read = ConfigV2JsonSerializer.Deserialize(json);

            LinkModel originalRoot = original.Tree.BaseLink;
            LinkModel readRoot = read.Tree.BaseLink;

            Assert.Equal(originalRoot.Name, readRoot.Name);
            Assert.Equal(originalRoot.Children.Count, readRoot.Children.Count);
            Assert.Equal(originalRoot.VisualGroups.Count, readRoot.VisualGroups.Count);
            Assert.Equal(originalRoot.VisualGroups[0].Components.Count,
                readRoot.VisualGroups[0].Components.Count);

            LinkModel originalChild = originalRoot.Children[0];
            LinkModel readChild = readRoot.Children[0];
            Assert.Equal(originalChild.Name, readChild.Name);
            Assert.NotNull(readChild.Joint);
            Assert.Equal(originalChild.Joint.Name, readChild.Joint.Name);
            Assert.Equal(originalChild.Joint.Type, readChild.Joint.Type);
            Assert.Equal(originalChild.Joint.AxisFlipped, readChild.Joint.AxisFlipped);
            Assert.Equal(originalChild.Joint.Origin.Position.X,
                readChild.Joint.Origin.Position.X);
            Assert.Equal(originalChild.Joint.Axis.Z, readChild.Joint.Axis.Z);
        }

        [Fact]
        public void TestRoundTripPreservesPersistentIdBytes()
        {
            ConfigV2 original = SampleConfig();
            string json = ConfigV2JsonSerializer.Serialize(original);
            ConfigV2 read = ConfigV2JsonSerializer.Deserialize(json);

            byte[] originalPid = original.Tree.BaseLink.VisualGroups[0].Components[0].PersistentId;
            byte[] readPid = read.Tree.BaseLink.VisualGroups[0].Components[0].PersistentId;

            Assert.NotNull(readPid);
            Assert.Equal(originalPid.Length, readPid.Length);
            Assert.Equal(originalPid, readPid);
        }

        [Fact]
        public void TestRoundTripPreservesNewJointPropertiesFields()
        {
            // SampleConfig populates all of: AutoComputeLimits=false,
            // Damping, Friction, Armature, Reference. Verifies that the
            // System.Text.Json record-deserialization picks up the new
            // ctor parameters (added with default values for backward
            // compat) when present in the JSON.
            ConfigV2 original = SampleConfig();
            string json = ConfigV2JsonSerializer.Serialize(original);
            ConfigV2 read = ConfigV2JsonSerializer.Deserialize(json);

            JointModel originalJoint = original.Tree.BaseLink.Children[0].Joint;
            JointModel readJoint = read.Tree.BaseLink.Children[0].Joint;

            Assert.Equal(originalJoint.AutoComputeLimits, readJoint.AutoComputeLimits);
            Assert.Equal(originalJoint.Damping, readJoint.Damping);
            Assert.Equal(originalJoint.Friction, readJoint.Friction);
            Assert.Equal(originalJoint.Armature, readJoint.Armature);
            Assert.Equal(originalJoint.Reference, readJoint.Reference);
        }

        [Fact]
        public void TestRoundTripPreservesAutoDeriveAxisField()
        {
            // SampleConfig sets AutoDeriveAxis = false by default.
            // Build a parallel config with AutoDeriveAxis = true and
            // verify the JSON round-trip preserves the bit.
            ConfigV2 original = SampleConfigWithAutoDeriveAxis(autoDerive: true);
            string json = ConfigV2JsonSerializer.Serialize(original);
            ConfigV2 read = ConfigV2JsonSerializer.Deserialize(json);

            JointModel originalJoint = original.Tree.BaseLink.Children[0].Joint;
            JointModel readJoint = read.Tree.BaseLink.Children[0].Joint;

            Assert.True(originalJoint.AutoDeriveAxis);
            Assert.True(readJoint.AutoDeriveAxis);
        }

        [Fact]
        public void TestLegacyAxisNameAutomaticallyGenerateMigrates()
        {
            // ConfigV2 documents written before AutoDeriveAxis existed
            // stored the literal "Automatically Generate" sentinel in
            // AxisName. KinematicTreeAdapter.ApplyJoint translates that
            // onto the new boolean. This test exercises the end-to-end
            // ConfigV2 -> Joint adapter path so the migration runs once
            // even when the JSON layer's record default for
            // AutoDeriveAxis is false.
            JointModel legacyAxis = new JointModel(
                "joint1", "revolute", "base_link", "child_link",
                new PoseModel(new Vector3Model(0, 0, 0), new RpyModel(0, 0, 0)),
                new Vector3Model(0, 0, 1),
                Limit: null,
                CoordinateSystemName: "joint_coordsys",
                AxisName: "Automatically Generate",
                AxisFlipped: false);

            SW2URDF.URDF.Joint adapted = new SW2URDF.URDF.Joint();
            // Use reflection: ApplyJoint is private. Instead, we invoke
            // the public ToLegacyLink on a synthetic LinkModel that
            // wraps the legacy joint so the adapter pipeline runs.
            LinkModel root = new LinkModel(
                "base_link", null, null,
                Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(),
                false, InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                null,
                new[]
                {
                    new LinkModel(
                        "child_link", null, null,
                        Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(),
                        false, InertialSourceModel.Visual,
                        Array.Empty<ComponentReferenceModel>(),
                        Array.Empty<SiteModel>(),
                        legacyAxis,
                        Array.Empty<LinkModel>())
                });

            SW2URDF.URDF.Link legacyRoot = SW2URDF.URDFExport.KinematicTreeAdapter.ToLegacyLink(root, null);
            SW2URDF.URDF.Link legacyChild = legacyRoot.Children[0];

            Assert.True(legacyChild.Joint.AutoDeriveAxis);
            Assert.Equal(string.Empty, legacyChild.Joint.AxisName);
            // Quiet unused-local warning.
            Assert.NotNull(adapted);
        }

        [Fact]
        public void TestRoundTripDefaultsAutoComputeLimitsTrueWhenMissing()
        {
            // Older ConfigV2 documents (pre-Joint-properties feature) do
            // not carry the AutoComputeLimits field. The JointModel
            // record default is `true`; System.Text.Json must respect
            // that when the JSON field is missing, otherwise old configs
            // would silently flip to "manual limits" on first reload.
            string json = "{ \"SchemaVersion\": " + ConfigV2.CurrentSchemaVersion + ", " +
                "\"ExporterVersion\": \"\", \"SavedAtUtc\": \"2024-01-01T00:00:00Z\", " +
                "\"Tree\": { \"Name\": \"x\", \"BaseLink\": { " +
                "\"Name\": \"base\", \"Inertial\": null, \"Material\": null, " +
                "\"VisualGroups\": [], \"CollisionGroups\": [], \"CollisionUsesVisual\": false, " +
                "\"InertialSource\": 0, \"InertialComponents\": [], \"Sites\": [], " +
                "\"Joint\": null, \"Children\": [{ " +
                "\"Name\": \"link1\", \"Inertial\": null, \"Material\": null, " +
                "\"VisualGroups\": [], \"CollisionGroups\": [], \"CollisionUsesVisual\": false, " +
                "\"InertialSource\": 0, \"InertialComponents\": [], \"Sites\": [], " +
                "\"Joint\": { \"Name\": \"j1\", \"Type\": \"revolute\", " +
                "\"ParentLinkName\": \"base\", \"ChildLinkName\": \"link1\", " +
                "\"Origin\": { \"Position\": { \"X\": 0, \"Y\": 0, \"Z\": 0 }, " +
                "\"Rotation\": { \"Roll\": 0, \"Pitch\": 0, \"Yaw\": 0 } }, " +
                "\"Axis\": { \"X\": 0, \"Y\": 0, \"Z\": 1 }, \"Limit\": null, " +
                "\"CoordinateSystemName\": \"\", \"AxisName\": \"\", \"AxisFlipped\": false }, " +
                "\"Children\": [] }] } } }";

            ConfigV2 read = ConfigV2JsonSerializer.Deserialize(json);
            JointModel joint = read.Tree.BaseLink.Children[0].Joint;
            Assert.True(joint.AutoComputeLimits);
            Assert.Null(joint.Damping);
            Assert.Null(joint.Friction);
            Assert.Null(joint.Armature);
            Assert.Null(joint.Reference);
        }

        [Fact]
        public void TestRoundTripPreservesSitesAndInertialFields()
        {
            ConfigV2 original = SampleConfig();
            string json = ConfigV2JsonSerializer.Serialize(original);
            ConfigV2 read = ConfigV2JsonSerializer.Deserialize(json);

            LinkModel originalChild = original.Tree.BaseLink.Children[0];
            LinkModel readChild = read.Tree.BaseLink.Children[0];

            Assert.Equal(originalChild.InertialSource, readChild.InertialSource);
            Assert.Equal(originalChild.CollisionUsesVisual, readChild.CollisionUsesVisual);
            Assert.Equal(originalChild.Sites.Count, readChild.Sites.Count);
            Assert.Equal(originalChild.Sites[0].Name, readChild.Sites[0].Name);
            Assert.Equal(originalChild.Sites[0].CoordinateSystemName,
                readChild.Sites[0].CoordinateSystemName);
            Assert.Equal(originalChild.Inertial.Mass, readChild.Inertial.Mass);
            Assert.Equal(originalChild.Inertial.Inertia.Ixx, readChild.Inertial.Inertia.Ixx);
        }

        [Fact]
        public void TestDeserializeRejectsUnsupportedSchemaVersion()
        {
            ConfigV2 stale = new ConfigV2(99, "1.0", DateTime.UtcNow,
                new KinematicTree("x",
                    new LinkModel("base", null, null,
                        Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(), false,
                        InertialSourceModel.Visual,
                        Array.Empty<ComponentReferenceModel>(),
                        Array.Empty<SiteModel>(), null, Array.Empty<LinkModel>())));
            string json = JsonSerializer.Serialize(stale, new JsonSerializerOptions { WriteIndented = true });

            Assert.Throws<NotSupportedException>(() => ConfigV2JsonSerializer.Deserialize(json));
        }

        [Fact]
        public void TestSerializerProducesIndentedJson()
        {
            ConfigV2 config = SampleConfig();
            string json = ConfigV2JsonSerializer.Serialize(config);

            Assert.Contains("\n", json);
            Assert.True(ConfigV2JsonSerializer.LooksLikeJson(json));
        }

        // Variant of SampleConfig that flips the AutoDeriveAxis bit on
        // the child joint so the round-trip test can verify the JSON
        // record-deserialization picks the value up off the wire.
        private static ConfigV2 SampleConfigWithAutoDeriveAxis(bool autoDerive)
        {
            ComponentReferenceModel comp = new ComponentReferenceModel(
                "part1-1", new byte[] { 0x01 });
            JointModel childJoint = new JointModel(
                "joint1", "revolute", "base_link", "child_link",
                new PoseModel(new Vector3Model(0, 0, 0), new RpyModel(0, 0, 0)),
                new Vector3Model(0, 0, 1),
                Limit: null,
                CoordinateSystemName: "joint_coordsys",
                AxisName: autoDerive ? "" : "joint_axis",
                AxisFlipped: false,
                AutoDeriveAxis: autoDerive);
            LinkModel child = new LinkModel(
                "child_link", null, null,
                Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(), false,
                InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                childJoint, Array.Empty<LinkModel>());
            LinkModel root = new LinkModel(
                "base_link", null, null,
                Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(), false,
                InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                null, new[] { child });
            return new ConfigV2(
                ConfigV2.CurrentSchemaVersion, "1.0", DateTime.UtcNow,
                new KinematicTree("autoderive_robot", root));
        }

        private static ConfigV2 SampleConfig()
        {
            ComponentReferenceModel comp = new ComponentReferenceModel(
                "part1-1", new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            MeshGroupModel visualGroup = new MeshGroupModel(
                "base_visual", "base_visual.STL", new[] { comp });
            MeshGroupModel collisionGroup = new MeshGroupModel(
                "base_collision", "base_collision.STL", new[] { comp });

            JointModel childJoint = new JointModel(
                "joint1", "revolute", "base_link", "child_link",
                new PoseModel(new Vector3Model(0.1, 0.2, 0.3),
                    new RpyModel(0.4, 0.5, 0.6)),
                new Vector3Model(0, 0, 1),
                new JointLimitModel(-3.14, 3.14, 100.0, 1.0),
                "joint_coordsys", "joint_axis", true,
                AutoComputeLimits: false,
                Damping: 0.1,
                Friction: 0.05,
                Armature: 1.5e-4,
                Reference: 0.7);

            InertialModel childInertial = new InertialModel(
                new PoseModel(new Vector3Model(0.01, 0.02, 0.03),
                    new RpyModel(0, 0, 0)),
                1.5,
                new InertiaTensorModel(0.01, 0.001, 0.002, 0.02, 0.003, 0.03));

            LinkModel child = new LinkModel(
                "child_link", childInertial,
                new MaterialModel("blue", new RgbaModel(0, 0, 1, 1), ""),
                new[] { visualGroup }, new[] { collisionGroup }, false,
                InertialSourceModel.Custom,
                new[] { comp },
                new[] { new SiteModel("site_a", "lcs_link1", null) },
                childJoint, Array.Empty<LinkModel>());

            LinkModel root = new LinkModel(
                "base_link",
                new InertialModel(
                    new PoseModel(new Vector3Model(0, 0, 0), new RpyModel(0, 0, 0)),
                    0.5,
                    new InertiaTensorModel(0.001, 0, 0, 0.001, 0, 0.001)),
                new MaterialModel("", new RgbaModel(1, 1, 1, 1), ""),
                new[] { visualGroup }, Array.Empty<MeshGroupModel>(), false,
                InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                null,
                new[] { child });

            KinematicTree tree = new KinematicTree("test_robot", root);
            return new ConfigV2(
                ConfigV2.CurrentSchemaVersion,
                "1.6.1-test",
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                tree);
        }
    }
}
