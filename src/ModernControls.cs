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
                Color on = Enabled ? Theme.Teams : Theme.Mix(Theme.Teams, BackColor, 0.6);
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
                using (var pen = new Pen(Theme.Teams) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, track.X, track.Y - 1, track.Width - 1, track.Height + 1);
        }
    }

    // A button with rounded corners in the app's colors. Accent fills it with the Teams purple, for the main action.
    // Rainbow fills it with the Test lights colors, which drift across it while the mouse is over it.
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

        // The Test lights colors across the button, darkened a little so white text reads on every color.
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
}
