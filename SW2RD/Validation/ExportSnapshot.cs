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
using System.Text.Json;
using System.Text.Json.Serialization;
using SW2RD.Core;
using SW2RD.MJCF;

namespace SW2RD.Validation
{
    /// <summary>
    /// A frozen capture of everything a format writer needs to (re)produce a
    /// URDF / MJCF document, with no SolidWorks dependency. It is the
    /// <em>input</em> half of a golden fixture: a snapshot is captured from a
    /// real SolidWorks export (the ground truth), committed alongside the
    /// blessed <c>expected.*</c> output, and then replayed SW-free by the
    /// golden tests and the regeneration tool.
    ///
    /// <para>The two writers need different payloads, so a snapshot carries both
    /// and the consumer reads the half that matches <see cref="Format"/>:</para>
    /// <list type="bullet">
    /// <item>URDF: <see cref="Tree"/> only (mesh URIs are already stamped onto
    /// the tree's <c>MeshGroupModel.MeshFilename</c>).</item>
    /// <item>MJCF: <see cref="Tree"/> plus <see cref="Auxiliary"/> (per-link
    /// mesh asset refs + site transforms), <see cref="MeshDir"/>, and the
    /// rotation-format / angle-unit writer options.</item>
    /// </list>
    /// </summary>
    internal sealed class ExportSnapshot
    {
        /// <summary>Bumped if the snapshot JSON shape changes incompatibly.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>"URDF" or "MJCF".</summary>
        public string Format { get; set; }

        /// <summary>The model name, for diagnostics and fixture labeling.</summary>
        public string ModelName { get; set; }

        /// <summary>The canonical kinematic tree fed to the writer.</summary>
        public KinematicTree Tree { get; set; }

        /// <summary>Mesh directory passed to the writer (MJCF: e.g. "../meshes/").</summary>
        public string MeshDir { get; set; }

        /// <summary>Per-link MJCF auxiliary data (mesh asset refs + sites).</summary>
        public Dictionary<string, LinkAuxiliary> Auxiliary { get; set; }
            = new Dictionary<string, LinkAuxiliary>();

        public MJCFRotationFormat MjcfRotationFormat { get; set; } = MJCFRotationFormat.AxisAngle;

        public MJCFAngleUnit MjcfAngleUnit { get; set; } = MJCFAngleUnit.Degree;
    }

    /// <summary>
    /// JSON codec for <see cref="ExportSnapshot"/>. Mirrors
    /// <c>ConfigJsonSerializer</c>'s conventions (pretty, case-insensitive) and
    /// additionally enables field serialization because the MJCF auxiliary
    /// POCOs (<see cref="LinkAuxiliary"/>, <c>MeshAssetRef</c>, <c>SiteTransform</c>)
    /// expose public fields rather than properties.
    /// </summary>
    internal static class ExportSnapshotSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            IncludeFields = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string Serialize(ExportSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            return JsonSerializer.Serialize(snapshot, JsonOptions);
        }

        public static ExportSnapshot Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Snapshot JSON cannot be empty.", nameof(json));
            }
            ExportSnapshot snapshot = JsonSerializer.Deserialize<ExportSnapshot>(json, JsonOptions);
            if (snapshot == null)
            {
                throw new InvalidOperationException("Snapshot JSON did not contain a document.");
            }
            if (snapshot.SchemaVersion != ExportSnapshot.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    "Unsupported snapshot schema version " + snapshot.SchemaVersion + ".");
            }
            return snapshot;
        }
    }
}
