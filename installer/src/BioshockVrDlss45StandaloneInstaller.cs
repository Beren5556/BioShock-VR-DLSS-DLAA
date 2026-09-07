using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

[assembly: AssemblyTitle("Instalador BioShock VR DLSS-DLAA Beta 0.2.1")]
[assembly: AssemblyDescription("Instalador autonomo y reversible de BioShock VR DLSS-DLAA Beta 0.2.1")]
[assembly: AssemblyCompany("BioShock VR Community")]
[assembly: AssemblyProduct("BioShock VR DLSS-DLAA Beta")]
[assembly: AssemblyVersion("0.2.1.0")]
[assembly: AssemblyFileVersion("0.2.1.0")]

namespace BioshockVrDlss45Installer
{
    internal sealed class Payload
    {
        internal readonly string Resource;
        internal readonly string RelativePath;
        internal readonly string Sha256;

        internal Payload(string resource, string relativePath, string sha256)
        {
            Resource = resource;
            RelativePath = relativePath;
            Sha256 = sha256;
        }
    }

    internal sealed class InstallRecord
    {
        internal string RelativePath;
        internal bool Existed;
        internal string BackupRelativePath;
        internal string OriginalSha256;
        internal string InstalledSha256;
    }

    internal sealed class InstallManifest
    {
        internal string GameDirectory;
        internal string BackupDirectory;
        internal string ShortcutPath;
        internal bool ShortcutExisted;
        internal string ShortcutBackupPath;
        internal bool ShortcutManaged;
        internal string ShortcutInstalledSha256;
        internal readonly List<InstallRecord> Records = new List<InstallRecord>();
    }

    internal sealed class ShortcutSnapshot
    {
        internal string ShortcutPath;
        internal bool Existed;
        internal string SavedPath;
    }

    internal static class InstallerCore
    {
        internal const string DisplayVersion = "Beta 0.2.1";
        internal const string ExpectedGameSha256 = "AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B";
        internal const string RequiredReleaseUrl = "https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr/releases/tag/v0.8.2";
        internal const string ProjectUrl = "https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr";
        internal const string CreatorUrl = "https://github.com/mohamad-balouza";
        internal const string LauncherName = "Lanzador BioShock VR DLSS-DLAA.exe";
        internal const string ShortcutName = "BioShock VR DLSS-DLAA Beta.lnk";
        internal static string TestLocalRootOverride;

        private static readonly Payload[] Payloads = new Payload[]
        {
            new Payload("xinput1_3.dll", "xinput1_3.dll", "441BF1728BB38A2EC2BA57605CF840D786122E862D47A6DFA642BFF484F8E191"),
            new Payload("bioshockvr.dll", "bioshockvr.dll", "44B0FB0946330AB7F471D230A3E27D686CDFD400B2CF251B70BE6A9364986E7F"),
            new Payload("bvr_steamvr32.dll", "bvr_steamvr32.dll", "56537A2EA8F88FCE6A2928EAECDE11EEEEED9C4D04F36B39E466B4D03330B972"),
            new Payload("openvr_api.dll", "openvr_api.dll", "AB696E4F218A95B3E396BC310F9FE6485DF48C99C0969762083212B1E1F025A6"),
            new Payload("BioShockVR-DLSS45-Host64.exe", Path.Combine("host64", "BioShockVR-DLSS45-Host64.exe"), "480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453"),
            new Payload("nvngx_dlss.dll", Path.Combine("host64", "nvngx_dlss.dll"), "BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E"),
            new Payload("dlss-capabilities.ini", Path.Combine("host64", "dlss-capabilities.ini"), "7C52BD6F6F186C40CDA847F0E143BDCFF94F0CB9BAC355977C27C2E27B857D77"),
            new Payload("Lanzador BioShock VR DLSS-DLAA.exe", LauncherName, "298E4E7E744DBD5EC11FF7A32083B1CA5EB787C7BD8A7F23C504E3062B57D0D4"),
            new Payload("LEEME-DLSS45.md", Path.Combine("BioShockVR-DLSS45", "LEEME-DLSS45.md"), "8AEE2FCA8E2B2AA053FC483417402C81EAD38BFBA6FA9A0CE0A3C00E00E2EB5F"),
            new Payload("NVIDIA-DLSS-LICENSE.txt", Path.Combine("BioShockVR-DLSS45", "NVIDIA-DLSS-LICENSE.txt"), "A3E28883672AB1B48187A0CC004EA468C76F6BEA15F33F0F38A970B7F7E04C64"),
            new Payload("INFORMACION-DEL-PAQUETE.txt", Path.Combine("BioShockVR-DLSS45", "INFORMACION-DEL-PAQUETE.txt"), "C7D9799CC7D1E8CC4E4673B0BB913F8EC3A962BB6B6A030FB079602CC13AF449"),
            new Payload("dlss.ini.example", Path.Combine("BioShockVR-DLSS45", "dlss.ini.example"), "0C8D1260BC3A5782106D95E6D374CE87D03CA0F36D3C198527F3C3359B601329"),
            new Payload("BioShockVR-MIT-LICENSE.txt", Path.Combine("BioShockVR-DLSS45", "Licenses", "BioShockVR-MIT-LICENSE.txt"), "199384980B6925AA5DA072314C0C265BB097F41C7849A7AB0E6DE9294D3D8114"),
            new Payload("DLSS-Host-MIT-LICENSE.txt", Path.Combine("BioShockVR-DLSS45", "Licenses", "DLSS-Host-MIT-LICENSE.txt"), "1CE240E402901FB81EB82A60A6BAFD2FB913CD5746860B0A4EC52A5ACB49CED7"),
            new Payload("THIRD_PARTY_NOTICES.md", Path.Combine("BioShockVR-DLSS45", "Licenses", "THIRD_PARTY_NOTICES.md"), "56EB4D3AEF9087E47113609CE507856A0270A62B8C6E1734CDF0EE5A2B670C13"),
            new Payload("MinHook-LICENSE.txt", Path.Combine("BioShockVR-DLSS45", "Licenses", "MinHook-LICENSE.txt"), "4F21F857550D7BE854DA6EA5F2DA4E6775CA4E3FBB535E4F3D961C47D0BF3335"),
            new Payload("Dear-ImGui-LICENSE.txt", Path.Combine("BioShockVR-DLSS45", "Licenses", "Dear-ImGui-LICENSE.txt"), "F20418B409E53C8C9F4E90917FF395554A60320D4DFBF833DA89B339CAD8628A"),
            new Payload("OpenVR-LICENSE.txt", Path.Combine("BioShockVR-DLSS45", "Licenses", "OpenVR-LICENSE.txt"), "9E6D1480FB68E86CEAFED312F7E67DADCDC2A99B350B710D624B8F0F0F1A2329"),
            new Payload("OpenXR-LICENSE.txt", Path.Combine("BioShockVR-DLSS45", "Licenses", "OpenXR-LICENSE.txt"), "3DDF9BE5C28FE27DAD143A5DC76EEA25222AD1DD68934A047064E56ED2FA40C5"),
            new Payload("OpenXR-COPYING.adoc", Path.Combine("BioShockVR-DLSS45", "Licenses", "OpenXR-COPYING.adoc"), "1B0FF1CFEADAE317A54457E4407EA1AE026AB71555CAF9A20294C492F2514273")
        };

        internal static string LocalRoot
        {
            get
            {
                if (!String.IsNullOrEmpty(TestLocalRootOverride))
                    return Path.GetFullPath(TestLocalRootOverride);
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "BioshockVR", "Installer-DLSS-DLAA-Beta-0.2");
            }
        }

        internal static string ManifestPath
        {
            get { return Path.Combine(LocalRoot, "install.manifest"); }
        }

        internal static bool IsGameRunning()
        {
            return RunningComponents().Length != 0;
        }

        internal static string RunningComponents()
        {
            string[] names = new string[]
            {
                "BioshockHD",
                "BioShockVR-DLSS45-Host64",
                "Lanzador BioShock VR DLSS-DLAA"
            };
            List<string> found = new List<string>();
            foreach (string name in names)
            {
                Process[] processes = new Process[0];
                try
                {
                    processes = Process.GetProcessesByName(name);
                    if (processes.Length != 0) found.Add(name + ".exe");
                }
                catch { }
                finally
                {
                    foreach (Process process in processes) process.Dispose();
                }
            }
            return string.Join(", ", found.ToArray());
        }

        internal static string ValidateGameDirectory(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) return "Selecciona la carpeta de instalación del juego.";
            string full;
            try { full = Path.GetFullPath(directory.Trim().Trim('"')); }
            catch { return "La ruta seleccionada no es válida."; }
            string game = Path.Combine(full, "BioshockHD.exe");
            if (!File.Exists(game)) return "No se encuentra BioshockHD.exe en esa carpeta.";
            try
            {
                if (ReadPeMachine(game) != 0x014c) return "El ejecutable del juego no es compatible con esta versión.";
                string hash = HashFile(game);
                if (!String.Equals(hash, ExpectedGameSha256, StringComparison.OrdinalIgnoreCase))
                    return "Esta versión de BioShock Remastered no es compatible con el instalador.";
            }
            catch (Exception ex)
            {
                return "No se ha podido comprobar la carpeta: " + ex.Message;
            }
            return String.Empty;
        }

        private static ushort ReadPeMachine(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt16() != 0x5a4d) return 0;
                stream.Position = 0x3c;
                int peOffset = reader.ReadInt32();
                if (peOffset <= 0 || peOffset > stream.Length - 6) return 0;
                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550) return 0;
                return reader.ReadUInt16();
            }
        }

        internal static string Install(string gameDirectory, bool createShortcut, Action<string> log)
        {
            string problem = ValidateGameDirectory(gameDirectory);
            if (problem.Length != 0) throw new InvalidOperationException(problem);
            string running = RunningComponents();
            if (running.Length != 0)
                throw new InvalidOperationException("Cierra el juego, el host y el lanzador antes de instalar: " + running);
            gameDirectory = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            Directory.CreateDirectory(LocalRoot);
            InstallManifest existing = LoadManifestIfPresent();
            bool firstInstall = existing == null;
            if (existing != null && !SamePath(existing.GameDirectory, gameDirectory))
                throw new InvalidOperationException("Ya hay una instalacion registrada en otra carpeta. Usa Restaurar / desinstalar antes de cambiar de ubicacion.");

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string rollback = Path.Combine(LocalRoot, "Transaction-" + stamp + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rollback);
            string priorManifest = Path.Combine(rollback, "install.manifest.before");
            if (existing != null) File.Copy(ManifestPath, priorManifest, true);
            InstallManifest manifest = existing;
            if (firstInstall)
            {
                manifest = new InstallManifest();
                manifest.GameDirectory = gameDirectory;
                manifest.BackupDirectory = Path.Combine(LocalRoot, "Original-" + stamp);
                Directory.CreateDirectory(manifest.BackupDirectory);
            }

            List<InstallRecord> current = new List<InstallRecord>();
            ShortcutSnapshot shortcutCurrent = null;
            try
            {
                current = CaptureCurrentFiles(gameDirectory, rollback, firstInstall ? manifest : null, log);
                VerifyAllEmbeddedPayloads();
                if (!firstInstall) PreserveRepairConflicts(gameDirectory, manifest, stamp, log);

                if (createShortcut)
                {
                    shortcutCurrent = CaptureCurrentShortcut(rollback);
                    PrepareOriginalShortcut(manifest);
                }

                // El manifiesto recuperable debe existir antes de modificar un solo
                // payload o acceso directo. Si el proceso se interrumpe, una futura
                // restauracion conserva la fotografia anterior a la primera instalacion.
                SaveManifest(manifest);
                log("Manifiesto recuperable guardado antes de instalar.");

                foreach (Payload payload in Payloads)
                {
                    string destination = SafeCombine(gameDirectory, payload.RelativePath);
                    WritePayload(payload, destination);
                    log("OK  " + payload.RelativePath);
                }

                if (createShortcut)
                {
                    CreateDesktopShortcut(Path.Combine(gameDirectory, LauncherName));
                    manifest.ShortcutInstalledSha256 = HashFile(manifest.ShortcutPath);
                    SaveManifest(manifest);
                    log("OK  acceso directo del Escritorio");
                }

                if (firstInstall)
                    log("Copia original: " + manifest.BackupDirectory);
                else
                    log("Reparacion completada; se conserva la copia original existente.");
                log("Todos los componentes del mod se han instalado y verificado.");
                TryDeleteDirectory(rollback);
                return Path.Combine(gameDirectory, LauncherName);
            }
            catch (Exception installError)
            {
                log("Error: restaurando el estado anterior a esta operacion...");
                try
                {
                    RestoreCapturedCurrent(gameDirectory, rollback, current);
                    RestoreCurrentShortcut(shortcutCurrent);
                    if (firstInstall)
                    {
                        DeleteFileStrict(ManifestPath);
                        TryDeleteDirectory(manifest.BackupDirectory);
                    }
                    else
                    {
                        File.Copy(priorManifest, ManifestPath, true);
                    }
                    TryDeleteDirectory(rollback);
                }
                catch (Exception rollbackError)
                {
                    log("ROLLBACK INCOMPLETO. Se conserva la transaccion: " + rollback);
                    throw new InvalidOperationException(
                        "La instalacion fallo y el rollback no pudo completarse. No borres la transaccion ni las copias.\r\n\r\n" +
                        "Error de instalacion: " + installError.Message + "\r\n\r\n" +
                        "Error de rollback: " + rollbackError.Message + "\r\n\r\n" + rollback,
                        installError);
                }
                throw;
            }
        }

        private static List<InstallRecord> CaptureCurrentFiles(string gameDirectory, string rollback,
                                                               InstallManifest firstManifest, Action<string> log)
        {
            List<InstallRecord> records = new List<InstallRecord>();
            foreach (Payload payload in Payloads)
            {
                string source = SafeCombine(gameDirectory, payload.RelativePath);
                bool existed = File.Exists(source);
                InstallRecord record = new InstallRecord();
                record.RelativePath = payload.RelativePath;
                record.Existed = existed;
                record.InstalledSha256 = payload.Sha256;
                record.BackupRelativePath = payload.RelativePath + ".original";
                record.OriginalSha256 = existed ? HashFile(source) : String.Empty;
                records.Add(record);
                if (existed)
                {
                    string transactionCopy = SafeCombine(rollback, payload.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(transactionCopy));
                    File.Copy(source, transactionCopy, true);
                    if (firstManifest != null)
                    {
                        string permanentCopy = SafeCombine(firstManifest.BackupDirectory, record.BackupRelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(permanentCopy));
                        File.Copy(source, permanentCopy, true);
                        log("Copia  " + payload.RelativePath);
                    }
                }
                if (firstManifest != null) firstManifest.Records.Add(record);
            }
            return records;
        }

        private static void RestoreCapturedCurrent(string gameDirectory, string rollback, List<InstallRecord> records)
        {
            List<string> errors = new List<string>();
            foreach (InstallRecord record in records)
            {
                try
                {
                    string destination = SafeCombine(gameDirectory, record.RelativePath);
                    string saved = SafeCombine(rollback, record.RelativePath);
                    if (record.Existed)
                    {
                        if (!File.Exists(saved))
                            throw new FileNotFoundException("Falta la copia de transaccion.", saved);
                        if (record.OriginalSha256.Length != 0 &&
                            !String.Equals(HashFile(saved), record.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("La copia de transaccion no supera SHA-256: " + record.RelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        File.Copy(saved, destination, true);
                        if (record.OriginalSha256.Length != 0 &&
                            !String.Equals(HashFile(destination), record.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("No se pudo verificar el rollback de " + record.RelativePath);
                    }
                    else
                    {
                        DeleteFileStrict(destination);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(record.RelativePath + ": " + ex.Message);
                }
            }
            if (errors.Count != 0)
                throw new IOException("Rollback incompleto:\r\n" + string.Join("\r\n", errors.ToArray()));
        }

        private static ShortcutSnapshot CaptureCurrentShortcut(string rollback)
        {
            ShortcutSnapshot snapshot = new ShortcutSnapshot();
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            snapshot.ShortcutPath = Path.Combine(desktop, ShortcutName);
            snapshot.Existed = File.Exists(snapshot.ShortcutPath);
            if (snapshot.Existed)
            {
                snapshot.SavedPath = Path.Combine(rollback, "desktop-shortcut.current.lnk");
                File.Copy(snapshot.ShortcutPath, snapshot.SavedPath, true);
            }
            return snapshot;
        }

        private static void RestoreCurrentShortcut(ShortcutSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (snapshot.Existed)
            {
                if (!File.Exists(snapshot.SavedPath))
                    throw new FileNotFoundException("Falta la copia transaccional del acceso directo.", snapshot.SavedPath);
                File.Copy(snapshot.SavedPath, snapshot.ShortcutPath, true);
            }
            else
            {
                DeleteFileStrict(snapshot.ShortcutPath);
            }
        }

        private static void PrepareOriginalShortcut(InstallManifest manifest)
        {
            if (manifest.ShortcutManaged) return;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcut = Path.Combine(desktop, ShortcutName);
            manifest.ShortcutPath = shortcut;
            manifest.ShortcutExisted = File.Exists(shortcut);
            manifest.ShortcutManaged = true;
            if (manifest.ShortcutExisted)
            {
                manifest.ShortcutBackupPath = Path.Combine(manifest.BackupDirectory, "desktop-shortcut.original.lnk");
                File.Copy(shortcut, manifest.ShortcutBackupPath, true);
            }
        }

        private static void PreserveRepairConflicts(string gameDirectory, InstallManifest manifest,
                                                    string stamp, Action<string> log)
        {
            string conflicts = null;
            foreach (Payload payload in Payloads)
            {
                string current = SafeCombine(gameDirectory, payload.RelativePath);
                if (!File.Exists(current)) continue;
                string currentHash = HashFile(current);
                if (String.Equals(currentHash, payload.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                if (conflicts == null)
                {
                    conflicts = Path.Combine(LocalRoot, "Conflicts-Before-Repair-" + stamp + "-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(conflicts);
                }
                string saved = SafeCombine(conflicts, payload.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(saved));
                File.Copy(current, saved, true);
                log("Conflicto preservado  " + payload.RelativePath);
            }

            if (manifest.ShortcutManaged && !String.IsNullOrEmpty(manifest.ShortcutPath) &&
                File.Exists(manifest.ShortcutPath) && !String.IsNullOrEmpty(manifest.ShortcutInstalledSha256))
            {
                string currentHash = HashFile(manifest.ShortcutPath);
                if (!String.Equals(currentHash, manifest.ShortcutInstalledSha256, StringComparison.OrdinalIgnoreCase))
                {
                    if (conflicts == null)
                    {
                        conflicts = Path.Combine(LocalRoot, "Conflicts-Before-Repair-" + stamp + "-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(conflicts);
                    }
                    File.Copy(manifest.ShortcutPath, Path.Combine(conflicts, "desktop-shortcut.modified.lnk"), true);
                    log("Conflicto preservado  acceso directo del Escritorio");
                }
            }

            if (conflicts != null) log("Conflictos guardados en: " + conflicts);
        }

        private static void CreateDesktopShortcut(string launcherPath)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktop, ShortcutName);
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("Windows Script Host no esta disponible para crear el acceso directo.");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { launcherPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(launcherPath) });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "BioShock VR DLSS-DLAA Beta 0.2.1" });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { launcherPath + ",0" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
                catch { }
            }
        }

        internal static string Uninstall(Action<string> log)
        {
            InstallManifest manifest = LoadManifestIfPresent();
            if (manifest == null) throw new InvalidOperationException("No hay una instalacion registrada por este instalador.");
            string running = RunningComponents();
            if (running.Length != 0)
                throw new InvalidOperationException("Cierra el juego, el host y el lanzador antes de restaurar: " + running);
            string gameProblem = ValidateRestoreDirectory(manifest.GameDirectory);
            if (gameProblem.Length != 0) throw new InvalidOperationException("La carpeta registrada ya no es valida:\r\n\r\n" + gameProblem);

            PreflightOriginalBackups(manifest);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string recovery = Path.Combine(LocalRoot, "Before-Uninstall-" + stamp + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(recovery);
            string manifestRecovery = Path.Combine(recovery, "install.manifest.before");
            File.Copy(ManifestPath, manifestRecovery, true);
            List<InstallRecord> current = CaptureCurrentFiles(manifest.GameDirectory, recovery, null, log);
            ShortcutSnapshot shortcutCurrent = manifest.ShortcutManaged ? CaptureCurrentShortcut(recovery) : null;

            foreach (InstallRecord record in manifest.Records)
            {
                string destination = SafeCombine(manifest.GameDirectory, record.RelativePath);
                if (File.Exists(destination) && !String.IsNullOrEmpty(record.InstalledSha256) &&
                    !String.Equals(HashFile(destination), record.InstalledSha256, StringComparison.OrdinalIgnoreCase))
                    log("Conflicto preservado antes de restaurar  " + record.RelativePath);
            }

            try
            {
                foreach (InstallRecord record in manifest.Records)
                {
                    string destination = SafeCombine(manifest.GameDirectory, record.RelativePath);
                    if (record.Existed)
                    {
                        string original = SafeCombine(manifest.BackupDirectory, record.BackupRelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        File.Copy(original, destination, true);
                        if (!String.Equals(HashFile(destination), record.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("No se pudo verificar el original restaurado: " + record.RelativePath);
                        log("Restaurado  " + record.RelativePath);
                    }
                    else
                    {
                        DeleteFileStrict(destination);
                        log("Eliminado    " + record.RelativePath);
                    }
                }

                if (manifest.ShortcutManaged)
                {
                    if (manifest.ShortcutExisted)
                        File.Copy(manifest.ShortcutBackupPath, manifest.ShortcutPath, true);
                    else
                        DeleteFileStrict(manifest.ShortcutPath);
                }
                DeleteFileStrict(ManifestPath);
                log("Copia de seguridad del estado desinstalado: " + recovery);
                return recovery;
            }
            catch (Exception uninstallError)
            {
                log("La restauracion no termino; reponiendo el estado previo a este intento...");
                try
                {
                    RestoreCapturedCurrent(manifest.GameDirectory, recovery, current);
                    RestoreCurrentShortcut(shortcutCurrent);
                    File.Copy(manifestRecovery, ManifestPath, true);
                }
                catch (Exception rollbackError)
                {
                    throw new InvalidOperationException(
                        "La desinstalacion fallo y el rollback no pudo completarse. Conserva esta copia:\r\n" + recovery +
                        "\r\n\r\nError de desinstalacion: " + uninstallError.Message +
                        "\r\n\r\nError de rollback: " + rollbackError.Message,
                        uninstallError);
                }
                throw;
            }
        }

        private static string ValidateRestoreDirectory(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) return "El manifiesto no contiene una carpeta de juego.";
            try
            {
                string full = Path.GetFullPath(directory);
                if (!Directory.Exists(full)) return "La carpeta ya no existe: " + full;
                if (!File.Exists(Path.Combine(full, "BioshockHD.exe")))
                    return "No se encuentra BioshockHD.exe. Selecciona o recupera la carpeta registrada antes de restaurar.";
                return String.Empty;
            }
            catch (Exception ex)
            {
                return "No se puede abrir la carpeta registrada: " + ex.Message;
            }
        }

        private static void PreflightOriginalBackups(InstallManifest manifest)
        {
            List<string> errors = new List<string>();
            foreach (InstallRecord record in manifest.Records)
            {
                if (!record.Existed) continue;
                try
                {
                    string original = SafeCombine(manifest.BackupDirectory, record.BackupRelativePath);
                    if (!File.Exists(original))
                        throw new FileNotFoundException("Falta la copia original.", original);
                    string actual = HashFile(original);
                    if (!String.Equals(actual, record.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SHA-256 distinto del registrado.");
                }
                catch (Exception ex)
                {
                    errors.Add(record.RelativePath + ": " + ex.Message);
                }
            }
            if (manifest.ShortcutManaged && manifest.ShortcutExisted &&
                !File.Exists(manifest.ShortcutBackupPath))
                errors.Add("Acceso directo: falta la copia original.");
            if (errors.Count != 0)
                throw new InvalidDataException("No se modifico nada porque faltan copias originales validas:\r\n" +
                                               string.Join("\r\n", errors.ToArray()));
        }

        internal static InstallManifest LoadManifestIfPresent()
        {
            if (!File.Exists(ManifestPath)) return null;
            string[] lines = File.ReadAllLines(ManifestPath, Encoding.UTF8);
            InstallManifest manifest = new InstallManifest();
            string format = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("Format=", StringComparison.Ordinal))
                    format = line.Substring("Format=".Length);
                else if (line.StartsWith("GameDirectory=", StringComparison.Ordinal))
                    manifest.GameDirectory = Decode(line.Substring("GameDirectory=".Length));
                else if (line.StartsWith("BackupDirectory=", StringComparison.Ordinal))
                    manifest.BackupDirectory = Decode(line.Substring("BackupDirectory=".Length));
                else if (line.StartsWith("ShortcutPath=", StringComparison.Ordinal))
                    manifest.ShortcutPath = Decode(line.Substring("ShortcutPath=".Length));
                else if (line.StartsWith("ShortcutExisted=", StringComparison.Ordinal))
                    manifest.ShortcutExisted = line.EndsWith("1", StringComparison.Ordinal);
                else if (line.StartsWith("ShortcutBackupPath=", StringComparison.Ordinal))
                    manifest.ShortcutBackupPath = Decode(line.Substring("ShortcutBackupPath=".Length));
                else if (line.StartsWith("ShortcutManaged=", StringComparison.Ordinal))
                    manifest.ShortcutManaged = line.EndsWith("1", StringComparison.Ordinal);
                else if (line.StartsWith("ShortcutInstalledSha256=", StringComparison.Ordinal))
                    manifest.ShortcutInstalledSha256 = line.Substring("ShortcutInstalledSha256=".Length);
                else if (line.StartsWith("File|", StringComparison.Ordinal))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length != 6) throw new InvalidDataException("El manifiesto de instalacion esta danado.");
                    InstallRecord record = new InstallRecord();
                    record.RelativePath = Decode(parts[1]);
                    record.Existed = parts[2] == "1";
                    record.InstalledSha256 = parts[3];
                    record.BackupRelativePath = Decode(parts[4]);
                    record.OriginalSha256 = parts[5];
                    manifest.Records.Add(record);
                }
            }
            if (format != "2")
                throw new InvalidDataException("El manifiesto pertenece a un prototipo anterior y no se usara para tocar archivos base. Conserva sus copias y retiralo manualmente tras revisarlo.");
            ValidateManifest(manifest);
            return manifest;
        }

        private static void SaveManifest(InstallManifest manifest)
        {
            Directory.CreateDirectory(LocalRoot);
            string temp = ManifestPath + ".new-" + Guid.NewGuid().ToString("N");
            List<string> lines = new List<string>();
            lines.Add("Format=2");
            lines.Add("Version=0.2.1");
            lines.Add("GameDirectory=" + Encode(manifest.GameDirectory));
            lines.Add("BackupDirectory=" + Encode(manifest.BackupDirectory));
            lines.Add("ShortcutPath=" + Encode(manifest.ShortcutPath));
            lines.Add("ShortcutExisted=" + (manifest.ShortcutExisted ? "1" : "0"));
            lines.Add("ShortcutBackupPath=" + Encode(manifest.ShortcutBackupPath));
            lines.Add("ShortcutManaged=" + (manifest.ShortcutManaged ? "1" : "0"));
            lines.Add("ShortcutInstalledSha256=" + (manifest.ShortcutInstalledSha256 ?? String.Empty));
            foreach (InstallRecord record in manifest.Records)
                lines.Add("File|" + Encode(record.RelativePath) + "|" + (record.Existed ? "1" : "0") + "|" + record.InstalledSha256 + "|" + Encode(record.BackupRelativePath) + "|" + (record.OriginalSha256 ?? String.Empty));
            File.WriteAllLines(temp, lines.ToArray(), new UTF8Encoding(false));
            if (File.Exists(ManifestPath)) File.Replace(temp, ManifestPath, null);
            else File.Move(temp, ManifestPath);
        }

        private static void ValidateManifest(InstallManifest manifest)
        {
            if (String.IsNullOrWhiteSpace(manifest.GameDirectory) ||
                String.IsNullOrWhiteSpace(manifest.BackupDirectory))
                throw new InvalidDataException("El manifiesto de instalacion esta incompleto.");
            if (!IsInside(LocalRoot, manifest.BackupDirectory))
                throw new InvalidDataException("La carpeta de copia del manifiesto no es segura.");
            if (manifest.Records.Count != Payloads.Length)
                throw new InvalidDataException("El manifiesto no contiene el inventario exacto de esta version.");

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InstallRecord record in manifest.Records)
            {
                Payload expected = null;
                foreach (Payload payload in Payloads)
                    if (String.Equals(payload.RelativePath, record.RelativePath, StringComparison.OrdinalIgnoreCase))
                        expected = payload;
                if (expected == null || !seen.Add(record.RelativePath))
                    throw new InvalidDataException("El manifiesto contiene una ruta no admitida o duplicada: " + record.RelativePath);
                if (!String.Equals(record.InstalledSha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("El manifiesto no coincide con el payload aceptado: " + record.RelativePath);
                SafeCombine(manifest.GameDirectory, record.RelativePath);
                SafeCombine(manifest.BackupDirectory, record.BackupRelativePath);
                if (record.Existed && !IsSha256(record.OriginalSha256))
                    throw new InvalidDataException("Falta el SHA-256 original de " + record.RelativePath);
                if (!record.Existed && !String.IsNullOrEmpty(record.OriginalSha256))
                    throw new InvalidDataException("El manifiesto atribuye un hash a un archivo que no existia: " + record.RelativePath);
            }

            if (manifest.ShortcutManaged)
            {
                string expectedShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
                if (String.IsNullOrEmpty(manifest.ShortcutPath) || !SamePath(manifest.ShortcutPath, expectedShortcut))
                    throw new InvalidDataException("La ruta del acceso directo del manifiesto no es segura.");
                if (manifest.ShortcutExisted &&
                    (String.IsNullOrEmpty(manifest.ShortcutBackupPath) || !IsInside(manifest.BackupDirectory, manifest.ShortcutBackupPath)))
                    throw new InvalidDataException("La copia del acceso directo del manifiesto no es segura.");
                if (!String.IsNullOrEmpty(manifest.ShortcutInstalledSha256) && !IsSha256(manifest.ShortcutInstalledSha256))
                    throw new InvalidDataException("El SHA-256 del acceso directo no es valido.");
            }
        }

        private static bool IsSha256(string value)
        {
            return !String.IsNullOrEmpty(value) && Regex.IsMatch(value, "\\A[0-9A-Fa-f]{64}\\z");
        }

        private static bool IsInside(string root, string path)
        {
            if (String.IsNullOrWhiteSpace(root) || String.IsNullOrWhiteSpace(path)) return false;
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string pathFull = Path.GetFullPath(path);
            return pathFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        }

        private static string Encode(string value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string Decode(string value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }

        private static string SafeCombine(string root, string relative)
        {
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string result = Path.GetFullPath(Path.Combine(rootFull, relative));
            if (!result.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Ruta de instalacion no segura: " + relative);
            return result;
        }

        private static bool SamePath(string first, string second)
        {
            return String.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'),
                                 Path.GetFullPath(second).TrimEnd('\\', '/'),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static Stream OpenResource(string fileName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] names = assembly.GetManifestResourceNames();
            foreach (string name in names)
            {
                if (String.Equals(name, fileName, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
                {
                    Stream stream = assembly.GetManifestResourceStream(name);
                    if (stream != null) return stream;
                }
            }
            throw new InvalidDataException("Falta el recurso interno: " + fileName);
        }

        private static void VerifyAllEmbeddedPayloads()
        {
            foreach (Payload payload in Payloads)
            {
                using (Stream stream = OpenResource(payload.Resource))
                using (SHA256 sha = SHA256.Create())
                {
                    string actual = Hex(sha.ComputeHash(stream));
                    if (!String.Equals(actual, payload.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("El recurso interno no supera SHA-256: " + payload.Resource);
                }
            }
        }

        private static void WritePayload(Payload payload, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temp = destination + ".bvr-new-" + Guid.NewGuid().ToString("N");
            try
            {
                using (Stream source = OpenResource(payload.Resource))
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(output);
                string actual = HashFile(temp);
                if (!String.Equals(actual, payload.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 incorrecto al extraer " + payload.RelativePath);
                if (File.Exists(destination)) File.Replace(temp, destination, null);
                else File.Move(temp, destination);
                actual = HashFile(destination);
                if (!String.Equals(actual, payload.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 incorrecto despues de instalar " + payload.RelativePath);
            }
            finally
            {
                TryDeleteFile(temp);
            }
        }

        internal static string HashFile(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (SHA256 sha = SHA256.Create())
                return Hex(sha.ComputeHash(stream));
        }

        private static string Hex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("X2"));
            return builder.ToString();
        }

        private static void TryDeleteFile(string path)
        {
            if (String.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static void DeleteFileStrict(string path)
        {
            if (String.IsNullOrEmpty(path)) return;
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path)) throw new IOException("No se pudo eliminar: " + path);
        }

        private static void TryDeleteDirectory(string path)
        {
            if (String.IsNullOrEmpty(path)) return;
            try
            {
                string full = Path.GetFullPath(path);
                string safeRoot = Path.GetFullPath(LocalRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (full.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
                    Directory.Delete(full, true);
            }
            catch { }
        }

        internal static string RunSelfTest()
        {
            VerifyAllEmbeddedPayloads();
            string root = Path.Combine(Path.GetTempPath(), "BvrDlss45InstallerSelfTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (Payload payload in Payloads)
                {
                    string destination = SafeCombine(root, payload.RelativePath);
                    WritePayload(payload, destination);
                    string actual = HashFile(destination);
                    if (!String.Equals(actual, payload.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Fallo de prueba en " + payload.RelativePath);
                }
                bool traversalRejected = false;
                try { SafeCombine(root, Path.Combine("..", "escape.test")); }
                catch (InvalidOperationException) { traversalRejected = true; }
                if (!traversalRejected) throw new InvalidDataException("SafeCombine no rechazo una salida de la raiz aislada.");
                return "PASS: " + Payloads.Length.ToString() +
                       " recursos extraidos y verificados en aislamiento; paquete autonomo completo; rutas seguras; no se accedio a la carpeta del juego.";
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox _path;
        private readonly Button _browse;
        private readonly Button _install;
        private readonly Button _restore;
        private readonly Button _close;
        private readonly Label _status;

        internal MainForm()
        {
            Text = "Instalador de BioShock VR · DLSS/DLAA Beta 0.2.1";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(650, 292);
            Font = new Font("Segoe UI", 9F);
            BackColor = SystemColors.Control;
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Label title = new Label();
            title.Text = "BioShock VR · DLSS/DLAA Beta 0.2.1";
            title.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            title.ForeColor = SystemColors.ControlText;
            title.Location = new Point(20, 17);
            title.AutoSize = true;
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Mod original de Mohamad Balouza · Base BioShock VR v0.8.2";
            subtitle.ForeColor = SystemColors.GrayText;
            subtitle.Location = new Point(22, 51);
            subtitle.AutoSize = true;
            Controls.Add(subtitle);

            Button about = new Button();
            about.Text = "Acerca de...";
            about.FlatStyle = FlatStyle.System;
            about.Location = new Point(535, 17);
            about.Size = new Size(94, 28);
            about.Click += delegate { using (AboutForm dialog = new AboutForm()) dialog.ShowDialog(this); };
            Controls.Add(about);

            GroupBox folder = new GroupBox();
            folder.Text = "Ruta de instalación del juego";
            folder.Location = new Point(20, 83);
            folder.Size = new Size(609, 102);
            Controls.Add(folder);

            _path = new TextBox();
            _path.Location = new Point(15, 28);
            _path.Size = new Size(482, 25);
            _path.TextChanged += delegate { RefreshValidation(); };
            folder.Controls.Add(_path);

            _browse = new Button();
            _browse.Text = "Explorar...";
            _browse.FlatStyle = FlatStyle.System;
            _browse.Location = new Point(505, 26);
            _browse.Size = new Size(88, 28);
            _browse.Click += BrowseClicked;
            folder.Controls.Add(_browse);

            _status = new Label();
            _status.Text = "Selecciona la carpeta Build\\Final de BioShock Remastered.";
            _status.ForeColor = SystemColors.GrayText;
            _status.AutoEllipsis = true;
            _status.Location = new Point(16, 65);
            _status.Size = new Size(575, 22);
            folder.Controls.Add(_status);

            _install = new Button();
            _install.Text = "Instalar";
            _install.FlatStyle = FlatStyle.System;
            _install.Location = new Point(236, 211);
            _install.Size = new Size(102, 32);
            _install.Click += InstallClicked;
            Controls.Add(_install);

            _restore = new Button();
            _restore.Text = "Restaurar";
            _restore.FlatStyle = FlatStyle.System;
            _restore.Location = new Point(344, 211);
            _restore.Size = new Size(102, 32);
            _restore.Click += RestoreClicked;
            Controls.Add(_restore);

            _close = new Button();
            _close.Text = "Cerrar";
            _close.FlatStyle = FlatStyle.System;
            _close.DialogResult = DialogResult.Cancel;
            _close.Location = new Point(527, 211);
            _close.Size = new Size(102, 32);
            Controls.Add(_close);

            Label note = new Label();
            note.Text = "El instalador incluye todos los componentes del mod y conserva los archivos que sustituya.";
            note.ForeColor = SystemColors.GrayText;
            note.Location = new Point(22, 263);
            note.AutoSize = true;
            Controls.Add(note);

            AcceptButton = _install;
            CancelButton = _close;
            RefreshValidation();
            RefreshRestoreState();
        }

        private void BrowseClicked(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Selecciona la carpeta Build\\Final de BioShock Remastered";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(_path.Text)) dialog.SelectedPath = _path.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.SelectedPath;
            }
        }

        private void RefreshValidation()
        {
            if (String.IsNullOrWhiteSpace(_path.Text))
            {
                _status.Text = "Selecciona la carpeta Build\\Final de BioShock Remastered.";
                _status.ForeColor = SystemColors.GrayText;
                _install.Enabled = false;
                return;
            }

            string problem = InstallerCore.ValidateGameDirectory(_path.Text);
            bool valid = problem.Length == 0;
            _status.Text = valid ? "Carpeta compatible. Todo listo para instalar." : problem;
            _status.ForeColor = valid ? Color.FromArgb(0, 102, 0) : Color.FromArgb(176, 74, 33);
            _install.Enabled = valid;
        }

        private void RefreshRestoreState()
        {
            try { _restore.Enabled = InstallerCore.LoadManifestIfPresent() != null; }
            catch { _restore.Enabled = true; }
        }

        private void SetBusy(bool busy, string text)
        {
            UseWaitCursor = busy;
            _browse.Enabled = !busy;
            _path.Enabled = !busy;
            _close.Enabled = !busy;
            _install.Enabled = !busy && InstallerCore.ValidateGameDirectory(_path.Text).Length == 0;
            _restore.Enabled = !busy && File.Exists(InstallerCore.ManifestPath);
            if (!String.IsNullOrEmpty(text)) _status.Text = text;
            Application.DoEvents();
        }

        private void InstallClicked(object sender, EventArgs e)
        {
            string problem = InstallerCore.ValidateGameDirectory(_path.Text);
            if (problem.Length != 0)
            {
                MessageBox.Show(this, problem, "Carpeta no compatible",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult answer = MessageBox.Show(this,
                "Se instalará BioShock VR DLSS/DLAA Beta 0.2.1 y se guardará una copia de los archivos sustituidos.\r\n\r\n¿Quieres continuar?",
                "Confirmar instalación", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (answer != DialogResult.OK) return;

            SetBusy(true, "Instalando BioShock VR...");
            try
            {
                string gameDirectory = Path.GetFullPath(_path.Text.Trim().Trim('"'));
                string launcher = InstallerCore.Install(gameDirectory, true, delegate { Application.DoEvents(); });
                _status.Text = "Instalación completada.";
                _status.ForeColor = Color.FromArgb(0, 102, 0);
                MessageBox.Show(this,
                    "Instalación completada.\r\n\r\nEl lanzador se abrirá ahora y también queda disponible en el Escritorio.",
                    "BioShock VR instalado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ProcessStartInfo start = new ProcessStartInfo(launcher);
                start.WorkingDirectory = Path.GetDirectoryName(launcher);
                start.Arguments = "--game \"" + Path.Combine(gameDirectory, "BioshockHD.exe") + "\"";
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                _status.Text = "No se pudo completar la instalación.";
                _status.ForeColor = Color.FromArgb(176, 74, 33);
                MessageBox.Show(this, ex.Message, "No se pudo instalar",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, String.Empty);
                RefreshRestoreState();
            }
        }

        private void RestoreClicked(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show(this,
                "Se restaurarán los archivos que había antes de la primera instalación.\r\n\r\n¿Quieres continuar?",
                "Restaurar BioShock VR", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (answer != DialogResult.OK) return;

            SetBusy(true, "Restaurando la instalación anterior...");
            try
            {
                InstallerCore.Uninstall(delegate { Application.DoEvents(); });
                _status.Text = "Restauración completada.";
                _status.ForeColor = Color.FromArgb(0, 102, 0);
                MessageBox.Show(this, "La instalación anterior se ha restaurado correctamente.",
                                "Restauración completada", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _status.Text = "No se pudo completar la restauración.";
                _status.ForeColor = Color.FromArgb(176, 74, 33);
                MessageBox.Show(this, ex.Message, "No se pudo restaurar",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, String.Empty);
                RefreshRestoreState();
            }
        }
    }

    internal sealed class AboutForm : Form
    {
        internal AboutForm()
        {
            Text = "Acerca de BioShock VR DLSS/DLAA";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 336);
            Font = new Font("Segoe UI", 9F);
            BackColor = SystemColors.Control;
            ShowInTaskbar = false;

            Label title = new Label();
            title.Text = "BioShock VR · DLSS/DLAA Beta 0.2.1";
            title.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            title.Location = new Point(20, 18);
            title.AutoSize = true;
            Controls.Add(title);

            Label description = new Label();
            description.Text =
                "Fork beta basado en BioShock VR v0.8.2, con integración experimental\r\n" +
                "de NORMAL, DLAA y DLSS 4.5. El instalador incluye el mod completo.\r\n\r\n" +
                "AGRADECIMIENTO ESPECIAL A MOHAMAD BALOUZA\r\n" +
                "Creador del mod original y de la implementación VR fundamental.\r\n" +
                "Sin su enorme trabajo, este fork no existiría.\r\n" +
                "Proyecto original: VR-Stereo-Hub · Versión base: v0.8.2";
            description.Location = new Point(22, 55);
            description.Size = new Size(470, 142);
            Controls.Add(description);

            AddLink("Versión original v0.8.2", InstallerCore.RequiredReleaseUrl, 220);
            AddLink("Proyecto original", InstallerCore.ProjectUrl, 244);
            AddLink("Perfil de Mohamad Balouza", InstallerCore.CreatorUrl, 268);

            Button close = new Button();
            close.Text = "Cerrar";
            close.FlatStyle = FlatStyle.System;
            close.DialogResult = DialogResult.OK;
            close.Location = new Point(410, 296);
            close.Size = new Size(88, 28);
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
        }

        private void AddLink(string text, string url, int top)
        {
            LinkLabel link = new LinkLabel();
            link.Text = text;
            link.AutoSize = true;
            link.Location = new Point(22, top);
            link.LinkClicked += delegate { OpenOfficialLink(url); };
            Controls.Add(link);
        }

        private static void OpenOfficialLink(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir el enlace:\r\n\r\n" + url + "\r\n\r\n" + ex.Message,
                                "Enlace oficial", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string failureOutput = null;
            try
            {
                if (args.Length > 0 && String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    if (args.Length > 1) failureOutput = args[1];
                    string result = InstallerCore.RunSelfTest();
                    if (args.Length > 1) File.WriteAllText(args[1], result + Environment.NewLine, new UTF8Encoding(false));
                    return 0;
                }
                if (args.Length >= 4 && String.Equals(args[0], "--integration-install", StringComparison.OrdinalIgnoreCase))
                {
                    failureOutput = args[3];
                    InstallerCore.TestLocalRootOverride = args[2];
                    StringBuilder log = new StringBuilder();
                    string launcher = InstallerCore.Install(args[1], false,
                        delegate(string message) { log.AppendLine(message); });
                    File.WriteAllText(args[3], "PASS: " + launcher + Environment.NewLine + log.ToString(),
                                      new UTF8Encoding(false));
                    return 0;
                }
                if (args.Length >= 3 && String.Equals(args[0], "--integration-restore", StringComparison.OrdinalIgnoreCase))
                {
                    failureOutput = args[2];
                    InstallerCore.TestLocalRootOverride = args[1];
                    StringBuilder log = new StringBuilder();
                    string recovery = InstallerCore.Uninstall(
                        delegate(string message) { log.AppendLine(message); });
                    File.WriteAllText(args[2], "PASS: " + recovery + Environment.NewLine + log.ToString(),
                                      new UTF8Encoding(false));
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                if (!String.IsNullOrEmpty(failureOutput))
                {
                    try { File.WriteAllText(failureOutput, "FAIL: " + ex + Environment.NewLine, new UTF8Encoding(false)); }
                    catch { }
                }
                else
                {
                    MessageBox.Show(ex.Message, "Instalador BioShock VR DLSS/DLAA", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return 1;
            }
        }
    }
}
