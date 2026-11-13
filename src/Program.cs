using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace ForcedUpscaling
{
    static class App
    {
        public const string Version = "1.0.0";
        public const string SupportUrl = "https://picpay.me/ilinzer";
        public const string ChannelUrl = "https://www.youtube.com/@iLinzer?sub_confirmation=1";
        public const string RepoUrl = "https://github.com/Linzer-Cyberheart/ForcedUpscaling";

        public static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write(ex); }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            using (Mutex mutex = new Mutex(true, @"Local\ForcedUpscaling.Instance", out first))
            {
                EventWaitHandle openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ForcedUpscaling.Open");

                // Already running: ask that copy to show its window and quit.
                if (!first)
                {
                    openSignal.Set();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write(e.Exception); };

                Settings settings = new Settings();
                Loc.Set(settings.Get("lang", Loc.Detect()));

                // Started by Windows at logon: stay in the tray, don't open the window.
                bool background = Array.IndexOf(args, "--background") >= 0;
                Application.Run(new TrayApp(settings, openSignal, !background));
            }
        }
    }
}
