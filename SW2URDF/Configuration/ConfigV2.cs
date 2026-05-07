using SW2URDF.Core;
using System;

namespace SW2URDF.Configuration
{
    /// <summary>
    /// Versioned JSON configuration root. V2 is the first configuration shape
    /// that stores the format-neutral Core model instead of a DataContract XML
    /// serialization of URDF.Link.
    /// </summary>
    public sealed record ConfigV2(
        int SchemaVersion,
        string ExporterVersion,
        DateTime SavedAtUtc,
        KinematicTree Tree)
    {
        public const int CurrentSchemaVersion = 2;

        public static ConfigV2 Create(KinematicTree tree, string exporterVersion)
        {
            return new ConfigV2(
                CurrentSchemaVersion,
                exporterVersion ?? "",
                DateTime.UtcNow,
                tree);
        }
    }
}
