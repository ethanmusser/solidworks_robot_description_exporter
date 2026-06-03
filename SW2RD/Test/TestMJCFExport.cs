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

using SW2RD.MJCF;
using SW2RD.URDF;
using SW2RD.Export;
using SW2RD.Utilities;
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
    // Unit tests for the MJCF writer/builder. These tests do NOT require SolidWorks
    // — they synthesize a small Robot tree directly in memory, run it through the
    // MJCF builder, and assert that the emitted body/joint/site transforms match
    // the URDF input. The reference values come from the 3_DOF_ARM example and
    // standard quaternion identities.
    public class TestMJCFExport
    {
        [Fact]
        public void TestRPYToQuaternionIdentity()
        {
            double[] q = MathOps.RPYToQuaternion(new double[] { 0, 0, 0 });
            Assert.Equal(1.0, q[0], 9);
            Assert.Equal(0.0, q[1], 9);
            Assert.Equal(0.0, q[2], 9);
            Assert.Equal(0.0, q[3], 9);
        }

        [Fact]
        public void TestRPYToQuaternionRollHalfPi()
        {
            // roll = +pi/2 about X. quaternion = (cos(pi/4), sin(pi/4), 0, 0)
            double[] q = MathOps.RPYToQuaternion(new double[] { Math.PI / 2, 0, 0 });
            Assert.Equal(Math.Sqrt(0.5), q[0], 9);
            Assert.Equal(Math.Sqrt(0.5), q[1], 9);
            Assert.Equal(0.0, q[2], 9);
            Assert.Equal(0.0, q[3], 9);
        }

        [Fact]
        public void TestRPYToQuaternion3DofArmProxJoint()
        {
            // The 3_DOF_ARM example has its prox_joint at rpy = (-pi/2, 0, -pi/2).
            // Using the URDF convention R = Rz(yaw) * Ry(pitch) * Rx(roll), the
            // unit quaternion is q = (1/2, -1/2, 1/2, -1/2) (with w >= 0).
            double[] rpy = new double[] { -Math.PI / 2, 0, -Math.PI / 2 };
            double[] q = MathOps.RPYToQuaternion(rpy);

            // Sanity: unit quaternion.
            double mag = q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3];
            Assert.Equal(1.0, mag, 9);

            Assert.Equal(0.5, q[0], 9);
            Assert.Equal(-0.5, q[1], 9);
            Assert.Equal(0.5, q[2], 9);
            Assert.Equal(-0.5, q[3], 9);
        }

        [Fact]
        public void TestBuildSimpleTwoLinkChain()
        {
            // A trivial 2-link robot: base_link -> child_link via a revolute joint
            // with origin at (0.1, 0, 0) and zero rotation.
            Link baseLink = new Link(null) { Name = "base_link" };
            // No inertial mass for the base — ExportHelper would normally set this.
            baseLink.Inertial.Mass.Value = 0;

            Link childLink = new Link(baseLink) { Name = "child_link" };
            childLink.Inertial.Mass.Value = 1.5;
            childLink.Inertial.Origin.SetXYZ(new double[] { 0.05, 0, 0 });
            childLink.Inertial.Inertia.Ixx = 0.001;
            childLink.Inertial.Inertia.Iyy = 0.002;
            childLink.Inertial.Inertia.Izz = 0.003;
            childLink.Joint.Name = "child_joint";
            childLink.Joint.Type = "revolute";
            childLink.Joint.Origin.SetXYZ(new double[] { 0.1, 0, 0 });
            childLink.Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
            childLink.Joint.Axis.SetXYZ(new double[] { 0, 0, 1 });
            childLink.Joint.Limit.Lower = -1;
            childLink.Joint.Limit.Upper = 1;
            childLink.Joint.Limit.Effort = 10;
            childLink.Joint.Limit.Velocity = 2;

            baseLink.Children.Add(childLink);

            Robot robot = new Robot { Name = "test_chain" };
            robot.SetBaseLink(baseLink);

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", null);

            // Root body: world placement, no transform.
            Assert.NotNull(model.RootBody);
            Assert.True(model.RootBody.SuppressTransform);
            Assert.Equal("base_link", model.RootBody.Name);
            Assert.Single(model.RootBody.Children);

            // Child body: pos = (0.1, 0, 0), quat = (1, 0, 0, 0).
            Body child = model.RootBody.Children[0];
            Assert.False(child.SuppressTransform);
            Assert.Equal("child_link", child.Name);
            Assert.Equal(0.1, child.Position[0], 9);
            Assert.Equal(0.0, child.Position[1], 9);
            Assert.Equal(0.0, child.Position[2], 9);
            Assert.Equal(1.0, child.Quaternion[0], 9);
            Assert.Equal(0.0, child.Quaternion[1], 9);
            Assert.Equal(0.0, child.Quaternion[2], 9);
            Assert.Equal(0.0, child.Quaternion[3], 9);

            // Joint is a hinge with axis (0,0,1) at body origin.
            Assert.NotNull(child.Joint);
            Assert.Equal(MJCFJointType.Hinge, child.Joint.Type);
            Assert.Equal(0.0, child.Joint.Position[0], 9);
            Assert.Equal(0.0, child.Joint.Position[1], 9);
            Assert.Equal(0.0, child.Joint.Position[2], 9);
            Assert.Equal(0.0, child.Joint.Axis[0], 9);
            Assert.Equal(0.0, child.Joint.Axis[1], 9);
            Assert.Equal(1.0, child.Joint.Axis[2], 9);
            Assert.True(child.Joint.HasLimits);
            Assert.Equal(-1.0, child.Joint.LowerLimit, 9);
            Assert.Equal(1.0, child.Joint.UpperLimit, 9);

            // Inertial element on the child.
            Assert.NotNull(child.Inertial);
            Assert.True(child.Inertial.HasInertia);
            Assert.Equal(1.5, child.Inertial.Mass, 9);
            Assert.Equal(0.05, child.Inertial.Position[0], 9);
        }

        [Fact]
        public void TestBuild3DofArmProxLinkTransform()
        {
            // Mimics the 3_DOF_ARM example: the prox_link sits at
            //   pos = (0.00249115384615384, 0, 0)
            //   rpy = (-pi/2, 0, -pi/2)
            // The MJCF output should encode the same transform via pos/quat.
            Link baseLink = new Link(null) { Name = "base_link" };
            Link prox = new Link(baseLink) { Name = "prox_link" };
            prox.Inertial.Mass.Value = 1.0; // arbitrary nonzero so inertial emits
            prox.Inertial.Inertia.Ixx = 0.001;
            prox.Inertial.Inertia.Iyy = 0.001;
            prox.Inertial.Inertia.Izz = 0.001;
            prox.Joint.Name = "prox_joint";
            prox.Joint.Type = "revolute";
            prox.Joint.Origin.SetXYZ(new double[] { 0.00249115384615384, 0, 0 });
            prox.Joint.Origin.SetRPY(new double[] { -Math.PI / 2, 0, -Math.PI / 2 });
            prox.Joint.Axis.SetXYZ(new double[] { 0, 0, 1 });
            prox.Joint.Limit.Lower = -3;
            prox.Joint.Limit.Upper = 3;
            prox.Joint.Limit.Effort = 100;
            prox.Joint.Limit.Velocity = 5;

            baseLink.Children.Add(prox);

            Robot robot = new Robot { Name = "test_arm" };
            robot.SetBaseLink(baseLink);

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", null);

            Body proxBody = model.RootBody.Children[0];
            Assert.Equal(0.00249115384615384, proxBody.Position[0], 12);
            Assert.Equal(0.0, proxBody.Position[1], 12);
            Assert.Equal(0.0, proxBody.Position[2], 12);

            // Quaternion from rpy=(-pi/2, 0, -pi/2) -> (0.5, -0.5, 0.5, -0.5)
            Assert.Equal(0.5, proxBody.Quaternion[0], 9);
            Assert.Equal(-0.5, proxBody.Quaternion[1], 9);
            Assert.Equal(0.5, proxBody.Quaternion[2], 9);
            Assert.Equal(-0.5, proxBody.Quaternion[3], 9);
        }

        [Fact]
        public void TestFixedJointOmitsJointElement()
        {
            Link baseLink = new Link(null) { Name = "base_link" };
            Link fixedChild = new Link(baseLink) { Name = "fixed_child" };
            fixedChild.Joint.Name = "fixed_joint";
            fixedChild.Joint.Type = "fixed";
            fixedChild.Joint.Origin.SetXYZ(new double[] { 0.5, 0, 0 });
            fixedChild.Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
            baseLink.Children.Add(fixedChild);

            Robot robot = new Robot { Name = "test_fixed" };
            robot.SetBaseLink(baseLink);
            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", null);

            Body child = model.RootBody.Children[0];
            // Fixed joint: body still gets the pos/quat, but no <joint> child element.
            Assert.Null(child.Joint);
            Assert.Equal(0.5, child.Position[0], 9);
        }

        [Fact]
        public void TestPrismaticJointMapsToSlide()
        {
            Link baseLink = new Link(null) { Name = "base_link" };
            Link slider = new Link(baseLink) { Name = "slider" };
            slider.Joint.Name = "slide_joint";
            slider.Joint.Type = "prismatic";
            slider.Joint.Origin.SetXYZ(new double[] { 0, 0, 0 });
            slider.Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
            slider.Joint.Axis.SetXYZ(new double[] { 1, 0, 0 });
            slider.Joint.Limit.Lower = 0;
            slider.Joint.Limit.Upper = 1;
            slider.Joint.Limit.Effort = 5;
            slider.Joint.Limit.Velocity = 1;
            baseLink.Children.Add(slider);

            Robot robot = new Robot { Name = "test_slide" };
            robot.SetBaseLink(baseLink);
            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", null);

            Body sliderBody = model.RootBody.Children[0];
            Assert.NotNull(sliderBody.Joint);
            Assert.Equal(MJCFJointType.Slide, sliderBody.Joint.Type);
        }

        [Fact]
        public void TestContinuousJointManualLimitsEmitAsHingeRange()
        {
            Link baseLink = new Link(null) { Name = "base_link" };
            Link child = new Link(baseLink) { Name = "child" };
            child.Joint.Name = "continuous_joint";
            child.Joint.Type = "continuous";
            child.Joint.Axis.SetXYZ(new double[] { 0, 0, 1 });
            child.Joint.Limit.Lower = -0.25;
            child.Joint.Limit.Upper = 0.75;
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "test_continuous_limits" };
            robot.SetBaseLink(baseLink);

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", null);
            Body childBody = model.RootBody.Children[0];

            Assert.NotNull(childBody.Joint);
            Assert.Equal(MJCFJointType.Hinge, childBody.Joint.Type);
            Assert.True(childBody.Joint.HasLimits);
            Assert.Equal(-0.25, childBody.Joint.LowerLimit, 9);
            Assert.Equal(0.75, childBody.Joint.UpperLimit, 9);
            Assert.False(childBody.Joint.HasEffort);

            string xml = WriteMJCFToString(model);
            Assert.DoesNotContain("actuatorfrcrange", xml);
            Assert.DoesNotContain("velocity=", xml);
        }

        [Fact]
        public void TestJointPropertyFieldsMapToMJCFJointAttributes()
        {
            Link baseLink = new Link(null) { Name = "base_link" };
            Link child = new Link(baseLink) { Name = "child" };
            child.Joint.Name = "joint_props";
            child.Joint.Type = "revolute";
            child.Joint.Axis.SetXYZ(new double[] { 0, 0, 1 });
            child.Joint.Limit.Lower = -90.0;
            child.Joint.Limit.Upper = 90.0;
            child.Joint.Limit.Effort = 12.0;
            child.Joint.Limit.Velocity = 180.0;
            child.Joint.Dynamics.Damping = 0.4;
            child.Joint.Dynamics.Friction = 0.2;
            child.Joint.Armature = 0.01;
            child.Joint.Reference = 15.0;
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "test_joint_props" };
            robot.SetBaseLink(baseLink);

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", null);
            Body childBody = model.RootBody.Children[0];

            Assert.NotNull(childBody.Joint);
            Assert.True(childBody.Joint.HasLimits);
            Assert.Equal(-90.0, childBody.Joint.LowerLimit, 9);
            Assert.Equal(90.0, childBody.Joint.UpperLimit, 9);
            Assert.True(childBody.Joint.HasEffort);
            Assert.Equal(12.0, childBody.Joint.Effort, 9);
            Assert.True(childBody.Joint.HasDamping);
            Assert.Equal(0.4 * 180.0 / Math.PI, childBody.Joint.Damping, 9);
            Assert.True(childBody.Joint.HasFriction);
            Assert.Equal(0.2, childBody.Joint.Friction, 9);
            Assert.True(childBody.Joint.HasArmature);
            Assert.Equal(0.01, childBody.Joint.Armature, 9);
            Assert.True(childBody.Joint.HasRef);
            Assert.Equal(15.0, childBody.Joint.Ref, 9);

            string xml = WriteMJCFToString(model);
            Assert.Contains("range=\"-90 90\"", xml);
            Assert.Contains("actuatorfrcrange=\"-12 12\"", xml);
            Assert.Contains("frictionloss=\"0.2\"", xml);
            Assert.Contains("armature=\"0.01\"", xml);
            Assert.Contains("ref=\"15\"", xml);
            Assert.DoesNotContain("velocity=", xml);
        }

        [Fact]
        public void TestURDFConvertsAngularJointPropertiesFromDegreesToRadians()
        {
            Link baseLink = new Link(null) { Name = "base_link" };
            Link child = new Link(baseLink) { Name = "child" };
            child.Joint.Name = "joint_props";
            child.Joint.Type = "revolute";
            child.Joint.Axis.SetXYZ(new double[] { 0, 0, 1 });
            child.Joint.Limit.Lower = -90.0;
            child.Joint.Limit.Upper = 90.0;
            child.Joint.Limit.Effort = 12.0;
            child.Joint.Limit.Velocity = 180.0;
            child.Joint.Dynamics.Damping = 0.4;
            child.Joint.Dynamics.Friction = 0.2;
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "test_urdf_degrees" };
            robot.SetBaseLink(baseLink);

            string xml = WriteURDFToString(robot);
            XElement joint = XDocument.Parse(xml).Descendants("joint").Single();
            XElement limit = joint.Element("limit");
            XElement dynamics = joint.Element("dynamics");

            Assert.Equal(-Math.PI / 2.0, ReadDouble(limit, "lower"), 12);
            Assert.Equal(Math.PI / 2.0, ReadDouble(limit, "upper"), 12);
            Assert.Equal(Math.PI, ReadDouble(limit, "velocity"), 12);
            Assert.Equal(12.0, ReadDouble(limit, "effort"), 12);
            Assert.Equal(0.4 * 180.0 / Math.PI, ReadDouble(dynamics, "damping"), 12);
            Assert.Equal(0.2, ReadDouble(dynamics, "friction"), 12);

            Assert.Equal(-90.0, child.Joint.Limit.LowerOrNull);
            Assert.Equal(90.0, child.Joint.Limit.UpperOrNull);
            Assert.Equal(180.0, child.Joint.Limit.VelocityOrNull);
            Assert.Equal(0.4, child.Joint.Dynamics.DampingOrNull);
        }

        [Fact]
        public void TestURDFOmitsBlankEffortAndVelocity()
        {
            Link baseLink = new Link(null) { Name = "base_link" };
            Link child = new Link(baseLink) { Name = "child" };
            child.Joint.Name = "joint_props";
            child.Joint.Type = "revolute";
            child.Joint.Axis.SetXYZ(new double[] { 0, 0, 1 });
            child.Joint.Limit.SetLower(-90.0);
            child.Joint.Limit.SetUpper(90.0);
            child.Joint.Limit.SetEffort(null);
            child.Joint.Limit.SetVelocity(null);
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "test_urdf_blank_limits" };
            robot.SetBaseLink(baseLink);

            string xml = WriteURDFToString(robot);
            XElement joint = XDocument.Parse(xml).Descendants("joint").Single();
            XElement limit = joint.Element("limit");

            Assert.NotNull(limit);
            Assert.Equal(-Math.PI / 2.0, ReadDouble(limit, "lower"), 12);
            Assert.Equal(Math.PI / 2.0, ReadDouble(limit, "upper"), 12);
            Assert.Null(limit.Attribute("effort"));
            Assert.Null(limit.Attribute("velocity"));
        }

        [Fact]
        public void TestWriteMJCFEmitsExpectedStructure()
        {
            Link baseLink = new Link(null) { Name = "base_link" };
            Link child = new Link(baseLink) { Name = "child" };
            child.Joint.Name = "j1";
            child.Joint.Type = "revolute";
            child.Joint.Origin.SetXYZ(new double[] { 0.1, 0, 0 });
            child.Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
            child.Joint.Axis.SetXYZ(new double[] { 0, 0, 1 });
            child.Joint.Limit.Lower = -1;
            child.Joint.Limit.Upper = 1;
            child.Joint.Limit.Effort = 1;
            child.Joint.Limit.Velocity = 1;
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "test_write" };
            robot.SetBaseLink(baseLink);

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", null);

            string xml;
            using (StringWriter sw = new StringWriter())
            {
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                using (XmlWriter writer = XmlWriter.Create(sw, settings))
                {
                    model.WriteMJCF(writer);
                }
                xml = sw.ToString();
            }

            // Sanity-check the document structure: it should contain a <mujoco
            // model="..."> root, a <compiler> element, the visual/collision default
            // classes, a <worldbody>, and the two body elements.
            Assert.Contains("<mujoco model=\"test_write\">", xml);
            Assert.Contains("<compiler", xml);
            Assert.Contains("class=\"visual\"", xml);
            Assert.Contains("class=\"collision\"", xml);
            Assert.Contains("<worldbody>", xml);
            Assert.Contains("<body name=\"base_link\">", xml);
            Assert.Contains("<body name=\"child\"", xml);
            Assert.Contains("<joint name=\"j1\"", xml);
            Assert.Contains("type=\"hinge\"", xml);
        }

        [Fact]
        public void TestAssetDeduplication()
        {
            // Adding the same mesh name twice should leave only one entry in the
            // asset block. This guards against duplicates that would happen if the
            // visual and collision passes accidentally produced the same asset.
            Asset asset = new Asset();
            asset.Add(new MeshAsset("mesh_a", "mesh_a.stl"));
            asset.Add(new MeshAsset("mesh_a", "mesh_a.stl"));
            asset.Add(new MeshAsset("mesh_b", "mesh_b.stl"));
            Assert.Equal(2, asset.Meshes.Count);
        }

        [Fact]
        public void TestRotationMatrixToQuaternionRoundTrips()
        {
            // RPY -> rotation matrix -> quaternion should match RPY -> quaternion.
            double[] rpy = new double[] { 0.3, -0.4, 0.7 };
            var matrix = MathOps.GetRotation(rpy);
            double[] q1 = MathOps.RotationMatrixToQuaternion(matrix);
            double[] q2 = MathOps.RPYToQuaternion(rpy);

            // Quaternions are equal up to sign; canonicalization (w >= 0) means
            // both should agree.
            Assert.Equal(q2[0], q1[0], 9);
            Assert.Equal(q2[1], q1[1], 9);
            Assert.Equal(q2[2], q1[2], 9);
            Assert.Equal(q2[3], q1[3], 9);
        }

        [Fact]
        public void TestExportPackageMJCFLayout()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                ExportPackage pkg = new ExportPackage("test_mjcf", tempDir, ExportFormat.MJCF);
                Assert.EndsWith(".xml", pkg.ModelExtension);
                Assert.Contains("mjcf", pkg.WindowsModelsDirectory);
                Assert.Null(pkg.WindowsCMakeLists);
                Assert.Null(pkg.WindowsLaunchDirectory);
                // The MJCF compiler meshdir must be relative to the model file
                // location (mjcf/<name>.xml), not a package:// URI.
                Assert.Equal("../meshes/", pkg.MJCFMeshDir);

                pkg.CreateDirectories();
                Assert.True(Directory.Exists(pkg.WindowsPackageDirectory));
                Assert.True(Directory.Exists(pkg.WindowsMeshesDirectory));
                Assert.True(Directory.Exists(pkg.WindowsModelsDirectory));
                Assert.False(Directory.Exists(pkg.WindowsTexturesDirectory));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void TestExportPackageCreatesTexturesDirectoryOnDemand()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                ExportPackage pkg = new ExportPackage("test_mjcf", tempDir, ExportFormat.MJCF);

                pkg.CreateDirectories();
                Assert.False(Directory.Exists(pkg.WindowsTexturesDirectory));

                pkg.EnsureTexturesDirectory();
                Assert.True(Directory.Exists(pkg.WindowsTexturesDirectory));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void TestMJCFEmitsMultipleVisualGeoms()
        {
            // A link with two visual groups should produce two <mesh> entries
            // in <asset> and two <geom class="visual"> children of the body.
            // Each geom is named after its mesh asset ("<link>_<group>").
            Link baseLink = new Link(null) { Name = "base_link" };
            Link child = new Link(baseLink) { Name = "multi_visual" };
            child.Joint.Name = "j1";
            child.Joint.Type = "fixed";
            child.Joint.Origin.SetXYZ(new double[] { 0, 0, 0 });
            child.Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "test_multi_visual" };
            robot.SetBaseLink(baseLink);

            var aux = new Dictionary<string, LinkAuxiliary>
            {
                ["multi_visual"] = new LinkAuxiliary
                {
                    VisualMeshes =
                    {
                        new MeshAssetRef { Name = "multi_visual_outer", File = "multi_visual_outer.STL" },
                        new MeshAssetRef { Name = "multi_visual_inner", File = "multi_visual_inner.STL" },
                    },
                },
            };

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", aux);

            // Two mesh assets registered, with the supplied names/files.
            Assert.Equal(2, model.Asset.Meshes.Count);
            string[] meshNames = new string[] { model.Asset.Meshes[0].Name, model.Asset.Meshes[1].Name };
            Assert.Contains("multi_visual_outer", meshNames);
            Assert.Contains("multi_visual_inner", meshNames);

            Body childBody = model.RootBody.Children[0];
            Assert.Equal(2, childBody.Geoms.Count);
            Assert.All(childBody.Geoms, g => Assert.Equal(GeomRole.Visual, g.Role));
            // Geom names match their mesh asset names so the <geom> reads the
            // way the user named the group.
            string[] geomNames = new string[] { childBody.Geoms[0].Name, childBody.Geoms[1].Name };
            Assert.Contains("multi_visual_outer", geomNames);
            Assert.Contains("multi_visual_inner", geomNames);
        }

        [Fact]
        public void TestMJCFEmitsMultipleCollisionGeoms()
        {
            // Two collision groups should yield two <mesh> entries and two
            // <geom class="collision"> children. This is what lets MuJoCo
            // approximate a concave shape as a union of convex hulls.
            Link baseLink = new Link(null) { Name = "base_link" };
            Link child = new Link(baseLink) { Name = "multi_col" };
            child.Joint.Name = "j1";
            child.Joint.Type = "fixed";
            child.Joint.Origin.SetXYZ(new double[] { 0, 0, 0 });
            child.Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "test_multi_col" };
            robot.SetBaseLink(baseLink);

            var aux = new Dictionary<string, LinkAuxiliary>
            {
                ["multi_col"] = new LinkAuxiliary
                {
                    VisualMeshes =
                    {
                        new MeshAssetRef { Name = "multi_col_visual", File = "multi_col_visual.STL" },
                    },
                    CollisionMeshes =
                    {
                        new MeshAssetRef { Name = "multi_col_upper", File = "multi_col_upper.STL" },
                        new MeshAssetRef { Name = "multi_col_lower", File = "multi_col_lower.STL" },
                    },
                },
            };

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", aux);

            // Three distinct assets: one visual + two collisions.
            Assert.Equal(3, model.Asset.Meshes.Count);

            Body childBody = model.RootBody.Children[0];
            // 1 visual geom + 2 collision geoms.
            Assert.Equal(3, childBody.Geoms.Count);
            int visualCount = 0;
            int collisionCount = 0;
            foreach (Geom g in childBody.Geoms)
            {
                if (g.Role == GeomRole.Visual) visualCount++;
                else if (g.Role == GeomRole.Collision) collisionCount++;
            }
            Assert.Equal(1, visualCount);
            Assert.Equal(2, collisionCount);
        }

        [Fact]
        public void TestMJCFEmitsPerLinkMaterialAsset()
        {
            // Each link with at least one visual geom should produce one
            // <material> in <asset>, with rgba pulled from
            // Link.Visual.Material.Color and the link's material name. The
            // visual <geom>s reference it via material="..."; collision
            // <geom>s carry neither rgba nor material (they inherit from
            // <default class="collision">).
            Link baseLink = new Link(null) { Name = "base_link" };
            // SetColor populates Link.Visual.Material.Color; the Material.Name
            // mirrors what ComputeVisualCollisionProperties writes by default.
            baseLink.Visual.Material.Color.SetColor(new double[] { 0.1, 0.2, 0.3, 1.0 });
            baseLink.Visual.Material.Name = "material_base_link";

            Link child = new Link(baseLink) { Name = "child" };
            child.Visual.Material.Color.SetColor(new double[] { 0.7, 0.8, 0.9, 0.5 });
            child.Visual.Material.Name = "material_child";
            child.Joint.Name = "j1";
            child.Joint.Type = "fixed";
            child.Joint.Origin.SetXYZ(new double[] { 0, 0, 0 });
            child.Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "test_materials" };
            robot.SetBaseLink(baseLink);

            var aux = new Dictionary<string, LinkAuxiliary>
            {
                ["base_link"] = new LinkAuxiliary
                {
                    VisualMeshes =
                    {
                        new MeshAssetRef { Name = "base_link_visual", File = "base_link_visual.STL" },
                    },
                    CollisionMeshes =
                    {
                        new MeshAssetRef { Name = "base_link_collision", File = "base_link_collision.STL" },
                    },
                },
                ["child"] = new LinkAuxiliary
                {
                    VisualMeshes =
                    {
                        new MeshAssetRef { Name = "child_visual", File = "child_visual.STL" },
                    },
                    CollisionMeshes =
                    {
                        new MeshAssetRef { Name = "child_collision", File = "child_collision.STL" },
                    },
                },
            };

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", aux);

            // Two materials, one per link. Names match the link's Material.Name.
            Assert.Equal(2, model.Asset.Materials.Count);
            MaterialAsset baseMat = model.Asset.FindMaterial("material_base_link");
            MaterialAsset childMat = model.Asset.FindMaterial("material_child");
            Assert.NotNull(baseMat);
            Assert.NotNull(childMat);
            Assert.Equal(new double[] { 0.1, 0.2, 0.3, 1.0 }, baseMat.Rgba);
            Assert.Equal(new double[] { 0.7, 0.8, 0.9, 0.5 }, childMat.Rgba);
            // No textures configured in this test.
            Assert.Empty(model.Asset.Textures);
            Assert.Null(baseMat.Texture);
            Assert.Null(childMat.Texture);

            // Visual geoms reference the material; collision geoms reference nothing.
            Body baseBody = model.RootBody;
            Geom baseVisual = baseBody.Geoms.Find(g => g.Role == GeomRole.Visual);
            Geom baseCollision = baseBody.Geoms.Find(g => g.Role == GeomRole.Collision);
            Assert.Equal("material_base_link", baseVisual.Material);
            Assert.Null(baseVisual.Rgba);
            Assert.Null(baseCollision.Material);
            Assert.Null(baseCollision.Rgba);

            Body childBody = baseBody.Children[0];
            Geom childVisual = childBody.Geoms.Find(g => g.Role == GeomRole.Visual);
            Geom childCollision = childBody.Geoms.Find(g => g.Role == GeomRole.Collision);
            Assert.Equal("material_child", childVisual.Material);
            Assert.Null(childVisual.Rgba);
            Assert.Null(childCollision.Material);
            Assert.Null(childCollision.Rgba);

            // Sanity-check the emitted XML: <material> elements land in <asset>
            // and visual <geom>s carry material="..." attributes.
            string xml;
            using (StringWriter sw = new StringWriter())
            {
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                using (XmlWriter writer = XmlWriter.Create(sw, settings))
                {
                    model.WriteMJCF(writer);
                }
                xml = sw.ToString();
            }

            Assert.Contains("<material name=\"material_base_link\"", xml);
            Assert.Contains("<material name=\"material_child\"", xml);
            Assert.Contains("rgba=\"0.1 0.2 0.3 1\"", xml);
            Assert.Contains("rgba=\"0.7 0.8 0.9 0.5\"", xml);
            Assert.Contains("material=\"material_base_link\"", xml);
            Assert.Contains("material=\"material_child\"", xml);
        }

        [Fact]
        public void TestMJCFMultiVisualGeomsShareLinkMaterial()
        {
            // A link with multiple visual groups produces ONE <material> and
            // every visual <geom> references it. Collisions remain unmaterialed.
            Link baseLink = new Link(null) { Name = "base_link" };
            Link child = new Link(baseLink) { Name = "multi" };
            child.Visual.Material.Color.SetColor(new double[] { 0.25, 0.5, 0.75, 1.0 });
            child.Visual.Material.Name = "material_multi";
            child.Joint.Name = "j1";
            child.Joint.Type = "fixed";
            child.Joint.Origin.SetXYZ(new double[] { 0, 0, 0 });
            child.Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "test_multi_material" };
            robot.SetBaseLink(baseLink);

            var aux = new Dictionary<string, LinkAuxiliary>
            {
                ["multi"] = new LinkAuxiliary
                {
                    VisualMeshes =
                    {
                        new MeshAssetRef { Name = "multi_a", File = "multi_a.STL" },
                        new MeshAssetRef { Name = "multi_b", File = "multi_b.STL" },
                    },
                    CollisionMeshes =
                    {
                        new MeshAssetRef { Name = "multi_col_a", File = "multi_col_a.STL" },
                        new MeshAssetRef { Name = "multi_col_b", File = "multi_col_b.STL" },
                    },
                },
            };

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", aux);

            // Exactly one material, despite two visual groups.
            Assert.Single(model.Asset.Materials);
            Assert.Equal("material_multi", model.Asset.Materials[0].Name);

            Body multiBody = model.RootBody.Children[0];
            foreach (Geom g in multiBody.Geoms)
            {
                if (g.Role == GeomRole.Visual)
                {
                    // Both visual geoms point at the same material.
                    Assert.Equal("material_multi", g.Material);
                    Assert.Null(g.Rgba);
                }
                else
                {
                    // Collision geoms inherit from <default class="collision">.
                    Assert.Null(g.Material);
                    Assert.Null(g.Rgba);
                }
            }
        }

        [Fact]
        public void TestMJCFEmitsTextureWhenLinkHasTextureFilename()
        {
            // A link with a non-empty Material.Texture.wFilename should emit a
            // <texture> in <asset> and the corresponding <material> should
            // reference it via texture="...". The <compiler> tag should carry
            // a texturedir attribute. Texture must appear before material in
            // the emitted XML (MuJoCo requires that ordering).
            Link baseLink = new Link(null) { Name = "tex_link" };
            baseLink.Visual.Material.Color.SetColor(new double[] { 0.4, 0.5, 0.6, 1.0 });
            baseLink.Visual.Material.Name = "material_tex_link";
            // ExportHelper sets wFilename to the absolute SolidWorks-side path;
            // here we use a basename-only string since MJCFBuilder calls
            // Path.GetFileName on it. Using a path with a directory prefix also
            // works -- the builder strips it.
            baseLink.Visual.Material.Texture.wFilename = "C:/some/where/checker.png";

            Robot robot = new Robot { Name = "test_texture" };
            robot.SetBaseLink(baseLink);

            var aux = new Dictionary<string, LinkAuxiliary>
            {
                ["tex_link"] = new LinkAuxiliary
                {
                    VisualMeshes =
                    {
                        new MeshAssetRef { Name = "tex_link_visual", File = "tex_link_visual.STL" },
                    },
                },
            };

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", aux);

            // Texture asset registered with basename only.
            Assert.Single(model.Asset.Textures);
            Assert.Equal("texture_tex_link", model.Asset.Textures[0].Name);
            Assert.Equal("checker.png", model.Asset.Textures[0].File);

            // Material references the texture.
            Assert.Single(model.Asset.Materials);
            Assert.Equal("texture_tex_link", model.Asset.Materials[0].Texture);

            // Compiler carries the texturedir attribute.
            Assert.Equal(MJCFBuilder.DefaultTextureDir, model.Compiler.TextureDir);

            string xml;
            using (StringWriter sw = new StringWriter())
            {
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                using (XmlWriter writer = XmlWriter.Create(sw, settings))
                {
                    model.WriteMJCF(writer);
                }
                xml = sw.ToString();
            }

            // texturedir on <compiler>.
            Assert.Contains("texturedir=\"../textures/\"", xml);
            // <texture> element with the basename only.
            Assert.Contains("<texture name=\"texture_tex_link\"", xml);
            Assert.Contains("file=\"checker.png\"", xml);
            // <material> references it.
            Assert.Contains("texture=\"texture_tex_link\"", xml);

            // Texture must be emitted BEFORE material in <asset> (MuJoCo
            // requires materials only reference textures already declared).
            int textureIdx = xml.IndexOf("<texture", StringComparison.Ordinal);
            int materialIdx = xml.IndexOf("<material name=\"material_tex_link\"", StringComparison.Ordinal);
            Assert.True(textureIdx >= 0);
            Assert.True(materialIdx >= 0);
            Assert.True(textureIdx < materialIdx,
                "Expected <texture> to be emitted before <material> within <asset>.");
        }

        [Fact]
        public void TestMJCFNoTextureWhenLinkHasNoTextureFilename()
        {
            // The common case (no texture on the link) should produce a
            // <material> with no texture= attribute and no <texture>
            // element in <asset>.
            Link baseLink = new Link(null) { Name = "plain_link" };
            baseLink.Visual.Material.Color.SetColor(new double[] { 0.9, 0.9, 0.9, 1.0 });
            // Texture.wFilename left as the default empty string.

            Robot robot = new Robot { Name = "test_no_texture" };
            robot.SetBaseLink(baseLink);

            var aux = new Dictionary<string, LinkAuxiliary>
            {
                ["plain_link"] = new LinkAuxiliary
                {
                    VisualMeshes =
                    {
                        new MeshAssetRef { Name = "plain_link_visual", File = "plain_link_visual.STL" },
                    },
                },
            };

            MJCFModel model = MJCFBuilder.Build(robot, "meshes/", aux);

            Assert.Empty(model.Asset.Textures);
            Assert.Single(model.Asset.Materials);
            Assert.Null(model.Asset.Materials[0].Texture);
            Assert.Null(model.Compiler.TextureDir);

            string xml;
            using (StringWriter sw = new StringWriter())
            {
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                using (XmlWriter writer = XmlWriter.Create(sw, settings))
                {
                    model.WriteMJCF(writer);
                }
                xml = sw.ToString();
            }
            Assert.DoesNotContain("<texture", xml);
            Assert.DoesNotContain("texturedir=", xml);
        }

        [Fact]
        public void TestURDFEmitsMultipleVisualsAndCollisions()
        {
            // A Link with two visual groups + two collision groups should
            // serialize as two <visual> and two <collision> elements inside
            // the same <link>, with each one's <mesh filename=...> picking up
            // the per-group MeshFilename.
            Link link = new Link { Name = "multi" };
            link.Visual.Material.Color.Red = 1.0;
            link.Visual.Material.Color.Green = 1.0;
            link.Visual.Material.Color.Blue = 1.0;
            link.Visual.Material.Color.Alpha = 1.0;
            link.VisualGroups = new List<MeshGroup>
            {
                new MeshGroup("multi_outer") { MeshFilename = "package://x/meshes/multi_outer.STL" },
                new MeshGroup("multi_inner") { MeshFilename = "package://x/meshes/multi_inner.STL" },
            };
            link.CollisionGroups = new List<MeshGroup>
            {
                new MeshGroup("multi_hull_upper") { MeshFilename = "package://x/meshes/multi_hull_upper.STL" },
                new MeshGroup("multi_hull_lower") { MeshFilename = "package://x/meshes/multi_hull_lower.STL" },
            };

            string xml;
            using (StringWriter sw = new StringWriter())
            {
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true };
                using (XmlWriter writer = XmlWriter.Create(sw, settings))
                {
                    writer.WriteStartDocument();
                    link.WriteURDF(writer);
                    writer.WriteEndDocument();
                }
                xml = sw.ToString();
            }

            // Both visual mesh filenames should appear distinctly.
            Assert.Contains("multi_outer.STL", xml);
            Assert.Contains("multi_inner.STL", xml);
            // Both collision mesh filenames should appear distinctly.
            Assert.Contains("multi_hull_upper.STL", xml);
            Assert.Contains("multi_hull_lower.STL", xml);

            // Count opening tags to verify two <visual> and two <collision>.
            int visualCount = CountOccurrences(xml, "<visual>");
            int collisionCount = CountOccurrences(xml, "<collision>");
            Assert.Equal(2, visualCount);
            Assert.Equal(2, collisionCount);
        }

        [Fact]
        public void TestLegacySingleListMigratesToOneGroup()
        {
            // A Link with only legacy SWComponentPIDs populated (and no
            // VisualGroups) should pick up a single visual group on the next
            // call to MigrateLegacyComponents. Same for CollisionComponentPIDs.
            Link link = new Link { Name = "legacy_link" };
            // Simulate the state of a freshly-deserialized old config: groups
            // are empty, but the legacy PID lists carry data.
            link.VisualGroups.Clear();
            link.CollisionGroups.Clear();
            link.SWComponentPIDs = new List<byte[]>
            {
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
            };
            link.CollisionComponentPIDs = new List<byte[]>
            {
                new byte[] { 7, 8, 9 },
            };

            link.MigrateLegacyComponents();

            Assert.Single(link.VisualGroups);
            // Default group names are link-INDEPENDENT ("visual"/"collision");
            // the export pipeline prepends the link name when building the
            // mesh / geom name, so embedding it here too would double it.
            Assert.Equal("visual", link.VisualGroups[0].Name);
            Assert.Equal(2, link.VisualGroups[0].ComponentPIDs.Count);

            Assert.Single(link.CollisionGroups);
            Assert.Equal("collision", link.CollisionGroups[0].Name);
            Assert.Single(link.CollisionGroups[0].ComponentPIDs);

            // Idempotent: a second call should not duplicate the migration.
            link.MigrateLegacyComponents();
            Assert.Single(link.VisualGroups);
            Assert.Single(link.CollisionGroups);
        }

        // Counts the number of times `needle` occurs in `haystack`.
        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
            {
                return 0;
            }
            int count = 0;
            int idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }

        private static string WriteMJCFToString(MJCFModel model)
        {
            using (StringWriter sw = new StringWriter())
            {
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                using (XmlWriter writer = XmlWriter.Create(sw, settings))
                {
                    model.WriteMJCF(writer);
                }
                return sw.ToString();
            }
        }

        private static string WriteURDFToString(Robot robot)
        {
            using (StringWriter sw = new StringWriter())
            {
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                using (XmlWriter writer = XmlWriter.Create(sw, settings))
                {
                    robot.WriteURDF(writer);
                }
                return sw.ToString();
            }
        }

        private static double ReadDouble(XElement element, string attributeName)
        {
            return double.Parse(
                element.Attribute(attributeName).Value,
                CultureInfo.CreateSpecificCulture("en-US"));
        }

        [Fact]
        public void TestExportPackageURDFLayoutPreserved()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                ExportPackage pkg = new ExportPackage("test_urdf", tempDir, ExportFormat.URDF);
                Assert.EndsWith(".urdf", pkg.ModelExtension);
                Assert.Contains("urdf", pkg.WindowsModelsDirectory);
                Assert.NotNull(pkg.WindowsCMakeLists);
                Assert.NotNull(pkg.WindowsLaunchDirectory);
                Assert.NotNull(pkg.WindowsConfigDirectory);
                // MJCFMeshDir is unused in the URDF path.
                Assert.Null(pkg.MJCFMeshDir);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
