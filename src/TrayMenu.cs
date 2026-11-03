using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // Draws the tray menu in the style of Windows 11 menus, in the app's Light or Dark colors: rounded corners,
    // roomier rows, a rounded highlight, a colored dot beside each mode and a line icon beside the other items.
    class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        // Segoe MDL2 Assets check mark, drawn for ticked items.
        const string CheckGlyph = "";
        const int MarkerSize = 16;

        readonly Dictionary<ToolStripItem, string> glyphs = new Dictionary<ToolStripItem, string>();
        readonly Dictionary<ToolStripItem, Func<Color[]>> dots = new Dictionary<ToolStripItem, Func<Color[]>>();

        public ModernMenuRenderer() { RoundedEdges = false; }

        // A line icon (a Segoe MDL2 Assets character) beside item.
        public void Glyph(ToolStripItem item, string glyph) { glyphs[item] = glyph; }

        // A dot beside item in the colors colors() gives: one color fills it, several split it into slices,
        // none draws an empty ring.
        public void Dot(ToolStripItem item, Func<Color[]> colors) { dots[item] = colors; }

        // Makes the menu look the part: font, row height, rounded corners on Windows 11.
        public static void Style(ContextMenuStrip menu, ModernMenuRenderer renderer)
        {
            menu.Renderer = renderer;
            menu.Font = new Font("Segoe UI", 9.5f);
            menu.ShowCheckMargin = false;
            menu.ShowImageMargin = true;
            menu.ImageScalingSize = new Size(20, 20);
            menu.Padding = new Padding(4);
            foreach (ToolStripItem item in menu.Items)
                if (item is ToolStripMenuItem) item.Padding = new Padding(2, 5, 2, 5);
            menu.HandleCreated += delegate
            {
                int round = DWMWCP_ROUNDSMALL;
                try { DwmSetWindowAttribute(menu.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int)); }
                catch (Exception) { } // Before Windows 11: square corners.
            };
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUNDSMALL = 3;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var fill = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        // The icon column is part of the menu's plain background.
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        // Ticks are drawn as part of the marker beside each item instead.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var menuItem = e.Item as ToolStripMenuItem;
            bool shortcut = menuItem != null && e.Text == menuItem.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled || shortcut ? Theme.Muted : Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (e.Item.Selected && e.Item.Enabled)
            {
                var r = new Rectangle(2, 1, e.Item.Width - 5, e.Item.Height - 3);
                Theme.FillRounded(g, r, 5, Theme.Wash(Theme.Teams, 0.82), Theme.Wash(Theme.Teams, 0.82));
            }
            DrawMarker(g, e.Item);
        }

        void DrawMarker(Graphics g, ToolStripItem item)
        {
            var menuItem = item as ToolStripMenuItem;
            bool ticked = menuItem != null && menuItem.Checked;
            var box = new Rectangle(10, (item.Height - MarkerSize) / 2, MarkerSize, MarkerSize);
            Func<Color[]> colors;

            if (dots.TryGetValue(item, out colors))
            {
                // The mode that's on gets a ring in the accent color around its dot.
                if (ticked)
                    using (var ring = new Pen(Theme.Teams, 2)) g.DrawEllipse(ring, box.X, box.Y, box.Width - 1, box.Height - 1);
                DrawDot(g, Rectangle.Inflate(box, -3, -3), colors());
            }
            else if (ticked)
            {
                // An on/off item that's on: a filled box with a check mark, like a Windows 11 check box.
                Theme.FillRounded(g, box, 4, Theme.Teams, Theme.Teams);
                DrawGlyph(g, CheckGlyph, box, Color.White, 8f);
            }
            else
            {
                string glyph;
                if (glyphs.TryGetValue(item, out glyph))
                    DrawGlyph(g, glyph, box, item.Enabled ? Theme.Text : Theme.Muted, 10.5f);
            }
        }

        static void DrawDot(Graphics g, Rectangle r, Color[] colors)
        {
            if (colors == null || colors.Length == 0)
            {
                using (var pen = new Pen(Theme.Muted, 1.5f)) g.DrawEllipse(pen, r);
                return;
            }
            float sweep = 360f / colors.Length;
            for (int i = 0; i < colors.Length; i++)
                using (var fill = new SolidBrush(colors[i])) g.FillPie(fill, r, -90 + i * sweep, sweep);
        }

        static readonly Dictionary<float, Font> glyphFonts = new Dictionary<float, Font>();

        static void DrawGlyph(Graphics g, string glyph, Rectangle box, Color color, float size)
        {
            if (Theme.Icons == null) return; // No icon font before Windows 10.
            Font font;
            if (!glyphFonts.TryGetValue(size, out font)) glyphFonts[size] = font = new Font(Theme.Icons.FontFamily, size);
            TextRenderer.DrawText(g, glyph, font, box, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
