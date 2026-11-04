using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // Device tiles for the window's Active Devices panel, the way an iPhone shows a connected device: an app-icon style
    // rounded square with a drawing of the device (lights glowing in their current color), a green "connected" dot,
    // and its name underneath. The drawings are the app's own, simplified from the real lights.
    static class DeviceTiles
    {
        public const int TileSize = 44, LabelHeight = 16;
        static readonly Color ConnectedGreen = Color.FromArgb(52, 199, 89);

        // The Active Devices grid: every device BusyLightz works with, in a row across area. Plugged-in ones are lit
        // with a green dot and "Connected"; the rest are greyed out with "Disconnected". Height is what it needs.
        public const int GridHeight = TileSize + 6 + 2 * LabelHeight;

        public static void DrawGrid(Graphics g, Rectangle area, int blynclights, int luxafors, bool k2, Color glow,
            Color tile, Color border, Color text, Color muted)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var ring = MuteSyncButton.Ring;
            var devices = new[]
            {
                new GridDevice(blynclights > 1 ? blynclights + " Blynclights" : "Blynclight", blynclights > 0, r => DrawBlynclight(g, r, blynclights > 0 ? glow : Color.Gray)),
                new GridDevice(luxafors > 1 ? luxafors + " Luxafors" : "Luxafor", luxafors > 0, r => DrawLuxafor(g, r, luxafors > 0 ? glow : Color.Gray)),
                new GridDevice("Mutesync Button", ring.HasValue, r => DrawMuteSync(g, r, ring ?? Color.Gray)),
                new GridDevice("K2 USBC Key", k2, r => DrawK2(g, r)),
            };
            int slot = area.Width / devices.Length;
            for (int i = 0; i < devices.Length; i++)
            {
                var d = devices[i];
                int x = area.X + i * slot + (slot - TileSize) / 2;
                var r = new Rectangle(x, area.Y, TileSize, TileSize);
                Theme.FillRounded(g, r, 11, tile, border);
                d.Draw(Rectangle.Inflate(r, -7, -7));
                if (d.Connected)
                {
                    var dot = new Rectangle(r.Right - 11, r.Y - 3, 14, 14);
                    using (var edge = new SolidBrush(tile)) g.FillEllipse(edge, dot);
                    using (var green = new SolidBrush(ConnectedGreen)) g.FillEllipse(green, Rectangle.Inflate(dot, -2, -2));
                }
                else
                    // Greyed out: the tile's own color laid over most of the drawing.
                    using (var veil = Theme.Rounded(r, 11))
                    using (var brush = new SolidBrush(Color.FromArgb(150, tile)))
                        g.FillPath(brush, veil);
                var label = new Rectangle(area.X + i * slot, r.Bottom + 6, slot, LabelHeight);
                TextRenderer.DrawText(g, d.Name, Theme.Tiny, label, d.Connected ? text : muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);
                label.Offset(0, LabelHeight);
                TextRenderer.DrawText(g, d.Connected ? "Connected" : "Disconnected", Theme.Tiny, label,
                    d.Connected ? Theme.Mix(ConnectedGreen, text, 0.25) : muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);
            }
        }

        sealed class GridDevice
        {
            public readonly string Name;
            public readonly bool Connected;
            public readonly Action<Rectangle> Draw;
            public GridDevice(string name, bool connected, Action<Rectangle> draw) { Name = name; Connected = connected; Draw = draw; }
        }

        // K2 macro key: a clear base with two black keys side by side.
        static void DrawK2(Graphics g, Rectangle r)
        {
            var body = new Rectangle(r.X + 1, r.Y + 6, r.Width - 2, r.Height - 10);
            Theme.FillRounded(g, body, 5, Color.FromArgb(225, 228, 234), Color.FromArgb(170, 174, 184));
            // The see-through base's lower edge.
            using (var edge = new SolidBrush(Color.FromArgb(190, 194, 204)))
                g.FillRectangle(edge, body.X + 3, body.Bottom - 6, body.Width - 6, 3);
            int key = (body.Width - 9) / 2;
            for (int i = 0; i < 2; i++)
            {
                var cap = new Rectangle(body.X + 3 + i * (key + 3), body.Y - 4, key, key);
                Theme.FillRounded(g, cap, 3, Color.FromArgb(38, 38, 44), Color.FromArgb(16, 16, 20));
                using (var shine = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
                    g.FillRectangle(shine, cap.X + 3, cap.Y + 2, cap.Width - 6, 2);
            }
        }

        // mutesync: a rounded square pad with a glowing ring around its edge and a dark button in the middle.
        static void DrawMuteSync(Graphics g, Rectangle r, Color ringColor)
        {
            bool on;
            var lit = Lit(ringColor, out on);
            var pad = new Rectangle(r.X + 2, r.Y + 3, r.Width - 4, r.Height - 6);
            if (on)
                using (var halo = new SolidBrush(Color.FromArgb(70, lit))) g.FillEllipse(halo, Rectangle.Inflate(pad, 4, 4));
            // A dark base under the pad, like the real one's edge.
            Theme.FillRounded(g, new Rectangle(pad.X, pad.Y + 3, pad.Width, pad.Height - 2), 7, Color.FromArgb(40, 40, 46), Color.FromArgb(40, 40, 46));
            Theme.FillRounded(g, new Rectangle(pad.X, pad.Y, pad.Width, pad.Height - 3), 7, lit, Theme.Mix(lit, Color.Black, 0.2));
            // A soft shine across the top.
            using (var shine = new SolidBrush(Color.FromArgb(on ? 90 : 140, 255, 255, 255)))
                g.FillEllipse(shine, pad.X + pad.Width / 5, pad.Y + 2, pad.Width * 3 / 5, pad.Height / 5);
            int size = pad.Width * 2 / 5;
            var button = new Rectangle(pad.X + (pad.Width - size) / 2, pad.Y + (pad.Height - 3 - size) / 2, size, size);
            Theme.FillRounded(g, button, 3, Color.FromArgb(34, 34, 40), Color.FromArgb(20, 20, 24));
        }

        // A large Blynclight and Luxafor side by side, glowing in glow (gray or black draws them unlit), for Light
        // Control.
        public static void DrawLightsLarge(Graphics g, Rectangle r, Color glow)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int s = Math.Min(r.Width / 2, r.Height) - 8;
            int top = r.Y + (r.Height - s) / 2;
            int gap = (r.Width - 2 * s) / 3;
            DrawBlynclight(g, new Rectangle(r.X + gap, top, s, s), glow);
            DrawLuxafor(g, new Rectangle(r.X + 2 * gap + s, top, s, s), glow);
        }

        // A large Mutesync Button for its Settings page: a rounded pad whose edge glows in the ring's color, with a
        // dark square button in the middle. ring is null when no button is connected (drawn grey and unlit).
        public static void DrawMuteSyncLarge(Graphics g, Rectangle r, Color? ring)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool on;
            var lit = Lit(ring ?? Color.Gray, out on);
            int s = Math.Min(r.Width, r.Height);
            var pad = new Rectangle(r.X + (r.Width - s) / 2 + s / 10, r.Y + (r.Height - s) / 2 + s / 10, s * 8 / 10, s * 7 / 10);
            int radius = pad.Width / 5;
            if (on)
            {
                // A soft glow around the pad, like the real ring lighting the desk.
                var glowArea = Rectangle.Inflate(pad, s / 8, s / 8);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(glowArea);
                    using (var glow = new PathGradientBrush(path))
                    {
                        glow.CenterColor = Color.FromArgb(150, lit);
                        glow.SurroundColors = new[] { Color.FromArgb(0, lit) };
                        g.FillEllipse(glow, glowArea);
                    }
                }
            }
            // The dark base under the pad.
            var dark = Color.FromArgb(36, 36, 42);
            Theme.FillRounded(g, new Rectangle(pad.X, pad.Y + s / 14, pad.Width, pad.Height), radius, dark, dark);
            // The pad, lit from the top.
            using (var path = Theme.Rounded(pad, radius))
            using (var fill = new LinearGradientBrush(pad, Theme.Mix(lit, Color.White, 0.25), Theme.Mix(lit, Color.Black, 0.15), LinearGradientMode.Vertical))
                g.FillPath(fill, path);
            using (var path = Theme.Rounded(pad, radius))
            using (var edge = new Pen(Theme.Mix(lit, Color.Black, 0.25), 1.5f))
                g.DrawPath(edge, path);
            // The button in the middle, with a microphone on it.
            int size = pad.Width * 9 / 20;
            var button = new Rectangle(pad.X + (pad.Width - size) / 2, pad.Y + (pad.Height - size) / 2, size, size);
            using (var path = Theme.Rounded(button, size / 6))
            using (var fill = new LinearGradientBrush(button, Color.FromArgb(58, 58, 66), Color.FromArgb(24, 24, 28), LinearGradientMode.Vertical))
                g.FillPath(fill, path);
            if (Theme.Icons != null)
                using (var icon = new Font(Theme.Icons.FontFamily, size / 4f, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "", icon, button, Color.FromArgb(150, 150, 160),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            // A soft shine across the top of the pad.
            using (var shine = new SolidBrush(Color.FromArgb(on ? 70 : 110, 255, 255, 255)))
                g.FillEllipse(shine, pad.X + pad.Width / 6, pad.Y + pad.Height / 20, pad.Width * 2 / 3, pad.Height / 6);
        }

        // A large K2 USBC Key for its Settings page: a clear base with two black keys side by side. A pressed key is
        // drawn lit in the accent color; connected false draws it greyed out.
        public static void DrawK2Large(Graphics g, Rectangle r, bool connected, bool leftLit, bool rightLit)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int s = Math.Min(r.Width, r.Height);
            var body = new Rectangle(r.X + (r.Width - s) / 2 + s / 12, r.Y + (r.Height - s) / 2 + s * 3 / 10, s * 10 / 12, s * 5 / 10);
            int radius = s / 12;
            // The clear acrylic base: pale, with a darker lower edge and the USB-C port.
            Theme.FillRounded(g, new Rectangle(body.X, body.Y + s / 20, body.Width, body.Height), radius,
                Color.FromArgb(180, 186, 196), Color.FromArgb(150, 156, 168));
            using (var path = Theme.Rounded(body, radius))
            using (var fill = new LinearGradientBrush(body, Color.FromArgb(240, 242, 246), Color.FromArgb(206, 210, 218), LinearGradientMode.Vertical))
                g.FillPath(fill, path);
            using (var path = Theme.Rounded(body, radius))
            using (var edge = new Pen(Color.FromArgb(160, 166, 178), 1.2f))
                g.DrawPath(edge, path);
            var port = new Rectangle(body.X + body.Width / 2 - s / 14, body.Bottom - s / 24, s / 7, s / 22);
            Theme.FillRounded(g, port, port.Height / 2, Color.FromArgb(70, 70, 78), Color.FromArgb(50, 50, 56));
            // Two keys, standing up from the base.
            int key = body.Width * 4 / 10, gap = body.Width / 14;
            int left = body.X + (body.Width - 2 * key - gap) / 2;
            for (int i = 0; i < 2; i++)
            {
                bool pressed = i == 0 ? leftLit : rightLit;
                var cap = new Rectangle(left + i * (key + gap), body.Y - key / 2 + (pressed ? s / 40 : 0), key, key);
                if (pressed)
                    using (var halo = new SolidBrush(Color.FromArgb(90, Theme.Teams))) g.FillEllipse(halo, Rectangle.Inflate(cap, s / 14, s / 14));
                var top = pressed ? Theme.Mix(Theme.Teams, Color.White, 0.2) : Color.FromArgb(56, 56, 64);
                var bottom = pressed ? Theme.Teams : Color.FromArgb(20, 20, 24);
                using (var path = Theme.Rounded(cap, key / 7))
                using (var fill = new LinearGradientBrush(cap, top, bottom, LinearGradientMode.Vertical))
                    g.FillPath(fill, path);
                // The key's dished top.
                var dish = Rectangle.Inflate(cap, -key / 7, -key / 7);
                using (var path = Theme.Rounded(dish, key / 10))
                using (var pen = new Pen(Color.FromArgb(pressed ? 90 : 40, 255, 255, 255), 1.2f))
                    g.DrawPath(pen, path);
            }
            if (!connected)
                using (var veil = new SolidBrush(Color.FromArgb(130, Theme.Card))) g.FillRectangle(veil, r);
        }

        // Lit color for the drawings, or a soft gray when the lights are off.
        static Color Lit(Color glow, out bool on)
        {
            on = !(glow.R < 70 && glow.G < 70 && glow.B < 70) && glow != Color.Gray;
            return on ? Theme.Soften(glow) : Color.FromArgb(200, 200, 206);
        }

        // Blynclight: a rounded light on a dark base.
        static void DrawBlynclight(Graphics g, Rectangle r, Color glow)
        {
            bool on;
            var lit = Lit(glow, out on);
            var lens = new Rectangle(r.X + r.Width / 6, r.Y + 1, r.Width * 2 / 3, r.Height * 3 / 5);
            if (on)
                using (var halo = new SolidBrush(Color.FromArgb(70, lit))) g.FillEllipse(halo, Rectangle.Inflate(lens, 4, 4));
            Theme.FillRounded(g, lens, lens.Width / 3, lit, Theme.Mix(lit, Color.Black, 0.2));
            // A soft shine across the top of the lens.
            using (var shine = new SolidBrush(Color.FromArgb(on ? 110 : 160, 255, 255, 255)))
                g.FillEllipse(shine, lens.X + lens.Width / 5, lens.Y + 2, lens.Width * 3 / 5, lens.Height / 4);
            var neck = new Rectangle(r.X + r.Width * 2 / 5, lens.Bottom - 1, r.Width / 5, r.Height / 8);
            var dark = Color.FromArgb(40, 40, 46);
            using (var brush = new SolidBrush(dark)) g.FillRectangle(brush, neck);
            var foot = new Rectangle(r.X + 2, neck.Bottom, r.Width - 4, r.Bottom - neck.Bottom);
            Theme.FillRounded(g, foot, 3, dark, dark);
        }

        // A yellow "Product Still BETA" sticker, tilted a little like a real one, centered on (x, y).
        public static void BetaSticker(Graphics g, int x, int y)
        {
            const string text = "Product Still BETA";
            var yellow = Color.FromArgb(255, 221, 0);
            using (var font = new Font("Segoe UI Black", 8.5f))
            {
                var size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
                var r = new Rectangle(-size.Width / 2 - 10, -size.Height / 2 - 4, size.Width + 20, size.Height + 8);
                var state = g.Save();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(x, y);
                g.RotateTransform(-6);
                using (var shadow = Theme.Rounded(new Rectangle(r.X + 2, r.Y + 3, r.Width, r.Height), 5))
                using (var dark = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                    g.FillPath(dark, shadow);
                Theme.FillRounded(g, r, 5, yellow, Theme.Mix(yellow, Color.Black, 0.25));
                // TextRenderer ignores the rotation, so the text is drawn with GDI+.
                using (var ink = new SolidBrush(Color.FromArgb(30, 30, 34)))
                using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, font, ink, new RectangleF(r.X, r.Y + 1, r.Width, r.Height), center);
                g.Restore(state);
            }
        }

        // Luxafor Flag: a white upright bar with the colored flag on its side.
        static void DrawLuxafor(Graphics g, Rectangle r, Color glow)
        {
            bool on;
            var lit = Lit(glow, out on);
            var flag = new Rectangle(r.X + 1, r.Y + r.Height / 6, r.Width * 11 / 20, r.Height * 9 / 20);
            if (on)
                using (var halo = new SolidBrush(Color.FromArgb(70, lit))) g.FillEllipse(halo, Rectangle.Inflate(flag, 4, 4));
            Theme.FillRounded(g, flag, 4, lit, Theme.Mix(lit, Color.Black, 0.2));
            var bar = new Rectangle(r.X + r.Width * 11 / 20 - 1, r.Y + 1, r.Width / 3, r.Height - 2);
            Theme.FillRounded(g, bar, 3, Color.White, Color.FromArgb(196, 196, 204));
        }
    }
}
