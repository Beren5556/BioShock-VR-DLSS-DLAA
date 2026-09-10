using System;
using System.IO;
using WixToolset.Dtf.WindowsInstaller;

internal static class SelectorTest
{
    private static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    private static int Main(string[] args)
    {
        try
        {
            Require(args.Length>=2 && Path.GetFileName(args[1]).StartsWith("BvrMsiTest-"),"Only isolated fixtures.");
            Installer.SetInternalUI(InstallUIOptions.Silent);
            using(Session session=Installer.OpenPackage(args[0],false))
            {
                session["BVR_TESTROOT"]=args[1];session["BVR_TESTDISPATCHONLY"]="1";
                Require(session["BVR_INSTANCE"]=="selector","Default MSI must open the selector, not a game.");
                session["BVR_GAME"]="choose";session.DoAction("OpenSelectedGame");
                Require(session["BVR_VALID"]=="0","Empty dropdown must not launch anything.");
                foreach(string id in new[]{"bs1","bs2"})
                {
                    session["BVR_GAME"]=id;session.DoAction("OpenSelectedGame");
                    Require(session["BVR_VALID"]=="1","Valid game selection.");
                    string command=session["BVR_DISPATCH_ARGS"];
                    Require(command.Contains("TRANSFORMS=:"+id) || command.Contains(" /n {"),"Native instance must be targeted.");
                    Require(!command.Contains("ADDLOCAL=ALL") && !command.Contains("REMOVE=") && !command.Contains("REINSTALL="),"Selector opens the wizard, never applies changes.");
                }
                Console.WriteLine("PASS: dropdown, game-scoped native dispatch, no installation from selector.");
            }
            for(int index=2;index<args.Length;index++)
            {
                string[] target=args[index].Split('|');
                Require(target.Length==2&&(target[0]=="bs1"||target[0]=="bs2"),"Explicit isolated game instance.");
                string transform=Path.Combine(args[1],"Plan-"+target[0]+".mst");
                string transformed=Path.Combine(args[1],"Plan-"+target[0]+".msi");
                // OpenProduct exposes the cached base database without the
                // instance transform. Apply that SAME embedded transform to a
                // private test copy before testing the wizard's immediate plan.
                using(Database database=new Database(args[0],DatabaseOpenMode.ReadOnly))
                using(View view=database.OpenView("SELECT `Name`, `Data` FROM `_Storages` WHERE `Name` = ?"))
                using(Record parameter=new Record(1))
                {
                    parameter.SetString(1,target[0]);view.Execute(parameter);
                    using(Record row=view.Fetch()){Require(row!=null,"Embedded instance transform.");row.GetStream(2,transform);}
                }
                File.Copy(args[0],transformed,true);
                using(Database database=new Database(transformed,DatabaseOpenMode.Transact)){database.ApplyTransform(transform,TransformErrors.None);database.Commit();}
                using(Session session=Installer.OpenPackage(transformed,false))
                {
                    string id=session["BVR_INSTANCE"], other=id=="bs1"?"bs2":"bs1";
                    Require(id==target[0]&&session["ProductCode"]==target[1],"Registered native instance.");
                    session["BVR_TESTROOT"]=args[1];
                    foreach(string action in new[]{"CostInitialize","FileCost","DetectSuite","DetectGame_"+id,"CostFinalize"})session.DoAction(action);
                    session["BVR_OPERATION"]="install";session.DoAction("PlanSingleGame");
                    Require(session["BVR_VALID"]=="1",session["BVR_ERROR"]);
                    Require(session["REINSTALL"].Contains("Game_"+id)&&!session["REINSTALL"].Contains(other),"Maintenance only repairs selected game.");
                    session.DoAction("PrepareSuite");
                    Require(session["BVR_"+id.ToUpperInvariant()+"_ACTIVE"]=="1"&&session["BVR_"+other.ToUpperInvariant()+"_ACTIVE"]=="","Other game cannot be active.");
                    session["BVR_OPERATION"]="remove";session.DoAction("PlanSingleGame");
                    Require(session["BVR_VALID"]=="1"&&session["REMOVE"]=="ALL"&&session["REINSTALL"]=="","Remove only this native instance.");
                    Require(session.Features["Game_"+other].CurrentState!=InstallState.Local&&session.Features["Game_"+other].RequestState!=InstallState.Local,"Other instance untouched by UI plan.");
                    Console.WriteLine("PASS: native maintenance UI plan for "+id);
                }
            }
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
