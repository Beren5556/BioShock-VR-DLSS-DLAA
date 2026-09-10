using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("BioShock 1-2 VR - DLSS/DLAA - Beren5556")]
[assembly: AssemblyDescription("Launcher and safe editor with Normal, DLAA and DLSS 4.5 modes")]
[assembly: AssemblyProduct("DLSS 4.5 add-on for BioShock VR")]
[assembly: AssemblyVersion("0.2.17.0")]
[assembly: AssemblyFileVersion("0.2.17.0")]

namespace BioshockVrLauncher
{
    internal enum ParamKind
    {
        Number,
        Boolean,
        Choice
    }

    internal sealed class ParamDef
    {
        public string Key;
        public string Category;
        public string Label;
        public string Description;
        public string Unit;
        public ParamKind Kind;
        public decimal Minimum;
        public decimal Maximum;
        public decimal Increment;
        public int Decimals;
        public string DefaultValue;
        public string[] Choices;

        public static ParamDef Number(string key, string category, string label,
                                      string description, string unit, decimal minimum,
                                      decimal maximum, decimal increment, int decimals,
                                      string defaultValue)
        {
            ParamDef result = new ParamDef();
            result.Key = key;
            result.Category = category;
            result.Label = label;
            result.Description = description;
            result.Unit = unit;
            result.Kind = ParamKind.Number;
            result.Minimum = minimum;
            result.Maximum = maximum;
            result.Increment = increment;
            result.Decimals = decimals;
            result.DefaultValue = defaultValue;
            return result;
        }

        public static ParamDef Boolean(string key, string category, string label,
                                       string description, bool defaultValue)
        {
            ParamDef result = new ParamDef();
            result.Key = key;
            result.Category = category;
            result.Label = label;
            result.Description = description;
            result.Unit = string.Empty;
            result.Kind = ParamKind.Boolean;
            result.DefaultValue = defaultValue ? "1" : "0";
            return result;
        }

        public static ParamDef Choice(string key, string category, string label,
                                      string description, string[] choices, int defaultValue)
        {
            ParamDef result = new ParamDef();
            result.Key = key;
            result.Category = category;
            result.Label = label;
            result.Description = description;
            result.Unit = string.Empty;
            result.Kind = ParamKind.Choice;
            result.Minimum = 0;
            result.Maximum = choices.Length - 1;
            result.Choices = choices;
            result.DefaultValue = defaultValue.ToString(CultureInfo.InvariantCulture);
            return result;
        }
    }

    internal static class ConfigDocument
    {
        public static Dictionary<string, string> Parse(string content)
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string normalized = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;
                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;
                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                if (key.Length > 0)
                    values[key] = value;
            }
            return values;
        }

        public static string Render(string original, IList<ParamDef> definitions,
                                    Dictionary<string, string> replacements)
        {
            string normalized = (original ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            List<string> lines = new List<string>(normalized.Split('\n'));
            if (lines.Count == 1 && lines[0].Length == 0)
            {
                lines.Clear();
                lines.Add("# " + GameProfile.DisplayName + " VR - values saved by the English launcher");
            }

            HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("#") || trimmed.StartsWith(";") || trimmed.Length == 0)
                    continue;
                int equals = trimmed.IndexOf('=');
                if (equals <= 0)
                    continue;
                string key = trimmed.Substring(0, equals).Trim();
                string value;
                if (replacements.TryGetValue(key, out value))
                {
                    if (written.Contains(key))
                        lines[i] = "; duplicate disabled by the launcher: " + trimmed;
                    else
                    {
                        lines[i] = key + "=" + value;
                        written.Add(key);
                    }
                }
            }

            if (lines.Count > 0 && lines[lines.Count - 1].Length != 0)
                lines.Add(string.Empty);
            foreach (ParamDef definition in definitions)
            {
                if (!written.Contains(definition.Key))
                {
                    lines.Add(definition.Key + "=" + replacements[definition.Key]);
                    written.Add(definition.Key);
                }
            }
            return string.Join(Environment.NewLine, lines.ToArray()).TrimEnd('\r', '\n') +
                   Environment.NewLine;
        }

        public static bool SelfTest()
        {
            List<ParamDef> definitions = new List<ParamDef>();
            definitions.Add(ParamDef.Number("worldScale", "Prueba", "Escala", "Prueba", "", 10, 200, 1, 1, "100.0"));
            definitions.Add(ParamDef.Boolean("autoVr", "Prueba", "Auto", "Prueba", true));
            Dictionary<string, string> replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            replacements["worldScale"] = "95.0";
            replacements["autoVr"] = "1";
            string input = "# comentario\r\nworldScale=100.0\r\nunknownKey=conservar\r\n";
            string output = Render(input, definitions, replacements);
            Dictionary<string, string> parsed = Parse(output);
            return output.Contains("# comentario") && output.Contains("unknownKey=conservar") &&
                   parsed.ContainsKey("worldScale") && parsed["worldScale"] == "95.0" &&
                   parsed.ContainsKey("autoVr") && parsed["autoVr"] == "1";
        }
    }

    internal sealed class UpscalerSettings
    {
        public bool Enabled;
        public int OutputWidth;
        public int OutputHeight;
        public decimal Sharpness;
    }

    internal static class UpscalerConfigDocument
    {
        private static readonly string[] Keys = new string[] {
            "enabled", "outputWidth", "outputHeight", "sharpness"
        };

        public static UpscalerSettings Defaults(int renderWidth, int renderHeight)
        {
            UpscalerSettings settings = new UpscalerSettings();
            settings.Enabled = false;
            settings.OutputWidth = ClampDimension(renderWidth);
            settings.OutputHeight = ClampDimension(renderHeight);
            settings.Sharpness = 0.20m;
            return settings;
        }

        private static int ClampDimension(int value)
        {
            if (value < 1024) return 2048;
            if (value > 8192) return 8192;
            return value;
        }

        private static int GreatestCommonDivisor(int first, int second)
        {
            first = Math.Abs(first);
            second = Math.Abs(second);
            while (second != 0)
            {
                int remainder = first % second;
                first = second;
                second = remainder;
            }
            return Math.Max(1, first);
        }

        public static bool TryCalculateProportionalOutput(int renderWidth, int renderHeight,
                                                           double scale, out int outputWidth,
                                                           out int outputHeight)
        {
            outputWidth = 0;
            outputHeight = 0;
            if (renderWidth < 1 || renderHeight < 1 || scale < 1.0)
                return false;

            int divisor = GreatestCommonDivisor(renderWidth, renderHeight);
            int ratioWidth = renderWidth / divisor;
            int ratioHeight = renderHeight / divisor;
            double targetMultiplier = divisor * scale;
            int evenMultiplier = 2 * (int)Math.Round(targetMultiplier / 2.0,
                                                      MidpointRounding.AwayFromZero);
            if (evenMultiplier < 2) evenMultiplier = 2;
            long width = (long)ratioWidth * evenMultiplier;
            long height = (long)ratioHeight * evenMultiplier;
            if (width < renderWidth || height < renderHeight || width > 8192 || height > 8192)
                return false;
            outputWidth = (int)width;
            outputHeight = (int)height;
            return (outputWidth & 1) == 0 && (outputHeight & 1) == 0;
        }

        public static bool HasExactAspect(int renderWidth, int renderHeight,
                                          int outputWidth, int outputHeight)
        {
            if (renderWidth < 1 || renderHeight < 1 || outputWidth < 1 || outputHeight < 1)
                return false;
            return (long)outputWidth * renderHeight == (long)outputHeight * renderWidth;
        }

        private static Dictionary<string, string> ReadSpatialValues(string content,
                                                                     out string structureError)
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool inSpatial = false;
            bool foundSpatial = false;
            string normalized = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                {
                    inSpatial = string.Equals(line.Substring(1, line.Length - 2).Trim(),
                                              "spatial", StringComparison.OrdinalIgnoreCase);
                    if (inSpatial) foundSpatial = true;
                    continue;
                }
                if (!inSpatial) continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                bool known = false;
                foreach (string candidate in Keys)
                {
                    if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                    {
                        known = true;
                        key = candidate;
                        break;
                    }
                }
                if (!known) continue;
                if (values.ContainsKey(key)) duplicates.Add(key);
                else values[key] = value;
            }

            if (!foundSpatial)
                structureError = "The [spatial] section is missing.";
            else if (duplicates.Count > 0)
                structureError = "There are duplicate keys in [spatial].";
            else
                structureError = null;
            return values;
        }

        public static bool TryParse(string content, int renderWidth, int renderHeight,
                                    out UpscalerSettings settings, out string warning)
        {
            settings = Defaults(renderWidth, renderHeight);
            if (string.IsNullOrWhiteSpace(content))
            {
                warning = null;
                return true;
            }

            string structureError;
            Dictionary<string, string> values = ReadSpatialValues(content, out structureError);
            List<string> problems = new List<string>();
            if (!string.IsNullOrEmpty(structureError)) problems.Add(structureError);

            string raw;
            if (!values.TryGetValue("enabled", out raw))
                problems.Add("Missing enabled.");
            else if (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                settings.Enabled = true;
            else if (raw == "0" || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                settings.Enabled = false;
            else
                problems.Add("enabled must be 0 or 1.");

            int number;
            if (!values.TryGetValue("outputWidth", out raw))
                problems.Add("Missing outputWidth.");
            else if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                     number < 1024 || number > 8192)
                problems.Add("outputWidth must be between 1024 and 8192.");
            else
                settings.OutputWidth = number;

            if (!values.TryGetValue("outputHeight", out raw))
                problems.Add("Missing outputHeight.");
            else if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                     number < 1024 || number > 8192)
                problems.Add("outputHeight must be between 1024 and 8192.");
            else
                settings.OutputHeight = number;

            decimal sharpness;
            if (!values.TryGetValue("sharpness", out raw))
                problems.Add("Missing sharpness.");
            else if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out sharpness) ||
                     sharpness < 0m || sharpness > 1m)
                problems.Add("sharpness must be between 0.00 and 1.00.");
            else
                settings.Sharpness = sharpness;

            warning = string.Join(" ", problems.ToArray());
            return problems.Count == 0;
        }

        public static string Render(string original, UpscalerSettings settings)
        {
            string normalized = (original ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            List<string> lines = new List<string>(normalized.Split('\n'));
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            Dictionary<string, string> replacements =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            replacements["enabled"] = settings.Enabled ? "1" : "0";
            replacements["outputWidth"] = settings.OutputWidth.ToString(CultureInfo.InvariantCulture);
            replacements["outputHeight"] = settings.OutputHeight.ToString(CultureInfo.InvariantCulture);
            replacements["sharpness"] = settings.Sharpness.ToString("F2", CultureInfo.InvariantCulture);

            bool inSpatial = false;
            bool foundSpatial = false;
            int spatialEnd = -1;
            HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && trimmed.Length > 2)
                {
                    bool nextSpatial = string.Equals(trimmed.Substring(1, trimmed.Length - 2).Trim(),
                                                     "spatial", StringComparison.OrdinalIgnoreCase);
                    if (inSpatial && !nextSpatial && spatialEnd < 0) spatialEnd = i;
                    inSpatial = nextSpatial;
                    if (inSpatial) foundSpatial = true;
                    continue;
                }
                if (!inSpatial || trimmed.StartsWith("#") || trimmed.StartsWith(";") ||
                    trimmed.Length == 0) continue;
                int equals = trimmed.IndexOf('=');
                if (equals <= 0) continue;
                string key = trimmed.Substring(0, equals).Trim();
                string value;
                if (replacements.TryGetValue(key, out value))
                {
                    lines[i] = key + "=" + value;
                    written.Add(key);
                }
            }
            if (inSpatial && spatialEnd < 0) spatialEnd = lines.Count;

            if (!foundSpatial)
            {
                if (lines.Count > 0) lines.Add(string.Empty);
                lines.Add("# Experimental spatial upscaling for " + GameProfile.DisplayName + " VR");
                lines.Add("# Does not enable DLSS or DLAA. Used only by the compatible experimental DLL.");
                lines.Add("[spatial]");
                spatialEnd = lines.Count;
            }

            foreach (string key in Keys)
            {
                if (!written.Contains(key))
                {
                    lines.Insert(spatialEnd, key + "=" + replacements[key]);
                    spatialEnd++;
                }
            }
            return string.Join(Environment.NewLine, lines.ToArray()).TrimEnd('\r', '\n') +
                   Environment.NewLine;
        }

        public static bool SelfTest()
        {
            UpscalerSettings input = new UpscalerSettings();
            input.Enabled = true;
            input.OutputWidth = 4504;
            input.OutputHeight = 4504;
            input.Sharpness = 0.25m;
            string original = "; conservar\r\n[spatial]\r\nenabled=0\r\nunknown=ok\r\n" +
                              "outputWidth=2048\r\noutputHeight=2048\r\nsharpness=0.10\r\n";
            string rendered = Render(original, input);
            UpscalerSettings parsed;
            string warning;
            bool valid = TryParse(rendered, 4056, 4056, out parsed, out warning);
            input.Enabled = false;
            string disabled = Render(rendered, input);
            UpscalerSettings disabledParsed;
            string disabledWarning;
            bool disabledValid = TryParse(disabled, 4056, 4056,
                                          out disabledParsed, out disabledWarning);
            int squareWidth, squareHeight, wideWidth, wideHeight;
            bool squarePreset = TryCalculateProportionalOutput(4056, 4056, 1.10,
                                                               out squareWidth, out squareHeight);
            bool widePreset = TryCalculateProportionalOutput(1920, 1080, 1.10,
                                                             out wideWidth, out wideHeight);
            bool exactAspect = HasExactAspect(1920, 1080, 2112, 1188) &&
                               !HasExactAspect(1920, 1080, 2112, 1189);
            return valid && string.IsNullOrEmpty(warning) && parsed.Enabled &&
                   parsed.OutputWidth == 4504 && parsed.OutputHeight == 4504 &&
                   parsed.Sharpness == 0.25m && rendered.Contains("unknown=ok") &&
                   rendered.Contains("; conservar") && disabledValid &&
                   string.IsNullOrEmpty(disabledWarning) && !disabledParsed.Enabled &&
                   disabled.Contains("unknown=ok") && disabled.Contains("; conservar") &&
                   squarePreset &&
                   squareWidth == 4462 && squareHeight == 4462 && widePreset &&
                   wideWidth == 2112 && wideHeight == 1188 && exactAspect;
        }
    }

    internal enum DlssMode
    {
        Off,
        Dlaa,
        SuperResolution
    }

    internal enum DlssQuality
    {
        UltraPerformance,
        Percent40,
        Performance,
        Balanced,
        Percent60,
        Quality,
        Percent70,
        Percent80,
        Percent90,
        Custom
    }

    internal sealed class DlssSettings
    {
        public DlssMode Mode;
        public string Runtime;
        public string Preset;
        public DlssQuality Quality;
        public int OutputWidth;
        public int OutputHeight;
        public decimal NearPlaneUu;
        public int SrScaleNumerator;
        public int SrScaleDenominator;
        public int SharpnessPercent;
    }

    internal static class DlssConfigDocument
    {
        public const string RequiredRuntime = "310.7.0";
        public const string TestedRuntimeDisplay = "310.7.0.0";

        private static readonly string[] Keys = new string[] {
            "mode", "runtime", "preset", "quality", "outputWidth", "outputHeight",
            "nearPlaneUu", "srScaleNumerator", "srScaleDenominator", "sharpnessPercent"
        };

        public static DlssSettings Defaults(int renderWidth, int renderHeight)
        {
            DlssSettings settings = new DlssSettings();
            settings.Mode = DlssMode.Off;
            settings.Runtime = RequiredRuntime;
            settings.Preset = "auto";
            settings.Quality = DlssQuality.Custom;
            settings.OutputWidth = ClampDimension(renderWidth);
            settings.OutputHeight = ClampDimension(renderHeight);
            settings.NearPlaneUu = 10.0m;
            settings.SrScaleNumerator = 2;
            settings.SrScaleDenominator = 3;
            return settings;
        }

        private static int ClampDimension(int value)
        {
            if (value < 1024) return 2048;
            if (value > 8192) return 8192;
            return value;
        }

        private static Dictionary<string, string> ReadDlssValues(string content,
                                                                  out string structureError)
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool inDlss = false;
            bool foundDlss = false;
            string normalized = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                {
                    inDlss = string.Equals(line.Substring(1, line.Length - 2).Trim(),
                                           "dlss", StringComparison.OrdinalIgnoreCase);
                    if (inDlss) foundDlss = true;
                    continue;
                }
                if (!inDlss) continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                bool known = false;
                foreach (string candidate in Keys)
                {
                    if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                    {
                        known = true;
                        key = candidate;
                        break;
                    }
                }
                if (!known) continue;
                if (values.ContainsKey(key)) duplicates.Add(key);
                else values[key] = value;
            }

            if (!foundDlss)
                structureError = "The [dlss] section is missing.";
            else if (duplicates.Count > 0)
                structureError = "There are duplicate keys in [dlss].";
            else
                structureError = null;
            return values;
        }

        private static bool TryParseMode(string raw, out DlssMode mode)
        {
            if (string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase))
            {
                mode = DlssMode.Off;
                return true;
            }
            if (string.Equals(raw, "dlaa", StringComparison.OrdinalIgnoreCase))
            {
                mode = DlssMode.Dlaa;
                return true;
            }
            if (string.Equals(raw, "sr", StringComparison.OrdinalIgnoreCase))
            {
                mode = DlssMode.SuperResolution;
                return true;
            }
            mode = DlssMode.Off;
            return false;
        }

        private static bool TryParseQuality(string raw, out DlssQuality quality)
        {
            if (string.Equals(raw, "auto", StringComparison.OrdinalIgnoreCase))
            {
                quality = DlssQuality.Custom;
                return true;
            }
            if (string.Equals(raw, "quality", StringComparison.OrdinalIgnoreCase))
            {
                quality = DlssQuality.Quality;
                return true;
            }
            if (string.Equals(raw, "balanced", StringComparison.OrdinalIgnoreCase))
            {
                quality = DlssQuality.Balanced;
                return true;
            }
            if (string.Equals(raw, "performance", StringComparison.OrdinalIgnoreCase))
            {
                quality = DlssQuality.Performance;
                return true;
            }
            if (string.Equals(raw, "ultra_performance", StringComparison.OrdinalIgnoreCase))
            {
                quality = DlssQuality.UltraPerformance;
                return true;
            }
            quality = DlssQuality.Custom;
            return false;
        }

        private static string ModeValue(DlssMode mode)
        {
            if (mode == DlssMode.Dlaa) return "dlaa";
            if (mode == DlssMode.SuperResolution) return "sr";
            return "off";
        }

        private static bool IsValidPreset(string preset)
        {
            return string.Equals(preset, "auto", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(preset, "K", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(preset, "M", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(preset, "L", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePreset(string preset)
        {
            if (string.Equals(preset, "K", StringComparison.OrdinalIgnoreCase)) return "K";
            if (string.Equals(preset, "M", StringComparison.OrdinalIgnoreCase)) return "M";
            if (string.Equals(preset, "L", StringComparison.OrdinalIgnoreCase)) return "L";
            return "auto";
        }

        public static bool TryParse(string content, int renderWidth, int renderHeight,
                                    out DlssSettings settings, out string warning)
        {
            settings = Defaults(renderWidth, renderHeight);
            if (string.IsNullOrWhiteSpace(content))
            {
                warning = null;
                return true;
            }

            string structureError;
            Dictionary<string, string> values = ReadDlssValues(content, out structureError);
            List<string> problems = new List<string>();
            if (!string.IsNullOrEmpty(structureError)) problems.Add(structureError);

            string raw;
            DlssMode mode;
            if (!values.TryGetValue("mode", out raw))
                problems.Add("Missing mode.");
            else if (!TryParseMode(raw, out mode))
                problems.Add("mode must be off, dlaa or sr.");
            else
                settings.Mode = mode;

            if (!values.TryGetValue("runtime", out raw))
                problems.Add("Missing runtime.");
            else if (!string.Equals(raw, RequiredRuntime, StringComparison.OrdinalIgnoreCase))
                problems.Add("runtime must be " + RequiredRuntime + ".");
            settings.Runtime = RequiredRuntime;

            if (!values.TryGetValue("preset", out raw))
                problems.Add("Missing preset.");
            else if (!IsValidPreset(raw))
                problems.Add("preset must be auto, K, M or L.");
            else
                settings.Preset = NormalizePreset(raw);

            DlssQuality quality;
            if (!values.TryGetValue("quality", out raw))
                problems.Add("Missing quality.");
            else if (!TryParseQuality(raw, out quality))
                problems.Add("quality must be auto, quality, balanced, performance or ultra_performance.");
            else
                settings.Quality = quality;

            int number;
            if (!values.TryGetValue("outputWidth", out raw))
                problems.Add("Missing outputWidth.");
            else if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                     number < 1024 || number > 8192 || (number & 1) != 0)
                problems.Add("outputWidth must be even and between 1024 and 8192.");
            else
                settings.OutputWidth = number;

            if (!values.TryGetValue("outputHeight", out raw))
                problems.Add("Missing outputHeight.");
            else if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                     number < 1024 || number > 8192 || (number & 1) != 0)
                problems.Add("outputHeight must be even and between 1024 and 8192.");
            else
                settings.OutputHeight = number;

            decimal nearPlane;
            if (values.TryGetValue("nearPlaneUu", out raw))
            {
                string normalizedNear = raw.Replace(',', '.');
                if (!decimal.TryParse(normalizedNear, NumberStyles.Float,
                                      CultureInfo.InvariantCulture, out nearPlane) ||
                    nearPlane < 0.1m || nearPlane > 1000.0m)
                    problems.Add("nearPlaneUu must be between 0.1 and 1000.0 UU.");
                else
                    settings.NearPlaneUu = nearPlane;
            }

            if (values.TryGetValue("sharpnessPercent", out raw))
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                    number < 0 || number > 100)
                    problems.Add("sharpnessPercent must be between 0 and 100.");
                else settings.SharpnessPercent = number;
            }

            // Los archivos anteriores no recordaban la preferencia al pasar por DLAA.
            // Se migra del ratio real sin cambiar el archivo al cargarlo.
            if (settings.Mode == DlssMode.SuperResolution &&
                renderWidth < settings.OutputWidth && renderHeight < settings.OutputHeight &&
                UpscalerConfigDocument.HasExactAspect(renderWidth, renderHeight,
                                                      settings.OutputWidth, settings.OutputHeight))
            {
                DlssQuality inferred;
                if (!DlssQualityPolicy.TryMatchCanonical(renderWidth, renderHeight,
                        settings.OutputWidth, settings.OutputHeight, out inferred) ||
                    !DlssQualityPolicy.TryGetFraction(inferred,
                        out settings.SrScaleNumerator, out settings.SrScaleDenominator))
                {
                    settings.SrScaleNumerator = renderWidth;
                    settings.SrScaleDenominator = settings.OutputWidth;
                }
            }
            bool hasNumerator = values.ContainsKey("srScaleNumerator");
            bool hasDenominator = values.ContainsKey("srScaleDenominator");
            if (hasNumerator || hasDenominator)
            {
                int numerator, denominator;
                if (!hasNumerator || !hasDenominator ||
                    !int.TryParse(values["srScaleNumerator"], NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out numerator) ||
                    !int.TryParse(values["srScaleDenominator"], NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out denominator) ||
                    numerator <= 0 || denominator <= numerator || denominator > 8192)
                    problems.Add("The preferred SR scale must be a valid fraction between 0 and 1.");
                else
                {
                    settings.SrScaleNumerator = numerator;
                    settings.SrScaleDenominator = denominator;
                }
            }

            warning = string.Join(" ", problems.ToArray());
            return problems.Count == 0;
        }

        public static string Render(string original, DlssSettings settings)
        {
            string normalized = (original ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            List<string> lines = new List<string>(normalized.Split('\n'));
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            Dictionary<string, string> replacements =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            replacements["mode"] = ModeValue(settings.Mode);
            replacements["runtime"] = RequiredRuntime;
            replacements["preset"] = NormalizePreset(settings.Preset);
            // El host deduce la calidad real del ratio render→salida.
            replacements["quality"] = "auto";
            replacements["outputWidth"] = settings.OutputWidth.ToString(CultureInfo.InvariantCulture);
            replacements["outputHeight"] = settings.OutputHeight.ToString(CultureInfo.InvariantCulture);
            replacements["nearPlaneUu"] = settings.NearPlaneUu.ToString("0.0##", CultureInfo.InvariantCulture);
            replacements["srScaleNumerator"] = settings.SrScaleNumerator.ToString(CultureInfo.InvariantCulture);
            replacements["srScaleDenominator"] = settings.SrScaleDenominator.ToString(CultureInfo.InvariantCulture);
            replacements["sharpnessPercent"] = settings.SharpnessPercent.ToString(CultureInfo.InvariantCulture);

            bool inDlss = false;
            bool foundDlss = false;
            int dlssEnd = -1;
            HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && trimmed.Length > 2)
                {
                    bool nextDlss = string.Equals(trimmed.Substring(1, trimmed.Length - 2).Trim(),
                                                  "dlss", StringComparison.OrdinalIgnoreCase);
                    if (inDlss && !nextDlss && dlssEnd < 0) dlssEnd = i;
                    inDlss = nextDlss;
                    if (inDlss) foundDlss = true;
                    continue;
                }
                if (!inDlss || trimmed.StartsWith("#") || trimmed.StartsWith(";") ||
                    trimmed.Length == 0) continue;
                int equals = trimmed.IndexOf('=');
                if (equals <= 0) continue;
                string key = trimmed.Substring(0, equals).Trim();
                string value;
                if (replacements.TryGetValue(key, out value))
                {
                    if (written.Contains(key))
                        lines[i] = "; duplicate disabled by the launcher: " + trimmed;
                    else
                    {
                        lines[i] = key + "=" + value;
                        written.Add(key);
                    }
                }
            }
            if (inDlss && dlssEnd < 0) dlssEnd = lines.Count;

            if (!foundDlss)
            {
                if (lines.Count > 0) lines.Add(string.Empty);
                lines.Add("# DLSS 4.5 add-on for " + GameProfile.DisplayName + " VR - integration by Beren5556");
                lines.Add("# Requires two eye histories, depth, motion, jitter and an x64 host.");
                lines.Add("[dlss]");
                dlssEnd = lines.Count;
            }

            foreach (string key in Keys)
            {
                if (!written.Contains(key))
                {
                    lines.Insert(dlssEnd, key + "=" + replacements[key]);
                    dlssEnd++;
                }
            }
            return string.Join(Environment.NewLine, lines.ToArray()).TrimEnd('\r', '\n') +
                   Environment.NewLine;
        }

        public static bool SelfTest()
        {
            DlssSettings input = new DlssSettings();
            input.Mode = DlssMode.SuperResolution;
            input.Runtime = RequiredRuntime;
            input.Preset = "M";
            input.Quality = DlssQuality.Performance;
            input.OutputWidth = 4504;
            input.OutputHeight = 4504;
            input.NearPlaneUu = 12.5m;
            input.SrScaleNumerator = 1;
            input.SrScaleDenominator = 2;
            input.SharpnessPercent = 35;
            string original = "; conservar\r\n[dlss]\r\nmode=off\r\nruntime=310.7.0\r\n" +
                              "preset=auto\r\nquality=quality\r\nunknown=ok\r\n" +
                              "outputWidth=2048\r\noutputHeight=2048\r\n";
            string rendered = Render(original, input);
            DlssSettings parsed;
            string warning;
            bool valid = TryParse(rendered, 4056, 4056, out parsed, out warning);
            string duplicate = rendered.Replace("preset=M", "preset=M\r\npreset=L");
            string repaired = Render(duplicate, input);
            DlssSettings repairedSettings;
            string repairedWarning;
            bool repairedValid = TryParse(repaired, 4056, 4056,
                                          out repairedSettings, out repairedWarning);
            string legacyWithoutNear = rendered.Replace(
                "nearPlaneUu=12.5" + Environment.NewLine, string.Empty);
            DlssSettings legacySettings;
            string legacyWarning;
            bool legacyValid = TryParse(legacyWithoutNear, 4056, 4056,
                                        out legacySettings, out legacyWarning);
            string invalidNear = rendered.Replace("nearPlaneUu=12.5", "nearPlaneUu=0.0");
            DlssSettings invalidNearSettings;
            string invalidNearWarning;
            bool invalidNearAccepted = TryParse(invalidNear, 4056, 4056,
                                                out invalidNearSettings,
                                                out invalidNearWarning);
            input.Mode = DlssMode.Dlaa;
            DlssSettings remembered;
            string rememberedWarning;
            bool remembersSrInDlaa = TryParse(Render(rendered, input), 4504, 4504,
                out remembered, out rememberedWarning) && remembered.SrScaleNumerator == 1 &&
                remembered.SrScaleDenominator == 2;
            input.Mode = DlssMode.Off;
            bool remembersSrInNormal = TryParse(Render(rendered, input), 4504, 4504,
                out remembered, out rememberedWarning) && remembered.SrScaleNumerator == 1 &&
                remembered.SrScaleDenominator == 2;
            string legacyWithoutScale = original.Replace("mode=off", "mode=sr")
                .Replace("outputWidth=2048", "outputWidth=4096")
                .Replace("outputHeight=2048", "outputHeight=4096");
            bool migratesLegacy = TryParse(legacyWithoutScale, 2730, 2730,
                out remembered, out rememberedWarning) && remembered.SrScaleNumerator == 2 &&
                remembered.SrScaleDenominator == 3;
            return valid && string.IsNullOrEmpty(warning) &&
                   parsed.Mode == DlssMode.SuperResolution &&
                   parsed.Runtime == RequiredRuntime && parsed.Preset == "M" &&
                   parsed.Quality == DlssQuality.Custom &&
                   parsed.OutputWidth == 4504 && parsed.OutputHeight == 4504 &&
                   parsed.NearPlaneUu == 12.5m && parsed.SharpnessPercent == 35 &&
                   parsed.SrScaleNumerator == 1 && parsed.SrScaleDenominator == 2 &&
                   rendered.Contains("unknown=ok") && rendered.Contains("; conservar") &&
                   repairedValid && string.IsNullOrEmpty(repairedWarning) &&
                   repaired.Contains("; duplicate disabled by the launcher: preset=L") &&
                   legacyValid && string.IsNullOrEmpty(legacyWarning) &&
                   legacySettings.NearPlaneUu == 10.0m && !invalidNearAccepted &&
                   invalidNearWarning.Contains("nearPlaneUu") && remembersSrInDlaa &&
                   remembersSrInNormal && migratesLegacy;
        }
    }

    internal static class DlssQualityPolicy
    {
        private static int GreatestCommonDivisor(int first, int second)
        {
            first = Math.Abs(first);
            second = Math.Abs(second);
            while (second != 0)
            {
                int remainder = first % second;
                first = second;
                second = remainder;
            }
            return Math.Max(1, first);
        }

        public static bool TryGetFraction(DlssQuality quality,
                                           out int numerator, out int denominator)
        {
            numerator = 0;
            denominator = 1;
            if (quality == DlssQuality.Quality)
            {
                numerator = 2;
                denominator = 3;
                return true;
            }
            if (quality == DlssQuality.Balanced)
            {
                numerator = 29;
                denominator = 50;
                return true;
            }
            if (quality == DlssQuality.Performance)
            {
                numerator = 1;
                denominator = 2;
                return true;
            }
            if (quality == DlssQuality.UltraPerformance)
            {
                numerator = 1;
                denominator = 3;
                return true;
            }
            if (quality == DlssQuality.Percent40 || quality == DlssQuality.Percent60 ||
                quality == DlssQuality.Percent70 || quality == DlssQuality.Percent80 ||
                quality == DlssQuality.Percent90)
            {
                numerator = quality == DlssQuality.Percent40 ? 4 :
                    quality == DlssQuality.Percent60 ? 6 :
                    quality == DlssQuality.Percent70 ? 7 :
                    quality == DlssQuality.Percent80 ? 8 : 9;
                denominator = 10;
                return true;
            }
            return false;
        }

        public static bool TryCalculateRender(int outputWidth, int outputHeight,
                                              DlssQuality quality,
                                              out int renderWidth, out int renderHeight)
        {
            int numerator, denominator;
            if (!TryGetFraction(quality, out numerator, out denominator))
            {
                renderWidth = 0;
                renderHeight = 0;
                return false;
            }
            return TryCalculateRender(outputWidth, outputHeight, numerator, denominator,
                                      out renderWidth, out renderHeight);
        }

        public static bool TryCalculateRender(int outputWidth, int outputHeight,
                                              int numerator, int denominator,
                                              out int renderWidth, out int renderHeight)
        {
            renderWidth = 0;
            renderHeight = 0;
            if (numerator <= 0 || denominator <= numerator || denominator > 8192 ||
                outputWidth < 1024 || outputWidth > 8192 ||
                outputHeight < 1024 || outputHeight > 8192 ||
                (outputWidth & 1) != 0 || (outputHeight & 1) != 0)
                return false;

            int divisor = GreatestCommonDivisor(outputWidth, outputHeight);
            int ratioWidth = outputWidth / divisor;
            int ratioHeight = outputHeight / divisor;
            long targetNumerator = (long)divisor * numerator;
            int floor = (int)(targetNumerator / denominator);
            int lowerEven = floor - (floor & 1);
            int upperEven = lowerEven + 2;
            int[] candidates = new int[] { lowerEven, upperEven };
            long bestError = long.MaxValue;
            int bestWidth = 0;
            int bestHeight = 0;
            foreach (int multiplier in candidates)
            {
                if (multiplier < 2 || multiplier > divisor) continue;
                long width = (long)ratioWidth * multiplier;
                long height = (long)ratioHeight * multiplier;
                if (width < 1024 || height < 1024 || width > 8192 || height > 8192 ||
                    width >= outputWidth || height >= outputHeight ||
                    (width & 1) != 0 || (height & 1) != 0)
                    continue;
                // Match image_control_policy.h: never round a >=50% request
                // below NGX's half-resolution boundary (2950: 1476, not 1474).
                // The host still validates the real runtime's advertised range.
                if ((long)numerator * 2 >= denominator &&
                    (width * 2 < outputWidth || height * 2 < outputHeight))
                    continue;
                long error = Math.Abs((long)denominator * multiplier - targetNumerator);
                if (error < bestError)
                {
                    bestError = error;
                    bestWidth = (int)width;
                    bestHeight = (int)height;
                }
            }
            if (bestWidth == 0 || bestHeight == 0) return false;
            renderWidth = bestWidth;
            renderHeight = bestHeight;
            return true;
        }

        public static bool TryMatchCanonical(int renderWidth, int renderHeight,
                                             int outputWidth, int outputHeight,
                                             out DlssQuality quality)
        {
            quality = DlssQuality.Custom;
            if (renderWidth < 1024 || renderHeight < 1024 ||
                (renderWidth & 1) != 0 || (renderHeight & 1) != 0 ||
                !UpscalerConfigDocument.HasExactAspect(renderWidth, renderHeight,
                                                        outputWidth, outputHeight))
                return false;
            DlssQuality[] canonical = new DlssQuality[] {
                DlssQuality.Quality,
                DlssQuality.Balanced,
                DlssQuality.Performance,
                DlssQuality.UltraPerformance,
                DlssQuality.Percent40,
                DlssQuality.Percent60,
                DlssQuality.Percent70,
                DlssQuality.Percent80,
                DlssQuality.Percent90
            };
            foreach (DlssQuality candidate in canonical)
            {
                int expectedWidth, expectedHeight;
                if (TryCalculateRender(outputWidth, outputHeight, candidate,
                                       out expectedWidth, out expectedHeight) &&
                    renderWidth == expectedWidth && renderHeight == expectedHeight)
                {
                    quality = candidate;
                    return true;
                }
            }
            return false;
        }

        public static bool SelfTest()
        {
            int width, height;
            bool quality = TryCalculateRender(4056, 4056, DlssQuality.Quality,
                                               out width, out height) &&
                           width == 2704 && height == 2704;
            bool balanced = TryCalculateRender(4056, 4056, DlssQuality.Balanced,
                                                out width, out height) &&
                            width == 2352 && height == 2352;
            bool performance = TryCalculateRender(4056, 4056, DlssQuality.Performance,
                                                   out width, out height) &&
                               width == 2028 && height == 2028;
            bool ultra = TryCalculateRender(4056, 4056, DlssQuality.UltraPerformance,
                                             out width, out height) &&
                         width == 1352 && height == 1352;

            DlssQuality matched;
            bool recognizesBalanced = TryMatchCanonical(2352, 2352, 4056, 4056,
                                                        out matched) &&
                                      matched == DlssQuality.Balanced;
            bool personalized = !TryMatchCanonical(2360, 2360, 4056, 4056,
                                                   out matched) &&
                                matched == DlssQuality.Custom;
            bool intermediateSteps = true;
            DlssQuality[] intermediate = new DlssQuality[] {
                DlssQuality.Percent40, DlssQuality.Percent60, DlssQuality.Percent70,
                DlssQuality.Percent80, DlssQuality.Percent90
            };
            int[] expected = new int[] { 1638, 2458, 2868, 3276, 3686 };
            for (int i = 0; i < intermediate.Length; i++)
                intermediateSteps &= TryCalculateRender(4096, 4096, intermediate[i],
                    out width, out height) && width == expected[i] && height == expected[i] &&
                    TryMatchCanonical(width, height, 4096, 4096, out matched) &&
                    matched == intermediate[i];
            bool minimumIsRespected = !TryCalculateRender(1024, 1024, DlssQuality.Quality,
                out width, out height);
            bool tiesRoundDown = TryCalculateRender(4096, 4096, 2049, 4096,
                out width, out height) && width == 2048 && height == 2048;
            bool oddPerformanceBoundary = TryCalculateRender(2950, 2950, 1, 2,
                out width, out height) && width == 1476 && height == 1476;
            bool allPerformanceBoundaries = true;
            for (int output = 2048; output <= 8192; output += 2)
                allPerformanceBoundaries &= TryCalculateRender(output, output, 1, 2,
                    out width, out height) && width * 2 >= output && height * 2 >= output &&
                    width <= output / 2 + 1 && (width & 1) == 0 && (height & 1) == 0;

            DlssSettings custom = DlssConfigDocument.Defaults(2360, 2360);
            custom.Mode = DlssMode.SuperResolution;
            custom.OutputWidth = 4056;
            custom.OutputHeight = 4056;
            custom.Quality = DlssQuality.Custom;
            string rendered = DlssConfigDocument.Render(string.Empty, custom);
            DlssSettings roundTrip;
            string warning;
            bool roundTripValid = DlssConfigDocument.TryParse(rendered, 2360, 2360,
                                                               out roundTrip, out warning);
            return quality && balanced && performance && ultra &&
                   recognizesBalanced && personalized && intermediateSteps &&
                   minimumIsRespected && tiesRoundDown && oddPerformanceBoundary &&
                   allPerformanceBoundaries && roundTripValid &&
                   string.IsNullOrEmpty(warning) &&
                   roundTrip.Quality == DlssQuality.Custom &&
                   rendered.Contains("quality=auto") &&
                   UpscalerConfigDocument.HasExactAspect(2360, 2360, 4056, 4056);
        }
    }

    internal sealed class DlssBackendStatus
    {
        public bool Ready;
        public int EyeHosts;
        public bool HostFound;
        public bool RuntimeFound;
        public bool RuntimeIs64Bit;
        public bool RuntimeMatches;
        public bool CapabilityFound;
        public string RuntimeVersion;
        public string HostDirectory;
        public string Summary;
    }

    internal enum IniImpact
    {
        Normal,
        VrDirect,
        Performance,
        Warning
    }

    internal sealed class IniEntry
    {
        public int LineIndex;
        public string Section;
        public string Key;
        public int ValueStart;
        public int ValueLength;
        public string OriginalValue;
        public string Value;
        public string FriendlyName;
        public string Description;
        public IniImpact Impact;
        public bool Protected;
        public int Occurrence;
        public int OccurrenceCount;

        public bool Changed
        {
            get { return Value != OriginalValue; }
        }
    }

    internal static class GameIniDocument
    {
        public static List<IniEntry> Parse(string content)
        {
            List<IniEntry> entries = new List<IniEntry>();
            string text = content ?? string.Empty;
            string section = "No section";
            int lineIndex = 0;
            int lineStart = 0;
            while (lineStart <= text.Length)
            {
                int lineEnd = lineStart;
                while (lineEnd < text.Length && text[lineEnd] != '\r' && text[lineEnd] != '\n')
                    lineEnd++;
                string rawLine = text.Substring(lineStart, lineEnd - lineStart);
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && trimmed.Length > 2)
                {
                    section = trimmed.Substring(1, trimmed.Length - 2);
                }
                else if (trimmed.Length > 0 && !trimmed.StartsWith("#") && !trimmed.StartsWith(";"))
                {
                    int equals = rawLine.IndexOf('=');
                    if (equals > 0)
                    {
                        IniEntry entry = new IniEntry();
                        entry.LineIndex = lineIndex;
                        entry.Section = section;
                        entry.Key = rawLine.Substring(0, equals).Trim();
                        entry.ValueStart = lineStart + equals + 1;
                        entry.ValueLength = rawLine.Length - equals - 1;
                        entry.OriginalValue = rawLine.Substring(equals + 1).Trim();
                        entry.Value = entry.OriginalValue;
                        IniKnowledge.Annotate(entry);
                        entries.Add(entry);
                    }
                }

                if (lineEnd >= text.Length)
                    break;
                if (text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n')
                    lineStart = lineEnd + 2;
                else
                    lineStart = lineEnd + 1;
                lineIndex++;
            }
            Dictionary<string, int> totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (IniEntry entry in entries)
            {
                string identity = entry.Section + "\u001f" + entry.Key;
                int count;
                totals.TryGetValue(identity, out count);
                totals[identity] = count + 1;
            }
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (IniEntry entry in entries)
            {
                string identity = entry.Section + "\u001f" + entry.Key;
                int count;
                seen.TryGetValue(identity, out count);
                entry.Occurrence = count + 1;
                entry.OccurrenceCount = totals[identity];
                seen[identity] = count + 1;
            }
            IniKnowledge.ApplyDocumentContext(entries);
            return entries;
        }

        public static string Render(string original, IList<IniEntry> entries)
        {
            StringBuilder result = new StringBuilder(original ?? string.Empty);
            List<IniEntry> changed = new List<IniEntry>();
            foreach (IniEntry entry in entries)
            {
                if (entry.Changed) changed.Add(entry);
            }
            changed.Sort(delegate(IniEntry a, IniEntry b) { return b.ValueStart.CompareTo(a.ValueStart); });
            foreach (IniEntry entry in changed)
            {
                if (entry.Value.IndexOf('\r') >= 0 || entry.Value.IndexOf('\n') >= 0)
                    throw new InvalidDataException("The value of " + entry.Key + " contains a line break.");
                if (entry.ValueStart < 0 || entry.ValueStart + entry.ValueLength > result.Length)
                    throw new InvalidDataException("Invalid position for " + entry.Key + ".");
                result.Remove(entry.ValueStart, entry.ValueLength);
                result.Insert(entry.ValueStart, entry.Value);
            }
            return result.ToString();
        }

        public static bool SelfTest()
        {
            string input = "[WinDrv.WindowsClient]\r\nWindowedViewportX=4096\r\n" +
                           "[XeDrv.XenonClient]\r\nWindowedViewportX=640\r\n";
            List<IniEntry> entries = Parse(input);
            if (entries.Count != 2 || entries[0].Section != "WinDrv.WindowsClient")
                return false;
            entries[0].Value = "4504";
            string output = Render(input, entries);
            if (!output.Contains("[WinDrv.WindowsClient]\r\nWindowedViewportX=4504") ||
                !output.Contains("[XeDrv.XenonClient]\r\nWindowedViewportX=640") ||
                output.Replace("4504", "4096") != input)
                return false;

            string mixed = "[Core.System]\nPaths=Uno\r\nPaths=Dos\n" +
                           "[WinDrv.WindowsClient]\r\nWindowedViewportX=4504\nWindowedViewportY=4504\r\n";
            List<IniEntry> mixedEntries = Parse(mixed);
            if (mixedEntries.Count != 4 || mixedEntries[0].Occurrence != 1 ||
                mixedEntries[0].OccurrenceCount != 2 || mixedEntries[1].Occurrence != 2 ||
                Render(mixed, mixedEntries) != mixed)
                return false;
            mixedEntries[2].Value = "8192";
            mixedEntries[3].Value = "1024";
            string mixedOutput = Render(mixed, mixedEntries);
            if (mixedOutput != "[Core.System]\nPaths=Uno\r\nPaths=Dos\n" +
                               "[WinDrv.WindowsClient]\r\nWindowedViewportX=8192\nWindowedViewportY=1024\r\n")
                return false;

            string routes = "[Engine.Engine]\r\nRenderDevice=D3DDrv.D3DRenderDevice\r\n" +
                            "AudioDevice=FMODAudio.FMODAudioSubsystem\r\n" +
                            "[D3DDrv.D3DRenderDevice]\r\nUseVSync=True\r\n" +
                            "[D3DDrv11.D3DRenderDevice11]\r\nUseVSync=True\r\n" +
                            "[ALAudio.ALAudioSubsystem]\r\nUse3DSound=True\r\n" +
                            "[Engine.RenderConfig]\r\nHorizontalFOVLock=True;\r\nUseFxaa=0\r\n";
            List<IniEntry> routeEntries = Parse(routes);
            if (routeEntries.Count != 7 ||
                routeEntries[2].Impact != IniImpact.Performance || routeEntries[2].Protected ||
                routeEntries[3].Impact != IniImpact.Warning || !routeEntries[3].Protected ||
                routeEntries[4].Impact != IniImpact.Warning || !routeEntries[4].Protected ||
                routeEntries[5].Impact != IniImpact.VrDirect || !routeEntries[5].Protected ||
                routeEntries[6].Impact != IniImpact.Performance || routeEntries[6].Protected)
                return false;
            routeEntries[6].Value = "1";
            string fxaaOutput = Render(routes, routeEntries);
            return fxaaOutput.Contains("[Engine.RenderConfig]\r\nHorizontalFOVLock=True;\r\nUseFxaa=1\r\n") &&
                   fxaaOutput.Replace("UseFxaa=1", "UseFxaa=0") == routes;
        }
    }

    internal static class IniKnowledge
    {
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Descriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static IniKnowledge()
        {
            Add("WindowedViewportX", "Horizontal resolution", "Backbuffer width for windowed startup. The mod uses this surface to construct the VR image.");
            Add("WindowedViewportY", "Vertical resolution", "Backbuffer height for windowed startup. This mod recommends keeping it equal to width.");
            Add("FullscreenViewportX", "Fullscreen horizontal resolution", "Width used for fullscreen startup. The launcher keeps it equal to the windowed resolution to avoid unexpected changes.");
            Add("FullscreenViewportY", "Fullscreen vertical resolution", "Height used for fullscreen startup. The launcher keeps it equal to the windowed resolution.");
            Add("MenuViewportX", "Internal game menu width", "Logical resolution of the original 2D game menus. Does not control the mod's F10 menu size.");
            Add("MenuViewportY", "Internal game menu height", "Logical resolution of the original 2D game menus. Does not control the mod's F10 menu position.");
            Add("StartupFullscreen", "Start fullscreen", "Choose whether the game starts fullscreen. Windowed mode (False) is the tested VR path; exclusive mode may prevent the requested resolution from applying correctly.");
            Add("Brightness", "Brightness", "Image brightness level. Different sections use different scales; change only the section actually used by the game.");
            Add("Contrast", "Contrast", "Image contrast level. May affect readability in the headset.");
            Add("Gamma", "Gamma", "Image brightness curve. Large changes may hide shadow detail or blow out highlights in the headset.");
            Add("UseVSync", "Vertical sync", "Synchronize presentation with the configured refresh rate. May add VR latency or interfere with runtime pacing; effects depend on the active graphics path.");
            Add("VSync", "User vertical sync", "VSync preference saved by the game. May affect VR latency and frame pacing.");
            Add("DesiredRefreshRate", "Requested refresh rate", "Refresh rate requested by the desktop renderer. Does not directly change OpenXR headset refresh rate.");
            Add("MinDesiredFrameRate", "Desired minimum frame rate", "Internal target used for engine performance decisions. Not the headset refresh limit.");
            Add("UseMultithreadedRendering", "Multithreaded rendering", "Allow the renderer to distribute work across threads. May change performance and VR frame pacing.");
            Add("UseMultithreading", "Engine multithreading", "Enable general engine multithreading. May affect performance and frame pacing stability.");
            Add("ReduceMouseLag", "Reduce mouse latency", "Special engine path to reduce input latency. Usually unnecessary with VR controllers and may affect performance.");
            Add("Sync Mouse To Framerate", "Sync mouse to frames", "Tie mouse input to rendering cadence. Usually does not help motion controllers.");
            Add("HorizontalFOV", "Horizontal field of view", "Horizontal FOV saved by the game. The custom VR DLL replaces it with full headset FOV during play.");
            Add("bHorizontalFOVLock", "Lock horizontal FOV", "Lock saved by the game. Disabling it did not remove cropping in tests; leave unchanged because the VR DLL corrects FOV live.");
            Add("HorizontalFOVLock", "Horizontal FOV lock", "Renderer field-of-view policy. Leave unchanged in this installation and let the VR DLL apply headset FOV.");
            Add("ControlSensitivity", "Controller sensitivity", "Overall sensitivity selected in game options.");
            Add("Sensitivity", "Sensitivity", "Sensitivity value stored in the game profile.");
            Add("MouseSensitivity", "Mouse sensitivity", "Mouse sensitivity. Does not directly control VR controller turn speed, which is set in vrpreset.ini.");
            Add("MouseAcceleration", "Mouse acceleration", "Make turning also depend on mouse movement speed.");
            Add("MouseSmoothing", "Mouse smoothing", "Filter mouse input. This is not VR controller turn smoothing.");
            Add("UseController", "Use gamepad", "Enable the client's gamepad input path. The mod generates gamepad input from VR controllers.");
            Add("UseJoystick", "Use joystick", "Enable Windows client joystick support.");
            Add("CaptureMouse", "Capture mouse", "Keep the cursor captured in the game window. May affect mouse use in the F10 menu.");
            Add("AutoAim", "Automatic aim assist", "Game aim assist. The mod also provides lockOnDisabled to prevent magnetic aim in VR.");
            Add("WantsXboxController", "Prefer Xbox controller", "Use the game's controller interface and input. Matches the VR mod's controller emulation.");
            Add("InvertYAxis", "Invert vertical axis", "Invert the vertical axis of traditional input.");
            Add("Vibration", "Controller vibration", "Enable game-requested vibration; mapping to VR controllers depends on the mod/runtime.");
            Add("bMaintainUIScale", "Keep interface scale", "Try to preserve original HUD scale when resolution changes. Does not control the OpenXR HUD panel or the mod's F10 menu.");
            Add("MouseIconScale", "Pointer size", "Mouse pointer scale in the game interface.");
            Add("ScreenFlashes", "Screen flashes", "Allow damage flashes and other effects. May be intense or uncomfortable in VR.");
            Add("Decals", "Decals", "Enable marks such as impacts and stains. Mainly affects quality and performance.");
            Add("NoDynamicLights", "Disable dynamic lights", "Remove dynamic lights to improve performance at the cost of visual quality.");
            Add("NoLighting", "Disable lighting", "Disable engine lighting. A diagnostic setting, not recommended during play.");
            Add("LevelOfAnisotropy", "Anisotropic filtering", "Improve texture sharpness at oblique angles. Higher values use somewhat more GPU power.");
            Add("UseTrilinear", "Trilinear filtering", "Smooth transitions between texture detail levels.");
            Add("UsePrecaching", "Resource preloading", "Preload data to reduce later stutter, using more memory and increasing initial loading.");
            Add("HighDetailActors", "High-detail characters", "Use detailed actors and characters.");
            Add("SuperHighDetailActors", "Maximum-detail characters", "Enable the highest actor detail level.");
            Add("UseHighDetailShadowMaps", "High-quality shadows", "Use higher-detail shadow maps at greater GPU cost.");
            Add("Shadows", "Shadows", "Enable engine shadows. May have a noticeable VR impact.");
            Add("RealTimeReflection", "Real-time reflections", "Enable dynamic reflections; improves the image and increases graphics load.");
            Add("PostProcessing", "Post-processing", "Enable effects applied after rendering. Some may be uncomfortable or costly in VR.");
            Add("UseDistortion", "Game graphics distortion", "Enable engine visual distortion. This is not headset optical correction, which is handled by the VR runtime.");
            Add("UseFxaa", "Antialiasing FXAA", "Post-process edge smoothing. Lightweight, but may reduce headset image sharpness.");
            Add("UseSoftwareAntiAliasing", "Software antialiasing", "Additional engine edge smoothing. May affect sharpness and performance.");
            Add("HardwareOcclusion", "Hardware occlusion", "Avoid drawing hidden geometry. Usually improves performance.");
            Add("TextureDetail", "Overall texture detail", "Overall texture quality. Higher values use more video memory.");
            Add("FluidSurfaceDetail", "Fluid detail", "Quality of water and other fluid surfaces.");
            Add("DynamicShadowDetail", "Dynamic shadow detail", "Quality of shadows that change in real time.");
            Add("RenderDetail", "Rendering detail", "Overall renderer detail level.");
            Add("UseHighDetailPostProcEffects", "High-quality post-processing", "Use higher-detail post-processing effects.");
            Add("UseLinearSpace", "Linear color space", "Perform certain lighting and blending operations in linear space. May substantially change the image.");
            Add("OverrideDesktopRefreshRate", "Override desktop refresh rate", "Allow the game to override monitor refresh rate. Does not control headset refresh rate.");
            Add("AvoidHitches", "Avoid stutter", "Enable a renderer strategy to reduce stalls; may change memory usage or preloading.");
            Add("SpeakerMode", "Speaker configuration", "Game audio channel layout. Stereo or the runtime's recommended setting usually suits VR headphones.");
            Add("Use3DSound", "3D positional audio", "Enable the engine's 3D sound path. May affect spatial orientation in VR.");
            Add("ReverseStereo", "Swap stereo channels", "Swap left and right. Leave off unless the channels are actually reversed.");
            Add("MasterVolume", "Master volume", "Master game volume.");
            Add("SFXVolume", "Effects volume", "Sound effect volume.");
            Add("MusicVolume", "Music volume", "Music volume.");
            Add("VoVolume", "Voice volume", "Dialogue and voice volume.");
            Add("DialogSubtitles", "Dialogue subtitles", "Show conversation subtitles.");
            Add("ArtSubtitles", "Art subtitles", "Show additional text or subtitles associated with game content.");
            Add("language", "Language", "Language code or entry used by the corresponding section.");
            Add("Coronas", "Light halos", "Enable halos around certain light sources. May add graphics load and appear intense in VR.");
            Add("DecoLayers", "Decorative layers", "Enable decorative world layers. Disabling them may reduce detail and some graphics load.");
            Add("Projectors", "Visual projectors", "Enable effects projected onto surfaces, such as lights or marks. May affect quality and performance.");
            Add("ReportDynamicUploads", "Log dynamic uploads", "Renderer diagnostic option reporting dynamically uploaded resources; usually does not improve VR.");
            Add("TextureDetailInterface", "Interface texture detail", "Original game interface texture quality. Does not change the mod's F10 menu size.");
            Add("TextureDetailTerrain", "Terrain texture detail", "Quality of terrain and scene surface textures.");
            Add("TextureDetailWeaponSkin", "Weapon texture detail", "Weapon model texture quality, especially visible up close in VR.");
            Add("TextureDetailPlayerSkin", "Player texture detail", "Quality of textures on the player model.");
            Add("TextureDetailWorld", "World texture detail", "Quality of general scene textures; higher values use more video memory.");
            Add("TextureDetailRenderMap", "Rendered texture detail", "Quality of surfaces receiving game-generated images.");
            Add("TextureDetailLightmap", "Lightmap detail", "Quality of precomputed scene lighting textures.");
            Add("NoFractalAnim", "Disable fractal animation", "Disable certain procedural animations. May reduce visual motion and some graphics load.");
            Add("ScaleHUDX", "Original HUD horizontal scale", "Horizontal adjustment of the game's 2D HUD. Does not control the OpenXR panel or the mod's F10 menu position.");
            Add("MouseXMultiplier", "Horizontal mouse multiplier", "Scale horizontal mouse movement; this is not the turn speed configured in vrpreset.ini.");
            Add("MouseYMultiplier", "Vertical mouse multiplier", "Scale vertical mouse movement; does not directly control VR controllers.");
            Add("WindowedViewportXPos", "Window horizontal position", "Horizontal desktop window position. Does not move the image inside the headset.");
            Add("WindowedViewportYPos", "Window vertical position", "Vertical desktop window position. Does not move the image inside the headset.");
            Add("WindowedViewportXPosEditor", "Editor window X position", "Internal position reserved for engine tools; does not affect normal gameplay or VR.");
            Add("WindowedViewportYPosEditor", "Editor window Y position", "Internal position reserved for engine tools; does not affect normal gameplay or VR.");
            Add("MaxChannels", "Maximum audio channels", "Maximum simultaneous sounds. Lower values may cut sounds off; higher values use more resources.");
            Add("MaxStreams", "Maximum audio streams", "Maximum audio tracks streamed from disk simultaneously.");
            Add("StreamBufferSize", "Streaming audio buffer", "Buffer size for streamed audio. May affect dropouts, latency and memory.");
            Add("AdapterNumber", "Graphics adapter", "GPU index for this rendering path. -1 lets the engine choose; an incorrect index may prevent startup.");
            Add("TesselationFactor", "Tessellation factor", "Internal geometry detail factor for this rendering path. Actual effect depends on the active renderer.");
            Add("CheckForOverflow", "Check for overflows", "Internal renderer diagnostic check; may affect performance and is normally disabled during play.");
            Add("BatchRenderFlash", "Batch Flash rendering", "Batch drawing of the game's Flash interface. Changing it may affect menus and the HUD.");
            Add("DetailTextures", "Detail textures", "Add fine texture layers to nearby surfaces; improves detail at some GPU and memory cost.");
            Add("HDRSceneExpBias", "HDR exposure compensation", "Adjust base HDR scene exposure. Large changes may reduce visibility and VR comfort.");
            Add("MaxSkeletalProjectorsPerActor", "Projectors per character", "Limit projected effects per animated actor. Higher values may increase graphics load.");
            Add("StreamingDistanceScale", "Resource loading distance", "Scale visual resource loading distance. May affect sharpness, memory and VR stutter.");
            Add("HighDetailShaders", "High-detail shaders", "Enable higher-quality shaders at greater GPU cost.");
            Add("UseRippleSystem", "Water ripples", "Enable ripples and disturbances on water surfaces.");
            Add("UseHighDetailSoftParticles", "High-quality soft particles", "Use particles with softer intersections against geometry; improves quality and increases graphics load.");
            Add("UseSpecCubeMap", "Specular cubemap reflections", "Enable approximate cubemap reflections. May improve metallic and wet materials.");
            Add("ForceGlobalLighting", "Force global lighting", "Force a global engine lighting path. An advanced setting that can substantially change the image.");
            Add("CascadingWaterSimulationVelocity", "Water simulation speed", "Control the internal speed of certain cascading water effects.");
            Add("MovementStick", "Movement stick", "Select the traditional gamepad stick used for movement. The mod maps VR controls to this input.");
            Add("AutoCenter", "Auto-centering", "Make traditional view or input tend to recenter. May feel artificial in VR.");
            Add("bReverb", "Reverb", "Enable environmental reverb for a stronger sense of room space.");
            Add("bEAXEnabled", "EAX audio effects", "Enable legacy EAX effects if available; normally unnecessary with modern headset audio.");
            Add("GameDifficulty", "Difficulty", "Difficulty level saved by the game.");
            Add("AdaptiveTraining", "Adaptive training", "Allow the game to adapt certain tips or assistance to player progress.");
            Add("NoVitaChamber", "Disable Vita-Chambers", "Prevent respawning through Vita-Chambers when enabled.");
            Add("QuestArrow", "Objective arrow", "Show the directional objective guide in the game interface.");
            Add("bShowShimmer", "Highlight interactive objects", "Show a glow on certain objects; may make them easier to find in the headset.");
            Add("bHighlightFocussedItems", "Highlight focused items", "Highlight objects the player aims at or focuses on.");
            Add("UseGamePlusData", "Use New Game+ data", "Allow the game to reuse compatible data from a completed playthrough.");
        }

        private static void Add(string key, string name, string description)
        {
            Names[key] = name;
            Descriptions[key] = description;
        }

        public static void Annotate(IniEntry entry)
        {
            string name;
            if (!Names.TryGetValue(entry.Key, out name))
                name = Humanize(entry.Key);
            entry.FriendlyName = name;

            string section = entry.Section ?? string.Empty;
            string key = entry.Key ?? string.Empty;
            string lowerSection = section.ToLowerInvariant();
            string lowerKey = key.ToLowerInvariant();
            bool consoleSection = lowerSection.StartsWith("xedrv.") ||
                                  lowerSection.StartsWith("durangodrv.") ||
                                  lowerSection.StartsWith("orbisdrv.") ||
                                  lowerSection.StartsWith("gnmdrv.") ||
                                  lowerSection.Contains("xenon");
            bool pcResolution = section == "WinDrv.WindowsClient" &&
                (key == "WindowedViewportX" || key == "WindowedViewportY" ||
                 key == "FullscreenViewportX" || key == "FullscreenViewportY");
            bool fovLock = ((section == "ShockGame.ShockUserSettings" || section == "Engine.RenderConfig") &&
                            lowerKey.Contains("horizontalfovlock"));
            bool directVr = pcResolution ||
                (section == "WinDrv.WindowsClient" && key == "StartupFullscreen") ||
                (section == "ShockGame.ShockUserSettings" && key == "HorizontalFOV") ||
                fovLock;
            bool performance = lowerSection.StartsWith("d3ddrv") ||
                               lowerSection.StartsWith("fmodaudio.") ||
                               section == "Engine.RenderConfig" ||
                               (section == "WinDrv.WindowsClient" &&
                                 (lowerKey.Contains("menuviewport") || lowerKey.Contains("capturemouse") ||
                                  lowerKey.Contains("screenflash") || lowerKey.Contains("texture") ||
                                  lowerKey.Contains("lighting") || lowerKey.Contains("decal") ||
                                  lowerKey.Contains("decolayer") ||
                                 lowerKey.Contains("projector") || lowerKey.Contains("corona") ||
                                 lowerKey.Contains("hud"))) ||
                               lowerKey.Contains("vsync") || lowerKey.Contains("refresh") ||
                               lowerKey.Contains("shadow") || lowerKey.Contains("texture") ||
                               lowerKey.Contains("render") || lowerKey.Contains("detail") ||
                               lowerKey.Contains("anisotrop") || lowerKey.Contains("precache") ||
                               lowerKey.Contains("multithread") || lowerKey.Contains("postproc") ||
                               lowerKey.Contains("reflection") || lowerKey.Contains("occlusion") ||
                               lowerKey.Contains("antialias") || lowerKey.Contains("fxaa") ||
                               lowerKey.Contains("uiscale") || lowerKey.Contains("crosshair") ||
                               lowerKey.Contains("autoaim") || lowerKey.Contains("mousesmooth") ||
                               lowerKey.Contains("mouseaccel") || lowerKey.Contains("controller") ||
                               lowerKey.Contains("joystick") || lowerKey.Contains("sensitivity") ||
                               lowerKey.Contains("movementstick") || lowerKey.Contains("autocenter") ||
                               lowerKey.Contains("questarrow") || lowerKey.Contains("shimmer") ||
                               lowerKey.Contains("highlight") || lowerKey.Contains("reverb") ||
                               lowerKey.Contains("eax");
            bool internalRisk = lowerSection.StartsWith("engine.engine") ||
                                 lowerSection.StartsWith("core.system") ||
                                 lowerSection == "startup" || lowerSection == "perobjectconfig" ||
                                 lowerSection == "url" || lowerSection == "savegame" ||
                                 lowerSection == "engine.gameengine" || lowerSection == "havok" ||
                                 lowerSection == "firstrun" || lowerSection == "testtrack" ||
                                 lowerSection == "fogbugz" || lowerSection == "statnotifyproviders" ||
                                 lowerSection == "bink" || lowerSection == "performancelimits" ||
                                 lowerSection == "pcperformancelimits" ||
                                 lowerSection == "engine.player" ||
                                 lowerSection == "engine.demorecdriver" ||
                                 lowerSection == "engine.gamereplicationinfo" ||
                                 lowerSection == "engine.gameinfo" ||
                                 lowerSection == "engine.levelinfo" ||
                                 lowerSection == "engine.console" ||
                                 lowerSection == "windowpositions" ||
                                 lowerSection.StartsWith("umenu.") ||
                                 lowerSection.StartsWith("ipdrv.") ||
                                 lowerSection.StartsWith("ipserver.") ||
                                 lowerSection.StartsWith("editor.") ||
                                 lowerSection == "compatibility" || lowerSection == "skipcalls" ||
                                lowerKey.Contains("driver") || lowerKey.StartsWith("serverpackage") ||
                                lowerKey.StartsWith("serveractor") || lowerKey == "paths" ||
                                (section == "Engine.RenderConfig" &&
                                 (key == "UseMultithreadedRendering" ||
                                  key == "HorizontalFOVLockPS4" || key == "HorizontalFOVLockXB1" ||
                                  lowerKey.StartsWith("streaming") ||
                                  key == "DisableExtraAntiPortalClip")) ||
                                (lowerSection.StartsWith("d3ddrv") &&
                                 (key == "AdapterNumber" || key == "BatchRenderFlash" ||
                                  key == "UseLinearSpace" || key == "Use8bitBackBuffer" ||
                                  key == "Use8bitOpacity" || key == "UseShaderConstantChecking"));
            bool savedGameState = section == "ShockGame.ShockUserSettings" &&
                                  (lowerKey.StartsWith("dlc") || lowerKey.Contains("completion") ||
                                   lowerKey.Contains("besttime") || lowerKey.Contains("developerfilm") ||
                                   lowerKey.StartsWith("has") || lowerKey.StartsWith("need") ||
                                   lowerKey.StartsWith("bhas") || lowerKey.StartsWith("bplayed") ||
                                   lowerKey.StartsWith("bviewed") || lowerKey.Contains("prom") ||
                                   lowerKey.Contains("sso") || lowerKey.Contains("profile") ||
                                    lowerKey.Contains("legal") || lowerKey.Contains("bootopt"));
            savedGameState = savedGameState || (section == "ShockGame.ShockUserSettings" &&
                                  (lowerKey.Contains("gameplus") || lowerKey == "settingversion" ||
                                   lowerKey.Contains("gamewasfinished")));

            if (consoleSection || internalRisk || savedGameState)
                entry.Impact = IniImpact.Warning;
            else if (directVr)
                entry.Impact = IniImpact.VrDirect;
            else if (performance)
                entry.Impact = IniImpact.Performance;
            else
                entry.Impact = IniImpact.Normal;
            entry.Protected = consoleSection || internalRisk || savedGameState || fovLock;

            string description;
            if (!Descriptions.TryGetValue(entry.Key, out description))
            {
                if (entry.Impact == IniImpact.Warning)
                    description = "Internal engine setting or setting for another platform. No safe VR interpretation; change only if you know exactly what Unreal Engine expects.";
                else if (entry.Impact == IniImpact.Performance)
                    description = "Engine graphics or performance setting. May affect quality, GPU/CPU usage or VR frame pacing; test one change at a time.";
                else if (entry.Impact == IniImpact.VrDirect)
                    description = "Image, input or interface setting that may be noticeable in the headset. Keep a backup and check the result carefully.";
                else if (IsBoolean(entry.Value))
                    description = "Enable or disable this game behavior. A general option with no identified direct VR effect.";
                else
                    description = "General game setting. No direct VR effect identified; shown to provide access to the complete Bioshock2SP.ini.";
            }
            if (consoleSection)
                description = "Console copy of '" + entry.FriendlyName +
                    "'. This section does not affect the Windows version or installed VR mod; shown only for a complete Bioshock2SP.ini view.";
            entry.Description = description;
        }

        public static void ApplyDocumentContext(IList<IniEntry> entries)
        {
            string activeRenderer = null;
            string activeAudio = null;
            foreach (IniEntry entry in entries)
            {
                if (!string.Equals(entry.Section, "Engine.Engine", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(entry.Key, "RenderDevice", StringComparison.OrdinalIgnoreCase))
                    activeRenderer = entry.Value;
                else if (string.Equals(entry.Key, "AudioDevice", StringComparison.OrdinalIgnoreCase))
                    activeAudio = entry.Value;
            }
            foreach (IniEntry entry in entries)
            {
                bool alternativeRenderer = entry.Section.StartsWith("D3DDrv", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(activeRenderer) &&
                    !string.Equals(entry.Section, activeRenderer, StringComparison.OrdinalIgnoreCase);
                bool audioRoute = entry.Section.StartsWith("ALAudio.", StringComparison.OrdinalIgnoreCase) ||
                                  entry.Section.StartsWith("FMODAudio.", StringComparison.OrdinalIgnoreCase);
                bool alternativeAudio = audioRoute && !string.IsNullOrEmpty(activeAudio) &&
                    !string.Equals(entry.Section, activeAudio, StringComparison.OrdinalIgnoreCase);
                if (!alternativeRenderer && !alternativeAudio) continue;
                entry.Impact = IniImpact.Warning;
                entry.Protected = true;
                string selected = alternativeRenderer ? activeRenderer : activeAudio;
                entry.Description = "Setting for an alternative path not selected by this Bioshock2SP.ini. " +
                    "The active path is [" + selected + "]; changing this value normally will not affect the game or VR. " +
                    "Kept protected to avoid confusing an inactive copy with the effective setting.";
            }
        }

        public static string ImpactText(IniImpact impact)
        {
            if (impact == IniImpact.VrDirect) return "DIRECT VR";
            if (impact == IniImpact.Performance) return "VR REL.";
            if (impact == IniImpact.Warning) return "WARNING";
            return "GENERAL";
        }

        private static bool IsBoolean(string value)
        {
            return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "False", StringComparison.OrdinalIgnoreCase);
        }

        private static string Humanize(string key)
        {
            if (string.IsNullOrEmpty(key)) return "Unnamed setting";
            string result = key.Replace('_', ' ');
            result = Regex.Replace(result, "([a-z0-9])([A-Z])", "$1 $2");
            result = Regex.Replace(result, "\\s+", " ").Trim();
            return "Setting: " + result;
        }
    }

    internal sealed partial class MainForm : Form
    {
        private const bool FinalDlssEdition = true;
        // Mismo catálogo de salida cuadrada que F1/F2/F3 en el mod.
        private static readonly int[] SquareResolutionSteps = new int[] {
            1024, 1280, 1536, 1792, 2048, 2304, 2560, 2816,
            3072, 3328, 3584, 3840, 4096, 4352, 4608, 4864,
            5120, 5376, 5632, 5888, 6144, 6400, 6656, 6912,
            7168, 7424, 7680, 7936, 8192
        };
        private static readonly Color Navy = SystemColors.ControlText;
        private static readonly Color Blue = SystemColors.HotTrack;
        private static readonly Color PaleBlue = SystemColors.Control;
        private static readonly Color TextDark = SystemColors.ControlText;
        private static readonly Color Muted = SystemColors.GrayText;
        private static readonly Color Success = Color.FromArgb(25, 122, 76);
        private static readonly Color Warning = Color.FromArgb(176, 101, 0);

        private readonly string _configPath;
        private readonly string _upscalerConfigPath;
        private readonly string _dlssConfigPath;
        private readonly string _gameIniPath;
        private readonly string _sharedIniPath;
        private readonly string _weaponsPath;
        private string _weaponsLoadedContent;
        private string _sharedIniLoadedContent;
        private Encoding _sharedIniEncoding;
        private List<IniEntry> _sharedIniEntries = new List<IniEntry>();
        private Timer _launchTimer;
        private bool _launchPending;
        private DateTime _launchRequestedUtc;
        private readonly HashSet<int> _launchPreviousIds = new HashSet<int>();
        private GameLaunchTracker _launchTracker;
        private TextBox _diagnostics;
        private readonly List<ParamDef> _definitions;
        private readonly Dictionary<string, Control> _editors;
        private readonly Dictionary<string, string> _originalEditorValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _initialEditorValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ToolTip _toolTip;
        private string _loadedContent;
        private DateTime _loadedWriteTimeUtc;
        private string _upscalerLoadedContent;
        private string _upscalerLoadWarning;
        private string _dlssLoadedContent;
        private string _dlssLoadWarning;
        private string _dlssNormalizationNote;
        private DlssBackendStatus _dlssBackendStatus;
        private string _gameIniLoadedContent;
        private Encoding _gameIniEncoding;
        private List<IniEntry> _gameIniEntries;
        private bool _loading;
        private bool _dirty;
        private bool _vrDirty;
        private bool _upscalerDirty;
        private bool _dlssDirty;
        private bool _gameIniDirty;
        private bool _rebuildingIniGrid;
        private bool _syncingResolution;
        private bool _syncingFxaa;
        private bool _syncingUpscaler;
        private bool _syncingDlss;
        private int _srScaleNumerator = 2;
        private int _srScaleDenominator = 3;
        private int _previousDlssModeIndex;
        private int _previousDlssOutputWidth = 2048;
        private int _previousDlssOutputHeight = 2048;
        private string _gameExePath;

        private Label _configStateLabel;
        private Label _gameStateLabel;
        private Label _statusLabel;
        private Button _saveButton;
        private Button _launchButton;
        private TabControl _tabs;
        private NumericUpDown _resolutionWidth;
        private NumericUpDown _resolutionHeight;
        private CheckBox _squareResolution;
        private ComboBox _resolutionPreset;
        private Label _resolutionLoadLabel;
        private CheckBox _fxaaEnabled;
        private Label _fxaaStateLabel;
        private CheckBox _upscalerEnabled;
        private NumericUpDown _upscalerOutputWidth;
        private NumericUpDown _upscalerOutputHeight;
        private NumericUpDown _upscalerSharpness;
        private Label _upscalerStateLabel;
        private ComboBox _dlssMode;
        private TextBox _dlssRuntime;
        private ComboBox _dlssPreset;
        private ComboBox _dlssQuality;
        private NumericUpDown _dlssOutputWidth;
        private NumericUpDown _dlssOutputHeight;
        private ComboBox _dlssOutputPreset;
        private NumericUpDown _dlssNearPlane;
        private NumericUpDown _dlssSharpness;
        private Label _dlssBackendStateLabel;
        private Label _dlssSettingsStateLabel;
        private bool _runtimeWarningShown;
        private DataGridView _iniGrid;
        private ComboBox _iniSectionFilter;
        private ComboBox _iniImpactFilter;
        private TextBox _iniSearch;
        private Label _iniDetail;
        private Label _iniCountLabel;
        private CheckBox _allowRiskyEdits;

        public MainForm() : this(false)
        {
        }

        private MainForm(bool selfTest)
        {
            string isolated = selfTest ? Path.Combine(Path.GetTempPath(),
                "BioShockLauncherSelfTest-" + Guid.NewGuid().ToString("N")) : Program.SandboxRoot;
            string local = GameProfile.GetLocalDirectory(isolated);
            string game = GameProfile.GetGameIniDirectory(isolated);
            _configPath = Path.Combine(local, "vrpreset.ini");
            _upscalerConfigPath = Path.Combine(local, "upscaler.ini");
            _dlssConfigPath = Path.Combine(local, "dlss.ini");
            _weaponsPath = Path.Combine(local, "weapons.ini");
            _gameIniPath = Path.Combine(game, GameProfile.IniName);
            _sharedIniPath = Path.Combine(game, "Shared.ini");
            _definitions = BuildDefinitions();
            _editors = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
            _gameIniEntries = new List<IniEntry>();
            _toolTip = new ToolTip();
            _toolTip.AutoPopDelay = 12000;
            _toolTip.InitialDelay = 350;
            _toolTip.ReshowDelay = 100;

            Text = GameProfile.DisplayName + " VR · DLSS/DLAA 0.2.17 · English";
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(696, 479);
            ClientSize = new Size(680, 440);
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;

            _loading = true;
            BuildInterface();
            if (selfTest)
            {
                _dlssBackendStatus = new DlssBackendStatus();
                _dlssBackendStatus.Summary = "Self-test without user files";
                return;
            }
            _gameExePath = !string.IsNullOrEmpty(Program.InitialGamePath) &&
                           File.Exists(Program.InitialGamePath)
                ? Path.GetFullPath(Program.InitialGamePath)
                : FindGameExecutable();
            UpdatePathStatus();
            LoadConfiguration(false);
            Shown += delegate { WarnAboutUntestedRuntime(); };
        }

        internal static bool ImageControlsSelfTest()
        {
            using (MainForm form = new MainForm(true))
            {
                form._gameIniEntries = GameIniDocument.Parse(
                    "[WinDrv.WindowsClient]\r\nWindowedViewportX=2730\r\n" +
                    "WindowedViewportY=2730\r\nFullscreenViewportX=2730\r\n" +
                    "FullscreenViewportY=2730\r\n");
                form._sharedIniEntries = GameIniDocument.Parse(
                    "[SharedOptions]\r\nViewportX=2730\r\nViewportY=2730\r\nStartupFullscreen=False\r\n");
                form._dlssMode.SelectedIndex = 2;
                form._previousDlssModeIndex = 2;
                form._dlssPreset.SelectedIndex = 0;
                form._dlssOutputWidth.Value = form._dlssOutputHeight.Value = 4096;
                form._previousDlssOutputWidth = form._previousDlssOutputHeight = 4096;
                form._dlssQuality.SelectedIndex = (int)DlssQuality.Quality;
                form.LoadResolutionControls();
                form._loading = false;

                form._dlssMode.SelectedIndex = 1;
                if (form._resolutionWidth.Value != 4096 || form._srScaleNumerator != 2 ||
                    form._srScaleDenominator != 3) return false;
                form._dlssMode.SelectedIndex = 0;
                if (form._resolutionWidth.Value != 4096 || form._dlssOutputWidth.Value != 4096)
                    return false;
                form._dlssMode.SelectedIndex = 2;
                if (form._resolutionWidth.Value != 2730 ||
                    form._dlssQuality.SelectedIndex != (int)DlssQuality.Quality) return false;

                form._dlssQuality.SelectedIndex = (int)DlssQuality.Percent70;
                if (form._resolutionWidth.Value != 2868 || form._srScaleNumerator != 7 ||
                    form._srScaleDenominator != 10) return false;
                form._dlssOutputPreset.SelectedIndex = Array.IndexOf(SquareResolutionSteps, 3840) + 1;
                if (form._resolutionWidth.Value != 2688 || form._resolutionHeight.Value != 2688 ||
                    form._dlssOutputWidth.Value != 3840) return false;
                form._dlssMode.SelectedIndex = 1;
                if (form._resolutionWidth.Value != 3840 ||
                    form._dlssQuality.SelectedIndex != (int)DlssQuality.Percent70) return false;
                form._dlssMode.SelectedIndex = 2;
                if (form._resolutionWidth.Value != 2688) return false;

                form._resolutionWidth.Value = 2800;
                if (form._dlssQuality.SelectedIndex != (int)DlssQuality.Custom ||
                    form._srScaleNumerator != 2800 || form._srScaleDenominator != 3840) return false;
                form._dlssSharpness.Value = 35;
                if (form._resolutionWidth.Value != 2800 || form._dlssOutputWidth.Value != 3840 ||
                    form._srScaleNumerator != 2800 || form._srScaleDenominator != 3840 ||
                    form.CollectDlssSettings().SharpnessPercent != 35 || !form._dlssSharpness.Enabled)
                    return false;
                form._dlssMode.SelectedIndex = 0;
                if (form._dlssSharpness.Enabled || form._dlssSharpness.Value != 35) return false;
                form._dlssMode.SelectedIndex = 1;
                if (form._dlssSharpness.Enabled || form._dlssSharpness.Value != 35) return false;
                form._dlssMode.SelectedIndex = 2;
                string problem;
                return form._resolutionWidth.Value == 2800 && form._resolutionHeight.Value == 2800 &&
                    form.TryValidateDlssSettings(form.CollectDlssSettings(), out problem) &&
                    form.FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportX").Value == "2800";
            }
        }

        private static List<ParamDef> BuildDefinitions()
        {
            List<ParamDef> p = new List<ParamDef>();

            const string camera = "Camera and scale";
            p.Add(ParamDef.Number("worldScale", camera, "World scale",
                "Game units per real-world meter. Higher values make the world feel smaller; lower values make it feel larger. Also changes physical movement distance.",
                "UU/meter", 10, 200, 1, 1, "100.0"));
            p.Add(ParamDef.Number("headUpUu", camera, "Head height offset",
                "Move the viewpoint vertically. Positive raises you; negative lowers you, without changing world size.",
                "UU", -150, 150, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("headFwdUu", camera, "Head forward offset",
                "Move the viewpoint forward or backward relative to the body. Positive moves forward.",
                "UU", -80, 80, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("ipdMm", camera, "Virtual interpupillary distance (IPD)",
                "Distance between the two eye cameras. Normally matches headset IPD; an incorrect value changes perceived scale and may cause eye strain.",
                "mm", 55, 75, 0.5m, 1, "63.0"));
            p.Add(ParamDef.Number("gameFovDeg", camera, "Target game FOV (advanced)",
                "Horizontal angle the mod can write to the game. The custom DLL prioritizes the headset's full FOV, so this normally needs no adjustment.",
                "degrees", 75, 150, 1, 1, "130.0"));

            const string hands = "Hands and aiming";
            p.Add(ParamDef.Number("handScaleL", hands, "Left hand size",
                "Visual multiplier for the plasmid hand. 1.00 keeps the base size; 1.10 makes it 10 % larger.",
                "multiplier", 0.2m, 4.0m, 0.01m, 3, "1.000"));
            p.Add(ParamDef.Number("handScaleR", hands, "Right hand size",
                "Visual multiplier for the weapon hand. 1.00 keeps the base size; 1.10 makes it 10 % larger.",
                "multiplier", 0.2m, 4.0m, 0.01m, 3, "1.000"));
            p.Add(ParamDef.Number("wScale", hands, "Weapon size",
                "Uniform weapon model scale around the grip. Does not change hand or world size.",
                "multiplier", 0.3m, 2.5m, 0.01m, 3, "1.000"));
            p.Add(ParamDef.Number("aimTrimLPitch", hands, "Left aim pitch",
                "Adjust the left hand's firing direction up/down (plasmids). Does not move the hand model.",
                "degrees", -90, 90, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("aimTrimLYaw", hands, "Left aim yaw",
                "Adjust the left hand's firing direction left/right (plasmids).",
                "degrees", -90, 90, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("aimTrimRPitch", hands, "Right aim pitch",
                "Adjust the right hand's firing direction up/down (weapons). Does not move the weapon model.",
                "degrees", -90, 90, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("aimTrimRYaw", hands, "Right aim yaw",
                "Adjust the right hand's firing direction left/right (weapons).",
                "degrees", -90, 90, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosLFwd", hands, "Left origin: forward/back",
                "Move the plasmid shot origin along the hand's direction. Does not move the visible hand.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosLRight", hands, "Left origin: right",
                "Move the left shot origin sideways. Positive moves toward the hand's local right.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosLUp", hands, "Left origin: up",
                "Raise or lower the left shot origin relative to the hand. Positive moves up.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosRFwd", hands, "Right origin: forward/back",
                "Move the weapon's shot origin along the hand's direction. Does not move the visible weapon.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosRRight", hands, "Right origin: right",
                "Move the weapon's shot origin sideways. Positive moves toward the hand's local right.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosRUp", hands, "Right origin: up",
                "Raise or lower the weapon's shot origin relative to the hand. Positive moves up.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Boolean("laserOn", hands, "Aim laser",
                "Show a dotted line along the actual aim ray. Useful for calibration; can be left off during play.", false));
            p.Add(ParamDef.Boolean("aimDotOn", hands, "Mod aim dot",
                "Show a dot on the actual shot path, independent of the game's flat crosshair.", false));
            p.Add(ParamDef.Number("aimDotDistM", hands, "Aim dot distance",
                "Distance at which the mod's aim dot appears. Only visible when the dot is enabled.",
                "meters", 0.5m, 20, 0.1m, 2, "5.00"));
            p.Add(ParamDef.Number("aimDotSizeDeg", hands, "Aim dot size",
                "Angular dot diameter in degrees; maintains a similar apparent size at different distances.",
                "degrees", 0.1m, 3.0m, 0.05m, 2, "0.50"));

            const string movement = "Movement and turning";
            p.Add(ParamDef.Boolean("autoVr", movement, "Start VR automatically",
                "Apply the full VR configuration when the game starts. Recommended on when using this launcher.", true));
            p.Add(ParamDef.Number("bodyRate", movement, "Body turn smoothing",
                "Rate at which the body catches up with head orientation. 0 follows instantly; higher values follow progressively.",
                "per second", 0, 10, 0.05m, 2, "0.00"));
            p.Add(ParamDef.Number("bodyDeadzoneDeg", movement, "Body dead zone",
                "Head rotation allowed before the body starts following. Higher values reduce small corrections but may make the initial turn more abrupt.",
                "degrees", 0, 60, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Boolean("moveDirInstant", movement, "Immediate movement direction",
                "Make walking direction follow your head immediately during quick turns.", true));
            p.Add(ParamDef.Number("turnScale", movement, "Smooth turn speed",
                "Multiplier for stick turning. 1.00 is the base speed; lower is slower and higher is faster.",
                "multiplier", 0.1m, 4.0m, 0.05m, 2, "1.00"));
            p.Add(ParamDef.Boolean("snapTurn", movement, "Snap turning",
                "Replace continuous turning with fixed-angle steps. Off keeps smooth turning.", false));
            p.Add(ParamDef.Number("snapAngleDeg", movement, "Snap turn angle",
                "Degrees in each snap turn. Only applies when snap turning is enabled.",
                "degrees", 5, 180, 5, 0, "45"));

            const string swing = "Gesture attacks";
            p.Add(ParamDef.Boolean("swingOn", swing, "Attack by swinging the wrench",
                "Convert a fast right-hand motion into an attack press when holding the wrench.", true));
            p.Add(ParamDef.Number("swingThreshold", swing, "Gesture activation speed",
                "Minimum hand speed needed to attack. A higher value requires a stronger gesture and reduces accidental activation.",
                "m/s", 0.3m, 10, 0.05m, 2, "3.60"));
            p.Add(ParamDef.Number("swingRearm", swing, "Gesture reset speed",
                "The hand must drop below this speed before another strike is accepted. Must be below the gesture activation speed.",
                "m/s", 0.05m, 9, 0.05m, 2, "1.00"));
            p.Add(ParamDef.Number("swingCooldownMs", swing, "Delay between strikes",
                "Minimum time between gesture attacks. Increasing it prevents double strikes.",
                "ms", 0, 2000, 10, 0, "300"));
            p.Add(ParamDef.Number("swingPulseMs", swing, "Attack press duration",
                "How long the mod holds the simulated trigger. If too short, the game may not detect the attack.",
                "ms", 20, 500, 10, 0, "120"));
            p.Add(ParamDef.Number("swingDelayMs", swing, "Attack delay",
                "Delay between detecting a gesture and pressing attack. 0 responds immediately.",
                "ms", 0, 400, 10, 0, "0"));
            p.Add(ParamDef.Boolean("swingHeadRel", swing, "Measure gestures relative to head",
                "Subtract overall head movement when calculating hand speed, reducing accidental attacks while moving your body.", true));

            const string cinema = "Cutscenes and effects";
            p.Add(ParamDef.Boolean("cineBarsHidden", cinema, "Hide cutscene letterbox bars",
                "Hide letterbox bars; the underlying image stays complete, without cropping or stretching.", true));
            p.Add(ParamDef.Choice("cineDrive", cinema, "Cutscene behavior",
                "Choose VR camera control, full game-directed camera control, or head movement on top of the game camera.",
                new string[] {
                    "0 · VR camera",
                    "1 · Game camera",
                    "2 · Game + head"
                }, 1));
            p.Add(ParamDef.Boolean("cineSubsInFrame", cinema, "Subtitles inside the 3D image",
                "On embeds subtitles in each eye image, which may appear doubled. Off keeps them on the HUD panel, usually more readable.", false));
            p.Add(ParamDef.Boolean("effectsInFrame", cinema, "Full-view screen effects",
                "Place water, damage and flashes over the full view instead of the HUD panel. May also affect some health and EVE bar fills.", false));
            p.Add(ParamDef.Number("effectMaxVerts", cinema, "Effect vertex limit (advanced)",
                "Maximum vertices in an untextured draw treated as a screen effect. 8 is the tested value; change only for diagnostics.",
                "vertices", 3, 100, 1, 0, "8"));
            p.Add(ParamDef.Boolean("postFxRtOnly", cinema, "Filter effects by render source",
                "Keep blur effects, such as alcohol blur, in the view without mistaking normal HUD textures for effects. Recommended on.", true));

            const string hud = "HUD and aids";
            p.Add(ParamDef.Boolean("lockOnDisabled", hud, "Disable magnetic aim assist",
                "Prevent controller aim assist from pulling aim toward enemies. Usually feels more natural with motion controllers.", true));
            p.Add(ParamDef.Boolean("crosshairVisible", hud, "Show the game's flat crosshair",
                "Show the original 2D crosshair again. Independent of the mod's 3D aim dot.", false));
            p.Add(ParamDef.Number("hudQuadDistM", hud, "HUD panel distance",
                "Distance from the eyes to the floating panel. Farther away looks smaller unless width is also increased.",
                "meters", 0.5m, 3.0m, 0.05m, 2, "1.30"));
            p.Add(ParamDef.Number("hudQuadWidthM", hud, "HUD panel width",
                "Physical width of the floating panel. Increasing it makes the HUD larger in the headset.",
                "meters", 0.3m, 3.0m, 0.05m, 2, "1.25"));
            p.Add(ParamDef.Number("hudQuadUpM", hud, "HUD panel height",
                "Vertical panel offset. Positive raises it; negative lowers it.",
                "meters", -1.0m, 1.0m, 0.05m, 2, "-0.10"));

            return GameProfile.IsBioShock2 ? Bs2Profile.AdaptDefinitions(p) : p;
        }

        private void BuildInterface()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 42;
            header.BackColor = SystemColors.Control;
            header.Padding = new Padding(8, 3, 8, 3);
            Controls.Add(header);

            Label title = new Label();
            title.Text = GameProfile.DisplayName + " VR · DLSS/DLAA";
            title.ForeColor = SystemColors.ControlText;
            title.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(8, 2);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Original mod by Mohamad Balouza · DLSS/DLAA fork by Beren5556";
            subtitle.ForeColor = SystemColors.GrayText;
            subtitle.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(9, 23);
            header.Controls.Add(subtitle);

            _configStateLabel = new Label();
            _configStateLabel.ForeColor = SystemColors.ControlText;
            _configStateLabel.AutoEllipsis = true;
            _configStateLabel.TextAlign = ContentAlignment.MiddleLeft;
            _configStateLabel.SetBounds(440, 8, 505, 21);
            _configStateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(_configStateLabel);
            _configStateLabel.Visible = false;

            _gameStateLabel = new Label();
            _gameStateLabel.ForeColor = SystemColors.GrayText;
            _gameStateLabel.AutoEllipsis = true;
            _gameStateLabel.TextAlign = ContentAlignment.MiddleLeft;
            _gameStateLabel.SetBounds(440, 29, 505, 21);
            _gameStateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(_gameStateLabel);
            _gameStateLabel.Visible = false;
            header.MouseEnter += delegate {
                _toolTip.SetToolTip(header, _gameStateLabel.Text + Environment.NewLine + _configStateLabel.Text);
            };

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 55;
            footer.BackColor = SystemColors.Control;
            footer.BorderStyle = BorderStyle.FixedSingle;
            footer.Padding = new Padding(7, 3, 7, 4);
            Controls.Add(footer);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttons.SetBounds(375, 23, 295, 27);
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0);
            footer.Controls.Add(buttons);

            _launchButton = MakeButton("Save and launch", Blue, Color.White, 130);
            _launchButton.Click += delegate { SaveAndLaunch(); };
            buttons.Controls.Add(_launchButton);

            _saveButton = MakeButton("Save", SystemColors.Control, SystemColors.ControlText, 72);
            _saveButton.Click += delegate { SaveConfiguration(true); };
            buttons.Controls.Add(_saveButton);

            Button reload = MakeButton("Reload", SystemColors.Control, SystemColors.ControlText, 75);
            reload.Click += delegate { LoadConfiguration(true); };
            buttons.Controls.Add(reload);

            _statusLabel = new Label();
            _statusLabel.Text = "Preparing configuration…";
            _statusLabel.ForeColor = Muted;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.SetBounds(8, 2, 655, 18);
            _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            footer.Controls.Add(_statusLabel);
            ContextMenuStrip fileMenu = new ContextMenuStrip();
            fileMenu.Items.Add("Open vrpreset.ini", null, delegate { OpenConfigurationFile(); });
            fileMenu.Items.Add("Open " + GameProfile.IniName, null, delegate { OpenGameIniFile(); });
            fileMenu.Items.Add("View backups", null, delegate { OpenBackupFolder(); });
            fileMenu.Items.Add(new ToolStripSeparator());
            fileMenu.Items.Add("Credits and licenses", null, delegate { ShowCreditsAndLicenses(); });
            if (!FinalDlssEdition)
                fileMenu.Items.Add("Open upscaler.ini", null, delegate { OpenUpscalerConfigurationFile(); });
            Button files = MakeButton("Files and help ▾", SystemColors.Control, SystemColors.ControlText, 135);
            files.SetBounds(8, 23, 135, 26);
            files.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            files.ContextMenuStrip = fileMenu;
            files.Click += delegate { fileMenu.Show(files, new Point(0, files.Height)); };
            footer.Controls.Add(files);
            Disposed += delegate { fileMenu.Dispose(); };

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            _tabs.Padding = new Point(8, 3);
            Controls.Add(_tabs);
            _tabs.BringToFront();

            string[] categoryOrder = new string[] {
                "Camera and scale", "Hands and aiming", "Movement and turning",
                "Cutscenes and effects", "HUD and aids"
            };
            AddResolutionTab();
            foreach (string category in categoryOrder)
                AddCategoryTab(category);
            if (GameProfile.IsBioShock2) AddWeaponsTab();
            else AddCategoryTab("Gesture attacks");

            InitializeHiddenIniEditor();

            KeyPreview = true;
            KeyDown += MainForm_KeyDown;
            FormClosing += MainForm_FormClosing;
            FormClosed += delegate { StopLaunchMonitor(); };
        }

        private Button MakeButton(string text, Color backColor, Color foreColor, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 26;
            button.FlatStyle = FlatStyle.System;
            button.UseVisualStyleBackColor = true;
            button.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            button.Margin = new Padding(6, 0, 0, 0);
            return button;
        }

        private void AddCategoryTab(string category)
        {
            TabPage page = new TabPage(ShortCategoryName(category));
            page.Name = category;
            page.UseVisualStyleBackColor = true;
            page.Padding = new Padding(8);
            page.AutoScroll = true;

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Top;
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 4;
            table.Padding = new Padding(2);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            foreach (ParamDef definition in _definitions)
            {
                if (definition.Category != category)
                    continue;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                AddParameterRow(table, row, definition);
                row++;
            }

            page.Controls.Add(table);
            _tabs.TabPages.Add(page);
        }

        private void AddWeaponsTab()
        {
            TabPage page = new TabPage("Weapons");
            page.Name = "Weapons";
            page.Padding = new Padding(8);
            ComboBox select = new ComboBox();
            select.DropDownStyle = ComboBoxStyle.DropDownList;
            select.Dock = DockStyle.Top;
            select.Items.AddRange(Bs2Profile.WeaponNames);
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.AutoScroll = true;
            List<TableLayoutPanel> panels = new List<TableLayoutPanel>();
            for (int weapon = 0; weapon < Bs2Profile.WeaponClasses.Length; ++weapon)
            {
                TableLayoutPanel table = new TableLayoutPanel();
                table.Dock = DockStyle.Top;
                table.AutoSize = true;
                table.ColumnCount = 4;
                table.Padding = new Padding(2, 8, 2, 2);
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                int row = 0;
                foreach (ParamDef definition in _definitions)
                {
                    if (definition.Category != "Weapons" ||
                        !definition.Key.StartsWith(Bs2Profile.WeaponClasses[weapon] + ".", StringComparison.Ordinal)) continue;
                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    AddParameterRow(table, row++, definition);
                }
                table.Visible = false;
                panels.Add(table);
                host.Controls.Add(table);
            }
            select.SelectedIndexChanged += delegate
            {
                host.SuspendLayout();
                for (int i = 0; i < panels.Count; ++i) panels[i].Visible = i == select.SelectedIndex;
                host.AutoScrollPosition = Point.Empty;
                host.ResumeLayout();
            };
            page.Controls.Add(host);
            page.Controls.Add(select);
            select.SelectedIndex = 0;
            _tabs.TabPages.Add(page);
        }

        private void AddDiagnosticsTab()
        {
            TabPage page = new TabPage("Diagnostics");
            page.Name = "Diagnostics";
            page.Padding = new Padding(8);
            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Top;
            actions.Height = 38;
            Button refresh = MakeButton("Refresh", SystemColors.Control, SystemColors.ControlText, 95);
            refresh.Click += delegate { RefreshDiagnostics(); };
            actions.Controls.Add(refresh);
            Button copy = MakeButton("Copy report", SystemColors.Control, SystemColors.ControlText, 130);
            copy.Click += delegate
            {
                try { RefreshDiagnostics(); Clipboard.SetText(_diagnostics.Text); SetStatus("Diagnostics copied.", Success); }
                catch (Exception ex) { SetStatus("Could not copy: " + ex.Message, Color.Firebrick); }
            };
            actions.Controls.Add(copy);
            Button open = MakeButton("Open VR data", SystemColors.Control, SystemColors.ControlText, 125);
            open.Click += delegate
            {
                try
                {
                    if (Directory.Exists(GameProfile.LocalDirectory))
                        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + GameProfile.LocalDirectory + "\"") { UseShellExecute = true });
                    else SetStatus("The VR directory does not exist yet; it will be created when saving.", Warning);
                }
                catch (Exception ex) { SetStatus("Could not open: " + ex.Message, Color.Firebrick); }
            };
            actions.Controls.Add(open);
            _diagnostics = new TextBox();
            _diagnostics.Multiline = true;
            _diagnostics.ReadOnly = true;
            _diagnostics.ScrollBars = ScrollBars.Both;
            _diagnostics.WordWrap = false;
            _diagnostics.Dock = DockStyle.Fill;
            _diagnostics.Font = new Font("Consoles", 9);
            page.Controls.Add(_diagnostics);
            page.Controls.Add(actions);
            _tabs.TabPages.Add(page);
        }

        private void RefreshDiagnostics()
        {
            if (_diagnostics == null) return;
            StringBuilder info = new StringBuilder();
            info.AppendLine(GameProfile.DisplayName + " VR DLSS/DLAA · 0.2.17 English");
            info.AppendLine("UTC date: " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture));
            info.AppendLine("Steam AppID: " + GameProfile.AppId);
            info.AppendLine("Executable: " + (_gameExePath ?? "(not found)"));
            try
            {
                if (!string.IsNullOrEmpty(_gameExePath) && File.Exists(_gameExePath))
                {
                    string hash = Bs2Profile.Hash(_gameExePath);
                    info.AppendLine("SHA-256: " + hash);
                    info.AppendLine("Compatible build: " + (hash == GameProfile.ExeHash ? "yes" : "NO"));
                }
            }
            catch (Exception ex) { info.AppendLine("Hash unavailable: " + ex.Message); }
            info.AppendLine("BS2-specific data: " + GameProfile.LocalDirectory);
            info.AppendLine("Effective resolution: " + _sharedIniPath);
            info.AppendLine("Mirror and graphics: " + _gameIniPath);
            foreach (string file in new string[] { _configPath, _weaponsPath, _dlssConfigPath, _sharedIniPath, _gameIniPath })
                info.AppendLine((File.Exists(file) ? "[exists] " : "[missing] ") + file);
            int width, height;
            if (TryGetRenderDimensions(out width, out height))
                info.AppendLine("Render Shared.ini: " + width + " × " + height +
                    " | SP mirror: " + (MirrorMatches(width, height) ? "synchronized" : "distinto/incompleto"));
            info.AppendLine("Backend: " + DetectDlssBackend().Summary);
            try
            {
                using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey xr = machine.OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1"))
                    info.AppendLine("Runtime OpenXR x86: " + (xr == null ? "(no log)" : Convert.ToString(xr.GetValue("ActiveRuntime"), CultureInfo.InvariantCulture)));
            }
            catch (Exception ex) { info.AppendLine("OpenXR: " + ex.Message); }
            info.AppendLine("Startup check: new process " + GameProfile.ProcessName + ", exact path, window and responsiveness for at least 3 seconds.");
            info.AppendLine("Note: a working window confirms game startup; it does not validate image quality or the headset session.");
            string logPath = Path.Combine(GameProfile.LocalDirectory, "bioshockvr.log");
            info.AppendLine();
            info.AppendLine("Latest log lines: " + logPath);
            try
            {
                if (File.Exists(logPath))
                {
                    using (FileStream stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        stream.Seek(Math.Max(0, stream.Length - 16000), SeekOrigin.Begin);
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string[] lines = reader.ReadToEnd().Replace("\r", "").Split('\n');
                            for (int i = Math.Max(0, lines.Length - 50); i < lines.Length; ++i) info.AppendLine(lines[i]);
                        }
                    }
                }
                else info.AppendLine("(The game has not generated a BS2 log yet.)");
            }
            catch (Exception ex) { info.AppendLine("Could not read: " + ex.Message); }
            _diagnostics.Text = info.ToString();
        }

        private static string ShortCategoryName(string category)
        {
            if (category == "Camera and scale") return "Camera";
            if (category == "Hands and aiming") return "Hands";
            if (category == "Movement and turning") return "Turning";
            if (category == "Gesture attacks") return "Gestures";
            if (category == "Cutscenes and effects") return "Cinematics";
            if (category == "HUD and aids") return "HUD";
            return category;
        }

        // Retain the tested configuration controls/handlers as backing state.
        // ImageTab.cs supplies the simplified public view; other tabs are unchanged.
        private void InitializeImageBackingControls()
        {
            TabPage page = new TabPage("Image");
            page.Name = "Image and resolution";
            page.UseVisualStyleBackColor = true;
            page.Padding = new Padding(10);
            page.AutoScroll = true;

            Label title = new Label();
            title.Text = "VR image · Normal, DLAA and DLSS 4.5";
            title.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
            title.ForeColor = Navy;
            title.AutoSize = true;
            title.Location = new Point(14, 12);
            page.Controls.Add(title);

            Label explanation = new Label();
            explanation.Text = "The game render is shown above. The VR output profile and DLSS quality below offer the same steps as the in-game controls. Normal and DLAA operate at 100 %.";
            explanation.ForeColor = Muted;
            explanation.AutoSize = true;
            explanation.MaximumSize = new Size(820, 0);
            explanation.Location = new Point(16, 40);
            page.Controls.Add(explanation);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.ColumnCount = 4;
            layout.RowCount = 3;
            layout.AutoSize = true;
            layout.Location = new Point(14, 80);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            page.Controls.Add(layout);

            layout.Controls.Add(MakeCompactLabel("Profile"), 0, 0);
            _resolutionPreset = new ComboBox();
            _resolutionPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            _resolutionPreset.Width = 245;
            _resolutionPreset.Items.Add("Custom / current");
            _resolutionPreset.Items.Add("1920 × 1080 · flat-screen");
            foreach (int size in SquareResolutionSteps)
                _resolutionPreset.Items.Add(size + " × " + size);
            _resolutionPreset.SelectedIndexChanged += ResolutionPresetChanged;
            layout.SetColumnSpan(_resolutionPreset, 3);
            layout.Controls.Add(_resolutionPreset, 1, 0);

            layout.Controls.Add(MakeCompactLabel("Width"), 0, 1);
            _resolutionWidth = MakeResolutionNumber();
            _resolutionWidth.ValueChanged += ResolutionValueChanged;
            layout.Controls.Add(_resolutionWidth, 1, 1);
            layout.Controls.Add(MakeCompactLabel("Height"), 2, 1);
            _resolutionHeight = MakeResolutionNumber();
            _resolutionHeight.ValueChanged += ResolutionValueChanged;
            layout.Controls.Add(_resolutionHeight, 3, 1);

            _squareResolution = new CheckBox();
            _squareResolution.Text = "Keep square resolution (recommended for VR)";
            _squareResolution.Checked = true;
            _squareResolution.AutoSize = true;
            _squareResolution.Padding = new Padding(0, 6, 0, 0);
            _squareResolution.CheckedChanged += SquareResolutionChanged;
            layout.SetColumnSpan(_squareResolution, 4);
            layout.Controls.Add(_squareResolution, 0, 2);

            _resolutionLoadLabel = new Label();
            _resolutionLoadLabel.Text = "Resolution awaiting load…";
            _resolutionLoadLabel.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _resolutionLoadLabel.ForeColor = Blue;
            _resolutionLoadLabel.AutoSize = true;
            _resolutionLoadLabel.Location = new Point(16, 190);
            page.Controls.Add(_resolutionLoadLabel);

            GroupBox fxaaGroup = new GroupBox();
            fxaaGroup.Text = "GAME FXAA · NOT DLSS";
            fxaaGroup.ForeColor = Navy;
            fxaaGroup.Location = new Point(14, 222);
            fxaaGroup.Size = new Size(820, 108);
            fxaaGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            fxaaGroup.Visible = !FinalDlssEdition;
            page.Controls.Add(fxaaGroup);

            _fxaaEnabled = new CheckBox();
            _fxaaEnabled.Text = "Enable game FXAA";
            _fxaaEnabled.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _fxaaEnabled.ForeColor = TextDark;
            _fxaaEnabled.AutoSize = true;
            _fxaaEnabled.Location = new Point(12, 21);
            _fxaaEnabled.CheckedChanged += FxaaChanged;
            fxaaGroup.Controls.Add(_fxaaEnabled);

            Label fxaaExplanation = new Label();
            fxaaExplanation.Text = "Smooth edges before the mod copies the image to the headset. May reduce aliasing and some spatial shimmer, with a slight loss of sharpness. This is not TAA or DLSS.";
            fxaaExplanation.ForeColor = Muted;
            fxaaExplanation.AutoSize = true;
            fxaaExplanation.MaximumSize = new Size(760, 0);
            fxaaExplanation.Location = new Point(32, 45);
            fxaaGroup.Controls.Add(fxaaExplanation);

            _fxaaStateLabel = new Label();
            _fxaaStateLabel.Text = "Status awaiting load…";
            _fxaaStateLabel.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
            _fxaaStateLabel.ForeColor = Blue;
            _fxaaStateLabel.AutoSize = true;
            _fxaaStateLabel.Location = new Point(32, 79);
            fxaaGroup.Controls.Add(_fxaaStateLabel);

            Label warning = new Label();
            warning.Text = "WINDOWED VR STARTUP  ·  Saving disables fullscreen in both INI files to preserve the selected resolution. The view inside the headset is unchanged. Save with the game closed. 10 % more per axis means 21 % more pixels.";
            warning.BackColor = SystemColors.Info;
            warning.ForeColor = SystemColors.InfoText;
            warning.Padding = new Padding(10);
            warning.AutoSize = true;
            warning.MaximumSize = new Size(820, 0);
            warning.Location = new Point(14, FinalDlssEdition ? 222 : 344);
            page.Controls.Add(warning);

            GroupBox upscalerGroup = new GroupBox();
            upscalerGroup.Text = "EXPERIMENTAL SPATIAL UPSCALING · NO DLSS / NO DLAA";
            upscalerGroup.ForeColor = Navy;
            upscalerGroup.Location = new Point(14, 416);
            upscalerGroup.Size = new Size(820, 158);
            upscalerGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            upscalerGroup.Visible = !FinalDlssEdition;
            page.Controls.Add(upscalerGroup);

            _upscalerEnabled = new CheckBox();
            _upscalerEnabled.Text = "Enable in the experimental DLL";
            _upscalerEnabled.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _upscalerEnabled.ForeColor = TextDark;
            _upscalerEnabled.AutoSize = true;
            _upscalerEnabled.Location = new Point(12, 20);
            _upscalerEnabled.CheckedChanged += UpscalerChanged;
            upscalerGroup.Controls.Add(_upscalerEnabled);

            Label upscalerExplanation = new Label();
            upscalerExplanation.Text = "For better performance, lower the game render resolution and keep larger OpenXR output. Experimental spatial processing: no AI, DLSS or DLAA; the stable DLL ignores it. Enabling it disables DLSS.";
            upscalerExplanation.ForeColor = Muted;
            upscalerExplanation.AutoSize = true;
            upscalerExplanation.MaximumSize = new Size(770, 0);
            upscalerExplanation.Location = new Point(30, 42);
            upscalerGroup.Controls.Add(upscalerExplanation);

            Button outputPlusTen = MakeSmallPresetButton("Output +10 %", 92);
            outputPlusTen.Location = new Point(650, 17);
            outputPlusTen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            outputPlusTen.Click += delegate { ApplyUpscalerOutputPreset(1.10); };
            _toolTip.SetToolTip(outputPlusTen,
                "Set output 10 % larger per axis than the current render, preserving aspect ratio and rounding to even pixels.");
            upscalerGroup.Controls.Add(outputPlusTen);

            Button outputOneToOne = MakeSmallPresetButton("1:1", 50);
            outputOneToOne.Location = new Point(748, 17);
            outputOneToOne.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            outputOneToOne.Click += delegate { ApplyUpscalerOutputPreset(1.00); };
            _toolTip.SetToolTip(outputOneToOne,
                "Match OpenXR output to the game render resolution: filtering without enlargement.");
            upscalerGroup.Controls.Add(outputOneToOne);

            Label outputLabel = MakeCompactLabel("VR output");
            outputLabel.Location = new Point(30, 78);
            upscalerGroup.Controls.Add(outputLabel);

            _upscalerOutputWidth = MakeResolutionNumber();
            _upscalerOutputWidth.Width = 105;
            _upscalerOutputWidth.Location = new Point(100, 77);
            _upscalerOutputWidth.ValueChanged += UpscalerChanged;
            upscalerGroup.Controls.Add(_upscalerOutputWidth);

            Label multiply = MakeCompactLabel("×");
            multiply.Location = new Point(211, 78);
            upscalerGroup.Controls.Add(multiply);

            _upscalerOutputHeight = MakeResolutionNumber();
            _upscalerOutputHeight.Width = 105;
            _upscalerOutputHeight.Location = new Point(232, 77);
            _upscalerOutputHeight.ValueChanged += UpscalerChanged;
            upscalerGroup.Controls.Add(_upscalerOutputHeight);

            Label sharpnessLabel = MakeCompactLabel("Sharpness");
            sharpnessLabel.Location = new Point(365, 78);
            upscalerGroup.Controls.Add(sharpnessLabel);

            _upscalerSharpness = new NumericUpDown();
            _upscalerSharpness.Minimum = 0m;
            _upscalerSharpness.Maximum = 1m;
            _upscalerSharpness.Increment = 0.05m;
            _upscalerSharpness.DecimalPlaces = 2;
            _upscalerSharpness.Value = 0.20m;
            _upscalerSharpness.Width = 72;
            _upscalerSharpness.TextAlign = HorizontalAlignment.Right;
            _upscalerSharpness.Location = new Point(422, 77);
            _upscalerSharpness.ValueChanged += UpscalerChanged;
            upscalerGroup.Controls.Add(_upscalerSharpness);

            Label sharpnessHint = new Label();
            sharpnessHint.Text = "0.00 soft · 1.00 strong";
            sharpnessHint.ForeColor = Muted;
            sharpnessHint.AutoSize = true;
            sharpnessHint.Location = new Point(501, 81);
            upscalerGroup.Controls.Add(sharpnessHint);

            _upscalerStateLabel = new Label();
            _upscalerStateLabel.Text = "Status awaiting load…";
            _upscalerStateLabel.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
            _upscalerStateLabel.ForeColor = Blue;
            _upscalerStateLabel.AutoSize = true;
            _upscalerStateLabel.MaximumSize = new Size(770, 0);
            _upscalerStateLabel.Location = new Point(30, 113);
            upscalerGroup.Controls.Add(_upscalerStateLabel);

            GroupBox dlssGroup = new GroupBox();
            dlssGroup.Text = "IMAGE MODE · NORMAL / DLAA / DLSS 4.5";
            dlssGroup.ForeColor = Navy;
            dlssGroup.BackColor = SystemColors.Control;
            dlssGroup.Location = new Point(14, FinalDlssEdition ? 292 : 584);
            dlssGroup.Size = new Size(820, 225);
            dlssGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            page.Controls.Add(dlssGroup);

            _dlssBackendStateLabel = new Label();
            _dlssBackendStateLabel.Text = "BACKEND · verification pending…";
            _dlssBackendStateLabel.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
            _dlssBackendStateLabel.ForeColor = Warning;
            _dlssBackendStateLabel.AutoSize = true;
            _dlssBackendStateLabel.MaximumSize = new Size(785, 0);
            _dlssBackendStateLabel.Location = new Point(14, 20);
            dlssGroup.Controls.Add(_dlssBackendStateLabel);

            Label dlssModeLabel = MakeCompactLabel("Mode");
            dlssModeLabel.Location = new Point(14, 48);
            dlssGroup.Controls.Add(dlssModeLabel);

            _dlssMode = new ComboBox();
            _dlssMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _dlssMode.Width = 235;
            _dlssMode.Location = new Point(66, 49);
            _dlssMode.Items.AddRange(new object[] {
                "Normal · native resolution",
                "DLAA 4.5 · native resolution",
                "DLSS 4.5 SR · upscaling"
            });
            _dlssMode.SelectedIndexChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssMode);

            Label runtimeLabel = MakeCompactLabel("Runtime");
            runtimeLabel.Location = new Point(326, 48);
            dlssGroup.Controls.Add(runtimeLabel);

            _dlssRuntime = new TextBox();
            _dlssRuntime.Text = "310.7.0 · tested";
            _dlssRuntime.ReadOnly = true;
            _dlssRuntime.TabStop = false;
            _dlssRuntime.BackColor = Color.White;
            _dlssRuntime.ForeColor = TextDark;
            _dlssRuntime.Width = 140;
            _dlssRuntime.Location = new Point(390, 49);
            dlssGroup.Controls.Add(_dlssRuntime);

            LinkLabel openDlss = new LinkLabel();
            openDlss.Text = "Open dlss.ini";
            openDlss.LinkColor = Blue;
            openDlss.AutoSize = true;
            openDlss.Location = new Point(704, 53);
            openDlss.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openDlss.LinkClicked += delegate { OpenDlssConfigurationFile(); };
            dlssGroup.Controls.Add(openDlss);

            Label dlssExplanation = new Label();
            dlssExplanation.Text = "Normal keeps native rendering. DLAA smooths edges at native resolution; DLSS reconstructs larger output from a smaller render. Both use depth, motion and separate history per eye. Does not include DLSS 5 Neural Rendering.";
            dlssExplanation.ForeColor = Muted;
            dlssExplanation.AutoSize = true;
            dlssExplanation.MaximumSize = new Size(785, 0);
            dlssExplanation.Location = new Point(14, 79);
            dlssGroup.Controls.Add(dlssExplanation);

            Label presetLabel = MakeCompactLabel("Preset");
            presetLabel.Location = new Point(14, 112);
            dlssGroup.Controls.Add(presetLabel);

            _dlssPreset = new ComboBox();
            _dlssPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            _dlssPreset.Width = 232;
            _dlssPreset.Location = new Point(66, 113);
            _dlssPreset.Items.Add("Recommended automatic · K/M/L by ratio");
            _dlssPreset.SelectedIndexChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssPreset);

            Label qualityLabel = MakeCompactLabel("SR quality");
            qualityLabel.Location = new Point(326, 112);
            dlssGroup.Controls.Add(qualityLabel);

            _dlssQuality = new ComboBox();
            _dlssQuality.DropDownStyle = ComboBoxStyle.DropDownList;
            _dlssQuality.Width = 245;
            _dlssQuality.Location = new Point(404, 113);
            _dlssQuality.Items.AddRange(new object[] {
                "Ultra performance · 1/3 (≈ 33 %)",
                "40 %",
                "Performance · 50 %",
                "Balanced · 58 %",
                "60 %",
                "Quality · 2/3 (≈ 67 %)",
                "70 %",
                "80 %",
                "90 %",
                "Custom / AUTO"
            });
            _dlssQuality.SelectedIndexChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssQuality);

            Label dlssSharpnessLabel = MakeCompactLabel("DLSS sharpness (%)");
            dlssSharpnessLabel.Location = new Point(667, 112);
            dlssGroup.Controls.Add(dlssSharpnessLabel);
            _dlssSharpness = new NumericUpDown();
            _dlssSharpness.Minimum = 0;
            _dlssSharpness.Maximum = 100;
            _dlssSharpness.Increment = 5;
            _dlssSharpness.Width = 95;
            _dlssSharpness.Location = new Point(671, 149);
            _dlssSharpness.ValueChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssSharpness);
            _toolTip.SetToolTip(_dlssSharpness,
                "Optional sharpening after DLSS, as with F1/F2/F3. 0 % keeps the original image. Does not change resolution or quality. Applies only in DLSS.");

            Label dlssOutputLabel = MakeCompactLabel("VR output");
            dlssOutputLabel.Location = new Point(14, 148);
            dlssGroup.Controls.Add(dlssOutputLabel);

            _dlssOutputWidth = MakeResolutionNumber();
            _dlssOutputWidth.Width = 105;
            _dlssOutputWidth.Location = new Point(82, 149);
            _dlssOutputWidth.ValueChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssOutputWidth);

            Label dlssMultiply = MakeCompactLabel("×");
            dlssMultiply.Location = new Point(193, 149);
            dlssGroup.Controls.Add(dlssMultiply);

            _dlssOutputHeight = MakeResolutionNumber();
            _dlssOutputHeight.Width = 105;
            _dlssOutputHeight.Location = new Point(214, 149);
            _dlssOutputHeight.ValueChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssOutputHeight);

            _dlssOutputPreset = new ComboBox();
            _dlssOutputPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            _dlssOutputPreset.Width = 309;
            _dlssOutputPreset.Location = new Point(340, 149);
            _dlssOutputPreset.Items.Add("VR output profile · custom");
            foreach (int size in SquareResolutionSteps)
                _dlssOutputPreset.Items.Add(size + " × " + size + " · per eye");
            _dlssOutputPreset.SelectedIndexChanged += DlssOutputPresetChanged;
            dlssGroup.Controls.Add(_dlssOutputPreset);
            _toolTip.SetToolTip(_dlssOutputPreset,
                "Same steps as F1/F2/F3. Keeps DLSS quality and recalculates render size. In Normal and DLAA, render and output match.");

            Label nearPlaneLabel = MakeCompactLabel("Near plane · automatic");
            nearPlaneLabel.Location = new Point(14, 182);
            dlssGroup.Controls.Add(nearPlaneLabel);

            _dlssNearPlane = new NumericUpDown();
            _dlssNearPlane.Minimum = 0.1m;
            _dlssNearPlane.Maximum = 1000.0m;
            _dlssNearPlane.Increment = 0.1m;
            _dlssNearPlane.DecimalPlaces = 2;
            _dlssNearPlane.Value = 10.0m;
            _dlssNearPlane.Width = 76;
            _dlssNearPlane.TextAlign = HorizontalAlignment.Right;
            _dlssNearPlane.Location = new Point(176, 183);
            _dlssNearPlane.ValueChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssNearPlane);

            Label nearPlaneUnit = MakeCompactLabel("UU");
            nearPlaneUnit.Location = new Point(257, 182);
            dlssGroup.Controls.Add(nearPlaneUnit);

            Label nearPlaneHint = new Label();
            nearPlaneHint.Text = "BS2 baseline: 10.0 UU. The mod uses the observed per-eye projection; no manual adjustment is needed.";
            nearPlaneHint.ForeColor = Muted;
            nearPlaneHint.AutoSize = true;
            nearPlaneHint.MaximumSize = new Size(510, 0);
            nearPlaneHint.Location = new Point(296, 185);
            dlssGroup.Controls.Add(nearPlaneHint);
            nearPlaneLabel.Visible = false;
            nearPlaneUnit.Visible = false;
            nearPlaneHint.Visible = false;
            _dlssNearPlane.Visible = false;

            _dlssSettingsStateLabel = new Label();
            _dlssSettingsStateLabel.Text = "DLSS configuration awaiting load…";
            _dlssSettingsStateLabel.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
            _dlssSettingsStateLabel.ForeColor = Blue;
            _dlssSettingsStateLabel.AutoSize = true;
            _dlssSettingsStateLabel.MaximumSize = new Size(785, 0);
            _dlssSettingsStateLabel.Location = new Point(14, 184);
            dlssGroup.Controls.Add(_dlssSettingsStateLabel);

            _toolTip.SetToolTip(_dlssPreset,
                "Read-only in this phase: the current host does not use a manual preset. It uses the recommended K/M/L mapping based on mode and ratio.");
            _toolTip.SetToolTip(_dlssQuality,
                "In DLSS SR, choose a standard ratio. Output stays unchanged; the game's internal resolution is adjusted. Other fine-tuned ratios appear as Custom / AUTO.");
            _toolTip.SetToolTip(_dlssNearPlane,
                "Projection near plane in game units, used only for temporal reconstruction. Recommended: 10.0 UU. Does not change height or FOV.");

            _tabs.TabPages.Add(page);
        }

        private Label MakeCompactLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            label.ForeColor = TextDark;
            label.Padding = new Padding(0, 5, 4, 5);
            return label;
        }

        private NumericUpDown MakeResolutionNumber()
        {
            NumericUpDown number = new NumericUpDown();
            number.Minimum = 1024;
            number.Maximum = 8192;
            number.Increment = 64;
            number.Value = 2048;
            number.Width = 145;
            number.TextAlign = HorizontalAlignment.Right;
            number.ThousandsSeparator = true;
            return number;
        }

        private Button MakeSmallPresetButton(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 24;
            button.FlatStyle = FlatStyle.System;
            button.UseVisualStyleBackColor = true;
            button.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            return button;
        }

        private void InitializeHiddenIniEditor()
        {
            TabPage page = new TabPage("Bioshock2SP.ini");
            page.Name = "Full Bioshock2SP.ini";
            page.UseVisualStyleBackColor = true;
            page.Padding = new Padding(7);

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 34;
            toolbar.WrapContents = false;
            toolbar.Padding = new Padding(2, 3, 0, 0);
            page.Controls.Add(toolbar);

            toolbar.Controls.Add(MakeToolbarLabel("Section:"));
            _iniSectionFilter = new ComboBox();
            _iniSectionFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _iniSectionFilter.Width = 145;
            _iniSectionFilter.SelectedIndexChanged += delegate { RebuildIniGrid(); };
            toolbar.Controls.Add(_iniSectionFilter);

            toolbar.Controls.Add(MakeToolbarLabel("Mostrar:"));
            _iniImpactFilter = new ComboBox();
            _iniImpactFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _iniImpactFilter.Width = 108;
            _iniImpactFilter.Items.AddRange(new object[] {
                "All", "Direct VR", "VR related", "Warnings", "Modified"
            });
            _iniImpactFilter.SelectedIndexChanged += delegate { RebuildIniGrid(); };
            toolbar.Controls.Add(_iniImpactFilter);

            toolbar.Controls.Add(MakeToolbarLabel("Buscar:"));
            _iniSearch = new TextBox();
            _iniSearch.Width = 110;
            _iniSearch.TextChanged += delegate { RebuildIniGrid(); };
            toolbar.Controls.Add(_iniSearch);

            _allowRiskyEdits = new CheckBox();
            _allowRiskyEdits.Text = "Unlock";
            _allowRiskyEdits.AutoSize = true;
            _allowRiskyEdits.Padding = new Padding(5, 4, 0, 0);
            _allowRiskyEdits.CheckedChanged += delegate { RebuildIniGrid(); };
            toolbar.Controls.Add(_allowRiskyEdits);

            Button restore = new Button();
            restore.Text = "Restore row";
            restore.AutoSize = true;
            restore.Height = 26;
            restore.FlatStyle = FlatStyle.Flat;
            restore.Click += delegate { RestoreSelectedIniEntry(); };
            toolbar.Controls.Add(restore);

            _iniCountLabel = new Label();
            _iniCountLabel.AutoSize = true;
            _iniCountLabel.ForeColor = Muted;
            _iniCountLabel.Padding = new Padding(8, 6, 0, 0);
            toolbar.Controls.Add(_iniCountLabel);

            Panel detailPanel = new Panel();
            detailPanel.Dock = DockStyle.Bottom;
            detailPanel.Height = 68;
            detailPanel.BackColor = Color.White;
            detailPanel.Padding = new Padding(9, 7, 9, 7);
            page.Controls.Add(detailPanel);

            _iniDetail = new Label();
            _iniDetail.Dock = DockStyle.Fill;
            _iniDetail.AutoEllipsis = true;
            _iniDetail.ForeColor = Muted;
            _iniDetail.Text = "Select a row to see what it does and whether it may affect VR.";
            detailPanel.Controls.Add(_iniDetail);

            _iniGrid = new DataGridView();
            _iniGrid.Dock = DockStyle.Fill;
            _iniGrid.AllowUserToAddRows = false;
            _iniGrid.AllowUserToDeleteRows = false;
            _iniGrid.AllowUserToResizeRows = false;
            _iniGrid.RowHeadersVisible = false;
            _iniGrid.MultiSelect = false;
            _iniGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _iniGrid.EditMode = DataGridViewEditMode.EditOnEnter;
            _iniGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _iniGrid.RowTemplate.Height = 22;
            _iniGrid.BackgroundColor = Color.White;
            _iniGrid.BorderStyle = BorderStyle.FixedSingle;
            _iniGrid.EnableHeadersVisualStyles = false;
            _iniGrid.ColumnHeadersDefaultCellStyle.BackColor = Navy;
            _iniGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _iniGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
            _iniGrid.DefaultCellStyle.Font = new Font("Segoe UI", 8.5f);
            _iniGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 112, 166);
            _iniGrid.Columns.Add(MakeIniColumn("Impact", "Scope", 82, true));
            _iniGrid.Columns.Add(MakeIniColumn("Setting", "Setting in English", 175, true));
            _iniGrid.Columns.Add(MakeIniColumn("Value", "Value", 125, false));
            _iniGrid.Columns.Add(MakeIniColumn("Key", "Original key", 170, true));
            DataGridViewTextBoxColumn sectionColumn = MakeIniColumn("Section", "Section", 220, true);
            sectionColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            sectionColumn.MinimumWidth = 150;
            _iniGrid.Columns.Add(sectionColumn);
            _iniGrid.CellBeginEdit += IniGridCellBeginEdit;
            _iniGrid.CellValueChanged += IniGridCellValueChanged;
            _iniGrid.SelectionChanged += delegate { UpdateIniDetail(); };
            page.Controls.Add(_iniGrid);
            _iniGrid.BringToFront();

            // El editor completo se mantiene solo como infraestructura interna para
            // conservar la lectura y escritura segura del INI. No se muestra al usuario.
        }

        private Label MakeToolbarLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = TextDark;
            label.Padding = new Padding(3, 5, 0, 0);
            label.Margin = new Padding(2, 1, 1, 0);
            return label;
        }

        private DataGridViewTextBoxColumn MakeIniColumn(string name, string header, int width,
                                                        bool readOnly)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = header;
            column.Width = width;
            column.ReadOnly = readOnly;
            column.SortMode = DataGridViewColumnSortMode.Automatic;
            return column;
        }

        private void AddParameterRow(TableLayoutPanel table, int row, ParamDef definition)
        {
            Color rowColor = row % 2 == 0 ? Color.White : PaleBlue;
            Label label = new Label();
            label.Text = definition.Label;
            label.Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold);
            label.ForeColor = TextDark;
            label.AutoSize = true;
            label.MaximumSize = new Size(210, 0);
            label.Padding = new Padding(5, 4, 3, 4);
            label.Margin = new Padding(0, 1, 0, 1);
            label.BackColor = rowColor;
            label.Dock = DockStyle.Fill;

            Control editor = CreateEditor(definition);
            editor.Margin = new Padding(6, 5, 6, 3);
            _editors[definition.Key] = editor;

            Label unit = new Label();
            unit.Text = definition.Unit;
            unit.ForeColor = Muted;
            unit.AutoSize = true;
            unit.Padding = new Padding(0, 5, 3, 3);
            unit.Margin = new Padding(0, 1, 0, 1);
            unit.BackColor = rowColor;
            unit.Dock = DockStyle.Fill;

            Label description = new Label();
            description.Text = definition.Description;
            description.ForeColor = Muted;
            description.Font = new Font("Segoe UI", 8.25f);
            description.AutoSize = true;
            description.MaximumSize = new Size(470, 0);
            description.Padding = new Padding(5, 3, 5, 4);
            description.Margin = new Padding(0, 1, 0, 1);
            description.BackColor = rowColor;
            description.Dock = DockStyle.Fill;

            _toolTip.SetToolTip(label, definition.Description);
            _toolTip.SetToolTip(editor, definition.Description);
            _toolTip.SetToolTip(description, definition.Description + Environment.NewLine +
                                             "Internal parameter: " + definition.Key);
            table.Controls.Add(label, 0, row);
            table.Controls.Add(editor, 1, row);
            table.Controls.Add(unit, 2, row);
            table.Controls.Add(description, 3, row);
        }

        private Control CreateEditor(ParamDef definition)
        {
            if (definition.Kind == ParamKind.Boolean)
            {
                CheckBox box = new CheckBox();
                box.Text = "On";
                box.AutoSize = true;
                box.ForeColor = TextDark;
                box.CheckedChanged += delegate { MarkDirty(); };
                return box;
            }
            if (definition.Kind == ParamKind.Choice)
            {
                ComboBox combo = new ComboBox();
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                combo.Width = 135;
                combo.Items.AddRange(definition.Choices);
                combo.SelectedIndexChanged += delegate { MarkDirty(); };
                return combo;
            }

            NumericUpDown number = new NumericUpDown();
            number.Width = 120;
            number.Minimum = definition.Minimum;
            number.Maximum = definition.Maximum;
            number.Increment = definition.Increment;
            number.DecimalPlaces = definition.Decimals;
            number.ThousandsSeparator = false;
            number.TextAlign = HorizontalAlignment.Right;
            number.ValueChanged += delegate { MarkDirty(); };
            return number;
        }

        private void ResolutionPresetChanged(object sender, EventArgs e)
        {
            if (_loading || _syncingResolution || _resolutionPreset.SelectedIndex <= 0)
                return;
            int index = _resolutionPreset.SelectedIndex;
            int width = index == 1 ? 1920 : SquareResolutionSteps[index - 2];
            int height = index == 1 ? 1080 : width;
            _syncingResolution = true;
            _resolutionWidth.Value = width;
            _resolutionHeight.Value = height;
            _squareResolution.Checked = width == height;
            _syncingResolution = false;
            ApplyResolutionControlsToEntries();
        }

        private void ResolutionValueChanged(object sender, EventArgs e)
        {
            if (_loading || _syncingResolution)
                return;
            _syncingResolution = true;
            if (_squareResolution.Checked)
            {
                if (sender == _resolutionHeight)
                {
                    decimal square = Math.Max(_resolutionWidth.Minimum, _resolutionHeight.Value);
                    _resolutionWidth.Value = square;
                    _resolutionHeight.Value = square;
                }
                else
                    _resolutionHeight.Value = _resolutionWidth.Value;
            }
            _resolutionPreset.SelectedIndex = ResolutionPresetIndex(
                (int)_resolutionWidth.Value, (int)_resolutionHeight.Value);
            _syncingResolution = false;
            ApplyResolutionControlsToEntries();
        }

        private void SquareResolutionChanged(object sender, EventArgs e)
        {
            if (_loading || _syncingResolution || !_squareResolution.Checked)
                return;
            _syncingResolution = true;
            _resolutionHeight.Value = _resolutionWidth.Value;
            _resolutionPreset.SelectedIndex = ResolutionPresetIndex(
                (int)_resolutionWidth.Value, (int)_resolutionHeight.Value);
            _syncingResolution = false;
            ApplyResolutionControlsToEntries();
        }

        private static int ResolutionPresetIndex(int width, int height)
        {
            if (width == 1920 && height == 1080) return 1;
            if (width == height)
                for (int i = 0; i < SquareResolutionSteps.Length; i++)
                    if (SquareResolutionSteps[i] == width) return i + 2;
            return 0;
        }

        private IniEntry FindIniEntry(string section, string key)
        {
            IniEntry found = null;
            IList<IniEntry> entries = string.Equals(section, "SharedOptions", StringComparison.OrdinalIgnoreCase)
                ? _sharedIniEntries : _gameIniEntries;
            foreach (IniEntry entry in entries)
            {
                if (string.Equals(entry.Section, section, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (found != null)
                        return null;
                    found = entry;
                }
            }
            return found;
        }

        private static bool IsPcResolutionEntry(IniEntry entry)
        {
            if (entry == null || entry.Section != "WinDrv.WindowsClient") return false;
            return entry.Key == "WindowedViewportX" || entry.Key == "WindowedViewportY" ||
                   entry.Key == "FullscreenViewportX" || entry.Key == "FullscreenViewportY";
        }

        private static bool IsFxaaEntry(IniEntry entry)
        {
            return entry != null &&
                   string.Equals(entry.Section, "Engine.RenderConfig", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(entry.Key, "UseFxaa", StringComparison.OrdinalIgnoreCase);
        }

        private bool EnforceFinalImagePolicy()
        {
            if (!FinalDlssEdition) return false;
            bool adjusted = false;

            IniEntry fxaa = FindIniEntry("Engine.RenderConfig", "UseFxaa");
            bool fxaaEnabled;
            if (fxaa != null && TryParseIniSwitch(fxaa.Value, out fxaaEnabled) && fxaaEnabled)
            {
                fxaa.Value = FormatIniSwitch(fxaa.OriginalValue, false);
                _syncingFxaa = true;
                _fxaaEnabled.Checked = false;
                _syncingFxaa = false;
                adjusted = true;
            }

            // La edición final solo admite Normal, DLAA y DLSS. Un
            // upscaler.ini heredado puede seguir activando el reescalado
            // espacial aunque sus controles ya no se muestren, por lo que
            // dejamos pendiente persistir enabled=0 de forma transaccional.
            if (_upscalerEnabled != null && _upscalerEnabled.Checked)
            {
                _syncingUpscaler = true;
                _upscalerEnabled.Checked = false;
                _syncingUpscaler = false;
                _upscalerDirty = true;
                adjusted = true;
            }

            if (adjusted)
                RecalculateGameIniDirty();
            return adjusted;
        }

        private static bool TryParseIniSwitch(string value, out bool enabled)
        {
            string normalized = (value ?? string.Empty).Trim().TrimEnd(';').Trim();
            if (normalized == "1" || string.Equals(normalized, "True", StringComparison.OrdinalIgnoreCase))
            {
                enabled = true;
                return true;
            }
            if (normalized == "0" || string.Equals(normalized, "False", StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                return true;
            }
            enabled = false;
            return false;
        }

        private static string FormatIniSwitch(string originalValue, bool enabled)
        {
            string original = originalValue ?? string.Empty;
            string trimmed = original.Trim();
            bool semicolon = trimmed.EndsWith(";", StringComparison.Ordinal);
            string normalized = trimmed.TrimEnd(';').Trim();
            bool usesWords = string.Equals(normalized, "True", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(normalized, "False", StringComparison.OrdinalIgnoreCase);
            string value = usesWords ? (enabled ? "True" : "False") : (enabled ? "1" : "0");
            return value + (semicolon ? ";" : string.Empty);
        }

        private void LoadFxaaControl()
        {
            if (_fxaaEnabled == null || _fxaaStateLabel == null) return;
            IniEntry entry = FindIniEntry("Engine.RenderConfig", "UseFxaa");
            bool enabled;
            _syncingFxaa = true;
            try
            {
                if (entry == null)
                {
                    _fxaaEnabled.Enabled = false;
                    _fxaaEnabled.Checked = false;
                    _fxaaStateLabel.Text = "A unique [Engine.RenderConfig] UseFxaa key was not found.";
                    _fxaaStateLabel.ForeColor = Color.Firebrick;
                    return;
                }
                if (!TryParseIniSwitch(entry.Value, out enabled))
                {
                    _fxaaEnabled.Enabled = false;
                    _fxaaEnabled.Checked = false;
                    _fxaaStateLabel.Text = "Unrecognized value: UseFxaa=" + entry.Value + ". Correct it in the full INI editor.";
                    _fxaaStateLabel.ForeColor = Color.Firebrick;
                    return;
                }
                _fxaaEnabled.Enabled = true;
                _fxaaEnabled.Checked = enabled;
                UpdateFxaaSummary(entry, enabled);
            }
            finally
            {
                _syncingFxaa = false;
            }
        }

        private void UpdateFxaaSummary(IniEntry entry, bool enabled)
        {
            string pending = entry != null && entry.Changed ? "UNSAVED  ·  " : "";
            _fxaaStateLabel.Text = pending + "UseFxaa=" + (enabled ? "1" : "0") +
                "  ·  " + (enabled ? "enabled" : "disabled");
            _fxaaStateLabel.ForeColor = entry != null && entry.Changed ? Warning : Blue;
        }

        private void FxaaChanged(object sender, EventArgs e)
        {
            if (_loading || _syncingFxaa) return;
            IniEntry entry = FindIniEntry("Engine.RenderConfig", "UseFxaa");
            if (entry == null)
            {
                SetStatus("Cannot change FXAA: the UseFxaa key is missing or duplicated.", Color.Firebrick);
                LoadFxaaControl();
                return;
            }
            entry.Value = FormatIniSwitch(entry.OriginalValue, _fxaaEnabled.Checked);
            UpdateFxaaSummary(entry, _fxaaEnabled.Checked);
            RecalculateGameIniDirty();
            RebuildIniGrid();
            SetStatus("FXAA " + (_fxaaEnabled.Checked ? "enabled" : "disabled") +
                " in the editor. Click Save to apply it on the next launch.", Blue);
        }

        private void LoadResolutionControls()
        {
            IniEntry widthEntry = GameProfile.IsBioShock2 ? FindIniEntry("SharedOptions", "ViewportX") : FindIniEntry("WinDrv.WindowsClient", "WindowedViewportX");
            IniEntry heightEntry = GameProfile.IsBioShock2 ? FindIniEntry("SharedOptions", "ViewportY") : FindIniEntry("WinDrv.WindowsClient", "WindowedViewportY");
            IniEntry fullWidth = GameProfile.IsBioShock2 ? widthEntry : FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportX");
            IniEntry fullHeight = GameProfile.IsBioShock2 ? heightEntry : FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportY");
            int width, height, fw, fh;
            if (widthEntry == null || heightEntry == null || fullWidth == null || fullHeight == null ||
                !int.TryParse(widthEntry.Value.TrimEnd(';'), NumberStyles.Integer, CultureInfo.InvariantCulture, out width) ||
                !int.TryParse(heightEntry.Value.TrimEnd(';'), NumberStyles.Integer, CultureInfo.InvariantCulture, out height) ||
                !int.TryParse(fullWidth.Value.TrimEnd(';'), NumberStyles.Integer, CultureInfo.InvariantCulture, out fw) ||
                !int.TryParse(fullHeight.Value.TrimEnd(';'), NumberStyles.Integer, CultureInfo.InvariantCulture, out fh) ||
                width < (FinalDlssEdition ? 1 : 1024) || width > 8192 ||
                height < (FinalDlssEdition ? 1 : 1024) || height > 8192)
            {
                _resolutionWidth.Enabled = false;
                _resolutionHeight.Enabled = false;
                _resolutionPreset.Enabled = false;
                _resolutionLoadLabel.Text = "Unique, valid resolution keys are missing from " + (GameProfile.IsBioShock2 ? "Shared.ini" : "Bioshock.ini") + ".";
                _resolutionLoadLabel.ForeColor = Color.Firebrick;
                return;
            }
            _syncingResolution = true;
            _resolutionWidth.Minimum = 640;
            _resolutionHeight.Minimum = 480;
            _resolutionWidth.Enabled = true;
            _resolutionHeight.Enabled = true;
            _resolutionPreset.Enabled = true;
            _resolutionWidth.Value = Math.Max(1024, width);
            _resolutionHeight.Value = Math.Max(1024, height);
            _squareResolution.Checked = width == height;
            _resolutionPreset.SelectedIndex = ResolutionPresetIndex(width, height);
            _syncingResolution = false;
            UpdateResolutionSummary(width, height, MirrorMatches(width, height));
        }

        private bool MirrorMatches(int width, int height)
        {
            string[] keys = { "WindowedViewportX", "WindowedViewportY", "FullscreenViewportX", "FullscreenViewportY" };
            for (int i = 0; i < keys.Length; ++i)
            {
                IniEntry entry = FindIniEntry("WinDrv.WindowsClient", keys[i]);
                int value;
                if (entry == null || !int.TryParse(entry.Value.TrimEnd(';'), out value) ||
                    value != (i % 2 == 0 ? width : height)) return false;
            }
            return true;
        }

        private void ApplyResolutionControlsToEntries()
        {
            if (!GameProfile.IsBioShock2) { ApplyResolutionControlsToEntriesBs1(); return; }
            if (_loading) return;
            IniEntry sx = FindIniEntry("SharedOptions", "ViewportX");
            IniEntry sy = FindIniEntry("SharedOptions", "ViewportY");
            if (sx == null || sy == null)
            {
                SetStatus("Cannot change resolution: the unique Shared.ini pair is missing.", Color.Firebrick);
                return;
            }
            string width = ((int)_resolutionWidth.Value).ToString(CultureInfo.InvariantCulture);
            string height = ((int)_resolutionHeight.Value).ToString(CultureInfo.InvariantCulture);
            sx.Value = width;
            sy.Value = height;
            string[] keys = { "WindowedViewportX", "WindowedViewportY", "FullscreenViewportX", "FullscreenViewportY" };
            for (int i = 0; i < keys.Length; ++i)
            {
                IniEntry entry = FindIniEntry("WinDrv.WindowsClient", keys[i]);
                if (entry != null) entry.Value = i % 2 == 0 ? width : height;
            }
            UpdateResolutionSummary((int)_resolutionWidth.Value, (int)_resolutionHeight.Value,
                MirrorMatches((int)_resolutionWidth.Value, (int)_resolutionHeight.Value));
            RecalculateGameIniDirty();
            RebuildIniGrid();
        }


        private void ApplyResolutionControlsToEntriesBs1()
        {
            if (_loading) return;
            IniEntry wx = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportX");
            IniEntry wy = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportY");
            IniEntry fx = FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportX");
            IniEntry fy = FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportY");
            if (wx == null || wy == null || fx == null || fy == null)
            {
                SetStatus("Cannot change resolution: unique keys are missing from the PC section.", Color.Firebrick);
                return;
            }
            string width = ((int)_resolutionWidth.Value).ToString(CultureInfo.InvariantCulture);
            string height = ((int)_resolutionHeight.Value).ToString(CultureInfo.InvariantCulture);
            wx.Value = width;
            wy.Value = height;
            fx.Value = width;
            fy.Value = height;
            UpdateResolutionSummary((int)_resolutionWidth.Value, (int)_resolutionHeight.Value, true);
            RecalculateGameIniDirty();
            RebuildIniGrid();
        }

        private void UpdateResolutionSummary(int width, int height, bool pairsMatch)
        {
            double megapixels = (double)width * (double)height / 1000000.0;
            double ratio = height == 0 ? 0.0 : (double)width / (double)height;
            string shape = Math.Abs(ratio - 1.0) < 0.01 ? "square" : "ratio " + ratio.ToString("0.000", CultureInfo.CurrentCulture);
            _resolutionLoadLabel.Text = width.ToString(CultureInfo.CurrentCulture) + " × " +
                height.ToString(CultureInfo.CurrentCulture) + "  ·  " +
                megapixels.ToString("0.00", CultureInfo.CurrentCulture) + " MP  ·  " + shape +
                (pairsMatch ? "  ·  Shared.ini + SP mirror synchronized" :
                              "  ·  Shared.ini takes priority; SP mirror differs or is incomplete");
            _resolutionLoadLabel.ForeColor = pairsMatch ? Blue : Warning;
            UpdateUpscalerSummary();
            if (_syncingDlss) return;
            SynchronizeDlssOutputForDlaa(true);
            if (!_loading && _dlssMode != null && _dlssMode.SelectedIndex == 2 &&
                width < (int)_dlssOutputWidth.Value && height < (int)_dlssOutputHeight.Value &&
                UpscalerConfigDocument.HasExactAspect(width, height,
                    (int)_dlssOutputWidth.Value, (int)_dlssOutputHeight.Value))
            {
                DlssQuality matched;
                if (!DlssQualityPolicy.TryMatchCanonical(width, height,
                        (int)_dlssOutputWidth.Value, (int)_dlssOutputHeight.Value, out matched) ||
                    !DlssQualityPolicy.TryGetFraction(matched,
                        out _srScaleNumerator, out _srScaleDenominator))
                {
                    _srScaleNumerator = width;
                    _srScaleDenominator = (int)_dlssOutputWidth.Value;
                }
                _dlssDirty = true;
            }
            SynchronizeDlssQualityFromResolution();
            UpdateDlssSummary();
        }

        private bool TryGetRenderDimensions(out int width, out int height)
        {
            if (!GameProfile.IsBioShock2) return TryGetRenderDimensionsBs1(out width, out height);
            width = 0;
            height = 0;
            IniEntry widthEntry = FindIniEntry("SharedOptions", "ViewportX");
            IniEntry heightEntry = FindIniEntry("SharedOptions", "ViewportY");
            return widthEntry != null && heightEntry != null &&
                int.TryParse(widthEntry.Value.TrimEnd(';'), NumberStyles.Integer, CultureInfo.InvariantCulture, out width) &&
                int.TryParse(heightEntry.Value.TrimEnd(';'), NumberStyles.Integer, CultureInfo.InvariantCulture, out height) &&
                width >= 640 && height >= 480;
        }


        private bool TryGetRenderDimensionsBs1(out int width, out int height)
        {
            width = 0;
            height = 0;
            if (_resolutionWidth != null && _resolutionHeight != null &&
                _resolutionWidth.Enabled && _resolutionHeight.Enabled)
            {
                width = (int)_resolutionWidth.Value;
                height = (int)_resolutionHeight.Value;
                return width >= 1024 && height >= 1024;
            }

            IniEntry widthEntry = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportX");
            IniEntry heightEntry = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportY");
            return widthEntry != null && heightEntry != null &&
                   int.TryParse(widthEntry.Value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out width) &&
                   int.TryParse(heightEntry.Value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out height) &&
                   width >= 1024 && height >= 1024;
        }

        private UpscalerSettings CollectUpscalerSettings()
        {
            UpscalerSettings settings = new UpscalerSettings();
            settings.Enabled = _upscalerEnabled.Checked;
            settings.OutputWidth = (int)_upscalerOutputWidth.Value;
            settings.OutputHeight = (int)_upscalerOutputHeight.Value;
            settings.Sharpness = _upscalerSharpness.Value;
            return settings;
        }

        private bool TryValidateUpscalerSettings(UpscalerSettings settings, out string problem)
        {
            problem = null;
            if (settings.OutputWidth < 1024 || settings.OutputWidth > 8192 ||
                settings.OutputHeight < 1024 || settings.OutputHeight > 8192)
            {
                problem = "VR output must be between 1024 and 8192 pixels per axis.";
                return false;
            }
            if (settings.Sharpness < 0m || settings.Sharpness > 1m)
            {
                problem = "Sharpness must be between 0.00 and 1.00.";
                return false;
            }

            // La salida queda almacenada aunque el filtro esté apagado. La proporción
            // solo es operativa (y por tanto obligatoria) cuando se activa.
            if (!settings.Enabled)
                return true;

            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
            {
                problem = "Cannot check the aspect ratio because a valid game resolution is missing.";
                return false;
            }
            if (settings.OutputWidth < renderWidth || settings.OutputHeight < renderHeight)
            {
                problem = "VR output cannot be smaller than the game render resolution; that would be downsampling, not upscaling.";
                return false;
            }

            if (!UpscalerConfigDocument.HasExactAspect(renderWidth, renderHeight,
                                                        settings.OutputWidth,
                                                        settings.OutputHeight))
            {
                problem = "VR output must exactly preserve the aspect ratio of " +
                    renderWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                    renderHeight.ToString(CultureInfo.CurrentCulture) + ".";
                return false;
            }
            return true;
        }

        private void LoadUpscalerConfiguration()
        {
            if (_upscalerEnabled == null) return;
            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
            {
                renderWidth = 2048;
                renderHeight = 2048;
            }

            _syncingUpscaler = true;
            try
            {
                bool exists = File.Exists(_upscalerConfigPath);
                _upscalerLoadedContent = exists
                    ? File.ReadAllText(_upscalerConfigPath, Encoding.UTF8)
                    : string.Empty;
                UpscalerSettings settings;
                string warning;
                bool valid = UpscalerConfigDocument.TryParse(_upscalerLoadedContent,
                    renderWidth, renderHeight, out settings, out warning);

                _upscalerEnabled.Enabled = true;
                _upscalerOutputWidth.Enabled = true;
                _upscalerOutputHeight.Enabled = true;
                _upscalerSharpness.Enabled = true;
                _upscalerEnabled.Checked = settings.Enabled;
                _upscalerOutputWidth.Value = settings.OutputWidth;
                _upscalerOutputHeight.Value = settings.OutputHeight;
                _upscalerSharpness.Value = settings.Sharpness;
                _upscalerDirty = false;
                _upscalerLoadWarning = valid ? null :
                    "upscaler.ini contains incomplete or invalid values: " + warning;
            }
            catch (Exception ex)
            {
                _upscalerLoadedContent = string.Empty;
                _upscalerDirty = false;
                _upscalerLoadWarning = "Could not read upscaler.ini: " + ex.Message;
                _upscalerEnabled.Enabled = false;
                _upscalerOutputWidth.Enabled = false;
                _upscalerOutputHeight.Enabled = false;
                _upscalerSharpness.Enabled = false;
            }
            finally
            {
                _syncingUpscaler = false;
            }
            UpdateUpscalerSummary();
        }

        private void UpscalerChanged(object sender, EventArgs e)
        {
            if (_loading || _syncingUpscaler) return;
            _upscalerLoadWarning = null;
            _upscalerDirty = true;
            if (_upscalerEnabled.Checked)
                DeactivateDlssForSpatial();
            UpdateUpscalerSummary();
            UpdateDirtyState();
        }

        private void DeactivateDlssForSpatial()
        {
            if (_dlssMode == null) return;
            _syncingDlss = true;
            try
            {
                _dlssMode.SelectedIndex = 0;
                if (_dlssPreset != null) _dlssPreset.SelectedIndex = 0;
                if (_dlssQuality != null) _dlssQuality.SelectedIndex = (int)DlssQuality.Custom;
            }
            finally
            {
                _syncingDlss = false;
            }
            _dlssLoadWarning = null;
            _dlssNormalizationNote = null;
            _dlssDirty = true;
            UpdateDlssControlState();
            UpdateDlssSummary();
        }

        private void DeactivateSpatialForDlss()
        {
            if (_upscalerEnabled == null) return;
            _syncingUpscaler = true;
            try
            {
                _upscalerEnabled.Checked = false;
            }
            finally
            {
                _syncingUpscaler = false;
            }
            _upscalerLoadWarning = null;
            _upscalerDirty = true;
            UpdateUpscalerSummary();
        }

        private void ApplyUpscalerOutputPreset(double scale)
        {
            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
            {
                MessageBox.Show(this,
                    "Cannot calculate the preset because Bioshock2SP.ini has no valid render resolution.",
                    "VR output preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int outputWidth = renderWidth;
            int outputHeight = renderHeight;
            if (scale > 1.0001 &&
                !UpscalerConfigDocument.TryCalculateProportionalOutput(renderWidth, renderHeight,
                                                                       scale, out outputWidth,
                                                                       out outputHeight))
            {
                MessageBox.Show(this,
                    "Output 10 % larger would exceed the safe limit of 8192 pixels per axis. " +
                    "Lower the game render resolution first.",
                    "VR output preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool changed = (int)_upscalerOutputWidth.Value != outputWidth ||
                           (int)_upscalerOutputHeight.Value != outputHeight;
            _syncingUpscaler = true;
            try
            {
                _upscalerOutputWidth.Value = outputWidth;
                _upscalerOutputHeight.Value = outputHeight;
            }
            finally
            {
                _syncingUpscaler = false;
            }

            if (changed)
                UpscalerChanged(null, EventArgs.Empty);
            else
                UpdateUpscalerSummary();
            SetStatus(scale > 1.0001
                ? "Preset applied: OpenXR output +10 % per axis, preserving aspect ratio."
                : "Preset applied: 1:1 OpenXR output matching the game render.", Blue);
        }

        private void UpdateUpscalerSummary()
        {
            if (_upscalerStateLabel == null || _upscalerEnabled == null ||
                _upscalerOutputWidth == null || _upscalerOutputHeight == null ||
                _upscalerSharpness == null) return;

            if (!string.IsNullOrEmpty(_upscalerLoadWarning))
            {
                _upscalerStateLabel.Text = "CHECK · " + _upscalerLoadWarning;
                _upscalerStateLabel.ForeColor = Color.Firebrick;
                return;
            }

            UpscalerSettings settings = CollectUpscalerSettings();
            string problem;
            if (!TryValidateUpscalerSettings(settings, out problem))
            {
                _upscalerStateLabel.Text = "CHECK · " + problem;
                _upscalerStateLabel.ForeColor = Color.Firebrick;
                return;
            }

            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
            {
                _upscalerStateLabel.Text = (_upscalerDirty ? "PENDING · " : string.Empty) +
                    "OFF · output ready " +
                    settings.OutputWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                    settings.OutputHeight.ToString(CultureInfo.CurrentCulture) +
                    " · no input resolution to compare";
                _upscalerStateLabel.ForeColor = _upscalerDirty ? Warning : Muted;
                return;
            }
            double axisScale = (double)settings.OutputWidth / renderWidth;
            string effect = Math.Abs(axisScale - 1.0) < 0.001
                ? "1:1 filtering, no enlargement"
                : axisScale.ToString("0.00", CultureInfo.CurrentCulture) + "× per axis";
            string prefix = _upscalerDirty ? "PENDING · " :
                (File.Exists(_upscalerConfigPath) ? string.Empty : "FILE MISSING · ");
            _upscalerStateLabel.Text = prefix +
                (settings.Enabled ? "ACTIVE" : "OFF") + " · " +
                renderWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                renderHeight.ToString(CultureInfo.CurrentCulture) + " → " +
                settings.OutputWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                settings.OutputHeight.ToString(CultureInfo.CurrentCulture) + " · " + effect +
                " · sharpness " + settings.Sharpness.ToString("0.00", CultureInfo.CurrentCulture);
            _upscalerStateLabel.ForeColor = _upscalerDirty ? Warning : Blue;
        }

        private bool ValidateUpscalerBeforeSave()
        {
            UpscalerSettings settings = CollectUpscalerSettings();
            string problem;
            if (TryValidateUpscalerSettings(settings, out problem)) return true;
            MessageBox.Show(this,
                "Cannot save the spatial upscaling configuration:\n\n" + problem +
                "\n\nOutput must be at least as large as the game image and preserve its aspect ratio.",
                "Check spatial upscaling", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SelectTab("Image and resolution");
            return false;
        }

        private DlssSettings CollectDlssSettings()
        {
            DlssSettings settings = new DlssSettings();
            settings.Mode = _dlssMode.SelectedIndex == 1 ? DlssMode.Dlaa :
                (_dlssMode.SelectedIndex == 2 ? DlssMode.SuperResolution : DlssMode.Off);
            settings.Runtime = DlssConfigDocument.RequiredRuntime;
            // El host usa selección automática. Se persiste auto
            // para que el backend aplique K/M/L de acuerdo con modo y ratio.
            settings.Preset = "auto";
            settings.Quality = _dlssQuality.SelectedIndex >= 0 &&
                               _dlssQuality.SelectedIndex <= (int)DlssQuality.Custom
                ? (DlssQuality)_dlssQuality.SelectedIndex
                : DlssQuality.Custom;
            settings.OutputWidth = (int)_dlssOutputWidth.Value;
            settings.OutputHeight = (int)_dlssOutputHeight.Value;
            settings.NearPlaneUu = _dlssNearPlane.Value;
            settings.SrScaleNumerator = _srScaleNumerator;
            settings.SrScaleDenominator = _srScaleDenominator;
            settings.SharpnessPercent = (int)_dlssSharpness.Value;
            return settings;
        }

        private bool TryValidateDlssSettings(DlssSettings settings, out string problem)
        {
            problem = null;
            if (settings.SrScaleNumerator <= 0 ||
                settings.SrScaleDenominator <= settings.SrScaleNumerator ||
                settings.SrScaleDenominator > 8192)
            {
                problem = "The DLSS quality preference is invalid. Select a step again.";
                return false;
            }
            if (settings.Runtime != DlssConfigDocument.RequiredRuntime)
            {
                problem = "This edition requires exactly DLSS runtime 310.7.0.";
                return false;
            }
            if (settings.Preset != "auto" && settings.Preset != "K" &&
                settings.Preset != "M" && settings.Preset != "L")
            {
                problem = "The DLSS preset must be Automatic, K, M or L.";
                return false;
            }
            if (settings.NearPlaneUu < 0.1m || settings.NearPlaneUu > 1000.0m)
            {
                problem = "The near plane must be between 0.1 and 1000.0 UU. " +
                          "It must match the projection plane used by the adapter for " + GameProfile.DisplayName + ".";
                return false;
            }
            if (settings.OutputWidth < 1024 || settings.OutputWidth > 8192 ||
                settings.OutputHeight < 1024 || settings.OutputHeight > 8192 ||
                (settings.OutputWidth & 1) != 0 || (settings.OutputHeight & 1) != 0)
            {
                problem = "DLSS output must have even dimensions between 1024 and 8192 pixels.";
                return false;
            }
            if (settings.Mode == DlssMode.Off)
                return true;

            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
            {
                problem = "Cannot validate DLSS because a valid game render resolution is missing.";
                return false;
            }
            if (renderWidth < 1024 || renderHeight < 1024)
            {
                problem = "DLAA/DLSS requires at least 1024 render pixels per axis. Increase resolution; Normal allows the current value.";
                return false;
            }
            if (!UpscalerConfigDocument.HasExactAspect(renderWidth, renderHeight,
                                                        settings.OutputWidth,
                                                        settings.OutputHeight))
            {
                problem = "DLSS output must exactly preserve the aspect ratio of " +
                    renderWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                    renderHeight.ToString(CultureInfo.CurrentCulture) + ".";
                return false;
            }

            if (settings.Mode == DlssMode.Dlaa)
            {
                if (settings.OutputWidth != renderWidth || settings.OutputHeight != renderHeight)
                {
                    problem = "DLAA processes at native resolution: input and output must match.";
                    return false;
                }
                return true;
            }

            if (settings.OutputWidth <= renderWidth || settings.OutputHeight <= renderHeight)
            {
                problem = "DLSS Super Resolution requires output larger than the render resolution. " +
                          "Select DLAA for 1:1 processing.";
                return false;
            }
            DlssQuality detected;
            bool canonical = DlssQualityPolicy.TryMatchCanonical(
                renderWidth, renderHeight, settings.OutputWidth, settings.OutputHeight,
                out detected);
            if ((canonical && settings.Quality != detected) ||
                (!canonical && settings.Quality != DlssQuality.Custom))
            {
                problem = canonical
                    ? "The SR quality selector does not match the detected standard internal resolution."
                    : "The render-to-output ratio does not match a standard step and must be shown as Custom / AUTO.";
                return false;
            }
            return true;
        }

        private static string EffectiveDlssPreset(DlssSettings settings)
        {
            if (settings.Preset != "auto") return settings.Preset;
            if (settings.Mode == DlssMode.Dlaa) return "K";
            if (settings.Quality == DlssQuality.Custom) return "AUTO based on ratio/NGX";
            if (settings.Quality == DlssQuality.Quality ||
                settings.Quality == DlssQuality.Balanced ||
                settings.Quality == DlssQuality.Percent60 ||
                settings.Quality == DlssQuality.Percent70 ||
                settings.Quality == DlssQuality.Percent80 ||
                settings.Quality == DlssQuality.Percent90) return "K";
            if (settings.Quality == DlssQuality.Performance) return "M";
            return settings.Quality == DlssQuality.UltraPerformance ? "L" : "AUTO based on ratio/NGX";
        }

        private static string DlssQualityName(DlssQuality quality)
        {
            if (quality == DlssQuality.Balanced) return "Balanced";
            if (quality == DlssQuality.Performance) return "Performance";
            if (quality == DlssQuality.UltraPerformance) return "Ultra performance";
            if (quality == DlssQuality.Custom) return "Custom / AUTO";
            if (quality == DlssQuality.Percent40) return "40 %";
            if (quality == DlssQuality.Percent60) return "60 %";
            if (quality == DlssQuality.Percent70) return "70 %";
            if (quality == DlssQuality.Percent80) return "80 %";
            if (quality == DlssQuality.Percent90) return "90 %";
            return "Quality";
        }

        private void LoadDlssConfiguration()
        {
            if (_dlssMode == null) return;
            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
            {
                renderWidth = 2048;
                renderHeight = 2048;
            }

            _syncingDlss = true;
            try
            {
                bool exists = File.Exists(_dlssConfigPath);
                _dlssLoadedContent = exists
                    ? File.ReadAllText(_dlssConfigPath, Encoding.UTF8)
                    : string.Empty;
                DlssSettings settings;
                string warning;
                bool valid = DlssConfigDocument.TryParse(_dlssLoadedContent,
                    renderWidth, renderHeight, out settings, out warning);

                _dlssMode.Enabled = true;
                _dlssPreset.Enabled = true;
                _dlssQuality.Enabled = true;
                _dlssOutputWidth.Enabled = true;
                _dlssOutputHeight.Enabled = true;
                _dlssNearPlane.Enabled = false;
                _dlssMode.SelectedIndex = settings.Mode == DlssMode.Dlaa ? 1 :
                    (settings.Mode == DlssMode.SuperResolution ? 2 : 0);
                _dlssPreset.SelectedIndex = 0;
                _dlssQuality.SelectedIndex = (int)settings.Quality;
                _dlssOutputWidth.Value = settings.OutputWidth;
                _dlssOutputHeight.Value = settings.OutputHeight;
                _dlssNearPlane.Value = settings.NearPlaneUu;
                _dlssSharpness.Value = settings.SharpnessPercent;
                _srScaleNumerator = settings.SrScaleNumerator;
                _srScaleDenominator = settings.SrScaleDenominator;
                _previousDlssModeIndex = _dlssMode.SelectedIndex;
                _previousDlssOutputWidth = settings.OutputWidth;
                _previousDlssOutputHeight = settings.OutputHeight;
                SynchronizeOutputPreset();
                _dlssDirty = false;
                List<string> ignoredStoredValues = new List<string>();
                if (!string.Equals(settings.Preset, "auto", StringComparison.OrdinalIgnoreCase))
                    ignoredStoredValues.Add("manual preset " + settings.Preset + " saved but inactive");
                _dlssNormalizationNote = ignoredStoredValues.Count == 0 ? null :
                    string.Join("; ", ignoredStoredValues.ToArray()) +
                    ". The file stays unchanged until you change a setting and click Save.";
                _dlssLoadWarning = valid ? null :
                    "dlss.ini contains incomplete or invalid values: " + warning;
            }
            catch (Exception ex)
            {
                _dlssLoadedContent = string.Empty;
                _dlssDirty = false;
                _dlssNormalizationNote = null;
                _dlssLoadWarning = "Could not read dlss.ini: " + ex.Message;
                _dlssMode.Enabled = false;
                _dlssPreset.Enabled = false;
                _dlssQuality.Enabled = false;
                _dlssOutputWidth.Enabled = false;
                _dlssOutputHeight.Enabled = false;
                _dlssNearPlane.Enabled = false;
                _dlssSharpness.Enabled = false;
            }
            finally
            {
                _syncingDlss = false;
            }
            SynchronizeDlssQualityFromResolution();
            if (!FinalDlssEdition && _dlssMode.SelectedIndex > 0 && _upscalerEnabled != null &&
                _upscalerEnabled.Checked)
            {
                // Una configuración antigua puede contener ambos métodos activos.
                // DLSS tiene prioridad visible y deja pendiente persistir
                // la exclusión en los dos archivos.
                DeactivateSpatialForDlss();
                _dlssDirty = true;
            }
            _dlssBackendStatus = DetectDlssBackend();
            UpdateDlssControlState();
            UpdateDlssSummary();
        }

        private void DlssChanged(object sender, EventArgs e)
        {
            if (_loading || _syncingDlss) return;
            _dlssLoadWarning = null;
            _dlssNormalizationNote = null;
            bool modeSelection = object.ReferenceEquals(sender, _dlssMode);
            bool qualitySelection = object.ReferenceEquals(sender, _dlssQuality);
            bool outputSelection = object.ReferenceEquals(sender, _dlssOutputWidth) ||
                object.ReferenceEquals(sender, _dlssOutputHeight) ||
                object.ReferenceEquals(sender, _dlssOutputPreset);
            if (!FinalDlssEdition && modeSelection && _dlssMode.SelectedIndex > 0)
                DeactivateSpatialForDlss();

            if (FinalDlssEdition && (outputSelection || modeSelection))
            {
                _syncingDlss = true;
                _syncingResolution = true;
                _dlssOutputHeight.Value = _dlssOutputWidth.Value;
                _squareResolution.Checked = true;
                _syncingResolution = false;
                _syncingDlss = false;
            }
            if (outputSelection && _squareResolution.Checked &&
                !object.ReferenceEquals(sender, _dlssOutputPreset))
            {
                _syncingDlss = true;
                if (object.ReferenceEquals(sender, _dlssOutputHeight))
                    _dlssOutputWidth.Value = _dlssOutputHeight.Value;
                else
                    _dlssOutputHeight.Value = _dlssOutputWidth.Value;
                _syncingDlss = false;
            }

            string qualityStatus = null;
            if (qualitySelection && _dlssMode.SelectedIndex == 2)
            {
                DlssQuality selectedQuality = _dlssQuality.SelectedIndex >= 0 &&
                                              _dlssQuality.SelectedIndex < (int)DlssQuality.Custom
                    ? (DlssQuality)_dlssQuality.SelectedIndex
                    : DlssQuality.Custom;
                if (selectedQuality == DlssQuality.Custom)
                {
                    SynchronizeDlssQualityFromResolution();
                    qualityStatus = "Custom / AUTO appears automatically when the render does not match a standard step; resolutions remain editable.";
                }
                else
                {
                    int renderWidth, renderHeight;
                    if (TryApplyDlssQualityToRender(selectedQuality,
                                                    out renderWidth, out renderHeight))
                        qualityStatus = "Step " + DlssQualityName(selectedQuality) +
                            " applied in memory: render " +
                            renderWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                            renderHeight.ToString(CultureInfo.CurrentCulture) +
                            "; DLSS output has not changed. Click Save to write Shared.ini and its SP mirror.";
                    else
                        SynchronizeDlssQualityFromResolution();
                }
            }
            else if (modeSelection || outputSelection)
            {
                int renderWidth, renderHeight;
                if (!TryApplyOutputAndScaleToRender(_srScaleNumerator, _srScaleDenominator,
                        out renderWidth, out renderHeight))
                {
                    _syncingDlss = true;
                    if (modeSelection) _dlssMode.SelectedIndex = _previousDlssModeIndex;
                    _dlssOutputWidth.Value = _previousDlssOutputWidth;
                    _dlssOutputHeight.Value = _previousDlssOutputHeight;
                    _syncingDlss = false;
                }
                _previousDlssModeIndex = _dlssMode.SelectedIndex;
                SynchronizeDlssQualityFromResolution();
            }
            else if (!object.ReferenceEquals(sender, _dlssSharpness))
            {
                SynchronizeDlssQualityFromResolution();
            }

            // quality=auto sigue siendo del host; la fracción recuerda aparte
            // la preferencia SR, también cuando se guarda Normal o DLAA.
            _dlssDirty = true;
            _previousDlssOutputWidth = (int)_dlssOutputWidth.Value;
            _previousDlssOutputHeight = (int)_dlssOutputHeight.Value;
            SynchronizeOutputPreset();
            UpdateDlssControlState();
            UpdateDlssSummary();
            UpdateDirtyState();
            if (!string.IsNullOrEmpty(qualityStatus))
                SetStatus(qualityStatus, Blue);
        }

        private bool TryApplyDlssQualityToRender(DlssQuality quality,
                                                 out int renderWidth,
                                                 out int renderHeight)
        {
            int numerator, denominator;
            if (!DlssQualityPolicy.TryGetFraction(quality, out numerator, out denominator))
            {
                renderWidth = renderHeight = 0;
                return false;
            }
            if (!TryApplyOutputAndScaleToRender(numerator, denominator,
                                               out renderWidth, out renderHeight))
                return false;
            _srScaleNumerator = numerator;
            _srScaleDenominator = denominator;
            return true;
        }

        private bool TryApplyOutputAndScaleToRender(int numerator, int denominator,
                                                     out int renderWidth,
                                                     out int renderHeight)
        {
            renderWidth = 0;
            renderHeight = 0;
            if (_resolutionWidth == null || _resolutionHeight == null ||
                !_resolutionWidth.Enabled || !_resolutionHeight.Enabled ||
                FindIniEntry(GameProfile.IsBioShock2 ? "SharedOptions" : "WinDrv.WindowsClient",
                    GameProfile.IsBioShock2 ? "ViewportX" : "WindowedViewportX") == null ||
                FindIniEntry(GameProfile.IsBioShock2 ? "SharedOptions" : "WinDrv.WindowsClient",
                    GameProfile.IsBioShock2 ? "ViewportY" : "WindowedViewportY") == null)
            {
                MessageBox.Show(this,
                    "Cannot apply this step because the unique ViewportX / ViewportY pair is missing from Shared.ini.",
                    "DLSS SR quality", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            int outputWidth = (int)_dlssOutputWidth.Value;
            int outputHeight = (int)_dlssOutputHeight.Value;
            renderWidth = outputWidth;
            renderHeight = outputHeight;
            if (_dlssMode.SelectedIndex == 2 &&
                !DlssQualityPolicy.TryCalculateRender(outputWidth, outputHeight,
                    numerator, denominator, out renderWidth, out renderHeight))
            {
                MessageBox.Show(this,
                    "Cannot obtain an even internal resolution between 1024 and 8192 pixels " +
                    "that exactly preserves the aspect ratio of output " +
                    outputWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                    outputHeight.ToString(CultureInfo.CurrentCulture) +
                    " for that step. Check DLSS output first.",
                    "DLSS SR quality", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _syncingResolution = true;
            _syncingDlss = true;
            try
            {
                _resolutionWidth.Value = renderWidth;
                _resolutionHeight.Value = renderHeight;
                _squareResolution.Checked = renderWidth == renderHeight;
                _resolutionPreset.SelectedIndex = ResolutionPresetIndex(renderWidth, renderHeight);
                // Solo edita memoria; Guardar conserva el respaldo y la comprobación externa.
                ApplyResolutionControlsToEntries();
            }
            finally
            {
                _syncingResolution = false;
                _syncingDlss = false;
            }
            return true;
        }

        private void DlssOutputPresetChanged(object sender, EventArgs e)
        {
            if (_loading || _syncingDlss || _dlssOutputPreset.SelectedIndex <= 0) return;
            int size = SquareResolutionSteps[_dlssOutputPreset.SelectedIndex - 1];
            _syncingDlss = true;
            _dlssOutputWidth.Value = size;
            _dlssOutputHeight.Value = size;
            _syncingDlss = false;
            DlssChanged(_dlssOutputPreset, EventArgs.Empty);
        }

        private void SynchronizeOutputPreset()
        {
            if (_dlssOutputPreset == null) return;
            bool previous = _syncingDlss;
            _syncingDlss = true;
            int selected = 0;
            if (_dlssOutputWidth.Value == _dlssOutputHeight.Value)
                for (int i = 0; i < SquareResolutionSteps.Length; i++)
                    if (SquareResolutionSteps[i] == (int)_dlssOutputWidth.Value) selected = i + 1;
            _dlssOutputPreset.SelectedIndex = selected;
            _syncingDlss = previous;
        }

        private void SynchronizeDlssOutputForDlaa(bool markDirty)
        {
            if (_dlssMode == null || _dlssMode.SelectedIndex == 2) return;
            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight)) return;
            if (renderWidth < _dlssOutputWidth.Minimum || renderHeight < _dlssOutputHeight.Minimum ||
                renderWidth > _dlssOutputWidth.Maximum || renderHeight > _dlssOutputHeight.Maximum) return;
            if ((int)_dlssOutputWidth.Value == renderWidth &&
                (int)_dlssOutputHeight.Value == renderHeight) return;

            _syncingDlss = true;
            try
            {
                _dlssOutputWidth.Value = renderWidth;
                _dlssOutputHeight.Value = renderHeight;
            }
            finally
            {
                _syncingDlss = false;
            }
            if (markDirty && !_loading)
                _dlssDirty = true;
            _previousDlssOutputWidth = renderWidth;
            _previousDlssOutputHeight = renderHeight;
            SynchronizeOutputPreset();
        }

        private void SynchronizeDlssQualityFromResolution()
        {
            if (_dlssMode == null || _dlssQuality == null || _dlssPreset == null ||
                _dlssOutputWidth == null || _dlssOutputHeight == null) return;
            int renderWidth, renderHeight;
            DlssQuality desiredQuality = DlssQuality.Custom;
            DlssQuality matched;
            if (_dlssMode.SelectedIndex == 2 &&
                TryGetRenderDimensions(out renderWidth, out renderHeight) &&
                DlssQualityPolicy.TryMatchCanonical(
                    renderWidth, renderHeight,
                    (int)_dlssOutputWidth.Value, (int)_dlssOutputHeight.Value,
                    out matched))
                desiredQuality = matched;
            else if (_dlssMode.SelectedIndex != 2)
            {
                foreach (DlssQuality candidate in Enum.GetValues(typeof(DlssQuality)))
                {
                    int numerator, denominator;
                    if (DlssQualityPolicy.TryGetFraction(candidate, out numerator, out denominator) &&
                        (long)numerator * _srScaleDenominator == (long)denominator * _srScaleNumerator)
                        desiredQuality = candidate;
                }
            }
            _syncingDlss = true;
            try
            {
                _dlssPreset.SelectedIndex = 0;
                _dlssQuality.SelectedIndex = (int)desiredQuality;
            }
            finally
            {
                _syncingDlss = false;
            }
        }

        private void UpdateDlssControlState()
        {
            if (_dlssMode == null || !_dlssMode.Enabled) return;
            bool active = _dlssMode.SelectedIndex > 0;
            bool superResolution = _dlssMode.SelectedIndex == 2;
            // El preset sigue automático; la calidad SR sí aplica ratios de render.
            _dlssPreset.Enabled = false;
            _dlssQuality.Enabled = superResolution;
            _dlssSharpness.Enabled = superResolution;
            _dlssOutputWidth.Enabled = true;
            _dlssOutputHeight.Enabled = true;
            _dlssOutputPreset.Enabled = true;
            _dlssNearPlane.Enabled = active;
        }

        private void UpdateDlssSummary()
        {
            RefreshSimpleImage();
            if (_dlssSettingsStateLabel == null || _dlssBackendStateLabel == null ||
                _dlssMode == null || _dlssNearPlane == null ||
                _dlssMode.SelectedIndex < 0) return;

            if (_dlssBackendStatus == null)
                _dlssBackendStatus = DetectDlssBackend();
            _dlssBackendStateLabel.Text = _dlssBackendStatus.Summary;
            _dlssBackendStateLabel.ForeColor =
                _dlssBackendStatus.Ready && _dlssBackendStatus.RuntimeMatches ? Success : Warning;

            if (!string.IsNullOrEmpty(_dlssLoadWarning))
            {
                _dlssSettingsStateLabel.Text = "CHECK · " + _dlssLoadWarning;
                _dlssSettingsStateLabel.ForeColor = Color.Firebrick;
                return;
            }

            DlssSettings settings = CollectDlssSettings();
            string problem;
            if (!TryValidateDlssSettings(settings, out problem))
            {
                _dlssSettingsStateLabel.Text = "CHECK · " + problem;
                _dlssSettingsStateLabel.ForeColor = Color.Firebrick;
                return;
            }

            string prefix = _dlssDirty ? "PENDING · " :
                (File.Exists(_dlssConfigPath) ? string.Empty : "FILE MISSING · ");
            if (settings.Mode == DlssMode.Off)
            {
                _dlssSettingsStateLabel.Text = prefix +
                    "OFF · native rendering, without running DLSS." +
                    (string.IsNullOrEmpty(_dlssNormalizationNote) ? string.Empty :
                        " NOTE · " + _dlssNormalizationNote);
                _dlssSettingsStateLabel.ForeColor = _dlssDirty ? Warning : Muted;
                return;
            }

            int renderWidth, renderHeight;
            TryGetRenderDimensions(out renderWidth, out renderHeight);
            string mode;
            if (settings.Mode == DlssMode.Dlaa)
                mode = "DLAA 4.5 · 1:1 · SR quality ignored";
            else if (settings.Quality == DlssQuality.Custom)
                mode = "DLSS 4.5 SR · Custom / AUTO";
            else
                mode = "DLSS 4.5 SR · step " + DlssQualityName(settings.Quality);
            double inputPercent = settings.OutputWidth > 0
                ? 100.0 * renderWidth / settings.OutputWidth
                : 0.0;
            _dlssSettingsStateLabel.Text = prefix + mode + " · " +
                renderWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                renderHeight.ToString(CultureInfo.CurrentCulture) + " → " +
                settings.OutputWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                settings.OutputHeight.ToString(CultureInfo.CurrentCulture) +
                (settings.Mode == DlssMode.SuperResolution
                    ? " · input " + inputPercent.ToString("0.0", CultureInfo.CurrentCulture) + "%"
                    : string.Empty) +
                " · preset auto→" + EffectiveDlssPreset(settings) +
                (settings.Mode == DlssMode.SuperResolution &&
                 settings.Quality == DlssQuality.Custom
                    ? " · NGX will choose based on the actual ratio"
                    : string.Empty) +
                " · projection captured per eye" +
                (_dlssBackendStatus.Ready ? " · backend ready" :
                    " · will be saved, but the backend is not operational yet") +
                (string.IsNullOrEmpty(_dlssNormalizationNote) ? string.Empty :
                    " · NOTE: " + _dlssNormalizationNote);
            _dlssSettingsStateLabel.ForeColor = _dlssDirty ? Warning :
                (_dlssBackendStatus.Ready && _dlssBackendStatus.RuntimeMatches ? Success : Blue);
        }

        private bool ValidateDlssBeforeSave()
        {
            DlssSettings settings = CollectDlssSettings();
            string problem;
            if (TryValidateDlssSettings(settings, out problem)) return true;
            MessageBox.Show(this,
                "Cannot save the DLSS 4.5 configuration:\n\n" + problem +
                "\n\nDLAA requires matching input/output. DLSS SR requires larger output with the same aspect ratio.",
                "Check DLSS 4.5", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SelectTab("Image and resolution");
            return false;
        }

        private static bool Is64BitPe(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) return false;
                    stream.Position = 0x3C;
                    int peOffset = reader.ReadInt32();
                    if (peOffset < 0 || peOffset + 6 > stream.Length) return false;
                    stream.Position = peOffset;
                    if (reader.ReadUInt32() != 0x00004550) return false;
                    return reader.ReadUInt16() == 0x8664;
                }
            }
            catch { return false; }
        }

        private static string FirstExistingFile(params string[] paths)
        {
            foreach (string path in paths)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
            return null;
        }

        private DlssBackendStatus DetectDlssBackend()
        {
            DlssBackendStatus status = new DlssBackendStatus();
            status.Summary = "BACKEND UNCONFIRMED · could not check host64.";
            try
            {
                List<string> candidates = new List<string>();
                if (!string.IsNullOrEmpty(_gameExePath))
                    candidates.Add(Path.Combine(Path.GetDirectoryName(_gameExePath), "host64"));

                string selected = null;
                int bestScore = -1;
                foreach (string candidate in candidates)
                {
                    int score = Directory.Exists(candidate) ? 1 : 0;
                    if (Directory.Exists(candidate))
                    {
                        bool directHost = File.Exists(Path.Combine(
                            candidate, "BioShockVR-DLSS45-Host64.exe"));
                        bool eyePair =
                            (File.Exists(Path.Combine(candidate, "eye0", "BioShockVR-DLSS45-Host64.exe")) &&
                             File.Exists(Path.Combine(candidate, "eye1", "BioShockVR-DLSS45-Host64.exe"))) ||
                            (File.Exists(Path.Combine(candidate, "left", "BioShockVR-DLSS45-Host64.exe")) &&
                             File.Exists(Path.Combine(candidate, "right", "BioShockVR-DLSS45-Host64.exe")));
                        if (directHost || eyePair) score += 4;
                        if (File.Exists(Path.Combine(candidate, "nvngx_dlss.dll"))) score += 2;
                        if (File.Exists(Path.Combine(candidate, "dlss-capabilities.ini"))) score += 3;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        selected = candidate;
                    }
                }

                status.HostDirectory = selected;
                if (string.IsNullOrEmpty(selected) || !Directory.Exists(selected))
                {
                    status.Summary = "BACKEND NOT INSTALLED · the host64 directory is missing beside the game or launcher.";
                    return status;
                }

                string[] forbiddenPhaseFiles = new string[] {
                    "nvngx_dlssnr.dll",
                    "renodx-dlss5.addon64",
                    "dlss5-feed.addon64",
                    "dxgi.dll",
                    "ReShade64.dll"
                };
                List<string> foundForbidden = new List<string>();
                foreach (string forbidden in forbiddenPhaseFiles)
                {
                    if (File.Exists(Path.Combine(selected, forbidden)))
                        foundForbidden.Add(forbidden);
                }
                if (foundForbidden.Count > 0)
                {
                    status.Summary = "BACKEND NOT READY · host64 contains components outside the clean DLSS 4.5 setup: " +
                        string.Join(", ", foundForbidden.ToArray()) + ".";
                    return status;
                }

                string sharedCandidate = Path.Combine(
                    selected, "BioShockVR-DLSS45-Host64.exe");
                string sharedHost = File.Exists(sharedCandidate) ? sharedCandidate : null;
                string eyeZeroHost = null;
                string eyeOneHost = null;
                string eyeZeroCandidate = Path.Combine(
                    selected, "eye0", "BioShockVR-DLSS45-Host64.exe");
                string eyeOneCandidate = Path.Combine(
                    selected, "eye1", "BioShockVR-DLSS45-Host64.exe");
                if (File.Exists(eyeZeroCandidate) && File.Exists(eyeOneCandidate))
                {
                    eyeZeroHost = eyeZeroCandidate;
                    eyeOneHost = eyeOneCandidate;
                }
                else
                {
                    string leftCandidate = Path.Combine(
                        selected, "left", "BioShockVR-DLSS45-Host64.exe");
                    string rightCandidate = Path.Combine(
                        selected, "right", "BioShockVR-DLSS45-Host64.exe");
                    if (File.Exists(leftCandidate) && File.Exists(rightCandidate))
                    {
                        eyeZeroHost = leftCandidate;
                        eyeOneHost = rightCandidate;
                    }
                }
                status.HostFound = sharedHost != null || (eyeZeroHost != null && eyeOneHost != null);

                string runtimePath = FirstExistingFile(
                    Path.Combine(selected, "nvngx_dlss.dll"));
                status.RuntimeFound = runtimePath != null;
                if (runtimePath != null)
                {
                    FileVersionInfo version = FileVersionInfo.GetVersionInfo(runtimePath);
                    status.RuntimeVersion = version.FileMajorPart.ToString(CultureInfo.InvariantCulture) + "." +
                        version.FileMinorPart.ToString(CultureInfo.InvariantCulture) + "." +
                        version.FileBuildPart.ToString(CultureInfo.InvariantCulture) + "." +
                        version.FilePrivatePart.ToString(CultureInfo.InvariantCulture);
                    status.RuntimeIs64Bit = Is64BitPe(runtimePath);
                    status.RuntimeMatches = status.RuntimeIs64Bit &&
                        string.Equals(status.RuntimeVersion, DlssConfigDocument.TestedRuntimeDisplay,
                                      StringComparison.OrdinalIgnoreCase);
                }

                string capabilityPath = Path.Combine(selected, "dlss-capabilities.ini");
                status.CapabilityFound = File.Exists(capabilityPath);
                bool capabilityValid = false;
                int declaredEyes = 0;
                if (status.CapabilityFound)
                {
                    Dictionary<string, string> capability =
                        ConfigDocument.Parse(File.ReadAllText(capabilityPath, Encoding.UTF8));
                    string phase, eyes, runtime, game, adapter;
                    capability.TryGetValue("phase", out phase);
                    capability.TryGetValue("eyeHosts", out eyes);
                    capability.TryGetValue("runtime", out runtime);
                    game = null;
                    adapter = null;
                    foreach (IniEntry entry in GameIniDocument.Parse(File.ReadAllText(capabilityPath, Encoding.UTF8)))
                    {
                        if (!string.Equals(entry.Section, "backend", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(entry.Key, "game", StringComparison.OrdinalIgnoreCase)) game = entry.Value;
                        if (string.Equals(entry.Key, "adapter", StringComparison.OrdinalIgnoreCase)) adapter = entry.Value;
                    }
                    int.TryParse(eyes, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                 out declaredEyes);
                    capabilityValid = string.Equals(game, "bs2", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(adapter, "bioshock2r", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(phase, "DLSS45", StringComparison.OrdinalIgnoreCase) &&
                        declaredEyes == 2 &&
                        string.Equals(runtime, DlssConfigDocument.RequiredRuntime,
                                      StringComparison.OrdinalIgnoreCase);
                }

                bool splitHosts = eyeZeroHost != null && eyeOneHost != null &&
                                  Is64BitPe(eyeZeroHost) && Is64BitPe(eyeOneHost);
                bool reusableHost = sharedHost != null && Is64BitPe(sharedHost);
                status.EyeHosts = splitHosts ? 2 : (reusableHost ? declaredEyes : 0);
                status.Ready = status.HostFound && (splitHosts || reusableHost) &&
                    status.EyeHosts >= 2 && status.RuntimeFound && status.RuntimeIs64Bit &&
                    status.CapabilityFound && capabilityValid;

                if (!status.HostFound)
                    status.Summary = "INCOMPLETE BACKEND · missing host64\\BioShockVR-DLSS45-Host64.exe.";
                else if (!splitHosts && !reusableHost)
                    status.Summary = "INVALID BACKEND · the host found is not an x64 executable.";
                else if (!status.RuntimeFound)
                    status.Summary = "INCOMPLETE BACKEND · missing host64\\nvngx_dlss.dll 310.7.0.";
                else if (!status.RuntimeIs64Bit)
                    status.Summary = "INCOMPATIBLE BACKEND · nvngx_dlss.dll must be x64.";
                else if (!status.CapabilityFound)
                    status.Summary = "INCOMPLETE BACKEND · dlss-capabilities.ini is missing; stereo integration is unconfirmed.";
                else if (!capabilityValid || status.EyeHosts < 2)
                    status.Summary = "INVALID BACKEND · the manifest must declare phase=DLSS45, eyeHosts=2 and runtime=310.7.0.";
                else if (!status.RuntimeMatches)
                    status.Summary = "BACKEND READY WITH WARNING · nvngx_dlss.dll x64 " +
                        (string.IsNullOrEmpty(status.RuntimeVersion) ? "with an unidentified version" :
                            status.RuntimeVersion) +
                        "; only 310.7.0.0 has been tested.";
                else
                    status.Summary = "BACKEND READY · x64 host · 2 eyes · tested nvngx_dlss.dll 310.7.0.0.";
            }
            catch (Exception ex)
            {
                status.Ready = false;
                status.Summary = "BACKEND UNCONFIRMED · " + ex.Message;
            }
            return status;
        }

        private void WarnAboutUntestedRuntime()
        {
            if (_runtimeWarningShown || _dlssBackendStatus == null ||
                !_dlssBackendStatus.Ready || _dlssBackendStatus.RuntimeMatches)
                return;
            _runtimeWarningShown = true;
            MessageBox.Show(this,
                "A different nvngx_dlss.dll version was detected: " +
                (string.IsNullOrEmpty(_dlssBackendStatus.RuntimeVersion)
                    ? "unidentified" : _dlssBackendStatus.RuntimeVersion) + ".\r\n\r\n" +
                "The installer includes, and this integration was tested with, NVIDIA DLSS " +
                DlssConfigDocument.TestedRuntimeDisplay + ". You may continue, but other " +
                "versions are not guaranteed to work or provide the same stability or image quality. " +
                "Replacing it is at your own risk.\r\n\r\n" +
                "Reinstalling this version restores the tested DLL.",
                "Untested NVIDIA DLL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void PopulateIniFilters()
        {
            string selected = _iniSectionFilter.SelectedItem as string;
            SortedSet<string> sections = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (IniEntry entry in _gameIniEntries)
                if (!FinalDlssEdition || !IsFxaaEntry(entry)) sections.Add(entry.Section);
            _iniSectionFilter.Items.Clear();
            _iniSectionFilter.Items.Add("All sections");
            foreach (string section in sections) _iniSectionFilter.Items.Add(section);
            int index = selected == null ? -1 : _iniSectionFilter.Items.IndexOf(selected);
            _iniSectionFilter.SelectedIndex = index >= 0 ? index : 0;
            if (_iniImpactFilter.SelectedIndex < 0) _iniImpactFilter.SelectedIndex = 0;
        }

        private void RebuildIniGrid()
        {
            if (_iniGrid == null || _rebuildingIniGrid) return;
            _rebuildingIniGrid = true;
            try
            {
                _iniGrid.Rows.Clear();
                string sectionFilter = _iniSectionFilter.SelectedIndex > 0
                    ? _iniSectionFilter.SelectedItem as string : null;
                string search = _iniSearch == null ? string.Empty : _iniSearch.Text.Trim();
                int impact = _iniImpactFilter == null ? 0 : _iniImpactFilter.SelectedIndex;
                int shown = 0;
                int available = 0;
                foreach (IniEntry candidate in _gameIniEntries)
                    if (!FinalDlssEdition || !IsFxaaEntry(candidate)) available++;
                foreach (IniEntry entry in _gameIniEntries)
                {
                    if (FinalDlssEdition && IsFxaaEntry(entry)) continue;
                    if (sectionFilter != null && entry.Section != sectionFilter) continue;
                    if (impact == 1 && entry.Impact != IniImpact.VrDirect) continue;
                    if (impact == 2 && entry.Impact != IniImpact.Performance) continue;
                    if (impact == 3 && entry.Impact != IniImpact.Warning) continue;
                    if (impact == 4 && !entry.Changed) continue;
                    if (search.Length > 0 &&
                        entry.Key.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                        entry.Section.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                        entry.FriendlyName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                        entry.Description.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                        entry.Value.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) < 0)
                        continue;

                    string displayName = entry.FriendlyName;
                    if (entry.OccurrenceCount > 1)
                        displayName += "  (item " + entry.Occurrence + " of " + entry.OccurrenceCount + ")";
                    int rowIndex = _iniGrid.Rows.Add(IniKnowledge.ImpactText(entry.Impact), displayName,
                                                     entry.Value, entry.Key, entry.Section);
                    DataGridViewRow row = _iniGrid.Rows[rowIndex];
                    row.Tag = entry;
                    bool protectedEntry = entry.Protected &&
                                           (_allowRiskyEdits == null || !_allowRiskyEdits.Checked);
                    row.Cells["Value"].ReadOnly = protectedEntry || IsPcResolutionEntry(entry);
                    ApplyIniRowAppearance(row, entry);
                    shown++;
                }
                _iniCountLabel.Text = shown + " / " + available;
            }
            finally
            {
                _rebuildingIniGrid = false;
            }
            UpdateIniDetail();
        }

        private static void ApplyIniRowAppearance(DataGridViewRow row, IniEntry entry)
        {
            row.DefaultCellStyle.BackColor = Color.White;
            if (entry.Changed)
                row.DefaultCellStyle.BackColor = Color.FromArgb(225, 246, 231);
            else if (entry.Impact == IniImpact.VrDirect)
                row.DefaultCellStyle.BackColor = Color.FromArgb(224, 240, 255);
            else if (entry.Impact == IniImpact.Performance)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
            else if (entry.Impact == IniImpact.Warning)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 231, 231);
        }

        private void IniGridCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.RowIndex < 0 || _iniGrid.Columns[e.ColumnIndex].Name != "Value") return;
            IniEntry entry = _iniGrid.Rows[e.RowIndex].Tag as IniEntry;
            if (IsPcResolutionEntry(entry))
            {
                e.Cancel = true;
                SetStatus("Resolution is edited safely and consistently in the Image tab.", Warning);
            }
            else if (entry != null && entry.Protected && !_allowRiskyEdits.Checked)
            {
                e.Cancel = true;
                SetStatus("This internal setting is protected. Enable 'Unlock internal settings' to edit it.", Warning);
            }
        }

        private void IniGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_rebuildingIniGrid || _loading || e.RowIndex < 0 ||
                _iniGrid.Columns[e.ColumnIndex].Name != "Value") return;
            IniEntry entry = _iniGrid.Rows[e.RowIndex].Tag as IniEntry;
            if (entry == null) return;
            string value = Convert.ToString(_iniGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value,
                                            CultureInfo.CurrentCulture) ?? string.Empty;
            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
            {
                _iniGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = entry.Value;
                SetStatus("An INI value cannot contain line breaks.", Color.Firebrick);
                return;
            }
            entry.Value = value;
            if (IsFxaaEntry(entry))
                LoadFxaaControl();
            RecalculateGameIniDirty();
            bool membershipCanChange = (_iniImpactFilter != null && _iniImpactFilter.SelectedIndex == 4) ||
                                       (_iniSearch != null && _iniSearch.Text.Trim().Length > 0);
            if (membershipCanChange)
                RebuildIniGrid();
            else
            {
                ApplyIniRowAppearance(_iniGrid.Rows[e.RowIndex], entry);
                UpdateIniDetail();
            }
        }

        private void RestoreSelectedIniEntry()
        {
            IniEntry entry = SelectedIniEntry();
            if (entry == null) return;
            if (IsPcResolutionEntry(entry))
            {
                IniEntry wx = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportX");
                IniEntry wy = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportY");
                int w, h;
                if (wx != null && wy != null &&
                    int.TryParse(wx.OriginalValue, out w) && int.TryParse(wy.OriginalValue, out h))
                {
                    _syncingResolution = true;
                    _resolutionWidth.Value = w;
                    _resolutionHeight.Value = h;
                    _resolutionPreset.SelectedIndex = ResolutionPresetIndex(w, h);
                    _syncingResolution = false;
                    ApplyResolutionControlsToEntries();
                }
                return;
            }
            entry.Value = entry.OriginalValue;
            if (IsFxaaEntry(entry))
                LoadFxaaControl();
            RecalculateGameIniDirty();
            RebuildIniGrid();
        }

        private IniEntry SelectedIniEntry()
        {
            if (_iniGrid == null || _iniGrid.SelectedRows.Count == 0) return null;
            return _iniGrid.SelectedRows[0].Tag as IniEntry;
        }

        private void UpdateIniDetail()
        {
            if (_iniDetail == null) return;
            IniEntry entry = SelectedIniEntry();
            if (entry == null)
            {
                _iniDetail.Text = "Select a row to see what it does and whether it may affect VR.";
                return;
            }
            string repeated = entry.OccurrenceCount > 1
                ? " · item " + entry.Occurrence + " of " + entry.OccurrenceCount : string.Empty;
            string protection = entry.Protected
                ? " · PROTECTED BY DEFAULT" : string.Empty;
            if (IsPcResolutionEntry(entry)) protection += " · edit in Image";
            _iniDetail.Text = IniKnowledge.ImpactText(entry.Impact) + protection + repeated +
                Environment.NewLine + "[" + entry.Section + "]  " + entry.Key +
                Environment.NewLine + entry.Description;
        }

        private void RecalculateGameIniDirty()
        {
            _gameIniDirty = false;
            foreach (IniEntry entry in _gameIniEntries)
                if (entry.Changed) { _gameIniDirty = true; break; }
            foreach (IniEntry entry in _sharedIniEntries)
                if (entry.Changed) { _gameIniDirty = true; break; }
            UpdateDirtyState();
        }

        private bool HasPcResolutionChanges()
        {
            if (!GameProfile.IsBioShock2)
            {
                foreach (IniEntry entry in _gameIniEntries)
                    if (entry.Changed && IsPcResolutionEntry(entry)) return true;
                return false;
            }
            foreach (IniEntry entry in _sharedIniEntries)
                if (entry.Changed && entry.Section == "SharedOptions" &&
                    (entry.Key == "ViewportX" || entry.Key == "ViewportY")) return true;
            return false;
        }

        private void MarkDirty()
        {
            if (_loading)
                return;
            _vrDirty = true;
            UpdateDirtyState();
        }

        private void UpdateDirtyState()
        {
            _dirty = _vrDirty || _upscalerDirty ||
                     _dlssDirty || _gameIniDirty;
            _saveButton.Enabled = _dirty || NeedsWindowedVrMode();
            if (_dirty)
                SetStatus("There are unsaved changes.", Warning);
            else if (_statusLabel != null &&
                     _statusLabel.Text == "There are unsaved changes.")
                SetStatus("No pending changes.", Success);
        }

        private void UpdatePathStatus()
        {
            _configStateLabel.Text = GameProfile.DisplayName + (GameProfile.IsBioShock2 ? "  ·  VR + weapons + DLSS + Shared.ini" : "  ·  VR + gestures + DLSS");
            if (!string.IsNullOrEmpty(_gameExePath) && File.Exists(_gameExePath))
            {
                _gameStateLabel.Text = "GAME FOUND  ·  " + _gameExePath;
                _launchButton.Enabled = !_launchPending;
            }
            else
            {
                _gameStateLabel.Text = "GAME NOT FOUND  ·  you can locate " + GameProfile.ExeName + " at launch";
                _launchButton.Enabled = !_launchPending;
            }
            _dlssBackendStatus = DetectDlssBackend();
            UpdateDlssSummary();
        }

        private void LoadConfiguration(bool initiatedByUser)
        {
            if (_launchPending) return;
            if (initiatedByUser && _dirty)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "There are unsaved changes. Discard them and reload the files?",
                    "Reload configuration",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                    return;
            }

            try
            {
                _loading = true;
                if (File.Exists(_configPath))
                {
                    _loadedContent = File.ReadAllText(_configPath, Encoding.UTF8);
                    _loadedWriteTimeUtc = File.GetLastWriteTimeUtc(_configPath);
                    PopulateEditors(LoadAllVrValues(_loadedContent));
                    _vrDirty = false;
                }
                else
                {
                    _loadedContent = string.Empty;
                    _loadedWriteTimeUtc = DateTime.MinValue;
                    PopulateEditors(LoadAllVrValues(string.Empty));
                    _vrDirty = true;
                }

                LoadSharedIni();
                if (File.Exists(_gameIniPath))
                {
                    _gameIniLoadedContent = ReadGameIniText(_gameIniPath);
                    _gameIniEntries = GameIniDocument.Parse(_gameIniLoadedContent);
                    _gameIniDirty = false;
                    PopulateIniFilters();
                    LoadResolutionControls();
                    LoadFxaaControl();
                    RebuildIniGrid();
                }
                else
                {
                    _gameIniLoadedContent = string.Empty;
                    _gameIniEntries = new List<IniEntry>();
                    _gameIniDirty = false;
                    PopulateIniFilters();
                    RebuildIniGrid();
                    _resolutionWidth.Enabled = false;
                    _resolutionHeight.Enabled = false;
                    _resolutionPreset.Enabled = false;
                    _fxaaEnabled.Enabled = false;
                    _fxaaEnabled.Checked = false;
                    _resolutionLoadLabel.Text = "Bioshock2SP.ini was not found in " + _gameIniPath;
                    _resolutionLoadLabel.ForeColor = Color.Firebrick;
                    _fxaaStateLabel.Text = "Bioshock2SP.ini was not found.";
                    _fxaaStateLabel.ForeColor = Color.Firebrick;
                }
                if (File.Exists(_sharedIniPath)) LoadResolutionControls();
                LoadUpscalerConfiguration();
                LoadDlssConfiguration();
                LoadGraphicsOptions();
                bool finalPolicyAdjusted = EnforceFinalImagePolicy();
                _dirty = _vrDirty || _upscalerDirty ||
                         _dlssDirty || _gameIniDirty;
                _saveButton.Enabled = _dirty || NeedsWindowedVrMode();
                if (finalPolicyAdjusted)
                    SetStatus("This edition will disable FXAA and legacy spatial upscaling when saving.", Warning);
                else if (NeedsWindowedVrMode())
                    SetStatus("VR startup: click Save to prepare windowed mode and prevent fullscreen from overriding the resolution.", Warning);
                else if (_dlssDirty && _upscalerDirty)
                    SetStatus("DLSS and spatial upscaling were both active. The interface kept DLSS only; click Save to correct both files.", Warning);
                else if (!string.IsNullOrEmpty(_dlssLoadWarning))
                    SetStatus("Configuration loaded; check the dlss.ini warning in the Image tab.", Warning);
                else if (!FinalDlssEdition && !string.IsNullOrEmpty(_upscalerLoadWarning))
                    SetStatus("Configuration loaded; check the upscaler.ini warning in the Image tab.", Warning);
                else
                    SetStatus(File.Exists(_configPath)
                        ? "VR configuration, Normal/DLAA/DLSS 4.5 modes and " + _gameIniEntries.Count + " Bioshock2SP.ini entries loaded without changing files."
                        : "vrpreset.ini is missing; initial values are displayed and ready to save.",
                        File.Exists(_configPath) ? Success : Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not read the configuration:\n\n" + ex.Message,
                    "Error loading", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Error reading vrpreset.ini.", Color.Firebrick);
            }
            finally
            {
                _loading = false;
            }
            if (FinalDlssEdition && PrepareSquareImage())
                SetStatus("The square headset resolution is ready. Click Save to apply it.", Warning);
        }

        private void PopulateEditors(Dictionary<string, string> values)
        {
            _originalEditorValues.Clear();
            _initialEditorValues.Clear();
            foreach (KeyValuePair<string, string> item in values) _originalEditorValues[item.Key] = item.Value;
            foreach (ParamDef definition in _definitions)
            {
                string text;
                if (!values.TryGetValue(definition.Key, out text))
                    text = definition.DefaultValue;
                Control editor = _editors[definition.Key];
                if (definition.Kind == ParamKind.Boolean)
                {
                    decimal value;
                    ((CheckBox)editor).Checked =
                        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                        ? value != 0
                        : string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
                }
                else if (definition.Kind == ParamKind.Choice)
                {
                    int value;
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                        value = 0;
                    value = Math.Max(0, Math.Min(((ComboBox)editor).Items.Count - 1, value));
                    ((ComboBox)editor).SelectedIndex = value;
                }
                else
                {
                    decimal value;
                    if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        decimal.TryParse(definition.DefaultValue, NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out value);
                    NumericUpDown number = (NumericUpDown)editor;
                    if (value < number.Minimum) value = number.Minimum;
                    if (value > number.Maximum) value = number.Maximum;
                    number.Value = value;
                }
                _initialEditorValues[definition.Key] = FormatEditorValue(definition);
            }
        }

        private string FormatEditorValue(ParamDef definition)
        {
            Control editor = _editors[definition.Key];
            if (definition.Kind == ParamKind.Boolean) return ((CheckBox)editor).Checked ? "1" : "0";
            if (definition.Kind == ParamKind.Choice)
                return ((ComboBox)editor).SelectedIndex.ToString(CultureInfo.InvariantCulture);
            return ((NumericUpDown)editor).Value.ToString(
                "F" + definition.Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private Dictionary<string, string> CollectValues()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ParamDef definition in _definitions)
            {
                string value = FormatEditorValue(definition);
                string initial, original;
                if (_initialEditorValues.TryGetValue(definition.Key, out initial) && initial == value &&
                    _originalEditorValues.TryGetValue(definition.Key, out original))
                    value = original;
                values[definition.Key] = value;
            }
            return values;
        }

        private decimal NumericValue(string key)
        {
            return ((NumericUpDown)_editors[key]).Value;
        }

        private bool ValidateBeforeSave()
        {
            return true;
        }

        private string ReadGameIniText(string path)
        {
            _gameIniEncoding = AtomicConfigBatch.DetectEncoding(path);
            return File.ReadAllText(path, _gameIniEncoding);
        }

        private void LoadSharedIni()
        {
            if (!GameProfile.IsBioShock2) return;
            _sharedIniLoadedContent = string.Empty;
            _sharedIniEncoding = Encoding.GetEncoding(1252);
            if (File.Exists(_sharedIniPath))
            {
                _sharedIniEncoding = AtomicConfigBatch.DetectEncoding(_sharedIniPath);
                _sharedIniLoadedContent = File.ReadAllText(_sharedIniPath, _sharedIniEncoding);
            }
            _sharedIniEntries = GameIniDocument.Parse(_sharedIniLoadedContent);
        }

        private Dictionary<string, string> LoadAllVrValues(string vrContent)
        {
            Dictionary<string, string> values = ConfigDocument.Parse(vrContent);
            _weaponsLoadedContent = File.Exists(_weaponsPath)
                ? File.ReadAllText(_weaponsPath, Encoding.UTF8) : string.Empty;
            foreach (KeyValuePair<string, string> pair in ConfigDocument.Parse(_weaponsLoadedContent))
                values[pair.Key] = pair.Value;
            return values;
        }

        private bool PrepareVrSave(out string expectedContent)
        {
            expectedContent = string.Empty;
            try
            {
                expectedContent = File.Exists(_configPath)
                    ? File.ReadAllText(_configPath, Encoding.UTF8)
                    : string.Empty;
                if (expectedContent == (_loadedContent ?? string.Empty))
                    return true;
                DialogResult answer = MessageBox.Show(this,
                    "vrpreset.ini has changed since you opened or reloaded the launcher. " +
                    "It may have been changed by the game or another application.\n\n" +
                    "Apply the currently displayed values to that externally modified version?",
                    "Configuration changed externally",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                return answer == DialogResult.Yes;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not check vrpreset.ini:\n\n" + ex.Message,
                    "Error preparing to save", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool PrepareUpscalerSave(out string expectedContent)
        {
            expectedContent = string.Empty;
            try
            {
                expectedContent = File.Exists(_upscalerConfigPath)
                    ? File.ReadAllText(_upscalerConfigPath, Encoding.UTF8)
                    : string.Empty;
                if (expectedContent == (_upscalerLoadedContent ?? string.Empty))
                    return true;
                DialogResult answer = MessageBox.Show(this,
                    "upscaler.ini has changed since you opened or reloaded the launcher. " +
                    "It may have been changed by the game or another application.\n\n" +
                    "Apply the currently displayed values to that externally modified version?",
                    "Upscaling changed externally",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                return answer == DialogResult.Yes;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not check upscaler.ini:\n\n" + ex.Message,
                    "Error preparing upscaling", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool PrepareDlssSave(out string expectedContent)
        {
            expectedContent = string.Empty;
            try
            {
                expectedContent = File.Exists(_dlssConfigPath)
                    ? File.ReadAllText(_dlssConfigPath, Encoding.UTF8)
                    : string.Empty;
                if (expectedContent == (_dlssLoadedContent ?? string.Empty))
                    return true;
                DialogResult answer = MessageBox.Show(this,
                    "dlss.ini has changed since you opened or reloaded the launcher. " +
                    "It may have been changed by the game or another application.\n\n" +
                    "Apply the currently displayed values to that externally modified version?",
                    "DLSS changed externally",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                return answer == DialogResult.Yes;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not check dlss.ini:\n\n" + ex.Message,
                    "Error preparing DLSS 4.5", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool SaveConfiguration(bool showNoChanges)
        {
            if (_launchPending) return false;
            if (IsGameRunning())
            {
                MessageBox.Show(this,
                    "" + GameProfile.DisplayName + " is open. Close it before saving so the game does not overwrite the file on exit.",
                    "Game running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // Only an explicit save prepares windowed VR. Opening/reloading
            // remains read-only, including profiles inherited from flat play.
            if (!PrepareWindowedVrMode()) return false;

            if (!_dirty)
            {
                if (showNoChanges)
                    SetStatus("No pending changes; the file is up to date.", Success);
                return true;
            }
            if (_vrDirty && !ValidateBeforeSave())
                return false;

            if (!FinalDlssEdition && _upscalerEnabled.Checked && _dlssMode.SelectedIndex > 0)
            {
                MessageBox.Show(this,
                    "DLSS 4.5 and spatial upscaling cannot be active at the same time. " +
                    "Select only one of the two methods.",
                    "Mutually exclusive image methods", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SelectTab("Image and resolution");
                return false;
            }

            if (!FinalDlssEdition &&
                (_upscalerDirty || HasPcResolutionChanges()) && !ValidateUpscalerBeforeSave())
                return false;

            if ((_dlssDirty || HasPcResolutionChanges()) && !ValidateDlssBeforeSave())
                return false;

            if (_gameIniDirty && !ConfirmSensitiveGameIniChanges())
                return false;

            string expectedVrContent = null;
            if (_vrDirty && !PrepareVrSave(out expectedVrContent))
                return false;
            string expectedUpscalerContent = null;
            if (_upscalerDirty &&
                !PrepareUpscalerSave(out expectedUpscalerContent))
                return false;
            string expectedDlssContent = null;
            if (_dlssDirty && !PrepareDlssSave(out expectedDlssContent))
                return false;

            bool savedVr = false;
            bool savedGame = false;
            bool savedUpscaler = false;
            bool savedDlss = false;
            if (_gameIniDirty)
            {
                if (!SaveGameIniCore()) return false;
                savedGame = true;
            }
            if (_vrDirty)
            {
                if (!SaveVrConfigurationCore(expectedVrContent))
                {
                    if (savedGame)
                        SetStatus("Bioshock2SP.ini was saved; vrpreset.ini was not saved.", Warning);
                    return false;
                }
                savedVr = true;
            }
            if (_upscalerDirty)
            {
                if (!SaveUpscalerConfigurationCore(expectedUpscalerContent))
                {
                    if (savedGame || savedVr)
                        SetStatus("The other files were saved; upscaler.ini was not saved.", Warning);
                    return false;
                }
                savedUpscaler = true;
            }
            if (_dlssDirty)
            {
                if (!SaveDlssConfigurationCore(expectedDlssContent))
                {
                    if (savedGame || savedVr || savedUpscaler)
                        SetStatus("The other files were saved; dlss.ini was not saved.", Warning);
                    return false;
                }
                savedDlss = true;
            }
            UpdateDirtyState();
            List<string> savedNames = new List<string>();
            if (savedVr) savedNames.Add("vrpreset.ini + weapons.ini");
            if (savedDlss) savedNames.Add("dlss.ini");
            if (savedUpscaler) savedNames.Add("upscaler.ini");
            if (savedGame) savedNames.Add(GameProfile.IsBioShock2 ? "Shared.ini + Bioshock2SP.ini" : "Bioshock.ini");
            SetStatus(string.Join(" + ", savedNames.ToArray()) +
                " saved, verified and backed up.", Success);
            return true;
        }

        private bool NeedsWindowedVrMode()
        {
            if (!GameProfile.IsBioShock2) return false;
            if (!FinalDlssEdition) return false;
            foreach (string section in new string[] { "SharedOptions", "WinDrv.WindowsClient" })
            {
                IniEntry entry = FindIniEntry(section, "StartupFullscreen");
                bool enabled;
                if (entry == null || !TryParseIniSwitch(entry.Value, out enabled) || enabled)
                    return true;
            }
            return false;
        }

        private bool PrepareWindowedVrMode()
        {
            if (!GameProfile.IsBioShock2) return true;
            if (!FinalDlssEdition) return true;
            try
            {
                if (VrWindowPolicy.Apply(_sharedIniEntries, _gameIniEntries))
                {
                    RecalculateGameIniDirty();
                    RebuildIniGrid();
                }
                return true;
            }
            catch (InvalidDataException ex)
            {
                MessageBox.Show(this, "Could not prepare windowed VR startup:\n\n" + ex.Message +
                    "\n\nNo files were saved. Check the INI files and reload the launcher.",
                    "Invalid window configuration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private bool SaveUpscalerConfigurationCore(string expectedContent)
        {
            string temporaryPath = null;
            try
            {
                string currentContent = File.Exists(_upscalerConfigPath)
                    ? File.ReadAllText(_upscalerConfigPath, Encoding.UTF8)
                    : string.Empty;
                if (currentContent != (expectedContent ?? string.Empty))
                    throw new IOException("upscaler.ini changed again while saving. Reload before trying again.");

                UpscalerSettings values = CollectUpscalerSettings();
                string newContent = UpscalerConfigDocument.Render(currentContent, values);
                int renderWidth, renderHeight;
                if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
                {
                    renderWidth = 2048;
                    renderHeight = 2048;
                }
                UpscalerSettings verification;
                string warning;
                if (!UpscalerConfigDocument.TryParse(newContent, renderWidth, renderHeight,
                                                     out verification, out warning) ||
                    verification.Enabled != values.Enabled ||
                    verification.OutputWidth != values.OutputWidth ||
                    verification.OutputHeight != values.OutputHeight ||
                    verification.Sharpness != values.Sharpness)
                    throw new InvalidDataException("Could not verify upscaler.ini. " + warning);

                AtomicConfigBatch.Save(new ConfigWrite[] {
                    new ConfigWrite(_upscalerConfigPath, expectedContent, newContent, new UTF8Encoding(false))
                });

                string diskContent = File.ReadAllText(_upscalerConfigPath, Encoding.UTF8);
                if (diskContent != newContent)
                    throw new IOException("Final upscaler.ini verification mismatch.");
                _upscalerLoadedContent = diskContent;
                _upscalerLoadWarning = null;
                _upscalerDirty = false;
                UpdateUpscalerSummary();
                return true;
            }
            catch (Exception ex)
            {
                if (temporaryPath != null)
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
                MessageBox.Show(this,
                    "Could not save upscaler.ini:\n\n" + ex.Message,
                    "Error saving upscaling settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("upscaler.ini was not saved.", Color.Firebrick);
                return false;
            }
        }

        private bool SaveDlssConfigurationCore(string expectedContent)
        {
            string temporaryPath = null;
            try
            {
                string currentContent = File.Exists(_dlssConfigPath)
                    ? File.ReadAllText(_dlssConfigPath, Encoding.UTF8)
                    : string.Empty;
                if (currentContent != (expectedContent ?? string.Empty))
                    throw new IOException("dlss.ini changed again while saving. Reload before trying again.");

                DlssSettings values = CollectDlssSettings();
                string newContent = DlssConfigDocument.Render(currentContent, values);
                int renderWidth, renderHeight;
                if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
                {
                    renderWidth = 2048;
                    renderHeight = 2048;
                }
                DlssSettings verification;
                string warning;
                if (!DlssConfigDocument.TryParse(newContent, renderWidth, renderHeight,
                                                  out verification, out warning) ||
                    verification.Mode != values.Mode ||
                    !string.Equals(verification.Runtime, values.Runtime,
                                   StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(verification.Preset, values.Preset,
                                   StringComparison.OrdinalIgnoreCase) ||
                    verification.OutputWidth != values.OutputWidth ||
                    verification.OutputHeight != values.OutputHeight ||
                    verification.SrScaleNumerator != values.SrScaleNumerator ||
                    verification.SrScaleDenominator != values.SrScaleDenominator ||
                    verification.SharpnessPercent != values.SharpnessPercent ||
                    verification.NearPlaneUu != values.NearPlaneUu)
                    throw new InvalidDataException("Could not verify dlss.ini. " + warning);

                AtomicConfigBatch.Save(new ConfigWrite[] {
                    new ConfigWrite(_dlssConfigPath, expectedContent, newContent, new UTF8Encoding(false))
                });

                string diskContent = File.ReadAllText(_dlssConfigPath, Encoding.UTF8);
                if (diskContent != newContent)
                    throw new IOException("Final dlss.ini verification mismatch.");
                _dlssLoadedContent = diskContent;
                _dlssLoadWarning = null;
                _dlssNormalizationNote = null;
                _dlssDirty = false;
                UpdateDlssSummary();
                return true;
            }
            catch (Exception ex)
            {
                if (temporaryPath != null)
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
                MessageBox.Show(this,
                    "Could not save dlss.ini:\n\n" + ex.Message,
                    "Error saving DLSS 4.5", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("dlss.ini was not saved.", Color.Firebrick);
                return false;
            }
        }

        private bool SaveVrConfigurationCore(string expectedContent)
        {
            try
            {
                Dictionary<string, string> values = CollectValues();
                List<ParamDef> vrDefinitions = new List<ParamDef>();
                List<ParamDef> weaponDefinitions = new List<ParamDef>();
                Dictionary<string, string> vrValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> weaponValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (ParamDef definition in _definitions)
                {
                    bool weapon = definition.Category == "Weapons";
                    (weapon ? weaponDefinitions : vrDefinitions).Add(definition);
                    (weapon ? weaponValues : vrValues)[definition.Key] = values[definition.Key];
                }
                string vrContent = ConfigDocument.Render(expectedContent, vrDefinitions, vrValues);
                string weaponsContent = ConfigDocument.Render(_weaponsLoadedContent, weaponDefinitions, weaponValues);
                List<ConfigWrite> writes = new List<ConfigWrite>();
                writes.Add(new ConfigWrite(_configPath, expectedContent, vrContent, new UTF8Encoding(false)));
                if (GameProfile.IsBioShock2)
                    writes.Add(new ConfigWrite(_weaponsPath, _weaponsLoadedContent, weaponsContent, new UTF8Encoding(false)));
                AtomicConfigBatch.Save(writes);
                _loadedContent = vrContent;
                _weaponsLoadedContent = weaponsContent;
                _loadedWriteTimeUtc = File.GetLastWriteTimeUtc(_configPath);
                _vrDirty = false;
                RefreshDiagnostics();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save vrpreset.ini and weapons.ini:\n\n" + ex.Message,
                    "Error saving", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("VR save incomplete. Check the warning and reload.", Color.Firebrick);
                return false;
            }
        }

        private bool ConfirmSensitiveGameIniChanges()
        {
            List<IniEntry> sensitive = new List<IniEntry>();
            foreach (IniEntry entry in _gameIniEntries)
            {
                if (!entry.Changed) continue;
                if (entry.Protected ||
                    (entry.Section == "WinDrv.WindowsClient" && entry.Key == "StartupFullscreen" &&
                     string.Equals(entry.Value.TrimEnd(';'), "True", StringComparison.OrdinalIgnoreCase)))
                    sensitive.Add(entry);
            }
            if (sensitive.Count == 0) return true;

            StringBuilder list = new StringBuilder();
            int shown = Math.Min(8, sensitive.Count);
            for (int i = 0; i < shown; i++)
                list.AppendLine("• [" + sensitive[i].Section + "] " + sensitive[i].Key);
            if (sensitive.Count > shown)
                list.AppendLine("• …and " + (sensitive.Count - shown) + " more setting(s)");
            DialogResult answer = MessageBox.Show(this,
                "You changed internal, protected or VR-sensitive settings:\n\n" +
                list.ToString() + "\nA backup will be created, but an incorrect value could prevent startup or affect progress. Continue?",
                "Confirm sensitive changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            return answer == DialogResult.Yes;
        }

        private bool ValidateGameIniStructure()
        {
            int sectionCount = Regex.Matches(_gameIniLoadedContent ?? string.Empty,
                @"(?m)^\[WinDrv\.WindowsClient\]\r?$").Count;
            if (sectionCount != 1)
                throw new InvalidDataException("Expected one [WinDrv.WindowsClient] section; found " + sectionCount + ".");
            string[] keys = new string[] {
                "WindowedViewportX", "WindowedViewportY", "FullscreenViewportX", "FullscreenViewportY"
            };
            bool resolutionChanged = false;
            foreach (IniEntry entry in _gameIniEntries)
            {
                if (IsPcResolutionEntry(entry) && entry.Changed)
                {
                    resolutionChanged = true;
                    break;
                }
            }
            int fxaaCount = 0;
            IniEntry changedFxaa = null;
            foreach (IniEntry entry in _gameIniEntries)
            {
                if (!IsFxaaEntry(entry)) continue;
                fxaaCount++;
                if (entry.Changed) changedFxaa = entry;
            }
            if (changedFxaa != null)
            {
                if (fxaaCount != 1)
                    throw new InvalidDataException("The [Engine.RenderConfig] UseFxaa key is missing or duplicated.");
                bool enabled;
                if (!TryParseIniSwitch(changedFxaa.Value, out enabled))
                    throw new InvalidDataException("[Engine.RenderConfig] UseFxaa must be 0, 1, False or True.");
            }
            int width = 0, height = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                int count = 0;
                IniEntry found = null;
                foreach (IniEntry entry in _gameIniEntries)
                {
                    if (entry.Section == "WinDrv.WindowsClient" && entry.Key == keys[i])
                    {
                        count++;
                        found = entry;
                    }
                }
                if (count != 1 || found == null)
                    throw new InvalidDataException("The PC key " + keys[i] + " is missing or duplicated.");
                if (!resolutionChanged)
                    continue;
                int number;
                if (!int.TryParse(found.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                    number < 1024 || number > 8192)
                    throw new InvalidDataException(keys[i] + " must be between 1024 and 8192.");
                if (i == 0) width = number;
                if (i == 1) height = number;
                if ((i == 2 && number != width) || (i == 3 && number != height))
                    throw new InvalidDataException("Windowed and fullscreen resolutions must be saved in sync.");
            }
            return true;
        }

        private bool SaveGameIniCore()
        {
            if (!GameProfile.IsBioShock2) return SaveGameIniCoreBs1();
            try
            {
                IniEntry sx = FindIniEntry("SharedOptions", "ViewportX");
                IniEntry sy = FindIniEntry("SharedOptions", "ViewportY");
                if (sx == null || sy == null)
                    throw new InvalidDataException("Shared.ini must contain exactly one ViewportX / ViewportY pair in [SharedOptions].");
                int width, height;
                if (!int.TryParse(sx.Value.TrimEnd(';'), out width) || !int.TryParse(sy.Value.TrimEnd(';'), out height) ||
                    width < 640 || width > 8192 || height < 480 || height > 8192)
                    throw new InvalidDataException("The Shared.ini resolution is outside the 640×480 to 8192×8192 range.");
                string sharedContent = GameIniDocument.Render(_sharedIniLoadedContent, _sharedIniEntries);
                string spContent = GameIniDocument.Render(_gameIniLoadedContent, _gameIniEntries);
                List<ConfigWrite> writes = new List<ConfigWrite>();
                writes.Add(new ConfigWrite(_sharedIniPath, _sharedIniLoadedContent, sharedContent, _sharedIniEncoding));
                if (File.Exists(_gameIniPath))
                    writes.Add(new ConfigWrite(_gameIniPath, _gameIniLoadedContent, spContent, _gameIniEncoding));
                AtomicConfigBatch.Save(writes);
                _sharedIniLoadedContent = sharedContent;
                _sharedIniEntries = GameIniDocument.Parse(sharedContent);
                _gameIniLoadedContent = spContent;
                _gameIniEntries = GameIniDocument.Parse(spContent);
                _gameIniDirty = false;
                _loading = true;
                PopulateIniFilters();
                LoadResolutionControls();
                LoadFxaaControl();
                RebuildIniGrid();
                RefreshDiagnostics();
                _loading = false;
                return true;
            }
            catch (Exception ex)
            {
                _loading = false;
                MessageBox.Show(this, "Could not save Shared.ini and Bioshock2SP.ini:\n\n" + ex.Message,
                    "Error saving image settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Saving the game INI files did not complete.", Color.Firebrick);
                return false;
            }
        }


        private bool SaveGameIniCoreBs1()
        {
            string temporaryPath = null;
            try
            {
                if (!File.Exists(_gameIniPath))
                    throw new FileNotFoundException("Bioshock.ini was not found.", _gameIniPath);
                string currentContent = ReadGameIniText(_gameIniPath);
                if (currentContent != (_gameIniLoadedContent ?? string.Empty))
                {
                    DialogResult answer = MessageBox.Show(this,
                        "Bioshock.ini has changed since you loaded it, probably because the game or another application wrote to it.\n\nReload the file before continuing to avoid losing those changes.",
                        "Bioshock.ini changed externally", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }
                ValidateGameIniStructure();
                string newContent = GameIniDocument.Render(currentContent, _gameIniEntries);
                if (newContent == currentContent)
                {
                    _gameIniDirty = false;
                    return true;
                }

                List<IniEntry> verificationModel = GameIniDocument.Parse(newContent);
                if (verificationModel.Count != _gameIniEntries.Count)
                    throw new InvalidDataException("The INI structure changed while preparing to save.");
                for (int i = 0; i < verificationModel.Count; i++)
                {
                    if (verificationModel[i].Section != _gameIniEntries[i].Section ||
                        verificationModel[i].Key != _gameIniEntries[i].Key ||
                        verificationModel[i].Value != _gameIniEntries[i].Value)
                        throw new InvalidDataException("Verification failed for [" +
                            _gameIniEntries[i].Section + "] " + _gameIniEntries[i].Key + ".");
                }

                string directory = Path.GetDirectoryName(_gameIniPath);
                string backupDirectory = Path.Combine(directory, "Copias del lanzador");
                Directory.CreateDirectory(backupDirectory);
                string backupPath = Path.Combine(backupDirectory,
                    "Bioshock-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".ini");
                File.Copy(_gameIniPath, backupPath, false);
                if (!FilesHaveSameBytes(backupPath, _gameIniPath))
                    throw new IOException("Could not verify the Bioshock.ini backup.");

                temporaryPath = _gameIniPath + ".lanzador-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryPath, newContent, _gameIniEncoding);
                try
                {
                    File.Replace(temporaryPath, _gameIniPath, null, true);
                    temporaryPath = null;
                }
                catch (Exception ex)
                {
                    throw new IOException("Windows could not replace Bioshock.ini atomically. The original is still protected by the backup: " + ex.Message, ex);
                }

                string diskContent = ReadGameIniText(_gameIniPath);
                if (diskContent != newContent)
                    throw new IOException("The final Bioshock.ini readback does not match the prepared content.");
                _gameIniLoadedContent = diskContent;
                _gameIniEntries = GameIniDocument.Parse(diskContent);
                _gameIniDirty = false;
                _loading = true;
                PopulateIniFilters();
                LoadResolutionControls();
                LoadFxaaControl();
                RebuildIniGrid();
                _loading = false;
                return true;
            }
            catch (Exception ex)
            {
                if (temporaryPath != null)
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
                _loading = false;
                MessageBox.Show(this,
                    "Could not save changes to Bioshock.ini:\n\n" + ex.Message,
                    "Error saving Bioshock.ini", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Bioshock.ini was not saved.", Color.Firebrick);
                return false;
            }
        }

        private static bool FilesHaveSameBytes(string firstPath, string secondPath)
        {
            byte[] first = File.ReadAllBytes(firstPath);
            byte[] second = File.ReadAllBytes(secondPath);
            if (first.Length != second.Length) return false;
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i]) return false;
            }
            return true;
        }

        private void SaveAndLaunch()
        {
            if (_launchPending) return;
            if (!string.IsNullOrEmpty(Program.SandboxRoot))
            {
                SetStatus("Isolated test: game startup is disabled.", Warning);
                return;
            }
            if (IsGameRunning())
            {
                MessageBox.Show(this, "" + GameProfile.DisplayName + " is already running. Close the game before applying changes or starting another session.",
                    "Game running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.IsNullOrEmpty(_gameExePath) || !File.Exists(_gameExePath))
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Locate " + GameProfile.ExeName;
                    dialog.Filter = GameProfile.DisplayName + " Remastered|" + GameProfile.ExeName;
                    dialog.CheckFileExists = true;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    _gameExePath = Path.GetFullPath(dialog.FileName);
                    UpdatePathStatus();
                }
            }
            string problem;
            try
            {
                if (!GameProfile.VerifyExecutable(_gameExePath, out problem))
                {
                    MessageBox.Show(this, problem, "Unsupported game version", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not verify the executable:\n\n" + ex.Message,
                    "Verification error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!SaveConfiguration(false)) return;
            _launchPreviousIds.Clear();
            foreach (Process process in Process.GetProcessesByName(GameProfile.ProcessName))
                using (process) { _launchPreviousIds.Add(process.Id); }
            _launchRequestedUtc = DateTime.UtcNow;
            _launchTracker = new GameLaunchTracker(_launchRequestedUtc, _gameExePath, _launchPreviousIds);
            _launchPending = true;
            _launchButton.Enabled = false;
            _saveButton.Enabled = false;
            _tabs.Enabled = false;
            try
            {
                try
                {
                    ProcessStartInfo steam = new ProcessStartInfo("steam://rungameid/" + GameProfile.AppId);
                    steam.UseShellExecute = true;
                    using (Process request = Process.Start(steam)) { }
                }
                catch
                {
                    ProcessStartInfo direct = new ProcessStartInfo(_gameExePath);
                    direct.WorkingDirectory = Path.GetDirectoryName(_gameExePath);
                    direct.UseShellExecute = true;
                    using (Process request = Process.Start(direct)) { }
                }
                _launchTimer = new Timer();
                _launchTimer.Interval = 500;
                _launchTimer.Tick += PollGameLaunch;
                _launchTimer.Start();
                SetStatus("Waiting for " + GameProfile.DisplayName + ": new process, verified path and game window…", Blue);
            }
            catch (Exception ex)
            {
                EndLaunchAttempt("Could not request startup for " + GameProfile.DisplayName + ".\n\n" + ex.Message);
            }
        }

        private void PollGameLaunch(object sender, EventArgs e)
        {
            List<GameProcessObservation> observations = new List<GameProcessObservation>();
            foreach (Process process in Process.GetProcessesByName(GameProfile.ProcessName))
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        observations.Add(new GameProcessObservation {
                            Id = process.Id,
                            StartedUtc = process.StartTime.ToUniversalTime(),
                            ImagePath = GameLaunchEvidence.ProcessPath(process.Id),
                            HasWindow = process.MainWindowHandle != IntPtr.Zero,
                            Responding = process.Responding
                        });
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            LaunchOutcome outcome = _launchTracker.Observe(DateTime.UtcNow, observations);
            if (outcome == LaunchOutcome.Started)
            {
                StopLaunchMonitor();
                _launchPending = false;
                SetStatus("" + GameProfile.DisplayName + " started and verified.", Success);
                Close();
                return;
            }
            if (outcome == LaunchOutcome.ExitedEarly)
            {
                EndLaunchAttempt("" + GameProfile.DisplayName + " created a process, but exited before a working window could be confirmed.\n\n" +
                    "Check the VR log in Diagnostics and the game's status in Steam. Your saved settings are retained.");
                return;
            }
            if (outcome == LaunchOutcome.TimedOut)
            {
                EndLaunchAttempt("Startup has not been confirmed for " + GameProfile.DisplayName + " within 60 seconds.\n\n" +
                    "An open Steam window does not confirm that the game started. Check whether Steam is updating the game or displaying a prompt. " +
                    "You can also check Diagnostics. The launcher remains open and your settings have been saved.");
                return;
            }
            int elapsed = (int)(DateTime.UtcNow - _launchRequestedUtc).TotalSeconds;
            SetStatus("Waiting for the process and window of " + GameProfile.DisplayName + "… " + elapsed + "/60 s", Blue);
        }

        private void StopLaunchMonitor()
        {
            if (_launchTimer == null) return;
            _launchTimer.Stop();
            _launchTimer.Dispose();
            _launchTimer = null;
        }

        private void EndLaunchAttempt(string problem)
        {
            StopLaunchMonitor();
            _launchPending = false;
            _tabs.Enabled = true;
            _launchButton.Enabled = true;
            _saveButton.Enabled = _dirty;
            RefreshDiagnostics();
            SetStatus("Startup not confirmed; check Steam and Diagnostics.", Color.Firebrick);
            MessageBox.Show(this, problem, "" + GameProfile.DisplayName + " did not start correctly",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static bool IsGameRunning()
        {
            Process[] processes = Process.GetProcessesByName(GameProfile.ProcessName);
            bool running = processes.Length > 0;
            foreach (Process process in processes)
                process.Dispose();
            return running;
        }

        private static string FindGameExecutable()
        {
            if (!string.IsNullOrEmpty(Program.SandboxRoot)) return null;
            List<string> candidates = new List<string>();
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, GameProfile.ExeName));

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string steamPath = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(steamPath))
                        {
                            AddSteamCandidate(candidates, steamPath);
                            AddLibrariesFromVdf(candidates, steamPath);
                        }
                    }
                }
            }
            catch { }

            try
            {
                foreach (string root in Directory.GetLogicalDrives())
                {
                    AddSteamCandidate(candidates, Path.Combine(root, "SteamLibrary"));
                    AddSteamCandidate(candidates, Path.Combine(root, "Program Files (x86)", "Steam"));
                }
            }
            catch { }

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch { }
            }
            return null;
        }

        private static void AddSteamCandidate(List<string> candidates, string steamRoot)
        {
            candidates.Add(Path.Combine(steamRoot, "steamapps", "common", GameProfile.SteamFolder,
                                        "Build", "Final", GameProfile.ExeName));
        }

        private static void AddLibrariesFromVdf(List<string> candidates, string steamRoot)
        {
            try
            {
                string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf))
                    return;
                string text = File.ReadAllText(vdf, Encoding.UTF8);
                MatchCollection matches = Regex.Matches(text, "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"",
                                                        RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    string path = match.Groups[1].Value.Replace("\\\\", "\\");
                    AddSteamCandidate(candidates, path);
                }
            }
            catch { }
        }

        private void OpenConfigurationFile()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    ProcessStartInfo info = new ProcessStartInfo("notepad.exe", "\"" + _configPath + "\"");
                    info.UseShellExecute = true;
                    Process.Start(info);
                }
                else
                    MessageBox.Show(this, "The file does not exist yet. Click Save changes to create it.",
                                    "Configuration", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open file",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowCreditsAndLicenses()
        {
            MessageBox.Show(this,
                "BioShock 1–2 VR · DLSS/DLAA 0.2.17 English\n" +
                "DLSS/DLAA integration and launcher: Beren5556\n\n" +
                "SPECIAL THANKS TO MOHAMAD BALOUZA\n" +
                "Creator of BioShock VR and the fundamental VR implementation " +
                "on which this fork is built. Without his tremendous work, " +
                "this project would not exist. Published by VR-Stereo-Hub (MIT).\n" +
                "Exact base version: BioShock VR v0.8.2\n" +
                "Project: https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr\n" +
                "Creator: https://github.com/mohamad-balouza\n\n" +
                "DLSS host: Jean-Laurent ROUZIES and NIGos (MIT).\n" +
                "Uses NVIDIA DLSS SDK 310.7.0 under the NVIDIA license.\n\n" +
                "Community project, not affiliated with or endorsed by 2K, " +
                "Take-Two Interactive or NVIDIA. Full licenses are " +
                "installed alongside the launcher.",
                "Credits and licenses", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OpenUpscalerConfigurationFile()
        {
            try
            {
                if (File.Exists(_upscalerConfigPath))
                {
                    ProcessStartInfo info = new ProcessStartInfo("notepad.exe",
                        "\"" + _upscalerConfigPath + "\"");
                    info.UseShellExecute = true;
                    Process.Start(info);
                }
                else
                    MessageBox.Show(this,
                        "upscaler.ini does not exist yet. Change an experimental option and click Save to create it.",
                        "Spatial upscaling", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open upscaler.ini",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenDlssConfigurationFile()
        {
            try
            {
                if (File.Exists(_dlssConfigPath))
                {
                    ProcessStartInfo info = new ProcessStartInfo("notepad.exe",
                        "\"" + _dlssConfigPath + "\"");
                    info.UseShellExecute = true;
                    Process.Start(info);
                }
                else
                    MessageBox.Show(this,
                        "dlss.ini does not exist yet. Change a DLSS 4.5 option and click Save to create it. The file alone does not install the backend.",
                        "DLSS 4.5", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open dlss.ini",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenGameIniFile()
        {
            try
            {
                if (File.Exists(_sharedIniPath))
                {
                    ProcessStartInfo info = new ProcessStartInfo("notepad.exe", "\"" + _sharedIniPath + "\"");
                    info.UseShellExecute = true;
                    Process.Start(info);
                }
                else
                    MessageBox.Show(this, "Shared.ini was not found in:\n\n" + _sharedIniPath,
                                    "Shared.ini", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open Shared.ini",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenBackupFolder()
        {
            try
            {
                string vrDirectory = Path.Combine(Path.GetDirectoryName(_configPath), "Copias del lanzador");
                string gameDirectory = Path.Combine(Path.GetDirectoryName(_gameIniPath), "Copias del lanzador");
                Directory.CreateDirectory(vrDirectory);
                Directory.CreateDirectory(gameDirectory);
                ProcessStartInfo vrInfo = new ProcessStartInfo("explorer.exe", "\"" + vrDirectory + "\"");
                vrInfo.UseShellExecute = true;
                Process.Start(vrInfo);
                ProcessStartInfo gameInfo = new ProcessStartInfo("explorer.exe", "\"" + gameDirectory + "\"");
                gameInfo.UseShellExecute = true;
                Process.Start(gameInfo);
                SetStatus("Opened the backups of vrpreset.ini, dlss.ini and Bioshock2SP.ini.", Success);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open directory",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SelectTab(string name)
        {
            foreach (TabPage page in _tabs.TabPages)
            {
                if (page.Text == name || page.Name == name)
                {
                    _tabs.SelectedTab = page;
                    break;
                }
            }
        }

        private void SetStatus(string message, Color color)
        {
            if (_statusLabel == null)
                return;
            _statusLabel.Text = message;
            _statusLabel.ForeColor = color;
        }

        internal bool RunSandboxRoundTrip()
        {
            if (string.IsNullOrEmpty(Program.SandboxRoot)) throw new InvalidOperationException("This test requires a sandbox.");
            if (_resolutionWidth.Value != 800 || _resolutionHeight.Value != 600)
                throw new InvalidDataException("The interface did not use the effective resolution from Shared.ini.");
            if (string.IsNullOrEmpty(_dlssMode.Text) || string.IsNullOrEmpty(_resolutionPreset.Text))
                throw new InvalidDataException("The selectors do not display their values.");
            // The real regression starts from inherited fullscreen=True with
            // no edited controls. Use the same save path as Guardar/iniciar.
            _dirty = _vrDirty = _gameIniDirty = _dlssDirty = _upscalerDirty = false;
            UpdateDirtyState();
            if (!NeedsWindowedVrMode() || !_saveButton.Enabled || !SaveConfiguration(false) || NeedsWindowedVrMode())
                throw new InvalidDataException("Saving without other changes did not prepare legacy windowed mode.");
            foreach (string path in new string[] { _sharedIniPath, _gameIniPath })
            {
                if (!File.ReadAllText(path).Contains("StartupFullscreen=False"))
                    throw new InvalidDataException("Windowed mode was not saved in " + path);
                string savedHash = Bs2Profile.Hash(path);
                if (!SaveConfiguration(false) || Bs2Profile.Hash(path) != savedHash)
                    throw new InvalidDataException("Saving windowed mode a second time is not idempotent.");
            }
            _dlssMode.SelectedIndex = 1;
            string lowResolutionProblem;
            if (TryValidateDlssSettings(CollectDlssSettings(), out lowResolutionProblem))
                throw new InvalidDataException("DLAA must not accept an 800×600 render resolution.");
            _dlssMode.SelectedIndex = 0;
            _squareResolution.Checked = true;
            _resolutionHeight.Value = 480;
            if (_resolutionWidth.Value != 640 || _resolutionHeight.Value != 640)
                throw new InvalidDataException("Invalid minimum square resolution.");
            if (_definitions.Count < 180 || _editors.Count != _definitions.Count)
                throw new InvalidDataException("VR/weapon editors missing.");
            for (int index = 1; index < ResolutionPresets.Items.Length; ++index)
            {
                ResolutionPreset preset = ResolutionPresets.Items[index];
                _resolutionPreset.SelectedIndex = index;
                if (_resolutionWidth.Value != preset.Width || _resolutionHeight.Value != preset.Height ||
                    ResolutionPresetIndex(preset.Width, preset.Height) != index)
                    throw new InvalidDataException("Resolution selector out of sync: " + preset);
                _dlssMode.SelectedIndex = preset.Width == preset.Height ? 1 : 0;
                if (!SaveConfiguration(false) || !MirrorMatches(preset.Width, preset.Height))
                    throw new InvalidDataException("The profile and its mirror were not saved: " + preset);
                if (_dlssMode.SelectedIndex == 1)
                {
                    DlssSettings persisted;
                    string note;
                    if (!DlssConfigDocument.TryParse(File.ReadAllText(_dlssConfigPath), preset.Width, preset.Height,
                            out persisted, out note) || persisted.Mode != DlssMode.Dlaa ||
                        persisted.OutputWidth != preset.Width || persisted.OutputHeight != preset.Height)
                        throw new InvalidDataException("DLAA did not keep 1:1 for " + preset);
                }
            }
            _resolutionWidth.Value = 3010;
            if (_resolutionPreset.SelectedIndex != 0)
                throw new InvalidDataException("The custom resolution lost its selector.");
            _resolutionWidth.Value = 2048;
            _resolutionHeight.Value = 2048;
            ApplyResolutionControlsToEntries();
            if (!SaveGameIniCore()) return false;
            if (!MirrorMatches(2048, 2048)) throw new InvalidDataException("SP mirror not synchronized.");
            _dlssMode.SelectedIndex = 1;
            if (!SaveDlssConfigurationCore(_dlssLoadedContent)) return false;
            DlssSettings settings;
            string warning;
            if (!DlssConfigDocument.TryParse(File.ReadAllText(_dlssConfigPath), 2048, 2048, out settings, out warning) ||
                settings.Mode != DlssMode.Dlaa || settings.OutputWidth != 2048 || settings.OutputHeight != 2048)
                throw new InvalidDataException("Incorrect 1:1 DLAA save.");
            _dlssMode.SelectedIndex = 2;
            _dlssOutputWidth.Value = 3072;
            _dlssOutputHeight.Value = 3072;
            if (!SaveDlssConfigurationCore(_dlssLoadedContent)) return false;
            if (!DlssConfigDocument.TryParse(File.ReadAllText(_dlssConfigPath), 2048, 2048, out settings, out warning) ||
                settings.Mode != DlssMode.SuperResolution || settings.OutputWidth != 3072 || settings.OutputHeight != 3072)
                throw new InvalidDataException("Incorrect DLSS SR save.");
            _dlssMode.SelectedIndex = 0;
            if (!SaveDlssConfigurationCore(_dlssLoadedContent)) return false;
            string untouchedScale = ConfigDocument.Parse(_loadedContent)["worldScale"];
            ((NumericUpDown)_editors["PlayerDrill.aimTrimPitch"]).Value = 21.5m;
            ((NumericUpDown)_editors["handOffFwdL"]).Value = 2.5m;
            if (!SaveVrConfigurationCore(_loadedContent)) return false;
            Dictionary<string, string> vr = ConfigDocument.Parse(File.ReadAllText(_configPath));
            Dictionary<string, string> weapons = ConfigDocument.Parse(File.ReadAllText(_weaponsPath));
            if (vr.ContainsKey("PlayerDrill.aimTrimPitch") || weapons.ContainsKey("handOffFwdL") ||
                weapons["PlayerDrill.aimTrimPitch"] != "21.50" || vr["handOffFwdL"] != "2.50" ||
                vr["worldScale"] != untouchedScale)
                throw new InvalidDataException("Weapon profile isolation failed.");
            _dirty = _vrDirty = _gameIniDirty = _dlssDirty = _upscalerDirty = false;
            return true;
        }

        internal void CaptureSandboxTabs(string directory)
        {
            if (string.IsNullOrEmpty(Program.SandboxRoot)) throw new InvalidOperationException("This test requires a sandbox.");
            foreach (string name in new string[] { "Image", "Hands", "Weapons", "Diagnostics" })
            {
                foreach (TabPage tab in _tabs.TabPages)
                    if (tab.Text == name) { _tabs.SelectedTab = tab; break; }
                Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                    bitmap.Save(Path.Combine(directory, "launcher-" + name + ".png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                SaveConfiguration(true);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F5)
            {
                LoadConfiguration(true);
                e.SuppressKeyPress = true;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopLaunchMonitor();
            if (!_dirty)
                return;
            DialogResult answer = MessageBox.Show(this,
                "There are unsaved changes. Save them before closing?",
                "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
                e.Cancel = true;
            else if (answer == DialogResult.Yes && !SaveConfiguration(false))
                e.Cancel = true;
        }
    }

    internal static class Program
    {
        internal static string InitialGamePath;
        internal static string SandboxRoot;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--self-test-process")
                return Bs2LauncherTests.RunHelperWindow();
            if (args != null && args.Length > 0 && args[0] == "--self-test")
                return GameProfile.SelfTest() && (!GameProfile.IsBioShock2 || Bs2LauncherTests.RunCoreTests()) &&
                       ConfigDocument.SelfTest() && UpscalerConfigDocument.SelfTest() &&
                       DlssConfigDocument.SelfTest() && DlssQualityPolicy.SelfTest() &&
                       GameIniDocument.SelfTest() && MainForm.ImageControlsSelfTest() &&
                       MainForm.SimpleImageSelfTest() ? 0 : 2;

            if (args != null && (args.Length == 2 || args.Length == 3) && args[0] == "--preview-image")
            {
                int tabIndex = 0;
                if (args.Length == 3 && (!int.TryParse(args[2], out tabIndex) || tabIndex < 0 || tabIndex > 6))
                    return 2;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm.WriteImagePreview(args[1], tabIndex);
                return 0;
            }

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--sandbox", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        SandboxRoot = Path.GetFullPath(args[++i]);
                    }
                    else if (string.Equals(args[i], "--game", StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < args.Length)
                    {
                        InitialGamePath = args[++i];
                    }
                    else if (args[i].StartsWith("--game=", StringComparison.OrdinalIgnoreCase))
                    {
                        InitialGamePath = args[i].Substring("--game=".Length);
                    }
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
