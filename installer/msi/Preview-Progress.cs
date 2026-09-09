using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Developer-only, read-only MSI dialog preview. Never calls an installation
// API or an installer sequence; the preview closes itself after 60 seconds.
internal static class ProgressPreview
{
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string path, IntPtr mode, out uint database);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiEnableUIPreview(uint database, out uint preview);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiPreviewDialogW(uint preview, string dialog);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(uint handle);

    [STAThread]
    private static int Main()
    {
        uint database = 0, preview = 0;
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "progress-preview.msi");
            uint result = MsiOpenDatabaseW(path, IntPtr.Zero, out database);
            if (result != 0) return (int)result;
            result = MsiEnableUIPreview(database, out preview);
            if (result != 0) return (int)result;
            result = MsiPreviewDialogW(preview, "BioShockProgressDlg");
            if (result != 0) return (int)result;
            using (Timer timer = new Timer())
            {
                timer.Interval = 60000;
                timer.Tick += delegate { Application.ExitThread(); };
                timer.Start();
                Application.Run();
            }
            return 0;
        }
        finally
        {
            if (preview != 0) MsiCloseHandle(preview);
            if (database != 0) MsiCloseHandle(database);
        }
    }
}
