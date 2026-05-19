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
