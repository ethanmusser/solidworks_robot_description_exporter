using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Reflection;
using Xunit;

namespace SW2URDF.Test
{
    [Collection("Requires SW Test Collection")]
    public class TestSerialization : SW2URDFTest
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

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
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
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        public void TestLoadBaseNodeFromModel(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Xunit.Assert.False(error);
            Xunit.Assert.NotNull(baseNode);
            Xunit.Assert.Equal(expNumLinks, CommonSwOperations.GetCount(baseNode.RebuildLink()));
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        [InlineData("4_WHEELER")]
        public void TestSerializeToString(string modelName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
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
