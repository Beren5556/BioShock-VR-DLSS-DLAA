using System;
using System.IO;
using WixToolset.Dtf.WindowsInstaller;

// Isolated installer integration harness. Runs ONLY costing and immediate
// planning/validation actions; never InstallInitialize/Execute/Finalize.
internal static class SelectionTest
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static int Main(string[] args)
    {
        try
        {
            Require(args.Length == 3 && Path.GetFileName(args[1]).StartsWith("BvrMsiTest-"),
                "Use an isolated MSI, fixture and test mode.");
            Installer.SetInternalUI(InstallUIOptions.Silent);
            using (Session session = Installer.OpenPackage(args[0], false))
            {
                session["BVR_TESTROOT"] = args[1];
                session["BVR_BS1_GAMEPATH"] = Path.Combine(args[1], @"bs1\Game\Build\Final");
                session["BVR_BS2_GAMEPATH"] = Path.Combine(args[1], @"bs2\Game\Build\Final");
                foreach (string action in new[] { "CostInitialize", "FileCost", "FindRelatedProducts",
                    "DetectSuite", "DetectGame_bs1", "DetectGame_bs2", "CostFinalize" })
                {
                    // Windows skips FindRelatedProducts in maintenance; calling
                    // it manually then returns ERROR_FUNCTION_NOT_CALLED (1626).
                    if (action == "FindRelatedProducts" && session["Installed"].Length > 0) continue;
                    session.DoAction(action);
                }
                string mode = args[2];
                if (mode == "fresh")
                {
                    session["BVR_BS1_SELECTED"] = ""; session["BVR_BS2_SELECTED"] = "";
                    session.DoAction("ApplySelection");
                    Require(session["BVR_VALID"] == "0", "Empty fresh selection must be refused.");
                    session["BVR_BS2_SELECTED"] = "1";
                    session.DoAction("ApplySelection");
                    Require(session["ADDLOCAL"].Contains("Game_bs2") && !session["ADDLOCAL"].Contains("Game_bs1"),
                        "UI must be able to install BS2 alone.");
                }
                else if (mode == "add")
                {
                    Require(session["BVR_BS1_SELECTED"] == "1" && session["BVR_BS2_SELECTED"] != "1", "Detect existing BS1.");
                    session["BVR_BS2_SELECTED"] = "1"; session.DoAction("ApplySelection");
                    Require(session["ADDLOCAL"].Contains("Game_bs2") && !session["ADDLOCAL"].Contains("Game_bs1") &&
                        session["REINSTALL"] == "" && session["REMOVE"] == "", "Adding BS2 must leave BS1 untouched.");
                }
                else if (mode == "repair")
                {
                    Require(session["BVR_BS1_SELECTED"] == "1" && session["BVR_BS2_SELECTED"] == "1", "Detect both mods.");
                    session["BVR_BS2_REPAIR"] = "1"; session.DoAction("ApplySelection");
                    Require(session["REINSTALL"].Contains("Game_bs2") && !session["REINSTALL"].Contains("Game_bs1"), "Repair only BS2.");
                }
                else if (mode == "remove")
                {
                    session["BVR_BS1_SELECTED"] = ""; session.DoAction("ApplySelection");
                    Require(session["REMOVE"].Contains("Game_bs1") && !session["REMOVE"].Contains("Game_bs2"), "Remove only BS1.");
                    session["BVR_BS2_SELECTED"] = ""; session.DoAction("ApplySelection");
                    Require(session["REMOVE"] == "ALL", "Removing the last mod must remove the product.");
                }
                else throw new InvalidOperationException("Unknown test mode.");
                Require(session["BVR_VALID"] == "1", "UI plan must be valid: " + session["BVR_ERROR"]);
                Console.WriteLine("PASS native MSI selection plan: " + mode);
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
