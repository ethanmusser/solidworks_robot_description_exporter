/*
Copyright (c) 2015 Stephen Brawner

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

using SW2URDF.MJCF;
using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SW2URDF.Test
{
    // Unit tests for MjcfWriter. Deliberately does NOT inherit SW2URDFTest / take the
    // SWTestFixture because the writer is fully decoupled from SolidWorks and these tests must
    // be runnable in environments where SolidWorks is not installed.
    public class TestMjcfWriter
    {
        /// <summary>
        /// Runs MjcfWriter over the given Robot and returns the produced MJCF as an XDocument,
        /// for XPath / attribute assertions.
        /// </summary>
        private static XDocument WriteToXDoc(
            Robot robot,
            MjcfOptions options,
            IDictionary<string, List<MjcfSite>> linkSites = null,
            IDictionary<string, string> linkMeshFilenames = null)
        {
            StringBuilder sb = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = false,
            };
            using (XmlWriter xw = XmlWriter.Create(sb, settings))
            {
                MjcfWriter.WriteTo(robot, options, xw, linkSites, linkMeshFilenames);
            }
            return XDocument.Parse(sb.ToString());
        }

        private static Link MakeLink(string name, bool fixedFrame = false, bool withMesh = true,
            double mass = 0, double[] inertiaDiag = null, string meshFilename = null)
        {
            Link link = new Link { Name = name, isFixedFrame = fixedFrame };
            if (withMesh)
            {
                link.Visual.Geometry.Mesh.Filename = meshFilename ?? (name + ".STL");
                link.Collision.Geometry.Mesh.Filename = meshFilename ?? (name + ".STL");
            }
            if (mass > 0)
            {
                link.Inertial.Mass.Value = mass;
                if (inertiaDiag != null && inertiaDiag.Length == 3)
                {
                    link.Inertial.Inertia.Ixx = inertiaDiag[0];
                    link.Inertial.Inertia.Iyy = inertiaDiag[1];
                    link.Inertial.Inertia.Izz = inertiaDiag[2];
                }
            }
            return link;
        }

        private static void SetJoint(Link child, string jointName, string type, string parentName,
            double[] xyz = null, double[] axis = null, double? lower = null, double? upper = null)
        {
            child.Joint.Name = jointName;
            child.Joint.Type = type;
            child.Joint.Parent.Name = parentName;
            child.Joint.Child.Name = child.Name;
            if (xyz != null)
            {
                child.Joint.Origin.SetXYZ(xyz);
            }
            if (axis != null)
            {
                child.Joint.Axis.SetXYZ(axis);
            }
            if (lower.HasValue && upper.HasValue)
            {
                child.Joint.Limit.Lower = lower.Value;
                child.Joint.Limit.Upper = upper.Value;
                child.Joint.Limit.Effort = 10;
                child.Joint.Limit.Velocity = 1;
            }
        }

        private static Robot MakeRobot(string name, Link baseLink)
        {
            Robot r = new Robot { Name = name };
            r.SetBaseLink(baseLink);
            return r;
        }

        private static MjcfOptions DefaultOptions(
            MjcfActuatorType actuator = MjcfActuatorType.None,
            bool excludeContacts = false,
            bool mimic = false)
        {
            return new MjcfOptions
            {
                Timestep = 0.002,
                Integrator = MjcfIntegrator.RK4,
                Gravity = new double[] { 0, 0, -9.81 },
                MeshDir = "meshes",
                ActuatorType = actuator,
                ActuatorGain = 1.0,
                ExcludeAdjacentContacts = excludeContacts,
                EmitMimicEqualities = mimic,
            };
        }

        // Headers and asset section ------------------------------------------------

        [Fact]
        public void SingleLinkRobot_ProducesMujocoRootAndHeaderElements()
        {
            Link root = MakeLink("base_link", mass: 1.0, inertiaDiag: new[] { 1e-3, 1e-3, 1e-3 });
            Robot robot = MakeRobot("my_bot", root);

            XDocument doc = WriteToXDoc(robot, DefaultOptions());

            Assert.Equal("mujoco", doc.Root.Name.LocalName);
            Assert.Equal("my_bot", doc.Root.Attribute("model").Value);

            XElement compiler = doc.Root.Element("compiler");
            Assert.NotNull(compiler);
            Assert.Equal("radian", compiler.Attribute("angle").Value);
            Assert.Equal("meshes", compiler.Attribute("meshdir").Value);
            Assert.Equal("true", compiler.Attribute("autolimits").Value);

            XElement option = doc.Root.Element("option");
            Assert.NotNull(option);
            Assert.Equal("0.002", option.Attribute("timestep").Value);
            Assert.Equal("RK4", option.Attribute("integrator").Value);
            Assert.Equal("0 0 -9.81", option.Attribute("gravity").Value);

            XElement worldbody = doc.Root.Element("worldbody");
            Assert.NotNull(worldbody);
            XElement body = worldbody.Element("body");
            Assert.NotNull(body);
            Assert.Equal("base_link", body.Attribute("name").Value);
        }

        [Fact]
        public void EmptyMeshDir_OmitsMeshDirAttribute()
        {
            Link root = MakeLink("base", withMesh: false);
            Robot robot = MakeRobot("r", root);

            MjcfOptions opts = DefaultOptions();
            opts.MeshDir = "";

            XDocument doc = WriteToXDoc(robot, opts);
            XElement compiler = doc.Root.Element("compiler");
            Assert.Null(compiler.Attribute("meshdir"));
        }

        [Fact]
        public void NullGravity_OmitsGravityAttribute()
        {
            Link root = MakeLink("base", withMesh: false);
            Robot robot = MakeRobot("r", root);

            MjcfOptions opts = DefaultOptions();
            opts.Gravity = null;

            XDocument doc = WriteToXDoc(robot, opts);
            XElement option = doc.Root.Element("option");
            Assert.Null(option.Attribute("gravity"));
        }

        [Fact]
        public void MeshLinks_ProduceAssetEntries()
        {
            Link root = MakeLink("base_link");
            Link child = MakeLink("link1");
            SetJoint(child, "j1", "revolute", "base_link",
                xyz: new[] { 0.0, 0.0, 0.1 }, axis: new[] { 0.0, 0.0, 1.0 },
                lower: -1.0, upper: 1.0);
            child.Parent = root;
            root.Children.Add(child);
            Robot robot = MakeRobot("two_link", root);

            Dictionary<string, string> files = new Dictionary<string, string>
            {
                { "base_link", "base_link.STL" },
                { "link1", "link1.STL" },
            };

            XDocument doc = WriteToXDoc(robot, DefaultOptions(), linkMeshFilenames: files);
            XElement asset = doc.Root.Element("asset");
            Assert.NotNull(asset);
            List<XElement> meshes = asset.Elements("mesh").ToList();
            Assert.Equal(2, meshes.Count);
            Assert.Contains(meshes, m => m.Attribute("name").Value == "base_link"
                && m.Attribute("file").Value == "base_link.STL");
            Assert.Contains(meshes, m => m.Attribute("name").Value == "link1"
                && m.Attribute("file").Value == "link1.STL");
        }

        [Fact]
        public void FixedFrameLink_ExcludedFromAssets()
        {
            Link root = MakeLink("base");
            Link fixedChild = MakeLink("fixed_child", fixedFrame: true, withMesh: false);
            SetJoint(fixedChild, "jfix", "fixed", "base");
            fixedChild.Parent = root;
            root.Children.Add(fixedChild);

            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());

            XElement asset = doc.Root.Element("asset");
            // base has a mesh -> asset exists, but fixed_child should not be listed.
            Assert.NotNull(asset);
            Assert.DoesNotContain(asset.Elements("mesh"),
                m => m.Attribute("name").Value == "fixed_child");
        }

        // Joint translations --------------------------------------------------------

        [Fact]
        public void RevoluteJoint_WritesHingeWithAxisAndRange()
        {
            Link root = MakeLink("base");
            Link link1 = MakeLink("link1");
            SetJoint(link1, "joint1", "revolute", "base",
                xyz: new[] { 0.1, 0.2, 0.3 },
                axis: new[] { 0.0, 1.0, 0.0 },
                lower: -1.5, upper: 1.5);
            link1.Parent = root;
            root.Children.Add(link1);

            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());

            XElement baseBody = doc.Root.Element("worldbody").Element("body");
            XElement childBody = baseBody.Elements("body").Single();
            Assert.Equal("link1", childBody.Attribute("name").Value);
            Assert.Equal("0.1 0.2 0.3", childBody.Attribute("pos").Value);

            XElement joint = childBody.Element("joint");
            Assert.NotNull(joint);
            Assert.Equal("joint1", joint.Attribute("name").Value);
            Assert.Equal("hinge", joint.Attribute("type").Value);
            Assert.Equal("0 1 0", joint.Attribute("axis").Value);
            Assert.Equal("-1.5 1.5", joint.Attribute("range").Value);
        }

        [Fact]
        public void ContinuousJoint_WritesHingeWithoutRange()
        {
            Link root = MakeLink("base");
            Link link1 = MakeLink("link1");
            SetJoint(link1, "joint1", "continuous", "base",
                axis: new[] { 0.0, 0.0, 1.0 });
            link1.Parent = root;
            root.Children.Add(link1);

            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            XElement joint = doc.Descendants("joint").First(j => j.Attribute("type")?.Value == "hinge");
            Assert.Null(joint.Attribute("range"));
        }

        [Fact]
        public void PrismaticJoint_WritesSlide()
        {
            Link root = MakeLink("base");
            Link link1 = MakeLink("link1");
            SetJoint(link1, "j_slide", "prismatic", "base",
                axis: new[] { 1.0, 0.0, 0.0 }, lower: 0.0, upper: 0.1);
            link1.Parent = root;
            root.Children.Add(link1);

            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            XElement joint = doc.Descendants("joint").Single();
            Assert.Equal("slide", joint.Attribute("type").Value);
            Assert.Equal("0 0.1", joint.Attribute("range").Value);
        }

        [Fact]
        public void FixedJoint_OmitsJointElement()
        {
            Link root = MakeLink("base");
            Link link1 = MakeLink("link1");
            SetJoint(link1, "j_fixed", "fixed", "base",
                xyz: new[] { 0.0, 0.0, 0.05 });
            link1.Parent = root;
            root.Children.Add(link1);

            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            XElement childBody = doc.Root.Element("worldbody").Element("body").Element("body");
            Assert.Null(childBody.Element("joint"));
            Assert.Equal("0 0 0.05", childBody.Attribute("pos").Value);
        }

        [Fact]
        public void PlanarJoint_EmitsTwoOrthogonalSlides()
        {
            Link root = MakeLink("base");
            Link link1 = MakeLink("link1");
            SetJoint(link1, "j_planar", "planar", "base",
                axis: new[] { 0.0, 0.0, 1.0 });
            link1.Parent = root;
            root.Children.Add(link1);

            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            List<XElement> joints = doc.Descendants("joint").ToList();
            Assert.Equal(2, joints.Count);
            Assert.All(joints, j => Assert.Equal("slide", j.Attribute("type").Value));
            Assert.Contains(joints, j => j.Attribute("name").Value == "j_planar_x");
            Assert.Contains(joints, j => j.Attribute("name").Value == "j_planar_y");
        }

        [Fact]
        public void FloatingRoot_EmitsFreeJoint()
        {
            Link root = MakeLink("base");
            root.Joint.Name = "root_free";
            root.Joint.Type = "floating";
            Robot robot = MakeRobot("r", root);

            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            XElement joint = doc.Root.Element("worldbody").Element("body").Element("joint");
            Assert.NotNull(joint);
            Assert.Equal("free", joint.Attribute("type").Value);
            Assert.Equal("root_free", joint.Attribute("name").Value);
        }

        // Inertial ------------------------------------------------------------------

        [Fact]
        public void Inertial_EmittedWithFullInertiaInMjcfOrder()
        {
            Link root = MakeLink("base");
            root.Inertial.Mass.Value = 2.5;
            root.Inertial.Inertia.Ixx = 0.1;
            root.Inertial.Inertia.Iyy = 0.2;
            root.Inertial.Inertia.Izz = 0.3;
            root.Inertial.Inertia.Ixy = 0.01;
            root.Inertial.Inertia.Ixz = 0.02;
            root.Inertial.Inertia.Iyz = 0.03;
            root.Inertial.Origin.SetXYZ(new[] { 0.01, 0.02, 0.03 });

            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            XElement inertial = doc.Descendants("inertial").Single();
            Assert.Equal("2.5", inertial.Attribute("mass").Value);
            Assert.Equal("0.01 0.02 0.03", inertial.Attribute("pos").Value);

            string[] parts = inertial.Attribute("fullinertia").Value.Split(' ');
            Assert.Equal(6, parts.Length);
            Assert.Equal(0.1, double.Parse(parts[0], CultureInfo.InvariantCulture), 6);
            Assert.Equal(0.2, double.Parse(parts[1], CultureInfo.InvariantCulture), 6);
            Assert.Equal(0.3, double.Parse(parts[2], CultureInfo.InvariantCulture), 6);
            Assert.Equal(0.01, double.Parse(parts[3], CultureInfo.InvariantCulture), 6);
            Assert.Equal(0.02, double.Parse(parts[4], CultureInfo.InvariantCulture), 6);
            Assert.Equal(0.03, double.Parse(parts[5], CultureInfo.InvariantCulture), 6);
        }

        [Fact]
        public void ZeroMass_SuppressesInertial()
        {
            Link root = MakeLink("base");
            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            Assert.Empty(doc.Descendants("inertial"));
        }

        // Geoms ---------------------------------------------------------------------

        [Fact]
        public void VisualAndCollisionGeoms_EmittedPerLink()
        {
            Link root = MakeLink("base_link", mass: 1.0);
            Robot robot = MakeRobot("r", root);

            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            List<XElement> geoms = doc.Root.Element("worldbody").Element("body").Elements("geom").ToList();
            Assert.Equal(2, geoms.Count);

            XElement visualGeom = geoms.Single(g => g.Attribute("group")?.Value == "1");
            Assert.Equal("mesh", visualGeom.Attribute("type").Value);
            Assert.Equal("base_link", visualGeom.Attribute("mesh").Value);
            Assert.Equal("0", visualGeom.Attribute("contype").Value);
            Assert.Equal("0", visualGeom.Attribute("conaffinity").Value);

            XElement collisionGeom = geoms.Single(g => g.Attribute("group")?.Value == "3");
            Assert.Equal("mesh", collisionGeom.Attribute("type").Value);
            Assert.Equal("base_link", collisionGeom.Attribute("mesh").Value);
        }

        // Chain traversal -----------------------------------------------------------

        [Fact]
        public void ChainOfThreeRevolute_NestedBodiesWithJoints()
        {
            Link root = MakeLink("L0");
            Link l1 = MakeLink("L1");
            Link l2 = MakeLink("L2");
            SetJoint(l1, "j1", "revolute", "L0",
                xyz: new[] { 0.0, 0.0, 0.1 }, axis: new[] { 0.0, 0.0, 1.0 },
                lower: -1, upper: 1);
            SetJoint(l2, "j2", "revolute", "L1",
                xyz: new[] { 0.1, 0.0, 0.0 }, axis: new[] { 0.0, 1.0, 0.0 },
                lower: -1, upper: 1);
            l1.Parent = root; root.Children.Add(l1);
            l2.Parent = l1; l1.Children.Add(l2);

            Robot robot = MakeRobot("arm", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());

            XElement b0 = doc.Root.Element("worldbody").Element("body");
            Assert.Equal("L0", b0.Attribute("name").Value);
            XElement b1 = b0.Elements("body").Single();
            Assert.Equal("L1", b1.Attribute("name").Value);
            XElement b2 = b1.Elements("body").Single();
            Assert.Equal("L2", b2.Attribute("name").Value);

            Assert.Equal("j1", b1.Element("joint").Attribute("name").Value);
            Assert.Equal("j2", b2.Element("joint").Attribute("name").Value);
        }

        // Sites ---------------------------------------------------------------------

        [Fact]
        public void Sites_EmittedInsideBody()
        {
            Link root = MakeLink("base", mass: 1.0);
            Robot robot = MakeRobot("r", root);
            Dictionary<string, List<MjcfSite>> linkSites = new Dictionary<string, List<MjcfSite>>
            {
                { "base", new List<MjcfSite>
                    {
                        new MjcfSite("tool_tip", new double[] { 0.01, 0.02, 0.03 }, new double[] { 0, 0, 0 }),
                        new MjcfSite("imu", new double[] { 0, 0, 0 }, new double[] { 0, 0, 0 }),
                    }
                }
            };

            XDocument doc = WriteToXDoc(robot, DefaultOptions(), linkSites: linkSites);
            List<XElement> sites = doc.Root.Element("worldbody").Element("body").Elements("site").ToList();
            Assert.Equal(2, sites.Count);

            XElement tip = sites.Single(s => s.Attribute("name").Value == "tool_tip");
            Assert.Equal("0.01 0.02 0.03", tip.Attribute("pos").Value);
            // Zero rpy -> no quat attribute (no silent defaults).
            Assert.Null(tip.Attribute("quat"));

            XElement imu = sites.Single(s => s.Attribute("name").Value == "imu");
            // Zero xyz -> no pos attribute either.
            Assert.Null(imu.Attribute("pos"));
        }

        [Fact]
        public void Site_WithRotation_EmitsQuat()
        {
            Link root = MakeLink("base", mass: 1.0);
            Robot robot = MakeRobot("r", root);
            Dictionary<string, List<MjcfSite>> linkSites = new Dictionary<string, List<MjcfSite>>
            {
                { "base", new List<MjcfSite>
                    {
                        new MjcfSite("rotated",
                            new double[] { 0, 0, 0 },
                            new double[] { 0, 0, Math.PI / 2 }),
                    }
                }
            };
            XDocument doc = WriteToXDoc(robot, DefaultOptions(), linkSites: linkSites);
            XElement site = doc.Root.Element("worldbody").Element("body").Element("site");
            Assert.NotNull(site.Attribute("quat"));

            // Expected quaternion for yaw=pi/2 is [cos(pi/4), 0, 0, sin(pi/4)].
            string[] parts = site.Attribute("quat").Value.Split(' ');
            Assert.Equal(4, parts.Length);
            double w = double.Parse(parts[0], CultureInfo.InvariantCulture);
            double qz = double.Parse(parts[3], CultureInfo.InvariantCulture);
            Assert.Equal(Math.Cos(Math.PI / 4), w, 5);
            Assert.Equal(Math.Sin(Math.PI / 4), qz, 5);
        }

        [Fact]
        public void NoSitesForLink_NoSiteElement()
        {
            Link root = MakeLink("base", mass: 1.0);
            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            Assert.Empty(doc.Descendants("site"));
        }

        // Mimic equalities ----------------------------------------------------------

        [Fact]
        public void MimicJoint_EmitsEqualityConstraint_WhenOptedIn()
        {
            Link root = MakeLink("base");
            Link leftFinger = MakeLink("left_finger");
            Link rightFinger = MakeLink("right_finger");
            SetJoint(leftFinger, "left_finger_joint", "prismatic", "base",
                axis: new[] { 1.0, 0.0, 0.0 }, lower: 0, upper: 0.04);
            SetJoint(rightFinger, "right_finger_joint", "prismatic", "base",
                axis: new[] { -1.0, 0.0, 0.0 }, lower: 0, upper: 0.04);
            rightFinger.Joint.Mimic.JointName = "left_finger_joint";
            rightFinger.Joint.Mimic.Multiplier = 1.0;
            rightFinger.Joint.Mimic.Offset = 0.0;

            leftFinger.Parent = root; root.Children.Add(leftFinger);
            rightFinger.Parent = root; root.Children.Add(rightFinger);
            Robot robot = MakeRobot("gripper", root);

            XDocument doc = WriteToXDoc(robot, DefaultOptions(mimic: true));
            XElement equality = doc.Root.Element("equality");
            Assert.NotNull(equality);
            XElement eq = equality.Element("joint");
            Assert.NotNull(eq);
            Assert.Equal("right_finger_joint", eq.Attribute("joint1").Value);
            Assert.Equal("left_finger_joint", eq.Attribute("joint2").Value);
            Assert.Equal("0 1 0 0 0", eq.Attribute("polycoef").Value);
        }

        [Fact]
        public void MimicJoint_NoEquality_WhenOptedOut()
        {
            Link root = MakeLink("base");
            Link f = MakeLink("f");
            SetJoint(f, "jf", "prismatic", "base", axis: new[] { 1.0, 0.0, 0.0 });
            f.Joint.Mimic.JointName = "other";
            f.Joint.Mimic.Multiplier = 1.0;
            f.Joint.Mimic.Offset = 0.0;
            f.Parent = root; root.Children.Add(f);
            Robot robot = MakeRobot("r", root);

            XDocument doc = WriteToXDoc(robot, DefaultOptions(mimic: false));
            Assert.Empty(doc.Descendants("equality"));
        }

        // Contacts ------------------------------------------------------------------

        [Fact]
        public void ExcludeAdjacentContacts_EmitsContactExcludesForEachEdge()
        {
            Link root = MakeLink("base");
            Link l1 = MakeLink("l1");
            Link l2 = MakeLink("l2");
            SetJoint(l1, "j1", "revolute", "base", axis: new[] { 0.0, 0.0, 1.0 },
                lower: -1, upper: 1);
            SetJoint(l2, "j2", "revolute", "l1", axis: new[] { 0.0, 0.0, 1.0 },
                lower: -1, upper: 1);
            l1.Parent = root; root.Children.Add(l1);
            l2.Parent = l1; l1.Children.Add(l2);
            Robot robot = MakeRobot("r", root);

            XDocument doc = WriteToXDoc(robot, DefaultOptions(excludeContacts: true));
            XElement contact = doc.Root.Element("contact");
            Assert.NotNull(contact);
            List<XElement> excludes = contact.Elements("exclude").ToList();
            Assert.Equal(2, excludes.Count);
            Assert.Contains(excludes, e => e.Attribute("body1").Value == "base"
                && e.Attribute("body2").Value == "l1");
            Assert.Contains(excludes, e => e.Attribute("body1").Value == "l1"
                && e.Attribute("body2").Value == "l2");
        }

        [Fact]
        public void ExcludeAdjacentContacts_Disabled_NoContactSection()
        {
            Link root = MakeLink("base");
            Link l1 = MakeLink("l1");
            SetJoint(l1, "j", "revolute", "base", axis: new[] { 0.0, 0.0, 1.0 },
                lower: -1, upper: 1);
            l1.Parent = root; root.Children.Add(l1);
            Robot robot = MakeRobot("r", root);

            XDocument doc = WriteToXDoc(robot, DefaultOptions(excludeContacts: false));
            Assert.Empty(doc.Descendants("contact"));
        }

        // Actuators -----------------------------------------------------------------

        [Theory]
        [InlineData(MjcfActuatorType.Motor, "motor", "gear")]
        [InlineData(MjcfActuatorType.Position, "position", "kp")]
        [InlineData(MjcfActuatorType.Velocity, "velocity", "kv")]
        public void Actuators_Emitted_WithCorrectElementAndGain(
            MjcfActuatorType actuator, string expectedElement, string expectedAttr)
        {
            Link root = MakeLink("base");
            Link l1 = MakeLink("l1");
            SetJoint(l1, "j1", "revolute", "base",
                axis: new[] { 0.0, 0.0, 1.0 }, lower: -1, upper: 1);
            l1.Parent = root; root.Children.Add(l1);
            Robot robot = MakeRobot("r", root);

            MjcfOptions opts = DefaultOptions(actuator: actuator);
            opts.ActuatorGain = 42.0;

            XDocument doc = WriteToXDoc(robot, opts);
            XElement actuatorSection = doc.Root.Element("actuator");
            Assert.NotNull(actuatorSection);
            XElement act = actuatorSection.Elements(expectedElement).Single();
            Assert.Equal("j1_act", act.Attribute("name").Value);
            Assert.Equal("j1", act.Attribute("joint").Value);
            Assert.Equal("42", act.Attribute(expectedAttr).Value);
        }

        [Fact]
        public void ActuatorTypeNone_NoActuatorSection()
        {
            Link root = MakeLink("base");
            Link l1 = MakeLink("l1");
            SetJoint(l1, "j1", "revolute", "base",
                axis: new[] { 0.0, 0.0, 1.0 }, lower: -1, upper: 1);
            l1.Parent = root; root.Children.Add(l1);
            Robot robot = MakeRobot("r", root);

            XDocument doc = WriteToXDoc(robot, DefaultOptions(actuator: MjcfActuatorType.None));
            Assert.Empty(doc.Descendants("actuator"));
        }

        [Fact]
        public void ActuatorSection_ExcludesFixedAndMimicJoints()
        {
            Link root = MakeLink("base");
            Link l1 = MakeLink("l1");
            Link l2 = MakeLink("l2");
            Link l3 = MakeLink("l3");
            SetJoint(l1, "j1", "revolute", "base", axis: new[] { 0.0, 0.0, 1.0 },
                lower: -1, upper: 1);
            SetJoint(l2, "j2", "fixed", "l1");
            SetJoint(l3, "j3", "revolute", "l1", axis: new[] { 0.0, 0.0, 1.0 },
                lower: -1, upper: 1);
            l3.Joint.Mimic.JointName = "j1";
            l3.Joint.Mimic.Multiplier = 1.0;
            l3.Joint.Mimic.Offset = 0.0;

            l1.Parent = root; root.Children.Add(l1);
            l2.Parent = l1; l1.Children.Add(l2);
            l3.Parent = l1; l1.Children.Add(l3);
            Robot robot = MakeRobot("r", root);

            XDocument doc = WriteToXDoc(robot, DefaultOptions(actuator: MjcfActuatorType.Motor));
            List<XElement> actuators = doc.Root.Element("actuator").Elements("motor").ToList();
            Assert.Single(actuators);
            Assert.Equal("j1", actuators[0].Attribute("joint").Value);
        }

        // Name sanitization ---------------------------------------------------------

        [Fact]
        public void LinkNameWithIllegalChars_IsSanitized()
        {
            Link root = MakeLink("base/link with space", mass: 1.0);
            Robot robot = MakeRobot("r", root);
            XDocument doc = WriteToXDoc(robot, DefaultOptions());
            XElement body = doc.Root.Element("worldbody").Element("body");
            Assert.Equal("base_link_with_space", body.Attribute("name").Value);
        }

        // RpyToQuat direct test -----------------------------------------------------

        [Fact]
        public void RpyToQuat_IdentityForZeroRpy()
        {
            double[] q = MjcfWriter.RpyToQuat(new double[] { 0, 0, 0 });
            Assert.Equal(1.0, q[0], 10);
            Assert.Equal(0.0, q[1], 10);
            Assert.Equal(0.0, q[2], 10);
            Assert.Equal(0.0, q[3], 10);
        }

        [Fact]
        public void RpyToQuat_PureRoll()
        {
            double[] q = MjcfWriter.RpyToQuat(new double[] { Math.PI / 2, 0, 0 });
            Assert.Equal(Math.Cos(Math.PI / 4), q[0], 10);
            Assert.Equal(Math.Sin(Math.PI / 4), q[1], 10);
            Assert.Equal(0.0, q[2], 10);
            Assert.Equal(0.0, q[3], 10);
        }

        // Round-trip through file system --------------------------------------------

        [Fact]
        public void WriteToFile_ProducesParseableXml()
        {
            Link root = MakeLink("base", mass: 1.0);
            Robot robot = MakeRobot("r", root);

            string tempPath = Path.Combine(Path.GetTempPath(),
                "mjcf_writer_test_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                MjcfWriter.Write(robot, DefaultOptions(), tempPath);
                Assert.True(File.Exists(tempPath));
                XDocument doc = XDocument.Load(tempPath);
                Assert.Equal("mujoco", doc.Root.Name.LocalName);
                Assert.Equal("r", doc.Root.Attribute("model").Value);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
