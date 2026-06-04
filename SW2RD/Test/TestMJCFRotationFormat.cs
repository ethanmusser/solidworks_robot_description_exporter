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
using SW2RD.MJCF;
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SW2RD.Test
{
    // SW-less coverage for the MJCF rotation-format option. All three
    // representations (quat / axisangle / euler) normalize internally to the
    // same quaternion in MuJoCo, so the contracts under test are: (1) the
    // correct attribute name is emitted, (2) competing attributes are absent,
    // (3) eulerseq="XYZ" appears for - and only for - euler, and (4) parsing
    // the emitted value back to a quaternion recovers the original orientation.
    public class TestMJCFRotationFormat
    {
        private const double Deg2Rad = Math.PI / 180.0;

        // A deliberately non-axis-aligned orientation so a sign / axis-order
        // bug in any one path is caught by the round-trip.
        private static readonly double[] Rpy = { 0.3, -0.5, 0.8 };

        [Fact]
        public void TestDefaultIsQuaternionAndByteCompatible()
        {
            // The Build optional argument defaults to Quaternion so existing
            // golden output is preserved; the child body must carry quat=.
            XElement child = ChildBody(MJCFBuilder.Build(RotatedTree(), "../meshes/",
                new Dictionary<string, LinkAuxiliary>()));

            Assert.NotNull(child.Attribute("quat"));
            Assert.Null(child.Attribute("axisangle"));
            Assert.Null(child.Attribute("euler"));
        }

        [Fact]
        public void TestQuaternionFormatEmitsQuatOnly()
        {
            XDocument doc = BuildDoc(MJCFRotationFormat.Quaternion);
            XElement child = ChildBody(doc);

            Assert.NotNull(child.Attribute("quat"));
            Assert.Null(child.Attribute("axisangle"));
            Assert.Null(child.Attribute("euler"));
            Assert.Null(Compiler(doc).Attribute("eulerseq"));

            double[] q = ParseDoubles(child.Attribute("quat").Value);
            AssertQuaternionsEqual(MathOps.RPYToQuaternion(Rpy), q);
        }

        [Fact]
        public void TestAxisAngleFormatEmitsAxisAngleInDegrees()
        {
            XDocument doc = BuildDoc(MJCFRotationFormat.AxisAngle);
            XElement child = ChildBody(doc);

            Assert.NotNull(child.Attribute("axisangle"));
            Assert.Null(child.Attribute("quat"));
            Assert.Null(child.Attribute("euler"));
            // axisangle is NOT euler, so no eulerseq should be emitted.
            Assert.Null(Compiler(doc).Attribute("eulerseq"));

            double[] aa = ParseDoubles(child.Attribute("axisangle").Value);
            Assert.Equal(4, aa.Length);
            double[] q = AxisAngleDegToQuaternion(aa);
            AssertQuaternionsEqual(MathOps.RPYToQuaternion(Rpy), q);
        }

        [Fact]
        public void TestEulerFormatEmitsEulerInDegreesWithEulerSeq()
        {
            XDocument doc = BuildDoc(MJCFRotationFormat.Euler);
            XElement child = ChildBody(doc);

            Assert.NotNull(child.Attribute("euler"));
            Assert.Null(child.Attribute("quat"));
            Assert.Null(child.Attribute("axisangle"));

            // Euler angles need the extrinsic-XYZ sequence to match URDF rpy.
            Assert.Equal("XYZ", Compiler(doc).Attribute("eulerseq").Value);

            double[] e = ParseDoubles(child.Attribute("euler").Value);
            Assert.Equal(3, e.Length);
            double[] rpyRad = { e[0] * Deg2Rad, e[1] * Deg2Rad, e[2] * Deg2Rad };
            AssertQuaternionsEqual(MathOps.RPYToQuaternion(Rpy), MathOps.RPYToQuaternion(rpyRad));
        }

        [Fact]
        public void TestSiteHonorsRotationFormat()
        {
            // A world-direct site picks up the same rotation format as the
            // bodies, with a known 90-degree rotation about X.
            double[] siteRpy = { Math.PI / 2.0, 0, 0 };
            double[] siteQuat = MathOps.RPYToQuaternion(siteRpy);

            KinematicTree tree = new KinematicTree(
                "site_fmt", "Origin_global",
                WorldBody(BodyWithCoordSys("base_link", "Origin_global")));

            Dictionary<string, LinkAuxiliary> aux = new Dictionary<string, LinkAuxiliary>();
            LinkAuxiliary worldAux = new LinkAuxiliary
            {
                Sites = new List<SiteTransform>
                {
                    new SiteTransform
                    {
                        Name = "marker",
                        Position = new[] { 0.0, 0.0, 0.0 },
                        Quaternion = siteQuat,
                    },
                },
            };
            aux[MJCFBuilder.WorldAuxKey] = worldAux;

            XDocument doc = XDocument.Parse(WriteMjcf(
                MJCFBuilder.Build(tree, "../meshes/", aux, MJCFRotationFormat.AxisAngle)));
            XElement site = doc.Descendants("site").Single();

            Assert.NotNull(site.Attribute("axisangle"));
            Assert.Null(site.Attribute("quat"));
            // axisangle for a +90deg rotation about X is "1 0 0 90".
            double[] aa = ParseDoubles(site.Attribute("axisangle").Value);
            AssertQuaternionsEqual(siteQuat, AxisAngleDegToQuaternion(aa));
        }

        // ---- builders --------------------------------------------------

        private static XDocument BuildDoc(MJCFRotationFormat format)
        {
            return XDocument.Parse(WriteMjcf(
                MJCFBuilder.Build(RotatedTree(), "../meshes/",
                    new Dictionary<string, LinkAuxiliary>(), format)));
        }

        // world -> base_link (welded at world, suppressed) -> child with a
        // joint-origin rotation of Rpy. The child is a non-root body so it
        // emits pos/orientation attributes.
        private static KinematicTree RotatedTree()
        {
            LinkModel child = SimpleLink("child").WithJoint(new JointModel(
                "child_joint", "fixed", "base_link", "child",
                new PoseModel(new Vector3Model(0, 0, 0), TestRotations.Quat(Rpy[0], Rpy[1], Rpy[2])),
                new Vector3Model(0, 0, 1),
                Limit: null,
                CoordinateSystemName: "Origin_global",
                AxisName: "",
                AxisFlipped: false));

            LinkModel baseLink = BodyWithCoordSys("base_link", "Origin_global") with
            {
                Children = new[] { child },
            };

            return new KinematicTree("rotation_fmt", "Origin_global", WorldBody(baseLink));
        }

        private static XElement ChildBody(MJCFModel model)
        {
            return ChildBody(XDocument.Parse(WriteMjcf(model)));
        }

        private static XElement ChildBody(XDocument doc)
        {
            return doc.Descendants("body").Single(b => b.Attribute("name")?.Value == "child");
        }

        private static XElement Compiler(XDocument doc)
        {
            return doc.Descendants("compiler").Single();
        }

        // ---- math helpers ----------------------------------------------

        private static double[] AxisAngleDegToQuaternion(double[] aa)
        {
            double x = aa[0], y = aa[1], z = aa[2];
            double angle = aa[3] * Deg2Rad;
            double norm = Math.Sqrt(x * x + y * y + z * z);
            if (norm < 1e-12)
            {
                return new double[] { 1, 0, 0, 0 };
            }
            x /= norm; y /= norm; z /= norm;
            double half = angle * 0.5;
            double s = Math.Sin(half);
            double[] q = { Math.Cos(half), x * s, y * s, z * s };
            if (q[0] < 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    q[i] = -q[i];
                }
            }
            return q;
        }

        private static void AssertQuaternionsEqual(double[] expected, double[] actual)
        {
            Assert.Equal(4, actual.Length);
            // Canonicalize sign (q and -q are the same rotation).
            double[] e = Canonicalize(expected);
            double[] a = Canonicalize(actual);
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(e[i], a[i], 6);
            }
        }

        private static double[] Canonicalize(double[] q)
        {
            double[] r = (double[])q.Clone();
            if (r[0] < 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    r[i] = -r[i];
                }
            }
            return r;
        }

        private static double[] ParseDoubles(string value)
        {
            return value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => double.Parse(s, CultureInfo.InvariantCulture))
                .ToArray();
        }

        // ---- model fixtures (mirror TestMJCFMultiTree) ------------------

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
                "world", null,
                new MaterialModel("", new RgbaModel(1, 1, 1, 1), ""),
                Array.Empty<MeshGroupModel>(), Array.Empty<MeshGroupModel>(),
                false, InertialSourceModel.Visual,
                Array.Empty<ComponentReferenceModel>(),
                Array.Empty<SiteModel>(),
                null, topLevelBodies);
        }

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
}
