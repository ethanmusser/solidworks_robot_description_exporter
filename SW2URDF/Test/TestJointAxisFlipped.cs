using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.URDFExport.CSV;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Unit tests for the persisted Joint.AxisFlipped field added with the
    /// PropertyManager "Reverse Direction" toggle. Covers the four-paths
    /// landmine for new Joint-scope fields described in AGENTS.md
    /// (SetElement + SetJointKinematics + CSV round-trip + ContextToColumns)
    /// plus the actual sign-negation behavior at SW resolution time via the
    /// public PreviewAxisDirection helper.
    /// </summary>
    public class TestJointAxisFlipped : SW2URDFTest
    {
        public TestJointAxisFlipped(SWTestFixture fixture) : base(fixture)
        {
        }

        // Verifies that a true value on Joint.AxisFlipped survives a
        // SetElement copy (which is the authoritative clone path used by
        // Link.Clone after every DataContractSerializer reload). Without the
        // explicit `AxisFlipped = joint.AxisFlipped;` line in
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

        // Round-trips AxisFlipped through the CSV dictionary path used by
        // ImportExport.WriteRobotToCSV / LoadURDFRobotFromCSV. This is the
        // CSV side of the four-paths landmine - if AppendToCSVDictionary
        // and SetElementFromData use mismatched context strings, the value
        // silently doesn't round-trip.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestCSVRoundTripsAxisFlipped(bool flipped)
        {
            Joint source = new Joint
            {
                AxisFlipped = flipped,
                CoordinateSystemName = "Origin_x",
                AxisName = "Axis_x",
                Type = "revolute",
                Name = "joint_x",
            };

            // AppendToCSVDictionary expects the same context shape that
            // Link.AppendToCSVDictionary sets up - "Link" as the type-name
            // prefix - because Link is what owns Joint as a child element.
            List<string> context = new List<string> { "Link" };
            OrderedDictionary csvDict = new OrderedDictionary();
            source.AppendToCSVDictionary(context, csvDict);

            Assert.Contains("Link.Joint.AxisFlipped", csvDict.Keys.Cast<string>());
            Assert.Equal(flipped.ToString(), (string)csvDict["Link.Joint.AxisFlipped"]);

            // Build a StringDictionary the way CSVImportExport.BuildLinkFromData
            // does (string-keyed lookup) and round-trip back into a fresh Joint.
            StringDictionary readbackDict = new StringDictionary();
            foreach (DictionaryEntry entry in csvDict)
            {
                readbackDict[(string)entry.Key] = entry.Value as string;
            }

            Joint dest = new Joint();
            dest.SetElementFromData(context, readbackDict);

            Assert.Equal(flipped, dest.AxisFlipped);
        }

        // Verifies that the column is present in the canonical CSV column
        // mapping so it both gets emitted in WriteRobotToCSV's header row
        // AND gets recognized in LoadURDFRobotFromCSV's column lookup.
        [Fact]
        public void TestContextToColumnsIncludesAxisFlipped()
        {
            Assert.True(ContextToColumns.Dictionary.Contains("Link.Joint.AxisFlipped"));
            Assert.Equal("Joint Axis Flipped",
                (string)ContextToColumns.Dictionary["Link.Joint.AxisFlipped"]);
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
