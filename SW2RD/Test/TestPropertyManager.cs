using SW2RD.SW;
using SW2RD.Export;
using SW2RD.URDF;
using System.Reflection;
using System.Runtime.Serialization;
using Xunit;

namespace SW2RD.Test
{
    /// <summary>
    ///  TODO (SIMINT-164), code in UI components needs to be tested, but
    ///  pm.show() crashes SolidWorks. 
    /// </summary>
    [Collection("Requires SW Test Collection")]
    public class TestPropertyManager : SW2RDTest
    {
        public TestPropertyManager(SWTestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public void TestGeneratedLinksStartWithExpectedNamesAndNoVisualGroups()
        {
            ExportPropertyManager pm = CreateUninitializedPropertyManager();

            LinkNode freshRoot = pm.CreateEmptyNode(null);
            WorldNode world = Xunit.Assert.IsType<WorldNode>(freshRoot);
            Xunit.Assert.Single(world.Nodes);
            LinkNode generatedTopLevel = Xunit.Assert.IsType<LinkNode>(world.Nodes[0]);
            Xunit.Assert.Equal("base_link", generatedTopLevel.Link.Name);
            Xunit.Assert.Empty(generatedTopLevel.Link.VisualGroups);

            LinkNode directWorldChild = pm.CreateEmptyNode(world);
            Xunit.Assert.Equal("base_link", directWorldChild.Link.Name);
            Xunit.Assert.Empty(directWorldChild.Link.VisualGroups);

            LinkNode nestedChild = pm.CreateEmptyNode(generatedTopLevel);
            Xunit.Assert.Equal("empty_link", nestedChild.Link.Name);
            Xunit.Assert.Empty(nestedChild.Link.VisualGroups);
        }

        private static ExportPropertyManager CreateUninitializedPropertyManager()
        {
            return (ExportPropertyManager)FormatterServices.GetUninitializedObject(
                typeof(ExportPropertyManager));
        }


        // TODO(SIMINT-164) pm.Show() crashes with drag drop 
        //[Theory]
        //[InlineData("3_DOF_ARM")]
        public void TestPropertyManagerOpens(string modelName)
        {
            OpenSWDocument(modelName);
            SwAddin addin = new SwAddin();
            addin.ConnectToSW(SwApp, 0);
            addin.SetupAssemblyExporter();
            SwApp.CloseAllDocuments(true);
        }

        // TODO(SIMINT-164) pm.Show() crashes with drag drop 
        //[Theory]
        //[InlineData("3_DOF_ARM")]
        public void TestPropertyManagerOpenCloseOk(string modelName)
        {
            OpenSWDocument(modelName);

            ExportPropertyManager pm = new ExportPropertyManager(SwApp);
            pm.Show();
            pm.Close(true);
            SwApp.CloseAllDocuments(true);
            Xunit.Assert.True(true, "Property manager failed to open/close with okay");
        }

        // TODO(SIMINT-164) pm.Show() crashes with drag drop 
        //[Theory]
        //[InlineData("3_DOF_ARM")]
        public void TestPropertyManagerOpenCloseNotOk(string modelName)
        {
            OpenSWDocument(modelName);

            ExportPropertyManager pm = new ExportPropertyManager(SwApp);
            pm.Show();
            pm.Close(false);
            SwApp.CloseAllDocuments(true);
            Xunit.Assert.True(true, "Property manager failed to open/close with cancel");
        }

        // TODO(SIMINT-164) pm.Show() crashes with drag drop
        //[Theory]
        //[InlineData("3_DOF_ARM")]
        public void TestPreviewExport(string modelName)
        {
            OpenSWDocument(modelName);
            ExportPropertyManager pm = new ExportPropertyManager(SwApp);
            pm.Show();

            // PrivateObject came from Microsoft.VisualStudio.TestTools.UnitTesting,
            // which we dropped during the toolchain modernization. Plain reflection
            // does the same job: invoke the private ExportButtonPress() and walk
            // the public Exporter.URDFRobot property chain.
            MethodInfo exportButtonPress = typeof(ExportPropertyManager).GetMethod(
                "ExportButtonPress",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Xunit.Assert.NotNull(exportButtonPress);
            exportButtonPress.Invoke(pm, null);

            object exporter = typeof(ExportPropertyManager)
                .GetField("Exporter", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(pm);
            object urdfRobot = exporter?.GetType()
                .GetProperty("URDFRobot")
                ?.GetValue(exporter);
            Xunit.Assert.NotNull(urdfRobot);
        }
    }
}
