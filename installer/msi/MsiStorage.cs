using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using WixToolset.Dtf.WindowsInstaller;

namespace BioShockMsi
{
    public static partial class Actions
    {
        private static readonly string[] GraphicsKeys = {
            "HighDetailShaders", "Shadows", "RealTimeReflection", "PostProcessing",
            "UseRippleSystem", "UseHighDetailSoftParticles", "UseDistortion",
            "UseHighDetailPostProcEffects", "FluidSurfaceDetail"
        };

        private static string Destination(CustomActionData data, string name)
        {
            if (name == "@shortcut") return data["Shortcut"];
            // Keep the legacy token for 0.2.7/0.2.8 recovery snapshots. Each new
            // shortcut carries its own version so an upgrade cannot restore it
            // under the name of a different release.
            const string prefix = "@shortcut:";
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                string version = name.Substring(prefix.Length);
                if (!Regex.IsMatch(version, @"\A[0-9]+\.[0-9]+\.[0-9]+\z"))
                    throw new InvalidDataException("Versión de acceso directo no válida.");
                return Child(data["Desktop"], "BioShock VR DLSS-DLAA " + version + ".lnk");
            }
            foreach (string[] item in PayloadPlan.Items)
                if (item[0] == name) return Child(data["Game"], name);
            throw new InvalidDataException("Archivo no reconocido en la copia del instalador.");
        }
        private static void SaveSnapshot(string path, Snapshot snapshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (FileStream stream = new FileStream(temporary, FileMode.CreateNew))
                new XmlSerializer(typeof(Snapshot)).Serialize(stream, snapshot);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        private static Snapshot LoadSnapshot(string path)
        {
            using (FileStream stream = File.OpenRead(path))
                return (Snapshot)new XmlSerializer(typeof(Snapshot)).Deserialize(stream);
        }
        private static void Copy(string source, string destination)
        {
            if (File.Exists(destination) && (File.GetAttributes(destination) & System.IO.FileAttributes.ReparsePoint) != 0)
                throw new IOException("No se sobrescribirá un enlace de archivo: " + destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
        }
        private static void CheckSnapshot(CustomActionData data, Snapshot snapshot)
        {
            if (!string.Equals(Full(snapshot.Game), Full(data["Game"]), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La copia pertenece a otra instalación del juego.");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SavedFile file in snapshot.Files)
            {
                Destination(data, file.Name);
                if (!seen.Add(file.Name)) throw new InvalidDataException("Copia con archivos duplicados.");
                if (file.Existed && (!File.Exists(Child(data["Root"], file.Backup)) ||
                    Hash(Child(data["Root"], file.Backup)) != file.Hash))
                    throw new IOException("Falta una copia original válida de " + file.Name + ". No se han retirado los archivos del mod.");
            }
        }

        [CustomAction]
        public static ActionResult BackupFiles(Session session)
        {
            try
            {
                CustomActionData data = session.CustomActionData;
                string original = Child(data["Root"], "Original.xml");
                if (File.Exists(original)) CheckSnapshot(data, LoadSnapshot(original));
                Snapshot snapshot = new Snapshot();
                snapshot.Game = data["Game"];
                snapshot.NewOriginal = !File.Exists(original);
                snapshot.FreshMod = data["Removing"] != "1" &&
                    !File.Exists(Child(data["Game"], "bioshockvr.dll")) && !File.Exists(data["Dlss"]);
                List<string> names = new List<string>();
                foreach (string[] item in PayloadPlan.Items) names.Add(item[0]);
                names.Add("@shortcut");
                names.Add("@shortcut:" + PayloadPlan.Version);
                // The previous product's RemoveShortcuts also creates an RBF.
                // Capture/retire that exact registered version before its MSI
                // removal runs, including when Desktop is on a secondary drive.
                string previousVersion = data["PreviousVersion"];
                if (!string.IsNullOrEmpty(previousVersion) && previousVersion != PayloadPlan.Version)
                {
                    if (!Regex.IsMatch(previousVersion, @"\A[0-9]+\.[0-9]+\.[0-9]+\z"))
                        throw new InvalidDataException("La versión MSI anterior no es válida.");
                    names.Add("@shortcut:" + previousVersion);
                }
                foreach (string name in names)
                {
                    string destination = Destination(data, name);
                    SavedFile file = new SavedFile();
                    file.Name = name;
                    file.Existed = File.Exists(destination);
                    if (file.Existed)
                    {
                        string backup = Child(data["Transaction"], "Files\\" + snapshot.Files.Count + ".bin");
                        Copy(destination, backup);
                        file.Backup = backup.Substring(Full(data["Root"]).Length + 1);
                        file.Hash = Hash(backup);
                    }
                    snapshot.Files.Add(file);
                }
                if (snapshot.FreshMod && File.Exists(data["Ini"]))
                {
                    snapshot.GameIni = data["Ini"];
                    string iniBackup = Child(data["Transaction"], "Bioshock.ini.before");
                    Copy(data["Ini"], iniBackup);
                    snapshot.IniBackup = iniBackup.Substring(Full(data["Root"]).Length + 1);
                    snapshot.IniHash = Hash(iniBackup);
                }
                SaveSnapshot(Child(data["Transaction"], "Snapshot.xml"), snapshot);
                if (snapshot.NewOriginal && data["Removing"] != "1") SaveSnapshot(original, snapshot);
                // The per-user MSI cannot secure rollback files in a protected
                // secondary-drive Config.Msi (1926/error 5). Retire only files
                // whose exact previous bytes have already been durably saved
                // and verified. The scheduled rollback action restores them;
                // Windows Installer rollback remains enabled for the new files,
                // shortcuts, registration and removal of the previous product.
                RetireSnapshottedFiles(data, snapshot);
                if (data["FailTest"] == "after-retire")
                    throw new IOException("Fallo aislado tras retirar archivos, antes de InstallFiles.");
                session.Log("Copia recuperable: " + data["Transaction"]);
                return ActionResult.Success;
            }
            catch (Exception ex) { return Fail(session, ex); }
        }

        private static void RetireSnapshottedFiles(CustomActionData data, Snapshot snapshot)
        {
            CheckSnapshot(data, snapshot);
            // Validate the entire bounded list before deleting its first file.
            foreach (SavedFile file in snapshot.Files)
            {
                string destination = Destination(data, file.Name);
                if (!file.Existed)
                {
                    if (File.Exists(destination)) throw new IOException("El archivo cambió durante la instalación: " + file.Name);
                    continue;
                }
                if (!File.Exists(destination) || Hash(destination) != file.Hash)
                    throw new IOException("El archivo cambió después de copiarlo: " + file.Name);
                if ((File.GetAttributes(destination) & (System.IO.FileAttributes.ReparsePoint | System.IO.FileAttributes.ReadOnly)) != 0)
                    throw new IOException("No se puede sustituir un enlace o archivo de solo lectura: " + file.Name);
            }
            foreach (SavedFile file in snapshot.Files)
                if (file.Existed) File.Delete(Destination(data, file.Name));
        }

        private static void RestoreSnapshot(CustomActionData data, Snapshot snapshot, bool rollback)
        {
            CheckSnapshot(data, snapshot);
            foreach (SavedFile file in snapshot.Files)
            {
                string destination = Destination(data, file.Name);
                if (file.Existed) Copy(Child(data["Root"], file.Backup), destination);
                else if (rollback && File.Exists(destination)) File.Delete(destination);
            }
            if (rollback && !string.IsNullOrEmpty(snapshot.IniBackup))
            {
                string backup = Child(data["Root"], snapshot.IniBackup);
                if (snapshot.GameIni != data["Ini"] || Hash(backup) != snapshot.IniHash)
                    throw new InvalidDataException("La copia de configuración no es válida.");
                Copy(backup, snapshot.GameIni);
            }
        }

        [CustomAction]
        public static ActionResult RollbackFiles(Session session)
        {
            try
            {
                CustomActionData data = session.CustomActionData;
                string snapshotPath = Child(data["Transaction"], "Snapshot.xml");
                if (!File.Exists(snapshotPath)) return ActionResult.Success;
                Snapshot snapshot = LoadSnapshot(snapshotPath);
                RestoreSnapshot(data, snapshot, true);
                string original = Child(data["Root"], "Original.xml");
                if (snapshot.NewOriginal && File.Exists(original)) File.Delete(original);
                session.Log("Estado anterior a la operación restaurado. Copia: " + data["Transaction"]);
                return ActionResult.Success;
            }
            catch (Exception ex) { return Fail(session, ex); }
        }

        internal static string ApplyGraphicsDefaults(string content)
        {
            Match section = Regex.Match(content, @"(?im)^\[Engine\.RenderConfig\][^\r\n]*(?:\r?\n|$)");
            if (!section.Success) return content;
            int start = section.Index + section.Length;
            Match next = Regex.Match(content.Substring(start), @"(?m)^\[");
            int length = next.Success ? next.Index : content.Length - start;
            string values = content.Substring(start, length);
            foreach (string key in GraphicsKeys)
            {
                Regex setting = new Regex(@"(?im)^(" + Regex.Escape(key) + @"\s*=\s*)[^\r\n]*");
                string replacement = key == "FluidSurfaceDetail" ? "High" :
                    key == "RealTimeReflection" || key == "UseRippleSystem" ? "False" : "True";
                int count = setting.Matches(values).Count;
                if (count > 1) throw new InvalidDataException("Hay una opción gráfica duplicada: " + key);
                if (count == 1) values = setting.Replace(values, delegate(Match m) { return m.Groups[1].Value + replacement; });
                else values = values.TrimEnd('\r', '\n') + Environment.NewLine + key + "=" + replacement + Environment.NewLine;
            }
            return content.Substring(0, start) + values + content.Substring(start + length);
        }

        [CustomAction]
        public static ActionResult FinishFiles(Session session)
        {
            try
            {
                CustomActionData data = session.CustomActionData;
                if (data["Removing"] == "1")
                {
                    string original = Child(data["Root"], "Original.xml");
                    // If Steam removed the game itself, keep recovery copies but do not recreate it.
                    if (File.Exists(original) && File.Exists(Child(data["Game"], "BioshockHD.exe")))
                        RestoreSnapshot(data, LoadSnapshot(original), false);
                }
                else
                {
                    foreach (string[] item in PayloadPlan.Items)
                    {
                        string file = Destination(data, item[0]);
                        if (!File.Exists(file) || Hash(file) != item[1])
                            throw new IOException("No se ha instalado correctamente " + item[0] + ". Se recuperará el estado anterior.");
                    }
                    Snapshot snapshot = LoadSnapshot(Child(data["Transaction"], "Snapshot.xml"));
                    if (snapshot.FreshMod && File.Exists(data["Ini"]))
                    {
                        string content;
                        Encoding encoding;
                        using (StreamReader reader = new StreamReader(data["Ini"], Encoding.Default, true))
                        {
                            content = reader.ReadToEnd();
                            encoding = reader.CurrentEncoding;
                        }
                        string updated = ApplyGraphicsDefaults(content);
                        if (updated != content)
                        {
                            string temporary = data["Ini"] + ".msi-" + Guid.NewGuid().ToString("N");
                            File.WriteAllText(temporary, updated, encoding);
                            File.Replace(temporary, data["Ini"], null);
                        }
                    }
                }
                if (data["FailTest"] == "1") throw new IOException("Fallo controlado de la prueba aislada de rollback MSI.");
                return ActionResult.Success;
            }
            catch (Exception ex) { return Fail(session, ex); }
        }

        [CustomAction]
        public static ActionResult CommitFiles(Session session)
        {
            try
            {
                CustomActionData data = session.CustomActionData;
                string original = Child(data["Root"], "Original.xml");
                if (data["Removing"] == "1" && File.Exists(original))
                {
                    string archived = Child(data["Transaction"], "Original-before-uninstall.xml");
                    File.Move(original, archived);
                }
                // Recovery snapshots are intentionally retained. User preferences remain in place.
                session.Log("Operación completada. Copias conservadas en " + data["Root"]);
                return ActionResult.Success;
            }
            catch (Exception ex) { return Fail(session, ex); }
        }
    }
}
