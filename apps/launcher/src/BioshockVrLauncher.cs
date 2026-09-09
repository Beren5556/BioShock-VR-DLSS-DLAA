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

[assembly: AssemblyTitle("Complemento DLSS 4.5 para BioShock VR - Beren5556")]
[assembly: AssemblyDescription("Lanzador y editor seguro con modos Normal, DLAA y DLSS 4.5")]
[assembly: AssemblyProduct("Complemento DLSS 4.5 para BioShock VR")]
[assembly: AssemblyVersion("0.2.12.0")]
[assembly: AssemblyFileVersion("0.2.12.0")]

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
                lines.Add("# BioShock VR - valores guardados por el lanzador en castellano");
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
                        lines[i] = "; duplicado desactivado por el lanzador: " + trimmed;
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
                structureError = "Falta la sección [spatial].";
            else if (duplicates.Count > 0)
                structureError = "Hay claves duplicadas en [spatial].";
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
                problems.Add("Falta enabled.");
            else if (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                settings.Enabled = true;
            else if (raw == "0" || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                settings.Enabled = false;
            else
                problems.Add("enabled debe ser 0 o 1.");

            int number;
            if (!values.TryGetValue("outputWidth", out raw))
                problems.Add("Falta outputWidth.");
            else if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                     number < 1024 || number > 8192)
                problems.Add("outputWidth debe estar entre 1024 y 8192.");
            else
                settings.OutputWidth = number;

            if (!values.TryGetValue("outputHeight", out raw))
                problems.Add("Falta outputHeight.");
            else if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                     number < 1024 || number > 8192)
                problems.Add("outputHeight debe estar entre 1024 y 8192.");
            else
                settings.OutputHeight = number;

            decimal sharpness;
            if (!values.TryGetValue("sharpness", out raw))
                problems.Add("Falta sharpness.");
            else if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out sharpness) ||
                     sharpness < 0m || sharpness > 1m)
                problems.Add("sharpness debe estar entre 0,00 y 1,00.");
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
                lines.Add("# Reescalado espacial experimental de BioShock VR");
                lines.Add("# No activa DLSS ni DLAA. Solo lo utiliza la DLL experimental compatible.");
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
                structureError = "Falta la sección [dlss].";
            else if (duplicates.Count > 0)
                structureError = "Hay claves duplicadas en [dlss].";
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
                problems.Add("Falta mode.");
            else if (!TryParseMode(raw, out mode))
                problems.Add("mode debe ser off, dlaa o sr.");
            else
                settings.Mode = mode;

            if (!values.TryGetValue("runtime", out raw))
                problems.Add("Falta runtime.");
            else if (!string.Equals(raw, RequiredRuntime, StringComparison.OrdinalIgnoreCase))
                problems.Add("runtime debe ser " + RequiredRuntime + ".");
            settings.Runtime = RequiredRuntime;

            if (!values.TryGetValue("preset", out raw))
                problems.Add("Falta preset.");
            else if (!IsValidPreset(raw))
                problems.Add("preset debe ser auto, K, M o L.");
            else
                settings.Preset = NormalizePreset(raw);

            DlssQuality quality;
            if (!values.TryGetValue("quality", out raw))
                problems.Add("Falta quality.");
            else if (!TryParseQuality(raw, out quality))
                problems.Add("quality debe ser auto, quality, balanced, performance o ultra_performance.");
            else
                settings.Quality = quality;

            int number;
            if (!values.TryGetValue("outputWidth", out raw))
                problems.Add("Falta outputWidth.");
            else if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                     number < 1024 || number > 8192 || (number & 1) != 0)
                problems.Add("outputWidth debe ser par y estar entre 1024 y 8192.");
            else
                settings.OutputWidth = number;

            if (!values.TryGetValue("outputHeight", out raw))
                problems.Add("Falta outputHeight.");
            else if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                     number < 1024 || number > 8192 || (number & 1) != 0)
                problems.Add("outputHeight debe ser par y estar entre 1024 y 8192.");
            else
                settings.OutputHeight = number;

            decimal nearPlane;
            if (values.TryGetValue("nearPlaneUu", out raw))
            {
                string normalizedNear = raw.Replace(',', '.');
                if (!decimal.TryParse(normalizedNear, NumberStyles.Float,
                                      CultureInfo.InvariantCulture, out nearPlane) ||
                    nearPlane < 0.1m || nearPlane > 1000.0m)
                    problems.Add("nearPlaneUu debe estar entre 0,1 y 1000,0 UU.");
                else
                    settings.NearPlaneUu = nearPlane;
            }

            if (values.TryGetValue("sharpnessPercent", out raw))
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                    number < 0 || number > 100)
                    problems.Add("sharpnessPercent debe estar entre 0 y 100.");
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
                    problems.Add("La preferencia de escala SR debe ser una fracción válida entre 0 y 1.");
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
                        lines[i] = "; duplicado desactivado por el lanzador: " + trimmed;
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
                lines.Add("# Complemento DLSS 4.5 para BioShock VR - integracion de Beren5556");
                lines.Add("# Requiere dos historiales por ojo, profundidad, movimiento, jitter y host x64.");
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
                   repaired.Contains("; duplicado desactivado por el lanzador: preset=L") &&
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
                   minimumIsRespected && tiesRoundDown && roundTripValid &&
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
            string section = "Sin sección";
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
                    throw new InvalidDataException("El valor de " + entry.Key + " contiene un salto de línea.");
                if (entry.ValueStart < 0 || entry.ValueStart + entry.ValueLength > result.Length)
                    throw new InvalidDataException("Posición no válida para " + entry.Key + ".");
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
            Add("WindowedViewportX", "Resolución horizontal", "Anchura del backbuffer cuando el juego arranca en ventana. El mod usa esta superficie para construir la imagen VR.");
            Add("WindowedViewportY", "Resolución vertical", "Altura del backbuffer cuando el juego arranca en ventana. Para este mod se recomienda mantenerla igual que la anchura.");
            Add("FullscreenViewportX", "Resolución horizontal a pantalla completa", "Anchura usada al arrancar a pantalla completa. El lanzador la mantiene igual que la resolución de ventana para evitar cambios inesperados.");
            Add("FullscreenViewportY", "Resolución vertical a pantalla completa", "Altura usada al arrancar a pantalla completa. El lanzador la mantiene igual que la resolución de ventana.");
            Add("MenuViewportX", "Anchura interna de los menús del juego", "Resolución lógica de los menús 2D originales del juego. No controla el tamaño del menú F10 del mod.");
            Add("MenuViewportY", "Altura interna de los menús del juego", "Resolución lógica de los menús 2D originales del juego. No controla la posición del menú F10 del mod.");
            Add("StartupFullscreen", "Arrancar a pantalla completa", "Decide si el juego intenta abrirse a pantalla completa. Para esta instalación VR, el modo de ventana (False) es la ruta probada; el modo exclusivo puede impedir que la resolución solicitada se aplique correctamente.");
            Add("Brightness", "Brillo", "Nivel de brillo de la imagen. Hay valores con escalas distintas en varias secciones; modifica solo el de la sección que realmente usa el juego.");
            Add("Contrast", "Contraste", "Nivel de contraste de la imagen. Puede afectar a la legibilidad dentro del visor.");
            Add("Gamma", "Gamma", "Curva de luminosidad de la imagen. Cambios grandes pueden ocultar detalle en sombras o quemar zonas claras dentro del visor.");
            Add("UseVSync", "Sincronización vertical", "Sincroniza la presentación con el refresco configurado. En VR puede añadir latencia o interferir con el ritmo del runtime; el efecto depende de la ruta gráfica activa.");
            Add("VSync", "Sincronización vertical del usuario", "Preferencia de VSync guardada por el juego. En VR puede influir en latencia y regularidad de fotogramas.");
            Add("DesiredRefreshRate", "Frecuencia de refresco solicitada", "Frecuencia que solicita el renderizador de escritorio. No cambia directamente los Hz del visor OpenXR.");
            Add("MinDesiredFrameRate", "Fotogramas mínimos deseados", "Objetivo interno usado por el motor para decisiones de rendimiento. No es el límite de refresco del visor.");
            Add("UseMultithreadedRendering", "Renderizado multihilo", "Permite al renderizador repartir trabajo entre hilos. Puede cambiar rendimiento y regularidad de fotogramas en VR.");
            Add("UseMultithreading", "Multihilo del motor", "Activa trabajo multihilo general del motor. Puede afectar al rendimiento y a la estabilidad del ritmo de fotogramas.");
            Add("ReduceMouseLag", "Reducir latencia del ratón", "Ruta especial del motor para reducir latencia de entrada. Con controladores VR normalmente no es necesaria y puede alterar el rendimiento.");
            Add("Sync Mouse To Framerate", "Sincronizar ratón con fotogramas", "Vincula la entrada del ratón al ritmo de renderizado. No suele ayudar a los controladores de movimiento.");
            Add("HorizontalFOV", "Campo de visión horizontal", "FOV horizontal guardado por el juego. La DLL VR personalizada instalada lo sustituye por el FOV completo del visor durante el juego.");
            Add("bHorizontalFOVLock", "Bloquear FOV horizontal", "Bloqueo guardado por el juego. En las pruebas, desactivarlo no eliminó el recorte; conviene mantenerlo como está porque la DLL VR corrige el FOV en vivo.");
            Add("HorizontalFOVLock", "Bloqueo horizontal del FOV", "Política del renderizador para el campo de visión. En esta instalación conviene mantenerla como está y dejar que la DLL VR aplique el FOV del visor.");
            Add("ControlSensitivity", "Sensibilidad del mando", "Nivel general de sensibilidad seleccionado en las opciones del juego.");
            Add("Sensitivity", "Sensibilidad", "Valor de sensibilidad guardado por el perfil del juego.");
            Add("MouseSensitivity", "Sensibilidad del ratón", "Sensibilidad del ratón. No controla directamente la velocidad de giro del mando VR, que se ajusta en vrpreset.ini.");
            Add("MouseAcceleration", "Aceleración del ratón", "Hace que el giro dependa también de la rapidez del movimiento del ratón.");
            Add("MouseSmoothing", "Suavizado del ratón", "Filtra la entrada del ratón. No es el suavizado del giro de los controladores VR.");
            Add("UseController", "Usar mando", "Habilita la ruta de mando del cliente. El mod genera entrada de mando a partir de los controladores VR.");
            Add("UseJoystick", "Usar joystick", "Habilita compatibilidad de joystick del cliente de Windows.");
            Add("CaptureMouse", "Capturar el ratón", "Mantiene el cursor capturado dentro de la ventana del juego. Puede influir al usar el menú F10 con ratón.");
            Add("AutoAim", "Ayuda automática de apuntado", "Asistencia de apuntado del juego. El mod dispone además de lockOnDisabled para impedir el magnetismo en VR.");
            Add("WantsXboxController", "Preferir mando Xbox", "Hace que el juego use interfaz y entrada de mando. Es coherente con la emulación de mando del mod VR.");
            Add("InvertYAxis", "Invertir eje vertical", "Invierte el eje vertical de la entrada tradicional.");
            Add("Vibration", "Vibración del mando", "Activa la vibración solicitada por el juego; su traducción a controladores VR depende del mod/runtime.");
            Add("bMaintainUIScale", "Mantener escala de interfaz", "Intenta conservar la escala del HUD original al cambiar de resolución. No controla el panel HUD OpenXR ni el menú F10 del mod.");
            Add("MouseIconScale", "Tamaño del puntero", "Escala del icono del ratón en la interfaz del juego.");
            Add("ScreenFlashes", "Destellos de pantalla", "Permite destellos de daño y otros efectos. En VR pueden resultar intensos o molestos.");
            Add("Decals", "Calcomanías", "Activa marcas como impactos y manchas. Afecta principalmente a calidad y rendimiento.");
            Add("NoDynamicLights", "Desactivar luces dinámicas", "Si está activado elimina luces dinámicas y mejora rendimiento a costa de calidad visual.");
            Add("NoLighting", "Desactivar iluminación", "Deshabilita iluminación del motor. Es un ajuste de diagnóstico y no se recomienda para jugar.");
            Add("LevelOfAnisotropy", "Filtrado anisotrópico", "Mejora la nitidez de texturas vistas en ángulo. Valores altos consumen algo más de GPU.");
            Add("UseTrilinear", "Filtrado trilineal", "Suaviza transiciones entre niveles de detalle de las texturas.");
            Add("UsePrecaching", "Precarga de recursos", "Precarga datos para reducir tirones posteriores, con mayor uso de memoria y carga inicial.");
            Add("HighDetailActors", "Personajes con alto detalle", "Usa versiones detalladas de actores y personajes.");
            Add("SuperHighDetailActors", "Personajes con detalle máximo", "Activa el nivel de detalle más alto de los actores.");
            Add("UseHighDetailShadowMaps", "Sombras de alta calidad", "Usa mapas de sombra con mayor detalle y coste de GPU.");
            Add("Shadows", "Sombras", "Activa las sombras del motor. Puede tener impacto apreciable en VR.");
            Add("RealTimeReflection", "Reflejos en tiempo real", "Activa reflejos dinámicos; mejora la imagen y aumenta la carga gráfica.");
            Add("PostProcessing", "Posprocesado", "Activa efectos aplicados después del renderizado. Algunos pueden resultar incómodos o costosos en VR.");
            Add("UseDistortion", "Distorsión gráfica del juego", "Activa distorsiones visuales del motor. No es la corrección óptica del visor, que realiza el runtime VR.");
            Add("UseFxaa", "Antialiasing FXAA", "Suavizado de bordes por posprocesado. Es ligero, aunque puede reducir nitidez dentro del visor.");
            Add("UseSoftwareAntiAliasing", "Antialiasing por software", "Suavizado adicional de bordes del motor. Puede afectar a nitidez y rendimiento.");
            Add("HardwareOcclusion", "Oclusión por hardware", "Evita dibujar geometría no visible. Normalmente mejora el rendimiento.");
            Add("TextureDetail", "Detalle general de texturas", "Calidad general de texturas. Valores altos consumen más memoria de vídeo.");
            Add("FluidSurfaceDetail", "Detalle de líquidos", "Calidad de las superficies de agua y otros fluidos.");
            Add("DynamicShadowDetail", "Detalle de sombras dinámicas", "Nivel de calidad de las sombras que cambian en tiempo real.");
            Add("RenderDetail", "Detalle de renderizado", "Nivel general de detalle del renderizador.");
            Add("UseHighDetailPostProcEffects", "Posprocesado de alta calidad", "Usa versiones más detalladas de los efectos posteriores al renderizado.");
            Add("UseLinearSpace", "Espacio de color lineal", "Realiza determinadas operaciones de iluminación y mezcla en espacio lineal. Cambiarlo puede alterar mucho la imagen.");
            Add("OverrideDesktopRefreshRate", "Ignorar refresco del Escritorio", "Permite al juego imponer otra frecuencia al monitor. No controla el refresco del visor.");
            Add("AvoidHitches", "Evitar tirones", "Activa una estrategia del renderizador para reducir parones; puede cambiar uso de memoria o carga previa.");
            Add("SpeakerMode", "Configuración de altavoces", "Distribución de canales elegida por el juego. Para auriculares VR suele convenir estéreo o la opción recomendada por el runtime.");
            Add("Use3DSound", "Audio posicional 3D", "Activa la ruta de sonido 3D del motor. Puede ser relevante para la orientación espacial en VR.");
            Add("ReverseStereo", "Intercambiar canales estéreo", "Intercambia izquierda y derecha. Déjalo desactivado salvo que los canales estén realmente invertidos.");
            Add("MasterVolume", "Volumen general", "Volumen maestro del juego.");
            Add("SFXVolume", "Volumen de efectos", "Volumen de efectos de sonido.");
            Add("MusicVolume", "Volumen de música", "Volumen de la música.");
            Add("VoVolume", "Volumen de voces", "Volumen de diálogos y voces.");
            Add("DialogSubtitles", "Subtítulos de diálogos", "Muestra subtítulos de las conversaciones.");
            Add("ArtSubtitles", "Subtítulos de material artístico", "Muestra textos o subtítulos adicionales asociados al contenido del juego.");
            Add("language", "Idioma", "Código o entrada de idioma usada por la sección correspondiente.");
            Add("Coronas", "Halos de luz", "Activa halos alrededor de determinadas fuentes de luz. Puede añadir algo de carga gráfica y resultar intenso en VR.");
            Add("DecoLayers", "Capas decorativas", "Activa capas visuales decorativas del mundo. Desactivarlas puede reducir detalle y algo de carga gráfica.");
            Add("Projectors", "Proyectores visuales", "Activa efectos proyectados sobre superficies, como luces o marcas. Puede afectar a calidad y rendimiento.");
            Add("ReportDynamicUploads", "Registrar cargas dinámicas", "Opción de diagnóstico del renderizador para informar de recursos enviados dinámicamente; no suele mejorar la experiencia VR.");
            Add("TextureDetailInterface", "Detalle de texturas de interfaz", "Calidad de las texturas de la interfaz original del juego. No cambia el tamaño del menú F10 del mod.");
            Add("TextureDetailTerrain", "Detalle de texturas del terreno", "Calidad de las texturas aplicadas al terreno y superficies del escenario.");
            Add("TextureDetailWeaponSkin", "Detalle de texturas de armas", "Calidad de las texturas de los modelos de armas, muy visibles de cerca en VR.");
            Add("TextureDetailPlayerSkin", "Detalle de texturas del jugador", "Calidad de las texturas asociadas al modelo del jugador.");
            Add("TextureDetailWorld", "Detalle de texturas del mundo", "Calidad de las texturas generales del escenario; valores altos consumen más memoria de vídeo.");
            Add("TextureDetailRenderMap", "Detalle de texturas renderizadas", "Calidad de superficies que reciben imágenes generadas por el juego.");
            Add("TextureDetailLightmap", "Detalle de mapas de luz", "Calidad de las texturas de iluminación precalculada del escenario.");
            Add("NoFractalAnim", "Desactivar animación fractal", "Deshabilita ciertas animaciones procedurales. Puede reducir movimiento visual y algo de carga gráfica.");
            Add("ScaleHUDX", "Escala horizontal del HUD original", "Ajuste horizontal del HUD 2D del juego. No controla el panel OpenXR ni la posición del menú F10 del mod.");
            Add("MouseXMultiplier", "Multiplicador horizontal del ratón", "Multiplica el movimiento horizontal del ratón; no es la velocidad de giro configurada en vrpreset.ini.");
            Add("MouseYMultiplier", "Multiplicador vertical del ratón", "Multiplica el movimiento vertical del ratón; no controla directamente los mandos VR.");
            Add("WindowedViewportXPos", "Posición horizontal de la ventana", "Coordenada horizontal de la ventana de escritorio. No desplaza la imagen dentro del visor.");
            Add("WindowedViewportYPos", "Posición vertical de la ventana", "Coordenada vertical de la ventana de escritorio. No desplaza la imagen dentro del visor.");
            Add("WindowedViewportXPosEditor", "Posición X de la ventana del editor", "Posición interna reservada para herramientas del motor; no afecta al juego normal ni a VR.");
            Add("WindowedViewportYPosEditor", "Posición Y de la ventana del editor", "Posición interna reservada para herramientas del motor; no afecta al juego normal ni a VR.");
            Add("MaxChannels", "Máximo de canales de audio", "Número máximo de sonidos simultáneos. Reducirlo puede cortar sonidos; aumentarlo consume más recursos.");
            Add("MaxStreams", "Máximo de flujos de audio", "Cantidad máxima de pistas de audio transmitidas simultáneamente desde disco.");
            Add("StreamBufferSize", "Búfer de audio en streaming", "Tamaño del búfer usado para audio transmitido. Cambiarlo puede afectar a cortes, latencia y memoria.");
            Add("AdapterNumber", "Adaptador gráfico", "Índice de la GPU elegida por esta ruta de renderizado. -1 deja que el motor seleccione el adaptador; un índice erróneo puede impedir el arranque.");
            Add("TesselationFactor", "Factor de teselación", "Factor interno de detalle geométrico de esta ruta de renderizado. El efecto real depende del renderizador activo.");
            Add("CheckForOverflow", "Comprobar desbordamientos", "Comprobación interna de diagnóstico del renderizador; puede afectar al rendimiento y no suele activarse para jugar.");
            Add("BatchRenderFlash", "Agrupar renderizado Flash", "Agrupa el dibujo de la interfaz Flash del juego. Alterarlo puede afectar a menús y HUD.");
            Add("DetailTextures", "Texturas de detalle", "Añade capas de textura fina en superficies cercanas; mejora detalle a costa de algo de GPU y memoria.");
            Add("HDRSceneExpBias", "Compensación de exposición HDR", "Ajusta la exposición base de la escena HDR. Cambios grandes pueden empeorar la visibilidad y comodidad en VR.");
            Add("MaxSkeletalProjectorsPerActor", "Proyectores por personaje", "Límite de efectos proyectados sobre cada actor animado. Valores altos pueden aumentar la carga gráfica.");
            Add("StreamingDistanceScale", "Distancia de carga de recursos", "Escala la distancia usada para cargar recursos visuales. Puede afectar a nitidez, memoria y tirones en VR.");
            Add("HighDetailShaders", "Shaders de alto detalle", "Activa versiones de mayor calidad de los shaders, con mayor coste de GPU.");
            Add("UseRippleSystem", "Ondulaciones del agua", "Activa el sistema de ondas y perturbaciones de las superficies de agua.");
            Add("UseHighDetailSoftParticles", "Partículas suaves de alta calidad", "Usa partículas con transiciones más suaves contra la geometría; mejora calidad y aumenta carga gráfica.");
            Add("UseSpecCubeMap", "Reflejos especulares por cubemap", "Activa reflejos aproximados mediante mapas cúbicos. Puede mejorar materiales metálicos y mojados.");
            Add("ForceGlobalLighting", "Forzar iluminación global", "Fuerza una ruta global de iluminación del motor. Es un ajuste avanzado que puede cambiar mucho la imagen.");
            Add("CascadingWaterSimulationVelocity", "Velocidad de simulación del agua", "Controla la velocidad interna de determinados efectos de agua en cascada.");
            Add("MovementStick", "Palanca de movimiento", "Indica qué palanca del mando tradicional usa el juego para el movimiento. El mod traduce sus controles VR a esta entrada.");
            Add("AutoCenter", "Centrado automático", "Hace que la vista o la entrada tradicional tienda a recentrarse. En VR puede sentirse artificial.");
            Add("bReverb", "Reverberación", "Activa reverberación ambiental, útil para la sensación espacial de las estancias.");
            Add("bEAXEnabled", "Efectos de audio EAX", "Activa la antigua ruta de efectos EAX si está disponible; normalmente no es necesaria con audio moderno del visor.");
            Add("GameDifficulty", "Dificultad", "Nivel de dificultad de la partida guardado por el juego.");
            Add("AdaptiveTraining", "Ayudas de aprendizaje adaptativas", "Permite que el juego adapte determinados consejos o ayudas al progreso del jugador.");
            Add("NoVitaChamber", "Desactivar Vita-Cámaras", "Impide la reaparición mediante Vita-Cámaras cuando está activado.");
            Add("QuestArrow", "Flecha de objetivo", "Muestra la ayuda direccional hacia el objetivo dentro de la interfaz del juego.");
            Add("bShowShimmer", "Resaltar objetos interactivos", "Muestra un brillo visual sobre ciertos objetos; puede facilitar localizarlos dentro del visor.");
            Add("bHighlightFocussedItems", "Resaltar elementos enfocados", "Destaca los objetos a los que apunta o enfoca el jugador.");
            Add("UseGamePlusData", "Usar datos de Nueva Partida+", "Permite que el juego reutilice datos compatibles de una partida terminada.");
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
                    description = "Ajuste interno del motor o de una plataforma distinta. No hay una interpretación segura para VR; modifícalo solo si sabes exactamente qué espera Unreal Engine.";
                else if (entry.Impact == IniImpact.Performance)
                    description = "Ajuste gráfico o de rendimiento del motor. Puede cambiar calidad, consumo de GPU/CPU o regularidad de fotogramas en VR; prueba un solo cambio cada vez.";
                else if (entry.Impact == IniImpact.VrDirect)
                    description = "Ajuste relacionado con la imagen, entrada o interfaz que puede sentirse directamente dentro del visor. Guarda una copia y comprueba el resultado con cuidado.";
                else if (IsBoolean(entry.Value))
                    description = "Activa o desactiva este comportamiento del juego. Es una opción general y no se ha identificado un efecto VR directo.";
                else
                    description = "Valor de configuración general del juego. No se ha identificado un efecto VR directo; se conserva visible para ofrecer acceso al Bioshock.ini completo.";
            }
            if (consoleSection)
                description = "Copia para consola de «" + entry.FriendlyName +
                    "». Esta sección no afecta a la versión de Windows ni al mod VR instalado; se muestra únicamente para que Bioshock.ini esté completo.";
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
                entry.Description = "Ajuste de una ruta alternativa que este Bioshock.ini no tiene seleccionada. " +
                    "La ruta activa es [" + selected + "]; cambiar este valor normalmente no afectará al juego ni a VR. " +
                    "Se mantiene protegido para evitar confundir una copia inactiva con el ajuste efectivo.";
            }
        }

        public static string ImpactText(IniImpact impact)
        {
            if (impact == IniImpact.VrDirect) return "VR DIRECTO";
            if (impact == IniImpact.Performance) return "REL. VR";
            if (impact == IniImpact.Warning) return "AVISO";
            return "GENERAL";
        }

        private static bool IsBoolean(string value)
        {
            return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "False", StringComparison.OrdinalIgnoreCase);
        }

        private static string Humanize(string key)
        {
            if (string.IsNullOrEmpty(key)) return "Ajuste sin nombre";
            string result = key.Replace('_', ' ');
            result = Regex.Replace(result, "([a-z0-9])([A-Z])", "$1 $2");
            result = Regex.Replace(result, "\\s+", " ").Trim();
            return "Ajuste: " + result;
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
        private readonly List<ParamDef> _definitions;
        private readonly Dictionary<string, Control> _editors;
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
        private bool _launchPending;
        private DateTime _launchDeadlineUtc;
        private Timer _launchWatchTimer;
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
                "BioShockLauncherSelfTest-" + Guid.NewGuid().ToString("N")) : null;
            _configPath = Path.Combine(
                isolated ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BioshockVR", "vrpreset.ini");
            _upscalerConfigPath = Path.Combine(
                isolated ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BioshockVR", "upscaler.ini");
            _dlssConfigPath = Path.Combine(
                isolated ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BioshockVR", "dlss.ini");
            UiLanguage.Initialize(_dlssConfigPath);
            _gameIniPath = Path.Combine(
                isolated ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BioshockHD", "Bioshock", "Bioshock.ini");
            _definitions = BuildDefinitions();
            foreach (ParamDef definition in _definitions) UiLanguage.Localize(definition);
            _editors = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
            _gameIniEntries = new List<IniEntry>();
            _toolTip = new ToolTip();
            _toolTip.AutoPopDelay = 12000;
            _toolTip.InitialDelay = 350;
            _toolTip.ReshowDelay = 100;

            Text = "BioShock VR · DLSS/DLAA 0.2.12";
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
            UiLanguage.Apply(this);
            if (UiLanguage.IsEnglish)
                Application.Idle += delegate { if (!IsDisposed) UiLanguage.Apply(this); };
            if (selfTest)
            {
                _dlssBackendStatus = new DlssBackendStatus();
                _dlssBackendStatus.Summary = "Autoprueba sin archivos de usuario";
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

            const string camera = "Cámara y escala";
            p.Add(ParamDef.Number("worldScale", camera, "Escala del mundo",
                "Unidades del juego por metro real. Subirla hace que el mundo se perciba más pequeño; bajarla, más grande. También cambia cuánto avanzas al moverte físicamente.",
                "UU/metro", 10, 200, 1, 1, "100.0"));
            p.Add(ParamDef.Number("headUpUu", camera, "Altura adicional de la cabeza",
                "Desplaza el punto de vista verticalmente. Un valor positivo te eleva; uno negativo te baja, sin cambiar el tamaño del mundo.",
                "UU", -150, 150, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("headFwdUu", camera, "Desplazamiento frontal de la cabeza",
                "Mueve el punto de vista hacia delante o hacia atrás respecto al cuerpo. Positivo significa hacia delante.",
                "UU", -80, 80, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("ipdMm", camera, "Distancia interpupilar virtual (IPD)",
                "Separación entre las cámaras de ambos ojos. Normalmente debe coincidir con la IPD del visor; un valor incorrecto altera la escala percibida y puede cansar la vista.",
                "mm", 55, 75, 0.5m, 1, "63.0"));
            p.Add(ParamDef.Number("gameFovDeg", camera, "FOV objetivo del juego (avanzado)",
                "Ángulo horizontal que el mod puede escribir en el juego. La DLL personalizada instalada prioriza el FOV completo del visor, por lo que normalmente no necesitas tocar este valor.",
                "grados", 75, 150, 1, 1, "130.0"));

            const string hands = "Manos y apuntado";
            p.Add(ParamDef.Number("handScaleL", hands, "Tamaño de la mano izquierda",
                "Multiplicador visual de la mano de plásmidos. 1,00 conserva el tamaño base; 1,10 la hace un 10 % mayor.",
                "multiplicador", 0.2m, 4.0m, 0.01m, 3, "1.000"));
            p.Add(ParamDef.Number("handScaleR", hands, "Tamaño de la mano derecha",
                "Multiplicador visual de la mano del arma. 1,00 conserva el tamaño base; 1,10 la hace un 10 % mayor.",
                "multiplicador", 0.2m, 4.0m, 0.01m, 3, "1.000"));
            p.Add(ParamDef.Number("wScale", hands, "Tamaño de las armas",
                "Escala uniforme del modelo del arma alrededor de la empuñadura. No modifica el tamaño de las manos ni del mundo.",
                "multiplicador", 0.3m, 2.5m, 0.01m, 3, "1.000"));
            p.Add(ParamDef.Number("aimTrimLPitch", hands, "Inclinación del apuntado izquierdo",
                "Corrige arriba/abajo la dirección de disparo de la mano izquierda (plásmidos). No mueve el modelo de la mano.",
                "grados", -90, 90, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("aimTrimLYaw", hands, "Giro del apuntado izquierdo",
                "Corrige a izquierda/derecha la dirección de disparo de la mano izquierda (plásmidos).",
                "grados", -90, 90, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("aimTrimRPitch", hands, "Inclinación del apuntado derecho",
                "Corrige arriba/abajo la dirección de disparo de la mano derecha (armas). No mueve el modelo del arma.",
                "grados", -90, 90, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("aimTrimRYaw", hands, "Giro del apuntado derecho",
                "Corrige a izquierda/derecha la dirección de disparo de la mano derecha (armas).",
                "grados", -90, 90, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosLFwd", hands, "Origen izquierdo: delante/atrás",
                "Desplaza el punto desde el que nace el disparo del plásmido a lo largo de la dirección de la mano. No desplaza la mano visible.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosLRight", hands, "Origen izquierdo: lateral",
                "Desplaza lateralmente el origen del disparo izquierdo. Positivo va hacia la derecha local de la mano.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosLUp", hands, "Origen izquierdo: altura",
                "Sube o baja el origen del disparo izquierdo respecto a la mano. Positivo significa arriba.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosRFwd", hands, "Origen derecho: delante/atrás",
                "Desplaza el punto desde el que nace el disparo del arma a lo largo de la dirección de la mano. No desplaza el arma visible.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosRRight", hands, "Origen derecho: lateral",
                "Desplaza lateralmente el origen del disparo del arma. Positivo va hacia la derecha local de la mano.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Number("aimPosRUp", hands, "Origen derecho: altura",
                "Sube o baja el origen del disparo del arma respecto a la mano. Positivo significa arriba.",
                "cm", -30, 30, 0.1m, 1, "0.0"));
            p.Add(ParamDef.Boolean("laserOn", hands, "Láser de apuntado",
                "Muestra una línea de puntos a lo largo del rayo real de apuntado. Es útil para calibrar; puede dejarse apagado al jugar.", false));
            p.Add(ParamDef.Boolean("aimDotOn", hands, "Punto de mira del mod",
                "Muestra un punto en la trayectoria real del disparo, independiente de la cruceta plana del juego.", false));
            p.Add(ParamDef.Number("aimDotDistM", hands, "Distancia del punto de mira",
                "Distancia a la que aparece el punto de mira del mod. Solo se aprecia cuando el punto está activado.",
                "metros", 0.5m, 20, 0.1m, 2, "5.00"));
            p.Add(ParamDef.Number("aimDotSizeDeg", hands, "Tamaño del punto de mira",
                "Diámetro angular del punto; al medirse en grados conserva un tamaño aparente parecido a distintas distancias.",
                "grados", 0.1m, 3.0m, 0.05m, 2, "0.50"));

            const string movement = "Movimiento y giro";
            p.Add(ParamDef.Boolean("autoVr", movement, "Activar el modo VR automáticamente",
                "Aplica la configuración VR completa al iniciar el juego. Conviene mantenerlo activado para usar este lanzador.", true));
            p.Add(ParamDef.Number("bodyRate", movement, "Suavidad con la que gira el cuerpo",
                "Velocidad a la que el cuerpo alcanza la orientación de la cabeza. 0 hace el seguimiento instantáneo; valores mayores lo hacen progresivo.",
                "por segundo", 0, 10, 0.05m, 2, "0.00"));
            p.Add(ParamDef.Number("bodyDeadzoneDeg", movement, "Zona muerta del cuerpo",
                "Diferencia de giro de la cabeza que se permite antes de que el cuerpo empiece a acompañarla. Más alta reduce pequeñas correcciones, pero puede hacer el inicio más brusco.",
                "grados", 0, 60, 0.5m, 1, "0.0"));
            p.Add(ParamDef.Boolean("moveDirInstant", movement, "Dirección de movimiento inmediata",
                "Hace que la dirección al caminar siga la cabeza inmediatamente durante giros rápidos.", true));
            p.Add(ParamDef.Number("turnScale", movement, "Velocidad del giro suave",
                "Multiplicador aplicado al giro con la palanca. 1,00 es la velocidad base; menor gira más despacio y mayor, más rápido.",
                "multiplicador", 0.1m, 4.0m, 0.05m, 2, "1.00"));
            p.Add(ParamDef.Boolean("snapTurn", movement, "Giro por pasos",
                "Sustituye el giro continuo por saltos de un ángulo fijo. Desactivado mantiene el giro suave.", false));
            p.Add(ParamDef.Number("snapAngleDeg", movement, "Ángulo de cada paso",
                "Número de grados de cada giro discreto. Solo tiene efecto si está activado el giro por pasos.",
                "grados", 5, 180, 5, 0, "45"));

            const string swing = "Ataque por gesto";
            p.Add(ParamDef.Boolean("swingOn", swing, "Atacar al blandir la llave inglesa",
                "Convierte un movimiento rápido de la mano derecha en una pulsación de ataque cuando llevas la llave inglesa.", true));
            p.Add(ParamDef.Number("swingThreshold", swing, "Velocidad necesaria del gesto",
                "Velocidad mínima que debe alcanzar la mano para atacar. Subirla exige un gesto más fuerte y reduce activaciones accidentales.",
                "m/s", 0.3m, 10, 0.05m, 2, "3.60"));
            p.Add(ParamDef.Number("swingRearm", swing, "Velocidad para rearmar el gesto",
                "La mano debe volver a bajar de esta velocidad antes de aceptar otro golpe. Debe ser menor que la velocidad necesaria del gesto.",
                "m/s", 0.05m, 9, 0.05m, 2, "1.00"));
            p.Add(ParamDef.Number("swingCooldownMs", swing, "Espera entre golpes",
                "Tiempo mínimo entre dos ataques generados por el gesto. Aumentarlo evita golpes dobles.",
                "ms", 0, 2000, 10, 0, "300"));
            p.Add(ParamDef.Number("swingPulseMs", swing, "Duración de la pulsación de ataque",
                "Cuánto tiempo simula el mod que el gatillo permanece pulsado. Si es demasiado corto, el juego puede no detectar el golpe.",
                "ms", 20, 500, 10, 0, "120"));
            p.Add(ParamDef.Number("swingDelayMs", swing, "Retraso antes de atacar",
                "Espera entre detectar el gesto y pulsar el ataque. 0 responde inmediatamente.",
                "ms", 0, 400, 10, 0, "0"));
            p.Add(ParamDef.Boolean("swingHeadRel", swing, "Medir el gesto respecto a la cabeza",
                "Resta el movimiento general de la cabeza al calcular la velocidad de la mano, reduciendo ataques accidentales al mover todo el cuerpo.", true));

            const string cinema = "Cinemáticas y efectos";
            p.Add(ParamDef.Boolean("cineBarsHidden", cinema, "Ocultar bandas negras de las cinemáticas",
                "Omite las bandas panorámicas; la imagen que hay debajo sigue completa, sin recorte ni estiramiento.", true));
            p.Add(ParamDef.Choice("cineDrive", cinema, "Comportamiento durante cinemáticas",
                "Elige si la cámara VR sigue mandando, si se respeta totalmente la cámara dirigida del juego o si se permite mirar con la cabeza sobre esa cámara.",
                new string[] {
                    "0 · La VR sigue controlando la cámara",
                    "1 · Cámara y manos dirigidas por el juego",
                    "2 · Cámara del juego + movimiento de cabeza"
                }, 1));
            p.Add(ParamDef.Boolean("cineSubsInFrame", cinema, "Subtítulos dentro de la imagen 3D",
                "Activado incrusta los subtítulos en cada imagen ocular y pueden verse dobles. Desactivado los deja en el panel HUD, normalmente más legibles.", false));
            p.Add(ParamDef.Boolean("effectsInFrame", cinema, "Efectos de pantalla en toda la vista",
                "Coloca agua, daño y destellos sobre la vista completa en vez del panel HUD. Puede afectar también a algunos rellenos de las barras de salud y EVE.", false));
            p.Add(ParamDef.Number("effectMaxVerts", cinema, "Límite de vértices para efectos (avanzado)",
                "Máximo de vértices de un dibujo sin textura que el mod trata como efecto de pantalla. El valor 8 es el ajuste probado; conviene no cambiarlo salvo diagnóstico.",
                "vértices", 3, 100, 1, 0, "8"));
            p.Add(ParamDef.Boolean("postFxRtOnly", cinema, "Filtrar efectos por origen renderizado",
                "Mantiene desenfoques como el del alcohol en la vista y evita confundir texturas normales del HUD con efectos. Activado es el ajuste recomendado.", true));

            const string hud = "HUD y ayudas";
            p.Add(ParamDef.Boolean("lockOnDisabled", hud, "Desactivar ayuda magnética de apuntado",
                "Evita que la asistencia de mando arrastre el apuntado hacia los enemigos. Activado suele ser más natural con controladores de movimiento.", true));
            p.Add(ParamDef.Boolean("crosshairVisible", hud, "Mostrar la cruceta plana del juego",
                "Vuelve a mostrar la cruceta 2D original. Es independiente del punto de mira tridimensional del mod.", false));
            p.Add(ParamDef.Number("hudQuadDistM", hud, "Distancia del panel HUD",
                "Distancia del panel flotante respecto a los ojos. Más lejos reduce su tamaño aparente si no aumentas también la anchura.",
                "metros", 0.5m, 3.0m, 0.05m, 2, "1.30"));
            p.Add(ParamDef.Number("hudQuadWidthM", hud, "Anchura del panel HUD",
                "Anchura física del panel flotante. Aumentarla hace más grande el HUD dentro del visor.",
                "metros", 0.3m, 3.0m, 0.05m, 2, "1.25"));
            p.Add(ParamDef.Number("hudQuadUpM", hud, "Altura del panel HUD",
                "Desplazamiento vertical del panel. Positivo lo sube y negativo lo baja.",
                "metros", -1.0m, 1.0m, 0.05m, 2, "-0.10"));

            return p;
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
            title.Text = "BioShock VR · DLSS/DLAA";
            title.ForeColor = SystemColors.ControlText;
            title.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(8, 2);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Mod original de Mohamad Balouza · Fork DLSS/DLAA de Beren5556";
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

            _launchButton = MakeButton("Guardar e iniciar", Blue, Color.White, 130);
            _launchButton.Click += delegate { SaveAndLaunch(); };
            buttons.Controls.Add(_launchButton);

            _saveButton = MakeButton("Guardar", SystemColors.Control, SystemColors.ControlText, 72);
            _saveButton.Click += delegate { SaveConfiguration(true); };
            buttons.Controls.Add(_saveButton);

            Button reload = MakeButton("Recargar", SystemColors.Control, SystemColors.ControlText, 75);
            reload.Click += delegate { LoadConfiguration(true); };
            buttons.Controls.Add(reload);

            _statusLabel = new Label();
            _statusLabel.Text = "Preparando configuración…";
            _statusLabel.ForeColor = Muted;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.SetBounds(8, 2, 655, 18);
            _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            footer.Controls.Add(_statusLabel);
            ContextMenuStrip fileMenu = new ContextMenuStrip();
            fileMenu.Items.Add(UiLanguage.Text("Abrir vrpreset.ini", "Open vrpreset.ini"), null, delegate { OpenConfigurationFile(); });
            fileMenu.Items.Add(UiLanguage.Text("Abrir Bioshock.ini", "Open Bioshock.ini"), null, delegate { OpenGameIniFile(); });
            fileMenu.Items.Add(UiLanguage.Text("Ver copias de seguridad", "View backups"), null, delegate { OpenBackupFolder(); });
            fileMenu.Items.Add(new ToolStripSeparator());
            fileMenu.Items.Add(UiLanguage.Text("Créditos y licencias", "Credits and licenses"), null, delegate { ShowCreditsAndLicenses(); });
            if (!FinalDlssEdition)
                fileMenu.Items.Add(UiLanguage.Text("Abrir upscaler.ini", "Open upscaler.ini"), null, delegate { OpenUpscalerConfigurationFile(); });
            Button files = MakeButton(UiLanguage.Text("Archivos y ayuda ▾", "Files and help ▾"), SystemColors.Control, SystemColors.ControlText, 135);
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
                "Cámara y escala", "Manos y apuntado", "Movimiento y giro",
                "Ataque por gesto", "Cinemáticas y efectos", "HUD y ayudas"
            };
            AddResolutionTab();
            foreach (string category in categoryOrder)
                AddCategoryTab(category);
            InitializeHiddenIniEditor();

            KeyPreview = true;
            KeyDown += MainForm_KeyDown;
            FormClosing += MainForm_FormClosing;
            FormClosed += delegate { StopLaunchWatch(); };
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

        private static string ShortCategoryName(string category)
        {
            if (category == "Cámara y escala") return "Cámara";
            if (category == "Manos y apuntado") return "Manos";
            if (category == "Movimiento y giro") return "Giro";
            if (category == "Ataque por gesto") return "Gestos";
            if (category == "Cinemáticas y efectos") return "Cine";
            if (category == "HUD y ayudas") return "HUD";
            return category;
        }

        // Retain the tested configuration controls/handlers as backing state.
        // ImageTab.cs supplies the simplified public view; other tabs are unchanged.
        private void InitializeImageBackingControls()
        {
            TabPage page = new TabPage("Imagen");
            page.Name = "Imagen y resolución";
            page.UseVisualStyleBackColor = true;
            page.Padding = new Padding(10);
            page.AutoScroll = true;

            Label title = new Label();
            title.Text = "Imagen VR · Normal, DLAA y DLSS 4.5";
            title.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
            title.ForeColor = Navy;
            title.AutoSize = true;
            title.Location = new Point(14, 12);
            page.Controls.Add(title);

            Label explanation = new Label();
            explanation.Text = "Arriba se muestra el render del juego. El perfil de salida VR y la calidad DLSS de abajo ofrecen los mismos tramos que los controles dentro del juego. Normal y DLAA trabajan al 100 %.";
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

            layout.Controls.Add(MakeCompactLabel("Perfil"), 0, 0);
            _resolutionPreset = new ComboBox();
            _resolutionPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            _resolutionPreset.Width = 245;
            _resolutionPreset.Items.Add("Personalizada / actual");
            _resolutionPreset.Items.Add("1920 × 1080 · juego plano");
            foreach (int size in SquareResolutionSteps)
                _resolutionPreset.Items.Add(size + " × " + size);
            _resolutionPreset.SelectedIndexChanged += ResolutionPresetChanged;
            layout.SetColumnSpan(_resolutionPreset, 3);
            layout.Controls.Add(_resolutionPreset, 1, 0);

            layout.Controls.Add(MakeCompactLabel("Anchura"), 0, 1);
            _resolutionWidth = MakeResolutionNumber();
            _resolutionWidth.ValueChanged += ResolutionValueChanged;
            layout.Controls.Add(_resolutionWidth, 1, 1);
            layout.Controls.Add(MakeCompactLabel("Altura"), 2, 1);
            _resolutionHeight = MakeResolutionNumber();
            _resolutionHeight.ValueChanged += ResolutionValueChanged;
            layout.Controls.Add(_resolutionHeight, 3, 1);

            _squareResolution = new CheckBox();
            _squareResolution.Text = "Mantener resolución cuadrada (recomendado para VR)";
            _squareResolution.Checked = true;
            _squareResolution.AutoSize = true;
            _squareResolution.Padding = new Padding(0, 6, 0, 0);
            _squareResolution.CheckedChanged += SquareResolutionChanged;
            layout.SetColumnSpan(_squareResolution, 4);
            layout.Controls.Add(_squareResolution, 0, 2);

            _resolutionLoadLabel = new Label();
            _resolutionLoadLabel.Text = "Resolución pendiente de cargar…";
            _resolutionLoadLabel.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _resolutionLoadLabel.ForeColor = Blue;
            _resolutionLoadLabel.AutoSize = true;
            _resolutionLoadLabel.Location = new Point(16, 190);
            page.Controls.Add(_resolutionLoadLabel);

            GroupBox fxaaGroup = new GroupBox();
            fxaaGroup.Text = "FXAA DEL JUEGO · NO ES DLSS";
            fxaaGroup.ForeColor = Navy;
            fxaaGroup.Location = new Point(14, 222);
            fxaaGroup.Size = new Size(820, 108);
            fxaaGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            fxaaGroup.Visible = !FinalDlssEdition;
            page.Controls.Add(fxaaGroup);

            _fxaaEnabled = new CheckBox();
            _fxaaEnabled.Text = "Activar FXAA del juego";
            _fxaaEnabled.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _fxaaEnabled.ForeColor = TextDark;
            _fxaaEnabled.AutoSize = true;
            _fxaaEnabled.Location = new Point(12, 21);
            _fxaaEnabled.CheckedChanged += FxaaChanged;
            fxaaGroup.Controls.Add(_fxaaEnabled);

            Label fxaaExplanation = new Label();
            fxaaExplanation.Text = "Suaviza dientes de sierra antes de que el mod copie la imagen al visor. Puede reducir aliasing y algo de parpadeo espacial, a cambio de una ligera pérdida de nitidez. No es TAA ni DLSS.";
            fxaaExplanation.ForeColor = Muted;
            fxaaExplanation.AutoSize = true;
            fxaaExplanation.MaximumSize = new Size(760, 0);
            fxaaExplanation.Location = new Point(32, 45);
            fxaaGroup.Controls.Add(fxaaExplanation);

            _fxaaStateLabel = new Label();
            _fxaaStateLabel.Text = "Estado pendiente de cargar…";
            _fxaaStateLabel.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
            _fxaaStateLabel.ForeColor = Blue;
            _fxaaStateLabel.AutoSize = true;
            _fxaaStateLabel.Location = new Point(32, 79);
            fxaaGroup.Controls.Add(_fxaaStateLabel);

            Label warning = new Label();
            warning.Text = "AVISO VR  ·  Un 10 % más de resolución por eje supone aproximadamente un 21 % más de píxeles. Los cambios se aplican al siguiente inicio y deben guardarse con BioShock cerrado. Se recomienda StartupFullscreen=False.";
            warning.BackColor = SystemColors.Info;
            warning.ForeColor = SystemColors.InfoText;
            warning.Padding = new Padding(10);
            warning.AutoSize = true;
            warning.MaximumSize = new Size(820, 0);
            warning.Location = new Point(14, FinalDlssEdition ? 222 : 344);
            page.Controls.Add(warning);

            GroupBox upscalerGroup = new GroupBox();
            upscalerGroup.Text = "REESCALADO ESPACIAL EXPERIMENTAL · SIN DLSS / SIN DLAA";
            upscalerGroup.ForeColor = Navy;
            upscalerGroup.Location = new Point(14, 416);
            upscalerGroup.Size = new Size(820, 158);
            upscalerGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            upscalerGroup.Visible = !FinalDlssEdition;
            page.Controls.Add(upscalerGroup);

            _upscalerEnabled = new CheckBox();
            _upscalerEnabled.Text = "Activar en la DLL experimental";
            _upscalerEnabled.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _upscalerEnabled.ForeColor = TextDark;
            _upscalerEnabled.AutoSize = true;
            _upscalerEnabled.Location = new Point(12, 20);
            _upscalerEnabled.CheckedChanged += UpscalerChanged;
            upscalerGroup.Controls.Add(_upscalerEnabled);

            Label upscalerExplanation = new Label();
            upscalerExplanation.Text = "Para ganar rendimiento, baja la resolución de render del juego y conserva una salida OpenXR mayor. Es espacial experimental: no usa IA, DLSS ni DLAA; la DLL estable lo ignora. Al activarlo, DLSS se desactiva.";
            upscalerExplanation.ForeColor = Muted;
            upscalerExplanation.AutoSize = true;
            upscalerExplanation.MaximumSize = new Size(770, 0);
            upscalerExplanation.Location = new Point(30, 42);
            upscalerGroup.Controls.Add(upscalerExplanation);

            Button outputPlusTen = MakeSmallPresetButton("Salida +10 %", 92);
            outputPlusTen.Location = new Point(650, 17);
            outputPlusTen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            outputPlusTen.Click += delegate { ApplyUpscalerOutputPreset(1.10); };
            _toolTip.SetToolTip(outputPlusTen,
                "Fija una salida un 10 % mayor por eje que el render actual, conserva la proporción y redondea a píxeles pares.");
            upscalerGroup.Controls.Add(outputPlusTen);

            Button outputOneToOne = MakeSmallPresetButton("1:1", 50);
            outputOneToOne.Location = new Point(748, 17);
            outputOneToOne.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            outputOneToOne.Click += delegate { ApplyUpscalerOutputPreset(1.00); };
            _toolTip.SetToolTip(outputOneToOne,
                "Iguala la salida OpenXR a la resolución renderizada por el juego: filtra, pero no amplía.");
            upscalerGroup.Controls.Add(outputOneToOne);

            Label outputLabel = MakeCompactLabel("Salida VR");
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

            Label sharpnessLabel = MakeCompactLabel("Nitidez");
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
            sharpnessHint.Text = "0,00 suave · 1,00 intensa";
            sharpnessHint.ForeColor = Muted;
            sharpnessHint.AutoSize = true;
            sharpnessHint.Location = new Point(501, 81);
            upscalerGroup.Controls.Add(sharpnessHint);

            _upscalerStateLabel = new Label();
            _upscalerStateLabel.Text = "Estado pendiente de cargar…";
            _upscalerStateLabel.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
            _upscalerStateLabel.ForeColor = Blue;
            _upscalerStateLabel.AutoSize = true;
            _upscalerStateLabel.MaximumSize = new Size(770, 0);
            _upscalerStateLabel.Location = new Point(30, 113);
            upscalerGroup.Controls.Add(_upscalerStateLabel);

            GroupBox dlssGroup = new GroupBox();
            dlssGroup.Text = "MODO DE IMAGEN · NORMAL / DLAA / DLSS 4.5";
            dlssGroup.ForeColor = Navy;
            dlssGroup.BackColor = SystemColors.Control;
            dlssGroup.Location = new Point(14, FinalDlssEdition ? 292 : 584);
            dlssGroup.Size = new Size(820, 270);
            dlssGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            page.Controls.Add(dlssGroup);

            _dlssBackendStateLabel = new Label();
            _dlssBackendStateLabel.Text = "BACKEND · comprobación pendiente…";
            _dlssBackendStateLabel.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
            _dlssBackendStateLabel.ForeColor = Warning;
            _dlssBackendStateLabel.AutoSize = true;
            _dlssBackendStateLabel.MaximumSize = new Size(785, 0);
            _dlssBackendStateLabel.Location = new Point(14, 20);
            dlssGroup.Controls.Add(_dlssBackendStateLabel);

            Label dlssModeLabel = MakeCompactLabel("Modo");
            dlssModeLabel.Location = new Point(14, 48);
            dlssGroup.Controls.Add(dlssModeLabel);

            _dlssMode = new ComboBox();
            _dlssMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _dlssMode.Width = 235;
            _dlssMode.Location = new Point(66, 49);
            _dlssMode.Items.AddRange(new object[] {
                "Normal · resolución nativa",
                "DLAA 4.5 · resolución nativa",
                "DLSS 4.5 SR · reescalado"
            });
            _dlssMode.SelectedIndexChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssMode);

            Label runtimeLabel = MakeCompactLabel("Runtime");
            runtimeLabel.Location = new Point(326, 48);
            dlssGroup.Controls.Add(runtimeLabel);

            _dlssRuntime = new TextBox();
            _dlssRuntime.Text = "310.7.0 · probada";
            _dlssRuntime.ReadOnly = true;
            _dlssRuntime.TabStop = false;
            _dlssRuntime.BackColor = Color.White;
            _dlssRuntime.ForeColor = TextDark;
            _dlssRuntime.Width = 140;
            _dlssRuntime.Location = new Point(390, 49);
            dlssGroup.Controls.Add(_dlssRuntime);

            LinkLabel openDlss = new LinkLabel();
            openDlss.Text = "Abrir dlss.ini";
            openDlss.LinkColor = Blue;
            openDlss.AutoSize = true;
            openDlss.Location = new Point(704, 53);
            openDlss.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openDlss.LinkClicked += delegate { OpenDlssConfigurationFile(); };
            dlssGroup.Controls.Add(openDlss);

            Label dlssExplanation = new Label();
            dlssExplanation.Text = "Normal conserva el render nativo. DLAA suaviza bordes a resolución nativa y DLSS reconstruye una salida mayor desde un render más pequeño. Ambos usan profundidad, movimiento e historial independiente por ojo. No incluye DLSS 5 Neural Rendering.";
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
            _dlssPreset.Items.Add("Automático recomendado · K/M/L según ratio");
            _dlssPreset.SelectedIndexChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssPreset);

            Label qualityLabel = MakeCompactLabel("Calidad SR");
            qualityLabel.Location = new Point(326, 112);
            dlssGroup.Controls.Add(qualityLabel);

            _dlssQuality = new ComboBox();
            _dlssQuality.DropDownStyle = ComboBoxStyle.DropDownList;
            _dlssQuality.Width = 245;
            _dlssQuality.Location = new Point(404, 113);
            _dlssQuality.Items.AddRange(new object[] {
                "Ultra rendimiento · 1/3 (≈ 33 %)",
                "40 %",
                "Rendimiento · 50 %",
                "Equilibrado · 58 %",
                "60 %",
                "Calidad · 2/3 (≈ 67 %)",
                "70 %",
                "80 %",
                "90 %",
                "Personalizado / AUTO"
            });
            _dlssQuality.SelectedIndexChanged += DlssChanged;
            dlssGroup.Controls.Add(_dlssQuality);

            Label dlssSharpnessLabel = MakeCompactLabel("Nitidez DLSS (%)");
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
                "Nitidez opcional después de DLSS, igual que F1/F2/F3. 0 % conserva la imagen anterior. No cambia resolución ni calidad. Solo actúa en DLSS.");

            Label dlssOutputLabel = MakeCompactLabel("Salida VR");
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
            _dlssOutputPreset.Items.Add("Perfil de salida VR · personalizado");
            foreach (int size in SquareResolutionSteps)
                _dlssOutputPreset.Items.Add(size + " × " + size + " · por ojo");
            _dlssOutputPreset.SelectedIndexChanged += DlssOutputPresetChanged;
            dlssGroup.Controls.Add(_dlssOutputPreset);
            _toolTip.SetToolTip(_dlssOutputPreset,
                "Mismos tramos que F1/F2/F3. Conserva la calidad DLSS y recalcula el render. En Normal y DLAA, render y salida son iguales.");

            Label nearPlaneLabel = MakeCompactLabel("Avanzado · Plano cercano");
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
            nearPlaneHint.Text = "Ayuda a reconstruir profundidad y movimiento temporal. Recomendado: 10,0. No cambia tu altura ni el FOV.";
            nearPlaneHint.ForeColor = Muted;
            nearPlaneHint.AutoSize = true;
            nearPlaneHint.MaximumSize = new Size(510, 0);
            nearPlaneHint.Location = new Point(296, 185);
            dlssGroup.Controls.Add(nearPlaneHint);

            _dlssSettingsStateLabel = new Label();
            _dlssSettingsStateLabel.Text = "Configuración DLSS pendiente de cargar…";
            _dlssSettingsStateLabel.Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
            _dlssSettingsStateLabel.ForeColor = Blue;
            _dlssSettingsStateLabel.AutoSize = true;
            _dlssSettingsStateLabel.MaximumSize = new Size(785, 0);
            _dlssSettingsStateLabel.Location = new Point(14, 229);
            dlssGroup.Controls.Add(_dlssSettingsStateLabel);

            _toolTip.SetToolTip(_dlssPreset,
                "Solo lectura en esta fase: el host actual no consume un preset manual. Usa el mapeo recomendado K/M/L según modo y ratio.");
            _toolTip.SetToolTip(_dlssQuality,
                "En DLSS SR puedes elegir un ratio canónico. La salida no cambia; se ajusta la resolución interna del juego. Un ajuste fino no canónico se muestra como Personalizado / AUTO.");
            _toolTip.SetToolTip(_dlssNearPlane,
                "Plano cercano de la proyección en unidades del juego, usado solo para reconstrucción temporal. Recomendado: 10,0 UU. No modifica altura ni FOV.");

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
            TabPage page = new TabPage("Bioshock.ini");
            page.Name = "Bioshock.ini completo";
            page.UseVisualStyleBackColor = true;
            page.Padding = new Padding(7);

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 34;
            toolbar.WrapContents = false;
            toolbar.Padding = new Padding(2, 3, 0, 0);
            page.Controls.Add(toolbar);

            toolbar.Controls.Add(MakeToolbarLabel("Sección:"));
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
                "Todo", "VR directo", "Relacionado VR", "Avisos", "Modificados"
            });
            _iniImpactFilter.SelectedIndexChanged += delegate { RebuildIniGrid(); };
            toolbar.Controls.Add(_iniImpactFilter);

            toolbar.Controls.Add(MakeToolbarLabel("Buscar:"));
            _iniSearch = new TextBox();
            _iniSearch.Width = 110;
            _iniSearch.TextChanged += delegate { RebuildIniGrid(); };
            toolbar.Controls.Add(_iniSearch);

            _allowRiskyEdits = new CheckBox();
            _allowRiskyEdits.Text = "Desbloquear";
            _allowRiskyEdits.AutoSize = true;
            _allowRiskyEdits.Padding = new Padding(5, 4, 0, 0);
            _allowRiskyEdits.CheckedChanged += delegate { RebuildIniGrid(); };
            toolbar.Controls.Add(_allowRiskyEdits);

            Button restore = new Button();
            restore.Text = "Restaurar fila";
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
            _iniDetail.Text = "Selecciona una fila para ver qué hace y si puede afectar a VR.";
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
            _iniGrid.Columns.Add(MakeIniColumn("Impacto", "Alcance", 82, true));
            _iniGrid.Columns.Add(MakeIniColumn("Ajuste", "Ajuste en castellano", 175, true));
            _iniGrid.Columns.Add(MakeIniColumn("Valor", "Valor", 125, false));
            _iniGrid.Columns.Add(MakeIniColumn("Clave", "Clave original", 170, true));
            DataGridViewTextBoxColumn sectionColumn = MakeIniColumn("Seccion", "Sección", 220, true);
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
                                             UiLanguage.Text("Parámetro interno: ", "Internal parameter: ") + definition.Key);
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
                box.Text = "Activado";
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
                    _resolutionWidth.Value = _resolutionHeight.Value;
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
            foreach (IniEntry entry in _gameIniEntries)
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
                    _fxaaStateLabel.Text = "No se encontró una clave única [Engine.RenderConfig] UseFxaa.";
                    _fxaaStateLabel.ForeColor = Color.Firebrick;
                    return;
                }
                if (!TryParseIniSwitch(entry.Value, out enabled))
                {
                    _fxaaEnabled.Enabled = false;
                    _fxaaEnabled.Checked = false;
                    _fxaaStateLabel.Text = "Valor no reconocido: UseFxaa=" + entry.Value + ". Corrígelo en INI completo.";
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
            string pending = entry != null && entry.Changed ? "PENDIENTE DE GUARDAR  ·  " : "";
            _fxaaStateLabel.Text = pending + "UseFxaa=" + (enabled ? "1" : "0") +
                "  ·  " + (enabled ? "activado" : "desactivado");
            _fxaaStateLabel.ForeColor = entry != null && entry.Changed ? Warning : Blue;
        }

        private void FxaaChanged(object sender, EventArgs e)
        {
            if (_loading || _syncingFxaa) return;
            IniEntry entry = FindIniEntry("Engine.RenderConfig", "UseFxaa");
            if (entry == null)
            {
                SetStatus("No se puede cambiar FXAA: la clave UseFxaa falta o está duplicada.", Color.Firebrick);
                LoadFxaaControl();
                return;
            }
            entry.Value = FormatIniSwitch(entry.OriginalValue, _fxaaEnabled.Checked);
            UpdateFxaaSummary(entry, _fxaaEnabled.Checked);
            RecalculateGameIniDirty();
            RebuildIniGrid();
            SetStatus("FXAA " + (_fxaaEnabled.Checked ? "activado" : "desactivado") +
                " en la edición. Pulsa Guardar para aplicarlo al siguiente inicio.", Blue);
        }

        private void LoadResolutionControls()
        {
            IniEntry widthEntry = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportX");
            IniEntry heightEntry = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportY");
            IniEntry fullWidth = FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportX");
            IniEntry fullHeight = FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportY");
            int width, height, fw, fh;
            if (widthEntry == null || heightEntry == null || fullWidth == null || fullHeight == null ||
                !int.TryParse(widthEntry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out width) ||
                !int.TryParse(heightEntry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out height) ||
                !int.TryParse(fullWidth.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out fw) ||
                !int.TryParse(fullHeight.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out fh) ||
                width < (FinalDlssEdition ? 1 : 1024) || width > 8192 ||
                height < (FinalDlssEdition ? 1 : 1024) || height > 8192)
            {
                _resolutionWidth.Enabled = false;
                _resolutionHeight.Enabled = false;
                _resolutionPreset.Enabled = false;
                _resolutionLoadLabel.Text = "No se pudo localizar una pareja única y válida de resolución PC.";
                _resolutionLoadLabel.ForeColor = Color.Firebrick;
                return;
            }

            _syncingResolution = true;
            _resolutionWidth.Enabled = true;
            _resolutionHeight.Enabled = true;
            _resolutionPreset.Enabled = true;
            _resolutionWidth.Value = Math.Max(1024, width);
            _resolutionHeight.Value = Math.Max(1024, height);
            _squareResolution.Checked = width == height;
            _resolutionPreset.SelectedIndex = ResolutionPresetIndex(width, height);
            _syncingResolution = false;
            UpdateResolutionSummary(width, height, fw == width && fh == height);
        }

        private void ApplyResolutionControlsToEntries()
        {
            if (_loading) return;
            IniEntry wx = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportX");
            IniEntry wy = FindIniEntry("WinDrv.WindowsClient", "WindowedViewportY");
            IniEntry fx = FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportX");
            IniEntry fy = FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportY");
            if (wx == null || wy == null || fx == null || fy == null)
            {
                SetStatus("No se puede cambiar la resolución: faltan claves únicas en la sección PC.", Color.Firebrick);
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
            string shape = Math.Abs(ratio - 1.0) < 0.01 ? "cuadrada" : "relación " + ratio.ToString("0.000", CultureInfo.CurrentCulture);
            _resolutionLoadLabel.Text = width.ToString(CultureInfo.CurrentCulture) + " × " +
                height.ToString(CultureInfo.CurrentCulture) + "  ·  " +
                megapixels.ToString("0.00", CultureInfo.CurrentCulture) + " MP  ·  " + shape +
                (pairsMatch ? "  ·  ventana y pantalla completa sincronizadas" :
                              "  ·  AVISO: los dos modos no coinciden");
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
                problem = "La salida VR debe estar entre 1024 y 8192 píxeles por eje.";
                return false;
            }
            if (settings.Sharpness < 0m || settings.Sharpness > 1m)
            {
                problem = "La nitidez debe estar entre 0,00 y 1,00.";
                return false;
            }

            // La salida queda almacenada aunque el filtro esté apagado. La proporción
            // solo es operativa (y por tanto obligatoria) cuando se activa.
            if (!settings.Enabled)
                return true;

            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
            {
                problem = "No se puede comprobar la relación de aspecto porque falta una resolución válida del juego.";
                return false;
            }
            if (settings.OutputWidth < renderWidth || settings.OutputHeight < renderHeight)
            {
                problem = "La salida VR no puede ser menor que la resolución renderizada por el juego; eso sería reducción, no reescalado.";
                return false;
            }

            if (!UpscalerConfigDocument.HasExactAspect(renderWidth, renderHeight,
                                                        settings.OutputWidth,
                                                        settings.OutputHeight))
            {
                problem = "La salida VR debe conservar exactamente la relación de aspecto de " +
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
                    "upscaler.ini contiene valores incompletos o no válidos: " + warning;
            }
            catch (Exception ex)
            {
                _upscalerLoadedContent = string.Empty;
                _upscalerDirty = false;
                _upscalerLoadWarning = "No se pudo leer upscaler.ini: " + ex.Message;
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
                LocalizedMessageBox.Show(this,
                    "No se puede calcular el preset porque no hay una resolución de render válida en Bioshock.ini.",
                    "Preset de salida VR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int outputWidth = renderWidth;
            int outputHeight = renderHeight;
            if (scale > 1.0001 &&
                !UpscalerConfigDocument.TryCalculateProportionalOutput(renderWidth, renderHeight,
                                                                       scale, out outputWidth,
                                                                       out outputHeight))
            {
                LocalizedMessageBox.Show(this,
                    "No cabe una salida un 10 % mayor dentro del límite seguro de 8192 píxeles por eje. " +
                    "Baja primero la resolución de render del juego.",
                    "Preset de salida VR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                ? "Preset aplicado: salida OpenXR +10 % por eje, con proporción conservada."
                : "Preset aplicado: salida OpenXR 1:1 con el render del juego.", Blue);
        }

        private void UpdateUpscalerSummary()
        {
            if (_upscalerStateLabel == null || _upscalerEnabled == null ||
                _upscalerOutputWidth == null || _upscalerOutputHeight == null ||
                _upscalerSharpness == null) return;

            if (!string.IsNullOrEmpty(_upscalerLoadWarning))
            {
                _upscalerStateLabel.Text = "REVISAR · " + _upscalerLoadWarning;
                _upscalerStateLabel.ForeColor = Color.Firebrick;
                return;
            }

            UpscalerSettings settings = CollectUpscalerSettings();
            string problem;
            if (!TryValidateUpscalerSettings(settings, out problem))
            {
                _upscalerStateLabel.Text = "REVISAR · " + problem;
                _upscalerStateLabel.ForeColor = Color.Firebrick;
                return;
            }

            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
            {
                _upscalerStateLabel.Text = (_upscalerDirty ? "PENDIENTE · " : string.Empty) +
                    "DESACTIVADO · salida preparada " +
                    settings.OutputWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                    settings.OutputHeight.ToString(CultureInfo.CurrentCulture) +
                    " · no hay resolución de entrada para comparar";
                _upscalerStateLabel.ForeColor = _upscalerDirty ? Warning : Muted;
                return;
            }
            double axisScale = (double)settings.OutputWidth / renderWidth;
            string effect = Math.Abs(axisScale - 1.0) < 0.001
                ? "filtrado 1:1, sin ampliar"
                : axisScale.ToString("0.00", CultureInfo.CurrentCulture) + "× por eje";
            string prefix = _upscalerDirty ? "PENDIENTE · " :
                (File.Exists(_upscalerConfigPath) ? string.Empty : "SIN ARCHIVO · ");
            _upscalerStateLabel.Text = prefix +
                (settings.Enabled ? "ACTIVO" : "DESACTIVADO") + " · " +
                renderWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                renderHeight.ToString(CultureInfo.CurrentCulture) + " → " +
                settings.OutputWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                settings.OutputHeight.ToString(CultureInfo.CurrentCulture) + " · " + effect +
                " · nitidez " + settings.Sharpness.ToString("0.00", CultureInfo.CurrentCulture);
            _upscalerStateLabel.ForeColor = _upscalerDirty ? Warning : Blue;
        }

        private bool ValidateUpscalerBeforeSave()
        {
            UpscalerSettings settings = CollectUpscalerSettings();
            string problem;
            if (TryValidateUpscalerSettings(settings, out problem)) return true;
            LocalizedMessageBox.Show(this,
                "No se puede guardar la configuración del reescalado espacial:\n\n" + problem +
                "\n\nLa salida debe ser igual o mayor que la imagen del juego y conservar su proporción.",
                "Revisar reescalado espacial", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SelectTab("Imagen y resolución");
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
                problem = "La preferencia de calidad DLSS no es válida. Selecciona de nuevo un tramo.";
                return false;
            }
            if (settings.Runtime != DlssConfigDocument.RequiredRuntime)
            {
                problem = "Esta edición exige exactamente el runtime DLSS 310.7.0.";
                return false;
            }
            if (settings.Preset != "auto" && settings.Preset != "K" &&
                settings.Preset != "M" && settings.Preset != "L")
            {
                problem = "El preset DLSS debe ser Automático, K, M o L.";
                return false;
            }
            if (settings.NearPlaneUu < 0.1m || settings.NearPlaneUu > 1000.0m)
            {
                problem = "El plano cercano debe estar entre 0,1 y 1000,0 UU. " +
                          "El valor recomendado para BioShock es 10,0 UU.";
                return false;
            }
            if (settings.OutputWidth < 1024 || settings.OutputWidth > 8192 ||
                settings.OutputHeight < 1024 || settings.OutputHeight > 8192 ||
                (settings.OutputWidth & 1) != 0 || (settings.OutputHeight & 1) != 0)
            {
                problem = "La salida DLSS debe tener dimensiones pares entre 1024 y 8192 píxeles.";
                return false;
            }
            if (settings.Mode == DlssMode.Off)
                return true;

            int renderWidth, renderHeight;
            if (!TryGetRenderDimensions(out renderWidth, out renderHeight))
            {
                problem = "No se puede validar DLSS porque falta una resolución render válida del juego.";
                return false;
            }
            if (!UpscalerConfigDocument.HasExactAspect(renderWidth, renderHeight,
                                                        settings.OutputWidth,
                                                        settings.OutputHeight))
            {
                problem = "La salida DLSS debe conservar exactamente la relación de aspecto de " +
                    renderWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                    renderHeight.ToString(CultureInfo.CurrentCulture) + ".";
                return false;
            }

            if (settings.Mode == DlssMode.Dlaa)
            {
                if (settings.OutputWidth != renderWidth || settings.OutputHeight != renderHeight)
                {
                    problem = "DLAA procesa a resolución nativa: la entrada y la salida deben ser idénticas.";
                    return false;
                }
                return true;
            }

            if (settings.OutputWidth <= renderWidth || settings.OutputHeight <= renderHeight)
            {
                problem = "DLSS Super Resolution exige una salida mayor que la resolución renderizada. " +
                          "Para trabajar 1:1 selecciona DLAA.";
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
                    ? "El selector de calidad SR no coincide con la resolución interna canónica detectada."
                    : "La relación render→salida no coincide con un tramo canónico y debe mostrarse como Personalizado / AUTO.";
                return false;
            }
            return true;
        }

        private static string EffectiveDlssPreset(DlssSettings settings)
        {
            if (settings.Preset != "auto") return settings.Preset;
            if (settings.Mode == DlssMode.Dlaa) return "K";
            if (settings.Quality == DlssQuality.Custom) return "AUTO según ratio/NGX";
            if (settings.Quality == DlssQuality.Quality ||
                settings.Quality == DlssQuality.Balanced ||
                settings.Quality == DlssQuality.Percent60 ||
                settings.Quality == DlssQuality.Percent70 ||
                settings.Quality == DlssQuality.Percent80 ||
                settings.Quality == DlssQuality.Percent90) return "K";
            if (settings.Quality == DlssQuality.Performance) return "M";
            return settings.Quality == DlssQuality.UltraPerformance ? "L" : "AUTO según ratio/NGX";
        }

        private static string DlssQualityName(DlssQuality quality)
        {
            if (quality == DlssQuality.Balanced) return "Equilibrado";
            if (quality == DlssQuality.Performance) return "Rendimiento";
            if (quality == DlssQuality.UltraPerformance) return "Ultra rendimiento";
            if (quality == DlssQuality.Custom) return "Personalizado / AUTO";
            if (quality == DlssQuality.Percent40) return "40 %";
            if (quality == DlssQuality.Percent60) return "60 %";
            if (quality == DlssQuality.Percent70) return "70 %";
            if (quality == DlssQuality.Percent80) return "80 %";
            if (quality == DlssQuality.Percent90) return "90 %";
            return "Calidad";
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
                _dlssNearPlane.Enabled = true;
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
                    ignoredStoredValues.Add("preset manual " + settings.Preset + " guardado sin efecto");
                _dlssNormalizationNote = ignoredStoredValues.Count == 0 ? null :
                    string.Join("; ", ignoredStoredValues.ToArray()) +
                    ". El archivo no cambia hasta que hagas un cambio y pulses Guardar.";
                _dlssLoadWarning = valid ? null :
                    "dlss.ini contiene valores incompletos o no válidos: " + warning;
            }
            catch (Exception ex)
            {
                _dlssLoadedContent = string.Empty;
                _dlssDirty = false;
                _dlssNormalizationNote = null;
                _dlssLoadWarning = "No se pudo leer dlss.ini: " + ex.Message;
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
                    qualityStatus = "Personalizado / AUTO aparece automáticamente cuando el render no coincide con un tramo canónico; las resoluciones siguen editables.";
                }
                else
                {
                    int renderWidth, renderHeight;
                    if (TryApplyDlssQualityToRender(selectedQuality,
                                                    out renderWidth, out renderHeight))
                        qualityStatus = "Tramo " + DlssQualityName(selectedQuality) +
                            " aplicado en memoria: render " +
                            renderWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                            renderHeight.ToString(CultureInfo.CurrentCulture) +
                            "; la salida DLSS no ha cambiado. Pulsa Guardar para escribir Bioshock.ini.";
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
                FindIniEntry("WinDrv.WindowsClient", "WindowedViewportX") == null ||
                FindIniEntry("WinDrv.WindowsClient", "WindowedViewportY") == null ||
                FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportX") == null ||
                FindIniEntry("WinDrv.WindowsClient", "FullscreenViewportY") == null)
            {
                LocalizedMessageBox.Show(this,
                    "No se puede aplicar el tramo porque faltan las cuatro claves únicas de resolución PC en Bioshock.ini.",
                    "Calidad DLSS SR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                LocalizedMessageBox.Show(this,
                    "No se puede obtener una resolución interna par, entre 1024 y 8192 píxeles, " +
                    "que mantenga exactamente el aspecto de la salida " +
                    outputWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                    outputHeight.ToString(CultureInfo.CurrentCulture) +
                    " para ese tramo. Revisa primero la salida DLSS.",
                    "Calidad DLSS SR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                _dlssSettingsStateLabel.Text = "REVISAR · " + _dlssLoadWarning;
                _dlssSettingsStateLabel.ForeColor = Color.Firebrick;
                return;
            }

            DlssSettings settings = CollectDlssSettings();
            string problem;
            if (!TryValidateDlssSettings(settings, out problem))
            {
                _dlssSettingsStateLabel.Text = "REVISAR · " + problem;
                _dlssSettingsStateLabel.ForeColor = Color.Firebrick;
                return;
            }

            string prefix = _dlssDirty ? "PENDIENTE · " :
                (File.Exists(_dlssConfigPath) ? string.Empty : "SIN ARCHIVO · ");
            if (settings.Mode == DlssMode.Off)
            {
                _dlssSettingsStateLabel.Text = prefix +
                    "DESACTIVADO · plano cercano preparado " +
                    settings.NearPlaneUu.ToString("0.0##", CultureInfo.CurrentCulture) +
                    " UU · sin ejecutar DLSS." +
                    (string.IsNullOrEmpty(_dlssNormalizationNote) ? string.Empty :
                        " NOTA · " + _dlssNormalizationNote);
                _dlssSettingsStateLabel.ForeColor = _dlssDirty ? Warning : Muted;
                return;
            }

            int renderWidth, renderHeight;
            TryGetRenderDimensions(out renderWidth, out renderHeight);
            string mode;
            if (settings.Mode == DlssMode.Dlaa)
                mode = "DLAA 4.5 · 1:1 · calidad SR ignorada";
            else if (settings.Quality == DlssQuality.Custom)
                mode = "DLSS 4.5 SR · Personalizado / AUTO";
            else
                mode = "DLSS 4.5 SR · tramo " + DlssQualityName(settings.Quality);
            double inputPercent = settings.OutputWidth > 0
                ? 100.0 * renderWidth / settings.OutputWidth
                : 0.0;
            _dlssSettingsStateLabel.Text = prefix + mode + " · " +
                renderWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                renderHeight.ToString(CultureInfo.CurrentCulture) + " → " +
                settings.OutputWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                settings.OutputHeight.ToString(CultureInfo.CurrentCulture) +
                (settings.Mode == DlssMode.SuperResolution
                    ? " · entrada " + inputPercent.ToString("0.0", CultureInfo.CurrentCulture) + "%"
                    : string.Empty) +
                " · preset auto→" + EffectiveDlssPreset(settings) +
                (settings.Mode == DlssMode.SuperResolution &&
                 settings.Quality == DlssQuality.Custom
                    ? " · NGX elegirá según el ratio real"
                    : string.Empty) +
                " · plano cercano " +
                settings.NearPlaneUu.ToString("0.0##", CultureInfo.CurrentCulture) + " UU" +
                (_dlssBackendStatus.Ready ? " · backend preparado" :
                    " · se guardará, pero el backend aún no está operativo") +
                (string.IsNullOrEmpty(_dlssNormalizationNote) ? string.Empty :
                    " · NOTA: " + _dlssNormalizationNote);
            _dlssSettingsStateLabel.ForeColor = _dlssDirty ? Warning :
                (_dlssBackendStatus.Ready && _dlssBackendStatus.RuntimeMatches ? Success : Blue);
        }

        private bool ValidateDlssBeforeSave()
        {
            DlssSettings settings = CollectDlssSettings();
            string problem;
            if (TryValidateDlssSettings(settings, out problem)) return true;
            LocalizedMessageBox.Show(this,
                "No se puede guardar la configuración DLSS 4.5:\n\n" + problem +
                "\n\nDLAA exige entrada=salida. DLSS SR exige una salida mayor y la misma proporción.",
                "Revisar DLSS 4.5", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SelectTab("Imagen y resolución");
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
            status.Summary = "BACKEND SIN CONFIRMAR · no se ha podido comprobar host64.";
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
                    status.Summary = "BACKEND NO INSTALADO · falta la carpeta host64 junto al juego o al lanzador.";
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
                    status.Summary = "BACKEND NO PREPARADO · host64 contiene componentes ajenos a la fase limpia DLSS 4.5: " +
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
                    string phase, eyes, runtime;
                    capability.TryGetValue("phase", out phase);
                    capability.TryGetValue("eyeHosts", out eyes);
                    capability.TryGetValue("runtime", out runtime);
                    int.TryParse(eyes, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                 out declaredEyes);
                    capabilityValid = string.Equals(phase, "DLSS45", StringComparison.OrdinalIgnoreCase) &&
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
                    status.Summary = "BACKEND INCOMPLETO · falta host64\\BioShockVR-DLSS45-Host64.exe.";
                else if (!splitHosts && !reusableHost)
                    status.Summary = "BACKEND INVÁLIDO · el host encontrado no es un ejecutable x64.";
                else if (!status.RuntimeFound)
                    status.Summary = "BACKEND INCOMPLETO · falta host64\\nvngx_dlss.dll 310.7.0.";
                else if (!status.RuntimeIs64Bit)
                    status.Summary = "BACKEND INCOMPATIBLE · nvngx_dlss.dll debe ser x64.";
                else if (!status.CapabilityFound)
                    status.Summary = "BACKEND INCOMPLETO · falta dlss-capabilities.ini; no se confirma la integración estéreo.";
                else if (!capabilityValid || status.EyeHosts < 2)
                    status.Summary = "BACKEND INVÁLIDO · el manifiesto debe declarar phase=DLSS45, eyeHosts=2 y runtime=310.7.0.";
                else if (!status.RuntimeMatches)
                    status.Summary = "BACKEND PREPARADO CON AVISO · nvngx_dlss.dll x64 " +
                        (string.IsNullOrEmpty(status.RuntimeVersion) ? "de versión no identificada" :
                            status.RuntimeVersion) +
                        "; solo 310.7.0.0 está probada.";
                else
                    status.Summary = "BACKEND PREPARADO · host x64 · 2 ojos · nvngx_dlss.dll 310.7.0.0 probada.";
            }
            catch (Exception ex)
            {
                status.Ready = false;
                status.Summary = "BACKEND SIN CONFIRMAR · " + ex.Message;
            }
            return status;
        }

        private void WarnAboutUntestedRuntime()
        {
            if (_runtimeWarningShown || _dlssBackendStatus == null ||
                !_dlssBackendStatus.Ready || _dlssBackendStatus.RuntimeMatches)
                return;
            _runtimeWarningShown = true;
            LocalizedMessageBox.Show(this,
                "Se ha detectado una versión distinta de nvngx_dlss.dll: " +
                (string.IsNullOrEmpty(_dlssBackendStatus.RuntimeVersion)
                    ? "no identificada" : _dlssBackendStatus.RuntimeVersion) + ".\r\n\r\n" +
                "El instalador coloca y esta integración ha sido probada con NVIDIA DLSS " +
                DlssConfigDocument.TestedRuntimeDisplay + ". Se permitirá continuar, pero con otras " +
                "versiones no se garantizan el funcionamiento, la estabilidad ni la calidad de imagen. " +
                "La sustitución corre por cuenta del usuario.\r\n\r\n" +
                "Reinstalar esta versión restaura la DLL probada.",
                "DLL de NVIDIA no probada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void PopulateIniFilters()
        {
            string selected = _iniSectionFilter.SelectedItem as string;
            SortedSet<string> sections = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (IniEntry entry in _gameIniEntries)
                if (!FinalDlssEdition || !IsFxaaEntry(entry)) sections.Add(entry.Section);
            _iniSectionFilter.Items.Clear();
            _iniSectionFilter.Items.Add("Todas las secciones");
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
                        displayName += "  (elemento " + entry.Occurrence + " de " + entry.OccurrenceCount + ")";
                    int rowIndex = _iniGrid.Rows.Add(IniKnowledge.ImpactText(entry.Impact), displayName,
                                                     entry.Value, entry.Key, entry.Section);
                    DataGridViewRow row = _iniGrid.Rows[rowIndex];
                    row.Tag = entry;
                    bool protectedEntry = entry.Protected &&
                                           (_allowRiskyEdits == null || !_allowRiskyEdits.Checked);
                    row.Cells["Valor"].ReadOnly = protectedEntry || IsPcResolutionEntry(entry);
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
            if (e.RowIndex < 0 || _iniGrid.Columns[e.ColumnIndex].Name != "Valor") return;
            IniEntry entry = _iniGrid.Rows[e.RowIndex].Tag as IniEntry;
            if (IsPcResolutionEntry(entry))
            {
                e.Cancel = true;
                SetStatus("La resolución se edita de forma segura y conjunta en la pestaña Imagen.", Warning);
            }
            else if (entry != null && entry.Protected && !_allowRiskyEdits.Checked)
            {
                e.Cancel = true;
                SetStatus("Este ajuste interno está protegido. Activa «Desbloquear internos» para editarlo.", Warning);
            }
        }

        private void IniGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_rebuildingIniGrid || _loading || e.RowIndex < 0 ||
                _iniGrid.Columns[e.ColumnIndex].Name != "Valor") return;
            IniEntry entry = _iniGrid.Rows[e.RowIndex].Tag as IniEntry;
            if (entry == null) return;
            string value = Convert.ToString(_iniGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value,
                                            CultureInfo.CurrentCulture) ?? string.Empty;
            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
            {
                _iniGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = entry.Value;
                SetStatus("Un valor de INI no puede contener saltos de línea.", Color.Firebrick);
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
                _iniDetail.Text = "Selecciona una fila para ver qué hace y si puede afectar a VR.";
                return;
            }
            string repeated = entry.OccurrenceCount > 1
                ? " · elemento " + entry.Occurrence + " de " + entry.OccurrenceCount : string.Empty;
            string protection = entry.Protected
                ? " · PROTEGIDO POR DEFECTO" : string.Empty;
            if (IsPcResolutionEntry(entry)) protection += " · editar en Imagen";
            _iniDetail.Text = IniKnowledge.ImpactText(entry.Impact) + protection + repeated +
                Environment.NewLine + "[" + entry.Section + "]  " + entry.Key +
                Environment.NewLine + entry.Description;
        }

        private void RecalculateGameIniDirty()
        {
            _gameIniDirty = false;
            foreach (IniEntry entry in _gameIniEntries)
            {
                if (entry.Changed)
                {
                    _gameIniDirty = true;
                    break;
                }
            }
            UpdateDirtyState();
        }

        private bool HasPcResolutionChanges()
        {
            foreach (IniEntry entry in _gameIniEntries)
            {
                if (IsPcResolutionEntry(entry) && entry.Changed)
                    return true;
            }
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
            _saveButton.Enabled = _dirty;
            if (_dirty)
                SetStatus("Hay cambios pendientes de guardar.", Warning);
            else if (_statusLabel != null &&
                     _statusLabel.Text == "Hay cambios pendientes de guardar.")
                SetStatus("No hay cambios pendientes.", Success);
        }

        private void UpdatePathStatus()
        {
            _configStateLabel.Text = "CONFIGURACIÓN  ·  vrpreset.ini + dlss.ini + Bioshock.ini";
            if (!string.IsNullOrEmpty(_gameExePath) && File.Exists(_gameExePath))
            {
                _gameStateLabel.Text = "JUEGO ENCONTRADO  ·  " + _gameExePath;
                _launchButton.Enabled = !_launchPending;
            }
            else
            {
                _gameStateLabel.Text = "JUEGO NO ENCONTRADO  ·  podrás localizar BioshockHD.exe al iniciar";
                _launchButton.Enabled = !_launchPending;
            }
            _dlssBackendStatus = DetectDlssBackend();
            UpdateDlssSummary();
        }

        private void LoadConfiguration(bool initiatedByUser)
        {
            if (initiatedByUser && _dirty)
            {
                DialogResult answer = LocalizedMessageBox.Show(
                    this,
                    "Hay cambios sin guardar. ¿Quieres descartarlos y volver a leer los ficheros?",
                    "Recargar configuración",
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
                    PopulateEditors(ConfigDocument.Parse(_loadedContent));
                    _vrDirty = false;
                }
                else
                {
                    _loadedContent = string.Empty;
                    _loadedWriteTimeUtc = DateTime.MinValue;
                    PopulateEditors(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                    _vrDirty = true;
                }

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
                    _resolutionLoadLabel.Text = "No se encuentra Bioshock.ini en " + _gameIniPath;
                    _resolutionLoadLabel.ForeColor = Color.Firebrick;
                    _fxaaStateLabel.Text = "No se encuentra Bioshock.ini.";
                    _fxaaStateLabel.ForeColor = Color.Firebrick;
                }
                LoadUpscalerConfiguration();
                LoadDlssConfiguration();
                LoadGraphicsOptions();
                bool finalPolicyAdjusted = EnforceFinalImagePolicy();
                _dirty = _vrDirty || _upscalerDirty ||
                         _dlssDirty || _gameIniDirty;
                _saveButton.Enabled = _dirty;
                if (finalPolicyAdjusted)
                    SetStatus("La edición final desactivará FXAA y el antiguo reescalado espacial al guardar.", Warning);
                else if (_dlssDirty && _upscalerDirty)
                    SetStatus("Se detectaron DLSS y reescalado espacial activos a la vez. La interfaz ha dejado solo DLSS; pulsa Guardar para corregir ambos archivos.", Warning);
                else if (!string.IsNullOrEmpty(_dlssLoadWarning))
                    SetStatus("Configuración cargada; revisa el aviso de dlss.ini en la pestaña Imagen.", Warning);
                else if (!FinalDlssEdition && !string.IsNullOrEmpty(_upscalerLoadWarning))
                    SetStatus("Configuración cargada; revisa el aviso de upscaler.ini en la pestaña Imagen.", Warning);
                else
                    SetStatus(File.Exists(_configPath)
                        ? "Configuración VR, modos Normal/DLAA/DLSS 4.5 y " + _gameIniEntries.Count + " entradas de Bioshock.ini cargadas sin modificar archivos."
                        : "Falta vrpreset.ini; se muestran valores iniciales listos para guardar.",
                        File.Exists(_configPath) ? Success : Warning);
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this,
                    "No se ha podido leer la configuración:\n\n" + ex.Message,
                    "Error al cargar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Error al leer vrpreset.ini.", Color.Firebrick);
            }
            finally
            {
                _loading = false;
            }
            if (FinalDlssEdition && PrepareSquareImage())
                SetStatus("La resolución cuadrada del visor está preparada. Pulsa Guardar para aplicarla.", Warning);
        }

        private void PopulateEditors(Dictionary<string, string> values)
        {
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
            }
        }

        private Dictionary<string, string> CollectValues()
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ParamDef definition in _definitions)
            {
                Control editor = _editors[definition.Key];
                if (definition.Kind == ParamKind.Boolean)
                    values[definition.Key] = ((CheckBox)editor).Checked ? "1" : "0";
                else if (definition.Kind == ParamKind.Choice)
                    values[definition.Key] = ((ComboBox)editor).SelectedIndex.ToString(CultureInfo.InvariantCulture);
                else
                {
                    decimal value = ((NumericUpDown)editor).Value;
                    string format = "F" + definition.Decimals.ToString(CultureInfo.InvariantCulture);
                    values[definition.Key] = value.ToString(format, CultureInfo.InvariantCulture);
                }
            }
            return values;
        }

        private decimal NumericValue(string key)
        {
            return ((NumericUpDown)_editors[key]).Value;
        }

        private bool ValidateBeforeSave()
        {
            if (NumericValue("swingRearm") >= NumericValue("swingThreshold"))
            {
                DialogResult answer = LocalizedMessageBox.Show(this,
                    "La velocidad para rearmar el gesto es igual o superior a la velocidad necesaria para atacar. " +
                    "El mod la limitará internamente, pero el comportamiento será menos predecible.\n\n" +
                    "¿Quieres guardar de todos modos?",
                    "Revisar ataque por gesto", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    SelectTab("Ataque por gesto");
                    return false;
                }
            }
            return true;
        }

        private string ReadGameIniText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                _gameIniEncoding = new UTF8Encoding(true);
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                _gameIniEncoding = Encoding.Unicode;
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                _gameIniEncoding = Encoding.BigEndianUnicode;
            else
                _gameIniEncoding = Encoding.GetEncoding(1252);
            return File.ReadAllText(path, _gameIniEncoding);
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
                DialogResult answer = LocalizedMessageBox.Show(this,
                    "vrpreset.ini ha cambiado desde que abriste o recargaste el lanzador. " +
                    "Puede haber sido modificado por el juego u otra aplicación.\n\n" +
                    "¿Quieres aplicar sobre esa versión externa los valores que ves ahora?",
                    "Configuración modificada externamente",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                return answer == DialogResult.Yes;
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this, "No se ha podido comprobar vrpreset.ini:\n\n" + ex.Message,
                    "Error al preparar el guardado", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                DialogResult answer = LocalizedMessageBox.Show(this,
                    "upscaler.ini ha cambiado desde que abriste o recargaste el lanzador. " +
                    "Puede haber sido modificado por el juego u otra aplicación.\n\n" +
                    "¿Quieres aplicar sobre esa versión externa los valores que ves ahora?",
                    "Reescalado modificado externamente",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                return answer == DialogResult.Yes;
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this, "No se ha podido comprobar upscaler.ini:\n\n" + ex.Message,
                    "Error al preparar el reescalado", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                DialogResult answer = LocalizedMessageBox.Show(this,
                    "dlss.ini ha cambiado desde que abriste o recargaste el lanzador. " +
                    "Puede haber sido modificado por el juego u otra aplicación.\n\n" +
                    "¿Quieres aplicar sobre esa versión externa los valores que ves ahora?",
                    "DLSS modificado externamente",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                return answer == DialogResult.Yes;
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this, "No se ha podido comprobar dlss.ini:\n\n" + ex.Message,
                    "Error al preparar DLSS 4.5", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool SaveConfiguration(bool showNoChanges)
        {
            if (IsGameRunning())
            {
                LocalizedMessageBox.Show(this,
                    "BioShock está abierto. Ciérralo antes de guardar para evitar que el juego vuelva a sobrescribir el fichero al salir.",
                    "Juego en ejecución", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!_dirty)
            {
                if (showNoChanges)
                    SetStatus("No hay cambios pendientes; el fichero ya está al día.", Success);
                return true;
            }
            if (_vrDirty && !ValidateBeforeSave())
                return false;

            if (!FinalDlssEdition && _upscalerEnabled.Checked && _dlssMode.SelectedIndex > 0)
            {
                LocalizedMessageBox.Show(this,
                    "DLSS 4.5 y el reescalado espacial no pueden estar activos a la vez. " +
                    "Selecciona solo uno de los dos métodos.",
                    "Métodos de imagen excluyentes", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SelectTab("Imagen y resolución");
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
                        SetStatus("Bioshock.ini sí se guardó; vrpreset.ini no se ha guardado.", Warning);
                    return false;
                }
                savedVr = true;
            }
            if (_upscalerDirty)
            {
                if (!SaveUpscalerConfigurationCore(expectedUpscalerContent))
                {
                    if (savedGame || savedVr)
                        SetStatus("Los otros ficheros sí se guardaron; upscaler.ini no se ha guardado.", Warning);
                    return false;
                }
                savedUpscaler = true;
            }
            if (_dlssDirty)
            {
                if (!SaveDlssConfigurationCore(expectedDlssContent))
                {
                    if (savedGame || savedVr || savedUpscaler)
                        SetStatus("Los otros ficheros sí se guardaron; dlss.ini no se ha guardado.", Warning);
                    return false;
                }
                savedDlss = true;
            }
            UpdateDirtyState();
            List<string> savedNames = new List<string>();
            if (savedVr) savedNames.Add("vrpreset.ini");
            if (savedDlss) savedNames.Add("dlss.ini");
            if (savedUpscaler) savedNames.Add("upscaler.ini");
            if (savedGame) savedNames.Add("Bioshock.ini");
            SetStatus(string.Join(" + ", savedNames.ToArray()) +
                " guardado(s), verificado(s) y respaldado(s).", Success);
            return true;
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
                    throw new IOException("upscaler.ini ha vuelto a cambiar durante el guardado. Recarga antes de intentarlo de nuevo.");

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
                    throw new InvalidDataException("No se pudo verificar upscaler.ini. " + warning);

                string directory = Path.GetDirectoryName(_upscalerConfigPath);
                Directory.CreateDirectory(directory);
                if (File.Exists(_upscalerConfigPath))
                {
                    string backupDirectory = Path.Combine(directory, "Copias del lanzador");
                    Directory.CreateDirectory(backupDirectory);
                    string backupPath = Path.Combine(backupDirectory,
                        "upscaler-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff",
                                                            CultureInfo.InvariantCulture) + ".ini");
                    File.Copy(_upscalerConfigPath, backupPath, false);
                    if (!FilesHaveSameBytes(backupPath, _upscalerConfigPath))
                        throw new IOException("No se pudo verificar la copia de seguridad de upscaler.ini.");
                }

                temporaryPath = _upscalerConfigPath + ".lanzador-" +
                                Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryPath, newContent, new UTF8Encoding(false));
                if (File.Exists(_upscalerConfigPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, _upscalerConfigPath, null, true);
                        temporaryPath = null;
                    }
                    catch
                    {
                        File.Copy(temporaryPath, _upscalerConfigPath, true);
                        File.Delete(temporaryPath);
                        temporaryPath = null;
                    }
                }
                else
                {
                    File.Move(temporaryPath, _upscalerConfigPath);
                    temporaryPath = null;
                }

                string diskContent = File.ReadAllText(_upscalerConfigPath, Encoding.UTF8);
                if (diskContent != newContent)
                    throw new IOException("La comprobación final de upscaler.ini no coincide.");
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
                LocalizedMessageBox.Show(this,
                    "No se ha podido guardar upscaler.ini:\n\n" + ex.Message,
                    "Error al guardar reescalado", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("upscaler.ini no se ha guardado.", Color.Firebrick);
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
                    throw new IOException("dlss.ini ha vuelto a cambiar durante el guardado. Recarga antes de intentarlo de nuevo.");

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
                    throw new InvalidDataException("No se pudo verificar dlss.ini. " + warning);

                string directory = Path.GetDirectoryName(_dlssConfigPath);
                Directory.CreateDirectory(directory);
                if (File.Exists(_dlssConfigPath))
                {
                    string backupDirectory = Path.Combine(directory, "Copias del lanzador");
                    Directory.CreateDirectory(backupDirectory);
                    string backupPath = Path.Combine(backupDirectory,
                        "dlss-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff",
                                                       CultureInfo.InvariantCulture) + ".ini");
                    File.Copy(_dlssConfigPath, backupPath, false);
                    if (!FilesHaveSameBytes(backupPath, _dlssConfigPath))
                        throw new IOException("No se pudo verificar la copia de seguridad de dlss.ini.");
                }

                temporaryPath = _dlssConfigPath + ".lanzador-" +
                                Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryPath, newContent, new UTF8Encoding(false));
                if (File.Exists(_dlssConfigPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, _dlssConfigPath, null, true);
                        temporaryPath = null;
                    }
                    catch
                    {
                        File.Copy(temporaryPath, _dlssConfigPath, true);
                        File.Delete(temporaryPath);
                        temporaryPath = null;
                    }
                }
                else
                {
                    File.Move(temporaryPath, _dlssConfigPath);
                    temporaryPath = null;
                }

                string diskContent = File.ReadAllText(_dlssConfigPath, Encoding.UTF8);
                if (diskContent != newContent)
                    throw new IOException("La comprobación final de dlss.ini no coincide.");
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
                LocalizedMessageBox.Show(this,
                    "No se ha podido guardar dlss.ini:\n\n" + ex.Message,
                    "Error al guardar DLSS 4.5", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("dlss.ini no se ha guardado.", Color.Firebrick);
                return false;
            }
        }

        private bool SaveVrConfigurationCore(string expectedContent)
        {

            try
            {
                string currentContent = File.Exists(_configPath)
                    ? File.ReadAllText(_configPath, Encoding.UTF8)
                    : string.Empty;
                if (currentContent != (expectedContent ?? string.Empty))
                    throw new IOException("vrpreset.ini ha vuelto a cambiar durante el guardado. Recarga antes de intentarlo de nuevo.");

                Dictionary<string, string> values = CollectValues();
                string newContent = ConfigDocument.Render(currentContent, _definitions, values);
                Dictionary<string, string> verification = ConfigDocument.Parse(newContent);
                foreach (ParamDef definition in _definitions)
                {
                    if (!verification.ContainsKey(definition.Key) ||
                        verification[definition.Key] != values[definition.Key])
                        throw new InvalidDataException("No se pudo verificar el parámetro " + definition.Key + ".");
                }

                string directory = Path.GetDirectoryName(_configPath);
                Directory.CreateDirectory(directory);
                string backupPath = null;
                if (File.Exists(_configPath))
                {
                    string backupDirectory = Path.Combine(directory, "Copias del lanzador");
                    Directory.CreateDirectory(backupDirectory);
                    backupPath = Path.Combine(backupDirectory,
                        "vrpreset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".ini");
                    File.Copy(_configPath, backupPath, false);
                }

                string temporaryPath = _configPath + ".lanzador.tmp";
                File.WriteAllText(temporaryPath, newContent, new UTF8Encoding(false));
                if (File.Exists(_configPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, _configPath, null, true);
                    }
                    catch
                    {
                        File.Copy(temporaryPath, _configPath, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, _configPath);
                }

                string diskContent = File.ReadAllText(_configPath, Encoding.UTF8);
                if (diskContent != newContent)
                    throw new IOException("La comprobación final del fichero guardado no coincide.");

                _loadedContent = newContent;
                _loadedWriteTimeUtc = File.GetLastWriteTimeUtc(_configPath);
                _vrDirty = false;
                return true;
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this,
                    "No se han podido guardar los cambios:\n\n" + ex.Message,
                    "Error al guardar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("No se ha podido guardar la configuración.", Color.Firebrick);
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
                list.AppendLine("• …y " + (sensitive.Count - shown) + " ajuste(s) más");
            DialogResult answer = LocalizedMessageBox.Show(this,
                "Has modificado ajustes internos, protegidos o especialmente sensibles para VR:\n\n" +
                list.ToString() + "\nSe creará una copia de seguridad, pero un valor incorrecto puede impedir que el juego arranque o alterar el progreso. ¿Quieres continuar?",
                "Confirmar cambios delicados", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            return answer == DialogResult.Yes;
        }

        private bool ValidateGameIniStructure()
        {
            int sectionCount = Regex.Matches(_gameIniLoadedContent ?? string.Empty,
                @"(?m)^\[WinDrv\.WindowsClient\]\r?$").Count;
            if (sectionCount != 1)
                throw new InvalidDataException("Se esperaba una sola sección [WinDrv.WindowsClient] y se encontraron " + sectionCount + ".");
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
                    throw new InvalidDataException("La clave [Engine.RenderConfig] UseFxaa falta o está duplicada.");
                bool enabled;
                if (!TryParseIniSwitch(changedFxaa.Value, out enabled))
                    throw new InvalidDataException("[Engine.RenderConfig] UseFxaa debe ser 0, 1, False o True.");
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
                    throw new InvalidDataException("La clave PC " + keys[i] + " falta o está duplicada.");
                if (!resolutionChanged)
                    continue;
                int number;
                if (!int.TryParse(found.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                    number < 1024 || number > 8192)
                    throw new InvalidDataException(keys[i] + " debe estar entre 1024 y 8192.");
                if (i == 0) width = number;
                if (i == 1) height = number;
                if ((i == 2 && number != width) || (i == 3 && number != height))
                    throw new InvalidDataException("Las resoluciones de ventana y pantalla completa deben guardarse sincronizadas.");
            }
            return true;
        }

        private bool SaveGameIniCore()
        {
            string temporaryPath = null;
            try
            {
                if (!File.Exists(_gameIniPath))
                    throw new FileNotFoundException("No se encuentra Bioshock.ini.", _gameIniPath);
                string currentContent = ReadGameIniText(_gameIniPath);
                if (currentContent != (_gameIniLoadedContent ?? string.Empty))
                {
                    DialogResult answer = LocalizedMessageBox.Show(this,
                        "Bioshock.ini ha cambiado desde que lo cargaste, probablemente porque el juego u otra aplicación lo ha escrito.\n\nRecarga el fichero antes de continuar para no perder esos cambios.",
                        "Bioshock.ini modificado externamente", MessageBoxButtons.OK,
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
                    throw new InvalidDataException("La estructura del INI cambió durante la preparación del guardado.");
                for (int i = 0; i < verificationModel.Count; i++)
                {
                    if (verificationModel[i].Section != _gameIniEntries[i].Section ||
                        verificationModel[i].Key != _gameIniEntries[i].Key ||
                        verificationModel[i].Value != _gameIniEntries[i].Value)
                        throw new InvalidDataException("Falló la verificación de [" +
                            _gameIniEntries[i].Section + "] " + _gameIniEntries[i].Key + ".");
                }

                string directory = Path.GetDirectoryName(_gameIniPath);
                string backupDirectory = Path.Combine(directory, "Copias del lanzador");
                Directory.CreateDirectory(backupDirectory);
                string backupPath = Path.Combine(backupDirectory,
                    "Bioshock-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".ini");
                File.Copy(_gameIniPath, backupPath, false);
                if (!FilesHaveSameBytes(backupPath, _gameIniPath))
                    throw new IOException("No se pudo verificar la copia de seguridad de Bioshock.ini.");

                temporaryPath = _gameIniPath + ".lanzador-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryPath, newContent, _gameIniEncoding);
                try
                {
                    File.Replace(temporaryPath, _gameIniPath, null, true);
                    temporaryPath = null;
                }
                catch (Exception ex)
                {
                    throw new IOException("Windows no pudo sustituir Bioshock.ini de forma atómica. El original sigue protegido por la copia: " + ex.Message, ex);
                }

                string diskContent = ReadGameIniText(_gameIniPath);
                if (diskContent != newContent)
                    throw new IOException("La lectura final de Bioshock.ini no coincide con lo preparado.");
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
                LocalizedMessageBox.Show(this,
                    "No se han podido guardar los cambios de Bioshock.ini:\n\n" + ex.Message,
                    "Error al guardar Bioshock.ini", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Bioshock.ini no se ha guardado.", Color.Firebrick);
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
            if (_launchPending)
                return;
            if (!SaveConfiguration(false))
                return;
            if (IsGameRunning())
            {
                LocalizedMessageBox.Show(this, "BioShock ya está ejecutándose.", "Juego en ejecución",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(_gameExePath) || !File.Exists(_gameExePath))
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.Title = "Localiza BioshockHD.exe";
                dialog.Filter = "BioShock Remastered (BioshockHD.exe)|BioshockHD.exe|Aplicaciones (*.exe)|*.exe";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                _gameExePath = dialog.FileName;
                UpdatePathStatus();
            }

            try
            {
                ProcessStartInfo steam = new ProcessStartInfo("steam://rungameid/409710");
                steam.UseShellExecute = true;
                Process.Start(steam);
                BeginSteamLaunchWatch();
            }
            catch
            {
                TryStartGameDirectly();
            }
        }

        private void BeginSteamLaunchWatch()
        {
            StopLaunchWatch();
            _launchPending = true;
            _launchDeadlineUtc = DateTime.UtcNow.AddSeconds(30);
            _launchButton.Enabled = false;
            SetStatus("Orden enviada a Steam · esperando a que BioShock se abra…", Blue);
            _launchWatchTimer = new Timer();
            _launchWatchTimer.Interval = 500;
            _launchWatchTimer.Tick += SteamLaunchWatchTick;
            _launchWatchTimer.Start();
        }

        private void SteamLaunchWatchTick(object sender, EventArgs e)
        {
            if (IsGameRunning())
            {
                StopLaunchWatch();
                SetStatus("BioShock VR se ha iniciado mediante Steam.", Success);
                Close();
                return;
            }
            if (DateTime.UtcNow < _launchDeadlineUtc)
                return;

            StopLaunchWatch();
            _launchButton.Enabled = true;
            SetStatus("Steam no ha iniciado BioShock; el lanzador sigue abierto.", Warning);
            DialogResult answer = LocalizedMessageBox.Show(this,
                "Steam ha aceptado la orden, pero BioShock no se ha abierto en 30 segundos.\r\n\r\n" +
                "El lanzador permanecerá abierto. ¿Quieres intentar iniciar BioshockHD.exe directamente?",
                "BioShock no se ha iniciado", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
                return;

            // Steam podría terminar de arrancar mientras se muestra el aviso.
            if (IsGameRunning())
            {
                SetStatus("BioShock VR se ha iniciado mediante Steam.", Success);
                Close();
                return;
            }
            TryStartGameDirectly();
        }

        private void StopLaunchWatch()
        {
            _launchPending = false;
            if (_launchWatchTimer == null)
                return;
            _launchWatchTimer.Stop();
            _launchWatchTimer.Tick -= SteamLaunchWatchTick;
            _launchWatchTimer.Dispose();
            _launchWatchTimer = null;
        }

        private void TryStartGameDirectly()
        {
            try
            {
                ProcessStartInfo direct = new ProcessStartInfo(_gameExePath);
                direct.WorkingDirectory = Path.GetDirectoryName(_gameExePath);
                direct.UseShellExecute = true;
                Process process = Process.Start(direct);
                if (process == null)
                    throw new InvalidOperationException("Windows no devolvió un proceso del juego.");
                process.Dispose();
                SetStatus("BioShock VR se está iniciando directamente.", Success);
                Close();
            }
            catch (Exception ex)
            {
                _launchButton.Enabled = true;
                LocalizedMessageBox.Show(this,
                    "No se ha podido iniciar BioShock:\n\n" + ex.Message,
                    "Error al iniciar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("No se ha podido iniciar el juego; el lanzador sigue abierto.", Color.Firebrick);
            }
        }

        private static bool IsGameRunning()
        {
            Process[] processes = new Process[0];
            try
            {
                processes = Process.GetProcessesByName("BioshockHD");
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        private static string FindGameExecutable()
        {
            List<string> candidates = new List<string>();
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BioshockHD.exe"));

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
            candidates.Add(Path.Combine(steamRoot, "steamapps", "common", "BioShock Remastered",
                                        "Build", "Final", "BioshockHD.exe"));
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
                    LocalizedMessageBox.Show(this, "El fichero todavía no existe. Pulsa Guardar cambios para crearlo.",
                                    "Configuración", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this, ex.Message, "No se pudo abrir el fichero",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowCreditsAndLicenses()
        {
            LocalizedMessageBox.Show(this,
                "Complemento DLSS 4.5 para BioShock VR · Beta 0.2.6\n" +
                "Integración DLSS/DLAA y lanzador: Beren5556\n\n" +
                "AGRADECIMIENTO ESPECIAL A MOHAMAD BALOUZA\n" +
                "Creador de BioShock VR y de la implementación VR fundamental " +
                "sobre la que se construye este fork. Sin su enorme trabajo, " +
                "este proyecto no existiría. Publicado por VR-Stereo-Hub (MIT).\n" +
                "Versión base exacta: BioShock VR v0.8.2\n" +
                "Proyecto: https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr\n" +
                "Creador: https://github.com/mohamad-balouza\n\n" +
                "Host DLSS: Jean-Laurent ROUZIES y NIGos (MIT).\n" +
                "Utiliza NVIDIA DLSS SDK 310.7.0 bajo licencia NVIDIA.\n\n" +
                "Proyecto comunitario no afiliado ni respaldado por 2K, " +
                "Take-Two Interactive o NVIDIA. Las licencias completas se " +
                "instalan junto al lanzador.",
                "Créditos y licencias", MessageBoxButtons.OK,
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
                    LocalizedMessageBox.Show(this,
                        "upscaler.ini todavía no existe. Cambia una opción del bloque experimental y pulsa Guardar para crearlo.",
                        "Reescalado espacial", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this, ex.Message, "No se pudo abrir upscaler.ini",
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
                    LocalizedMessageBox.Show(this,
                        "dlss.ini todavía no existe. Cambia una opción del bloque DLSS 4.5 y pulsa Guardar para crearlo. El archivo por sí solo no instala el backend.",
                        "DLSS 4.5", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this, ex.Message, "No se pudo abrir dlss.ini",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenGameIniFile()
        {
            try
            {
                if (File.Exists(_gameIniPath))
                {
                    ProcessStartInfo info = new ProcessStartInfo("notepad.exe", "\"" + _gameIniPath + "\"");
                    info.UseShellExecute = true;
                    Process.Start(info);
                }
                else
                    LocalizedMessageBox.Show(this, "No se encuentra Bioshock.ini en:\n\n" + _gameIniPath,
                                    "Bioshock.ini", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this, ex.Message, "No se pudo abrir Bioshock.ini",
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
                SetStatus("Se han abierto las copias de vrpreset.ini, dlss.ini y Bioshock.ini.", Success);
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(this, ex.Message, "No se pudo abrir la carpeta",
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
            _statusLabel.Text = UiLanguage.Translate(message);
            _statusLabel.ForeColor = color;
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
            if (!_dirty)
                return;
            DialogResult answer = LocalizedMessageBox.Show(this,
                "Hay cambios sin guardar. ¿Quieres guardarlos antes de cerrar?",
                "Cambios pendientes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
                e.Cancel = true;
            else if (answer == DialogResult.Yes && !SaveConfiguration(false))
                e.Cancel = true;
        }
    }

    internal static class Program
    {
        internal static string InitialGamePath;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--self-test")
                return ConfigDocument.SelfTest() && UpscalerConfigDocument.SelfTest() &&
                       DlssConfigDocument.SelfTest() && DlssQualityPolicy.SelfTest() &&
                       GameIniDocument.SelfTest() && MainForm.ImageControlsSelfTest() &&
                       MainForm.SimpleImageSelfTest() ? 0 : 2;

            if (args != null && (args.Length == 2 || args.Length == 3) &&
                (args[0] == "--preview-image" || args[0] == "--preview-image-en"))
            {
                int tabIndex = 0;
                if (args.Length == 3 && (!int.TryParse(args[2], out tabIndex) || tabIndex < 0 || tabIndex > 6))
                    return 2;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm.WriteImagePreview(args[1], tabIndex, args[0] == "--preview-image-en");
                return 0;
            }

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--game", StringComparison.OrdinalIgnoreCase) &&
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
