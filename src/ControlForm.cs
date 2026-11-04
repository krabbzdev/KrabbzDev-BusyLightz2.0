using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // Colors and drawing helpers shared by the app's windows. The neutral colors follow the Light/Dark setting;
    // the mode colors are the same in both.
    static class Theme
    {
        static bool Dark { get { return AppSettings.DarkMode; } }
        public static Color Page { get { return Dark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(247, 247, 249); } }
        public static Color Card { get { return Dark ? Color.FromArgb(44, 44, 50) : Color.White; } }
        public static Color Border { get { return Dark ? Color.FromArgb(64, 64, 72) : Color.FromArgb(228, 228, 233); } }
        public static Color BorderHover { get { return Dark ? Color.FromArgb(100, 100, 112) : Color.FromArgb(190, 190, 200); } }
        public static Color Text { get { return Dark ? Color.FromArgb(240, 240, 243) : Color.FromArgb(27, 27, 31); } }
        public static Color Muted { get { return Dark ? Color.FromArgb(160, 160, 172) : Color.FromArgb(107, 107, 118); } }
        public static readonly Color Red = Color.FromArgb(209, 52, 56);
        public static readonly Color Green = Color.FromArgb(16, 124, 16);
        public static readonly Color CallRed = Color.FromArgb(164, 38, 44);
        public static readonly Color CallBlue = Color.FromArgb(0, 120, 212);
        public static readonly Color Teams = Color.FromArgb(91, 95, 199);
        public static Color TeamsTint { get { return Dark ? Color.FromArgb(46, 48, 84) : Color.FromArgb(238, 240, 251); } }
        public static Color TeamsText { get { return Dark ? Color.FromArgb(184, 188, 246) : Color.FromArgb(59, 63, 143); } }
        public static Color OverrideTint { get { return Dark ? Color.FromArgb(78, 64, 18) : Color.FromArgb(255, 244, 206); } }
        public static Color OverrideText { get { return Dark ? Color.FromArgb(250, 214, 120) : Color.FromArgb(122, 90, 0); } }
        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Strong = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font Status = new Font("Segoe UI Semibold", 16f);
        public static readonly Font Hero = new Font("Segoe UI Semibold", 20f);
        public static readonly Font SmallStrong = new Font("Segoe UI Semibold", 8.5f);
        public static readonly Font Tiny = new Font("Segoe UI Semibold", 7f);
        // The mode icons; null where Windows lacks the font (before Windows 10).
        public static readonly Font Icons = IconFont();

        static Font IconFont()
        {
            try { return new Font(new FontFamily("Segoe MDL2 Assets"), 11f); }
            catch (ArgumentException) { return null; }
        }

        // a mixed toward b; amount 0 is a, 1 is b.
        public static Color Mix(Color a, Color b, double amount)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
        }

        // Text that reads on a filled color: near-black on light colors (yellow), white on the rest.
        public static Color TextOn(Color fill)
        {
            return fill.R * 0.299 + fill.G * 0.587 + fill.B * 0.114 > 160 ? Color.FromArgb(24, 24, 28) : Color.White;
        }

        // The tray's display colors are pure web colors; the window shows softer versions of them.
        public static Color Soften(Color color)
        {
            if (color == Color.Red) return Red;
            if (color == Color.Lime) return Color.FromArgb(19, 161, 14);
            if (color == Color.Gold) return Color.FromArgb(234, 179, 8);
            if (color == Color.MediumPurple) return Color.FromArgb(135, 100, 184);
            if (color == Color.Black) return Color.FromArgb(59, 59, 66);
            if (color == Color.Gray) return Color.FromArgb(160, 160, 168);
            return color;
        }

        // color mixed toward white; amount 0 is the color, 1 is white.
        public static Color Tint(Color color, double amount)
        {
            return Color.FromArgb(
                (int)(color.R + (255 - color.R) * amount),
                (int)(color.G + (255 - color.G) * amount),
                (int)(color.B + (255 - color.B) * amount));
        }

        // color faded into the card behind it; amount 0 is the color, 1 is the card. Used for halos and hovers.
        public static Color Wash(Color color, double amount)
        {
            var card = Card;
            return Color.FromArgb(
                (int)(color.R + (card.R - color.R) * amount),
                (int)(color.G + (card.G - color.G) * amount),
                (int)(color.B + (card.B - color.B) * amount));
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        // Colors a window with the standard controls (Settings and the like) to match, title bar included. In Light
        // mode they keep the standard Windows look. Call it once the window is built; switching modes reopens it.
        public static void Apply(Form form)
        {
            if (Dark) Style(form);
            TitleBar(form);
        }

        // A dark title bar in Dark mode (Windows 10 2004 and later).
        public static void TitleBar(Form form)
        {
            int dark = Dark ? 1 : 0;
            try { DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)); }
            catch (Exception) { } // Older Windows: the title bar just stays light.
        }

        // Marks a control that keeps its own colors in Dark mode, like the yellow Buy me a coffee button.
        public const string KeepColors = "keep-colors";

        static void Style(Control control)
        {
            if (KeepColors.Equals(control.Tag)) return;
            control.ForeColor = Text;
            if (control is Form || control is Panel || control is CheckBox || control is Label) control.BackColor = Page;
            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Card;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Wash(Teams, 0.75);
            }
            if (control is ComboBox || control is TextBox || control is ListBox || control is ListView) control.BackColor = Card;
            if (control is ComboBox) ((ComboBox)control).FlatStyle = FlatStyle.Flat;
            foreach (Control child in control.Controls) Style(child);
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            using (var path = Rounded(r, radius))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }

        // A small round pill with text, e.g. "Override on". Returns its width.
        public static int Pill(Graphics g, string text, int right, int y, Color fill, Color textColor)
        {
            var size = TextRenderer.MeasureText(g, text, Small, Size.Empty, TextFormatFlags.NoPadding);
            var r = new Rectangle(right - size.Width - 16, y, size.Width + 16, size.Height + 6);
            using (var path = Rounded(r, r.Height / 2))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, text, Small, r, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return r.Width;
        }
    }

    // Base for the window's owner-drawn parts: double-buffered and antialiased.
    class PaintedControl : Control
    {
        public PaintedControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            base.OnPaint(e);
        }
    }

    // The status card: a glowing dot in the light's color, what it means, and which lights are connected.
    class StatusCard : PaintedControl
    {
        public Color DotColor = Color.Gray;
        public string Status = "";
        public bool OverrideOn;
        public int Blynclights, Luxafors;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), 10, Theme.Card, Theme.Border);

            const int tiles = 0; // Device tiles are in the Active Devices panel.
            var dot = Theme.Soften(DotColor);
            using (var halo = new SolidBrush(Theme.Wash(dot, 0.82)))
                g.FillEllipse(halo, 16, 14, 68, 68);
            using (var fill = new SolidBrush(dot))
                g.FillEllipse(fill, 28, 26, 44, 44);

            TextRenderer.DrawText(g, "Your lights are", Theme.Small, new Point(98, 16), Theme.Muted);
            TextRenderer.DrawText(g, Status, Theme.Status, new Rectangle(96, 32, Width - 104 - tiles - 8, 34), Theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            if (OverrideOn) Theme.Pill(g, "Manual override on", Width - 12, 12, Theme.OverrideTint, Theme.OverrideText);
        }
    }

    // Active Devices: a card with a tile for every device BusyLightz works with, lit when plugged in and greyed out
    // ("Disconnected") when not.
    class DevicePanel : PaintedControl
    {
        public const int PanelHeight = 40 + DeviceTiles.GridHeight + 10;
        public Color DotColor = Color.Gray;
        public int Blynclights, Luxafors;
        public bool K2;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), 10, Theme.Card, Theme.Border);
            TextRenderer.DrawText(g, "Active Devices", Theme.Strong, new Point(14, 11), Theme.Text);
            DeviceTiles.DrawGrid(g, new Rectangle(4, 40, Width - 8, DeviceTiles.GridHeight), Blynclights, Luxafors, K2, DotColor,
                Theme.Page, Theme.Border, Theme.Text, Theme.Muted);
        }
    }

    // The space under the status card. While an override is on it shows how long is left and a link back to
    // Teams on one line; otherwise a short paragraph (Wrapped), with its link on its own line below.
    // It always takes the same space so the buttons below never move.
    class OverrideStrip : PaintedControl
    {
        public string Message = "";
        public string Link;
        public bool Highlighted;
        public bool Wrapped;
        public event Action LinkClicked;
        Rectangle linkBounds;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var text = Highlighted ? Theme.TeamsText : Theme.Muted;
            // The one-line countdown sits in a band at the top, not the whole space.
            var band = Wrapped ? new Rectangle(0, 0, Width, Height) : new Rectangle(0, 0, Width, Math.Min(Height, 38));
            if (Highlighted) Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, band.Height - 1), 8, Theme.TeamsTint, Theme.TeamsTint);
            linkBounds = Rectangle.Empty;

            if (Wrapped)
            {
                var area = new Rectangle(12, 4, Width - 24, Height - 8);
                var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
                int used = TextRenderer.MeasureText(g, Message, Theme.Small, new Size(area.Width, 0), flags).Height;
                TextRenderer.DrawText(g, Message, Theme.Small, area, text, flags);
                if (Link == null) return;
                using (var underline = new Font(Theme.Small, FontStyle.Underline))
                {
                    var size = TextRenderer.MeasureText(g, Link, underline, Size.Empty, TextFormatFlags.NoPadding);
                    linkBounds = new Rectangle(area.X, area.Y + used + 2, size.Width, size.Height);
                    TextRenderer.DrawText(g, Link, underline, linkBounds, Theme.TeamsText, TextFormatFlags.NoPadding);
                }
                return;
            }

            TextRenderer.DrawText(g, Message, Theme.Body, new Rectangle(12, 0, Width - 24, band.Height), text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Link == null) return;
            using (var underline = new Font(Theme.Body, FontStyle.Underline))
            {
                var size = TextRenderer.MeasureText(g, Link, underline);
                linkBounds = new Rectangle(Width - size.Width - 8, 0, size.Width, band.Height);
                TextRenderer.DrawText(g, Link, underline, linkBounds, text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = linkBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (linkBounds.Contains(e.Location) && LinkClicked != null) LinkClicked();
            base.OnMouseClick(e);
        }
    }

    // A mode button: a colored dot, a title and a hint. The picked mode fills with its color.
    class ModeTile : PaintedControl
    {
        public readonly string State;
        readonly string title, hint;
        readonly Color accent;
        bool active, hover;
        // Title only, for the shorter tiles in the Color block style.
        public bool Compact;

        public ModeTile(string state, string title, string hint, Color accent)
        {
            State = state;
            this.title = title;
            this.hint = hint;
            this.accent = accent;
            AccessibleName = title;
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public bool Active
        {
            get { return active; }
            set { if (active != value) { active = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = active ? accent : hover ? Theme.Wash(accent, 0.9) : Theme.Card;
            Color border = active ? accent : hover ? Theme.BorderHover : Theme.Border;
            Theme.FillRounded(g, r, 8, fill, border);

            var icon = new Rectangle(12, Compact ? (Height - 16) / 2 : 14, 16, 16);
            Mode.For(State).DrawIcon(g, icon, active ? Color.White : accent, fill);

            Color text = active ? Color.White : Theme.Text;
            Color sub = active ? Theme.Tint(accent, 0.75) : Theme.Muted;
            if (Compact)
                TextRenderer.DrawText(g, title, Theme.Strong, new Rectangle(32, 0, Width - 36, Height), text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            else
            {
                TextRenderer.DrawText(g, title, Theme.Strong, new Point(32, 10), text);
                TextRenderer.DrawText(g, hint, Theme.Small, new Point(33, 34), sub);
            }

            if (Focused && ShowFocusCues)
                using (var pen = new Pen(active ? Color.White : accent) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 4, 4, Width - 9, Height - 9);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }
    }

    // One of the four main modes, as the window styles show it.
    class Mode
    {
        public readonly string State, Title, Keys;
        public readonly Color Accent;

        Mode(string state, string title, Color accent)
        {
            State = state;
            Title = title;
            Accent = accent;
            string keys;
            Keys = TrayContext.HotkeyText.TryGetValue(state, out keys) ? keys : null;
        }

        public static readonly Mode[] All =
        {
            new Mode(LightState.Red, "Busy", Theme.Red),
            new Mode(LightState.Green, "Available", Theme.Green),
            new Mode(LightState.CallFlash, "On a call", Theme.CallRed),
            new Mode(LightState.FollowTeams, "Follow Teams Status", Theme.Teams),
        };

        // The title with its hotkey while hotkeys are on, e.g. "Busy (Alt+R)".
        public string Label
        {
            get { return Keys != null && AppSettings.HotkeysEnabled ? Title + " (" + Keys + ")" : Title; }
        }

        public static Mode For(string state)
        {
            foreach (var mode in All) if (mode.State == state) return mode;
            return null;
        }

        // Segoe MDL2 Assets glyphs: check mark, phone, people.
        const string AvailableGlyph = "", CallGlyph = "", TeamsGlyph = "";

        // The mode's icon in color: a stop sign for Busy (its bar in back, the color behind it), a check for
        // Available, a phone for On a call and people for Follow Teams Status. A plain dot if the icon font is missing.
        public void DrawIcon(Graphics g, Rectangle r, Color color, Color back)
        {
            if (State == LightState.Red)
            {
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, radius = r.Width / 2f;
                var points = new PointF[8];
                for (int i = 0; i < 8; i++)
                {
                    double angle = Math.PI / 8 + i * Math.PI / 4;
                    points[i] = new PointF(cx + (float)(radius * Math.Cos(angle)), cy + (float)(radius * Math.Sin(angle)));
                }
                using (var brush = new SolidBrush(color)) g.FillPolygon(brush, points);
                using (var bar = new SolidBrush(back))
                    g.FillRectangle(bar, cx - radius * 0.55f, cy - radius * 0.15f, radius * 1.1f, radius * 0.3f);
                return;
            }
            if (Theme.Icons == null)
            {
                using (var brush = new SolidBrush(color)) g.FillEllipse(brush, Rectangle.Inflate(r, -2, -2));
                return;
            }
            string glyph = State == LightState.Green ? AvailableGlyph : State == LightState.CallFlash ? CallGlyph : TeamsGlyph;
            TextRenderer.DrawText(g, glyph, Theme.Icons, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // Orbit style: the top of the window in the lights' color, glowing behind a big ring that holds the status with
    // an icon, inside two faint orbits with a "satellite" on each. During an override the ring is a countdown: it
    // empties as the time left runs out.
    class OrbitRing : PaintedControl
    {
        const int Pad = 16, Diameter = 168, Thickness = 10, RingTop = 58;
        const string SettingsText = "⚙  Settings", OverrideText = "Manual override on";
        public Color DotColor = Color.Gray;
        public string Big = "", Small = "";
        // Segoe MDL2 Assets icon over the status, or null.
        public string Glyph;
        // How much of an override's time is left, 0 to 1; a full ring when there's no countdown.
        public float Progress = 1;
        public bool OverrideOn;
        public event Action SettingsClicked;
        Rectangle settingsBounds;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var fill = Theme.Soften(DotColor);
            // Lighter at the top, deeper at the bottom.
            using (var brush = new LinearGradientBrush(ClientRectangle, Theme.Mix(fill, Color.White, 0.12),
                Theme.Mix(fill, Color.Black, 0.22), LinearGradientMode.Vertical))
                g.FillRectangle(brush, ClientRectangle);
            var text = Theme.TextOn(fill);
            var soft = Theme.Mix(text, fill, 0.25);
            var ring = new Rectangle((Width - Diameter) / 2, RingTop, Diameter, Diameter);
            DrawGlowAndOrbits(g, ring, fill, text);

            TextRenderer.DrawText(g, "BusyLightz 2.0", Theme.Strong, new Point(Pad, 10), text);
            TextRenderer.DrawText(g, "by KrabbzDev", Theme.Small, new Point(Pad + 1, 30), soft);
            var size = TextRenderer.MeasureText(g, SettingsText, Theme.Small);
            settingsBounds = new Rectangle(Width - Pad - size.Width, 10, size.Width, size.Height + 8);
            TextRenderer.DrawText(g, SettingsText, Theme.Small, settingsBounds, text, TextFormatFlags.VerticalCenter);

            // Centered at the top while the user's own pick is overriding Teams status.
            if (OverrideOn)
            {
                int pill = TextRenderer.MeasureText(g, OverrideText, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width + 16;
                Theme.Pill(g, OverrideText, (Width + pill) / 2, 14, Theme.Mix(fill, Color.Black, 0.25), text);
            }

            // A deep disc, lit a little from the top, inside the ring.
            using (var inside = new LinearGradientBrush(ring, Theme.Mix(fill, Color.Black, 0.06), Theme.Mix(fill, Color.Black, 0.3),
                LinearGradientMode.Vertical))
                g.FillEllipse(inside, ring);
            // The ring: a faint track, with the time left drawn over it from the top, clockwise.
            var track = Rectangle.Inflate(ring, -Thickness / 2, -Thickness / 2);
            using (var pen = new Pen(Color.FromArgb(70, text), Thickness)) g.DrawEllipse(pen, track);
            float sweep = 360f * Math.Max(0f, Math.Min(1f, Progress));
            if (sweep >= 359.5f)
                using (var pen = new Pen(Theme.Mix(text, fill, 0.08), Thickness)) g.DrawEllipse(pen, track);
            else if (sweep > 0)
                using (var pen = new Pen(Theme.Mix(text, fill, 0.08), Thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, track, -90, sweep);

            var inner = Rectangle.Inflate(ring, -Thickness - 8, 0);
            var font = TextRenderer.MeasureText(g, Big, Theme.Hero).Width <= inner.Width ? Theme.Hero : Theme.Status;
            var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            int bigHeight = TextRenderer.MeasureText(g, "Ag", font).Height;
            int glyphHeight = Glyph != null && Theme.Icons != null ? 28 : 0;
            int top = ring.Top + (ring.Height - glyphHeight - bigHeight - (Small.Length > 0 ? 18 : 0)) / 2;
            if (glyphHeight > 0)
            {
                using (var icon = new Font(Theme.Icons.FontFamily, 17f))
                    TextRenderer.DrawText(g, Glyph, icon, new Rectangle(inner.X, top, inner.Width, glyphHeight), soft, center);
                top += glyphHeight;
            }
            TextRenderer.DrawText(g, Big, font, new Rectangle(inner.X, top, inner.Width, bigHeight), text, center);
            if (Small.Length > 0)
                TextRenderer.DrawText(g, Small, Theme.Small, new Rectangle(inner.X, top + bigHeight, inner.Width, 18), soft, center);

            // Bottom left: how long since the PC last restarted, to the millisecond; bottom right: the beta sticker.
            DrawUptime(g, text, soft);
            DeviceTiles.BetaSticker(g, Width - Pad - 62, Height - 40);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();
        static readonly Font UptimeFont = new Font("Segoe UI Semibold", 10f);
        static readonly Font UptimeLabelFont = new Font("Segoe UI", 8f);
        static readonly Font SadFont = new Font("Segoe UI Symbol", 9f);

        // "Time since last reboot" over a running clock, e.g. "01:22:34.512". Past 24 hours (e.g. "34:08:49.234") it
        // turns into a red pill with a sad face. (Windows' Fast Startup keeps counting through a shut down; only a
        // restart resets it.)
        void DrawUptime(Graphics g, Color text, Color soft)
        {
            var up = TimeSpan.FromMilliseconds(GetTickCount64());
            bool overADay = up.TotalHours >= 24;
            // A running clock in total hours: hours:minutes:seconds.milliseconds, e.g. 34:08:49.234 after a day and 10 hours.
            string value = ((int)up.TotalHours).ToString("00") + ":" + up.Minutes.ToString("00") + ":" +
                up.Seconds.ToString("00") + "." + up.Milliseconds.ToString("000");
            int x = Pad, top = Height - 40;
            TextRenderer.DrawText(g, "Time since last reboot", UptimeLabelFont, new Point(x, top), overADay ? text : soft);
            var size = TextRenderer.MeasureText(g, value, UptimeFont, Size.Empty, TextFormatFlags.NoPadding);
            if (!overADay)
            {
                TextRenderer.DrawText(g, value, UptimeFont, new Point(x, top + 16), text, TextFormatFlags.NoPadding);
                return;
            }
            // Over a day: a red pill with a sad face.
            const string Sad = "☹";
            var face = TextRenderer.MeasureText(g, Sad, SadFont, Size.Empty, TextFormatFlags.NoPadding);
            var pill = new Rectangle(x - 4, top + 14, face.Width + 6 + size.Width + 12, Math.Max(size.Height, face.Height) + 4);
            Theme.FillRounded(g, pill, pill.Height / 2, Color.FromArgb(200, 18, 24), Color.FromArgb(255, 220, 220));
            TextRenderer.DrawText(g, Sad, SadFont, new Point(pill.X + 6, pill.Y + (pill.Height - face.Height) / 2), Color.White, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, value, UptimeFont, new Point(pill.X + 6 + face.Width + 4, pill.Y + (pill.Height - size.Height) / 2), Color.White, TextFormatFlags.NoPadding);
        }


        public int Blynclights, Luxafors;

        // The satellites drift round their orbits: the inner one clockwise once every 20 seconds, the outer one the
        // other way once every 30. Slow, smooth movement only, so nothing flashes. The timer runs only while the
        // window is showing.
        const int OrbitFrameMs = 40;
        readonly Timer orbitTimer = new Timer { Interval = OrbitFrameMs };
        readonly System.Diagnostics.Stopwatch orbitClock = System.Diagnostics.Stopwatch.StartNew();

        public OrbitRing()
        {
            orbitTimer.Tick += delegate { Invalidate(); };
            VisibleChanged += delegate { orbitTimer.Enabled = Visible; };
            orbitTimer.Enabled = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) orbitTimer.Dispose();
            base.Dispose(disposing);
        }

        // A soft glow behind the ring, and two faint orbits around it with a small "satellite" on each.
        void DrawGlowAndOrbits(Graphics g, Rectangle ring, Color fill, Color text)
        {
            double seconds = orbitClock.Elapsed.TotalSeconds;
            var glowArea = Rectangle.Inflate(ring, 70, 70);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(glowArea);
                using (var glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Color.FromArgb(120, Theme.Mix(fill, Color.White, 0.45));
                    glow.SurroundColors = new[] { Color.FromArgb(0, fill) };
                    g.FillEllipse(glow, glowArea);
                }
            }
            var cx = ring.X + ring.Width / 2f;
            var cy = ring.Y + ring.Height / 2f;
            // Radius past the ring, where the satellite starts (degrees, clockwise from the right), its size, and its
            // speed in degrees a second (negative goes the other way).
            var orbits = new[] { new[] { 24f, 215f, 4.5f, 18f }, new[] { 50f, 330f, 3.5f, -12f } };
            foreach (var orbit in orbits)
            {
                float r = ring.Width / 2f + orbit[0];
                using (var pen = new Pen(Color.FromArgb(45, text), 1.2f)) g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
                double angle = (orbit[1] + orbit[3] * seconds) % 360 * Math.PI / 180;
                float sx = cx + (float)(r * Math.Cos(angle)), sy = cy + (float)(r * Math.Sin(angle)), d = orbit[2];
                using (var halo = new SolidBrush(Color.FromArgb(60, text))) g.FillEllipse(halo, sx - d * 2, sy - d * 2, d * 4, d * 4);
                using (var dot = new SolidBrush(Color.FromArgb(220, text))) g.FillEllipse(dot, sx - d, sy - d, d * 2, d * 2);
            }
        }

        // The icon for a status shown in the ring, by its big text.
        public static string GlyphFor(string status)
        {
            string s = (status ?? "").ToLowerInvariant();
            if (s.Contains("available")) return "";           // check mark
            if (s.Contains("call")) return "";                // phone
            // A minus, like Teams' Do not disturb (the no-entry sign isn't in Segoe MDL2 Assets on Windows 10).
            if (s.Contains("busy") || s.Contains("disturb") || s.Contains("presenting")) return "";
            if (s.Contains("away") || s.Contains("right back")) return ""; // clock
            if (s.Contains("party")) return "";               // smiley
            if (s.Contains("off")) return "";                 // power
            return "";                                        // people (Teams)
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = settingsBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (settingsBounds.Contains(e.Location) && SettingsClicked != null) SettingsClicked();
            base.OnMouseClick(e);
        }
    }

    // Orbit style: the four modes as one pill-shaped switcher, each with its icon. The picked mode fills with its color.
    class PillSwitcher : PaintedControl
    {
        const int IconSize = 16, Gap = 5;
        public string Active;
        public event Action<string> Picked;
        int hover = -1;

        public PillSwitcher() { Cursor = Cursors.Hand; AccessibleName = "Modes"; }

        // Each segment is as wide as its icon and label, with the spare room shared out evenly.
        Rectangle Segment(int i)
        {
            var widths = new int[Mode.All.Length];
            int total = 0;
            for (int j = 0; j < widths.Length; j++)
            {
                widths[j] = IconSize + Gap + TextRenderer.MeasureText(Mode.All[j].Title, Theme.SmallStrong).Width;
                total += widths[j];
            }
            int spare = Math.Max(0, Width - 8 - total) / widths.Length, x = 4;
            for (int j = 0; j < i; j++) x += widths[j] + spare;
            return new Rectangle(x, 4, widths[i] + spare, Height - 9);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), (Height - 1) / 2, Theme.Card, Theme.Border);
            for (int i = 0; i < Mode.All.Length; i++)
            {
                var mode = Mode.All[i];
                var r = Segment(i);
                bool active = mode.State == Active;
                Color back = active ? mode.Accent : i == hover ? Theme.Wash(mode.Accent, 0.85) : Theme.Card;
                if (active || i == hover) Theme.FillRounded(g, r, r.Height / 2, back, back);

                var font = active ? Theme.SmallStrong : Theme.Small;
                int content = IconSize + Gap + TextRenderer.MeasureText(g, mode.Title, font).Width;
                int x = r.X + (r.Width - content) / 2;
                mode.DrawIcon(g, new Rectangle(x, r.Y + (r.Height - IconSize) / 2, IconSize, IconSize), active ? Color.White : mode.Accent, back);
                // The hotkey goes in small text under the title, as there's no room beside it for all four modes.
                bool keys = mode.Label != mode.Title;
                var text = new Rectangle(x + IconSize + Gap, r.Y, r.Right - x - IconSize - Gap, keys ? r.Height / 2 + 5 : r.Height);
                TextRenderer.DrawText(g, mode.Title, font, text, active ? Color.White : Theme.Text,
                    (keys ? TextFormatFlags.Bottom : TextFormatFlags.VerticalCenter) | TextFormatFlags.SingleLine);
                if (keys)
                    TextRenderer.DrawText(g, mode.Keys, Theme.Tiny, new Point(text.X + 1, text.Bottom - 1),
                        active ? Theme.Tint(mode.Accent, 0.75) : Theme.Muted);
            }
        }

        int SegmentAt(Point p)
        {
            for (int i = 0; i < Mode.All.Length; i++) if (Segment(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int at = SegmentAt(e.Location);
            if (at != hover) { hover = at; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int at = SegmentAt(e.Location);
            if (at >= 0 && Picked != null) Picked(Mode.All[at].State);
            base.OnMouseClick(e);
        }
    }

    // Color block style: the top of the window filled with the lights' color, with the status on it.
    class ColorBlock : PaintedControl
    {
        const int Pad = 16;
        const string SettingsText = "⚙  Settings";
        public Color DotColor = Color.Gray;
        public string Status = "", Countdown;
        public int Blynclights, Luxafors;
        public event Action SettingsClicked;
        Rectangle settingsBounds;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var fill = Theme.Soften(DotColor);
            using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, ClientRectangle);
            var text = Theme.TextOn(fill);
            var soft = Theme.Mix(text, fill, 0.25);

            TextRenderer.DrawText(g, "BusyLightz 2.0  ·  by KrabbzDev", Theme.Small, new Point(Pad, 12), soft);
            var size = TextRenderer.MeasureText(g, SettingsText, Theme.Small);
            settingsBounds = new Rectangle(Width - Pad - size.Width, 8, size.Width, size.Height + 8);
            TextRenderer.DrawText(g, SettingsText, Theme.Small, settingsBounds, text, TextFormatFlags.VerticalCenter);

            // The beta sticker on the right; device tiles are in the Active Devices panel.
            const int tiles = 0;
            DeviceTiles.BetaSticker(g, Width - Pad - 62, Height - 20);

            TextRenderer.DrawText(g, Status, Theme.Hero, new Rectangle(Pad - 2, 36, Width - 2 * Pad - tiles - 8, 40), text,
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            if (Countdown == null) return;
            var pill = TextRenderer.MeasureText(g, Countdown, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
            Theme.Pill(g, Countdown, Pad + pill.Width + 16, 100, Theme.Mix(fill, Color.Black, 0.25), text);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = settingsBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (settingsBounds.Contains(e.Location) && SettingsClicked != null) SettingsClicked();
            base.OnMouseClick(e);
        }
    }

    // Fluent list style: the four modes as rows, like Windows Settings. The picked row is tinted and ticked.
    class ModeList : PaintedControl
    {
        public const int RowHeight = 44;
        public string Active;
        public event Action<string> Picked;
        int hover = -1;

        public ModeList() { Cursor = Cursors.Hand; AccessibleName = "Modes"; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), 8, Theme.Card, Theme.Border);
            for (int i = 0; i < Mode.All.Length; i++)
            {
                var mode = Mode.All[i];
                var row = new Rectangle(1, i * RowHeight + 1, Width - 2, RowHeight - 1);
                bool active = mode.State == Active;
                if (active)
                {
                    using (var tint = new SolidBrush(Theme.Wash(mode.Accent, 0.85))) g.FillRectangle(tint, row);
                    using (var bar = new SolidBrush(mode.Accent)) g.FillRectangle(bar, row.X, row.Y + 8, 3, row.Height - 16);
                }
                else if (i == hover)
                    using (var tint = new SolidBrush(Theme.Wash(Theme.Text, 0.94))) g.FillRectangle(tint, row);
                if (i > 0) using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 12, row.Y - 1, Width - 12, row.Y - 1);

                var rowBack = active ? Theme.Wash(mode.Accent, 0.85) : i == hover ? Theme.Wash(Theme.Text, 0.94) : Theme.Card;
                mode.DrawIcon(g, new Rectangle(14, row.Y + (row.Height - 16) / 2, 16, 16), mode.Accent, rowBack);
                TextRenderer.DrawText(g, mode.Title, Theme.Strong, new Rectangle(38, row.Y, row.Width - 120, row.Height), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                string right = (mode.Keys ?? "") + (active ? "   ✓" : "");
                TextRenderer.DrawText(g, right, Theme.Body, new Rectangle(row.Right - 140, row.Y, 126, row.Height),
                    active ? mode.Accent : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int at = Math.Min(e.Y / RowHeight, Mode.All.Length - 1);
            if (at != hover) { hover = at; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int at = Math.Min(e.Y / RowHeight, Mode.All.Length - 1);
            if (Picked != null) Picked(Mode.All[at].State);
            base.OnMouseClick(e);
        }
    }

    // The main window: the current color, the four main modes, and the extras in the footer, laid out in the
    // style picked in Settings. Closing it hides it; the tray app keeps running.
    class ControlForm : Form
    {
        const int WindowWidth = 440, Pad = 16;
        const string BackNowLink = "Back to Teams now";

        readonly TrayContext tray;
        readonly OverrideStrip strip = new OverrideStrip();
        readonly ModernButton partyButton = new ModernButton { Text = "Party Mode", Rainbow = true, Glyph = "" };
        // Only the parts the chosen style uses are created; the rest stay null.
        StatusCard status;
        ModeTile[] tiles;
        OrbitRing ring;
        PillSwitcher pills;
        ColorBlock block;
        ModeList list;
        readonly DevicePanel devices;

        public ControlForm(TrayContext tray)
        {
            this.tray = tray;
            Text = Program.AppName;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.Body;
            BackColor = Theme.Page;
            DoubleBuffered = true;
            string style = AppSettings.WindowStyle;
            int width = WindowWidth - 2 * Pad, y;

            // Top: the status, shown the style's way.
            if (style == WindowStyles.ColorBlock)
            {
                // Tall enough for the light tiles with the beta sticker under them.
                block = new ColorBlock { Bounds = new Rectangle(0, 0, WindowWidth, 146) };
                block.SettingsClicked += delegate { tray.ShowSettings(); };
                Controls.Add(block);
                y = block.Bottom + 12;
            }
            else if (style == WindowStyles.Orbit)
            {
                ring = new OrbitRing { Bounds = new Rectangle(0, 0, WindowWidth, 264) };
                ring.SettingsClicked += delegate { tray.ShowSettings(); };
                Controls.Add(ring);
                y = ring.Bottom + 12;
            }
            else
            {
                y = AddHeader() + 14;
                // Tall enough for the light tiles under the override pill.
                status = new StatusCard { Bounds = new Rectangle(Pad, y, width, 110) };
                Controls.Add(status);
                y = status.Bottom + 8;
            }

            // Every device BusyLightz works with, greyed out when it isn't plugged in.
            devices = new DevicePanel { Bounds = new Rectangle(Pad, y, width, DevicePanel.PanelHeight) };
            Controls.Add(devices);
            y = devices.Bottom + 8;

            // One line: the override countdown, or a short tip.
            strip.Bounds = new Rectangle(Pad, y, width, 38);
            strip.LinkClicked += delegate
            {
                if (tray.OverrideOn) tray.SetState(LightState.FollowTeams);
                else tray.OpenHelp();
            };
            Controls.Add(strip);
            y = strip.Bottom + 10;

            // The four modes.
            if (style == WindowStyles.Orbit)
            {
                pills = new PillSwitcher { Bounds = new Rectangle(Pad, y, width, 46) };
                pills.Picked += state => tray.SetState(state);
                Controls.Add(pills);
                y = pills.Bottom;
            }
            else if (style == WindowStyles.Fluent)
            {
                list = new ModeList { Bounds = new Rectangle(Pad, y, width, Mode.All.Length * ModeList.RowHeight + 1) };
                list.Picked += state => tray.SetState(state);
                Controls.Add(list);
                y = list.Bottom;
            }
            else y = AddTiles(y);

            var footer = AddFooter(y + 16, style == WindowStyles.Orbit);
            ClientSize = new Size(WindowWidth, footer.Bottom);

            // The window follows the tray's color, including On a call's flashing (see TrayContext.UpdateDisplay).
            tray.DisplayChanged += RefreshDisplay;
            RefreshDisplay();
            HandleCreated += delegate { Theme.TitleBar(this); };
        }

        // Name, and Settings on the right (Fluent list; the other styles draw their own). Returns its bottom.
        int AddHeader()
        {
            var back = Theme.Card;
            var header = new Panel { Bounds = new Rectangle(0, 0, WindowWidth, 52), BackColor = back };
            header.Paint += (sender, e) =>
            {
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
                DeviceTiles.BetaSticker(e.Graphics, WindowWidth - Pad - 92 - 82, 26);
            };
            header.Controls.Add(new Label { Text = "BusyLightz 2.0", Font = Theme.Strong, ForeColor = Theme.Text, AutoSize = true, Location = new Point(Pad, 9) });
            header.Controls.Add(new Label { Text = "by KrabbzDev", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(Pad + 1, 29) });
            var settings = new Button
            {
                Text = "⚙  Settings",
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Theme.Text,
                Font = Theme.Small,
                Cursor = Cursors.Hand,
                Bounds = new Rectangle(WindowWidth - Pad - 92, 12, 92, 28),
            };
            settings.FlatAppearance.BorderColor = Theme.Border;
            settings.FlatAppearance.MouseOverBackColor = Theme.Page;
            settings.Click += delegate { tray.ShowSettings(); };
            header.Controls.Add(settings);
            Controls.Add(header);
            return header.Bottom;
        }

        // The modes as a 2x2 grid of tiles (Color block). Returns their bottom.
        int AddTiles(int y)
        {
            int tileWidth = (WindowWidth - 2 * Pad - 8) / 2, tileHeight = 44;
            tiles = new ModeTile[Mode.All.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                var mode = Mode.All[i];
                var tile = new ModeTile(mode.State, mode.Label, "", mode.Accent) { Compact = true };
                tile.Bounds = new Rectangle(Pad + (i % 2) * (tileWidth + 8), y + (i / 2) * (tileHeight + 8), tileWidth, tileHeight);
                tile.TabIndex = i;
                tile.Click += delegate { tray.SetState(tile.State); };
                Controls.Add(tile);
                tiles[i] = tile;
            }
            return tiles[tiles.Length - 1].Bottom;
        }

        // Party Mode, then version, links and the beta note. Flat drops the card and the line above it.
        Panel AddFooter(int top, bool flat)
        {
            var footer = new Panel { Bounds = new Rectangle(0, top, WindowWidth, 68), BackColor = flat ? Theme.Page : Theme.Card };
            footer.Paint += (sender, e) =>
            {
                if (!flat) using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            // A rainbow button with a smiley, whose colors drift while hovered; during a party it ends it.
            // Small, at the lower left, with the version and links beside it.
            partyButton.Bounds = new Rectangle(Pad, 16, 124, 32);
            partyButton.Click += delegate
            {
                tray.SetState(tray.Light.State == LightState.Party ? LightState.FollowTeams : LightState.Party);
            };
            footer.Controls.Add(partyButton);

            // Beside Party Mode: the version on one line, the links on the next, with room between them.
            DateTime built;
            string date = DateTime.TryParse(Program.BuildDate, out built) ? built.ToString("MMM d, yyyy") : Program.BuildDate;
            footer.Controls.Add(new Label
            {
                Text = "Version " + Program.VersionText + "   ·   Last updated " + date,
                ForeColor = Theme.Muted,
                Font = Theme.Small,
                AutoSize = true,
                Location = new Point(Pad + 124 + 14, 13),
            });
            if (Program.UpdateRepo != null)
            {
                const string UpdatesLink = "Check for updates", ProjectLink = "Project page", HelpLink = "Help", Gap = "   ·   ";
                string linksText = UpdatesLink + Gap + ProjectLink + Gap + HelpLink;
                var links = new LinkLabel
                {
                    Text = linksText,
                    ForeColor = Theme.Muted,
                    LinkColor = Theme.Muted,
                    ActiveLinkColor = Theme.Text,
                    Font = Theme.Small,
                    AutoSize = true,
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    Location = new Point(Pad + 124 + 14, 35),
                };
                links.Links.Clear();
                links.Links.Add(linksText.IndexOf(UpdatesLink), UpdatesLink.Length, "updates");
                links.Links.Add(linksText.IndexOf(ProjectLink), ProjectLink.Length, "project");
                links.Links.Add(linksText.LastIndexOf(HelpLink), HelpLink.Length, "help");
                links.LinkClicked += (sender, e) =>
                {
                    if ((string)e.Link.LinkData == "updates") tray.CheckForUpdates(true);
                    else if ((string)e.Link.LinkData == "help") tray.OpenHelp();
                    else tray.OpenProjectPage();
                };
                footer.Controls.Add(links);
            }

            // The beta note and the close-to-tray hint live in a tooltip so the window stays clean.
            var tips = new ToolTip { AutoPopDelay = 15000 };
            tips.SetToolTip(footer, "This app replaces the Embrava Connect app. When you're on a call, your light turns red " +
                "to let others know. It's still in beta.\n\nClosing this window keeps the tray icon and lights running.");
            Controls.Add(footer);
            return footer;
        }

        // Switching style or Light/Dark mode replaces the window, so stop listening once it's gone.
        protected override void Dispose(bool disposing)
        {
            if (disposing) tray.DisplayChanged -= RefreshDisplay;
            base.Dispose(disposing);
        }

        // How much of an override is left, from its "14:00" (or "1:05:00") time left and its full length; 1 (a full
        // ring) with no countdown.
        static float TimeLeftFraction(string left)
        {
            if (string.IsNullOrEmpty(left)) return 1;
            int seconds = 0;
            foreach (string part in left.Split(':'))
            {
                int n;
                if (!int.TryParse(part.Trim(), out n)) return 1;
                seconds = seconds * 60 + n;
            }
            int total = AppSettings.OverrideMinutes * 60;
            return total <= 0 ? 1 : Math.Max(0f, Math.Min(1f, seconds / (float)total));
        }

        void RefreshDisplay()
        {
            string picked = tray.Light.State;
            string statusText = tray.StatusText;
            string left = tray.OverrideTimeLeft;
            // Lights turned off (Disable USB Light Integration) show no tiles.
            bool enabled = tray.Light.LightsEnabled;
            int blynclights = enabled ? tray.Light.CountOf(LightKind.Blynclight) : 0;
            int luxafors = enabled ? tray.Light.CountOf(LightKind.Luxafor) : 0;
            var shown = tray.DisplayColor;
            devices.Blynclights = blynclights;
            devices.Luxafors = luxafors;
            devices.K2 = tray.K2Connected;
            devices.DotColor = shown;
            devices.Invalidate();
            if (status != null)
            {
                status.Blynclights = blynclights;
                status.Luxafors = luxafors;
                status.DotColor = shown;
                status.Status = statusText;
                status.OverrideOn = tray.OverrideOn;
                status.Invalidate();
            }
            if (ring != null)
            {
                // "Teams: Available" shows as a big "Available" over "From Teams Status", and "On a call (Zoom)" as
                // "On a call" over "Zoom". On an override, the time left goes underneath instead.
                int colon = statusText.IndexOf(": "), paren = statusText.IndexOf(" (");
                string big = statusText, small = "";
                if (colon >= 0) { big = statusText.Substring(colon + 2); small = "From Teams Status"; }
                else if (paren >= 0 && statusText.EndsWith(")"))
                {
                    big = statusText.Substring(0, paren);
                    small = statusText.Substring(paren + 2, statusText.Length - paren - 3);
                }
                ring.DotColor = shown;
                ring.Blynclights = blynclights;
                ring.Luxafors = luxafors;
                ring.Big = big;
                ring.Glyph = OrbitRing.GlyphFor(big);
                ring.Progress = TimeLeftFraction(left);
                ring.OverrideOn = tray.OverrideOn;
                ring.Small = left != null ? left + " left" : tray.OverrideOn ? "Until you change it" : small;
                ring.Invalidate();
            }
            if (block != null)
            {
                block.DotColor = shown;
                block.Blynclights = blynclights;
                block.Luxafors = luxafors;
                block.Status = statusText;
                block.Countdown = !tray.OverrideOn ? null : "Manual override on" + (left != null ? "  ·  " + left + " left" : "");
                block.Invalidate();
            }

            strip.Highlighted = tray.OverrideOn;
            strip.Wrapped = false;
            if (!tray.OverrideOn)
            {
                strip.Message = HotkeyTip();
                strip.Link = null;
            }
            else
            {
                strip.Message = left != null ? "⏱  Back to Teams status in " + left : "Stays on until you change it";
                strip.Link = BackNowLink;
            }
            strip.Invalidate();

            if (tiles != null) foreach (var tile in tiles) tile.Active = tile.State == picked;
            if (pills != null) { pills.Active = picked; pills.Invalidate(); }
            if (list != null) { list.Active = picked; list.Invalidate(); }
            partyButton.Text = picked == LightState.Party ? "Stop the party" : "Party Mode";
        }

        static string HotkeyTip()
        {
            return AppSettings.HotkeysEnabled ? "Tip: Alt+R and Alt+G work from any app." : "Pick a mode below.";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            tray.DisplayChanged -= RefreshDisplay;
            base.OnFormClosing(e);
        }
    }
}
