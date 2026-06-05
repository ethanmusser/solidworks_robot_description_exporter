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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SW2RD.Validation
{
    /// <summary>
    /// Numeric-aware structural comparison of two URDF / MJCF documents. It
    /// renders each document to a canonical, line-per-element snapshot and
    /// diffs the snapshots. The canonicalization is deliberately tolerant of
    /// everything that is <em>not</em> a semantic change so that an A/B compare
    /// of two builds (or a golden-file regression) only fires on real drift:
    ///
    /// <list type="bullet">
    /// <item>XML comments (which carry the per-build version / commit stamp)
    /// and processing instructions are ignored.</item>
    /// <item>Attribute order is ignored (attributes are sorted by name).</item>
    /// <item>XML namespace declarations are ignored.</item>
    /// <item>Numeric attribute / text values - including space-separated
    /// tuples (xyz, rpy, rgba, axis, fullinertia, quat) - are parsed and
    /// re-formatted with a fixed precision and culture, and any value within
    /// <see cref="NearZeroTolerance"/> of zero is snapped to 0. This masks
    /// last-ULP float noise and "0 vs 0.0 vs -0" while still catching a sign
    /// flip, a transform delta, or an inertia change.</item>
    /// </list>
    ///
    /// This type lives in the production assembly (not the test project) so the
    /// golden tests and the standalone <c>TestRunner diff</c> tool share one
    /// implementation. It has no SolidWorks dependency.
    /// </summary>
    public static class GoldenXmlComparer
    {
        /// <summary>
        /// Significant-digit precision used when re-formatting numeric values.
        /// G9 round-trips a 32-bit float exactly and is tight enough to flag a
        /// real regression (axis sign flip, 1mm transform delta, 1% inertia
        /// change) while masking last-ULP double-printing noise.
        /// </summary>
        public const string NumericFormat = "G9";

        /// <summary>
        /// Values whose magnitude is below this are treated as exactly zero.
        /// The canonical model round-trips rotations through
        /// quaternion &lt;-&gt; rpy, which leaves sub-1e-12 noise on components a
        /// reference records as a clean 0.
        /// </summary>
        public const double NearZeroTolerance = 1e-9;

        /// <summary>
        /// Attribute local-names skipped entirely during comparison. Empty by
        /// default; callers that need to ignore e.g. a derived <c>quat</c>
        /// attribute pass their own set.
        /// </summary>
        public static readonly IReadOnlyCollection<string> NoSkippedAttributes =
            Array.Empty<string>();

        /// <summary>
        /// Element local-names dropped (with their subtrees) before comparison.
        /// The legacy-compatible URDF writer emits a few always-empty
        /// placeholder elements that are not part of the semantic shape.
        /// </summary>
        public static readonly IReadOnlyCollection<string> DefaultIgnoredUrdfElements =
            new[] { "safety_controller", "calibration", "dynamics", "mimic" };

        /// <summary>
        /// Compares two XML documents and returns a human-readable diff. An
        /// empty result string means the documents are structurally equal under
        /// the tolerance rules.
        /// </summary>
        public static XmlDiffResult Compare(
            XDocument expected,
            XDocument actual,
            IReadOnlyCollection<string> ignoredElementNames = null,
            IReadOnlyCollection<string> skippedAttributeNames = null)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (actual == null) throw new ArgumentNullException(nameof(actual));

            HashSet<string> ignored = new HashSet<string>(
                ignoredElementNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            HashSet<string> skippedAttrs = new HashSet<string>(
                skippedAttributeNames ?? Array.Empty<string>(), StringComparer.Ordinal);

            string expectedSnapshot = Canonicalize(Filter(expected.Root, ignored), skippedAttrs);
            string actualSnapshot = Canonicalize(Filter(actual.Root, ignored), skippedAttrs);

            return new XmlDiffResult(expectedSnapshot, actualSnapshot);
        }

        /// <summary>
        /// Parses two raw XML strings and compares them. Convenience overload
        /// for the file-tree differ.
        /// </summary>
        public static XmlDiffResult Compare(
            string expectedXml,
            string actualXml,
            IReadOnlyCollection<string> ignoredElementNames = null,
            IReadOnlyCollection<string> skippedAttributeNames = null)
        {
            return Compare(
                XDocument.Parse(expectedXml),
                XDocument.Parse(actualXml),
                ignoredElementNames,
                skippedAttributeNames);
        }

        // Returns a deep clone of the element with ignored subtrees removed, so
        // the source documents are never mutated.
        private static XElement Filter(XElement root, HashSet<string> ignoredElementNames)
        {
            if (root == null)
            {
                return null;
            }
            XElement clone = new XElement(root);
            if (ignoredElementNames.Count > 0)
            {
                foreach (XElement noisy in clone.Descendants()
                    .Where(e => ignoredElementNames.Contains(e.Name.LocalName))
                    .ToList())
                {
                    noisy.Remove();
                }
            }
            // Drop transient empty <texture filename=""/> the writer can emit.
            foreach (XElement texture in clone.DescendantsAndSelf("texture").ToList())
            {
                string filename = (string)texture.Attribute("filename");
                if (string.IsNullOrWhiteSpace(filename))
                {
                    texture.Remove();
                }
            }
            return clone;
        }

        private static string Canonicalize(XElement element, HashSet<string> skippedAttributeNames)
        {
            if (element == null)
            {
                return "(empty)";
            }
            StringBuilder sb = new StringBuilder();
            CanonicalizeInto(element, sb, 0, skippedAttributeNames);
            return sb.ToString();
        }

        private static void CanonicalizeInto(
            XElement element, StringBuilder sb, int depth, HashSet<string> skippedAttributeNames)
        {
            sb.Append(' ', depth * 2);
            sb.Append(element.Name.LocalName);

            foreach (XAttribute attr in element.Attributes()
                .Where(a => !a.IsNamespaceDeclaration)
                .Where(a => !skippedAttributeNames.Contains(a.Name.LocalName))
                .OrderBy(a => a.Name.LocalName, StringComparer.Ordinal))
            {
                sb.Append(' ');
                sb.Append(attr.Name.LocalName);
                sb.Append('=');
                sb.Append(NormalizeValue(attr.Value));
            }

            string text = element.Nodes()
                .OfType<XText>()
                .Aggregate(new StringBuilder(), (acc, t) => acc.Append(t.Value))
                .ToString().Trim();
            if (text.Length > 0)
            {
                sb.Append(" #text=").Append(NormalizeValue(text));
            }

            sb.Append('\n');

            foreach (XElement child in element.Elements())
            {
                CanonicalizeInto(child, sb, depth + 1, skippedAttributeNames);
            }
        }

        /// <summary>
        /// Normalizes a value for stable cross-build comparison: numeric scalars
        /// and space-separated numeric tuples are re-formatted; everything else
        /// is returned trimmed and verbatim.
        /// </summary>
        public static string NormalizeValue(string raw)
        {
            if (raw == null)
            {
                return "";
            }
            string trimmed = raw.Trim();
            if (TryNormalizeTuple(trimmed, out string normalizedTuple))
            {
                return normalizedTuple;
            }
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                return FormatComponent(d);
            }
            return trimmed;
        }

        private static string FormatComponent(double d)
        {
            if (Math.Abs(d) < NearZeroTolerance)
            {
                d = 0.0;
            }
            return d.ToString(NumericFormat, CultureInfo.InvariantCulture);
        }

        private static bool TryNormalizeTuple(string raw, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(raw) || !raw.Contains(" "))
            {
                return false;
            }
            string[] parts = raw.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }
            double[] values = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }
            normalized = string.Join(" ", values.Select(FormatComponent));
            return true;
        }
    }

    /// <summary>
    /// Result of an XML structural comparison. <see cref="AreEqual"/> is the
    /// quick boolean; <see cref="Describe"/> renders a unified-style diff of the
    /// canonical snapshots for human consumption.
    /// </summary>
    public sealed class XmlDiffResult
    {
        private readonly string expectedSnapshot;
        private readonly string actualSnapshot;

        internal XmlDiffResult(string expectedSnapshot, string actualSnapshot)
        {
            this.expectedSnapshot = expectedSnapshot ?? "";
            this.actualSnapshot = actualSnapshot ?? "";
        }

        public bool AreEqual =>
            string.Equals(expectedSnapshot, actualSnapshot, StringComparison.Ordinal);

        /// <summary>Canonical snapshot of the expected (baseline) document.</summary>
        public string ExpectedSnapshot => expectedSnapshot;

        /// <summary>Canonical snapshot of the actual (candidate) document.</summary>
        public string ActualSnapshot => actualSnapshot;

        /// <summary>
        /// Renders the differing lines as a unified-style diff. Returns an empty
        /// string when the documents are equal. Lines that differ are prefixed
        /// with <c>-</c> (expected/baseline) and <c>+</c> (actual/candidate).
        /// </summary>
        public string Describe()
        {
            if (AreEqual)
            {
                return "";
            }
            string[] expectedLines = expectedSnapshot.Split('\n');
            string[] actualLines = actualSnapshot.Split('\n');
            int max = Math.Max(expectedLines.Length, actualLines.Length);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < max; i++)
            {
                string e = i < expectedLines.Length ? expectedLines[i] : null;
                string a = i < actualLines.Length ? actualLines[i] : null;
                if (!string.Equals(e, a, StringComparison.Ordinal))
                {
                    if (e != null && e.Length > 0)
                    {
                        sb.Append("- ").Append(e).Append('\n');
                    }
                    if (a != null && a.Length > 0)
                    {
                        sb.Append("+ ").Append(a).Append('\n');
                    }
                }
            }
            return sb.ToString();
        }
    }
}
