using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace BioShockMsi
{
    public sealed class SavedFile
    {
        public string Name;
        public bool Existed;
        public string Backup;
        public string Hash;
    }
    public sealed class Snapshot
    {
        public string Game;
        public bool NewOriginal;
        public bool FreshMod;
        public string GameIni;
        public string IniBackup;
        public string IniHash;
        public List<SavedFile> Files = new List<SavedFile>();
    }

    public static partial class Actions
    {
        internal const string GameHash = "AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B";
        internal const string RegistryPath = PayloadPlan.RegistryPath;
        internal const string ShortcutName = "BioShock VR DLSS-DLAA.lnk";

        private static string Full(string path)
        {
            return Path.GetFullPath(path.Trim().Trim('"')).TrimEnd('\\', '/');
        }
        private static string Child(string root, string relative)
        {
            string result = Full(Path.Combine(root, relative));
            if (!result.StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ruta fuera de la carpeta del paquete.");
            return result;
        }
        private static string Hash(string path)
        {
            using (FileStream file = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
        private static string GameDirectory(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return string.Empty;
            string root = Full(candidate);
            if (File.Exists(root) && string.Equals(Path.GetFileName(root), "BioshockHD.exe", StringComparison.OrdinalIgnoreCase))
                root = Path.GetDirectoryName(root);
            if (!File.Exists(Path.Combine(root, "BioshockHD.exe")) &&
                File.Exists(Path.Combine(root, @"Build\Final\BioshockHD.exe"))) root = Path.Combine(root, @"Build\Final");
            return root;
        }
        private static string Problem(string game, bool installing)
        {
            if (string.IsNullOrWhiteSpace(game)) return "Selecciona la carpeta de BioShock Remastered.";
            if (Full(game) == Path.GetPathRoot(Full(game)).TrimEnd('\\')) return "Selecciona la carpeta del juego, no una unidad completa.";
            if (installing)
            {
                string exe = Path.Combine(game, "BioshockHD.exe");
                if (!File.Exists(exe)) return "No se encuentra BioshockHD.exe. Selecciona la carpeta Build\\Final del juego.";
                if (!string.Equals(Hash(exe), GameHash, StringComparison.OrdinalIgnoreCase))
                    return "Esta copia de BioShock Remastered no coincide con la versión Steam compatible.";
            }
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    string name = process.ProcessName;
                    if (name.Equals("BioshockHD", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("BioShockVR-DLSS45-Host64", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Lanzador BioShock VR DLSS-DLAA", StringComparison.OrdinalIgnoreCase))
                        return "Cierra BioShock, el lanzador y los procesos del mod antes de continuar.";
                }
            }
            return string.Empty;
        }
        private static string RegistryValue(string key, string value)
        {
            using (RegistryKey entry = Registry.CurrentUser.OpenSubKey(key))
                return entry == null ? string.Empty : Convert.ToString(entry.GetValue(value), CultureInfo.InvariantCulture);
        }
        internal static List<string> SteamCandidates(string steam)
        {
            HashSet<string> libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> games = new List<string>();
            if (string.IsNullOrWhiteSpace(steam)) return games;
            libraries.Add(Full(steam));
            string libraryFile = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(libraryFile))
                foreach (Match match in Regex.Matches(File.ReadAllText(libraryFile), "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    libraries.Add(Full(match.Groups[1].Value.Replace("\\\\", "\\")));
            foreach (string library in libraries)
            {
                string common = Path.Combine(library, @"steamapps\common");
                string installName = "BioShock Remastered";
                string manifest = Path.Combine(library, @"steamapps\appmanifest_409710.acf");
                if (File.Exists(manifest))
                {
                    Match match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (match.Success) installName = match.Groups[1].Value;
                }
                string candidate = Child(common, Path.Combine(installName, @"Build\Final"));
                if (File.Exists(Path.Combine(candidate, "BioshockHD.exe"))) games.Add(candidate);
            }
            return games;
        }

        internal static string DesktopShortcutChoice(string requested, string saved)
        {
            // An explicit value wins; old installations have no saved choice
            // and default to creating the versioned desktop shortcut.
            string choice = string.IsNullOrEmpty(requested) ? saved : requested;
            return string.IsNullOrEmpty(choice) || choice == "1" ? "1" : "0";
        }

        [CustomAction]
        public static ActionResult DetectGame(Session session)
        {
            try
            {
                // Our recoverable replacement retires the selected .lnk too.
                // A file-only repair (/fa) must therefore also schedule its
                // recreation; otherwise MSI can succeed with the shortcut gone.
                if (!string.IsNullOrEmpty(session["REINSTALL"]))
                {
                    string repairMode = session["REINSTALLMODE"];
                    if (repairMode.IndexOf("s", StringComparison.OrdinalIgnoreCase) < 0)
                        session["REINSTALLMODE"] = repairMode + "s";
                }
                if (session["BVR_SHORTCUTINITIALIZED"] != "1")
                {
                    string choice = DesktopShortcutChoice(
                        session["BVR_DESKTOPSHORTCUT"], RegistryValue(RegistryPath, "DesktopShortcut"));
                    session["BVR_DESKTOPSHORTCUT"] = choice == "1" ? "1" : string.Empty;
                    // An unchecked MSI checkbox clears its property. Do not
                    // mistake that choice for a missing default in execute UI.
                    session["BVR_SHORTCUTINITIALIZED"] = "1";
                }
                string selected = session["BVR_GAMEPATH"];
                if (string.IsNullOrWhiteSpace(selected)) selected = session["GAMEDIR"];
                string registered = RegistryValue(RegistryPath, "GameDirectory");
                if (string.IsNullOrWhiteSpace(selected)) selected = registered;
                if (string.IsNullOrWhiteSpace(selected))
                {
                    string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        @"BioshockVR\Installer-DLSS-DLAA-Beta-0.2\install.manifest");
                    if (File.Exists(legacy))
                        foreach (string line in File.ReadAllLines(legacy))
                            if (line.StartsWith("GameDirectory=", StringComparison.Ordinal))
                            {
                                string previous = GameDirectory(Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(14))));
                                string executable = Path.Combine(previous, "BioshockHD.exe");
                                if (File.Exists(executable) && Hash(executable) == GameHash) selected = previous;
                            }
                }
                if (string.IsNullOrWhiteSpace(selected))
                {
                    List<string> candidates = SteamCandidates(RegistryValue(@"Software\Valve\Steam", "SteamPath"));
                    List<string> compatible = new List<string>();
                    foreach (string candidate in candidates)
                        if (Hash(Path.Combine(candidate, "BioshockHD.exe")) == GameHash) compatible.Add(candidate);
                    // Multiple compatible copies require an explicit user choice.
                    if (compatible.Count == 1) selected = compatible[0];
                }
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    session["BVR_GAMEPATH"] = GameDirectory(selected);
                    session["GAMEDIR"] = session["BVR_GAMEPATH"] + "\\";
                }
                string testRoot = session["BVR_TESTROOT"];
                if (string.IsNullOrWhiteSpace(testRoot)) testRoot = RegistryValue(RegistryPath, "TestRoot");
                if (!string.IsNullOrWhiteSpace(testRoot))
                {
                    if (string.IsNullOrWhiteSpace(selected) || !Full(selected).StartsWith(Full(testRoot) + "\\", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("La prueba MSI debe apuntar a un juego aislado dentro de su carpeta de prueba.");
                    session["DesktopFolder"] = Child(testRoot, "Desktop") + "\\";
                    session["BVR_TESTROOT"] = Full(testRoot);
                }
                return ActionResult.Success;
            }
            catch (Exception ex) { session.Log("Autodetección: " + ex.Message); return ActionResult.Success; }
        }

        [CustomAction]
        public static ActionResult ValidateGame(Session session)
        {
            try
            {
                string game = GameDirectory(session["BVR_GAMEPATH"]);
                string problem = Problem(game, true);
                string registered = RegistryValue(RegistryPath, "GameDirectory");
                if (problem.Length == 0 && registered.Length > 0 &&
                    !string.Equals(Full(registered), game, StringComparison.OrdinalIgnoreCase))
                    problem = "Ya hay una instalación MSI en otra carpeta. Desinstálala antes de cambiar de ubicación.";
                session["BVR_ERROR"] = problem;
                session["BVR_VALID"] = problem.Length == 0 ? "1" : "0";
                if (problem.Length == 0)
                {
                    session["BVR_GAMEPATH"] = game;
                    session["GAMEDIR"] = game + "\\";
                }
                return ActionResult.Success;
            }
            catch (Exception ex) { session["BVR_ERROR"] = ex.Message; session["BVR_VALID"] = "0"; return ActionResult.Success; }
        }

        [CustomAction]
        public static ActionResult Prepare(Session session)
        {
            try
            {
                bool removing = session["REMOVE"].Equals("ALL", StringComparison.OrdinalIgnoreCase);
                session["BVR_DESKTOPSHORTCUT"] = session["BVR_DESKTOPSHORTCUT"] == "1" ? "1" : "0";
                if (!removing)
                {
                    // Costing precedes the folder dialog. Set the feature's
                    // final request after the checkbox choice and before
                    // InstallValidate, for full UI and silent installs alike.
                    FeatureInfo shortcutFeature = session.Features["DesktopShortcut"];
                    InstallState requestedShortcut = session["BVR_DESKTOPSHORTCUT"] == "1" ? InstallState.Local : InstallState.Absent;
                    // Reapplying Local to an already-local feature clears MSI's
                    // reinstall intent. BackupFiles retired the old .lnk, so
                    // that would prevent CreateShortcuts from replacing it.
                    bool keepInstalledSelection = requestedShortcut == InstallState.Local &&
                        shortcutFeature.CurrentState == InstallState.Local;
                    if (!keepInstalledSelection && shortcutFeature.RequestState != requestedShortcut)
                        shortcutFeature.RequestState = requestedShortcut;
                }
                string game = GameDirectory(session["GAMEDIR"]);
                string problem = Problem(game, !removing);
                if (problem.Length > 0) throw new InvalidOperationException(problem);
                string registered = RegistryValue(RegistryPath, "GameDirectory");
                if (registered.Length > 0 && !string.Equals(Full(registered), game, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Ya hay una instalación MSI en otra carpeta. Desinstálala antes de cambiar de ubicación.");
                string testRoot = session["BVR_TESTROOT"];
                string local = session["LocalAppDataFolder"];
                string roaming = session["AppDataFolder"];
                string shortcut = Path.Combine(session["DesktopFolder"], ShortcutName);
                // Test packages have disjoint product/component/registry identities.
                // Their custom actions must never fall back to real user folders.
                if (PayloadPlan.TestFamily.Length > 0 &&
                    (string.IsNullOrWhiteSpace(testRoot) ||
                     !Path.GetFileName(Full(testRoot)).Equals("BvrMsiTest-" + PayloadPlan.TestFamily, StringComparison.OrdinalIgnoreCase) ||
                     !game.Equals(Child(testRoot, @"Game\Build\Final"), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("El paquete aislado requiere su carpeta privada de prueba.");
                if (!string.IsNullOrWhiteSpace(testRoot))
                {
                    if (!game.StartsWith(Full(testRoot) + "\\", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("La carpeta de juego de prueba no está aislada.");
                    local = Child(testRoot, "Local");
                    roaming = Child(testRoot, "Roaming");
                    shortcut = Child(testRoot, @"Desktop\" + ShortcutName);
                }
                string root = Path.Combine(local, @"BioshockVR\WindowsInstaller");
                CustomActionData data = new CustomActionData();
                data["Game"] = game;
                data["Root"] = root;
                data["Transaction"] = Child(root, @"Transactions\" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                data["Shortcut"] = shortcut;
                data["Desktop"] = Path.GetDirectoryName(shortcut);
                data["Ini"] = Path.Combine(roaming, @"BioshockHD\Bioshock\Bioshock.ini");
                data["Dlss"] = Path.Combine(local, @"BioshockVR\dlss.ini");
                data["Removing"] = removing ? "1" : "0";
                data["PreviousVersion"] = RegistryValue(RegistryPath, "Version");
                data["FailTest"] = !string.IsNullOrWhiteSpace(testRoot) ? session["BVR_TESTFAIL"] : string.Empty;
                foreach (string action in new string[] { "BackupFiles", "RollbackFiles", "FinishFiles", "CommitFiles" })
                    session[action] = data.ToString();
                return ActionResult.Success;
            }
            catch (Exception ex) { return Fail(session, ex); }
        }

        private static ActionResult Fail(Session session, Exception ex)
        {
            session.Log("BioShock MSI: " + ex);
            using (Record record = new Record(1))
            {
                record.FormatString = "[1]";
                record[1] = ex.Message;
                session.Message(InstallMessage.Error, record);
            }
            return ActionResult.Failure;
        }
    }
}
