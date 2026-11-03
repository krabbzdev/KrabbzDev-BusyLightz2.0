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
    class ModernButton : Button
    {
        public bool Accent;
        bool hover, pressed;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // BackColor is the surface the button sits on, showing at the rounded corners.
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
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
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Accent ? Color.White : Theme.Teams) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 3, 3, Width - 7, Height - 7);
        }
    }
}
