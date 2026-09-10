using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("BioShock 1-2 VR - DLSS/DLAA")]
[assembly: AssemblyVersion("0.2.13.0")]
[assembly: AssemblyFileVersion("0.2.13.0")]
namespace BioShockBundle
{
    internal sealed class Entry
    {
        internal string Id, Label, Resource, Hash;
        internal Entry(string id, string label, string resource, string hash)
        { Id=id; Label=label; Resource=resource; Hash=hash; }
        public override string ToString() { return Label; }
    }
    internal static class Packages
    {
        internal static string Hash(Stream stream)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        internal static void Verify(Entry entry)
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(entry.Resource))
            {
                if (source == null || Hash(source) != entry.Hash)
                    throw new InvalidDataException("El paquete de " + entry.Label + " no coincide con su huella verificada.");
            }
        }
        internal static string Extract(Entry entry, string directory)
        {
            Verify(entry);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, entry.Id + ".msi");
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(entry.Resource))
            using (FileStream target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { source.CopyTo(target); target.Flush(true); }
            using (FileStream check = File.OpenRead(path))
                if (Hash(check) != entry.Hash) throw new IOException("No se ha extraído correctamente el instalador.");
            return path;
        }
    }
    internal sealed class Selector : Form
    {
        readonly ComboBox games = new ComboBox();
        readonly Button install = new Button();
        readonly Label status = new Label();
        readonly Timer timer = new Timer();
        Process child;
        string logPath;
        readonly bool test;
        internal Selector(bool testMode)
        {
            test = testMode;
            Text = "BioShock 1–2 VR · DLSS/DLAA 0.2.13";
            ClientSize = new Size(540, 335);
            MinimumSize = MaximumSize = new Size(556, 374);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            Controls.Add(new Label { Text="BioShock 1–2 VR · DLSS/DLAA", Font=new Font(Font.FontFamily,15,FontStyle.Bold), Bounds=new Rectangle(24,22,492,32) });
            Controls.Add(new Label { Text="0.2.13 · Candidato de desarrollo, no publicado", ForeColor=Color.DarkGoldenrod, Bounds=new Rectangle(24,60,492,24) });
            Controls.Add(new Label { Text="Elige el juego que quieres instalar o mantener:", Bounds=new Rectangle(24,103,492,22) });
            games.DropDownStyle=ComboBoxStyle.DropDownList;
            games.Bounds=new Rectangle(24,133,492,30);
            games.Items.AddRange(Catalog.Items); games.SelectedIndex=0; Controls.Add(games);
            Controls.Add(new Label { Text="Puedes tener ambos mods instalados. Usa este mismo instalador una vez para cada juego; sus ajustes y copias son independientes.", Bounds=new Rectangle(24,179,492,44) });
            install.Text="Continuar"; install.Bounds=new Rectangle(383,245,133,32);
            install.Enabled=!test; install.Click+=Begin; Controls.Add(install);
            status.Text="El asistente del juego elegido confirmará la carpeta y la operación.";
            status.Bounds=new Rectangle(24,288,492,40); Controls.Add(status);
            timer.Interval=500; timer.Tick+=Observe;
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {
                if (child != null) {
                    e.Cancel=true;
                    status.Text="Termina o cancela primero el asistente de instalación abierto.";
                }
            };
            FormClosed+=delegate { timer.Dispose(); };
        }
        void Begin(object sender, EventArgs e)
        {
            if (child != null || test) return;
            Entry entry=(Entry)games.SelectedItem;
            if (entry.Id=="bs2" && MessageBox.Show(this,
                "BioShock 2 todavía requiere validación en visor. Este es un candidato de desarrollo, no una versión estable.\r\n\r\n¿Quieres continuar?",
                "Candidato de BioShock 2", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)!=DialogResult.Yes) return;
            games.Enabled=install.Enabled=false;
            try {
                string directory=Path.Combine(Path.GetTempPath(),"Bvr12-"+Guid.NewGuid().ToString("N"));
                string msi=Packages.Extract(entry,directory);
                logPath=Path.Combine(directory,"installation.log");
                var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),
                    "/i \"" + msi + "\" /norestart /l*v \"" + logPath + "\"");
                info.UseShellExecute=false;
                child=Process.Start(info);
                if (child==null) throw new IOException("Windows no devolvió el proceso del instalador.");
                timer.Start(); status.Text="Asistente de " + entry.Label + " abierto. No se inicia el juego.";
            } catch(Exception ex) {
                games.Enabled=install.Enabled=true;
                MessageBox.Show(this,ex.Message,"No se pudo abrir el instalador",MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
        }
        void Observe(object sender, EventArgs e)
        {
            if (child==null || !child.HasExited) return;
            int code=child.ExitCode; child.Dispose(); child=null; timer.Stop();
            games.Enabled=install.Enabled=true;
            status.Text=code==0 ? "Operación completada. Puedes elegir el otro juego o cerrar."
                : code==3010 ? "Operación completada. Windows recomienda reiniciar; no se reinicia automáticamente."
                : code==1602 ? "Operación cancelada. Puedes intentarlo de nuevo."
                : "El instalador devolvió el código " + code + ". Consulta el registro.";
            if(code!=0 && code!=3010 && code!=1602)
                MessageBox.Show(this,status.Text+"\r\n\r\n"+logPath,"Instalación no confirmada",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        internal void Preview(string path)
        {
            ShowInTaskbar=false; Opacity=0; Show(); Application.DoEvents();
            using(Bitmap bitmap=new Bitmap(Width,Height)) {
                DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size)); bitmap.Save(Path.GetFullPath(path));
            }
        }
    }
    internal static class Program
    {
        [STAThread] static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try {
                if(args.Length==2 && args[0]=="--verify") {
                    foreach(Entry entry in Catalog.Items) Packages.Verify(entry);
                    File.WriteAllText(Path.GetFullPath(args[1]),"PASS: 2/2 MSI integrados, SHA-256 verificado.\r\n",new UTF8Encoding(false));
                    return 0;
                }
                if(args.Length==2 && args[0]=="--preview") {
                    using(var form=new Selector(true)) form.Preview(args[1]);
                    return 0;
                }
                if(args.Length==1 && args[0]=="--window-test") {
                    using(var form=new Selector(true)) using(var close=new Timer()) {
                        close.Interval=2500; close.Tick+=delegate { close.Stop(); form.Close(); };
                        form.Shown+=delegate { close.Start(); }; Application.Run(form);
                    }
                    return 0;
                }
                if(args.Length!=0) return 2;
                Application.Run(new Selector(false)); return 0;
            } catch(Exception ex) {
                if(args.Length==0) MessageBox.Show(ex.Message,"BioShock 1–2 VR",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 2;
            }
        }
    }
}
