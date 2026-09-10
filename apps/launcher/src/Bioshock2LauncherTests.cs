using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BioshockVrLauncher
{
    internal static class Bs2LauncherTests
    {
        private static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidDataException(message);
        }

        internal static bool RunCoreTests()
        {
            string root = Path.Combine(Path.GetTempPath(), "BioShock2LauncherTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                TestResolutionPresets();
                TestVrWindowPolicy();
                List<ParamDef> definitions = Bs2Profile.AdaptDefinitions(new List<ParamDef>());
                // AdaptDefinitions also accepts an empty list to expose the complete BS2 key set.
                Check(definitions.Count >= 180, "Perfil incompleto.");
                Dictionary<string, string> defaults = ConfigDocument.Parse(Bs2Profile.WeaponDefaults);
                Check(defaults.Count == 128, "Se esperaban ocho perfiles con dieciséis campos.");
                foreach (string weapon in Bs2Profile.WeaponClasses)
                    Check(defaults.ContainsKey(weapon + ".aimTrimPitch") && defaults.ContainsKey(weapon + ".wOffUp"), "Arma incompleta: " + weapon);
                Check(defaults["PlayerResearchVideoCamera.modScale"] == "1.35", "Se ha perdido la calibración de la cámara.");
                Dictionary<string, string> vr = ConfigDocument.Parse(Bs2Profile.VrDefaults);
                Check(vr["handScaleL"] == "0.771" && vr.ContainsKey("aimTrimPitchR") &&
                    !vr.ContainsKey("aimTrimRPitch") && vr.ContainsKey("snapOn"), "Claves BS2 incorrectas.");

                DateTime requested = DateTime.UtcNow;
                HashSet<int> previous = new HashSet<int> { 12 };
                string exe = Path.Combine(root, "Game", Bs2Profile.ExeName);
                Check(GameLaunchEvidence.Matches(13, requested.AddSeconds(1), exe, previous, requested, exe), "Proceso nuevo válido rechazado.");
                Check(!GameLaunchEvidence.Matches(12, requested.AddSeconds(1), exe, previous, requested, exe), "Proceso previo aceptado.");
                Check(!GameLaunchEvidence.Matches(13, requested.AddSeconds(-1), exe, previous, requested, exe), "Inicio anterior aceptado.");
                Check(!GameLaunchEvidence.Matches(13, requested.AddSeconds(1), Path.Combine(root, "Steam.exe"), previous, requested, exe), "Steam confundido con el juego.");
                Check(!GameLaunchEvidence.Matches(13, requested.AddSeconds(1), Path.Combine(root, Bs2Profile.ExeName), previous, requested, exe), "Ruta errónea aceptada.");
                Check(!GameLaunchEvidence.Matches(13, requested, null, previous, requested, exe), "Proceso sin ruta aceptado.");
                using (Process self = Process.GetCurrentProcess())
                    Check(string.Equals(GameLaunchEvidence.ProcessPath(self.Id), self.MainModule.FileName, StringComparison.OrdinalIgnoreCase),
                        "No se pudo obtener la ruta real del proceso.");
                TestLaunchTracker(requested, exe, previous);
                TestRealHelperProcess(root);

                string first = Path.Combine(root, "Shared.ini");
                string second = Path.Combine(root, "Bioshock2SP.ini");
                string firstOriginal = "; comentario español\r\n[SharedOptions]\r\nViewportX=800\r\nViewportY=600\r\nOther=preservar\r\n";
                string secondOriginal = "; segundo\r\n[WinDrv.WindowsClient]\r\nWindowedViewportX=800\r\nWindowedViewportY=600\r\n";
                File.WriteAllText(first, firstOriginal, Encoding.Unicode);
                File.WriteAllText(second, secondOriginal, Encoding.GetEncoding(1252));
                string firstHash = Bs2Profile.Hash(first);
                string secondHash = Bs2Profile.Hash(second);
                string firstChanged = firstOriginal.Replace("800", "2048").Replace("600", "2048");
                string secondChanged = secondOriginal.Replace("800", "2048").Replace("600", "2048");
                bool failed = false;
                using (FileStream locked = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try
                    {
                        AtomicConfigBatch.Save(new ConfigWrite[] {
                            new ConfigWrite(first, firstOriginal, firstChanged, Encoding.Unicode),
                            new ConfigWrite(second, secondOriginal, secondChanged, Encoding.GetEncoding(1252))
                        });
                    }
                    catch (IOException) { failed = true; }
                }
                Check(failed, "Un segundo archivo bloqueado debía impedir el lote.");
                Check(Bs2Profile.Hash(first) == firstHash && Bs2Profile.Hash(second) == secondHash,
                    "Rollback no preservó los bytes originales.");
                Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Quedaron temporales.");
                AtomicConfigBatch.Save(new ConfigWrite[] {
                    new ConfigWrite(first, firstOriginal, firstChanged, Encoding.Unicode),
                    new ConfigWrite(second, secondOriginal, secondChanged, Encoding.GetEncoding(1252))
                });
                Check(File.ReadAllText(first, Encoding.Unicode) == firstChanged &&
                    File.ReadAllText(second, Encoding.GetEncoding(1252)) == secondChanged, "Guardado doble fallido.");
                Check(AtomicConfigBatch.DetectEncoding(first).CodePage == Encoding.Unicode.CodePage, "Se perdió UTF-16.");
                bool verifiedBackup = false;
                foreach (string file in Directory.GetFiles(Path.Combine(root, "Copias del lanzador")))
                    if (Bs2Profile.Hash(file) == firstHash) verifiedBackup = true;
                Check(verifiedBackup, "No hay copia byte a byte verificada.");
                failed = false;
                try { AtomicConfigBatch.Save(new ConfigWrite[] { new ConfigWrite(first, firstOriginal, "bad", Encoding.Unicode) }); }
                catch (IOException) { failed = true; }
                Check(failed && File.ReadAllText(first, Encoding.Unicode) == firstChanged, "No se detectó la edición externa.");

                string newFile = Path.Combine(root, "vrpreset.ini");
                AtomicConfigBatch.Save(new ConfigWrite[] { new ConfigWrite(newFile, "", "autoVr=1\n", new UTF8Encoding(false)) });
                Check(File.ReadAllText(newFile) == "autoVr=1\n", "Falló creación de config nueva.");
                return true;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(root, "FAILURE.txt"), ex.ToString(), Encoding.UTF8);
                return false;
            }
        }

        private static void TestResolutionPresets()
        {
            int[] widths = { 0, 1920, 1500, 1650, 1800, 1950, 2048, 2100, 2250, 2400, 2550, 2560,
                             2700, 2850, 3000, 3072, 3150, 3300, 3450, 3600, 3750, 3900, 4050, 4096 };
            Check(ResolutionPresets.Items.Length == widths.Length, "Catálogo de resolución incompleto.");
            HashSet<string> dimensions = new HashSet<string>();
            for (int i = 0; i < widths.Length; ++i)
            {
                ResolutionPreset preset = ResolutionPresets.Items[i];
                int height = i == 1 ? 1080 : widths[i];
                Check(preset != null && preset.Width == widths[i] && preset.Height == height,
                    "Resolución incorrecta en índice " + i);
                Check(!string.IsNullOrWhiteSpace(preset.Label) && preset.ToString() == preset.Label,
                    "Etiqueta de resolución vacía o distinta del texto del selector.");
                Check(dimensions.Add(preset.Width + "x" + preset.Height), "Resolución duplicada.");
                Check(ResolutionPresets.FindIndex(preset.Width, preset.Height) == i,
                    "El catálogo no conserva la selección al recargar: " + preset.Label);
                if (i > 2)
                    Check(preset.Width > ResolutionPresets.Items[i - 1].Width &&
                          preset.Width - ResolutionPresets.Items[i - 1].Width <= 150,
                          "Los perfiles cuadrados deben estar ordenados y sin saltos mayores de 150.");
            }
            for (int side = 1500; side <= 4050; side += 150)
                Check(ResolutionPresets.FindIndex(side, side) > 1, "Falta el paso de 150 píxeles: " + side);
            Check(ResolutionPresets.Items[0].Label == "Personalizada / actual", "Etiqueta personalizada cambiada.");
            Check(ResolutionPresets.Items[1].Label == "1920 × 1080 · juego plano", "Falta el perfil plano.");
            Check(ResolutionPresets.Items[ResolutionPresets.FindIndex(2048, 2048)].Label == "2048 × 2048 · equilibrada" &&
                  ResolutionPresets.Items[ResolutionPresets.FindIndex(2560, 2560)].Label == "2560 × 2560 · más nítida" &&
                  ResolutionPresets.Items[ResolutionPresets.FindIndex(3072, 3072)].Label == "3072 × 3072 · GPU potente" &&
                  ResolutionPresets.Items[ResolutionPresets.FindIndex(4096, 4096)].Label == "4096 × 4096 · muy exigente",
                  "Se perdieron las etiquetas de resolución existentes.");
            int[,] custom = { { 800, 600 }, { 2200, 2200 }, { 3010, 3010 }, { 1080, 1920 }, { 2048, 2100 },
                              { 1536, 1536 }, { 1792, 1792 },
                              { 0, 2048 }, { -1, -1 }, { int.MaxValue, int.MaxValue } };
            for (int i = 0; i < custom.GetLength(0); ++i)
                Check(ResolutionPresets.FindIndex(custom[i, 0], custom[i, 1]) == 0,
                      "Una resolución personalizada se confundió con un preset.");
        }

        private static List<IniEntry> WindowEntries(string section, string value)
        {
            return GameIniDocument.Parse("[" + section + "]\r\nStartupFullscreen=" + value +
                                         "\r\nUnknown=preservar\r\n[OtraSeccion]\r\nStartupFullscreen=True\r\n");
        }

        private static void ExpectWindowPolicyRejected(IList<IniEntry> shared, IList<IniEntry> sp, string reason)
        {
            List<IniEntry> all = new List<IniEntry>();
            if (shared != null) all.AddRange(shared);
            if (sp != null) all.AddRange(sp);
            List<string> values = new List<string>();
            List<string> originals = new List<string>();
            foreach (IniEntry entry in all)
            {
                values.Add(entry.Value);
                originals.Add(entry.OriginalValue);
            }
            bool rejected = false;
            try { VrWindowPolicy.Apply(shared, sp); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "La política VR debía rechazar " + reason);
            for (int i = 0; i < all.Count; ++i)
                Check(all[i].Value == values[i] && all[i].OriginalValue == originals[i],
                      "Rechazar " + reason + " mutó un INI antes de validar ambos.");
        }

        private static void TestVrWindowPolicy()
        {
            string[] inputs = { "True", "False", "1", "0", "tRuE;", "FALSE;", "1;", "0;", " True ; " };
            string[] outputs = { "False", "False", "0", "0", "False;", "FALSE;", "0;", "0;", "False;" };
            for (int i = 0; i < inputs.Length; ++i)
                for (int j = 0; j < inputs.Length; ++j)
                {
                    List<IniEntry> shared = WindowEntries("SharedOptions", inputs[i]);
                    List<IniEntry> sp = WindowEntries("WinDrv.WindowsClient", inputs[j]);
                    string sharedOriginal = shared[0].OriginalValue;
                    string spOriginal = sp[0].OriginalValue;
                    bool expectChange = outputs[i] != sharedOriginal || outputs[j] != spOriginal;
                    Check(VrWindowPolicy.Apply(shared, sp) == expectChange, "Resultado de cambios fullscreen incorrecto.");
                    Check(shared[0].Value == outputs[i] && sp[0].Value == outputs[j],
                          "True/False, 1/0 o mezcla de formatos incorrecta.");
                    Check(shared[0].OriginalValue == sharedOriginal && sp[0].OriginalValue == spOriginal,
                          "La política modificó el valor original del parser.");
                    Check(shared[1].Value == "preservar" && sp[1].Value == "preservar" &&
                          shared[2].Value == "True" && sp[2].Value == "True",
                          "La política modificó claves ajenas a las secciones PC.");
                    Check(!VrWindowPolicy.Apply(shared, sp), "La política fullscreen no es idempotente.");
                }

            List<IniEntry> words = WindowEntries("sharedoptions", "True;");
            List<IniEntry> numbers = WindowEntries("windrv.windowsclient", "1;");
            words[0].Key = "startupfullscreen";
            words[0].Value = "1";
            numbers[0].Value = "True";
            Check(VrWindowPolicy.Apply(words, numbers) && words[0].Value == "False;" && numbers[0].Value == "0;",
                  "Debe respetar formato/punto y coma originales y comparación sin mayúsculas.");

            ExpectWindowPolicyRejected(new List<IniEntry>(), WindowEntries("WinDrv.WindowsClient", "True"), "clave Shared ausente");
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), new List<IniEntry>(), "clave SP ausente");
            ExpectWindowPolicyRejected(WindowEntries("OtraShared", "True"), WindowEntries("WinDrv.WindowsClient", "True"), "sección Shared incorrecta");
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), WindowEntries("OtraSP", "True"), "sección SP incorrecta");
            ExpectWindowPolicyRejected(null, WindowEntries("WinDrv.WindowsClient", "True"), "documento Shared ausente");
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), null, "documento SP ausente");

            List<IniEntry> duplicateShared = GameIniDocument.Parse("[SharedOptions]\r\nStartupFullscreen=True\r\n[sharedoptions]\r\nstartupfullscreen=False\r\n");
            List<IniEntry> duplicateSp = GameIniDocument.Parse("[WinDrv.WindowsClient]\r\nStartupFullscreen=True\r\nStartupFullscreen=0\r\n");
            ExpectWindowPolicyRejected(duplicateShared, WindowEntries("WinDrv.WindowsClient", "True"), "clave Shared duplicada");
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), duplicateSp, "clave SP duplicada");

            string[] invalid = { "", "yes", "2", "-1", "True;;", "False; comentario", "True False" };
            foreach (string value in invalid)
            {
                ExpectWindowPolicyRejected(WindowEntries("SharedOptions", value), WindowEntries("WinDrv.WindowsClient", "True"), "Shared inválido: " + value);
                ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), WindowEntries("WinDrv.WindowsClient", value), "SP inválido: " + value);
            }
            List<IniEntry> nullValue = WindowEntries("SharedOptions", "True");
            nullValue[0].Value = null;
            ExpectWindowPolicyRejected(nullValue, WindowEntries("WinDrv.WindowsClient", "True"), "valor actual nulo");
            List<IniEntry> invalidOriginal = WindowEntries("WinDrv.WindowsClient", "True");
            invalidOriginal[0].OriginalValue = "yes";
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), invalidOriginal, "valor original inválido");
        }

        private static void TestLaunchTracker(DateTime requested, string exe, ISet<int> previous)
        {
            GameProcessObservation candidate = new GameProcessObservation {
                Id = 55, StartedUtc = requested.AddSeconds(1), ImagePath = exe
            };
            GameProcessObservation[] present = { candidate };
            GameProcessObservation[] absent = new GameProcessObservation[0];
            GameLaunchTracker tracker = new GameLaunchTracker(requested, exe, previous);
            Check(tracker.Observe(requested.AddSeconds(5), absent) == LaunchOutcome.Waiting, "Cerraría antes del EXE.");
            Check(tracker.Observe(requested.AddSeconds(60), absent) == LaunchOutcome.TimedOut, "Falta timeout sin EXE.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            candidate.Id = 12;
            candidate.HasWindow = candidate.Responding = true;
            Check(tracker.Observe(requested.AddSeconds(60), present) == LaunchOutcome.TimedOut, "Proceso previo aceptado.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            candidate.Id = 55;
            candidate.ImagePath = Path.Combine(Path.GetDirectoryName(exe), "steam.exe");
            Check(tracker.Observe(requested.AddSeconds(60), present) == LaunchOutcome.TimedOut, "Steam aceptado como juego.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            candidate.ImagePath = exe;
            candidate.HasWindow = false;
            Check(tracker.Observe(requested.AddSeconds(5), present) == LaunchOutcome.Waiting, "Proceso sin ventana aceptado.");
            Check(tracker.Observe(requested.AddSeconds(55), present) == LaunchOutcome.Waiting, "No esperó la ventana.");
            candidate.HasWindow = candidate.Responding = true;
            Check(tracker.Observe(requested.AddSeconds(55), present) == LaunchOutcome.Waiting, "La ventana acaba de aparecer.");
            Check(tracker.Observe(requested.AddSeconds(57), present) == LaunchOutcome.Waiting, "No esperó tres segundos.");
            candidate.Responding = false;
            Check(tracker.Observe(requested.AddSeconds(57), present) == LaunchOutcome.Waiting, "Ventana bloqueada aceptada.");
            candidate.Responding = true;
            Check(tracker.Observe(requested.AddSeconds(58), present) == LaunchOutcome.Waiting, "No reinició la estabilidad.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            Check(tracker.Observe(requested.AddSeconds(5), present) == LaunchOutcome.Waiting, "Inicio inmediato.");
            Check(tracker.Observe(requested.AddSeconds(8), present) == LaunchOutcome.Started, "Proceso y ventana válidos rechazados.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            candidate.HasWindow = false;
            Check(tracker.Observe(requested.AddSeconds(1), present) == LaunchOutcome.Waiting, "Espera inicial incorrecta.");
            Check(tracker.Observe(requested.AddSeconds(2), absent) == LaunchOutcome.ExitedEarly, "No detectó salida temprana.");
        }

        private static void TestRealHelperProcess(string root)
        {
            string directory = Path.Combine(root, "fake-game");
            Directory.CreateDirectory(directory);
            string fixture = Path.Combine(directory, Bs2Profile.ExeName);
            File.Copy(Application.ExecutablePath, fixture, false);
            DateTime requested = DateTime.UtcNow;
            GameLaunchTracker tracker = new GameLaunchTracker(requested, fixture, new HashSet<int>());
            ProcessStartInfo start = new ProcessStartInfo(fixture, "--self-test-process");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            using (Process helper = Process.Start(start))
            {
                try
                {
                    bool seen = false;
                    while (!helper.HasExited && (DateTime.UtcNow - requested).TotalSeconds < 8)
                    {
                        helper.Refresh();
                        GameProcessObservation sample = new GameProcessObservation {
                            Id = helper.Id, StartedUtc = helper.StartTime.ToUniversalTime(),
                            ImagePath = GameLaunchEvidence.ProcessPath(helper.Id),
                            HasWindow = helper.MainWindowHandle != IntPtr.Zero, Responding = helper.Responding
                        };
                        if (tracker.Observe(DateTime.UtcNow, new GameProcessObservation[] { sample }) == LaunchOutcome.Started)
                        { seen = true; break; }
                        System.Threading.Thread.Sleep(100);
                    }
                    Check(seen, "El helper Bioshock2HD.exe no confirmó ventana/ruta/estabilidad.");
                }
                finally
                {
                    if (!helper.HasExited && string.Equals(GameLaunchEvidence.ProcessPath(helper.Id), fixture, StringComparison.OrdinalIgnoreCase))
                    { helper.Kill(); helper.WaitForExit(3000); }
                }
            }
        }

        private sealed class HelperWindow : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000080; p.ExStyle &= ~0x00040000; return p; }
            }
        }

        internal static int RunHelperWindow()
        {
            using (HelperWindow form = new HelperWindow())
            using (Timer timer = new Timer())
            {
                form.Text = "BS2 launcher test helper";
                form.ShowInTaskbar = true;
                form.Opacity = 0;
                timer.Interval = 10000;
                timer.Tick += delegate { form.Close(); };
                form.Shown += delegate { timer.Start(); };
                Application.Run(form);
            }
            return 0;
        }

        internal static int RunUiTests(string root)
        {
            // A dedicated NEW directory prevents this test from overwriting an existing setup.
            if (Directory.Exists(root) || File.Exists(root)) return 3;
            Directory.CreateDirectory(root);
            Program.SandboxRoot = root;
            Program.InitialGamePath = null;
            try
            {
                Directory.CreateDirectory(Bs2Profile.GameIniDirectory);
                Directory.CreateDirectory(Bs2Profile.LocalDirectory);
                string shared = Path.Combine(Bs2Profile.GameIniDirectory, "Shared.ini");
                string sp = Path.Combine(Bs2Profile.GameIniDirectory, "Bioshock2SP.ini");
                string vr = Path.Combine(Bs2Profile.LocalDirectory, "vrpreset.ini");
                string weapons = Path.Combine(Bs2Profile.LocalDirectory, "weapons.ini");
                File.WriteAllText(shared, "; mantener español\r\n[SharedOptions]\r\nViewportX=800\r\nViewportY=600\r\nStartupFullscreen=True\r\nUnknown=preservar\r\n", Encoding.Unicode);
                File.WriteAllText(sp, "[WinDrv.WindowsClient]\r\nWindowedViewportX=1024\r\nWindowedViewportY=768\r\nFullscreenViewportX=1024\r\nFullscreenViewportY=768\r\nStartupFullscreen=True\r\n[Engine.RenderConfig]\r\nUseFxaa=0\r\n", Encoding.GetEncoding(1252));
                File.WriteAllText(vr, Bs2Profile.VrDefaults.Replace("worldScale=100.0", "worldScale=250.12345") + "\nunknownVr=keep\n", new UTF8Encoding(false));
                File.WriteAllText(weapons, Bs2Profile.WeaponDefaults + "\nCustomWeapon.customField=7.0\n", new UTF8Encoding(false));
                string[] files = { shared, sp, vr, weapons };
                Dictionary<string, string> originalHashes = new Dictionary<string, string>();
                foreach (string file in files) originalHashes[file] = Bs2Profile.Hash(file);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int exitCode = 2;
                using (MainForm form = new MainForm())
                {
                    foreach (string file in files) Check(Bs2Profile.Hash(file) == originalHashes[file], "Abrir modificó " + file);
                    Timer timer = new Timer();
                    timer.Interval = 1800;
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        try
                        {
                            Check(form.RunSandboxRoundTrip(), "Falló el guardado desde la interfaz.");
                            Check(File.ReadAllText(shared).Contains("StartupFullscreen=False") &&
                                  File.ReadAllText(sp).Contains("StartupFullscreen=False"),
                                  "Guardar desde la interfaz no aplicó modo de ventana a ambos INI.");
                            Check(File.ReadAllText(shared).Contains("Unknown=preservar"), "Se perdió una clave Shared desconocida.");
                            Check(File.ReadAllText(vr).Contains("unknownVr=keep"), "Se perdió una clave VR desconocida.");
                            Check(File.ReadAllText(weapons).Contains("CustomWeapon.customField=7.0"), "Se perdió un perfil externo.");
                            form.CaptureSandboxTabs(root);
                            File.WriteAllText(Path.Combine(root, "PASS.txt"),
                                "PASS: apertura sin escrituras con StartupFullscreen=True; Shared.ini 800x600 autoritativo; guardado 2048x2048 y ventana en ambos INI; " +
                                "ocho armas; mano izquierda; claves desconocidas; copias; interfaz nativa mostrada 1,8 s.\n", Encoding.UTF8);
                            exitCode = 0;
                        }
                        catch (Exception ex)
                        { File.WriteAllText(Path.Combine(root, "FAILURE.txt"), ex.ToString(), Encoding.UTF8); }
                        finally { timer.Dispose(); form.Close(); }
                    };
                    form.Shown += delegate { timer.Start(); };
                    Application.Run(form);
                }
                return exitCode;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(root, "FAILURE.txt"), ex.ToString(), Encoding.UTF8);
                return 2;
            }
        }
    }
}
