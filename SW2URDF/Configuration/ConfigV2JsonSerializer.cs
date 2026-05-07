using System;
using System.Text.Json;

namespace SW2URDF.Configuration
{
    /// <summary>
    /// JSON codec for ConfigV2. The serializer lives outside the DTO so callers
    /// share one set of options: pretty-printed, case-insensitive reads, and no
    /// SolidWorks / URDF writer dependencies.
    /// </summary>
    public static class ConfigV2JsonSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        public static string Serialize(ConfigV2 config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            return JsonSerializer.Serialize(config, JsonOptions);
        }

        public static ConfigV2 Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Config JSON cannot be empty.", nameof(json));
            }

            ConfigV2 config = JsonSerializer.Deserialize<ConfigV2>(json, JsonOptions);
            if (config == null)
            {
                throw new InvalidOperationException("Config JSON did not contain a ConfigV2 document.");
            }
            if (config.SchemaVersion != ConfigV2.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    "Unsupported configuration schema version " + config.SchemaVersion + ".");
            }
            return config;
        }

        public static bool LooksLikeJson(string data)
        {
            return !string.IsNullOrWhiteSpace(data) && data.TrimStart().StartsWith("{", StringComparison.Ordinal);
        }
    }
}
