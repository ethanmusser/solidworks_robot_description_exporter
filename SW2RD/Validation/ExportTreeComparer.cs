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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SW2RD.Validation
{
    /// <summary>
    /// Compares two already-produced export trees (e.g. one built with a
    /// baseline DLL and one with a candidate DLL) and reports semantic
    /// differences. This is the standalone differential-output tool: it never
    /// touches SolidWorks, so the workflow is "export the same assembly with
    /// each build, then diff the two output folders".
    ///
    /// <para>Comparison is per relative path:</para>
    /// <list type="bullet">
    /// <item>XML-family files (.urdf, .xml, .xacro, .config) are compared
    /// structurally with numeric tolerance via <see cref="GoldenXmlComparer"/>,
    /// ignoring comments / version stamps / attribute order.</item>
    /// <item>Mesh / binary files (.stl, .dae, .obj, .3dxml, .png, ...) are not
    /// byte-compared - tessellation carries float noise - only their presence
    /// and byte-size delta are reported.</item>
    /// <item>Everything else (.yaml, .launch, .txt, CMakeLists, ...) is compared
    /// as whitespace-normalized text.</item>
    /// </list>
    /// </summary>
    public static class ExportTreeComparer
    {
        private static readonly HashSet<string> XmlExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".urdf", ".xml", ".xacro", ".config", ".mjcf" };

        private static readonly HashSet<string> BinaryExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".stl", ".dae", ".obj", ".3dxml", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".dll", ".pdb" };

        /// <summary>
        /// Compares two directories recursively and returns a report. Pass a
        /// <paramref name="fileFilter"/> (glob-free predicate on the relative
        /// path) to restrict the comparison, e.g. to only URDF/MJCF files.
        /// </summary>
        public static ExportDiffReport CompareDirectories(
            string baselineDir,
            string candidateDir,
            Func<string, bool> fileFilter = null)
        {
            if (!Directory.Exists(baselineDir))
            {
                throw new DirectoryNotFoundException("Baseline directory not found: " + baselineDir);
            }
            if (!Directory.Exists(candidateDir))
            {
                throw new DirectoryNotFoundException("Candidate directory not found: " + candidateDir);
            }

            Dictionary<string, string> baseline = EnumerateRelative(baselineDir, fileFilter);
            Dictionary<string, string> candidate = EnumerateRelative(candidateDir, fileFilter);

            ExportDiffReport report = new ExportDiffReport(baselineDir, candidateDir);

            foreach (string rel in baseline.Keys.Where(k => !candidate.ContainsKey(k)).OrderBy(k => k))
            {
                report.AddMissing(rel);
            }
            foreach (string rel in candidate.Keys.Where(k => !baseline.ContainsKey(k)).OrderBy(k => k))
            {
                report.AddAdded(rel);
            }

            foreach (string rel in baseline.Keys.Where(candidate.ContainsKey).OrderBy(k => k))
            {
                CompareFile(rel, baseline[rel], candidate[rel], report);
            }

            return report;
        }

        private static Dictionary<string, string> EnumerateRelative(string root, Func<string, bool> fileFilter)
        {
            string full = Path.GetFullPath(root);
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                string rel = path.Substring(full.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                rel = rel.Replace(Path.DirectorySeparatorChar, '/');
                if (fileFilter == null || fileFilter(rel))
                {
                    map[rel] = path;
                }
            }
            return map;
        }

        private static void CompareFile(string rel, string baselinePath, string candidatePath, ExportDiffReport report)
        {
            string ext = Path.GetExtension(rel);

            if (BinaryExtensions.Contains(ext))
            {
                long baseLen = new FileInfo(baselinePath).Length;
                long candLen = new FileInfo(candidatePath).Length;
                if (baseLen != candLen)
                {
                    report.AddBinarySizeDelta(rel, baseLen, candLen);
                }
                return;
            }

            if (XmlExtensions.Contains(ext))
            {
                try
                {
                    XmlDiffResult diff = GoldenXmlComparer.Compare(
                        File.ReadAllText(baselinePath),
                        File.ReadAllText(candidatePath),
                        GoldenXmlComparer.DefaultIgnoredUrdfElements);
                    if (!diff.AreEqual)
                    {
                        report.AddXmlDiff(rel, diff);
                    }
                }
                catch (Exception e)
                {
                    report.AddError(rel, "XML compare failed: " + e.Message);
                }
                return;
            }

            // Text: whitespace-normalized line compare.
            string baseText = NormalizeText(File.ReadAllText(baselinePath));
            string candText = NormalizeText(File.ReadAllText(candidatePath));
            if (!string.Equals(baseText, candText, StringComparison.Ordinal))
            {
                report.AddTextDiff(rel);
            }
        }

        private static string NormalizeText(string s)
        {
            string[] lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            return string.Join("\n", lines.Select(l => l.TrimEnd()).Where(l => l.Length > 0));
        }
    }

    /// <summary>Accumulated differences between two export trees.</summary>
    public sealed class ExportDiffReport
    {
        private readonly string baselineDir;
        private readonly string candidateDir;
        private readonly List<string> missing = new List<string>();
        private readonly List<string> added = new List<string>();
        private readonly List<string> textDiffs = new List<string>();
        private readonly List<string> errors = new List<string>();
        private readonly List<KeyValuePair<string, XmlDiffResult>> xmlDiffs =
            new List<KeyValuePair<string, XmlDiffResult>>();
        private readonly List<string> binaryDeltas = new List<string>();

        internal ExportDiffReport(string baselineDir, string candidateDir)
        {
            this.baselineDir = baselineDir;
            this.candidateDir = candidateDir;
        }

        internal void AddMissing(string rel) => missing.Add(rel);
        internal void AddAdded(string rel) => added.Add(rel);
        internal void AddTextDiff(string rel) => textDiffs.Add(rel);
        internal void AddError(string rel, string message) => errors.Add(rel + ": " + message);
        internal void AddXmlDiff(string rel, XmlDiffResult diff) =>
            xmlDiffs.Add(new KeyValuePair<string, XmlDiffResult>(rel, diff));
        internal void AddBinarySizeDelta(string rel, long baseLen, long candLen) =>
            binaryDeltas.Add($"{rel} ({baseLen} -> {candLen} bytes)");

        /// <summary>True when the two trees are semantically identical.</summary>
        public bool AreEqual =>
            missing.Count == 0 && added.Count == 0 && textDiffs.Count == 0 &&
            xmlDiffs.Count == 0 && binaryDeltas.Count == 0 && errors.Count == 0;

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Baseline:  ").Append(baselineDir).Append('\n');
            sb.Append("Candidate: ").Append(candidateDir).Append('\n');

            if (AreEqual)
            {
                sb.Append("\nNo semantic differences. The two export trees are equivalent.\n");
                return sb.ToString();
            }

            if (missing.Count > 0)
            {
                sb.Append("\n== Files only in baseline (removed) ==\n");
                foreach (string m in missing) sb.Append("  - ").Append(m).Append('\n');
            }
            if (added.Count > 0)
            {
                sb.Append("\n== Files only in candidate (added) ==\n");
                foreach (string a in added) sb.Append("  + ").Append(a).Append('\n');
            }
            if (binaryDeltas.Count > 0)
            {
                sb.Append("\n== Mesh / binary size changes (not content-compared) ==\n");
                foreach (string b in binaryDeltas) sb.Append("  ~ ").Append(b).Append('\n');
            }
            if (textDiffs.Count > 0)
            {
                sb.Append("\n== Text files that differ ==\n");
                foreach (string t in textDiffs) sb.Append("  ~ ").Append(t).Append('\n');
            }
            if (errors.Count > 0)
            {
                sb.Append("\n== Errors ==\n");
                foreach (string e in errors) sb.Append("  ! ").Append(e).Append('\n');
            }
            if (xmlDiffs.Count > 0)
            {
                sb.Append("\n== XML files that differ (numeric-tolerant) ==\n");
                foreach (KeyValuePair<string, XmlDiffResult> kvp in xmlDiffs)
                {
                    sb.Append("\n--- ").Append(kvp.Key).Append(" ---\n");
                    sb.Append(kvp.Value.Describe());
                }
            }
            return sb.ToString();
        }
    }
}
