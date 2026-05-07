using SW2URDF.SW;
using SW2URDF.URDFExport;
using System.Reflection;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    ///  TODO (SIMINT-164), code in UI components needs to be tested, but
    ///  pm.show() crashes SolidWorks. 
    /// </summary>
    [Collection("Requires SW Test Collection")]
    public class TestPropertyManager : SW2URDFTest
    {
        public TestPropertyManager(SWTestFixture fixture) : base(fixture)
        {
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
