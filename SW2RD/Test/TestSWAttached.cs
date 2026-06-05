using Xunit;

namespace SW2RD.Test
{
    [Collection("Requires SW Test Collection")]
    public class TestSWAttached : SW2RDTest
    {
        public TestSWAttached(SWTestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public void TestSWOpens()
        {
            Assert.NotNull(SwApp);
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        [InlineData("4_WHEELER")]
        public void TestModelDocOpens(string modelName)
        {
            OpenSWDocument(modelName);
            Assert.True(SwApp.CloseAllDocuments(true));
        }
    }
}
