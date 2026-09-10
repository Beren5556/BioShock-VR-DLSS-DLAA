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
                Check(definitions.Count >= 180, "Incomplete profile.");
                Dictionary<string, string> defaults = ConfigDocument.Parse(Bs2Profile.WeaponDefaults);
                Check(defaults.Count == 128, "Expected eight profiles with sixteen fields.");
                foreach (string weapon in Bs2Profile.WeaponClasses)
                    Check(defaults.ContainsKey(weapon + ".aimTrimPitch") && defaults.ContainsKey(weapon + ".wOffUp"), "Incomplete weapon: " + weapon);
                Check(defaults["PlayerResearchVideoCamera.modScale"] == "1.35", "Camera calibration was lost.");
                Dictionary<string, string> vr = ConfigDocument.Parse(Bs2Profile.VrDefaults);
                Check(vr["handScaleL"] == "0.771" && vr.ContainsKey("aimTrimPitchR") &&
                    !vr.ContainsKey("aimTrimRPitch") && vr.ContainsKey("snapOn"), "Incorrect BS2 keys.");

                DateTime requested = DateTime.UtcNow;
                HashSet<int> previous = new HashSet<int> { 12 };
                string exe = Path.Combine(root, "Game", Bs2Profile.ExeName);
                Check(GameLaunchEvidence.Matches(13, requested.AddSeconds(1), exe, previous, requested, exe), "Valid new process was rejected.");
                Check(!GameLaunchEvidence.Matches(12, requested.AddSeconds(1), exe, previous, requested, exe), "Existing process accepted.");
                Check(!GameLaunchEvidence.Matches(13, requested.AddSeconds(-1), exe, previous, requested, exe), "Earlier startup was accepted.");
                Check(!GameLaunchEvidence.Matches(13, requested.AddSeconds(1), Path.Combine(root, "Steam.exe"), previous, requested, exe), "Steam was mistaken for the game.");
                Check(!GameLaunchEvidence.Matches(13, requested.AddSeconds(1), Path.Combine(root, Bs2Profile.ExeName), previous, requested, exe), "Incorrect path was accepted.");
                Check(!GameLaunchEvidence.Matches(13, requested, null, previous, requested, exe), "Process without a path was accepted.");
                using (Process self = Process.GetCurrentProcess())
                    Check(string.Equals(GameLaunchEvidence.ProcessPath(self.Id), self.MainModule.FileName, StringComparison.OrdinalIgnoreCase),
                        "Could not obtain the actual process path.");
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
                Check(failed, "A locked second file should have blocked the batch.");
                Check(Bs2Profile.Hash(first) == firstHash && Bs2Profile.Hash(second) == secondHash,
                    "Rollback did not preserve the original bytes.");
                Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Temporary files left behind.");
                AtomicConfigBatch.Save(new ConfigWrite[] {
                    new ConfigWrite(first, firstOriginal, firstChanged, Encoding.Unicode),
                    new ConfigWrite(second, secondOriginal, secondChanged, Encoding.GetEncoding(1252))
                });
                Check(File.ReadAllText(first, Encoding.Unicode) == firstChanged &&
                    File.ReadAllText(second, Encoding.GetEncoding(1252)) == secondChanged, "Two-file save failed.");
                Check(AtomicConfigBatch.DetectEncoding(first).CodePage == Encoding.Unicode.CodePage, "UTF-16 was lost.");
                bool verifiedBackup = false;
                foreach (string file in Directory.GetFiles(Path.Combine(root, "Copias del lanzador")))
                    if (Bs2Profile.Hash(file) == firstHash) verifiedBackup = true;
                Check(verifiedBackup, "No verified byte-for-byte backup.");
                failed = false;
                try { AtomicConfigBatch.Save(new ConfigWrite[] { new ConfigWrite(first, firstOriginal, "bad", Encoding.Unicode) }); }
                catch (IOException) { failed = true; }
                Check(failed && File.ReadAllText(first, Encoding.Unicode) == firstChanged, "External editing was not detected.");

                string newFile = Path.Combine(root, "vrpreset.ini");
                AtomicConfigBatch.Save(new ConfigWrite[] { new ConfigWrite(newFile, "", "autoVr=1\n", new UTF8Encoding(false)) });
                Check(File.ReadAllText(newFile) == "autoVr=1\n", "New configuration creation failed.");
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
            Check(ResolutionPresets.Items.Length == widths.Length, "Incomplete resolution catalog.");
            HashSet<string> dimensions = new HashSet<string>();
            for (int i = 0; i < widths.Length; ++i)
            {
                ResolutionPreset preset = ResolutionPresets.Items[i];
                int height = i == 1 ? 1080 : widths[i];
                Check(preset != null && preset.Width == widths[i] && preset.Height == height,
                    "Incorrect resolution at index " + i);
                Check(!string.IsNullOrWhiteSpace(preset.Label) && preset.ToString() == preset.Label,
                    "Resolution label is empty or differs from the selector text.");
                Check(dimensions.Add(preset.Width + "x" + preset.Height), "Duplicate resolution.");
                Check(ResolutionPresets.FindIndex(preset.Width, preset.Height) == i,
                    "The catalog does not preserve selection on reload: " + preset.Label);
                if (i > 2)
                    Check(preset.Width > ResolutionPresets.Items[i - 1].Width &&
                          preset.Width - ResolutionPresets.Items[i - 1].Width <= 150,
                          "Square profiles must be sorted with no gaps larger than 150.");
            }
            for (int side = 1500; side <= 4050; side += 150)
                Check(ResolutionPresets.FindIndex(side, side) > 1, "Missing 150-pixel step: " + side);
            Check(ResolutionPresets.Items[0].Label == "Custom / current", "Custom label changed.");
            Check(ResolutionPresets.Items[1].Label == "1920 × 1080 · flat-screen", "The flat-screen profile is missing.");
            Check(ResolutionPresets.Items[ResolutionPresets.FindIndex(2048, 2048)].Label == "2048 × 2048 · balanced" &&
                  ResolutionPresets.Items[ResolutionPresets.FindIndex(2560, 2560)].Label == "2560 × 2560 · sharper" &&
                  ResolutionPresets.Items[ResolutionPresets.FindIndex(3072, 3072)].Label == "3072 × 3072 · powerful GPU" &&
                  ResolutionPresets.Items[ResolutionPresets.FindIndex(4096, 4096)].Label == "4096 × 4096 · very demanding",
                  "Existing resolution labels were lost.");
            int[,] custom = { { 800, 600 }, { 2200, 2200 }, { 3010, 3010 }, { 1080, 1920 }, { 2048, 2100 },
                              { 1536, 1536 }, { 1792, 1792 },
                              { 0, 2048 }, { -1, -1 }, { int.MaxValue, int.MaxValue } };
            for (int i = 0; i < custom.GetLength(0); ++i)
                Check(ResolutionPresets.FindIndex(custom[i, 0], custom[i, 1]) == 0,
                      "A custom resolution was mistaken for a preset.");
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
            Check(rejected, "The VR policy should have rejected " + reason);
            for (int i = 0; i < all.Count; ++i)
                Check(all[i].Value == values[i] && all[i].OriginalValue == originals[i],
                      "Reject " + reason + " changed an INI before validating both.");
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
                    Check(VrWindowPolicy.Apply(shared, sp) == expectChange, "Incorrect fullscreen change result.");
                    Check(shared[0].Value == outputs[i] && sp[0].Value == outputs[j],
                          "Incorrect True/False, 1/0 or mixed formats.");
                    Check(shared[0].OriginalValue == sharedOriginal && sp[0].OriginalValue == spOriginal,
                          "The policy changed the parser's original value.");
                    Check(shared[1].Value == "preservar" && sp[1].Value == "preservar" &&
                          shared[2].Value == "True" && sp[2].Value == "True",
                          "The policy changed keys outside the PC sections.");
                    Check(!VrWindowPolicy.Apply(shared, sp), "The fullscreen policy is not idempotent.");
                }

            List<IniEntry> words = WindowEntries("sharedoptions", "True;");
            List<IniEntry> numbers = WindowEntries("windrv.windowsclient", "1;");
            words[0].Key = "startupfullscreen";
            words[0].Value = "1";
            numbers[0].Value = "True";
            Check(VrWindowPolicy.Apply(words, numbers) && words[0].Value == "False;" && numbers[0].Value == "0;",
                  "Must preserve original format/semicolon and case-insensitive comparison.");

            ExpectWindowPolicyRejected(new List<IniEntry>(), WindowEntries("WinDrv.WindowsClient", "True"), "missing Shared key");
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), new List<IniEntry>(), "missing SP key");
            ExpectWindowPolicyRejected(WindowEntries("OtraShared", "True"), WindowEntries("WinDrv.WindowsClient", "True"), "incorrect Shared section");
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), WindowEntries("OtraSP", "True"), "incorrect SP section");
            ExpectWindowPolicyRejected(null, WindowEntries("WinDrv.WindowsClient", "True"), "missing Shared document");
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), null, "missing SP document");

            List<IniEntry> duplicateShared = GameIniDocument.Parse("[SharedOptions]\r\nStartupFullscreen=True\r\n[sharedoptions]\r\nstartupfullscreen=False\r\n");
            List<IniEntry> duplicateSp = GameIniDocument.Parse("[WinDrv.WindowsClient]\r\nStartupFullscreen=True\r\nStartupFullscreen=0\r\n");
            ExpectWindowPolicyRejected(duplicateShared, WindowEntries("WinDrv.WindowsClient", "True"), "duplicate Shared key");
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), duplicateSp, "duplicate SP key");

            string[] invalid = { "", "yes", "2", "-1", "True;;", "False; comentario", "True False" };
            foreach (string value in invalid)
            {
                ExpectWindowPolicyRejected(WindowEntries("SharedOptions", value), WindowEntries("WinDrv.WindowsClient", "True"), "Invalid Shared: " + value);
                ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), WindowEntries("WinDrv.WindowsClient", value), "Invalid SP: " + value);
            }
            List<IniEntry> nullValue = WindowEntries("SharedOptions", "True");
            nullValue[0].Value = null;
            ExpectWindowPolicyRejected(nullValue, WindowEntries("WinDrv.WindowsClient", "True"), "null current value");
            List<IniEntry> invalidOriginal = WindowEntries("WinDrv.WindowsClient", "True");
            invalidOriginal[0].OriginalValue = "yes";
            ExpectWindowPolicyRejected(WindowEntries("SharedOptions", "True"), invalidOriginal, "invalid original value");
        }

        private static void TestLaunchTracker(DateTime requested, string exe, ISet<int> previous)
        {
            GameProcessObservation candidate = new GameProcessObservation {
                Id = 55, StartedUtc = requested.AddSeconds(1), ImagePath = exe
            };
            GameProcessObservation[] present = { candidate };
            GameProcessObservation[] absent = new GameProcessObservation[0];
            GameLaunchTracker tracker = new GameLaunchTracker(requested, exe, previous);
            Check(tracker.Observe(requested.AddSeconds(5), absent) == LaunchOutcome.Waiting, "Would close before the EXE starts.");
            Check(tracker.Observe(requested.AddSeconds(60), absent) == LaunchOutcome.TimedOut, "Missing timeout without an EXE.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            candidate.Id = 12;
            candidate.HasWindow = candidate.Responding = true;
            Check(tracker.Observe(requested.AddSeconds(60), present) == LaunchOutcome.TimedOut, "Existing process accepted.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            candidate.Id = 55;
            candidate.ImagePath = Path.Combine(Path.GetDirectoryName(exe), "steam.exe");
            Check(tracker.Observe(requested.AddSeconds(60), present) == LaunchOutcome.TimedOut, "Steam accepted as the game.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            candidate.ImagePath = exe;
            candidate.HasWindow = false;
            Check(tracker.Observe(requested.AddSeconds(5), present) == LaunchOutcome.Waiting, "Process without a window was accepted.");
            Check(tracker.Observe(requested.AddSeconds(55), present) == LaunchOutcome.Waiting, "Did not wait for the window.");
            candidate.HasWindow = candidate.Responding = true;
            Check(tracker.Observe(requested.AddSeconds(55), present) == LaunchOutcome.Waiting, "The window has just appeared.");
            Check(tracker.Observe(requested.AddSeconds(57), present) == LaunchOutcome.Waiting, "Did not wait three seconds.");
            candidate.Responding = false;
            Check(tracker.Observe(requested.AddSeconds(57), present) == LaunchOutcome.Waiting, "Unresponsive window accepted.");
            candidate.Responding = true;
            Check(tracker.Observe(requested.AddSeconds(58), present) == LaunchOutcome.Waiting, "Stability was not reset.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            Check(tracker.Observe(requested.AddSeconds(5), present) == LaunchOutcome.Waiting, "Immediate start.");
            Check(tracker.Observe(requested.AddSeconds(8), present) == LaunchOutcome.Started, "Valid process and window were rejected.");
            tracker = new GameLaunchTracker(requested, exe, previous);
            candidate.HasWindow = false;
            Check(tracker.Observe(requested.AddSeconds(1), present) == LaunchOutcome.Waiting, "Incorrect initial wait.");
            Check(tracker.Observe(requested.AddSeconds(2), absent) == LaunchOutcome.ExitedEarly, "Early exit was not detected.");
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
                    Check(seen, "The Bioshock2HD.exe helper did not confirm window/path/stability.");
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
                    foreach (string file in files) Check(Bs2Profile.Hash(file) == originalHashes[file], "Opening modified " + file);
                    Timer timer = new Timer();
                    timer.Interval = 1800;
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        try
                        {
                            Check(form.RunSandboxRoundTrip(), "Saving from the interface failed.");
                            Check(File.ReadAllText(shared).Contains("StartupFullscreen=False") &&
                                  File.ReadAllText(sp).Contains("StartupFullscreen=False"),
                                  "Saving from the interface did not apply windowed mode to both INIs.");
                            Check(File.ReadAllText(shared).Contains("Unknown=preservar"), "An unknown Shared key was lost.");
                            Check(File.ReadAllText(vr).Contains("unknownVr=keep"), "An unknown VR key was lost.");
                            Check(File.ReadAllText(weapons).Contains("CustomWeapon.customField=7.0"), "An external profile was lost.");
                            form.CaptureSandboxTabs(root);
                            File.WriteAllText(Path.Combine(root, "PASS.txt"),
                                "PASS: read-only opening with StartupFullscreen=True; authoritative Shared.ini 800x600; saved 2048x2048 and windowed mode in both INIs; " +
                                "eight weapons; left hand; unknown keys; backups; native interface shown for 1.8 s.\n", Encoding.UTF8);
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
