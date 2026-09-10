using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WixToolset.Dtf.WindowsInstaller;

namespace BioShockMsi
{
    public static partial class Actions
    {
        // Exact inventory emitted by the BS2 0.1.0/0.1.1 Format=3 installers.
        // Old documentation paths differ from the new package; do not infer
        // ownership from a directory prefix or enumerate the game directory.
        internal static readonly string[] LegacyNames = {
            "xinput1_3.dll", "bioshockvr.dll", "bvr_steamvr32.dll", "openvr_api.dll",
            @"host64\BioShockVR-DLSS45-Host64.exe", @"host64\nvngx_dlss.dll",
            @"host64\dlss-capabilities.ini", "Lanzador BioShock 2 VR DLSS-DLAA.exe",
            @"BioShock2VR-DLSS45\LEEME-DLSS45.md",
            @"BioShock2VR-DLSS45\NVIDIA-DLSS-LICENSE.txt",
            @"BioShock2VR-DLSS45\INFORMACION-DEL-PAQUETE.txt",
            @"BioShock2VR-DLSS45\dlss.ini.example",
            @"BioShock2VR-DLSS45\Licenses\BioShockVR-MIT-LICENSE.txt",
            @"BioShock2VR-DLSS45\Licenses\DLSS-Host-MIT-LICENSE.txt",
            @"BioShock2VR-DLSS45\Licenses\DLSS-Bridge-MIT-LICENSE.txt",
            @"BioShock2VR-DLSS45\Licenses\THIRD_PARTY_NOTICES.md",
            @"BioShock2VR-DLSS45\Licenses\MinHook-LICENSE.txt",
            @"BioShock2VR-DLSS45\Licenses\Dear-ImGui-LICENSE.txt",
            @"BioShock2VR-DLSS45\Licenses\OpenVR-LICENSE.txt",
            @"BioShock2VR-DLSS45\Licenses\OpenXR-LICENSE.txt",
            @"BioShock2VR-DLSS45\Licenses\OpenXR-COPYING.adoc"
        };
        sealed class LegacyRecord
        {
            internal string Name, Backup, OriginalHash;
            internal bool Existed, Restored;
        }
        sealed class LegacyInstallation
        {
            internal string ManifestHash;
            internal Dictionary<string, LegacyRecord> Records = new Dictionary<string, LegacyRecord>(StringComparer.OrdinalIgnoreCase);
            internal LegacyRecord Shortcut;
        }
        static bool IsLegacyName(string name)
        {
            return GamePackage.Id == "bs2" && Array.Exists(LegacyNames,
                delegate(string candidate) { return string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase); });
        }
        static string DecodeLegacy(string encoded)
        {
            return new UTF8Encoding(false,true).GetString(Convert.FromBase64String(encoded));
        }
        static bool LegacySwitch(string value)
        {
            if(value!="0" && value!="1") throw new InvalidDataException("Indicador beta no válido.");
            return value=="1";
        }
        static string RequireHash(string value)
        {
            if (!Regex.IsMatch(value ?? "", @"\A[0-9A-Fa-f]{64}\z"))
                throw new InvalidDataException("SHA-256 beta no válido.");
            return value.ToUpperInvariant();
        }
        static string LegacyRelative(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.IndexOf(':')>=0 ||
                name.IndexOf('/')>=0 || Array.Exists(name.Split('\\'), delegate(string part) {
                    return part=="." || part==".." || part.Length==0 || part.Trim()!=part;
                }))
                throw new InvalidDataException("Ruta relativa beta no válida.");
            return name;
        }
        static void NoLegacyLinks(string path, string anchor)
        {
            string current=Full(path), root=Full(anchor);
            if(current!=root && !current.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ruta fuera de las copias beta.");
            for(;;) {
                if((File.Exists(current)||Directory.Exists(current)) &&
                    (File.GetAttributes(current)&System.IO.FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("No se migran copias beta a través de enlaces.");
                if(string.Equals(current,root,StringComparison.OrdinalIgnoreCase)) break;
                current=Path.GetDirectoryName(current);
                if(string.IsNullOrEmpty(current)) throw new InvalidDataException("Ancla de copia beta no válida.");
            }
        }
        static LegacyInstallation ReadLegacy(CustomActionData data)
        {
            if(GamePackage.Id!="bs2" || !File.Exists(data["Legacy"])) return null;
            string path=data["Legacy"], root=Path.GetDirectoryName(path);
            NoLegacyLinks(path,root);
            if(new FileInfo(path).Length>131072) throw new InvalidDataException("Manifiesto beta demasiado grande.");
            var fields=new Dictionary<string,string>(StringComparer.Ordinal);
            var rows=new List<string[]>(); var directories=new List<string>();
            string[] allowed={"Format","GameId","Version","GameDirectory","BackupDirectory","ShortcutPath",
                "ShortcutExisted","ShortcutBackupPath","ShortcutManaged","ShortcutInstalledSha256","ShortcutOriginalSha256","ShortcutRestored"};
            string initialHash=Hash(path);
            foreach(string line in File.ReadAllLines(path,new UTF8Encoding(false,true))) {
                if(line.Length==0) continue;
                if(line.StartsWith("File|",StringComparison.Ordinal)) {
                    string[] parts=line.Split('|');
                    if(parts.Length!=7) throw new InvalidDataException("Registro beta incompleto.");
                    rows.Add(parts); continue;
                }
                if(line.StartsWith("CreatedDirectory=",StringComparison.Ordinal)) {
                    directories.Add(LegacyRelative(DecodeLegacy(line.Substring(17)))); continue;
                }
                int at=line.IndexOf('=');
                if(at<=0) throw new InvalidDataException("Campo beta no válido.");
                string key=line.Substring(0,at);
                if(Array.IndexOf(allowed,key)<0 || fields.ContainsKey(key))
                    throw new InvalidDataException("Campo beta desconocido o duplicado: "+key);
                fields.Add(key,line.Substring(at+1));
            }
            foreach(string key in allowed) if(!fields.ContainsKey(key)) throw new InvalidDataException("Falta el campo beta "+key);
            if(fields["Format"]!="3" || fields["GameId"]!="bs2" ||
                (fields["Version"]!="0.1.0-beta" && fields["Version"]!="0.1.1-beta"))
                throw new InvalidDataException("La versión beta no admite migración automática.");
            if(!string.Equals(Full(DecodeLegacy(fields["GameDirectory"])),Full(data["Game"]),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La copia beta pertenece a otro juego o a otra carpeta.");
            string backupRoot=Full(DecodeLegacy(fields["BackupDirectory"]));
            if(!backupRoot.StartsWith(Full(root)+"\\",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La carpeta de copias beta está fuera del perfil.");
            NoLegacyLinks(backupRoot,root);
            if(rows.Count!=LegacyNames.Length) throw new InvalidDataException("Inventario beta incompleto.");
            var result=new LegacyInstallation(); result.ManifestHash=initialHash;
            foreach(string[] row in rows) {
                string name=LegacyRelative(DecodeLegacy(row[1]));
                if(!IsLegacyName(name) || result.Records.ContainsKey(name)) throw new InvalidDataException("Archivo beta ajeno o duplicado: "+name);
                var record=new LegacyRecord { Name=name, Existed=LegacySwitch(row[2]), Restored=LegacySwitch(row[6]) };
                RequireHash(row[3]); // current bytes may be a legitimate launcher hotfix
                record.Backup=Child(backupRoot,LegacyRelative(DecodeLegacy(row[4])));
                if(record.Existed) {
                    record.OriginalHash=RequireHash(row[5]); NoLegacyLinks(record.Backup,root);
                    if(!File.Exists(record.Backup) || Hash(record.Backup)!=record.OriginalHash)
                        throw new IOException("Falta la copia original verificada de "+name);
                } else if(row[5].Length!=0) throw new InvalidDataException("Original inexistente con hash atribuido.");
                if(record.Restored) {
                    string live=Child(data["Game"],name);
                    if(File.Exists(live)!=record.Existed || (record.Existed && Hash(live)!=record.OriginalHash))
                        throw new IOException("El archivo restaurado por la beta cambió después: "+name);
                }
                result.Records.Add(name,record);
            }
            foreach(string dir in directories) {
                if(!Array.Exists(LegacyNames,delegate(string name) { return name.StartsWith(dir+"\\",StringComparison.OrdinalIgnoreCase); }))
                    throw new InvalidDataException("Directorio beta ajeno al inventario.");
            }
            bool managed=LegacySwitch(fields["ShortcutManaged"]);
            bool existed=LegacySwitch(fields["ShortcutExisted"]), restored=LegacySwitch(fields["ShortcutRestored"]);
            if(managed) {
                string shortcutPath=Full(DecodeLegacy(fields["ShortcutPath"]));
                string token=string.Equals(shortcutPath,Full(data["Shortcut"]),StringComparison.OrdinalIgnoreCase) ? "@shortcut" :
                    string.Equals(shortcutPath,Destination(data,"@beta-shortcut"),StringComparison.OrdinalIgnoreCase) ? "@beta-shortcut" : null;
                if(token==null) throw new InvalidDataException("El acceso de la beta no coincide con uno de sus nombres previstos en este escritorio.");
                NoLegacyLinks(shortcutPath,data["Desktop"]);
                if(fields["ShortcutInstalledSha256"].Length!=0) RequireHash(fields["ShortcutInstalledSha256"]);
                var shortcut=new LegacyRecord { Name=token, Existed=existed, Restored=restored };
                if(existed) {
                    shortcut.Backup=Full(DecodeLegacy(fields["ShortcutBackupPath"])); NoLegacyLinks(shortcut.Backup,backupRoot);
                    shortcut.OriginalHash=RequireHash(fields["ShortcutOriginalSha256"]);
                    if(!File.Exists(shortcut.Backup)||Hash(shortcut.Backup)!=shortcut.OriginalHash)
                        throw new IOException("Falta la copia del acceso anterior a la beta.");
                }
                result.Shortcut=shortcut;
                if(restored && (File.Exists(shortcutPath)!=existed ||
                    (existed && Hash(shortcutPath)!=shortcut.OriginalHash)))
                    throw new IOException("El acceso restaurado por la beta cambió después.");
            }
            if(Hash(path)!=initialHash) throw new IOException("El manifiesto beta cambió durante su lectura.");
            return result;
        }
        static Snapshot ImportLegacyOriginal(CustomActionData data, Snapshot current, LegacyInstallation legacy)
        {
            // The rollback snapshot remains an exact copy of the state immediately
            // before MSI. Only the permanent uninstall baseline adopts pre-beta bytes.
            var original=new Snapshot { Game=current.Game,GameId=current.GameId,NewOriginal=true,FreshMod=false };
            foreach(SavedFile before in current.Files) {
                if(before.Name=="@legacy-manifest") continue;
                LegacyRecord record;
                if(legacy.Shortcut!=null && before.Name==legacy.Shortcut.Name) record=legacy.Shortcut;
                else legacy.Records.TryGetValue(before.Name,out record);
                if(record==null) { original.Files.Add(before); continue; }
                var saved=new SavedFile { Name=before.Name,Existed=record.Existed,Hash=record.OriginalHash };
                if(record.Existed) {
                    string destination=Child(data["Transaction"],"LegacyOriginals\\"+original.Files.Count+".bin");
                    Copy(record.Backup,destination);
                    if(Hash(destination)!=record.OriginalHash) throw new IOException("No se ha verificado el original beta importado.");
                    saved.Backup=destination.Substring(Full(data["Root"]).Length+1);
                }
                original.Files.Add(saved);
            }
            CheckSnapshot(data,original);
            return original;
        }
    }
}
