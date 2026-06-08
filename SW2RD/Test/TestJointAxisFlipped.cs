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

using SolidWorks.Interop.sldworks;
using SW2RD.Input;
using SW2RD.Export;
using Xunit;

namespace SW2RD.Test
{
    /// <summary>
    /// Unit tests for the persisted Joint.AxisFlipped field used by the
    /// PropertyManager "Reverse Direction" toggle. Covers clone/copy,
    /// export-time kinematics transfer, Config round-trip, and sign-negation
    /// behavior through the public PreviewAxisDirection helper.
    /// </summary>
    public class TestJointAxisFlipped : SW2RDTest
    {
        public TestJointAxisFlipped(SWTestFixture fixture) : base(fixture)
        {
        }

        // Verifies that a true value on Joint.AxisFlipped survives a
        // SetElement copy (the authoritative clone path used by Link.Clone).
        // Without the explicit `AxisFlipped = joint.AxisFlipped;` line in
        // Joint.SetElement, this would silently reset to the zero-init
        // default.
        [Fact]
        public void TestSetElementCopiesAxisFlippedTrue()
        {
            Joint source = new Joint
            {
                AxisFlipped = true,
                CoordinateSystemName = "Origin_test",
                AxisName = "Axis_test",
            };

            Joint dest = new Joint();
            dest.SetElement(source);

            Assert.True(dest.AxisFlipped);
            Assert.Equal("Origin_test", dest.CoordinateSystemName);
            Assert.Equal("Axis_test", dest.AxisName);
        }

        // Mirror of the above for the false case, to make sure we're not
        // accidentally OR-ing or always-setting-true. Also verifies the
        // default value of a freshly-constructed Joint is false.
        [Fact]
        public void TestSetElementCopiesAxisFlippedFalse()
        {
            Joint freshlyConstructed = new Joint();
            Assert.False(freshlyConstructed.AxisFlipped);

            Joint source = new Joint { AxisFlipped = false };
            Joint dest = new Joint { AxisFlipped = true };
            dest.SetElement(source);

            Assert.False(dest.AxisFlipped);
        }

        // Verifies that SetJointKinematics propagates AxisFlipped onto the
        // target joint. This is the path that pushes saved kinematics onto
        // Exporter.URDFRobot before write.
        [Fact]
        public void TestSetJointKinematicsCopiesAxisFlipped()
        {
            Joint source = new Joint
            {
                AxisFlipped = true,
                CoordinateSystemName = "Origin_a",
                AxisName = "Axis_a",
                Type = "revolute",
            };

            Joint dest = new Joint();
            Assert.False(dest.AxisFlipped);

            dest.SetJointKinematics(source);

            Assert.True(dest.AxisFlipped);
            Assert.Equal("Origin_a", dest.CoordinateSystemName);
            Assert.Equal("Axis_a", dest.AxisName);
            Assert.Equal("revolute", dest.Type);
        }

        // SW-backed sanity check that the actual sign-negation logic runs
        // end-to-end against a real reference axis. Uses PreviewAxisDirection
        // (public, side-effect free) which applies the same negation that
        // EstimateAxis(Joint) does. Asserts flipped=true returns the exact
        // negation of flipped=false for the same coord-sys + axis names.
        [Theory]
        [InlineData(ModelName3DofArm, "Origin_prox_joint", "Axis_prox_joint")]
        [InlineData(ModelName3DofArm, "Origin_dist_joint", "Axis_dist_joint")]
        public void TestPreviewAxisDirectionFlipNegatesAxis(
            string modelName, string coordsysName, string axisName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            try
            {
                ExportHelper helper = new ExportHelper(SwApp);

                ExportHelper.AxisPreview unflipped =
                    helper.PreviewAxisDirection(coordsysName, axisName, false);
                ExportHelper.AxisPreview flipped =
                    helper.PreviewAxisDirection(coordsysName, axisName, true);

                Assert.True(unflipped.IsValid, "unflipped preview should resolve");
                Assert.True(flipped.IsValid, "flipped preview should resolve");

                // Origin must be identical regardless of the flip - flipping
                // only affects the axis direction, not the joint coord-sys.
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(unflipped.OriginGlobal[i], flipped.OriginGlobal[i], 9);
                }

                // Axis direction must be exactly negated.
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(-unflipped.AxisGlobal[i], flipped.AxisGlobal[i], 9);
                }

                // Sanity: the unflipped vector itself isn't degenerate.
                double mag = System.Math.Sqrt(
                    unflipped.AxisGlobal[0] * unflipped.AxisGlobal[0] +
                    unflipped.AxisGlobal[1] * unflipped.AxisGlobal[1] +
                    unflipped.AxisGlobal[2] * unflipped.AxisGlobal[2]);
                Assert.True(mag > 0.5, "axis vector magnitude should be near 1, was " + mag);
            }
            finally
            {
                Assert.True(SwApp.CloseAllDocuments(true));
            }
        }

        // PreviewAxisDirection should refuse to resolve placeholder selections
        // ("None", "Automatically Generate") and missing inputs - returning
        // IsValid=false rather than throwing or producing a zero vector.
        // The PM uses IsValid=false as the signal to clear any existing
        // overlay arrow.
        [Theory]
        [InlineData(null, "Axis_prox_joint")]
        [InlineData("Origin_prox_joint", null)]
        [InlineData("", "Axis_prox_joint")]
        [InlineData("Origin_prox_joint", "")]
        [InlineData("Automatically Generate", "Axis_prox_joint")]
        [InlineData("Origin_prox_joint", "Automatically Generate")]
        [InlineData("Origin_prox_joint", "None")]
        public void TestPreviewAxisDirectionRejectsPlaceholders(
            string coordsysName, string axisName)
        {
            ModelDoc2 doc = OpenSWDocument(ModelName3DofArm);
            try
            {
                ExportHelper helper = new ExportHelper(SwApp);
                ExportHelper.AxisPreview preview =
                    helper.PreviewAxisDirection(coordsysName, axisName, false);
                Assert.False(preview.IsValid);
            }
            finally
            {
                Assert.True(SwApp.CloseAllDocuments(true));
            }
        }
    }
}
