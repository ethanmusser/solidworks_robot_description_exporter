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
