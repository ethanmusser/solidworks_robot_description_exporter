using SW2RD.Export;
using Xunit;

namespace SW2RD.Test
{
    [Trait("Category", "SWFree")]
    public class TestExportFilenameDefault
    {
        [Theory]
        [InlineData(ExportFormat.MJCF)]
        [InlineData(ExportFormat.URDF)]
        public void TestSuggestedExportFileNameUsesPackageNameWithoutFormatSuffix(ExportFormat format)
        {
            string suggested = ExportPropertyManager.GetSuggestedExportFileName("3_DOF_ARM", format);

            Assert.Equal("3_DOF_ARM", suggested);
        }

        [Theory]
        [InlineData("3_DOF_ARM - MJCF", ExportFormat.MJCF)]
        [InlineData("3_DOF_ARM - URDF", ExportFormat.URDF)]
        public void TestSuggestedExportFileNamePreservesUserProvidedName(string packageName, ExportFormat format)
        {
            string suggested = ExportPropertyManager.GetSuggestedExportFileName(packageName, format);

            Assert.Equal(packageName, suggested);
        }
    }
}
