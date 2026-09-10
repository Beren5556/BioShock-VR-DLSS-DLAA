using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Read-only Windows Installer preview API: no installer session or sequence.
internal static class DialogPreview
{
    [DllImport("msi.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
    private static extern uint MsiOpenDatabaseW(string path, IntPtr mode, out uint database);
    [DllImport("msi.dll", ExactSpelling=true)]
    private static extern uint MsiEnableUIPreview(uint database, out uint preview);
    [DllImport("msi.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
    private static extern uint MsiPreviewDialogW(uint preview, string dialog);
    [DllImport("msi.dll", ExactSpelling=true)]
    private static extern uint MsiCloseHandle(uint handle);
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        uint database=0, preview=0;
        try
        {
            uint result=MsiOpenDatabaseW(args[0], IntPtr.Zero, out database);
            if (result != 0) return (int)result;
            result=MsiEnableUIPreview(database, out preview);
            if (result != 0) return (int)result;
            result=MsiPreviewDialogW(preview, args[1]);
            if (result != 0) return (int)result;
            using (Timer timer=new Timer())
            {
                timer.Interval=45000; timer.Tick+=delegate { Application.ExitThread(); };
                timer.Start(); Application.Run();
            }
            return 0;
        }
        finally { if(preview!=0) MsiCloseHandle(preview); if(database!=0) MsiCloseHandle(database); }
    }
}
