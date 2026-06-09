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
using SW2RD.Export;
using SW2RD.MJCF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SW2RD.Test
{
    // SW-less coverage for the multi-tree / free-joint / worldbody-direct
    // geometry MJCF emission contracts. These tests exercise the
    // KinematicTree -> MJCFModel boundary directly so they are immune to
    // any SolidWorks-side changes; if one of these regresses the bug is
    // squarely inside MJCFBuilder / Body / MJCFModel.
    [Trait("Category", "SWFree")]
    public class TestMJCFMultiTree
    {
        [Fact]
        public void TestSingleTreeWeldedAtWorldEmitsSuppressedTransform()
        {
            // Single welded body whose own coord-sys matches the world's
            // global-origin name -> SuppressTransform = true (legacy
            // single-tree byte-identical contract).
            KinematicTree tree = new KinematicTree(
                "single_welded",
                "Origin_global",
                WorldBody(BodyWithCoordSys("base_link", "Origin_global")));

            string xml = WriteMjcf(MJCFBuilder.Build(tree, "../meshes/", new Dictionary<string, LinkAuxiliary>()));
            XDocument doc = XDocument.Parse(xml);

            XElement worldBody = doc.Descendants("worldbody").Single();
            XElement[] bodies = worldBody.Elements("body").ToArray();
            Assert.Single(bodies);
            Assert.Equal("base_link", bodies[0].Attribute("name").Value);
            Assert.Null(bodies[0].Attribute("pos"));
            Assert.Null(bodies[0].Attribute("quat"));
        }

        [Fact]
        public void TestMultiTreeEmitsAllTopLevelBodiesUnderWorldbody()
        {
            // Two top-level bodies under one world. Both should appear as
            // direct children of <worldbody> in MJCF.
            KinematicTree tree = new KinematicTree(
                "multi",
                "Origin_global",
                WorldBody(
                    BodyWithCoordSys("base_a", "Origin_global"),
                    BodyWithCoordSys("base_b", "Origin_global")));

            string xml = WriteMjcf(MJCFBuilder.Build(tree, "../meshes/", new Dictionary<string, LinkAuxiliary>()));
            XDocument doc = XDocument.Parse(xml);

            XElement worldBody = doc.Descendants("worldbody").Single();
            string[] names = worldBody.Elements("body")
                .Select(b => b.Attribute("name").Value)
                .ToArray();
            Assert.Equal(new[] { "base_a", "base_b" }, names);
        }

        [Fact]
        public void TestFreeAttachmentEmitsFreejoint()
        {
            // Top-level body with WorldAttachment.Free should emit
            // <freejoint/> as the first child of the body (after
            // <inertial> if present).
            LinkModel free = BodyWithCoordSys("free_body", "Origin_global") with
            {
                WorldAttachment = WorldAttachmentModel.Free,
            };
            KinematicTree tree = new KinematicTree(
                "free_test",
                "Origin_global",
                WorldBody(free));

            string xml = WriteMjcf(MJCFBuilder.Build(tree, "../meshes/", new Dictionary<string, LinkAuxiliary>()));
            XDocument doc = XDocument.Parse(xml);

            XElement worldBody = doc.Descendants("worldbody").Single();
            XElement body = worldBody.Element("body");
            Assert.NotNull(body);
            // Freejoint must be a direct child of the body.
            XElement freejoint = body.Element("freejoint");
            Assert.NotNull(freejoint);
        }

        [Fact]
        public void TestWeldedAttachmentDoesNotEmitFreejoint()
        {
            LinkModel welded = BodyWithCoordSys("welded_body", "Origin_global") with
            {
                WorldAttachment = WorldAttachmentModel.Welded,
            };
            KinematicTree tree = new KinematicTree(
                "welded_test",
                "Origin_global",
                WorldBody(welded));

            string xml = WriteMjcf(MJCFBuilder.Build(tree, "../meshes/", new Dictionary<string, LinkAuxiliary>()));
            XDocument doc = XDocument.Parse(xml);

            XElement body = doc.Descendants("worldbody").Single().Element("body");
            Assert.Null(body.Element("freejoint"));
        }

        [Fact]
        public void TestWorldOffsetIdentityWhenCoordSysNamesDiffer()
        {
            // When the body's coord-sys name differs from the world's, the
            // export pipeline is responsible for resolving the offset onto
            // the body's Joint.Origin. If the resolved origin is identity,
            // SuppressTransform should still be true (defensive numeric
            // check inside MJCFBuilder.IsWorldOffsetIdentity).
            //
            // Here we provide an explicit identity origin to verify the
            // numeric-zero short-circuit works.
            JointModel zeroOrigin = new JointModel(
                "", "", "", "",
                new PoseModel(new Vector3Model(0, 0, 0), TestRotations.Quat(0, 0, 0)),
                new Vector3Model(0, 0, 1),
                Limit: null,
                CoordinateSystemName: "different_name",
                AxisName: "",
                AxisFlipped: false);
            LinkModel body = SimpleLink("body").WithJoint(zeroOrigin);
            KinematicTree tree = new KinematicTree(
                "id_test",
                "Origin_global",
                WorldBody(body));

            string xml = WriteMjcf(MJCFBuilder.Build(tree, "../meshes/", new Dictionary<string, LinkAuxiliary>()));
            XDocument doc = XDocument.Parse(xml);
            XElement b = doc.Descendants("worldbody").Single().Element("body");
            Assert.Null(b.Attribute("pos"));
            Assert.Null(b.Attribute("quat"));
        }

        [Fact]
        public void TestNonIdentityWorldOffsetEmitsPosQuat()
        {
            // A body whose Joint.Origin is non-zero should emit pos/quat
            // attributes (SuppressTransform false).
            JointModel offsetJoint = new JointModel(
                "", "", "", "",
                new PoseModel(new Vector3Model(1.0, 2.0, 3.0), TestRotations.Quat(0, 0, 0)),
                new Vector3Model(0, 0, 1),
                Limit: null,
                CoordinateSystemName: "different_name",
                AxisName: "",
                AxisFlipped: false);
            LinkModel body = SimpleLink("body").WithJoint(offsetJoint);
            KinematicTree tree = new KinematicTree(
                "offset_test",
                "Origin_global",
                WorldBody(body));

            string xml = WriteMjcf(MJCFBuilder.Build(tree, "../meshes/", new Dictionary<string, LinkAuxiliary>()));
            XDocument doc = XDocument.Parse(xml);
            XElement b = doc.Descendants("worldbody").Single().Element("body");
            Assert.NotNull(b.Attribute("pos"));
            Assert.NotNull(b.Attribute("quat"));
            // pos rounds to "1 2 3"-shaped values (formatted as doubles).
            Assert.Contains("1", b.Attribute("pos").Value);
        }

        [Fact]
        public void TestWorldDirectVisualGeomEmittedAsWorldbodyChild()
        {
            // A world configured with a visual mesh should produce a
            // <geom> directly under <worldbody>, sibling of any body.
            LinkModel world = WorldBody(BodyWithCoordSys("base_link", "Origin_global")) with
            {
                Material = new MaterialModel("", new RgbaModel(0.5, 0.5, 0.5, 1), ""),
            };
            KinematicTree tree = new KinematicTree(
                "world_geom",
                "Origin_global",
                world);

            // World aux carries the visual mesh entry; this is what the
            // export pipeline produces for a world-level visual group.
            Dictionary<string, LinkAuxiliary> aux = new Dictionary<string, LinkAuxiliary>();
            LinkAuxiliary worldAux = new LinkAuxiliary();
            worldAux.VisualMeshes.Add(new MeshAssetRef
            {
                Name = "world_visual",
                File = "world_visual.STL",
            });
            aux[MJCFBuilder.WorldAuxKey] = worldAux;

            string xml = WriteMjcf(MJCFBuilder.Build(tree, "../meshes/", aux));
            XDocument doc = XDocument.Parse(xml);

            XElement worldBody = doc.Descendants("worldbody").Single();
            XElement[] geoms = worldBody.Elements("geom").ToArray();
            Assert.Single(geoms);
            // Sibling, not nested - the body's geom is one level deeper.
            Assert.Equal("worldbody", geoms[0].Parent.Name.LocalName);
            Assert.Equal("world_visual", geoms[0].Attribute("mesh").Value);
        }

        [Fact]
        public void TestWorldDirectSiteEmittedAsWorldbodyChild()
        {
            KinematicTree tree = new KinematicTree(
                "world_site",
                "Origin_global",
                WorldBody(BodyWithCoordSys("base_link", "Origin_global")));

            Dictionary<string, LinkAuxiliary> aux = new Dictionary<string, LinkAuxiliary>();
            LinkAuxiliary worldAux = new LinkAuxiliary();
            worldAux.Sites = new List<SiteTransform>
            {
                new SiteTransform
                {
                    Name = "scene_marker",
                    Position = new[] { 0.5, 0.0, 0.0 },
                    Quaternion = new[] { 1.0, 0.0, 0.0, 0.0 },
                },
            };
            aux[MJCFBuilder.WorldAuxKey] = worldAux;

            string xml = WriteMjcf(MJCFBuilder.Build(tree, "../meshes/", aux));
            XDocument doc = XDocument.Parse(xml);

            XElement worldBody = doc.Descendants("worldbody").Single();
            XElement[] sites = worldBody.Elements("site").ToArray();
            Assert.Single(sites);
            Assert.Equal("scene_marker", sites[0].Attribute("name").Value);
        }

        [Fact]
        public void TestEmptyWorldGeometryProducesNoExtraElements()
        {
            // No aux entry under WorldAuxKey -> no world-direct <geom>
            // / <site>; today's single-tree welded output is preserved.
            KinematicTree tree = new KinematicTree(
                "empty_world",
                "Origin_global",
                WorldBody(BodyWithCoordSys("base_link", "Origin_global")));

            string xml = WriteMjcf(MJCFBuilder.Build(tree, "../meshes/", new Dictionary<string, LinkAuxiliary>()));
            XDocument doc = XDocument.Parse(xml);

            XElement worldBody = doc.Descendants("worldbody").Single();
            Assert.Empty(worldBody.Elements("geom"));
            Assert.Empty(worldBody.Elements("site"));
            Assert.Single(worldBody.Elements("body"));
        }

        // Helper: build a top-level body LinkModel anchored at the named
        // coord-sys. Joint.Origin is zero (identity) so the
        // SuppressTransform path triggers when the name matches the
        // world's global-origin.
        private static LinkModel BodyWithCoordSys(string linkName, string coordSys)
        {
            JointModel joint = new JointModel(
                "", "", "", "",
                new PoseModel(new Vector3Model(0, 0, 0), TestRotations.Quat(0, 0, 0)),
                new Vector3Model(0, 0, 1),
                Limit: null,
                CoordinateSystemName: coordSys,
                AxisName: "",
                AxisFlipped: false);
            return SimpleLink(linkName).WithJoint(joint);
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

        // Writes the MJCFModel to a string for XML parsing. Mirrors the
        // shape of TestGolden3DofArm.WriteMjcf so failures look the same.
        private static string WriteMjcf(MJCFModel model)
        {
            StringBuilder sb = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
            using (XmlWriter writer = XmlWriter.Create(sb, settings))
            {
                model.WriteMJCF(writer);
            }
            return sb.ToString();
        }
    }

    // Convenience extension to spawn a derived LinkModel from an
    // immutable record without rewriting every ctor argument inline.
    internal static class TestLinkModelExt
    {
        public static LinkModel WithJoint(this LinkModel link, JointModel joint)
        {
            return link with { Joint = joint };
        }
    }
}
