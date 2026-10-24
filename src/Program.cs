using System;
using System.Threading;
using System.Windows.Forms;

namespace Gili
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\GiliDynamicIsland", out created))
            {
                if (!created) return; // already running

                try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } // per-monitor v2
                catch (EntryPointNotFoundException) { }

                Application.ThreadException += (s, e) => Log.Write("ui: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("fatal: " + e.ExceptionObject);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new IslandForm());
            }
        }
    }
}
