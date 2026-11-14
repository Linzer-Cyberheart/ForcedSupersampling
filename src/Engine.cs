using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ForcedUpscaling
{
    // Tells whether a full screen window (a game) is in front on a monitor.
    static class Foreground
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr window, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr window, int index);

        const int Style = -16;
        const int Caption = 0xC00000;

        // Borderless and at least as big as the monitor. The desktop, the taskbar and
        // this program's own windows do not count.
        public static bool CoversMonitor(string device)
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return false;

                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) return false;

                StringBuilder cls = new StringBuilder(64);
                GetClassNameW(h, cls, cls.Capacity);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
                if ((GetWindowLong(h, Style) & Caption) == Caption) return false;   // a normal window, even if maximized

                Rectangle b = Screen.PrimaryScreen.Bounds;
                foreach (Screen s in Screen.AllScreens)
                    if (s.DeviceName == device) b = s.Bounds;

                RECT r;
                if (!GetWindowRect(h, out r)) return false;
                return r.Left <= b.Left + 2 && r.Top <= b.Top + 2 && r.Right >= b.Right - 2 && r.Bottom >= b.Bottom - 2;
            }
            catch (Exception ex) { Log.Write(ex); return false; }
        }
    }

    // Holds the state shared by the tray menu and the window: which resolution
    // is selected, whether it is on, and does the actual switching.
    class Engine
    {
        public static readonly int[] ScaleChoices = { 100, 125, 150, 175, 200, 250, 300, 0 };
        static readonly int[] ScaleSteps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

        // Windows needs a moment to settle after a mode change
        const int SettleMs = 1500;

        public event EventHandler Changed;
        public event EventHandler LanguageChanged;
        public event Action<string, string, bool> Notice;

        readonly Settings settings;
        readonly SynchronizationContext ui;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        string device;
        bool busy;
        string lastState = "";

        // What the user last asked for. When something else (a game that exits or is minimized)
        // drops the mode, it is put back as long as this says "on".
        bool wanted;
        int missedPolls;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int ScalePercent { get; private set; }

        public Engine(Settings settings)
        {
            this.settings = settings;
            if (SynchronizationContext.Current == null)
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            ui = SynchronizationContext.Current;

            device = PickDevice(settings.Get("monitor", ""));
            Width = settings.GetInt("w", 3840);
            Height = settings.GetInt("h", 2160);
            ScalePercent = settings.GetInt("scalePct", 100);

            // If we died while the scale was changed, put it back now.
            if (device != null && !IsOn) RestoreScale();

            lastState = StateKey();
            wanted = device != null && IsOn;
            poll.Interval = 2000;
            poll.Tick += delegate
            {
                string now = StateKey();
                if (now != lastState) { lastState = now; Raise(); }
                KeepOn();
            };
            poll.Start();
        }

        // The mode is only dropped by the user or by us. If a game that was on screen exits or is
        // minimized, Windows resets the display; put it back after a couple of polls, and never
        // while a full screen window is in front, because then that game owns the display mode.
        void KeepOn()
        {
            if (!wanted || busy || device == null) { missedPolls = 0; return; }
            if (IsOn) { missedPolls = 0; return; }
            if (++missedPolls < 2) return;
            if (Foreground.CoversMonitor(device)) return;
            missedPolls = 0;
            Run(delegate { DoTurnOn(true); });
        }

        public Settings Store { get { return settings; } }
        public bool HasDisplay { get { return device != null; } }
        public bool Busy { get { return busy; } }
        public string Device { get { return device; } }

        public List<MonitorInfo> Monitors() { return DisplayConfig.Monitors(); }

        // the monitor we used last time, or the primary one if it's gone
        static string PickDevice(string wanted)
        {
            if (wanted != "")
                foreach (MonitorInfo m in DisplayConfig.Monitors())
                    if (m.Device == wanted) return wanted;
            return Display.Primary();
        }

        public VideoMode Now { get { return Display.Current(device); } }
        public VideoMode Saved { get { return Display.Saved(device); } }

        public bool IsOn
        {
            get
            {
                if (device == null) return false;
                VideoMode now = Display.Current(device);
                VideoMode saved = Display.Saved(device);
                return now.Width != saved.Width || now.Height != saved.Height;
            }
        }

        string StateKey()
        {
            if (device == null) return "none";
            VideoMode now = Display.Current(device);
            return device + ":" + now.Width + "x" + now.Height + "@" + now.Hz;
        }

        // DSR resolutions on offer. Falls back to the biggest one if the stored
        // choice does not exist on this monitor.
        public List<VideoMode> Modes()
        {
            if (device == null) return new List<VideoMode>();
            List<VideoMode> list = Display.Larger(device, Display.Saved(device));
            bool known = list.Exists(delegate(VideoMode m) { return m.Width == Width && m.Height == Height; });
            if (!known && list.Count > 0)
            {
                Width = list[0].Width;
                Height = list[0].Height;
            }
            return list;
        }

        public static string ScaleLabel(int percent)
        {
            if (percent == 0) return Loc.T("scale_auto");
            if (percent == 100) return "100%   (" + Loc.T("recommended") + ")";
            return percent + "%";
        }

        public static string Factor(VideoMode saved, VideoMode m)
        {
            double f = (double)m.Width * m.Height / ((double)saved.Width * saved.Height);
            return f.ToString("0.00", CultureInfo.InvariantCulture) + "x";
        }

        public void Toggle()
        {
            if (IsOn) TurnOff(true); else TurnOn();
        }

        public void TurnOn()
        {
            wanted = true;
            Run(delegate { DoTurnOn(false); });
        }

        public void TurnOff(bool announce)
        {
            wanted = false;
            Run(delegate { DoTurnOff(announce); });
        }

        public void SetResolution(int width, int height)
        {
            Width = width;
            Height = height;
            settings.Set("w", width.ToString());
            settings.Set("h", height.ToString());
            if (IsOn)
                Run(delegate { DoTurnOff(false); DoTurnOn(false); });
            else
                Raise();
        }

        public void SetScale(int percent)
        {
            ScalePercent = percent;
            settings.Set("scalePct", percent.ToString());
            if (IsOn) ApplyScale();
            Raise();
        }

        // Switching monitors turns the mode off on the old one first. It does
        // not turn it on for the new one, that stays up to the user.
        public void SetMonitor(string name)
        {
            if (name == device || busy) return;
            wanted = false;
            if (IsOn)
                Run(delegate
                {
                    DoTurnOff(false);
                    ui.Post(delegate { ChooseMonitor(name); }, null);
                });
            else
                ChooseMonitor(name);
        }

        void ChooseMonitor(string name)
        {
            device = name;
            settings.Set("monitor", name);
            lastState = StateKey();
            Raise();
        }

        public void SetLanguage(string code)
        {
            Loc.Set(code);
            settings.Set("lang", code);
            if (LanguageChanged != null) LanguageChanged(this, EventArgs.Empty);
        }

        // Called on exit so we never leave the monitor stuck in a bigger mode.
        public void Shutdown()
        {
            poll.Stop();
            if (device != null && IsOn)
            {
                Display.Reset();
                RestoreScale();
            }
        }

        void DoTurnOn(bool quiet)
        {
            VideoMode saved = Display.Saved(device);
            int min, current, max;
            bool canScale = DisplayConfig.TryGetScale(device, out min, out current, out max);

            int code = Display.Apply(device, Width, Height, saved.Hz);
            if (code != 0)
            {
                if (!quiet) Tell(Loc.T("fail_title"), Loc.F("fail", Width, Height, code), true);
                return;
            }

            Thread.Sleep(SettleMs);
            if (canScale)
            {
                settings.Set("pendingDpi", current.ToString());
                settings.Set("pendingDevice", device);
            }
            if (ScalePercent != 0) ApplyScale();
            if (!quiet) Tell(Loc.T("on_title"), Loc.F("on_text", Width, Height), false);
        }

        void DoTurnOff(bool announce)
        {
            Display.Reset();
            Thread.Sleep(SettleMs);
            RestoreScale();
            if (announce)
            {
                VideoMode now = Display.Current(device);
                Tell(Loc.T("off_title"), Loc.F("off_text", now.Width, now.Height), false);
            }
        }

        void ApplyScale()
        {
            int min, current, max;
            if (!DisplayConfig.TryGetScale(device, out min, out current, out max)) return;
            DisplayConfig.SetScale(device, ScalePercent == 0 ? 0 : RelativeStep(ScalePercent, min, max));
        }

        // Windows counts steps from the recommended scale; the lowest step is 100%.
        static int RelativeStep(int percent, int min, int max)
        {
            int recommended = -min;
            int index = Array.IndexOf(ScaleSteps, percent);
            if (index < 0) return 0;
            return Math.Max(min, Math.Min(max, index - recommended));
        }

        void RestoreScale()
        {
            string stored = settings.Get("pendingDpi", "");
            int relative;
            if (stored != "" && int.TryParse(stored, out relative))
            {
                // put it back on the monitor we changed, even if another one is selected now
                DisplayConfig.SetScale(settings.Get("pendingDevice", device), relative);
                settings.Remove("pendingDpi");
                settings.Remove("pendingDevice");
            }
        }

        void Run(ThreadStart work)
        {
            if (busy || device == null) return;
            busy = true;
            Raise();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { work(); }
                catch (Exception ex) { Log.Write(ex); }
                ui.Post(delegate { busy = false; lastState = StateKey(); Raise(); }, null);
            });
        }

        void Tell(string title, string text, bool error)
        {
            ui.Post(delegate { if (Notice != null) Notice(title, text, error); }, null);
        }

        void Raise()
        {
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
    }
}
