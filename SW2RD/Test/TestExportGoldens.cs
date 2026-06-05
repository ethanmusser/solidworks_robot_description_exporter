/*
Copyright (c) 2026 Ethan J. Musser

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using SW2RD.Core;
using SW2RD.Validation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SW2RD.Test
{
    /// <summary>
    /// Data-driven golden regression for the format writers, run SW-free.
    ///
    /// <para>Each model lives under
    /// <c>solidworks_urdf_exporter/test-fixtures/golden/&lt;model&gt;/</c> and
    /// carries one fixture per output format, all captured from a real
    /// SolidWorks export:</para>
    /// <list type="bullet">
    /// <item><c>urdf.snapshot.json</c> / <c>mjcf.snapshot.json</c> - the frozen
    /// writer input (canonical <see cref="KinematicTree"/> + MJCF auxiliary +
    /// writer options), produced by exporting with the
    /// <c>SW2RD_CAPTURE_GOLDEN</c> env var set. This is the ground truth and
    /// changes only when the SolidWorks extraction layer changes.</item>
    /// <item><c>expected.urdf</c> / <c>expected.mjcf.xml</c> - the blessed
    /// writer output (the real captured export). Regenerated SW-free from the
    /// matching snapshot whenever a writer change is intentional (set
    /// <c>SW2RD_BLESS_GOLDEN=1</c>).</item>
    /// </list>
    ///
    /// <para>A model folder may hold both formats; each
    /// <c>*.snapshot.json</c> is an independent fixture whose
    /// <see cref="ExportSnapshot.Format"/> selects its <c>expected.*</c>
    /// sibling. Adding a model is just dropping a new folder with its
    /// snapshot + expected pair(s) in place; no code change required.</para>
    ///
    /// <para>The test replays each snapshot through the writer and compares to
    /// the committed expected output with <see cref="GoldenXmlComparer"/>'s
    /// numeric tolerance, so format / float-noise differences never fire but a
    /// real shape or value regression does. Adding a model is just dropping a
    /// new fixture folder in place; no code change required.</para>
    /// </summary>
    [Trait("Category", "SWFree")]
    public class TestExportGoldens
    {
        [Fact]
        public void AllGoldenFixturesMatchTheirSnapshots()
        {
            List<string> snapshots = EnumerateFixtureSnapshots();
            if (snapshots.Count == 0)
            {
                // No committed fixtures yet (fresh checkout before any have been
                // captured). Nothing to assert.
                return;
            }

            StringBuilder failures = new StringBuilder();
            int compared = 0;

            foreach (string snapshotPath in snapshots)
            {
                ExportSnapshot snapshot = ExportSnapshotSerializer.Deserialize(File.ReadAllText(snapshotPath));
                string fixtureDir = Path.GetDirectoryName(snapshotPath);
                string expectedPath = ExpectedOutputPath(fixtureDir, snapshot.Format);
                string label = Path.GetFileName(fixtureDir) + " (" + snapshot.Format + ")";

                if (!File.Exists(expectedPath))
                {
                    failures.Append("\n[").Append(label).Append("] missing expected output: ")
                        .Append(expectedPath).Append('\n');
                    continue;
                }

                string generated = SnapshotReplay.Render(snapshot);
                XmlDiffResult diff = GoldenXmlComparer.Compare(
                    File.ReadAllText(expectedPath),
                    generated,
                    GoldenXmlComparer.DefaultIgnoredUrdfElements);

                compared++;
                if (!diff.AreEqual)
                {
                    failures.Append("\n[").Append(label).Append("] writer output drifted from golden:\n");
                    failures.Append(diff.Describe());
                }
            }

            Assert.True(failures.Length == 0,
                "Golden mismatch across " + compared + " fixture(s):\n" + failures);
        }

        // Re-renders every committed fixture's expected.* output SW-free from
        // its trusted snapshot.json. Runs only when SW2RD_BLESS_GOLDEN is set
        // (so it is a no-op in normal test runs and in CI). Use this after an
        // intentional URDF/MJCF writer change: the snapshot (the SolidWorks
        // extraction ground truth) is unchanged, only the rendered output
        // moves, so this rebases every expected.* in one pass. `git diff` of
        // the fixtures is then the exact semantic change the writer edit made.
        // Extraction-layer changes need a fresh capture instead (re-export
        // with SW2RD_CAPTURE_GOLDEN set; see the TestRunner README).
        [Fact]
        public void BlessExpectedOutputsWhenRequested()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SW2RD_BLESS_GOLDEN")))
            {
                return;
            }

            foreach (string snapshotPath in EnumerateFixtureSnapshots())
            {
                ExportSnapshot snapshot = ExportSnapshotSerializer.Deserialize(File.ReadAllText(snapshotPath));
                string fixtureDir = Path.GetDirectoryName(snapshotPath);
                string expectedPath = ExpectedOutputPath(fixtureDir, snapshot.Format);
                File.WriteAllText(expectedPath, SnapshotReplay.Render(snapshot));
            }
        }

        private static string ExpectedOutputPath(string fixtureDir, string format)
        {
            string fileName = string.Equals(format, SnapshotReplay.MjcfFormat, StringComparison.OrdinalIgnoreCase)
                ? "expected.mjcf.xml"
                : "expected.urdf";
            return Path.Combine(fixtureDir, fileName);
        }

        // Every *.snapshot.json under a model folder is an independent fixture
        // (a model may carry both urdf.snapshot.json and mjcf.snapshot.json).
        private static List<string> EnumerateFixtureSnapshots()
        {
            string root = GetFixturesRoot();
            if (!Directory.Exists(root))
            {
                return new List<string>();
            }
            return Directory.EnumerateDirectories(root)
                .OrderBy(d => d, StringComparer.Ordinal)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.snapshot.json")
                    .OrderBy(f => f, StringComparer.Ordinal))
                .ToList();
        }

        private static string GetFixturesRoot()
        {
            return Path.Combine(GetRepoRoot(), "solidworks_urdf_exporter", "test-fixtures", "golden");
        }

        private static string GetRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "solidworks_urdf_exporter")))
            {
                dir = dir.Parent;
            }
            if (dir == null)
            {
                throw new DirectoryNotFoundException("Could not locate repository root.");
            }
            return dir.FullName;
        }
    }
}
