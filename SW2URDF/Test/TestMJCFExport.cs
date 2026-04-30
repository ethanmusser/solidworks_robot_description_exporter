using SW2URDF.MJCF;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.IO;
using System.Xml;
using Xunit;

namespace SW2URDF.Test
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
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
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
