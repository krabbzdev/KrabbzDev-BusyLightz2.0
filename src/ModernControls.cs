using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // A check box drawn as a Windows 11 style on/off switch, with its text beside it. Behaves like any CheckBox.
    class ToggleBox : CheckBox
    {
        const int SwitchWidth = 36, SwitchHeight = 18, Gap = 10;
        // The switch's color while on.
        public Color Accent = Theme.Teams;

        public ToggleBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        int LineHeight { get { return TextRenderer.MeasureText("Ag", Font).Height; } }
        int RowHeight { get { return Math.Max(SwitchHeight + 4, LineHeight); } }

        // The switch, then the text wrapped to fit MaximumSize when one is set.
        public override Size GetPreferredSize(Size proposedSize)
        {
            int maxText = (MaximumSize.Width > 0 ? MaximumSize.Width : 4000) - SwitchWidth - Gap;
            var text = TextRenderer.MeasureText(Text, Font, new Size(maxText, 0), TextFormatFlags.WordBreak);
            return new Size(SwitchWidth + Gap + text.Width, Math.Max(RowHeight, text.Height + (RowHeight - LineHeight)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new Rectangle(0, (RowHeight - SwitchHeight) / 2, SwitchWidth, SwitchHeight);
            int knob = SwitchHeight - 8;
            if (Checked)
            {
                Color on = Enabled ? Accent : Theme.Mix(Accent, BackColor, 0.6);
                Theme.FillRounded(g, track, SwitchHeight / 2, on, on);
                using (var fill = new SolidBrush(Color.White))
                    g.FillEllipse(fill, track.Right - 4 - knob, track.Y + 4, knob, knob);
            }
            else
            {
                Color edge = Enabled ? Theme.Muted : Theme.Border;
                Theme.FillRounded(g, track, SwitchHeight / 2, BackColor, edge);
                using (var fill = new SolidBrush(edge))
                    g.FillEllipse(fill, track.X + 4, track.Y + 4, knob, knob);
            }
            var text = new Rectangle(SwitchWidth + Gap, (RowHeight - LineHeight) / 2, Width - SwitchWidth - Gap, Height);
            TextRenderer.DrawText(g, Text, Font, text, Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.WordBreak);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Accent) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, track.X, track.Y - 1, track.Width - 1, track.Height + 1);
        }
    }

    // A button with rounded corners in the app's colors. Accent fills it with the Teams purple, for the main action.
    // Rainbow fills it with the Party Mode colors, which drift across it while the mouse is over it.
    class ModernButton : Button
    {
        public bool Accent, Rainbow;
        // A Segoe MDL2 Assets icon shown before the text, or null.
        public string Glyph;
        bool hover, pressed;
        // How far the rainbow has drifted, as a fraction of the button's width; moved on by drift while hovered.
        float phase;
        Timer drift;

        static readonly Color[] RainbowColors =
        {
            Theme.Red, Color.FromArgb(247, 99, 12), Color.FromArgb(234, 179, 8), Theme.Green,
            Theme.CallBlue, Theme.Teams, Color.FromArgb(194, 57, 179), Theme.Red,
        };

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            if (Rainbow)
            {
                if (drift == null)
                {
                    drift = new Timer { Interval = 30 };
                    drift.Tick += delegate { phase = (phase + 0.012f) % 1f; Invalidate(); };
                }
                drift.Start();
            }
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = pressed = false;
            if (drift != null) drift.Stop();
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && drift != null) drift.Dispose();
            base.Dispose(disposing);
        }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // BackColor is the surface the button sits on, showing at the rounded corners.
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Rainbow) { PaintRainbow(g); return; }
            Color fill, border, text;
            if (Accent)
            {
                fill = pressed ? Theme.Mix(Theme.Teams, Color.Black, 0.2) : hover ? Theme.Mix(Theme.Teams, Color.Black, 0.1) : Theme.Teams;
                border = fill;
                text = Color.White;
            }
            else
            {
                Color rest = AppSettings.DarkMode ? Color.FromArgb(58, 58, 66) : Color.FromArgb(251, 251, 252);
                fill = pressed ? Theme.Wash(Theme.Teams, 0.7) : hover ? Theme.Wash(Theme.Teams, 0.85) : rest;
                border = Theme.Border;
                text = Theme.Text;
            }
            if (!Enabled) { fill = Theme.Mix(fill, BackColor, 0.5); text = Theme.Muted; }
            Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), 6, fill, border);
            DrawLabel(g, Font, text);
            DrawFocus(g, Accent ? Color.White : Theme.Teams);
        }

        // The Party Mode colors across the button, darkened a little so white text reads on every color.
        void PaintRainbow(Graphics g)
        {
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            // The whole rainbow fits across the button and repeats (it starts and ends on red), so drifting by
            // phase never shows an edge.
            int span = Width;
            using (var path = Theme.Rounded(r, 6))
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, span, Height), Color.Black, Color.Black, 0f))
            {
                var blend = new ColorBlend { Colors = RainbowColors, Positions = new float[RainbowColors.Length] };
                for (int i = 0; i < RainbowColors.Length; i++) blend.Positions[i] = (float)i / (RainbowColors.Length - 1);
                brush.InterpolationColors = blend;
                brush.WrapMode = WrapMode.Tile;
                brush.TranslateTransform(-phase * span, 0);
                g.FillPath(brush, path);
                using (var shade = new SolidBrush(Color.FromArgb(pressed ? 100 : hover ? 25 : 50, 0, 0, 0))) g.FillPath(shade, path);
                using (var shine = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawPath(shine, path);
            }
            using (var bold = new Font("Segoe UI Semibold", Font.Size)) DrawLabel(g, bold, Color.White);
            DrawFocus(g, Color.White);
        }

        // The icon (if any) and text, centered together.
        void DrawLabel(Graphics g, Font font, Color color)
        {
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            int textWidth = TextRenderer.MeasureText(g, Text, font, Size.Empty, flags).Width;
            if (Glyph == null || Theme.Icons == null)
            {
                TextRenderer.DrawText(g, Text, font, ClientRectangle, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            const int iconWidth = 16, gap = 7;
            int x = (Width - iconWidth - gap - textWidth) / 2;
            TextRenderer.DrawText(g, Glyph, Theme.Icons, new Rectangle(x, 0, iconWidth, Height), color,
                TextFormatFlags.HorizontalCenter | flags);
            TextRenderer.DrawText(g, Text, font, new Rectangle(x + iconWidth + gap, 0, textWidth + 2, Height), color, flags);
        }

        void DrawFocus(Graphics g, Color color)
        {
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(color) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 3, 3, Width - 7, Height - 7);
        }
    }

    // A rounded slider: a track filled in an accent color up to a round thumb. Drag it, click the track, or use the
    // arrow keys and mouse wheel. Behaves like a TrackBar for Minimum, Maximum, Value and ValueChanged.
    class ModernSlider : Control
    {
        int minimum, maximum = 10, value;
        bool dragging, hover;
        public Color Accent = Theme.Teams;
        public event EventHandler ValueChanged;

        public ModernSlider()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Height = 30;
        }

        public int Minimum { get { return minimum; } set { minimum = value; Invalidate(); } }
        public int Maximum { get { return maximum; } set { maximum = value; Invalidate(); } }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(minimum, Math.Min(maximum, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        const int Thumb = 18;
        int TrackLeft { get { return Thumb / 2 + 2; } }
        int TrackWidth { get { return Math.Max(1, Width - Thumb - 4); } }

        int ValueAt(int x)
        {
            double t = (x - TrackLeft) / (double)TrackWidth;
            return minimum + (int)Math.Round(Math.Max(0, Math.Min(1, t)) * (maximum - minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); dragging = true; Value = ValueAt(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Value = ValueAt(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { Value += e.Delta > 0 ? 1 : -1; base.OnMouseWheel(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value--;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value++;
            else if (e.KeyCode == Keys.Home) Value = minimum;
            else if (e.KeyCode == Keys.End) Value = maximum;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool on = Enabled;
            var accent = on ? Accent : Theme.Mix(Accent, BackColor, 0.6);
            int cy = Height / 2;
            double t = maximum > minimum ? (value - minimum) / (double)(maximum - minimum) : 0;
            int x = TrackLeft + (int)(t * TrackWidth);
            var track = new Rectangle(TrackLeft, cy - 3, TrackWidth, 6);
            Theme.FillRounded(g, track, 3, Theme.Mix(Theme.Border, BackColor, 0.2), Theme.Mix(Theme.Border, BackColor, 0.2));
            if (x > TrackLeft)
            {
                var filled = new Rectangle(TrackLeft, cy - 3, x - TrackLeft, 6);
                using (var path = Theme.Rounded(filled, 3))
                using (var brush = new LinearGradientBrush(new Rectangle(TrackLeft - 1, 0, TrackWidth + 2, Height), Theme.Mix(accent, Color.White, 0.25), accent, LinearGradientMode.Horizontal))
                    g.FillPath(brush, path);
            }
            int size = hover || dragging ? Thumb + 2 : Thumb;
            var thumb = new Rectangle(x - size / 2, cy - size / 2, size, size);
            using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(shadow, thumb.X, thumb.Y + 2, size, size);
            using (var fill = new SolidBrush(Color.White)) g.FillEllipse(fill, thumb);
            using (var dot = new SolidBrush(accent)) g.FillEllipse(dot, Rectangle.Inflate(thumb, -size / 4 - 1, -size / 4 - 1));
            if (Focused)
                using (var pen = new Pen(Theme.Mix(accent, Color.White, 0.3), 1.5f)) g.DrawEllipse(pen, Rectangle.Inflate(thumb, 3, 3));
        }
    }

    // A row of color dots to pick from (the light color names), the picked one ringed with a check. Click or use the
    // arrow keys.
    class ColorSwatches : Control
    {
        public const int Dot = 22, Spacing = 30;
        readonly string[] names;
        int selected;
        int hover = -1;
        public event EventHandler SelectedChanged;
        readonly ToolTip tips = new ToolTip();

        public ColorSwatches(string[] names)
        {
            this.names = names;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(names.Length * Spacing, Dot + 10);
        }

        public string Selected
        {
            get { return names[selected]; }
            set
            {
                int i = Array.IndexOf(names, value);
                if (i < 0 || i == selected) return;
                selected = i;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        int IndexAt(Point p)
        {
            int i = p.X / Spacing;
            return i >= 0 && i < names.Length ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i != hover)
            {
                hover = i;
                tips.SetToolTip(this, i >= 0 ? names[i] : null);
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int i = IndexAt(e.Location);
            if (i >= 0) Selected = names[i];
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && selected > 0) Selected = names[selected - 1];
            else if (e.KeyCode == Keys.Right && selected < names.Length - 1) Selected = names[selected + 1];
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < names.Length; i++)
            {
                var c = BlyncDevice.ColorFor(names[i]);
                var color = Theme.Soften(Color.FromArgb(c.R, c.G, c.B));
                if (!Enabled) color = Theme.Mix(color, BackColor, 0.6);
                int grow = i == hover ? 2 : 0;
                var r = new Rectangle(i * Spacing + (Spacing - Dot) / 2 - grow, 5 - grow, Dot + 2 * grow, Dot + 2 * grow);
                using (var fill = new SolidBrush(color)) g.FillEllipse(fill, r);
                using (var edge = new Pen(Theme.Mix(color, Color.Black, 0.2), 1f)) g.DrawEllipse(edge, r);
                if (i != selected) continue;
                using (var ring = new Pen(Focused ? Theme.Teams : Theme.Text, 2f)) g.DrawEllipse(ring, Rectangle.Inflate(r, 3, 3));
                // A check mark in black or white, whichever shows on this color.
                var ink = color.GetBrightness() > 0.6f ? Color.FromArgb(30, 30, 34) : Color.White;
                using (var pen = new Pen(ink, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.28f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.68f), new PointF(r.X + r.Width * 0.72f, r.Y + r.Height * 0.34f) });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }}
