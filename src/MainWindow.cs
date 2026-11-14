using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ForcedUpscaling
{
    // Borderless window. The top strip is see-through so the mascot can sit on
    // the edge of the panel; everything below PanelTop is the actual window.
    class MainWindow : Form
    {
        const int W = 384;
        const int PanelTop = 70;
        const int Pad = 24;
        const int CardTop = 130;
        const int CardHeight = 76;
        const int RowsTop = 230;
        const int RowGap = 44;
        const int FooterGap = 12;      // last row to the divider
        const int FooterHeight = 72;   // divider to the bottom edge
        const int LabelWidth = 146;
        const int SelectWidth = 186;

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        readonly Engine engine;
        readonly SpriteBox mascot;
        readonly SupportButton support;
        readonly ToggleSwitch power;
        readonly ToggleSwitch startup;
        readonly SelectBox monitorBox;
        readonly SelectBox resBox;
        readonly SelectBox scaleBox;
        readonly SelectBox gameBox;
        readonly SelectBox langBox;
        readonly List<SelectBox> optionBoxes = new List<SelectBox>();
        readonly CaptionButton minButton;
        readonly CaptionButton closeButton;
        readonly TextLink repoLink;
        readonly TextLink byLink;
        readonly ToolTip tips = new ToolTip();
        readonly Icon iconOn;
        readonly Icon iconOff;

        Font textFont, boldFont, bigFont, smallFont, supportFont;
        List<MonitorInfo> monitors = new List<MonitorInfo>();
        List<VideoMode> modes = new List<VideoMode>();
        VideoMode now = new VideoMode();
        bool isOn;
        GameProfile profile;   // the game picked in the list, null for the generic mode
        int H, dividerY;

        public MainWindow(Engine engine)
        {
            this.engine = engine;
            profile = FindProfile(engine.Store.Get("game", ""));
            ComputeHeight();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(W, H);
            BackColor = Theme.Key;
            TransparencyKey = Theme.Key;
            DoubleBuffered = true;
            ShowInTaskbar = true;
            Text = "Forced Upscaling";
            iconOn = Icons.Load(true, 32);
            iconOff = Icons.Load(false, 32);

            // The hands in the artwork end 65px below the top of the sprite. Going
            // 3px lower than the panel edge puts them over the colored strip
            // instead of above it.
            mascot = new SpriteBox(GifSprite.Load("iLinzer_Sit.gif", 5));
            mascot.Location = new Point(26, PanelTop - 62);
            mascot.Playing = true;

            support = new SupportButton(GifSprite.Load("iLinzer_Cavando.gif", 3));

            power = new ToggleSwitch(56, 30);
            power.BackColor = Theme.Card;
            power.Location = new Point(W - Pad - 20 - 56, CardTop + (CardHeight - 30) / 2);

            startup = new ToggleSwitch(48, 26);
            startup.BackColor = Theme.Window;
            startup.Left = W - Pad - 48;
            startup.Checked = Autostart.IsOn;

            monitorBox = NewSelect();
            resBox = NewSelect();
            scaleBox = NewSelect();
            gameBox = NewSelect();
            langBox = NewSelect();

            minButton = new CaptionButton(CaptionKind.Minimize);
            minButton.Location = new Point(W - 1 - 92, PanelTop + 3);
            closeButton = new CaptionButton(CaptionKind.Close);
            closeButton.Location = new Point(W - 1 - 46, PanelTop + 3);

            repoLink = new TextLink();
            byLink = new TextLink();

            Controls.AddRange(new Control[] { minButton, closeButton, power, startup, monitorBox, resBox, scaleBox, gameBox, langBox, support, repoLink, byLink, mascot });

            power.UserToggled += delegate { engine.Toggle(); };
            startup.UserToggled += delegate { Autostart.Set(startup.Checked); };
            monitorBox.SelectionChanged += delegate
            {
                int i = monitorBox.SelectedIndex;
                if (i >= 0 && i < monitors.Count) engine.SetMonitor(monitors[i].Device);
            };
            resBox.SelectionChanged += delegate
            {
                int i = resBox.SelectedIndex;
                if (i >= 0 && i < modes.Count) engine.SetResolution(modes[i].Width, modes[i].Height);
            };
            scaleBox.SelectionChanged += delegate { engine.SetScale(Engine.ScaleChoices[scaleBox.SelectedIndex]); };
            gameBox.SelectionChanged += delegate { ChooseGame(gameBox.SelectedIndex); };
            langBox.SelectionChanged += delegate { engine.SetLanguage(Loc.Codes[langBox.SelectedIndex]); };
            support.Click += delegate { App.Open(App.SupportUrl); };
            repoLink.Click += delegate { App.Open(App.RepoUrl); };
            byLink.Click += delegate { App.Open(App.ChannelUrl); };
            minButton.Click += delegate { WindowState = FormWindowState.Minimized; };
            closeButton.Click += delegate { Close(); };

            engine.Changed += OnEngineChanged;
            engine.LanguageChanged += OnLanguageChanged;

            ApplyLanguage();
            RefreshState();
        }

        // rows from the top: monitor, resolution, scale, game, the game's options, language, autostart
        int OptionCount { get { return profile == null ? 0 : profile.Options.Length; } }

        static int RowY(int row) { return RowsTop + row * RowGap; }

        static GameProfile FindProfile(string id)
        {
            foreach (GameProfile p in Games.All) if (p.Id == id) return p;
            return null;
        }

        void ComputeHeight()
        {
            int rows = 6 + OptionCount;
            dividerY = RowY(rows) + FooterGap;
            H = dividerY + FooterHeight;
        }

        SelectBox NewSelect()
        {
            SelectBox box = new SelectBox();
            box.BackColor = Theme.Window;
            box.Size = new Size(SelectWidth, 34);
            box.Left = W - Pad - SelectWidth;
            return box;
        }

        void PlaceControls()
        {
            int options = OptionCount;
            monitorBox.Top = RowY(0);
            resBox.Top = RowY(1);
            scaleBox.Top = RowY(2);
            gameBox.Top = RowY(3);
            for (int i = 0; i < optionBoxes.Count; i++) optionBoxes[i].Top = RowY(4 + i);
            langBox.Top = RowY(4 + options);
            startup.Top = RowY(5 + options) + 4;

            // the digger stands on the bottom edge of the window
            support.Location = new Point(Pad - 18, H - 1 - support.Height);
            repoLink.Location = new Point(W - Pad - repoLink.Width, dividerY + 46);
            byLink.Location = new Point(W - Pad - byLink.Width, dividerY + 23);
        }

        void FillGames()
        {
            string[] items = new string[Games.All.Length + 1];
            items[0] = Loc.T("game_all");
            int selected = 0;
            for (int i = 0; i < Games.All.Length; i++)
            {
                items[i + 1] = Games.All[i].Name;
                if (Games.All[i] == profile) selected = i + 1;
            }
            gameBox.SetItems(items, selected);
        }

        // One drop-down per option of the picked game, rebuilt when the game changes.
        void BuildOptions()
        {
            foreach (SelectBox old in optionBoxes)
            {
                Controls.Remove(old);
                old.Dispose();
            }
            optionBoxes.Clear();
            if (profile == null) return;

            foreach (GameOption option in profile.Options)
            {
                GameOption opt = option;
                GameProfile owner = profile;
                SelectBox box = NewSelect();
                box.Font = textFont;
                box.SelectionChanged += delegate { engine.Store.Set(owner.SettingKey(opt), box.SelectedIndex.ToString()); };
                Controls.Add(box);
                optionBoxes.Add(box);
            }
            FillOptions();
        }

        void FillOptions()
        {
            for (int i = 0; i < optionBoxes.Count; i++)
            {
                GameOption option = profile.Options[i];
                string[] items = new string[option.ChoiceKeys.Length];
                for (int j = 0; j < items.Length; j++) items[j] = Loc.T(option.ChoiceKeys[j]);
                int saved = engine.Store.GetInt(profile.SettingKey(option), option.Default);
                optionBoxes[i].SetItems(items, saved < 0 || saved >= items.Length ? option.Default : saved);
            }
        }

        void ChooseGame(int index)
        {
            profile = index <= 0 ? null : Games.All[index - 1];
            engine.Store.Set("game", profile == null ? "" : profile.Id);
            BuildOptions();
            ComputeHeight();
            ClientSize = new Size(W, H);
            PlaceControls();
            Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x20000; // WS_MINIMIZEBOX, lets the taskbar button minimize it
                return cp;
            }
        }

        void OnEngineChanged(object sender, EventArgs e) { RefreshState(); }

        void OnLanguageChanged(object sender, EventArgs e) { ApplyLanguage(); }

        void ApplyLanguage()
        {
            string family = Loc.FontName;
            Font[] old = { textFont, boldFont, bigFont, smallFont, supportFont };

            textFont = new Font(family, 9.5f);
            boldFont = new Font(family, 15f, FontStyle.Bold);
            bigFont = new Font(family, 16f, FontStyle.Bold);
            smallFont = new Font(family, 9f);
            supportFont = new Font(family, 11f, FontStyle.Bold);

            monitorBox.Font = resBox.Font = scaleBox.Font = gameBox.Font = langBox.Font = textFont;
            foreach (SelectBox box in optionBoxes) box.Font = textFont;
            repoLink.Font = byLink.Font = smallFont;
            support.Font = supportFont;

            support.Label = Loc.T("support").ToUpperInvariant();
            repoLink.SetText("GitHub  ·  v" + App.Version);
            byLink.SetText(Loc.T("by"));

            tips.SetToolTip(minButton, Loc.T("minimize"));
            tips.SetToolTip(closeButton, Loc.T("close"));

            langBox.SetItems(Loc.Names, Array.IndexOf(Loc.Codes, Loc.Current));
            FillMonitors();
            FillScale();
            FillResolutions();
            FillGames();
            if (profile != null)
            {
                if (optionBoxes.Count == 0) BuildOptions();
                else FillOptions();
            }
            PlaceControls();

            foreach (Font f in old) if (f != null) f.Dispose();
            Invalidate();
        }

        void FillMonitors()
        {
            monitors = engine.Monitors();
            string[] items = new string[monitors.Count];
            int selected = 0;
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorInfo m = monitors[i];
                items[i] = (i + 1) + "  " + m.Name + (m.Primary ? "  (" + Loc.T("primary") + ")" : "");
                if (m.Device == engine.Device) selected = i;
            }
            monitorBox.SetItems(items, selected);
            monitorBox.Enabled = !engine.Busy;
        }

        void FillResolutions()
        {
            modes = engine.Modes();
            if (modes.Count == 0)
            {
                resBox.SetItems(new string[] { Loc.T("no_modes") }, 0);
                resBox.Enabled = false;
                return;
            }

            VideoMode saved = engine.Saved;
            string[] items = new string[modes.Count];
            int selected = 0;
            for (int i = 0; i < modes.Count; i++)
            {
                VideoMode m = modes[i];
                items[i] = m.Width + " × " + m.Height + "   (" + Engine.Factor(saved, m) + ")";
                if (m.Width == engine.Width && m.Height == engine.Height) selected = i;
            }
            resBox.SetItems(items, selected);
            resBox.Enabled = !engine.Busy;
        }

        void FillScale()
        {
            string[] items = new string[Engine.ScaleChoices.Length];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = Engine.ScaleLabel(Engine.ScaleChoices[i]);
            }
            scaleBox.SetItems(items, Array.IndexOf(Engine.ScaleChoices, engine.ScalePercent));
        }

        void RefreshState()
        {
            if (IsDisposed) return;
            isOn = engine.IsOn;
            now = engine.HasDisplay ? engine.Now : new VideoMode();

            power.Checked = isOn;
            power.Enabled = engine.HasDisplay && !engine.Busy;
            scaleBox.Enabled = !engine.Busy;
            FillMonitors();
            FillResolutions();

            Icon = isOn ? iconOn : iconOff;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // panel with a colored strip on top that follows the on/off state
            using (Brush b = new SolidBrush(Theme.Window)) g.FillRectangle(b, 0, PanelTop, W, H - PanelTop);
            using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, PanelTop, W - 1, H - PanelTop - 1);
            using (Brush b = new SolidBrush(isOn ? Theme.Accent : Theme.Off)) g.FillRectangle(b, 0, PanelTop, W, 3);

            TextRenderer.DrawText(g, "Forced Upscaling", boldFont, new Rectangle(0, PanelTop + 3, W, 45), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // status card
            Rectangle card = new Rectangle(Pad, CardTop, W - Pad * 2, CardHeight);
            using (Brush b = new SolidBrush(Theme.Card)) g.FillRectangle(b, card);
            using (Pen pen = new Pen(Theme.Border)) g.DrawRectangle(pen, card.X, card.Y, card.Width - 1, card.Height - 1);
            using (Brush b = new SolidBrush(isOn ? Theme.Accent : Theme.Off)) g.FillRectangle(b, card.X + 20, card.Y + 20, 10, 10);
            TextRenderer.DrawText(g, isOn ? Loc.T("on") : Loc.T("off"), bigFont,
                new Rectangle(card.X + 42, card.Y + 8, 200, 34), Theme.Text, Gfx.Line);

            string info = engine.HasDisplay ? Loc.F("resolution", now.Width, now.Height, now.Hz) : Loc.T("no_monitor");
            TextRenderer.DrawText(g, info, smallFont, new Rectangle(card.X + 20, card.Y + 44, 232, 22), Theme.Muted, Gfx.Line);

            List<string> labels = new List<string> { Loc.T("monitor"), Loc.T("res"), Loc.T("scale"), Loc.T("game") };
            if (profile != null)
                foreach (GameOption option in profile.Options) labels.Add(Loc.T(option.LabelKey));
            labels.Add(Loc.T("language"));
            labels.Add(Loc.T("autostart"));
            for (int i = 0; i < labels.Count; i++)
                TextRenderer.DrawText(g, labels[i], textFont, new Rectangle(Pad, RowY(i), LabelWidth, 34), Theme.Text, Gfx.Line);

            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, Pad, dividerY, W - Pad, dividerY);
        }

        // no title bar, so dragging anywhere on the panel moves the window
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y >= PanelTop)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
            }
            base.OnMouseDown(e);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (mascot != null) mascot.Playing = WindowState != FormWindowState.Minimized;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            engine.Changed -= OnEngineChanged;
            engine.LanguageChanged -= OnLanguageChanged;
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                Font[] fonts = { textFont, boldFont, bigFont, smallFont, supportFont };
                foreach (Font f in fonts) if (f != null) f.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
