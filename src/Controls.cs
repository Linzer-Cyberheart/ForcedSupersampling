using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace ForcedUpscaling
{
    static class Theme
    {
        // The window is painted on this color and then made see-through, which is
        // how the mascot can sit outside the panel.
        public static readonly Color Key = Color.FromArgb(255, 0, 255);

        public static readonly Color Window = Color.FromArgb(27, 29, 35);
        public static readonly Color Card = Color.FromArgb(36, 39, 47);
        public static readonly Color Hover = Color.FromArgb(47, 51, 61);
        public static readonly Color Border = Color.FromArgb(58, 63, 75);
        public static readonly Color Text = Color.FromArgb(232, 234, 238);
        public static readonly Color Muted = Color.FromArgb(145, 152, 166);
        public static readonly Color Accent = Color.FromArgb(46, 160, 67);
        public static readonly Color Off = Color.FromArgb(84, 92, 104);
        public static readonly Color Danger = Color.FromArgb(196, 43, 28);
        public static readonly Color Pink = Color.FromArgb(255, 79, 163);
    }

    static class Gfx
    {
        public static readonly TextFormatFlags Line =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        public static Stream Resource(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("ForcedUpscaling." + name);
        }
    }

    // A small pixel-art GIF, pre-scaled with nearest neighbour so the pixels
    // stay sharp. The source art is drawn on a 16px grid.
    class GifSprite
    {
        public readonly Bitmap[] Frames;
        public readonly int[] Delays;

        public int Width { get { return Frames[0].Width; } }
        public int Height { get { return Frames[0].Height; } }

        GifSprite(Bitmap[] frames, int[] delays)
        {
            Frames = frames;
            Delays = delays;
        }

        // pixelSize = how many screen pixels one pixel of the artwork gets
        public static GifSprite Load(string resource, int pixelSize)
        {
            byte[] raw;
            using (Stream s = Gfx.Resource(resource))
            {
                raw = new byte[s.Length];
                s.Read(raw, 0, raw.Length);
            }

            using (MemoryStream ms = new MemoryStream(raw))
            using (Image gif = Image.FromStream(ms))
            {
                FrameDimension time = new FrameDimension(gif.FrameDimensionsList[0]);
                int count = gif.GetFrameCount(time);
                int size = (int)Math.Round(gif.Width * pixelSize / 16.0);

                int[] delays = new int[count];
                for (int i = 0; i < count; i++) delays[i] = 100;
                try
                {
                    byte[] raw5100 = gif.GetPropertyItem(0x5100).Value;
                    for (int i = 0; i < count; i++) delays[i] = Math.Max(20, BitConverter.ToInt32(raw5100, i * 4) * 10);
                }
                catch (ArgumentException) { }

                Bitmap[] frames = new Bitmap[count];
                for (int i = 0; i < count; i++)
                {
                    gif.SelectActiveFrame(time, i);
                    Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(gif, new Rectangle(0, 0, size, size), 0, 0, gif.Width, gif.Height, GraphicsUnit.Pixel);
                    }
                    frames[i] = bmp;
                }
                return new GifSprite(frames, delays);
            }
        }
    }

    // Plays a sprite on a see-through background. Used for the mascot that sits
    // on top of the window.
    class SpriteBox : Control
    {
        readonly GifSprite sprite;
        readonly WinTimer timer = new WinTimer();
        int frame;

        public SpriteBox(GifSprite sprite)
        {
            this.sprite = sprite;
            Size = new Size(sprite.Width, sprite.Height);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
            timer.Tick += delegate
            {
                frame = (frame + 1) % sprite.Frames.Length;
                timer.Interval = sprite.Delays[frame];
                Invalidate();
            };
        }

        public bool Playing
        {
            get { return timer.Enabled; }
            set
            {
                if (value == timer.Enabled) return;
                if (value) { timer.Interval = sprite.Delays[frame]; timer.Start(); }
                else { timer.Stop(); frame = 0; Invalidate(); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Bitmap f = sprite.Frames[frame];
            e.Graphics.DrawImage(f, new Rectangle(0, 0, f.Width, f.Height), 0, 0, f.Width, f.Height, GraphicsUnit.Pixel);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    class ToggleSwitch : Control
    {
        bool on;

        // raised only when the user flips it, not when the code sets Checked
        public event EventHandler UserToggled;

        public ToggleSwitch(int width, int height)
        {
            Size = new Size(width, height);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Checked
        {
            get { return on; }
            set { if (on != value) { on = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            Color track = on ? Theme.Accent : Theme.Off;
            if (!Enabled) track = Color.FromArgb(52, 57, 66);
            using (Brush b = new SolidBrush(track)) g.FillRectangle(b, 0, 0, Width, Height);

            int d = Height - 8;
            int x = on ? Width - 4 - d : 4;
            using (Brush knob = new SolidBrush(Enabled ? Color.White : Color.FromArgb(150, 155, 165)))
                g.FillRectangle(knob, x, 4, d, d);

            if (Focused && ShowFocusCues)
                using (Pen ring = new Pen(Theme.Text)) g.DrawRectangle(ring, 1, 1, Width - 3, Height - 3);
        }

        void Flip()
        {
            if (!Enabled) return;
            on = !on;
            Invalidate();
            if (UserToggled != null) UserToggled(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e) { Flip(); base.OnClick(e); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Flip();
            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    // Looks like a dropdown, opens a dark menu under it.
    class SelectBox : Control
    {
        string[] items = new string[0];
        int selected = -1;

        public event EventHandler SelectionChanged;

        public SelectBox()
        {
            Height = 34;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int SelectedIndex { get { return selected; } }

        public void SetItems(string[] list, int index)
        {
            items = list;
            selected = index;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Brush b = new SolidBrush(Theme.Card)) g.FillRectangle(b, 0, 0, Width, Height);
            using (Pen pen = new Pen(Focused ? Theme.Muted : Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            string text = (selected >= 0 && selected < items.Length) ? items[selected] : "";
            Color fore = Enabled ? Theme.Text : Theme.Muted;

            // a long name gets a slightly smaller font before it would be cut short
            Rectangle area = new Rectangle(12, 0, Width - 38, Height);
            Font font = Font;
            float size = font.Size;
            while (size > 8f && TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width > area.Width)
            {
                size -= 0.5f;
                if (font != Font) font.Dispose();
                font = new Font(Font.FontFamily, size, Font.Style);
            }
            TextRenderer.DrawText(g, text, font, area, fore, Gfx.Line);
            if (font != Font) font.Dispose();

            // little chevron
            int cx = Width - 18, cy = Height / 2;
            using (Pen chev = new Pen(Theme.Muted, 1.6f))
                g.DrawLines(chev, new Point[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnClick(EventArgs e) { ShowMenu(); base.OnClick(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down) ShowMenu();
            base.OnKeyDown(e);
        }

        void ShowMenu()
        {
            if (!Enabled || items.Length == 0) return;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new DarkRenderer();
            menu.Font = Font;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.MinimumSize = new Size(Width, 0);

            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(items[i]);
                item.Checked = i == selected;
                item.ForeColor = Theme.Text;
                item.Padding = new Padding(2, 5, 2, 5);
                item.Click += delegate
                {
                    if (index == selected) return;
                    selected = index;
                    Invalidate();
                    if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                };
                menu.Items.Add(item);
            }

            // dispose later, the click handler above still has to run
            menu.Closed += delegate { BeginInvoke(new MethodInvoker(menu.Dispose)); };
            menu.Show(this, new Point(0, Height + 2));
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Hover; } }
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Rectangle r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            using (Brush b = new SolidBrush(Theme.Hover)) e.Graphics.FillRectangle(b, r);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Theme.Accent, 2f))
                e.Graphics.DrawLines(pen, new Point[] { new Point(cx - 4, cy), new Point(cx - 1, cy + 3), new Point(cx + 4, cy - 3) });
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (Pen pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, r);
        }
    }

    enum CaptionKind { Minimize, Close }

    // Minimize / close, since the window has no title bar
    class CaptionButton : Control
    {
        readonly CaptionKind kind;
        bool hover;

        public CaptionButton(CaptionKind kind)
        {
            this.kind = kind;
            Size = new Size(46, 30);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Window;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = BackColor;
            if (hover) back = kind == CaptionKind.Close ? Theme.Danger : Theme.Hover;
            g.Clear(back);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = Width / 2, cy = Height / 2;
            using (Pen pen = new Pen(Theme.Text, 1.3f))
            {
                if (kind == CaptionKind.Minimize)
                    g.DrawLine(pen, cx - 5, cy + 1, cx + 5, cy + 1);
                else
                {
                    g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(pen, cx - 5, cy + 5, cx + 5, cy - 5);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // The digging mascot plus a label. The mascot only moves while the pointer is on it.
    class SupportButton : Control
    {
        const int TopGap = 6;

        readonly GifSprite sprite;
        readonly WinTimer timer = new WinTimer();
        string label = "";
        int frame;
        bool hover;

        // same face a bit bigger, used while the pointer is on the button
        Font grown;

        public SupportButton(GifSprite sprite)
        {
            this.sprite = sprite;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Window;
            TabStop = true;
            timer.Tick += delegate
            {
                frame = (frame + 1) % sprite.Frames.Length;
                timer.Interval = sprite.Delays[frame];
                Invalidate();
            };
        }

        public string Label
        {
            get { return label; }
            set { label = value; Measure(); }
        }

        void Measure()
        {
            if (grown != null) grown.Dispose();
            grown = new Font(Font.FontFamily, Font.Size * 1.2f, Font.Style);

            // size for the grown text so the layout doesn't jump on hover
            Size text = TextRenderer.MeasureText(label, grown, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            Size = new Size(10 + sprite.Width + 12 + text.Width + 20, sprite.Height + TopGap);
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Measure(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            // the last row of the artwork is the ground, so it sits flush with the bottom edge
            Bitmap f = sprite.Frames[frame];
            g.DrawImage(f, new Rectangle(10, TopGap, f.Width, f.Height), 0, 0, f.Width, f.Height, GraphicsUnit.Pixel);

            // a few pixels above the middle of the canvas: the character, not the shovel, is what the eye centers on
            Rectangle textBox = new Rectangle(10 + sprite.Width + 12, TopGap - 3, Width - sprite.Width - 32, sprite.Height);
            TextRenderer.DrawText(g, label, hover ? grown : Font, textBox, hover ? Theme.Pink : Theme.Text, Gfx.Line);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            timer.Interval = sprite.Delays[frame];
            timer.Start();
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            timer.Stop();
            frame = 0;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                if (grown != null) grown.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    class TextLink : Control
    {
        bool hover;
        Font underlined;

        public TextLink()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Window;
            TabStop = false;
        }

        public void SetText(string text)
        {
            Text = text;
            Size size = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            Size = new Size(size.Width + 2, size.Height + 2);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (hover && underlined == null) underlined = new Font(Font, FontStyle.Underline);
            Font f = hover ? underlined : Font;
            TextRenderer.DrawText(e.Graphics, Text, f, new Rectangle(0, 0, Width, Height), Theme.Muted, Gfx.Line);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            if (underlined != null) { underlined.Dispose(); underlined = null; }
            base.OnFontChanged(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    }
}
