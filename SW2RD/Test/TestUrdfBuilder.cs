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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SW2RD.Test
{
    // SW-less coverage for the records-native URDF writer (URDFBuilder). The
    // writer consumes a canonical KinematicTree directly (angles already in
    // radians) and reduces a multi-body tree to the single robot URDF can
    // describe. These tests pin the behaviors that are specific to URDFBuilder
    // rather than to the shared canonical model.
    public class TestUrdfBuilder
    {
        [Fact]
        public void TestRobotAndBaseLinkNamesAreWritten()
        {
            KinematicTree tree = SingleChainTree("my_robot", JointWithLimit(null, null));
            XDocument doc = WriteUrdf(tree);

            Assert.Equal("my_robot", (string)doc.Root.Attribute("name"));
            Assert.Equal("base_link", (string)doc.Root.Elements("link").First().Attribute("name"));
        }

        [Fact]
        public void TestRevoluteWithoutLimitsDemotesToContinuous()
        {
            KinematicTree tree = SingleChainTree("r", JointWithLimit(null, null));
            XElement joint = WriteUrdf(tree).Descendants("joint").Single();
            Assert.Equal("continuous", (string)joint.Attribute("type"));
            Assert.Null(joint.Element("limit"));
        }

        [Fact]
        public void TestRevoluteWithLimitsStaysRevoluteAndEmitsRadians()
        {
            // URDF expresses angles in radians and the canonical model is
            // already radians, so the writer emits the stored values verbatim
            // (no unit conversion).
            KinematicTree tree = SingleChainTree("r", JointWithLimit(-Math.PI / 2.0, Math.PI / 2.0));
            XElement joint = WriteUrdf(tree).Descendants("joint").Single();

            Assert.Equal("revolute", (string)joint.Attribute("type"));
            XElement limit = joint.Element("limit");
            Assert.NotNull(limit);
            Assert.Equal(-Math.PI / 2.0, ParseDouble(limit, "lower"), 9);
            Assert.Equal(Math.PI / 2.0, ParseDouble(limit, "upper"), 9);
        }

        [Fact]
        public void TestOriginRpyDerivedFromQuaternion()
        {
            // A +90 deg rotation about Z, stored canonically as a quaternion,
            // must serialize as rpy = (0, 0, pi/2) in the joint <origin>.
            JointModel joint = new JointModel(
                "j", "revolute", "base_link", "child",
                new PoseModel(new Vector3Model(0, 0, 0), TestRotations.Quat(0, 0, Math.PI / 2.0)),
                new Vector3Model(0, 0, 1),
                Limit: new JointLimitModel(-1.0, 1.0, null, null),
                CoordinateSystemName: "", AxisName: "", AxisFlipped: false);

            XElement origin = WriteUrdf(SingleChainTree("r", joint))
                .Descendants("joint").Single().Element("origin");
            double[] rpy = origin.Attribute("rpy").Value
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

            Assert.Equal(0.0, rpy[0], 9);
            Assert.Equal(0.0, rpy[1], 9);
            Assert.Equal(Math.PI / 2.0, rpy[2], 9);
        }

        [Fact]
        public void TestMultipleTopLevelBodiesEmitsOnlyTheFirst()
        {
            // URDF describes a single robot; the writer keeps the first
            // top-level body as base_link and drops the rest.
            LinkModel first = Leaf("first_body", null);
            LinkModel second = Leaf("second_body", null);
            KinematicTree tree = new KinematicTree("multi", "", World(first, second));

            XDocument doc = WriteUrdf(tree);
            List<string> linkNames = doc.Descendants("link")
                .Select(l => (string)l.Attribute("name")).ToList();

            Assert.Contains("first_body", linkNames);
            Assert.DoesNotContain("second_body", linkNames);
        }

        // ---- builders --------------------------------------------------

        private static JointModel JointWithLimit(double? lower, double? upper)
        {
            JointLimitModel limit = (lower.HasValue || upper.HasValue)
                ? new JointLimitModel(lower, upper, null, null)
                : null;
            return new JointModel(
                "child_joint", "revolute", "base_link", "child_link",
                new PoseModel(new Vector3Model(0, 0, 0), QuaternionModel.Identity),
                new Vector3Model(0, 0, 1),
                limit, CoordinateSystemName: "", AxisName: "", AxisFlipped: false);
        }

        private static KinematicTree SingleChainTree(string name, JointModel childJoint)
        {
            LinkModel child = Leaf("child_link", childJoint);
            LinkModel baseLink = Leaf("base_link", null) with { Children = new[] { child } };
            return new KinematicTree(name, "", World(baseLink));
        }

        private static LinkModel Leaf(string name, JointModel joint)
        {
            return new LinkModel(
                name, null, new MaterialModel("", new RgbaModel(1, 1, 1, 1), ""),
                Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(),
                false, InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                joint, Array.Empty<LinkModel>());
        }

        private static LinkModel World(params LinkModel[] topLevel)
        {
            return new LinkModel(
                "world", null, new MaterialModel("", new RgbaModel(1, 1, 1, 1), ""),
                Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(),
                false, InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                null, topLevel);
        }

        private static double ParseDouble(XElement element, string attribute)
        {
            return double.Parse(element.Attribute(attribute).Value, CultureInfo.InvariantCulture);
        }

        private static XDocument WriteUrdf(KinematicTree tree)
        {
            using (StringWriter sw = new StringWriter())
            {
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                using (XmlWriter writer = XmlWriter.Create(sw, settings))
                {
                    URDFBuilder.Write(tree, writer);
                }
                return XDocument.Parse(sw.ToString());
            }
        }
    }
}
