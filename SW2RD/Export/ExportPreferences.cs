/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
*/

using Microsoft.Win32;
using SW2RD.Utilities;
using System;

namespace SW2RD.Export
{
    /// <summary>
    /// Per-user, per-machine roaming defaults for the export PMPage. Backed
    /// by HKCU\Software\SW2RD\ExportDefaults so a user's last picked
    /// output format / mesh format / "export meshes" toggle survives across
    /// SolidWorks restarts and is not tied to any particular .SLDASM doc.
    ///
    /// Per-document configuration (the link tree, joint properties, etc.)
    /// stays on the SW model attribute (DataContract) or the new
    /// Config JSON; this class is ONLY for choices that should
    /// pre-populate the Setup tab when the PMPage opens.
    ///
    /// All operations are wrapped in try/catch so a missing / unreadable
    /// registry hive (e.g. on a locked-down machine) silently falls back
    /// to the hard-coded defaults rather than blowing up the PMPage.
    /// </summary>
    internal static class ExportPreferences
    {
        private const string RegistryRoot = @"Software\SW2RD\ExportDefaults";
        private const string OutputFormatValueName = "OutputFormat";
        private const string MeshFormatValueName = "MeshFormat";
        private const string ExportMeshesValueName = "ExportMeshes";
        private const string FastMeshExportValueName = "FastMeshExport";
        private const string MeshQualityValueName = "MeshQuality";

        // Defaults used when the user has not saved export preferences.
        // These match the Setup tab's standard initial state.
        private const int DefaultOutputFormat = 0; // 0 = URDF, 1 = MJCF
        private const int DefaultMeshFormat = 0;   // 0 = STL, 1 = 3DXML
        private const bool DefaultExportMeshes = true;
        // Per-part tessellation mesh export. Default ON: validated on RAILGHOST
        // (geometry/orientation/units/frame correct, ~10 s mesh phase vs ~44 s
        // legacy whole-assembly hide/show restore) with per-body quality giving
        // uniform, display-independent detail. The legacy whole-assembly SaveAs
        // path remains available by unchecking "Fast mesh export".
        private const bool DefaultFastMeshExport = true;
        // Mesh quality for the per-part tessellation path:
        // 0 = Coarse, 1 = Medium, 2 = Fine, 3 = Very fine. Default Fine.
        private const int DefaultMeshQuality = 2;
        private const int MaxMeshQuality = 3;

        private static readonly log4net.ILog logger = Logger.GetLogger();

        public static int GetLastOutputFormat()
        {
            return ReadInt(OutputFormatValueName, DefaultOutputFormat);
        }

        public static int GetLastMeshFormat()
        {
            return ReadInt(MeshFormatValueName, DefaultMeshFormat);
        }

        public static bool GetLastExportMeshes()
        {
            return ReadInt(ExportMeshesValueName, DefaultExportMeshes ? 1 : 0) != 0;
        }

        public static void SetLastOutputFormat(int value)
        {
            WriteInt(OutputFormatValueName, value);
        }

        public static void SetLastMeshFormat(int value)
        {
            WriteInt(MeshFormatValueName, value);
        }

        public static void SetLastExportMeshes(bool value)
        {
            WriteInt(ExportMeshesValueName, value ? 1 : 0);
        }

        public static bool GetFastMeshExport()
        {
            return ReadInt(FastMeshExportValueName, DefaultFastMeshExport ? 1 : 0) != 0;
        }

        public static void SetFastMeshExport(bool value)
        {
            WriteInt(FastMeshExportValueName, value ? 1 : 0);
        }

        public static int GetMeshQuality()
        {
            return ClampMeshQuality(ReadInt(MeshQualityValueName, DefaultMeshQuality));
        }

        public static void SetMeshQuality(int value)
        {
            WriteInt(MeshQualityValueName, ClampMeshQuality(value));
        }

        public static int ClampMeshQuality(int value) =>
            value < 0 || value > MaxMeshQuality ? DefaultMeshQuality : value;

        // Combobox CurrentSelection is short-typed; clamp to the
        // documented range here so a stale registry entry left over
        // from a future schema doesn't crash an older add-in build.
        public static int ClampOutputFormat(int value) => value < 0 || value > 1 ? DefaultOutputFormat : value;
        public static int ClampMeshFormat(int value) => value < 0 || value > 1 ? DefaultMeshFormat : value;

        private static int ReadInt(string name, int defaultValue)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryRoot))
                {
                    if (key == null)
                    {
                        return defaultValue;
                    }
                    object raw = key.GetValue(name, null);
                    if (raw == null)
                    {
                        return defaultValue;
                    }
                    return Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                logger.Warn("ExportPreferences.ReadInt(" + name + ") failed: " + ex.Message);
                return defaultValue;
            }
        }

        private static void WriteInt(string name, int value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryRoot))
                {
                    if (key == null)
                    {
                        return;
                    }
                    key.SetValue(name, value, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                logger.Warn("ExportPreferences.WriteInt(" + name + ") failed: " + ex.Message);
            }
        }
    }
}
