using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace BioShockSuite
{
    public static class Actions
    {
        private static readonly string[] Games = { "bs1", "bs2" };
        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern InstallState MsiQueryFeatureStateW(string product, string feature);
        private static string P(string id, string suffix) { return "BVR_" + id.ToUpperInvariant() + "_" + suffix; }
        private static bool Has(string list, string name)
        {
            foreach (string item in list.Split(','))
                if (item == "ALL" || item == name) return true;
            return false;
        }
        private static string Saved(string id, string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(id == "bs1" ? SuitePlan.Bs1Registry : SuitePlan.Bs2Registry))
                return key == null ? "" : Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture);
        }
        private static bool Local(FeatureInfo feature) { return feature.CurrentState == InstallState.Local; }
        private static ActionResult Error(Session session, Exception ex)
        {
            session.Log("BioShock 1-2 MSI: " + ex);
            using (Record record = new Record(1))
            {
                record.FormatString = "[1]";
                record[1] = ex.Message;
                session.Message(InstallMessage.Error, record);
            }
            return ActionResult.Failure;
        }

        [CustomAction]
        public static ActionResult DetectSuite(Session session)
        {
            try
            {
                if (SuitePlan.TestFamily.Length > 0)
                {
                    string savedRoot = Saved("bs1", "TestRoot");
                    if (savedRoot.Length == 0) savedRoot = Saved("bs2", "TestRoot");
                    if (session["BVR_TESTROOT"].Length == 0) session["BVR_TESTROOT"] = savedRoot;
                    // Refuse before any file/registry actions, not only in the
                    // per-game deferred actions. Test packages cannot target real data.
                    string root = session["BVR_TESTROOT"];
                    if (string.IsNullOrWhiteSpace(root) ||
                        !System.IO.Path.GetFileName(root.TrimEnd('\\')).Equals(
                            "BvrMsiTest-" + SuitePlan.TestFamily, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Este MSI de prueba exige su carpeta aislada.");
                }
                bool installed = session["Installed"].Length > 0;
                if (session["BVR_SELECTIONINITIALIZED"] != "1")
                {
                    foreach (string id in Games)
                    {
                        // Session feature states are not costed yet here.
                        // Query the registered product, not the uncosted plan.
                        bool current = installed && MsiQueryFeatureStateW(session["ProductCode"], "Game_" + id) == InstallState.Local;
                        bool legacy = session["BVR_LEGACY_" + id.ToUpperInvariant()].Length > 0;
                        if (installed && legacy)
                            throw new InvalidOperationException("Hay otro MSI independiente registrado para " + id +
                                ". Retíralo antes de añadir ese juego al MSI conjunto.");
                        if (current || legacy) session[P(id, "SELECTED")] = "1";
                        session[P(id, "STATUS")] = legacy ? "MSI anterior: se integrará conservando sus copias." :
                            current ? "Mod instalado. Puedes conservarlo, repararlo o quitarlo." : "Mod no instalado con este MSI.";
                    }
                    session["BVR_SELECTIONINITIALIZED"] = "1";
                }
                // RemoveExistingProducts is initial-install only. Adopt ALL
                // registered predecessor MSIs in that transaction, so subsequent
                // maintenance never leaves two products owning a game's files.
                if (!installed)
                {
                    List<string> add = new List<string>();
                    if (session["ADDLOCAL"].Length > 0) add.AddRange(session["ADDLOCAL"].Split(','));
                    foreach (string id in Games)
                        if (session["BVR_LEGACY_" + id.ToUpperInvariant()].Length > 0)
                        {
                            session[P(id, "SELECTED")] = "1";
                            if (!add.Contains("Game_" + id)) add.Add("Game_" + id);
                            if (Saved(id, "DesktopShortcut") != "0" && !add.Contains("Desktop_" + id)) add.Add("Desktop_" + id);
                        }
                    if (add.Count > 0) session["ADDLOCAL"] = string.Join(",", add.ToArray());
                }
                return ActionResult.Success;
            }
            catch (Exception ex) { return Error(session, ex); }
        }

        [CustomAction]
        public static ActionResult ApplySelection(Session session)
        {
            try
            {
                List<string> add = new List<string>(), remove = new List<string>(), repair = new List<string>();
                int selectedCount = 0;
                foreach (string id in Games)
                {
                    bool selected = session[P(id, "SELECTED")] == "1";
                    FeatureInfo feature = session.Features["Game_" + id];
                    bool current = Local(feature);
                    if (session["BVR_LEGACY_" + id.ToUpperInvariant()].Length > 0 && !selected)
                        throw new InvalidOperationException("En esta primera instalación se deben integrar los MSI anteriores. Después podrás quitar cada mod por separado.");
                    if (selected)
                    {
                        selectedCount++;
                        if (!current) add.Add("Game_" + id);
                        bool shortcut = session[P(id, "DESKTOPSHORTCUT")] == "1";
                        bool shortcutChanged = current && shortcut != (Saved(id, "DesktopShortcut") != "0");
                        bool repairing = current && (session[P(id, "REPAIR")] == "1" || shortcutChanged);
                        session[P(id, "REPAIR")] = repairing ? "1" : "";
                        if (repairing) repair.Add("Game_" + id);
                        if (shortcut)
                        {
                            if (!Local(session.Features["Desktop_" + id])) add.Add("Desktop_" + id);
                            else if (repairing) repair.Add("Desktop_" + id);
                        }
                        else if (Local(session.Features["Desktop_" + id])) remove.Add("Desktop_" + id);
                    }
                    else if (current) { remove.Add("Game_" + id); remove.Add("Desktop_" + id); }
                    session[P(id, "SUMMARY")] = selected ? (!current ? "Instalar mod" :
                        session[P(id, "REPAIR")] == "1" ? "Reparar mod / actualizar acceso" : "Conservar sin cambios") :
                        current ? "QUITAR MOD (se conserva el juego)" : "No instalar";
                    // CostFinalize already ran in full UI. Keep untouched local
                    // features untouched, including their reinstall intentions.
                    InstallState desired = selected ? InstallState.Local : InstallState.Absent;
                    if (feature.RequestState != desired) feature.RequestState = desired;
                }
                if (selectedCount == 0 && session["Installed"].Length == 0)
                    throw new InvalidOperationException("Selecciona al menos un juego.");
                session["ADDLOCAL"] = string.Join(",", add.ToArray());
                session["REMOVE"] = selectedCount == 0 ? "ALL" : string.Join(",", remove.ToArray());
                session["REINSTALL"] = string.Join(",", repair.ToArray());
                session["REINSTALLMODE"] = "amus";
                session["BVR_VALID"] = "1";
                session["BVR_ERROR"] = "";
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session["BVR_VALID"] = "0";
                session["BVR_ERROR"] = ex.Message;
                return ActionResult.Success;
            }
        }

        [CustomAction]
        public static ActionResult PrepareSuite(Session session)
        {
            try
            {
                bool any = false;
                foreach (string id in Games)
                {
                    FeatureInfo feature = session.Features["Game_" + id];
                    bool current = Local(feature);
                    bool removing = current && feature.RequestState == InstallState.Absent;
                    bool installing = feature.RequestState == InstallState.Local && !current;
                    // MsiGetFeatureState reports Default (5), not Local (3),
                    // for a REINSTALL of an already-local feature.
                    bool repairing = current && feature.RequestState != InstallState.Absent &&
                        Has(session["REINSTALL"], "Game_" + id);
                    // Explicit ADDLOCAL on an already installed game is treated
                    // as no-op; use REINSTALL to repair it. Never retire files
                    // unless Windows has scheduled their installation/removal.
                    bool active = removing || installing || repairing;
                    session[P(id, "ACTIVE")] = active ? "1" : "";
                    session[P(id, "REMOVING")] = removing ? "1" : "";
                    if (active) any = true;
                    session.Log("Suite " + id + ": current=" + feature.CurrentState +
                        ", request=" + feature.RequestState + ", active=" + active + ", removing=" + removing);
                    if (session["BVR_LEGACY_" + id.ToUpperInvariant()].Length > 0 && !installing)
                        throw new InvalidOperationException("No se retirará un MSI anterior sin integrar su mod en el nuevo paquete.");
                }
                if (!any && session["Installed"].Length == 0)
                    throw new InvalidOperationException("Selecciona BioShock 1 o BioShock 2 antes de instalar.");
                return ActionResult.Success;
            }
            catch (Exception ex) { return Error(session, ex); }
        }
    }
}
