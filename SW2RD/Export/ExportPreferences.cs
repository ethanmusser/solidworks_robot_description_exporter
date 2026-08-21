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
    /// stays on the SW model attribute as canonical Config JSON; this class
    /// is ONLY for choices that should pre-populate the Setup tab when the
    /// PMPage opens.
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
        private const string CustomChordFractionValueName = "CustomChordFraction";
        private const string CustomAngleDegValueName = "CustomAngleDeg";
        private const string CustomMaxChordMmValueName = "CustomMaxChordMm";
        private const string RotationFormatValueName = "RotationFormat";
        private const string AngleUnitValueName = "AngleUnit";
        private const string KeepResolvedValueName = "KeepResolvedAfterExport";

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
        // 0 = Very coarse, 1 = Coarse, 2 = Medium, 3 = Fine, 4 = Very fine,
        // 5 = Custom. Default Fine.
        private const int DefaultMeshQuality = 3;
        private const int MaxMeshQuality = 5;

        // Custom (level 5) tessellation overrides. Only consulted when the user
        // picks the "Custom" mesh-quality option. Ranges are clamped on read so a
        // stale / hand-edited registry value can't drive a runaway or degenerate
        // tessellation. ChordFraction is a fraction of each body's bbox diagonal;
        // AngleDeg is the surface-plane angle tolerance in degrees; MaxChordMm is
        // the per-body chord clamp in millimeters.
        private const double DefaultCustomChordFraction = 0.010;
        private const double MinCustomChordFraction = 0.0001;
        private const double MaxCustomChordFraction = 0.5;
        private const double DefaultCustomAngleDeg = 30.0;
        private const double MinCustomAngleDeg = 1.0;
        private const double MaxCustomAngleDeg = 60.0;
        private const double DefaultCustomMaxChordMm = 25.0;
        private const double MinCustomMaxChordMm = 0.01;
        private const double MaxCustomMaxChordMm = 1000.0;
        // MJCF frame-orientation representation:
        // 0 = Axis-angle, 1 = Quaternion, 2 = Euler. Default Axis-angle (the
        // most human-readable while still unambiguous). Mirrors the
        // MJCFRotationFormat enum order.
        private const int DefaultRotationFormat = 0;
        private const int MaxRotationFormat = 2;
        // MJCF angular unit: 0 = Degree, 1 = Radian. Default Degree (MuJoCo's
        // own default, so no <compiler angle> attribute is written). Mirrors the
        // MJCFAngleUnit enum order.
        private const int DefaultAngleUnit = 0;
        private const int MaxAngleUnit = 1;
        // Whether to keep components that were resolved for an export resolved
        // afterward, instead of reverting them to lightweight. Default OFF so
        // the export path returns the assembly to its prior low-memory /
        // PDM-friendly state; users who run repeated exports in one session
        // opt in to pay the resolve cost only once.
        private const bool DefaultKeepResolvedAfterExport = false;

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

        public static bool GetKeepResolvedAfterExport()
        {
            return ReadInt(KeepResolvedValueName, DefaultKeepResolvedAfterExport ? 1 : 0) != 0;
        }

        public static void SetKeepResolvedAfterExport(bool value)
        {
            WriteInt(KeepResolvedValueName, value ? 1 : 0);
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

        public static double GetCustomChordFraction()
        {
            return ClampCustomChordFraction(
                ReadDouble(CustomChordFractionValueName, DefaultCustomChordFraction));
        }

        public static void SetCustomChordFraction(double value)
        {
            WriteDouble(CustomChordFractionValueName, ClampCustomChordFraction(value));
        }

        public static double ClampCustomChordFraction(double value) =>
            Clamp(value, MinCustomChordFraction, MaxCustomChordFraction, DefaultCustomChordFraction);

        public static double GetCustomAngleDeg()
        {
            return ClampCustomAngleDeg(ReadDouble(CustomAngleDegValueName, DefaultCustomAngleDeg));
        }

        public static void SetCustomAngleDeg(double value)
        {
            WriteDouble(CustomAngleDegValueName, ClampCustomAngleDeg(value));
        }

        public static double ClampCustomAngleDeg(double value) =>
            Clamp(value, MinCustomAngleDeg, MaxCustomAngleDeg, DefaultCustomAngleDeg);

        public static double GetCustomMaxChordMm()
        {
            return ClampCustomMaxChordMm(ReadDouble(CustomMaxChordMmValueName, DefaultCustomMaxChordMm));
        }

        public static void SetCustomMaxChordMm(double value)
        {
            WriteDouble(CustomMaxChordMmValueName, ClampCustomMaxChordMm(value));
        }

        public static double ClampCustomMaxChordMm(double value) =>
            Clamp(value, MinCustomMaxChordMm, MaxCustomMaxChordMm, DefaultCustomMaxChordMm);

        // Clamps to [min, max]; a NaN / infinity falls back to the default.
        private static double Clamp(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return fallback;
            }
            if (value < min)
            {
                return min;
            }
            return value > max ? max : value;
        }

        public static int GetRotationFormat()
        {
            return ClampRotationFormat(ReadInt(RotationFormatValueName, DefaultRotationFormat));
        }

        public static void SetRotationFormat(int value)
        {
            WriteInt(RotationFormatValueName, ClampRotationFormat(value));
        }

        public static int ClampRotationFormat(int value) =>
            value < 0 || value > MaxRotationFormat ? DefaultRotationFormat : value;

        public static int GetAngleUnit()
        {
            return ClampAngleUnit(ReadInt(AngleUnitValueName, DefaultAngleUnit));
        }

        public static void SetAngleUnit(int value)
        {
            WriteInt(AngleUnitValueName, ClampAngleUnit(value));
        }

        public static int ClampAngleUnit(int value) =>
            value < 0 || value > MaxAngleUnit ? DefaultAngleUnit : value;

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

        // Doubles are stored as invariant-culture strings (REG_SZ) because the
        // registry has no native floating-point value kind and the DWORD path
        // used by ReadInt/WriteInt would truncate the fractional part.
        private static double ReadDouble(string name, double defaultValue)
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
                    if (double.TryParse(
                        Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double parsed))
                    {
                        return parsed;
                    }
                    return defaultValue;
                }
            }
            catch (Exception ex)
            {
                logger.Warn("ExportPreferences.ReadDouble(" + name + ") failed: " + ex.Message);
                return defaultValue;
            }
        }

        private static void WriteDouble(string name, double value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryRoot))
                {
                    if (key == null)
                    {
                        return;
                    }
                    key.SetValue(
                        name,
                        value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                logger.Warn("ExportPreferences.WriteDouble(" + name + ") failed: " + ex.Message);
            }
        }
    }
}
