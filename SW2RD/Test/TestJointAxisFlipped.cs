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

        // A freshly-constructed Joint defaults to the ReferenceAxis source
        // (zero-init), which means AutoDeriveAxis is false and the joint is
        // not a coordinate-system basis axis.
        [Fact]
        public void TestDefaultAxisSourceIsReferenceAxis()
        {
            Joint joint = new Joint();
            Assert.Equal(JointAxisSource.ReferenceAxis, joint.AxisSource);
            Assert.False(joint.AutoDeriveAxis);
            Assert.False(joint.UsesCoordinateSystemAxis);
        }

        // The AutoDeriveAxis / UsesCoordinateSystemAxis convenience getters
        // must track AxisSource exactly (single source of truth).
        [Theory]
        [InlineData(JointAxisSource.ReferenceAxis, false, false)]
        [InlineData(JointAxisSource.CoordinateSystemX, false, true)]
        [InlineData(JointAxisSource.CoordinateSystemY, false, true)]
        [InlineData(JointAxisSource.CoordinateSystemZ, false, true)]
        [InlineData(JointAxisSource.AutoDerive, true, false)]
        public void TestAxisSourceConvenienceGetters(
            JointAxisSource source, bool expectAutoDerive, bool expectCoordSysAxis)
        {
            Joint joint = new Joint { AxisSource = source };
            Assert.Equal(expectAutoDerive, joint.AutoDeriveAxis);
            Assert.Equal(expectCoordSysAxis, joint.UsesCoordinateSystemAxis);
        }

        // AxisSource must survive a SetElement copy (the authoritative clone
        // path). Without the explicit copy line, a non-default source would
        // silently reset to ReferenceAxis on clone.
        [Theory]
        [InlineData(JointAxisSource.CoordinateSystemX)]
        [InlineData(JointAxisSource.CoordinateSystemY)]
        [InlineData(JointAxisSource.CoordinateSystemZ)]
        [InlineData(JointAxisSource.AutoDerive)]
        public void TestSetElementCopiesAxisSource(JointAxisSource source)
        {
            Joint src = new Joint { AxisSource = source };
            Joint dest = new Joint();
            dest.SetElement(src);
            Assert.Equal(source, dest.AxisSource);
        }

        // AxisSource must survive a Clone (used by Link.Clone / duplicate).
        [Fact]
        public void TestCloneCopiesAxisSource()
        {
            Joint src = new Joint { AxisSource = JointAxisSource.CoordinateSystemZ };
            Joint clone = src.Clone();
            Assert.Equal(JointAxisSource.CoordinateSystemZ, clone.AxisSource);
        }

        // AxisSource must propagate through SetJointKinematics (the export-time
        // push onto Exporter.URDFRobot).
        [Theory]
        [InlineData(JointAxisSource.CoordinateSystemY)]
        [InlineData(JointAxisSource.AutoDerive)]
        public void TestSetJointKinematicsCopiesAxisSource(JointAxisSource source)
        {
            Joint src = new Joint { AxisSource = source, Type = "revolute" };
            Joint dest = new Joint();
            dest.SetJointKinematics(src);
            Assert.Equal(source, dest.AxisSource);
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

        // A coordinate-system basis axis source resolves to that coordinate
        // system's basis vector without needing a reference-axis pick. The
        // X / Y / Z basis vectors must be an orthonormal triad sharing the
        // coord-sys origin, and the reverse-direction flag must negate the
        // direction without moving the origin.
        [Theory]
        [InlineData(ModelName3DofArm, "Origin_prox_joint")]
        [InlineData(ModelName3DofArm, "Origin_dist_joint")]
        public void TestPreviewAxisDirectionCoordinateSystemBasisAxes(
            string modelName, string coordsysName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            try
            {
                ExportHelper helper = new ExportHelper(SwApp);

                // AxisName is intentionally empty - a basis axis needs only
                // the coordinate system.
                ExportHelper.AxisPreview x = helper.PreviewAxisDirection(
                    coordsysName, "", false, JointAxisSource.CoordinateSystemX);
                ExportHelper.AxisPreview y = helper.PreviewAxisDirection(
                    coordsysName, "", false, JointAxisSource.CoordinateSystemY);
                ExportHelper.AxisPreview z = helper.PreviewAxisDirection(
                    coordsysName, "", false, JointAxisSource.CoordinateSystemZ);

                Assert.True(x.IsValid, "X basis preview should resolve");
                Assert.True(y.IsValid, "Y basis preview should resolve");
                Assert.True(z.IsValid, "Z basis preview should resolve");

                // Each basis vector is unit length.
                Assert.Equal(1.0, Magnitude(x.AxisGlobal), 6);
                Assert.Equal(1.0, Magnitude(y.AxisGlobal), 6);
                Assert.Equal(1.0, Magnitude(z.AxisGlobal), 6);

                // The triad is mutually orthogonal.
                Assert.Equal(0.0, Dot(x.AxisGlobal, y.AxisGlobal), 6);
                Assert.Equal(0.0, Dot(y.AxisGlobal, z.AxisGlobal), 6);
                Assert.Equal(0.0, Dot(z.AxisGlobal, x.AxisGlobal), 6);

                // All three share the coordinate-system origin.
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(x.OriginGlobal[i], y.OriginGlobal[i], 9);
                    Assert.Equal(x.OriginGlobal[i], z.OriginGlobal[i], 9);
                }

                // Reverse-direction negates the basis axis but not the origin.
                ExportHelper.AxisPreview xFlipped = helper.PreviewAxisDirection(
                    coordsysName, "", true, JointAxisSource.CoordinateSystemX);
                Assert.True(xFlipped.IsValid);
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(-x.AxisGlobal[i], xFlipped.AxisGlobal[i], 9);
                    Assert.Equal(x.OriginGlobal[i], xFlipped.OriginGlobal[i], 9);
                }
            }
            finally
            {
                Assert.True(SwApp.CloseAllDocuments(true));
            }
        }

        private static double Magnitude(double[] v)
        {
            return System.Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }
    }
}
