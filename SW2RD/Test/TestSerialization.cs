using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2RD.URDF;
using SW2RD.Export;
using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace SW2RD.Test
{
    [Collection("Requires SW Test Collection")]
    public class TestSerialization : SW2RDTest
    {
        public TestSerialization(SWTestFixture fixture) : base(fixture)
        {
        }

        // The legacy MSTest PrivateType wrapper was used here only to invoke
        // private static methods on ConfigurationSerialization. We've dropped
        // the Microsoft.VisualStudio.TestPlatform package as part of the
        // toolchain modernization; this helper does the same job with plain
        // reflection so we don't carry an MSTest dependency just for two
        // private-method calls.
        private static object InvokePrivateStatic(Type type, string methodName, params object[] args)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Xunit.Assert.NotNull(method);
            return method.Invoke(null, args);
        }

        private ModelDoc2 OpenCopiedSWDocument(string modelName, out string tempDirectory)
        {
            Assert.True(SwApp.CloseAllDocuments(true));

            string sourceDirectory = GetModelDirectory(modelName);
            tempDirectory = CreateRandomTempDirectory();
            string copiedModelDirectory = Path.Combine(tempDirectory, modelName);
            CopyDirectory(sourceDirectory, copiedModelDirectory);

            string filename = Path.Combine(copiedModelDirectory, modelName + ".SLDASM");
            Assert.True(File.Exists(filename));

            int errors = 0;
            int warnings = 0;
            ModelDoc2 doc = SwApp.OpenDoc6(
                filename,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "",
                ref errors,
                ref warnings);
            Assert.Equal(0, errors);
            Assert.Equal(0, warnings);
            return doc;
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
            }
            foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                File.Copy(file, Path.Combine(destinationDirectory, relative));
            }
        }

        [Theory]
        [InlineData("3_DOF_ARM", 5)]
        public void TestLoadConfigFromStringXML(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            object swAttObj = InvokePrivateStatic(
                typeof(ConfigurationSerialization),
                "FindSWSaveAttribute",
                doc, "URDF Export Configuration");
            Xunit.Assert.NotNull(swAttObj);

            // Disambiguate against System.Attribute (via using System;)
            // and SolidWorks.Interop.sldworks.Attribute (via using
            // SolidWorks.Interop.sldworks;).
            SolidWorks.Interop.sldworks.Attribute swAtt =
                (SolidWorks.Interop.sldworks.Attribute)swAttObj;
            Parameter param = swAtt.GetParameter("data");

            Xunit.Assert.NotNull(param);
            string data = param.GetStringValue();

            Xunit.Assert.NotNull(data);
            Xunit.Assert.NotEmpty(data);

            LinkNode baseNode = (LinkNode)InvokePrivateStatic(
                typeof(ConfigurationSerialization),
                "LoadConfigFromStringXML",
                data);
            Link link = baseNode.RebuildLink();
            Xunit.Assert.Equal(expNumLinks, CommonSwOperations.GetCount(link));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 5)]
        [InlineData("4_WHEELER", 6)]
        public void TestLoadLegacyBaseNodeFromModel(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            LinkNode baseNode = ConfigurationSerialization.LoadLegacyBaseNodeFromModel(doc, out bool error);
            Xunit.Assert.False(error);
            Xunit.Assert.NotNull(baseNode);
            Xunit.Assert.Equal(expNumLinks, CommonSwOperations.GetCount(baseNode.RebuildLink()));
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        public void TestDefaultLoadDoesNotImportLegacyConfig(string modelName)
        {
            string tempDirectory = null;
            try
            {
                ModelDoc2 doc = OpenCopiedSWDocument(modelName, out tempDirectory);
                Xunit.Assert.True(ConfigurationSerialization.HasLegacyConfiguration(doc));
                if (ConfigurationSerialization.HasSavedConfiguration(doc))
                {
                    Xunit.Assert.True(ConfigurationSerialization.ClearSavedConfiguration(doc));
                }

                LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);

                Xunit.Assert.False(error);
                Xunit.Assert.Null(baseNode);
            }
            finally
            {
                SwApp.CloseAllDocuments(true);
                if (!string.IsNullOrEmpty(tempDirectory) && Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        [InlineData("4_WHEELER")]
        public void TestSerializeToString(string modelName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            LinkNode baseNode = ConfigurationSerialization.LoadLegacyBaseNodeFromModel(doc, out bool error);
            Xunit.Assert.False(error);

            string newData = (string)InvokePrivateStatic(
                typeof(ConfigurationSerialization),
                "SerializeToString",
                baseNode);
            Xunit.Assert.NotNull(newData);
            Xunit.Assert.NotEmpty(newData);
        }
    }
}
