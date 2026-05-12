
using SW2RD.Versioning;
using Xunit;

namespace SW2RD.Test
{
    public class TestVersioning : SW2RDTest
    {
        public TestVersioning(SWTestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public void TestGetCommitVersion()
        {
            string commitVersion = Version.GetCommitVersion();
            Assert.NotNull(commitVersion);
            Assert.NotEmpty(commitVersion);
            Assert.DoesNotContain("dirty", commitVersion);
        }

        [Fact]
        public void TestGetBuildVersion()
        {
            string buildVersion = Version.GetBuildVersion();
            Assert.NotNull(buildVersion);
            Assert.NotEmpty(buildVersion);
        }
    }
}
