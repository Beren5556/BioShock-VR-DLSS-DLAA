using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace BioShockSuite
{
    public static class Actions
    {
        [DllImport("msi.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
        private static extern InstallState MsiQueryFeatureStateW(string product, string feature);
        [DllImport("msi.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
        private static extern InstallState MsiQueryProductStateW(string product);
        private static bool IsGame(string id) { return id == "bs1" || id == "bs2"; }
        private static string P(string id, string suffix) { return "BVR_" + id.ToUpperInvariant() + "_" + suffix; }
        private static string Product(string id) { return id == "bs1" ? SuitePlan.Bs1Product : SuitePlan.Bs2Product; }
        private static string Title(string id) { return id == "bs1" ? "BioShock Remastered" : "BioShock 2 Remastered"; }
        private static string Saved(string id, string name)
        {
            using (RegistryKey key=Registry.CurrentUser.OpenSubKey(id == "bs1" ? SuitePlan.Bs1Registry : SuitePlan.Bs2Registry))
                return key == null ? "" : Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture);
        }
        private static string Instance(Session session)
        {
            string id=session["BVR_INSTANCE"];
            if (!IsGame(id) || !string.Equals(session["ProductCode"], Product(id), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Selecciona el juego desde el desplegable inicial del MSI.");
            return id;
        }
        private static void CheckTestRoot(Session session, string id)
        {
            if (SuitePlan.TestFamily.Length == 0) return;
            if (session["BVR_TESTROOT"].Length == 0 && IsGame(id)) session["BVR_TESTROOT"] = Saved(id,"TestRoot");
            string root=session["BVR_TESTROOT"];
            if (string.IsNullOrWhiteSpace(root) || !Path.GetFileName(root.TrimEnd('\\')).Equals(
                "BvrMsiTest-"+SuitePlan.TestFamily, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Este MSI de prueba solo admite su carpeta aislada.");
        }
        private static ActionResult Error(Session session, Exception error)
        {
            session.Log("MSI por juego: " + error);
            using (Record record=new Record(1)) { record.FormatString="[1]";record[1]=error.Message;session.Message(InstallMessage.Error,record); }
            return ActionResult.Failure;
        }
        private static string Quote(string value)
        {
            if (value.IndexOfAny(new[]{'"','\r','\n'}) >= 0) throw new InvalidOperationException("Ruta no válida para Windows Installer.");
            return "\"" + value.TrimEnd('\\') + "\"";
        }

        // UI-only dispatcher. The base product is NEVER installed. Its UI exits
        // before InstallInitialize; the same MSI opens one native instance.
        // There is no nested MSI action, no payload extraction and no EXE bundle.
        [CustomAction]
        public static ActionResult OpenSelectedGame(Session session)
        {
            try
            {
                string id=session["BVR_GAME"];
                if (!IsGame(id)) throw new InvalidOperationException("Elige BioShock 1 o BioShock 2 en el desplegable.");
                CheckTestRoot(session,id);
                string source=Path.GetFullPath(session["OriginalDatabase"]);
                if (!File.Exists(source) || !source.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Vuelve a abrir el archivo MSI original para seleccionar un juego.");
                string args="/i " + Quote(source);
                if (MsiQueryProductStateW(Product(id)) == InstallState.Default)
                    args += " /n " + Product(id);
                else args += " TRANSFORMS=:" + id + " MSINEWINSTANCE=1";
                if (SuitePlan.TestFamily.Length > 0) args += " BVR_TESTROOT=" + Quote(session["BVR_TESTROOT"]);
                string gamePath=session[P(id,"GAMEPATH")];
                if (gamePath.Length > 0) args += " " + P(id,"GAMEPATH") + "=" + Quote(gamePath);
                if (SuitePlan.TestFamily.Length > 0 && session["BVR_TESTDISPATCHONLY"] == "1")
                    session["BVR_DISPATCH_ARGS"] = args;
                else
                {
                    string logRoot=SuitePlan.TestFamily.Length > 0 ? Path.Combine(session["BVR_TESTROOT"],"Logs") :
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            id == "bs1" ? @"BioshockVR\WindowsInstaller\Logs" : @"BioshockVR\bs2\WindowsInstaller\Logs");
                    Directory.CreateDirectory(logRoot);
                    args += " /l*v " + Quote(Path.Combine(logRoot, id+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".log"));
                    ProcessStartInfo start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),args);
                    start.UseShellExecute=false;
                    using(Process child=Process.Start(start)) { if(child==null) throw new IOException("No se pudo abrir el asistente del juego."); }
                }
                session["BVR_VALID"]="1";session["BVR_DISPATCHING"]="1";
            }
            catch(Exception ex) { session["BVR_VALID"]="0";session["BVR_ERROR"]=ex.Message;session.Log(ex.ToString()); }
            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult DetectSuite(Session session)
        {
            try
            {
                if (session["BVR_INSTANCE"] == "selector") { CheckTestRoot(session,""); return ActionResult.Success; }
                string id=Instance(session), other=id == "bs1" ? "bs2" : "bs1";
                CheckTestRoot(session,id);
                session["BVR_GAME_TITLE"]=Title(id);
                if (session["BVR_OPERATION"].Length == 0) session["BVR_OPERATION"]=session["REMOVE"] == "ALL" ? "remove" : "install";
                // Split the previously delivered two-feature product only for
                // this game. The other feature, files and backups stay owned
                // by that older product until that game is explicitly chosen.
                var related=new List<string>();
                string remove="";
                foreach(string old in session["BVR_OLD_SUITE"].Split(';'))
                {
                    if (old.Length == 0 || MsiQueryFeatureStateW(old,"Game_"+id) != InstallState.Local) continue;
                    if (related.Count > 0) throw new InvalidOperationException("Hay varias instalaciones conjuntas anteriores; revisa el registro antes de continuar.");
                    related.Add(old);
                    remove=MsiQueryFeatureStateW(old,"Game_"+other) == InstallState.Local ? "Game_"+id+",Desktop_"+id : "ALL";
                }
                session["BVR_OLD_SUITE"]=string.Join(";",related.ToArray());
                session["BVR_OLD_REMOVE"]=remove;
                return ActionResult.Success;
            }
            catch(Exception ex) { return Error(session,ex); }
        }

        [CustomAction]
        public static ActionResult PlanSingleGame(Session session)
        {
            try
            {
                string id=Instance(session), other=id == "bs1" ? "bs2" : "bs1";
                bool current=session.Features["Game_"+id].CurrentState == InstallState.Local;
                bool removing=session["BVR_OPERATION"] == "remove";
                bool shortcut=session[P(id,"DESKTOPSHORTCUT")] == "1";
                if (removing && !current) throw new InvalidOperationException("Este MSI todavía no ha instalado el mod seleccionado.");
                if (session.Features["Game_"+other].CurrentState == InstallState.Local)
                    throw new InvalidOperationException("La identidad MSI no está aislada por juego.");
                session.Features["Game_"+id].RequestState=removing ? InstallState.Absent : InstallState.Local;
                session.Features["Desktop_"+id].RequestState=!removing && shortcut ? InstallState.Local : InstallState.Absent;
                session["ADDLOCAL"]=!removing && !current ? "Game_"+id+(shortcut ? ",Desktop_"+id : "") : "";
                session["REMOVE"]=removing ? "ALL" : current && !shortcut ? "Desktop_"+id : "";
                session["REINSTALL"]=!removing && current ? "Game_"+id+(shortcut ? ",Desktop_"+id : "") : "";
                session["REINSTALLMODE"]="amus";
                session["BVR_SUMMARY"]=removing ? "Desinstalar este mod y recuperar sus archivos previos." :
                    current ? "Reparar / reinstalar este mod conservando tus preferencias." : "Instalar / actualizar este mod conservando tus preferencias.";
                session["BVR_SELECTED_PATH"]=session[id.ToUpperInvariant()+"DIR"];
                session["BVR_VALID"]="1";session["BVR_ERROR"]="";
            }
            catch(Exception ex) { session["BVR_VALID"]="0";session["BVR_ERROR"]=ex.Message; }
            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult PrepareSuite(Session session)
        {
            try
            {
                string id=Instance(session), other=id == "bs1" ? "bs2" : "bs1";
                // Defense in depth: even an explicit ADDLOCAL=ALL or a bad UI
                // plan cannot schedule the other game's files in this instance.
                foreach(string feature in new[]{"Game_"+other,"Desktop_"+other})
                    if (session.Features[feature].CurrentState == InstallState.Local ||
                        session.Features[feature].RequestState == InstallState.Local)
                        throw new InvalidOperationException("Esta ejecución no puede modificar el otro juego.");
                FeatureInfo selected=session.Features["Game_"+id];
                bool current=selected.CurrentState == InstallState.Local;
                bool removing=current && selected.RequestState == InstallState.Absent;
                bool active=removing || selected.RequestState == InstallState.Local ||
                    current && (session["REINSTALL"] == "ALL" || session["REINSTALL"].Contains("Game_"+id));
                if (!active) throw new InvalidOperationException("No se ha solicitado ninguna operación para este juego.");
                session[P(id,"ACTIVE")]="1";session[P(id,"REMOVING")]=removing ? "1" : "";
                session[P(other,"ACTIVE")]="";session[P(other,"REMOVING")]="";
                session.Log("Single game: "+id+", current="+selected.CurrentState+", request="+selected.RequestState+", removing="+removing);
                return ActionResult.Success;
            }
            catch(Exception ex) { return Error(session,ex); }
        }
    }
}
