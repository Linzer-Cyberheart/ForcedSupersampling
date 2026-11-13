using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace ForcedUpscaling
{
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
        readonly string device;
        bool busy;
        string lastState = "";

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int ScalePercent { get; private set; }

        public Engine(Settings settings)
        {
            this.settings = settings;
            if (SynchronizationContext.Current == null)
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            ui = SynchronizationContext.Current;

            device = Display.Primary();
            Width = settings.GetInt("w", 3840);
            Height = settings.GetInt("h", 2160);
            ScalePercent = settings.GetInt("scalePct", 100);

            // If we died while the scale was changed, put it back now.
            if (device != null && !IsOn) RestoreScale();

            lastState = StateKey();
            poll.Interval = 2000;
            poll.Tick += delegate
            {
                string now = StateKey();
                if (now != lastState) { lastState = now; Raise(); }
            };
            poll.Start();
        }

        public bool HasDisplay { get { return device != null; } }
        public bool Busy { get { return busy; } }

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
            return now.Width + "x" + now.Height + "@" + now.Hz;
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
            Run(delegate { DoTurnOn(); });
        }

        public void TurnOff(bool announce)
        {
            Run(delegate { DoTurnOff(announce); });
        }

        public void SetResolution(int width, int height)
        {
            Width = width;
            Height = height;
            settings.Set("w", width.ToString());
            settings.Set("h", height.ToString());
            if (IsOn)
                Run(delegate { DoTurnOff(false); DoTurnOn(); });
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

        void DoTurnOn()
        {
            VideoMode saved = Display.Saved(device);
            int min, current, max;
            bool canScale = DpiScale.TryGet(device, out min, out current, out max);

            int code = Display.Apply(device, Width, Height, saved.Hz);
            if (code != 0)
            {
                Tell(Loc.T("fail_title"), Loc.F("fail", Width, Height, code), true);
                return;
            }

            Thread.Sleep(SettleMs);
            if (canScale) settings.Set("pendingDpi", current.ToString());
            if (ScalePercent != 0) ApplyScale();
            Tell(Loc.T("on_title"), Loc.F("on_text", Width, Height), false);
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
            if (!DpiScale.TryGet(device, out min, out current, out max)) return;
            DpiScale.Set(device, ScalePercent == 0 ? 0 : RelativeStep(ScalePercent, min, max));
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
                DpiScale.Set(device, relative);
                settings.Remove("pendingDpi");
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
