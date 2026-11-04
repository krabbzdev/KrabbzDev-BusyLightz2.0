using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // A tile for each kind of light that's plugged in, the way an iPhone shows a connected device: an app-icon style
    // rounded square with a drawing of the light glowing in its current color, a green "connected" dot, and its name
    // underneath. The drawings are the app's own, simplified from the real lights.
    static class DeviceTiles
    {
        public const int TileSize = 44, Gap = 16, LabelHeight = 16;
        public const int Height = TileSize + 3 + LabelHeight;
        static readonly Color ConnectedGreen = Color.FromArgb(52, 199, 89);

        // How wide the tiles for these lights are; 0 when none are plugged in.
        public static int Width(int blynclights, int luxafors)
        {
            int count = (blynclights > 0 ? 1 : 0) + (luxafors > 0 ? 1 : 0);
            return count == 0 ? 0 : count * TileSize + (count - 1) * Gap;
        }

        // Draws the tiles left to right from (x, y). glow is the lights' color (black or gray draws them unlit); tile is
        // the tile's fill, and label the color of the names.
        public static void Draw(Graphics g, int x, int y, int blynclights, int luxafors, Color glow, Color tile, Color border, Color label)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (blynclights > 0)
            {
                Tile(g, x, y, tile, border, label, blynclights, "Blynclight", r => DrawBlynclight(g, r, glow));
                x += TileSize + Gap;
            }
            if (luxafors > 0) Tile(g, x, y, tile, border, label, luxafors, "Luxafor", r => DrawLuxafor(g, r, glow));
        }

        static void Tile(Graphics g, int x, int y, Color fill, Color border, Color label, int count, string name, Action<Rectangle> drawLight)
        {
            var r = new Rectangle(x, y, TileSize, TileSize);
            Theme.FillRounded(g, r, 11, fill, border);
            drawLight(Rectangle.Inflate(r, -7, -7));

            // The green dot that says it's seen and connected, ringed in the tile color so it stands off the corner.
            var dot = new Rectangle(r.Right - 11, r.Y - 3, 14, 14);
            using (var ring = new SolidBrush(fill)) g.FillEllipse(ring, dot);
            using (var green = new SolidBrush(ConnectedGreen)) g.FillEllipse(green, Rectangle.Inflate(dot, -2, -2));

            string text = count > 1 ? count + " " + name : name;
            TextRenderer.DrawText(g, text, Theme.Tiny, new Rectangle(x - Gap / 2, r.Bottom + 3, TileSize + Gap, LabelHeight), label,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);
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
