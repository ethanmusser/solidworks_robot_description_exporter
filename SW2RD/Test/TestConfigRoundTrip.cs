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

using SW2RD.Configuration;
using SW2RD.Core;
using SW2RD.Export;
using SW2RD.URDF;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace SW2RD.Test
{
    // SW-less coverage for the Config JSON persistence path. These tests
    // protect the System.Text.Json record-deserialization invariant: every
    // Config / KinematicTree / LinkModel / JointModel record
    // must be reconstructible from its own JSON output. A regression here
    // breaks configuration save/load on every assembly that uses the
    // exporter.
    public class TestConfigRoundTrip
    {
        [Fact]
        public void TestRoundTripPreservesScalarFields()
        {
            Config original = SampleConfig();
            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            Assert.Equal(Config.CurrentSchemaVersion, read.SchemaVersion);
            Assert.Equal(original.ExporterVersion, read.ExporterVersion);
            Assert.Equal(original.SavedAtUtc, read.SavedAtUtc);
            Assert.NotNull(read.Tree);
            Assert.Equal(original.Tree.Name, read.Tree.Name);
        }

        [Fact]
        public void TestRoundTripPreservesLinkAndJointShape()
        {
            Config original = SampleConfig();
            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            LinkModel originalRoot = FirstTopLevel(original.Tree);
            LinkModel readRoot = FirstTopLevel(read.Tree);

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
            Config original = SampleConfig();
            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            byte[] originalPid = FirstTopLevel(original.Tree).VisualGroups[0].Components[0].PersistentId;
            byte[] readPid = FirstTopLevel(read.Tree).VisualGroups[0].Components[0].PersistentId;

            Assert.NotNull(readPid);
            Assert.Equal(originalPid.Length, readPid.Length);
            Assert.Equal(originalPid, readPid);
        }

        [Fact]
        public void TestRoundTripPreservesNewJointPropertiesFields()
        {
            Config original = SampleConfig();
            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            JointModel originalJoint = FirstTopLevel(original.Tree).Children[0].Joint;
            JointModel readJoint = FirstTopLevel(read.Tree).Children[0].Joint;

            Assert.Equal(originalJoint.AutoComputeLimits, readJoint.AutoComputeLimits);
            Assert.Equal(originalJoint.Damping, readJoint.Damping);
            Assert.Equal(originalJoint.Friction, readJoint.Friction);
            Assert.Equal(originalJoint.Armature, readJoint.Armature);
            Assert.Equal(originalJoint.Reference, readJoint.Reference);
        }

        [Fact]
        public void TestAdapterPreservesUnsetJointLimitFields()
        {
            JointModel childJoint = new JointModel(
                "joint1", "revolute", "base_link", "child_link",
                new PoseModel(new Vector3Model(0, 0, 0), new RpyModel(0, 0, 0)),
                new Vector3Model(0, 0, 1),
                new JointLimitModel(null, 1.25, null, 2.5),
                "joint_coordsys", "joint_axis", false,
                AutoComputeLimits: false);

            LinkModel child = new LinkModel(
                "child_link", null, null,
                Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(),
                false, InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                childJoint, Array.Empty<LinkModel>());

            LinkModel root = new LinkModel(
                "base_link", null, null,
                Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(),
                false, InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                null, new[] { child });

            SW2RD.URDF.Link legacyRoot = KinematicTreeAdapter.ToLegacyLink(root, null);
            SW2RD.URDF.Joint legacyJoint = legacyRoot.Children[0].Joint;

            Assert.Null(legacyJoint.Limit.LowerOrNull);
            Assert.Equal(1.25, legacyJoint.Limit.UpperOrNull);
            Assert.Null(legacyJoint.Limit.EffortOrNull);
            Assert.Equal(2.5, legacyJoint.Limit.VelocityOrNull);

            JointModel roundTrippedJoint = KinematicTreeAdapter.ToCore(legacyRoot).Children[0].Joint;
            Assert.Null(roundTrippedJoint.Limit.Lower);
            Assert.Equal(1.25, roundTrippedJoint.Limit.Upper);
            Assert.Null(roundTrippedJoint.Limit.Effort);
            Assert.Equal(2.5, roundTrippedJoint.Limit.Velocity);
        }

        [Fact]
        public void TestLimitSetValuesKeepsBlankEffortAndVelocityUnset()
        {
            Limit limit = new Limit();

            limit.SetValues(
                new TextBox { Text = "-90" },
                new TextBox { Text = "90" },
                new TextBox { Text = "" },
                new TextBox { Text = "" });

            Assert.Equal(-90.0, limit.LowerOrNull);
            Assert.Equal(90.0, limit.UpperOrNull);
            Assert.Null(limit.EffortOrNull);
            Assert.Null(limit.VelocityOrNull);

            limit.SetValues(
                new TextBox { Text = "-45" },
                new TextBox { Text = "45" },
                new TextBox { Text = "12" },
                new TextBox { Text = "180" });

            Assert.Equal(-45.0, limit.LowerOrNull);
            Assert.Equal(45.0, limit.UpperOrNull);
            Assert.Equal(12.0, limit.EffortOrNull);
            Assert.Equal(180.0, limit.VelocityOrNull);
        }

        [Fact]
        public void TestLinkNodeConfigRoundTripPreservesJointProperties()
        {
            SW2RD.URDF.Link baseLink = new SW2RD.URDF.Link(null) { Name = "base_link" };
            SW2RD.URDF.Link child = new SW2RD.URDF.Link(baseLink) { Name = "child_link" };
            child.Joint.Name = "joint1";
            child.Joint.Type = "revolute";
            child.Joint.Limit.SetLower(-90.0);
            child.Joint.Limit.SetUpper(90.0);
            child.Joint.Limit.SetEffort(12.0);
            child.Joint.Limit.SetVelocity(180.0);
            child.Joint.Dynamics.SetDamping(0.4);
            child.Joint.Dynamics.SetFriction(0.2);
            child.Joint.Armature = 0.01;
            child.Joint.Reference = 15.0;
            child.Joint.AutoComputeLimits = false;
            baseLink.Children.Add(child);

            LinkNode node = new LinkNode(baseLink);
            Config config = ConfigBridge.CreateFromLinkNode(node, "test_robot");
            LinkNode read = ConfigBridge.CreateLinkNode(config);
            LinkNode readBase = (LinkNode)read.Nodes[0];
            LinkNode readChild = (LinkNode)readBase.Nodes[0];
            Joint joint = readChild.Link.Joint;

            Assert.Equal(-90.0, joint.Limit.LowerOrNull);
            Assert.Equal(90.0, joint.Limit.UpperOrNull);
            Assert.Equal(12.0, joint.Limit.EffortOrNull);
            Assert.Equal(180.0, joint.Limit.VelocityOrNull);
            Assert.Equal(0.4, joint.Dynamics.DampingOrNull);
            Assert.Equal(0.2, joint.Dynamics.FrictionOrNull);
            Assert.Equal(0.01, joint.Armature);
            Assert.Equal(15.0, joint.Reference);
            Assert.False(joint.AutoComputeLimits);
        }

        [Fact]
        public void TestLinkNodeConfigRoundTripPreservesBlankEffortAndVelocity()
        {
            SW2RD.URDF.Link baseLink = new SW2RD.URDF.Link(null) { Name = "base_link" };
            SW2RD.URDF.Link child = new SW2RD.URDF.Link(baseLink) { Name = "child_link" };
            child.Joint.Name = "joint1";
            child.Joint.Type = "revolute";
            child.Joint.Limit.SetLower(-90.0);
            child.Joint.Limit.SetUpper(90.0);
            child.Joint.Limit.SetEffort(null);
            child.Joint.Limit.SetVelocity(null);
            baseLink.Children.Add(child);

            LinkNode node = new LinkNode(baseLink);
            Config config = ConfigBridge.CreateFromLinkNode(node, "test_robot");
            LinkNode read = ConfigBridge.CreateLinkNode(config);
            LinkNode readBase = (LinkNode)read.Nodes[0];
            LinkNode readChild = (LinkNode)readBase.Nodes[0];
            Joint joint = readChild.Link.Joint;

            Assert.Equal(-90.0, joint.Limit.LowerOrNull);
            Assert.Equal(90.0, joint.Limit.UpperOrNull);
            Assert.Null(joint.Limit.EffortOrNull);
            Assert.Null(joint.Limit.VelocityOrNull);
        }

        [Fact]
        public void TestRoundTripPreservesAutoDeriveAxisField()
        {
            Config original = SampleConfigWithAutoDeriveAxis(autoDerive: true);
            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            JointModel originalJoint = FirstTopLevel(original.Tree).Children[0].Joint;
            JointModel readJoint = FirstTopLevel(read.Tree).Children[0].Joint;

            Assert.True(originalJoint.AutoDeriveAxis);
            Assert.True(readJoint.AutoDeriveAxis);
        }

        [Fact]
        public void TestLegacyAxisNameAutomaticallyGenerateMigrates()
        {
            // Config documents written before AutoDeriveAxis existed
            // stored the literal "Automatically Generate" sentinel in
            // AxisName. KinematicTreeAdapter.ApplyJoint translates that
            // onto the new boolean. This test exercises the end-to-end
            // Config -> Joint adapter path so the migration runs once
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

            SW2RD.URDF.Link legacyRoot = SW2RD.Export.KinematicTreeAdapter.ToLegacyLink(root, null);
            SW2RD.URDF.Link legacyChild = legacyRoot.Children[0];

            Assert.True(legacyChild.Joint.AutoDeriveAxis);
            Assert.Equal(string.Empty, legacyChild.Joint.AxisName);
        }

        [Fact]
        public void TestRoundTripDefaultsAutoComputeLimitsTrueWhenMissing()
        {
            // Older Config documents (pre-Joint-properties feature) do
            // not carry the AutoComputeLimits field. The JointModel
            // record default is `true`; System.Text.Json must respect
            // that when the JSON field is missing, otherwise old configs
            // would silently flip to "manual limits" on first reload.
            string json = "{ \"SchemaVersion\": " + Config.CurrentSchemaVersion + ", " +
                "\"ExporterVersion\": \"\", \"SavedAtUtc\": \"2024-01-01T00:00:00Z\", " +
                "\"Tree\": { \"Name\": \"x\", " +
                "\"GlobalOriginCoordinateSystemName\": \"\", " +
                "\"WorldBody\": { \"Name\": \"world\", \"Inertial\": null, \"Material\": null, " +
                "\"VisualGroups\": [], \"CollisionGroups\": [], \"CollisionUsesVisual\": false, " +
                "\"InertialSource\": 0, \"InertialComponents\": [], \"Sites\": [], " +
                "\"Joint\": null, \"Children\": [{ " +
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
                "\"Children\": [] }] }] } } }";

            Config read = ConfigJsonSerializer.Deserialize(json);
            JointModel joint = FirstTopLevel(read.Tree).Children[0].Joint;
            Assert.True(joint.AutoComputeLimits);
            Assert.Null(joint.Damping);
            Assert.Null(joint.Friction);
            Assert.Null(joint.Armature);
            Assert.Null(joint.Reference);
        }

        [Fact]
        public void TestRoundTripPreservesSitesAndInertialFields()
        {
            Config original = SampleConfig();
            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            LinkModel originalChild = FirstTopLevel(original.Tree).Children[0];
            LinkModel readChild = FirstTopLevel(read.Tree).Children[0];

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
            Config stale = new Config(99, "1.0", DateTime.UtcNow,
                new KinematicTree("x",
                    "",
                    WorldBody(
                        new LinkModel("base", null, null,
                            Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(), false,
                            InertialSourceModel.Visual,
                            Array.Empty<ComponentReferenceModel>(),
                            Array.Empty<SiteModel>(), null, Array.Empty<LinkModel>()))));
            string json = JsonSerializer.Serialize(stale, new JsonSerializerOptions { WriteIndented = true });

            Assert.Throws<NotSupportedException>(() => ConfigJsonSerializer.Deserialize(json));
        }

        [Fact]
        public void TestSerializerProducesIndentedJson()
        {
            Config config = SampleConfig();
            string json = ConfigJsonSerializer.Serialize(config);

            Assert.Contains("\n", json);
            Assert.True(ConfigJsonSerializer.LooksLikeJson(json));
        }

        [Fact]
        public void TestRoundTripPreservesWorldGlobalOriginAndAttachment()
        {
            // Verifies the new WorldBody / TopLevelBodies shape: the world's
            // global-origin coord-sys and each top-level body's
            // WorldAttachment must round-trip across JSON.
            Config original = SampleConfig();
            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            Assert.NotNull(read.Tree.WorldBody);
            Assert.Equal(
                original.Tree.GlobalOriginCoordinateSystemName,
                read.Tree.GlobalOriginCoordinateSystemName);
            Assert.Equal(original.Tree.TopLevelBodies.Count, read.Tree.TopLevelBodies.Count);
            for (int i = 0; i < original.Tree.TopLevelBodies.Count; i++)
            {
                Assert.Equal(
                    original.Tree.TopLevelBodies[i].WorldAttachment,
                    read.Tree.TopLevelBodies[i].WorldAttachment);
            }
        }

        [Fact]
        public void TestRoundTripPreservesMultipleTopLevelBodiesAndFreeAttachment()
        {
            // Two top-level bodies, the second is Free. Verifies multi-tree
            // round-trip and WorldAttachment.Free preservation.
            LinkModel root1 = SimpleLink("base_a");
            LinkModel root2 = SimpleLink("base_b") with
            {
                WorldAttachment = WorldAttachmentModel.Free,
            };
            KinematicTree tree = new KinematicTree(
                "multi_tree",
                "Origin_global",
                WorldBody(root1, root2));
            Config original = new Config(
                Config.CurrentSchemaVersion, "1.6.1-test",
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                tree);

            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            Assert.Equal(2, read.Tree.TopLevelBodies.Count);
            Assert.Equal("base_a", read.Tree.TopLevelBodies[0].Name);
            Assert.Equal("base_b", read.Tree.TopLevelBodies[1].Name);
            Assert.Equal(WorldAttachmentModel.Welded, read.Tree.TopLevelBodies[0].WorldAttachment);
            Assert.Equal(WorldAttachmentModel.Free, read.Tree.TopLevelBodies[1].WorldAttachment);
            Assert.Equal("Origin_global", read.Tree.GlobalOriginCoordinateSystemName);
        }

        [Fact]
        public void TestRoundTripPreservesWorldLevelGeometryAndSites()
        {
            // A world with one visual mesh group and one site must
            // round-trip through Config JSON without losing either.
            ComponentReferenceModel comp = new ComponentReferenceModel(
                "ground-1", new byte[] { 0xAA });
            MeshGroupModel worldVisual = new MeshGroupModel(
                "world_visual", "world_visual.STL", new[] { comp });
            SiteModel worldSite = new SiteModel("origin_marker", "Origin_global", null);
            LinkModel world = WorldBody(SimpleLink("base")) with
            {
                Material = new MaterialModel("", new RgbaModel(0.5, 0.5, 0.5, 1), ""),
                VisualGroups = new[] { worldVisual },
                Sites = new[] { worldSite },
            };

            KinematicTree tree = new KinematicTree(
                "world_geom_test", "Origin_global", world);
            Config original = new Config(
                Config.CurrentSchemaVersion, "1.6.1-test", DateTime.UtcNow, tree);

            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            Assert.NotNull(read.Tree.WorldBody);
            Assert.Equal(1, read.Tree.WorldBody.VisualGroups.Count);
            Assert.Equal("world_visual", read.Tree.WorldBody.VisualGroups[0].Name);
            Assert.Equal(1, read.Tree.WorldBody.VisualGroups[0].Components.Count);
            Assert.Equal(1, read.Tree.WorldBody.Sites.Count);
            Assert.Equal("origin_marker", read.Tree.WorldBody.Sites[0].Name);
        }

        [Fact]
        public void TestRoundTripPreservesWorldBodyCollisionUsesVisual()
        {
            LinkModel world = WorldBody(SimpleLink("base")) with
            {
                CollisionUsesVisual = true,
            };
            KinematicTree tree = new KinematicTree("world_collision", "Origin_global", world);
            Config original = new Config(
                Config.CurrentSchemaVersion, "1.6.1-test", DateTime.UtcNow, tree);

            string json = ConfigJsonSerializer.Serialize(original);
            Config read = ConfigJsonSerializer.Deserialize(json);

            Assert.True(read.Tree.WorldBody.CollisionUsesVisual);
        }

        // Convenience accessor used throughout the tests. All sample configs
        // have exactly one top-level body, so this is the single-tree
        // analogue of the old `tree.BaseLink`.
        private static LinkModel FirstTopLevel(KinematicTree tree)
        {
            Assert.NotEmpty(tree.TopLevelBodies);
            return tree.TopLevelBodies[0];
        }

        private static LinkModel SimpleLink(string name)
        {
            return new LinkModel(
                name, null, null,
                Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(),
                false, InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                null, Array.Empty<LinkModel>());
        }

        private static LinkModel WorldBody(params LinkModel[] topLevelBodies)
        {
            return new LinkModel(
                "world",
                null,
                new MaterialModel("", new RgbaModel(1, 1, 1, 1), ""),
                Array.Empty<MeshGroupModel>(),
                Array.Empty<MeshGroupModel>(),
                false,
                InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                null,
                topLevelBodies);
        }

        // Variant of SampleConfig that flips the AutoDeriveAxis bit on
        // the child joint so the round-trip test can verify the JSON
        // record-deserialization picks the value up off the wire.
        private static Config SampleConfigWithAutoDeriveAxis(bool autoDerive)
        {
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
            return new Config(
                Config.CurrentSchemaVersion, "1.0", DateTime.UtcNow,
                new KinematicTree("autoderive_robot",
                    "",
                    WorldBody(root)));
        }

        private static Config SampleConfig()
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

            KinematicTree tree = new KinematicTree(
                "test_robot",
                "Origin_global",
                WorldBody(root));
            return new Config(
                Config.CurrentSchemaVersion,
                "1.6.1-test",
                new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                tree);
        }
    }
}
