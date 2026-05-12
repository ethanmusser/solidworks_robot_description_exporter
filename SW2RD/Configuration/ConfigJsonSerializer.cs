using System;
using System.Text.Json;

namespace SW2RD.Configuration
{
    /// <summary>
    /// JSON codec for Config. The serializer lives outside the DTO so callers
    /// share one set of options: pretty-printed, case-insensitive reads, and no
    /// SolidWorks / URDF writer dependencies.
    /// </summary>
    public static class ConfigJsonSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        public static string Serialize(Config config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            return JsonSerializer.Serialize(config, JsonOptions);
        }

        public static Config Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Config JSON cannot be empty.", nameof(json));
            }

            Config config = JsonSerializer.Deserialize<Config>(json, JsonOptions);
            if (config == null)
            {
                throw new InvalidOperationException("Config JSON did not contain a Config document.");
            }
            if (config.SchemaVersion != Config.CurrentSchemaVersion)
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
