using System;
using System.IO;
using System.Linq;
using WixToolset.Dtf.WindowsInstaller;

namespace BioShockMsi
{
    public static partial class Actions
    {
        public static int SnapshotTestMain()
        {
            string root=Path.Combine(Path.GetTempPath(),"BvrSnapshot-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var data=new CustomActionData();data["Root"]=root;data["Game"]=Path.Combine(root,"Game");
            data["Desktop"]=Path.Combine(root,"Desktop");data["Shortcut"]=Path.Combine(data["Desktop"],ShortcutName);
            var snapshot=new Snapshot { Game=data["Game"] };
            foreach(string[] item in PayloadPlan.Items) snapshot.Files.Add(new SavedFile {Name=item[0].StartsWith("host64\\") ? item[0].Replace('\\','/') : item[0],Existed=false});
            string path=Path.Combine(root,"Original.xml");SaveSnapshot(path,snapshot);
            string before=Hash(path);
            var loaded=LoadSnapshot(path);CheckSnapshot(data,loaded);
            if(Hash(path)!=before || loaded.Files.Any(f=>f.Name.Contains("/"))) throw new Exception("Reading must normalize in memory only.");
            loaded.Files.Add(new SavedFile {Name="host64/nvngx_dlss.dll",Existed=false});
            bool duplicates=false;try{CheckSnapshot(data,loaded);}catch(InvalidDataException){duplicates=true;}
            if(!duplicates) throw new Exception("Mixed-separator duplicates must be refused.");
            foreach(string bad in new[]{"../outside.dll","host64/../../outside.dll","C:/outside.dll","host64/foreign.dll"})
            {
                bool refused=false;try{Destination(data,bad);}catch(InvalidDataException){refused=true;}
                if(!refused) throw new Exception("Foreign destination accepted: "+bad);
            }
            Console.WriteLine("PASS: historical BS1 separators, original file unchanged, duplicate/traversal/foreign-file rejection.");
            return 0;
        }
    }
    internal static class SnapshotTestProgram
    {
        private static int Main() { try{return Actions.SnapshotTestMain();}catch(Exception ex){Console.Error.WriteLine(ex);return 1;} }
    }
}
