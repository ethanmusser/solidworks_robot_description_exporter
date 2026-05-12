using SW2RD.Configuration;
using SW2RD.Core;
using SW2RD.URDF;
using SW2RD.Export;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Xunit;

namespace SW2RD.Test
{
    // SW-less coverage for the legacy-config -> SW2RD v1 (World +
    // TopLevelBodies) migration paths. These guard the contract that older
    // configurations
    // continue to load into the new shape with the world-frame inheriting
    // from the legacy base link's coord-sys, the base link demoted to a
    // single Welded top-level body, and world-level visual / collision /
    // sites empty.
    public class TestLegacyToConfigMigration
    {
        [Fact]
        public void TestV15DataContractMigratesIntoWorldNodeRootedTree()
        {
            // Build a minimal legacy v1.5 DataContract XML, write it to a
            // string, and read it back through LegacyConfigV15DataContractReader.
            // Asserts the WorldNode wrapping behavior end-to-end.
            Link baseLink = new Link
            {
                Name = "base_link",
            };
            baseLink.Joint.CoordinateSystemName = "Origin_global";

            string xml = SerializeWithDataContract(baseLink);

            LinkNode root = LegacyConfigV15DataContractReader.ReadBaseNode(xml);

            WorldNode world = Assert.IsType<WorldNode>(root);
            Assert.Equal("Origin_global", world.GlobalOriginCoordinateSystemName);
            Assert.Equal(1, world.Nodes.Count);

            LinkNode topLevel = (LinkNode)world.Nodes[0];
            Assert.Equal("base_link", topLevel.Link.Name);
            Assert.Equal(WorldAttachmentModel.Welded, topLevel.Link.WorldAttachment);
        }

        [Fact]
        public void TestLegacyMigrationProducesEmptyWorldGeometryAndSites()
        {
            // The migrated WorldNode must NOT inherit any geometry from
            // the legacy base link - geometry stays on the body, the
            // world starts with empty visual / collision / sites.
            Link baseLink = new Link { Name = "base_link" };
            baseLink.Joint.CoordinateSystemName = "Origin_global";
            string xml = SerializeWithDataContract(baseLink);

            LinkNode root = LegacyConfigV15DataContractReader.ReadBaseNode(xml);
            WorldNode world = (WorldNode)root;

            // Direct properties on the WorldNode's Link surface the
            // worldbody-level geometry. They must be empty after
            // migration.
            Assert.True(world.Link.VisualGroups == null
                || world.Link.VisualGroups.Count == 0);
            Assert.True(world.Link.CollisionGroups == null
                || world.Link.CollisionGroups.Count == 0);
            Assert.True(world.Link.Sites == null
                || world.Link.Sites.Count == 0);
        }

        [Fact]
        public void TestLegacyMigrationRoundTripThroughConfig()
        {
            // After migration, saving + reloading via Config must
            // produce the same World + TopLevelBodies shape.
            Link baseLink = new Link { Name = "base_link" };
            baseLink.Joint.CoordinateSystemName = "Origin_global";
            string xml = SerializeWithDataContract(baseLink);

            LinkNode migrated = LegacyConfigV15DataContractReader.ReadBaseNode(xml);
            Config saved = ConfigBridge.CreateFromLinkNode(migrated, "robot");
            string json = ConfigJsonSerializer.Serialize(saved);
            Config reloaded = ConfigJsonSerializer.Deserialize(json);

            Assert.NotNull(reloaded.Tree.WorldBody);
            Assert.Equal(
                "Origin_global",
                reloaded.Tree.GlobalOriginCoordinateSystemName);
            Assert.Equal(1, reloaded.Tree.TopLevelBodies.Count);
            Assert.Equal("base_link", reloaded.Tree.TopLevelBodies[0].Name);
            Assert.Equal(
                WorldAttachmentModel.Welded,
                reloaded.Tree.TopLevelBodies[0].WorldAttachment);

            // World-level visual / collision / sites stay empty.
            Assert.Equal(0, reloaded.Tree.WorldBody.VisualGroups.Count);
            Assert.Equal(0, reloaded.Tree.WorldBody.CollisionGroups.Count);
            Assert.Equal(0, reloaded.Tree.WorldBody.Sites.Count);
        }

        [Fact]
        public void TestLegacyMigrationPreservesEmptyGlobalOriginName()
        {
            // A legacy config with no explicit global-origin coord-sys
            // (empty string) migrates to an empty world-body global
            // origin too - the export pipeline auto-generates
            // Origin_global at run time.
            Link baseLink = new Link { Name = "base_link" };
            baseLink.Joint.CoordinateSystemName = "";
            string xml = SerializeWithDataContract(baseLink);

            LinkNode root = LegacyConfigV15DataContractReader.ReadBaseNode(xml);
            WorldNode world = (WorldNode)root;
            Assert.Equal("", world.GlobalOriginCoordinateSystemName);
        }

        // Serializes a Link via the legacy DataContract path to mirror
        // exactly what the reader sees from a real SW custom attribute.
        private static string SerializeWithDataContract(Link link)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(Link));
                serializer.WriteObject(stream, link);
                return Encoding.ASCII.GetString(stream.ToArray());
            }
        }
    }
}
