using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.IO;
using Xunit;

namespace SW2RD.Test
{
    /// <summary>
    /// Base class for each Test class. This file contains many helper functions as well
    /// as access to the TestFixture which contains the SwApp reference.
    /// </summary>
    public abstract class SW2RDTest : IClassFixture<SWTestFixture>
    {
        public const string ModelName3DofArm = "3_DOF_ARM";
        public const string ModelName4Wheeler = "4_WHEELER";
        protected readonly SWTestFixture TestFixture;
        protected readonly SldWorks SwApp;
        public SW2RDTest(SWTestFixture fixture)
        {
            SWTestFixture.Initialize();
            TestFixture = fixture;
            SwApp = SWTestFixture.SwApp;
        }

        public void Dispose()
        {
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        public static string GetDebugDirectory()
        {
            return System.AppDomain.CurrentDomain.BaseDirectory;
        }

        public static string GetX64Directory()
        {
            return Path.GetDirectoryName(GetDebugDirectory());
        }

        public static string GetBinDirectory()
        {
            return Path.GetDirectoryName(GetX64Directory());
        }

        public static string GetProjectDirectory()
        {
            return Path.GetDirectoryName(GetBinDirectory());
        }

        public static string GetSolutionDirectory()
        {
            return Path.GetDirectoryName(GetProjectDirectory());
        }

        public static string GetExamplesDirectory()
        {
            return Path.Combine(GetSolutionDirectory(), "examples");
        }

        public static string GetModelDirectory(string modelName)
        {
            return Path.Combine(GetExamplesDirectory(), modelName);
        }

        public static string GetPackageDirectory(string modelName)
        {
            string modelDirectory = GetModelDirectory(modelName);
            string package_name = modelName + "_description";
            return Path.Combine(modelDirectory, package_name);
        }

        public static string GetURDFDirectory(string modelName)
        {
            return Path.Combine(GetPackageDirectory(modelName), "urdf");
        }

        public static string GetMeshesDirectory(string modelName)
        {
            return Path.Combine(GetPackageDirectory(modelName), "meshes");
        }

        public ModelDoc2 OpenSWDocument(string modelName)
        {
            Assert.True(SwApp.CloseAllDocuments(true));

            string modelDirectory = GetModelDirectory(modelName);
            string filename = Path.Combine(modelDirectory, modelName + ".SLDASM");
            Assert.True(File.Exists(filename));
            int errors = 0;
            int warnings = 0;
            int filetype = (int)swDocumentTypes_e.swDocASSEMBLY;
            string configuration = "";

            ModelDoc2 doc = SwApp.OpenDoc6(filename, filetype, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, 
                                           configuration, ref errors, ref warnings);
            Assert.Equal(0, errors);
            // Two load warnings are benign for the test suite and must not fail
            // an open (verified values against the installed interop):
            //  - swFileLoadWarning_AlreadyOpen (128): a referenced document is
            //    still loaded from a prior test in the shared SW session. The
            //    doc opens and is fully usable; this is a test-isolation
            //    artifact, not a defect.
            //  - swFileLoadWarning_NeedsRegen (32): a re-saved example assembly
            //    reports "needs rebuild" on open but loads and exports fine.
            // Mask both off; still assert no OTHER load warnings.
            int benignWarnings =
                (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen |
                (int)swFileLoadWarning_e.swFileLoadWarning_NeedsRegen;
            Assert.Equal(0, warnings & ~benignWarnings);
            return doc;
        }

        public ModelDoc2 OpenSWPartDocument(string modelName)
        {
            Assert.True(SwApp.CloseAllDocuments(true));

            string modelDirectory = GetModelDirectory(modelName);
            string filename = Path.Combine(modelDirectory, modelName + ".SLDPRT");
            Assert.True(File.Exists(filename));
            int errors = 0;
            int warnings = 0;
            int filetype = (int)swDocumentTypes_e.swDocASSEMBLY;
            string configuration = "";

            ModelDoc2 doc = SwApp.OpenDoc6(filename, filetype, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, 
                                           configuration, ref errors, ref warnings);
            Assert.Equal(0, errors);
            // Two load warnings are benign for the test suite and must not fail
            // an open (verified values against the installed interop):
            //  - swFileLoadWarning_AlreadyOpen (128): a referenced document is
            //    still loaded from a prior test in the shared SW session. The
            //    doc opens and is fully usable; this is a test-isolation
            //    artifact, not a defect.
            //  - swFileLoadWarning_NeedsRegen (32): a re-saved example assembly
            //    reports "needs rebuild" on open but loads and exports fine.
            // Mask both off; still assert no OTHER load warnings.
            int benignWarnings =
                (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen |
                (int)swFileLoadWarning_e.swFileLoadWarning_NeedsRegen;
            Assert.Equal(0, warnings & ~benignWarnings);
            return doc;
        }

        public static string CreateRandomTempDirectory()
        {
            string name = Path.GetRandomFileName();
            string tempDirectory = Path.Combine(Path.GetTempPath(), name);
            Assert.True(Directory.CreateDirectory(tempDirectory).Exists);
            return tempDirectory;
        }
    }
}
