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
using SW2RD.Input;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SW2RD.Test
{
    [Trait("Category", "SWFree")]
    public class TestJointValidation
    {
        [Fact]
        public void TestUrdfWritesUnlimitedRevoluteAsContinuous()
        {
            Joint joint = CreateJoint("revolute");

            XElement element = WriteJointToElement(joint);

            Assert.Equal("continuous", (string)element.Attribute("type"));
            Assert.Equal("revolute", joint.Type);
        }

        [Fact]
        public void TestUrdfWritesLimitedRevoluteAsRevolute()
        {
            Joint joint = CreateJoint("revolute");
            joint.Limit.SetLower(-90.0);
            joint.Limit.SetUpper(90.0);

            XElement element = WriteJointToElement(joint);

            Assert.Equal("revolute", (string)element.Attribute("type"));
        }

        [Fact]
        public void TestUrdfPrismaticWithoutLimitsReportsJointOnly()
        {
            Link baseLink = CreateTwoLinkRobot("prismatic", setLimits: false);

            string errors = ExportPropertyManager.CheckExportFieldErrors(baseLink, ExportFormat.URDF);

            Assert.Contains("slider_link / slider_joint (prismatic)", errors);
            Assert.Contains("Lower and Upper limits are missing", errors);
            Assert.Contains("Auto-compute Lower/Upper was enabled", errors);
            Assert.DoesNotContain("base_link", errors);
        }

        [Fact]
        public void TestUrdfPrismaticWithLimitsPassesValidation()
        {
            Link baseLink = CreateTwoLinkRobot("prismatic", setLimits: true);

            string errors = ExportPropertyManager.CheckExportFieldErrors(baseLink, ExportFormat.URDF);

            Assert.True(string.IsNullOrWhiteSpace(errors));
        }

        [Theory]
        [InlineData("revolute")]
        [InlineData("prismatic")]
        public void TestMjcfAllowsUnlimitedHingeAndSlide(string jointType)
        {
            Link baseLink = CreateTwoLinkRobot(jointType, setLimits: false);

            string errors = ExportPropertyManager.CheckExportFieldErrors(baseLink, ExportFormat.MJCF);

            Assert.True(string.IsNullOrWhiteSpace(errors));
        }

        [Theory]
        [InlineData("")]
        [InlineData("Automatically Detect")]
        [InlineData("planar")]
        public void TestUnsupportedJointTypeReportsClearError(string jointType)
        {
            Link baseLink = CreateTwoLinkRobot(jointType, setLimits: false);

            string errors = ExportPropertyManager.CheckExportFieldErrors(baseLink, ExportFormat.URDF);

            Assert.Contains("Choose fixed, revolute, or prismatic", errors);
        }

        private static Link CreateTwoLinkRobot(string jointType, bool setLimits)
        {
            Link baseLink = new Link(null) { Name = "base_link" };
            Link child = new Link(baseLink) { Name = "slider_link" };
            child.Joint.Name = "slider_joint";
            child.Joint.Type = jointType;
            child.Joint.Parent.Name = baseLink.Name;
            child.Joint.Child.Name = child.Name;
            child.Joint.Axis.SetXYZ(new[] { 1.0, 0.0, 0.0 });
            child.Joint.Origin.SetXYZ(new[] { 0.0, 0.0, 0.0 });
            child.Joint.Origin.SetRPY(new[] { 0.0, 0.0, 0.0 });
            child.Joint.AutoComputeLimits = true;
            if (setLimits)
            {
                child.Joint.Limit.SetLower(-0.1);
                child.Joint.Limit.SetUpper(0.1);
            }
            baseLink.Children.Add(child);
            return baseLink;
        }

        private static Joint CreateJoint(string jointType)
        {
            Joint joint = new Joint
            {
                Name = "joint",
                Type = jointType,
            };
            joint.Parent.Name = "base_link";
            joint.Child.Name = "child_link";
            joint.Axis.SetXYZ(new[] { 0.0, 0.0, 1.0 });
            joint.Origin.SetXYZ(new[] { 0.0, 0.0, 0.0 });
            joint.Origin.SetRPY(new[] { 0.0, 0.0, 0.0 });
            return joint;
        }

        // URDF is now emitted from the canonical KinematicTree by URDFBuilder.
        // Build a minimal two-link robot carrying the joint, convert it at the
        // adapter boundary, write the URDF, and return the single <joint>
        // element so the joint-type-resolution assertions can inspect it.
        private static XElement WriteJointToElement(Joint joint)
        {
            Link baseLink = new Link(null) { Name = joint.Parent.Name };
            Link child = new Link(baseLink) { Name = joint.Child.Name };
            child.Joint = joint;
            baseLink.Children.Add(child);

            Robot robot = new Robot { Name = "joint_validation" };
            robot.SetBaseLink(baseLink);
            KinematicTree tree = KinematicTreeAdapter.ToCore(robot);

            string xml;
            using (StringWriter stringWriter = new StringWriter())
            {
                using (XmlWriter writer = XmlWriter.Create(stringWriter, new XmlWriterSettings
                {
                    Indent = true,
                }))
                {
                    URDFBuilder.Write(tree, writer);
                }
                xml = stringWriter.ToString();
            }
            return XDocument.Parse(xml).Descendants("joint").Single();
        }
    }
}
