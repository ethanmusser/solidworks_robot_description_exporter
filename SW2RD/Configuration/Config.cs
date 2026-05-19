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
using System;

namespace SW2RD.Configuration
{
    /// <summary>
    /// Versioned JSON configuration root for SW2RD. Stores the format-neutral
    /// Core model (KinematicTree). The v1 schema is the inaugural SW2RD shape;
    /// it is binary-equivalent to the SW2URDF v2 JSON schema it inherits from
    /// (same fields, same JSON layout), but is renumbered to v1 so SW2RD's
    /// schema versioning starts at 1 alongside the 0.1.0 product version.
    /// SW2URDF v2 JSON configs and the older v1.5 DataContract XML configs
    /// are read on a one-way migration path via
    /// <see cref="SW2RD.Export.ConfigurationSerialization.PREVIOUS_CONFIGURATION_NAMES"/>.
    /// </summary>
    public sealed record Config(
        int SchemaVersion,
        string ExporterVersion,
        DateTime SavedAtUtc,
        KinematicTree Tree)
    {
        public const int CurrentSchemaVersion = 1;

        public static Config Create(KinematicTree tree, string exporterVersion)
        {
            return new Config(
                CurrentSchemaVersion,
                exporterVersion ?? "",
                DateTime.UtcNow,
                tree);
        }
    }
}
