using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WixToolset.Dtf.WindowsInstaller;

namespace BioShockMsi
{
    public static partial class Actions
    {
        static int migrationChecks, migrationFailures;
        static void ExpectMigration(bool okay,string name) {
            ++migrationChecks; if(!okay) ++migrationFailures;
            Console.WriteLine((okay?"PASS: ":"FAIL: ")+name);
        }
        static string EncodeTest(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); }
        static CustomActionData MigrationFixture(string root, out string manifest) {
            var data=new CustomActionData();
            data["Game"]=Path.Combine(root,TestGameRelative());
            data["Root"]=Path.Combine(root,@"Local\BioshockVR\bs2\WindowsInstaller");
            data["Transaction"]=Path.Combine(data["Root"],@"Transactions\Fixture");
            data["Legacy"]=Path.Combine(root,@"Local\BioshockVR\bs2\Installer-DLSS-DLAA\install.manifest");
            data["Desktop"]=Path.Combine(root,"Desktop");
            data["Shortcut"]=Path.Combine(data["Desktop"],ShortcutName);
            string originals=Path.Combine(Path.GetDirectoryName(data["Legacy"]),"Original-fixture");
            Directory.CreateDirectory(originals);Directory.CreateDirectory(data["Game"]);Directory.CreateDirectory(data["Desktop"]);
            var text=new StringBuilder();
            text.AppendLine("Format=3");text.AppendLine("GameId=bs2");text.AppendLine("Version=0.1.1-beta");
            text.AppendLine("GameDirectory="+EncodeTest(data["Game"]));
            text.AppendLine("BackupDirectory="+EncodeTest(originals));
            text.AppendLine("ShortcutPath="+EncodeTest(data["Shortcut"]));
            text.AppendLine("ShortcutExisted=1");text.AppendLine("ShortcutManaged=1");text.AppendLine("ShortcutRestored=0");
            string shortcutBackup=Path.Combine(originals,"desktop-shortcut.original.lnk");
            File.WriteAllText(shortcutBackup,"BEFORE BETA shortcut");
            File.WriteAllText(data["Shortcut"],"BETA shortcut");
            text.AppendLine("ShortcutBackupPath="+EncodeTest(shortcutBackup));
            text.AppendLine("ShortcutOriginalSha256="+Hash(shortcutBackup));
            text.AppendLine("ShortcutInstalledSha256="+Hash(data["Shortcut"]));
            foreach(string name in LegacyNames) {
                string backup=Child(originals,name+".original"),live=Child(data["Game"],name);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));Directory.CreateDirectory(Path.GetDirectoryName(live));
                File.WriteAllText(backup,"BEFORE BETA "+name);File.WriteAllText(live,"BETA "+name);
                text.AppendLine("File|"+EncodeTest(name)+"|1|"+Hash(live)+"|"+EncodeTest(name+".original")+"|"+Hash(backup)+"|0");
            }
            text.AppendLine("CreatedDirectory="+EncodeTest("host64"));
            manifest=text.ToString(); File.WriteAllText(data["Legacy"],manifest,new UTF8Encoding(false));
            return data;
        }
        public static string CreateMsiMigrationFixture(string root) {
            if(!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(root.TrimEnd('\\')),
                @"\ABvrMsiTest-[a-f0-9]{32}\z"))
                throw new InvalidDataException("Solo se pueden crear fixtures MSI aislados.");
            string text;return MigrationFixture(root,out text)["Legacy"];
        }
        public static string CreateNamedBetaMsiFixture(string root, bool existed) {
            string path=CreateMsiMigrationFixture(root);
            string desktop=Path.Combine(root,"Desktop");
            string oldPath=Path.Combine(desktop,ShortcutName), betaPath=Path.Combine(desktop,"BioShock 2 VR DLSS-DLAA Beta.lnk");
            File.Move(oldPath,betaPath);
            string text=File.ReadAllText(path).Replace("ShortcutPath="+EncodeTest(oldPath),"ShortcutPath="+EncodeTest(betaPath));
            if(!existed) text=System.Text.RegularExpressions.Regex.Replace(text,
                @"(?m)^(ShortcutExisted|ShortcutBackupPath|ShortcutOriginalSha256)=[^\r\n]*",delegate(System.Text.RegularExpressions.Match m) {
                    return m.Groups[1].Value+"="+(m.Groups[1].Value=="ShortcutExisted" ? "0" : "");
                });
            File.WriteAllText(path,text,new UTF8Encoding(false));
            return path;
        }
        // Read-only diagnostic: never creates a fixture or edits user data.
        public static string InspectExistingBeta(string game, string manifest, string desktop) {
            var data=new CustomActionData();data["Game"]=game;data["Legacy"]=manifest;
            data["Desktop"]=desktop;data["Shortcut"]=Path.Combine(desktop,ShortcutName);
            string before=Hash(manifest);var legacy=ReadLegacy(data);
            if(Hash(manifest)!=before) throw new IOException("Manifest changed during inspection.");
            return legacy==null ? "No legacy installation" : "Validated "+legacy.Records.Count+" beta records; shortcut="+
                (legacy.Shortcut==null ? "unmanaged" : legacy.Shortcut.Name)+"; original manifest unchanged";
        }
        public static int MigrationTestMain() {
            string root=Path.Combine(Path.GetTempPath(),"bvr-migration-test-"+Guid.NewGuid().ToString("N"));
            string manifest; var data=MigrationFixture(root,out manifest);
            try {
                ExpectMigration(ReadLegacy(data).Records.Count==21,"exact Format=3 inventory");
                Action<string,string> rejects=delegate(string changed,string name) {
                    File.WriteAllText(data["Legacy"],changed,new UTF8Encoding(false));bool refused=false;
                    try { ReadLegacy(data); } catch(Exception) { refused=true; }
                    ExpectMigration(refused,name);File.WriteAllText(data["Legacy"],manifest,new UTF8Encoding(false));
                };
                File.WriteAllText(data["Legacy"],manifest.Replace("Version=0.1.1-beta","Version=0.1.0-beta"));
                ExpectMigration(ReadLegacy(data)!=null,"0.1.0 beta is supported");
                File.WriteAllText(data["Legacy"],manifest);
                rejects(manifest.Replace("GameId=bs2","GameId=bs1"),"never import BS1");
                rejects(manifest.Replace("Format=3","Format=2"),"reject older schema");
                rejects(manifest.Replace("Version=0.1.1-beta","Version=9.9.9"),"reject future unknown version");
                rejects(manifest+"GameId=bs2\n","duplicate metadata");
                rejects(manifest+"Unknown=anything\n","unknown metadata");
                rejects(manifest.Replace("GameDirectory="+EncodeTest(data["Game"]),"GameDirectory="+EncodeTest(root)),"wrong game directory");
                string backupRoot=Path.Combine(Path.GetDirectoryName(data["Legacy"]),"Original-fixture");
                rejects(manifest.Replace("BackupDirectory="+EncodeTest(backupRoot),"BackupDirectory="+EncodeTest(root)),"backup outside legacy root");
                string[] lines=manifest.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
                string first=Array.Find(lines,delegate(string line) {return line.StartsWith("File|");});
                rejects(manifest.Replace(first+Environment.NewLine,""),"missing original inventory record");
                rejects(manifest+first+"\n","duplicate inventory");
                rejects(manifest.Replace(first,first.Replace(EncodeTest("xinput1_3.dll"),EncodeTest("Bioshock2HD.exe"))),"game executable outside owned inventory");
                rejects(manifest.Replace(first,first.Replace(EncodeTest("xinput1_3.dll"),EncodeTest("..\\outside.dll"))),"parent traversal");
                rejects(manifest.Replace(first,first.Replace(EncodeTest("xinput1_3.dll"),EncodeTest("xinput1_3.dll:stream"))),"alternate data stream");
                string[] parts=first.Split('|');
                rejects(manifest.Replace(first,first.Replace("|"+parts[3]+"|","|bad-hash|")),"invalid installed hash");
                rejects(manifest.Replace(first,first.Replace("|"+parts[5]+"|","|"+new string('0',64)+"|")),"wrong original bytes");
                rejects(manifest.Replace(first,first.Replace("|1|","|2|")),"invalid existence flag");
                rejects(manifest.Replace(first,first.Replace("|1|","|0|")),"nonexistent original cannot carry a hash");
                rejects(manifest+"CreatedDirectory="+EncodeTest("SaveGames")+"\n","never own saves");
                rejects(manifest.Replace("ShortcutPath="+EncodeTest(data["Shortcut"]),"ShortcutPath="+EncodeTest(Path.Combine(root,"foreign.lnk"))),"foreign shortcut");
                rejects(manifest.Replace("ShortcutPath="+EncodeTest(data["Shortcut"]),"ShortcutPath="+EncodeTest(Path.Combine(data["Desktop"],"foreign.lnk"))),"unknown shortcut name on same desktop");
                rejects(manifest.Replace("ShortcutPath="+EncodeTest(data["Shortcut"]),"ShortcutPath="+EncodeTest(Path.Combine(root,"BioShock 2 VR DLSS-DLAA Beta.lnk"))),"known beta name outside desktop");
                rejects(manifest.Replace("ShortcutRestored=0","ShortcutRestored=1"),"edited shortcut after partial restore");
                rejects(manifest.Replace(first,first.Substring(0,first.Length-1)+"1"),"edited file after partial restore");
                string xinputBackup=Path.Combine(backupRoot,"xinput1_3.dll.original");
                File.Move(xinputBackup,xinputBackup+".saved");
                rejects(manifest,"missing original backup");
                File.Move(xinputBackup+".saved",xinputBackup);
                // Current hotfix bytes belong in rollback, never in the pre-beta baseline.
                File.WriteAllText(Child(data["Game"],GamePackage.LauncherName),"LATER LAUNCHER HOTFIX");
                var legacy=ReadLegacy(data);
                ExpectMigration(legacy!=null,"legitimate later launcher is accepted and backed up");
                var snapshot=new Snapshot {Game=data["Game"],GameId="bs2",NewOriginal=true};
                foreach(string name in LegacyNames) {
                    string destination=Child(data["Transaction"],"Files\\"+snapshot.Files.Count+".bin");
                    Copy(Child(data["Game"],name),destination);
                    snapshot.Files.Add(new SavedFile{Name=name,Existed=true,Hash=Hash(destination),
                        Backup=destination.Substring(Full(data["Root"]).Length+1)});
                }
                snapshot.Files.Add(new SavedFile{Name="@shortcut",Existed=true});
                snapshot.Files.Add(new SavedFile{Name="@legacy-manifest",Existed=true});
                string rollbackHash=snapshot.Files[7].Hash;
                var original=ImportLegacyOriginal(data,snapshot,legacy);
                ExpectMigration(original.Files.Count==22,"import all 21 originals and shortcut, not active beta manifest");
                ExpectMigration(snapshot.Files[7].Hash==rollbackHash,"rollback snapshot untouched");
                ExpectMigration(original.Files[7].Hash!=rollbackHash &&
                    File.ReadAllText(Child(data["Root"],original.Files[7].Backup))=="BEFORE BETA "+GamePackage.LauncherName,"uninstall baseline is pre-beta, not hotfixed launcher");
                ExpectMigration(File.Exists(data["Legacy"]),"dry import never retires beta manifest");
                var wrong=new Snapshot {Game=data["Game"],GameId="bs1"};
                bool wrongRejected=false;try{CheckSnapshot(data,wrong);}catch(InvalidDataException){wrongRejected=true;}
                ExpectMigration(wrongRejected,"MSI snapshots remain game-bound");
                foreach(bool existed in new[]{true,false}) {
                    string namedRoot=Path.Combine(root,existed ? "original-shortcut" : "new-shortcut");
                    string namedManifest;var namedData=MigrationFixture(namedRoot,out namedManifest);
                    string beta=Destination(namedData,"@beta-shortcut");
                    File.Move(namedData["Shortcut"],beta);
                    namedManifest=namedManifest.Replace("ShortcutPath="+EncodeTest(namedData["Shortcut"]),"ShortcutPath="+EncodeTest(beta));
                    if(!existed) namedManifest=System.Text.RegularExpressions.Regex.Replace(namedManifest,
                        @"(?m)^(ShortcutExisted|ShortcutBackupPath|ShortcutOriginalSha256)=[^\r\n]*",delegate(System.Text.RegularExpressions.Match m) {
                            return m.Groups[1].Value+"="+(m.Groups[1].Value=="ShortcutExisted" ? "0" : "");
                        });
                    File.WriteAllText(namedData["Legacy"],namedManifest,new UTF8Encoding(false));
                    string initial=Hash(namedData["Legacy"]);var named=ReadLegacy(namedData);
                    ExpectMigration(named.Shortcut.Name=="@beta-shortcut" && named.Shortcut.Existed==existed,"actual Beta shortcut identity, existed="+existed);
                    ExpectMigration(Hash(namedData["Legacy"])==initial,"named beta validation never edits manifest");
                    var current=new Snapshot{Game=namedData["Game"],GameId="bs2"};
                    current.Files.Add(new SavedFile{Name="@shortcut",Existed=false});
                    current.Files.Add(new SavedFile{Name="@beta-shortcut",Existed=true});
                    var baseline=ImportLegacyOriginal(namedData,current,named);
                    ExpectMigration(baseline.Files[0].Name=="@shortcut"&&!baseline.Files[0].Existed,"normal shortcut not confused with beta shortcut");
                    ExpectMigration(baseline.Files[1].Name=="@beta-shortcut"&&baseline.Files[1].Existed==existed,"beta uninstall baseline keeps its own destination");
                    if(existed) ExpectMigration(File.ReadAllText(Child(namedData["Root"],baseline.Files[1].Backup))=="BEFORE BETA shortcut","pre-beta shortcut bytes retained");
                }
                Console.WriteLine("Migration: "+migrationChecks+" checks, "+migrationFailures+" failures. Fixture: "+root);
                return migrationFailures==0?0:1;
            }catch(Exception ex){Console.WriteLine(ex);return 2;}
        }
    }
    internal static class MigrationTestsProgram {
        static int Main() { return Actions.MigrationTestMain(); }
    }
}
