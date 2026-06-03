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
    // SW-less coverage for the MJCF angle-units option. The contracts under
    // test: (1) Degree is the default and writes NO <compiler angle> attribute
    // (MuJoCo's own default), Radian writes angle="radian"; (2) orientation
    // angles (axisangle / euler), whose source is the radian quaternion, are
    // emitted in the selected unit; (3) HINGE joint range / ref, stored in the
    // model as degrees, are emitted as-is for Degree and converted for Radian;
    // (4) SLIDE (prismatic) range / ref are lengths and are NEVER converted.
    public class TestMJCFAngleUnit
    {
        private const double Deg2Rad = Math.PI / 180.0;

        // A deliberately non-axis-aligned orientation so a sign / axis-order
        // bug in any one path is caught by the round-trip.
        private static readonly double[] Rpy = { 0.3, -0.5, 0.8 };

        [Fact]
        public void TestDegreeUnitIsDefaultAndWritesNoCompilerAngle()
        {
            XDocument doc = BuildDoc(MJCFRotationFormat.AxisAngle, MJCFAngleUnit.Degree);
            Assert.Null(Compiler(doc).Attribute("angle"));
        }

        [Fact]
        public void TestBuildOmittingAngleUnitArgIsDegree()
        {
            // The Build optional argument defaults to Degree, so callers that
            // never pass an angle unit (existing golden path) emit no attribute.
            XDocument doc = XDocument.Parse(WriteMjcf(MJCFBuilder.Build(
                JointTree(), "../meshes/", new Dictionary<string, LinkAuxiliary>(),
                MJCFRotationFormat.Quaternion)));
            Assert.Null(Compiler(doc).Attribute("angle"));
        }

        [Fact]
        public void TestRadianUnitWritesCompilerAngleRadian()
        {
            XDocument doc = BuildDoc(MJCFRotationFormat.AxisAngle, MJCFAngleUnit.Radian);
            Assert.Equal("radian", Compiler(doc).Attribute("angle").Value);
        }

        [Fact]
        public void TestHingeRangeAndRefInDegreesByDefault()
        {
            XElement joint = HingeJoint(BuildDoc(MJCFRotationFormat.Quaternion, MJCFAngleUnit.Degree));

            double[] range = ParseDoubles(joint.Attribute("range").Value);
            Assert.Equal(-90.0, range[0], 9);
            Assert.Equal(90.0, range[1], 9);
            Assert.Equal(15.0, double.Parse(joint.Attribute("ref").Value, CultureInfo.InvariantCulture), 9);
        }

        [Fact]
        public void TestHingeRangeAndRefConvertedToRadians()
        {
            XElement joint = HingeJoint(BuildDoc(MJCFRotationFormat.Quaternion, MJCFAngleUnit.Radian));

            double[] range = ParseDoubles(joint.Attribute("range").Value);
            Assert.Equal(-Math.PI / 2.0, range[0], 6);
            Assert.Equal(Math.PI / 2.0, range[1], 6);
            Assert.Equal(15.0 * Deg2Rad,
                double.Parse(joint.Attribute("ref").Value, CultureInfo.InvariantCulture), 6);
        }

        [Fact]
        public void TestSlideRangeIsNeverConverted()
        {
            // Prismatic range is a length (meters); the angle unit must not touch
            // it in either mode.
            double[] degRange = ParseDoubles(
                SlideJoint(BuildDoc(MJCFRotationFormat.Quaternion, MJCFAngleUnit.Degree))
                    .Attribute("range").Value);
            double[] radRange = ParseDoubles(
                SlideJoint(BuildDoc(MJCFRotationFormat.Quaternion, MJCFAngleUnit.Radian))
                    .Attribute("range").Value);

            Assert.Equal(-0.1, degRange[0], 9);
            Assert.Equal(0.2, degRange[1], 9);
            Assert.Equal(-0.1, radRange[0], 9);
            Assert.Equal(0.2, radRange[1], 9);
        }

        [Fact]
        public void TestAxisAngleOrientationHonorsRadianUnit()
        {
            XElement child = ChildBody(BuildDoc(MJCFRotationFormat.AxisAngle, MJCFAngleUnit.Radian), "hinge_child");
            double[] aa = ParseDoubles(child.Attribute("axisangle").Value);
            Assert.Equal(4, aa.Length);
            // Angle is already in radians; round-trip back to the source quat.
            AssertQuaternionsEqual(MathOps.RPYToQuaternion(Rpy), AxisAngleRadToQuaternion(aa));
        }

        [Fact]
        public void TestEulerOrientationHonorsRadianUnit()
        {
            XElement child = ChildBody(BuildDoc(MJCFRotationFormat.Euler, MJCFAngleUnit.Radian), "hinge_child");
            double[] e = ParseDoubles(child.Attribute("euler").Value);
            Assert.Equal(3, e.Length);
            // Values are radians directly; no degree scaling.
            AssertQuaternionsEqual(MathOps.RPYToQuaternion(Rpy), MathOps.RPYToQuaternion(e));
            Assert.Equal("XYZ", Compiler(BuildDoc(MJCFRotationFormat.Euler, MJCFAngleUnit.Radian))
                .Attribute("eulerseq").Value);
        }

        // ---- builders --------------------------------------------------

        private static XDocument BuildDoc(MJCFRotationFormat format, MJCFAngleUnit unit)
        {
            return XDocument.Parse(WriteMjcf(MJCFBuilder.Build(
                JointTree(), "../meshes/", new Dictionary<string, LinkAuxiliary>(), format, unit)));
        }

        // world -> base_link (welded at world, suppressed) with two children:
        // a revolute "hinge_child" (limits + ref, plus an Rpy orientation so the
        // orientation tests have something non-identity to inspect) and a
        // prismatic "slide_child" (length limits).
        private static KinematicTree JointTree()
        {
            LinkModel hinge = SimpleLink("hinge_child").WithJoint(new JointModel(
                "hinge_joint", "revolute", "base_link", "hinge_child",
                new PoseModel(new Vector3Model(0, 0, 0), new RpyModel(Rpy[0], Rpy[1], Rpy[2])),
                new Vector3Model(0, 0, 1),
                Limit: new JointLimitModel(-90.0, 90.0, null, null),
                CoordinateSystemName: "Origin_global",
                AxisName: "",
                AxisFlipped: false,
                Reference: 15.0));

            LinkModel slide = SimpleLink("slide_child").WithJoint(new JointModel(
                "slide_joint", "prismatic", "base_link", "slide_child",
                new PoseModel(new Vector3Model(0, 0, 0), new RpyModel(0, 0, 0)),
                new Vector3Model(1, 0, 0),
                Limit: new JointLimitModel(-0.1, 0.2, null, null),
                CoordinateSystemName: "Origin_global",
                AxisName: "",
                AxisFlipped: false));

            LinkModel baseLink = BodyWithCoordSys("base_link", "Origin_global") with
            {
                Children = new[] { hinge, slide },
            };

            return new KinematicTree("angle_unit", "Origin_global", WorldBody(baseLink));
        }

        private static XElement HingeJoint(XDocument doc)
        {
            return ChildBody(doc, "hinge_child").Elements("joint").Single();
        }

        private static XElement SlideJoint(XDocument doc)
        {
            return ChildBody(doc, "slide_child").Elements("joint").Single();
        }

        private static XElement ChildBody(XDocument doc, string name)
        {
            return doc.Descendants("body").Single(b => b.Attribute("name")?.Value == name);
        }

        private static XElement Compiler(XDocument doc)
        {
            return doc.Descendants("compiler").Single();
        }

        // ---- math helpers ----------------------------------------------

        private static double[] AxisAngleRadToQuaternion(double[] aa)
        {
            double x = aa[0], y = aa[1], z = aa[2];
            double angle = aa[3];
            double norm = Math.Sqrt(x * x + y * y + z * z);
            if (norm < 1e-12)
            {
                return new double[] { 1, 0, 0, 0 };
            }
            x /= norm; y /= norm; z /= norm;
            double half = angle * 0.5;
            double s = Math.Sin(half);
            return new double[] { Math.Cos(half), x * s, y * s, z * s };
        }

        private static void AssertQuaternionsEqual(double[] expected, double[] actual)
        {
            Assert.Equal(4, actual.Length);
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

        // ---- model fixtures (mirror TestMJCFRotationFormat) -------------

        private static LinkModel BodyWithCoordSys(string linkName, string coordSys)
        {
            JointModel joint = new JointModel(
                "", "", "", "",
                new PoseModel(new Vector3Model(0, 0, 0), new RpyModel(0, 0, 0)),
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
