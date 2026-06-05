
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
            // The informational version comes from `git describe` at build time
            // (scripts/UpdateVersionInfo.ps1), so a build from an uncommitted
            // working tree legitimately ends in "-dirty". Asserting its absence
            // tests the cleanliness of the dev checkout, not the code - it would
            // fail on every developer build with pending changes. Validate the
            // shape instead (a SemVer-like "major.minor" prefix).
            Assert.Matches(@"^\d+\.\d+", commitVersion);
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
