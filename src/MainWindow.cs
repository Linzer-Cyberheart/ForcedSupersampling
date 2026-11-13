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
        const int H = 490;
        const int PanelTop = 70;
        const int Pad = 24;
        const int CardTop = 130;
        const int CardHeight = 76;
        const int RowsTop = 230;
        const int RowGap = 44;
        const int DividerY = 418;
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
        readonly SelectBox resBox;
        readonly SelectBox scaleBox;
        readonly SelectBox langBox;
        readonly CaptionButton minButton;
        readonly CaptionButton closeButton;
        readonly TextLink repoLink;
        readonly TextLink byLink;
        readonly ToolTip tips = new ToolTip();
        readonly Icon iconOn;
        readonly Icon iconOff;

        Font textFont, boldFont, bigFont, smallFont, supportFont;
        List<VideoMode> modes = new List<VideoMode>();
        VideoMode now = new VideoMode();
        bool isOn;

        public MainWindow(Engine engine)
        {
            this.engine = engine;

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
            startup.Location = new Point(W - Pad - 48, RowsTop + 3 * RowGap + 4);
            startup.Checked = Autostart.IsOn;

            resBox = NewSelect(0);
            scaleBox = NewSelect(1);
            langBox = NewSelect(2);

            minButton = new CaptionButton(CaptionKind.Minimize);
            minButton.Location = new Point(W - 1 - 92, PanelTop + 3);
            closeButton = new CaptionButton(CaptionKind.Close);
            closeButton.Location = new Point(W - 1 - 46, PanelTop + 3);

            repoLink = new TextLink();
            byLink = new TextLink();

            Controls.AddRange(new Control[] { minButton, closeButton, power, startup, resBox, scaleBox, langBox, support, repoLink, byLink, mascot });

            power.UserToggled += delegate { engine.Toggle(); };
            startup.UserToggled += delegate { Autostart.Set(startup.Checked); };
            resBox.SelectionChanged += delegate
            {
                int i = resBox.SelectedIndex;
                if (i >= 0 && i < modes.Count) engine.SetResolution(modes[i].Width, modes[i].Height);
            };
            scaleBox.SelectionChanged += delegate { engine.SetScale(Engine.ScaleChoices[scaleBox.SelectedIndex]); };
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

        SelectBox NewSelect(int row)
        {
            SelectBox box = new SelectBox();
            box.BackColor = Theme.Window;
            box.Bounds = new Rectangle(W - Pad - SelectWidth, RowsTop + row * RowGap, SelectWidth, 34);
            return box;
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

            resBox.Font = scaleBox.Font = langBox.Font = textFont;
            repoLink.Font = byLink.Font = smallFont;
            support.Font = supportFont;

            // the digger stands on the bottom edge of the window
            support.Label = Loc.T("support").ToUpperInvariant();
            support.Location = new Point(Pad - 18, H - 1 - support.Height);
            repoLink.SetText("GitHub  ·  v" + App.Version);
            repoLink.Location = new Point(W - Pad - repoLink.Width, 464);
            byLink.SetText(Loc.T("by"));
            byLink.Location = new Point(W - Pad - byLink.Width, 441);

            tips.SetToolTip(minButton, Loc.T("minimize"));
            tips.SetToolTip(closeButton, Loc.T("close"));

            langBox.SetItems(Loc.Names, Array.IndexOf(Loc.Codes, Loc.Current));
            FillScale();
            FillResolutions();

            foreach (Font f in old) if (f != null) f.Dispose();
            Invalidate();
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
                int p = Engine.ScaleChoices[i];
                items[i] = p == 0 ? Loc.T("scale_auto") : p + "%";
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

            string[] labels = { Loc.T("monitor_res"), Loc.T("scale"), Loc.T("language"), Loc.T("autostart") };
            for (int i = 0; i < labels.Length; i++)
                TextRenderer.DrawText(g, labels[i], textFont, new Rectangle(Pad, RowsTop + i * RowGap, LabelWidth, 34), Theme.Text, Gfx.Line);

            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, Pad, DividerY, W - Pad, DividerY);
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
