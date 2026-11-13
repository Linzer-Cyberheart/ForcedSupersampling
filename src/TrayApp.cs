using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ForcedUpscaling
{
    static class Icons
    {
        // The .ico files carry several sizes; ask for the one we actually need
        // so the tray icon isn't a blurry downscale.
        public static Icon Load(bool on, int size)
        {
            using (System.IO.Stream s = Gfx.Resource(on ? "fu_on.ico" : "fu_off.ico"))
                return new Icon(s, new Size(size, size));
        }
    }

    class TrayApp : ApplicationContext
    {
        readonly Settings settings;
        readonly Engine engine;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly SynchronizationContext ui;
        readonly Icon iconOn;
        readonly Icon iconOff;

        ToolStripMenuItem miOpen, miInfo, miToggle, miMonitor, miRes, miScale, miAuto, miSupport, miExit;
        MainWindow window;

        public TrayApp(Settings settings, EventWaitHandle openSignal, bool showWindow)
        {
            this.settings = settings;
            engine = new Engine(settings);
            ui = SynchronizationContext.Current;

            Size small = SystemInformation.SmallIconSize;
            iconOn = Icons.Load(true, small.Width);
            iconOff = Icons.Load(false, small.Width);

            BuildMenu();
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { engine.Toggle(); };
            tray.Visible = true;

            engine.Changed += delegate { UpdateTray(); };
            engine.LanguageChanged += delegate { UpdateTray(); };
            engine.Notice += delegate(string title, string text, bool error)
            {
                tray.ShowBalloonTip(error ? 9000 : 4000, title, text, error ? ToolTipIcon.Warning : ToolTipIcon.Info);
            };

            // a second launch pokes us through this event
            ThreadPool.RegisterWaitForSingleObject(openSignal, delegate(object state, bool timedOut)
            {
                ui.Post(delegate(object o) { ShowWindow(); }, null);
            }, null, Timeout.Infinite, false);

            // keep the Run entry pointing at wherever the exe lives now
            if (Autostart.IsOn) Autostart.Set(true);

            Application.ApplicationExit += delegate { engine.Shutdown(); };
            Microsoft.Win32.SystemEvents.SessionEnding += delegate { engine.Shutdown(); };

            UpdateTray();
            if (showWindow) ShowWindow();
        }

        void BuildMenu()
        {
            miOpen = new ToolStripMenuItem();
            miOpen.Click += delegate { ShowWindow(); };

            miInfo = new ToolStripMenuItem();
            miInfo.Enabled = false;
            miInfo.Font = new Font(menu.Font, FontStyle.Bold);

            miToggle = new ToolStripMenuItem();
            miToggle.Click += delegate { engine.Toggle(); };

            miMonitor = new ToolStripMenuItem();
            miRes = new ToolStripMenuItem();
            miScale = new ToolStripMenuItem();

            miAuto = new ToolStripMenuItem();
            miAuto.Click += delegate { Autostart.Set(!Autostart.IsOn); };

            miSupport = new ToolStripMenuItem();
            miSupport.Click += delegate { App.Open(App.SupportUrl); };

            miExit = new ToolStripMenuItem();
            miExit.Click += delegate { Quit(); };

            menu.Items.AddRange(new ToolStripItem[]
            {
                miOpen, new ToolStripSeparator(),
                miInfo, miToggle, miMonitor, miRes, miScale, new ToolStripSeparator(),
                miAuto, miSupport, new ToolStripSeparator(),
                miExit
            });
            menu.Opening += delegate { RefreshMenu(); };
        }

        // Texts and submenus are rebuilt every time the menu opens, so language
        // and resolution changes always show up.
        void RefreshMenu()
        {
            miOpen.Text = Loc.T("open");
            miMonitor.Text = Loc.T("monitor");
            miRes.Text = Loc.T("res");
            miScale.Text = Loc.T("scale");
            miAuto.Text = Loc.T("autostart");
            miSupport.Text = Loc.T("support");
            miExit.Text = Loc.T("exit");

            bool on = engine.IsOn;
            if (engine.HasDisplay)
            {
                VideoMode now = engine.Now;
                miInfo.Text = Loc.F("resolution", now.Width, now.Height, now.Hz);
            }
            else miInfo.Text = Loc.T("no_monitor");

            miToggle.Checked = on;
            miToggle.Text = on ? Loc.T("disable") : Loc.T("enable");
            miToggle.Enabled = engine.HasDisplay && !engine.Busy;

            miMonitor.DropDownItems.Clear();
            System.Collections.Generic.List<MonitorInfo> monitors = engine.Monitors();
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorInfo mon = monitors[i];
                string label = (i + 1) + "  " + mon.Name + (mon.Primary ? "  (" + Loc.T("primary") + ")" : "");
                ToolStripMenuItem item = new ToolStripMenuItem(label);
                item.Checked = mon.Device == engine.Device;
                item.Click += delegate { engine.SetMonitor(mon.Device); };
                miMonitor.DropDownItems.Add(item);
            }

            miRes.DropDownItems.Clear();
            System.Collections.Generic.List<VideoMode> modes = engine.Modes();
            VideoMode saved = engine.HasDisplay ? engine.Saved : null;
            foreach (VideoMode m in modes)
            {
                VideoMode mode = m;
                ToolStripMenuItem item = new ToolStripMenuItem(m.Width + " × " + m.Height + "   (" + Engine.Factor(saved, m) + ")");
                item.Checked = m.Width == engine.Width && m.Height == engine.Height;
                item.Click += delegate { engine.SetResolution(mode.Width, mode.Height); };
                miRes.DropDownItems.Add(item);
            }
            if (modes.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem(Loc.T("no_modes"));
                none.Enabled = false;
                miRes.DropDownItems.Add(none);
            }

            miScale.DropDownItems.Clear();
            foreach (int percent in Engine.ScaleChoices)
            {
                int p = percent;
                ToolStripMenuItem item = new ToolStripMenuItem(Engine.ScaleLabel(p));
                item.Checked = p == engine.ScalePercent;
                item.Click += delegate { engine.SetScale(p); };
                miScale.DropDownItems.Add(item);
            }

            miAuto.Checked = Autostart.IsOn;
        }

        void UpdateTray()
        {
            bool on = engine.IsOn;
            tray.Icon = on ? iconOn : iconOff;

            string tip = Loc.T("tip_off");
            if (on)
            {
                VideoMode now = engine.Now;
                tip = Loc.F("tip_on", now.Width, now.Height);
            }
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void ShowWindow()
        {
            if (window != null && !window.IsDisposed)
            {
                if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
                window.Activate();
                return;
            }

            window = new MainWindow(engine);
            window.FormClosed += delegate { window = null; };
            window.Show();
            window.Activate();
        }

        void Quit()
        {
            engine.Shutdown();
            tray.Visible = false;
            tray.Dispose();
            if (window != null && !window.IsDisposed) window.Close();
            ExitThread();
        }
    }
}
