using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace BioshockVrLauncher
{
    internal sealed class ResolutionPreset
    {
        internal readonly int Width;
        internal readonly int Height;
        internal readonly string Label;

        internal ResolutionPreset(int width, int height, string label)
        { Width = width; Height = height; Label = label; }

        public override string ToString() { return Label; }
    }

    internal static class ResolutionPresets
    {
        internal static readonly ResolutionPreset[] Items = {
            new ResolutionPreset(0, 0, "Personalizada / actual"),
            new ResolutionPreset(1920, 1080, "1920 × 1080 · juego plano"),
            new ResolutionPreset(1500, 1500, "1500 × 1500"),
            new ResolutionPreset(1650, 1650, "1650 × 1650"),
            new ResolutionPreset(1800, 1800, "1800 × 1800"),
            new ResolutionPreset(1950, 1950, "1950 × 1950"),
            new ResolutionPreset(2048, 2048, "2048 × 2048 · equilibrada"),
            new ResolutionPreset(2100, 2100, "2100 × 2100"),
            new ResolutionPreset(2250, 2250, "2250 × 2250"),
            new ResolutionPreset(2400, 2400, "2400 × 2400"),
            new ResolutionPreset(2550, 2550, "2550 × 2550"),
            new ResolutionPreset(2560, 2560, "2560 × 2560 · más nítida"),
            new ResolutionPreset(2700, 2700, "2700 × 2700"),
            new ResolutionPreset(2850, 2850, "2850 × 2850"),
            new ResolutionPreset(3000, 3000, "3000 × 3000"),
            new ResolutionPreset(3072, 3072, "3072 × 3072 · GPU potente"),
            new ResolutionPreset(3150, 3150, "3150 × 3150"),
            new ResolutionPreset(3300, 3300, "3300 × 3300"),
            new ResolutionPreset(3450, 3450, "3450 × 3450"),
            new ResolutionPreset(3600, 3600, "3600 × 3600"),
            new ResolutionPreset(3750, 3750, "3750 × 3750"),
            new ResolutionPreset(3900, 3900, "3900 × 3900"),
            new ResolutionPreset(4050, 4050, "4050 × 4050"),
            new ResolutionPreset(4096, 4096, "4096 × 4096 · muy exigente")
        };

        internal static int FindIndex(int width, int height)
        {
            for (int i = 1; i < Items.Length; ++i)
                if (Items[i].Width == width && Items[i].Height == height) return i;
            return 0;
        }
    }

    // Memory-only policy: validate both INI participants before changing either.
    // The caller decides when to apply it and persist the atomic file batch.
    internal static class VrWindowPolicy
    {
        internal static bool Apply(IList<IniEntry> shared, IList<IniEntry> sp)
        {
            IniEntry sharedEntry = FindUnique(shared, "SharedOptions", "Shared.ini");
            IniEntry spEntry = FindUnique(sp, "WinDrv.WindowsClient", "Bioshock2SP.ini");
            string sharedValue = WindowedValue(sharedEntry, "Shared.ini");
            string spValue = WindowedValue(spEntry, "Bioshock2SP.ini");
            bool changed = sharedValue != sharedEntry.Value || spValue != spEntry.Value;
            sharedEntry.Value = sharedValue;
            spEntry.Value = spValue;
            return changed;
        }

        private static IniEntry FindUnique(IList<IniEntry> entries, string section, string file)
        {
            IniEntry found = null;
            if (entries != null)
                foreach (IniEntry entry in entries)
                {
                    if (entry == null ||
                        !string.Equals(entry.Section, section, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(entry.Key, "StartupFullscreen", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (found != null)
                        throw new InvalidDataException(file + " contiene StartupFullscreen duplicado en [" + section + "].");
                    found = entry;
                }
            if (found == null)
                throw new InvalidDataException(file + " debe contener una única clave StartupFullscreen en [" + section + "].");
            return found;
        }

        private static bool ParseSwitch(string value, string file, out bool words, out bool semicolon)
        {
            string normalized = (value ?? string.Empty).Trim();
            semicolon = normalized.EndsWith(";", StringComparison.Ordinal);
            if (semicolon) normalized = normalized.Substring(0, normalized.Length - 1).Trim();
            words = string.Equals(normalized, "True", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, "False", StringComparison.OrdinalIgnoreCase);
            if (words) return string.Equals(normalized, "True", StringComparison.OrdinalIgnoreCase);
            if (normalized == "1") return true;
            if (normalized == "0") return false;
            throw new InvalidDataException(file + " contiene StartupFullscreen no válido: usa True/False o 1/0, con ';' final opcional.");
        }

        private static string WindowedValue(IniEntry entry, string file)
        {
            bool currentWords, currentSemicolon;
            bool enabled = ParseSwitch(entry.Value, file, out currentWords, out currentSemicolon);
            bool originalWords, originalSemicolon;
            ParseSwitch(entry.OriginalValue, file, out originalWords, out originalSemicolon);
            if (!enabled) return entry.Value;
            return (originalWords ? "False" : "0") + (originalSemicolon ? ";" : string.Empty);
        }
    }

    internal static class Bs2Profile
    {
        internal const string ExeName = "Bioshock2HD.exe";
        internal const string ExeHash = "C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C";
        internal const string AppId = "409720";
        internal const string VrDefaults = @"# BioShock 2 VR - tuned slider values (toggles are implied ON)
worldScale=100.0
headUpUu=0.0
headFwdUu=0.0
ipdMm=63.0
gameFovDeg=130.0
fgFovMatch=1
fillHeadsetFov=1
fgFovManual=0.0
handTrimPitchL=0.00
handTrimYawL=0.00
handTrimRollL=0.00
handOffFwdL=0.00
handOffRightL=0.00
handOffUpL=0.00
handScaleL=0.771
aimTrimPitchL=1.19
aimTrimYawL=23.25
aimPosFwdL=0.00
aimPosRightL=3.18
aimPosUpL=0.00
handTrimPitchR=12.50
handTrimYawR=-7.50
handTrimRollR=3.25
handOffFwdR=0.00
handOffRightR=0.00
handOffUpR=0.00
handScaleR=0.760
aimTrimPitchR=17.88
aimTrimYawR=-16.69
aimPosFwdR=-3.58
aimPosRightR=-1.19
aimPosUpR=11.13
originOn=1
dotDistM=3.00
armsMode=1
scaleWeapon=0
animMode=1
animTrans=0.00
wScale=0.770
wOffFwd=-6.30
wOffRight=0.00
wOffUp=0.00
laserL=0
laserR=0
dotL=1
dotR=1
turnScale=1.00
snapOn=0
snapAngle=45.0
ammoMod=1
cineBarsHidden=1
cineDrive=2
cineSubsInFrame=0
effectsInFrame=0
effectMaxVerts=8
postFxRtOnly=1
hudQuadDistM=1.30
hudQuadWidthM=1.25
hudQuadUpM=-0.10

# Auto-start VR at launch. 1 = the mod arms the full VR stack itself on the
# first frame (no F10 needed); 0 = the old behaviour, arm it from the F10
# preset button. Set this to 0 WITH THE GAME CLOSED if a launch ever misbehaves
# - it is the same switch as the ""Auto-start VR at launch"" checkbox in F10.
autoVr=1
";
        internal const string WeaponDefaults = @"# BioShock 2 VR - per-weapon RIGHT-hand aim/model profiles
# <Class>.<field>=<value>; fields: aimTrim*/aimPos* (deg/cm), modTrim*/modOff* (deg/cm), modScale, wScale
PlayerDistanceHackingTool.aimTrimPitch=11.72
PlayerDistanceHackingTool.aimTrimYaw=-8.94
PlayerDistanceHackingTool.aimPosFwd=0.00
PlayerDistanceHackingTool.aimPosRight=-0.00
PlayerDistanceHackingTool.aimPosUp=11.13
PlayerDistanceHackingTool.modTrimPitch=12.50
PlayerDistanceHackingTool.modTrimYaw=-7.50
PlayerDistanceHackingTool.modTrimRoll=3.25
PlayerDistanceHackingTool.modOffFwd=0.00
PlayerDistanceHackingTool.modOffRight=0.00
PlayerDistanceHackingTool.modOffUp=0.00
PlayerDistanceHackingTool.modScale=0.76
PlayerDistanceHackingTool.wScale=0.75
PlayerDistanceHackingTool.wOffFwd=0.00
PlayerDistanceHackingTool.wOffRight=0.00
PlayerDistanceHackingTool.wOffUp=0.00
PlayerDrill.aimTrimPitch=15.50
PlayerDrill.aimTrimYaw=-10.93
PlayerDrill.aimPosFwd=-19.87
PlayerDrill.aimPosRight=2.38
PlayerDrill.aimPosUp=18.68
PlayerDrill.modTrimPitch=12.50
PlayerDrill.modTrimYaw=-7.50
PlayerDrill.modTrimRoll=3.25
PlayerDrill.modOffFwd=0.00
PlayerDrill.modOffRight=0.00
PlayerDrill.modOffUp=0.00
PlayerDrill.modScale=0.76
PlayerDrill.wScale=0.75
PlayerDrill.wOffFwd=0.00
PlayerDrill.wOffRight=0.00
PlayerDrill.wOffUp=0.00
PlayerGrenadeLauncher.aimTrimPitch=10.33
PlayerGrenadeLauncher.aimTrimYaw=-12.32
PlayerGrenadeLauncher.aimPosFwd=0.00
PlayerGrenadeLauncher.aimPosRight=-4.37
PlayerGrenadeLauncher.aimPosUp=26.62
PlayerGrenadeLauncher.modTrimPitch=12.50
PlayerGrenadeLauncher.modTrimYaw=-7.50
PlayerGrenadeLauncher.modTrimRoll=3.25
PlayerGrenadeLauncher.modOffFwd=0.00
PlayerGrenadeLauncher.modOffRight=0.00
PlayerGrenadeLauncher.modOffUp=0.00
PlayerGrenadeLauncher.modScale=0.76
PlayerGrenadeLauncher.wScale=0.75
PlayerGrenadeLauncher.wOffFwd=0.00
PlayerGrenadeLauncher.wOffRight=0.00
PlayerGrenadeLauncher.wOffUp=0.00
PlayerMachineGun.aimTrimPitch=12.52
PlayerMachineGun.aimTrimYaw=-9.93
PlayerMachineGun.aimPosFwd=0.00
PlayerMachineGun.aimPosRight=-3.97
PlayerMachineGun.aimPosUp=26.62
PlayerMachineGun.modTrimPitch=12.50
PlayerMachineGun.modTrimYaw=-7.50
PlayerMachineGun.modTrimRoll=3.25
PlayerMachineGun.modOffFwd=0.00
PlayerMachineGun.modOffRight=0.00
PlayerMachineGun.modOffUp=0.00
PlayerMachineGun.modScale=0.76
PlayerMachineGun.wScale=0.75
PlayerMachineGun.wOffFwd=-7.47
PlayerMachineGun.wOffRight=0.00
PlayerMachineGun.wOffUp=-0.00
PlayerResearchVideoCamera.aimTrimPitch=0.00
PlayerResearchVideoCamera.aimTrimYaw=0.00
PlayerResearchVideoCamera.aimPosFwd=0.00
PlayerResearchVideoCamera.aimPosRight=0.00
PlayerResearchVideoCamera.aimPosUp=0.00
PlayerResearchVideoCamera.modTrimPitch=12.50
PlayerResearchVideoCamera.modTrimYaw=-7.50
PlayerResearchVideoCamera.modTrimRoll=3.25
PlayerResearchVideoCamera.modOffFwd=0.00
PlayerResearchVideoCamera.modOffRight=0.00
PlayerResearchVideoCamera.modOffUp=0.00
PlayerResearchVideoCamera.modScale=1.35
PlayerResearchVideoCamera.wScale=1.00
PlayerResearchVideoCamera.wOffFwd=0.00
PlayerResearchVideoCamera.wOffRight=0.00
PlayerResearchVideoCamera.wOffUp=0.00
PlayerRivetGun.aimTrimPitch=17.88
PlayerRivetGun.aimTrimYaw=-16.69
PlayerRivetGun.aimPosFwd=-3.58
PlayerRivetGun.aimPosRight=-1.19
PlayerRivetGun.aimPosUp=11.13
PlayerRivetGun.modTrimPitch=12.50
PlayerRivetGun.modTrimYaw=-7.50
PlayerRivetGun.modTrimRoll=3.25
PlayerRivetGun.modOffFwd=0.00
PlayerRivetGun.modOffRight=0.00
PlayerRivetGun.modOffUp=0.00
PlayerRivetGun.modScale=0.76
PlayerRivetGun.wScale=0.77
PlayerRivetGun.wOffFwd=-6.30
PlayerRivetGun.wOffRight=0.00
PlayerRivetGun.wOffUp=0.00
PlayerShotgun.aimTrimPitch=14.31
PlayerShotgun.aimTrimYaw=-14.31
PlayerShotgun.aimPosFwd=1.99
PlayerShotgun.aimPosRight=-0.79
PlayerShotgun.aimPosUp=10.33
PlayerShotgun.modTrimPitch=12.50
PlayerShotgun.modTrimYaw=-7.50
PlayerShotgun.modTrimRoll=3.25
PlayerShotgun.modOffFwd=0.00
PlayerShotgun.modOffRight=0.00
PlayerShotgun.modOffUp=0.00
PlayerShotgun.modScale=0.79
PlayerShotgun.wScale=0.77
PlayerShotgun.wOffFwd=-11.44
PlayerShotgun.wOffRight=0.00
PlayerShotgun.wOffUp=0.00
PlayerSpeargun.aimTrimPitch=14.90
PlayerSpeargun.aimTrimYaw=-15.89
PlayerSpeargun.aimPosFwd=17.48
PlayerSpeargun.aimPosRight=-17.48
PlayerSpeargun.aimPosUp=29.40
PlayerSpeargun.modTrimPitch=12.50
PlayerSpeargun.modTrimYaw=-7.50
PlayerSpeargun.modTrimRoll=3.25
PlayerSpeargun.modOffFwd=0.00
PlayerSpeargun.modOffRight=0.00
PlayerSpeargun.modOffUp=0.00
PlayerSpeargun.modScale=0.76
PlayerSpeargun.wScale=0.75
PlayerSpeargun.wOffFwd=-7.24
PlayerSpeargun.wOffRight=0.00
PlayerSpeargun.wOffUp=-2.10
";

        internal static string LocalDirectory
        {
            get { return string.IsNullOrEmpty(Program.SandboxRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BioshockVR", "bs2")
                : Path.Combine(Program.SandboxRoot, "LocalAppData", "BioshockVR", "bs2"); }
        }
        internal static string GameIniDirectory
        {
            get { return string.IsNullOrEmpty(Program.SandboxRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BioshockHD", "Bioshock2")
                : Path.Combine(Program.SandboxRoot, "AppData", "BioshockHD", "Bioshock2"); }
        }

        internal static List<ParamDef> AdaptDefinitions(List<ParamDef> source)
        {
            Dictionary<string, string> defaults = ConfigDocument.Parse(VrDefaults);
            defaults["bodyRate"] = "0.0";
            defaults["bodyDeadzone"] = "0.0";
            defaults["moveDirInstant"] = "1";
            defaults["crosshairVisible"] = "0";
            Dictionary<string, string> mapped = new Dictionary<string, string> {
                {"aimTrimLPitch","aimTrimPitchL"}, {"aimTrimLYaw","aimTrimYawL"},
                {"aimTrimRPitch","aimTrimPitchR"}, {"aimTrimRYaw","aimTrimYawR"},
                {"aimPosLFwd","aimPosFwdL"}, {"aimPosLRight","aimPosRightL"}, {"aimPosLUp","aimPosUpL"},
                {"aimPosRFwd","aimPosFwdR"}, {"aimPosRRight","aimPosRightR"}, {"aimPosRUp","aimPosUpR"},
                {"aimDotDistM","dotDistM"}, {"bodyDeadzoneDeg","bodyDeadzone"},
                {"snapTurn","snapOn"}, {"snapAngleDeg","snapAngle"}
            };
            List<ParamDef> result = new List<ParamDef>();
            HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ParamDef item in source)
            {
                string key;
                if (mapped.TryGetValue(item.Key, out key)) item.Key = key;
                if (!defaults.TryGetValue(item.Key, out key)) continue;
                item.DefaultValue = key;
                if (item.Kind == ParamKind.Choice)
                    item.DefaultValue = key;
                if (item.Key == "gameFovDeg")
                    item.Description = "FOV de respaldo del motor BS2. Con Llenar FOV del visor activado, se usa el campo de visión real de OpenXR.";
                result.Add(item);
                added.Add(item.Key);
            }
            foreach (KeyValuePair<string, string> pair in defaults)
            {
                if (added.Contains(pair.Key)) continue;
                result.Add(MakeAdditional(pair.Key, pair.Value));
            }
            Dictionary<string, string> weapons = ConfigDocument.Parse(WeaponDefaults);
            foreach (KeyValuePair<string, string> pair in weapons)
            {
                string field = pair.Key.Substring(pair.Key.IndexOf('.') + 1);
                bool scale = field == "modScale" || field == "wScale";
                bool angle = field.IndexOf("Trim", StringComparison.Ordinal) >= 0;
                ParamDef item = ParamDef.Number(pair.Key, "Armas", FieldLabel(field),
                    scale ? "Escala visual del perfil de esta arma. 1,00 conserva el tamaño original."
                          : (angle ? "Corrección angular específica de esta arma." : "Desplazamiento específico de esta arma, en el espacio local del mando."),
                    scale ? "multiplicador" : angle ? "grados" : "cm",
                    scale ? 0.06m : -180m, scale ? 10m : 180m, scale ? 0.01m : 0.1m,
                    scale ? 3 : 2, pair.Value);
                result.Add(item);
            }
            return result;
        }

        private static ParamDef MakeAdditional(string key, string value)
        {
            const string hands = "Manos y apuntado";
            const string camera = "Cámara y escala";
            if (key == "fgFovMatch") return ParamDef.Boolean(key, camera, "Armas ajustadas al FOV de la vista",
                "Sincroniza el campo de visión de manos y armas con el mundo para BioShock 2.", value == "1");
            if (key == "fillHeadsetFov") return ParamDef.Boolean(key, camera, "Llenar el FOV del visor",
                "Usa el campo de visión completo comunicado por OpenXR.", value == "1");
            if (key == "fgFovManual") return ParamDef.Number(key, camera, "FOV manual de manos y armas",
                "Valor avanzado. 0 conserva el ajuste automático; se usa cuando se desactiva la sincronización del FOV.",
                "grados", 0, 180, 1, 1, value);
            if (key == "armsMode") return ParamDef.Choice(key, hands, "Comportamiento de los brazos",
                "Brazos originales, acompañando los mandos, u ocultos.",
                new string[] {"0 · Original", "1 · Seguir mandos", "2 · Ocultar"}, 1);
            if (key == "ammoMod") return ParamDef.Choice(key, "Movimiento y giro", "Modificador de selección de munición",
                "Botón modificador utilizado para seleccionar munición con los controles de BioShock 2.",
                new string[] {"0 · Pulsación de palanca", "1 · Apoyo del pulgar", "2 · Ambos"}, int.Parse(value, CultureInfo.InvariantCulture));
            if (key == "originOn") return ParamDef.Boolean(key, hands, "Disparos desde los mandos",
                "Sitúa el origen de los disparos en la mano correspondiente.", value == "1");
            if (key == "scaleWeapon") return ParamDef.Boolean(key, hands, "Escalar el arma con la mano",
                "Aplica al arma adjunta la escala de la mano; puede combinarse con la escala propia del arma.", value == "1");
            if (key == "animMode") return ParamDef.Boolean(key, hands, "Animaciones originales en las manos",
                "Conserva las animaciones del juego, como recargar o atacar, mientras las manos siguen los mandos.", value == "1");
            if (key == "animTrans") return ParamDef.Number(key, hands, "Recorrido de animación de las manos",
                "Cantidad de desplazamiento original de la muñeca que se conserva. 0 mantiene la posición del mando.",
                "proporción", 0, 1, 0.05m, 2, value);
            if (key == "laserL" || key == "laserR" || key == "dotL" || key == "dotR")
                return ParamDef.Boolean(key, hands, (key.StartsWith("laser") ? "Láser" : "Punto de mira") +
                    (key.EndsWith("L") ? " izquierdo" : " derecho"),
                    "Ayuda de apuntado de la mano correspondiente.", value == "1");
            bool scale = key.IndexOf("Scale", StringComparison.Ordinal) >= 0;
            bool angle = key.IndexOf("Trim", StringComparison.Ordinal) >= 0;
            string side = key.EndsWith("L") ? " · izquierda" : key.EndsWith("R") ? " · derecha" : "";
            string baseKey = side.Length > 0 ? key.Substring(0, key.Length - 1) : key;
            return ParamDef.Number(key, hands, FieldLabel(baseKey) + side,
                angle ? "Corrección angular del modelo de la mano; es independiente de la dirección real del disparo."
                      : "Desplazamiento visual local del modelo; los perfiles de arma pueden sustituir estos valores.",
                scale ? "multiplicador" : angle ? "grados" : "cm",
                scale ? 0.06m : -180m, scale ? 10m : 180m, scale ? 0.01m : 0.1m, scale ? 3 : 2, value);
        }

        internal static string FieldLabel(string field)
        {
            switch (field)
            {
                case "aimTrimPitch": return "Apuntado · inclinación";
                case "aimTrimYaw": return "Apuntado · giro";
                case "aimPosFwd": return "Origen disparo · delante";
                case "aimPosRight": return "Origen disparo · lateral";
                case "aimPosUp": return "Origen disparo · altura";
                case "modTrimPitch": case "handTrimPitch": return "Modelo mano · inclinación";
                case "modTrimYaw": case "handTrimYaw": return "Modelo mano · giro";
                case "modTrimRoll": case "handTrimRoll": return "Modelo mano · rotación lateral";
                case "modOffFwd": case "handOffFwd": return "Modelo mano · delante";
                case "modOffRight": case "handOffRight": return "Modelo mano · lateral";
                case "modOffUp": case "handOffUp": return "Modelo mano · altura";
                case "modScale": return "Escala de la mano";
                case "wScale": return "Escala del arma";
                case "wOffFwd": return "Arma · delante";
                case "wOffRight": return "Arma · lateral";
                case "wOffUp": return "Arma · altura";
                default: return field;
            }
        }

        internal static readonly string[] WeaponClasses = {
            "PlayerDistanceHackingTool", "PlayerDrill", "PlayerGrenadeLauncher", "PlayerMachineGun",
            "PlayerResearchVideoCamera", "PlayerRivetGun", "PlayerShotgun", "PlayerSpeargun"
        };
        internal static readonly string[] WeaponNames = {
            "Herramienta de pirateo", "Taladro", "Lanzagranadas", "Ametralladora",
            "Cámara de investigación", "Remachadora", "Escopeta", "Lanzarpones"
        };

        internal static bool VerifyExecutable(string path, out string problem)
        {
            problem = null;
            if (!File.Exists(path) || !string.Equals(Path.GetFileName(path), ExeName, StringComparison.OrdinalIgnoreCase))
                problem = "Selecciona Bioshock2HD.exe de BioShock 2 Remastered.";
            else if (!string.Equals(Hash(path), ExeHash, StringComparison.OrdinalIgnoreCase))
                problem = "La versión de Bioshock2HD.exe no coincide con la compilación Steam compatible (build 8552776). Verifica los archivos del juego en Steam.";
            return problem == null;
        }
        internal static string Hash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }
    }

    internal sealed class ConfigWrite
    {
        internal string PathName;
        internal string Expected;
        internal string Content;
        internal Encoding Encoding;
        internal string Backup;
        internal string Temporary;
        internal bool Existed;
        internal bool Applied;
        internal ConfigWrite(string path, string expected, string content, Encoding encoding)
        { PathName = path; Expected = expected ?? ""; Content = content; Encoding = encoding; }
    }

    // Back up and validate every participant before replacing any file. If one replacement
    // fails, restore the originals from verified backups, preserving their exact bytes.
    internal static class AtomicConfigBatch
    {
        internal static void Save(IList<ConfigWrite> writes)
        {
            List<ConfigWrite> changed = new List<ConfigWrite>();
            try
            {
                foreach (ConfigWrite item in writes)
                {
                    item.Existed = File.Exists(item.PathName);
                    string actual = item.Existed ? File.ReadAllText(item.PathName, item.Encoding) : "";
                    if (actual != item.Expected)
                        throw new IOException(System.IO.Path.GetFileName(item.PathName) + " cambió externamente. Recarga antes de guardar.");
                    if (actual == item.Content && item.Existed) continue;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(item.PathName));
                    if (item.Existed)
                    {
                        string backupDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(item.PathName), "Copias del lanzador");
                        Directory.CreateDirectory(backupDirectory);
                        item.Backup = System.IO.Path.Combine(backupDirectory,
                            System.IO.Path.GetFileNameWithoutExtension(item.PathName) + "-" +
                            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" +
                            Guid.NewGuid().ToString("N").Substring(0, 8) + ".ini");
                        File.Copy(item.PathName, item.Backup, false);
                        if (Bs2Profile.Hash(item.PathName) != Bs2Profile.Hash(item.Backup))
                            throw new IOException("No se pudo verificar la copia de " + item.PathName);
                    }
                    item.Temporary = item.PathName + ".lanzador-" + Guid.NewGuid().ToString("N") + ".tmp";
                    changed.Add(item);
                    File.WriteAllText(item.Temporary, item.Content, item.Encoding);
                    if (File.ReadAllText(item.Temporary, item.Encoding) != item.Content)
                        throw new IOException("Falló la verificación temporal de " + item.PathName);
                }
                foreach (ConfigWrite item in changed)
                {
                    bool existsNow = File.Exists(item.PathName);
                    if (existsNow != item.Existed ||
                        (existsNow && (File.ReadAllText(item.PathName, item.Encoding) != item.Expected ||
                         Bs2Profile.Hash(item.PathName) != Bs2Profile.Hash(item.Backup))))
                        throw new IOException("El archivo volvió a cambiar durante el guardado: " + item.PathName);
                    if (item.Existed) File.Replace(item.Temporary, item.PathName, null, true);
                    else File.Move(item.Temporary, item.PathName);
                    item.Temporary = null;
                    item.Applied = true;
                    if (File.ReadAllText(item.PathName, item.Encoding) != item.Content)
                        throw new IOException("La lectura final no coincide: " + item.PathName);
                }
            }
            catch (Exception original)
            {
                List<string> rollbackErrors = new List<string>();
                for (int i = changed.Count - 1; i >= 0; --i)
                {
                    ConfigWrite item = changed[i];
                    if (!item.Applied) continue;
                    try
                    {
                        if (item.Existed)
                        {
                            string restore = item.PathName + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp";
                            try
                            {
                                File.Copy(item.Backup, restore, false);
                                File.Replace(restore, item.PathName, null, true);
                            }
                            finally { if (File.Exists(restore)) File.Delete(restore); }
                        }
                        else File.Delete(item.PathName);
                    }
                    catch (Exception restoreError)
                    { rollbackErrors.Add(item.PathName + ": " + restoreError.Message + " Copia: " + item.Backup); }
                }
                if (rollbackErrors.Count > 0)
                    throw new IOException(original.Message + "\nNo se pudo completar la recuperación:\n" + string.Join("\n", rollbackErrors.ToArray()), original);
                throw;
            }
            finally
            {
                foreach (ConfigWrite item in changed)
                    if (item.Temporary != null && File.Exists(item.Temporary))
                        try { File.Delete(item.Temporary); } catch { }
            }
        }

        internal static Encoding DetectEncoding(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return new UTF8Encoding(true);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;
            return Encoding.GetEncoding(1252);
        }
    }

    internal sealed class GameProcessObservation
    {
        internal int Id;
        internal DateTime StartedUtc;
        internal string ImagePath;
        internal bool HasWindow;
        internal bool Responding;
    }

    internal enum LaunchOutcome { Waiting, Started, ExitedEarly, TimedOut }

    internal sealed class GameLaunchTracker
    {
        private readonly DateTime _requestedUtc;
        private readonly string _expectedPath;
        private readonly ISet<int> _previousIds;
        private int _candidateId;
        private DateTime _readySinceUtc;
        internal GameLaunchTracker(DateTime requestedUtc, string expectedPath, ISet<int> previousIds)
        {
            _requestedUtc = requestedUtc;
            _expectedPath = expectedPath;
            _previousIds = new HashSet<int>(previousIds);
        }

        internal LaunchOutcome Observe(DateTime nowUtc, IList<GameProcessObservation> observations)
        {
            if ((nowUtc - _requestedUtc).TotalSeconds >= 60) return LaunchOutcome.TimedOut;
            GameProcessObservation match = null;
            foreach (GameProcessObservation observation in observations)
            {
                if (!GameLaunchEvidence.Matches(observation.Id, observation.StartedUtc, observation.ImagePath,
                    _previousIds, _requestedUtc, _expectedPath)) continue;
                if (match == null || observation.Id == _candidateId) match = observation;
            }
            if (match == null && _candidateId != 0) return LaunchOutcome.ExitedEarly;
            if (match != null)
            {
                if (_candidateId != match.Id)
                {
                    _candidateId = match.Id;
                    _readySinceUtc = DateTime.MinValue;
                }
                if (!match.HasWindow || !match.Responding)
                    _readySinceUtc = DateTime.MinValue;
                else
                {
                    if (_readySinceUtc == DateTime.MinValue) _readySinceUtc = nowUtc;
                    if ((nowUtc - _readySinceUtc).TotalSeconds >= 3) return LaunchOutcome.Started;
                }
            }
            return LaunchOutcome.Waiting;
        }
    }

    internal static class GameLaunchEvidence
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int id);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        internal static string ProcessPath(int id)
        {
            IntPtr handle = OpenProcess(0x1000, false, id);
            if (handle == IntPtr.Zero) return null;
            try
            {
                StringBuilder path = new StringBuilder(32768);
                int size = path.Capacity;
                return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString() : null;
            }
            finally { CloseHandle(handle); }
        }
        internal static bool Matches(int id, DateTime startedUtc, string actualPath,
                                     ISet<int> previousIds, DateTime requestedUtc, string expectedPath)
        {
            return !previousIds.Contains(id) && startedUtc >= requestedUtc &&
                !string.IsNullOrEmpty(actualPath) &&
                string.Equals(System.IO.Path.GetFullPath(actualPath),
                    System.IO.Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase);
        }
    }
}
