using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WzComparerR2.WzLib;

namespace WzComparerR2.CanvasViewer
{
    internal sealed class CanvasGrid : Control
    {
        private readonly VScrollBar scroll = new VScrollBar { Dock = DockStyle.Right };
        private readonly ToolTip tip = new ToolTip();
        private readonly Dictionary<int, Bitmap> thumbnails = new Dictionary<int, Bitmap>();
        private int cellSize = 144;
        private int selected = -1;
        private int hovered = -1;
        private bool more;
        public readonly List<CanvasEntry> Entries = new List<CanvasEntry>();
        public event Action DemandChanged;
        public event Action<CanvasEntry> OpenRequested;
        public event Action<CanvasEntry, Point> ContextRequested;

        public CanvasGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = SystemColors.Window;
            TabStop = true;
            Controls.Add(scroll);
            scroll.ValueChanged += (s, e) => { Invalidate(); DemandChanged?.Invoke(); };
        }

        private int CellWidth => cellSize + 16;
        private int CellHeight => cellSize + 42;
        public int Columns => Math.Max(1, (ClientSize.Width - scroll.Width) / CellWidth);
        private int RowsVisible => Math.Max(1, (ClientSize.Height + CellHeight - 1) / CellHeight);
        public int FirstIndex => (scroll.Value / CellHeight) * Columns;
        public int DesiredCount => FirstIndex + Columns * (RowsVisible * 3 + 1);
        public int CellSize => cellSize;
        public int SelectedIndex => selected;

        public void Reset()
        {
            foreach (var bitmap in thumbnails.Values) bitmap.Dispose();
            thumbnails.Clear();
            Entries.Clear();
            selected = hovered = -1;
            tip.SetToolTip(this, null);
            scroll.Value = 0;
            SetMore(false);
        }

        public void SetMore(bool value)
        {
            more = value;
            UpdateScroll();
            Invalidate();
        }

        public void ItemsAdded()
        {
            UpdateScroll();
            Invalidate();
        }

        private void UpdateScroll()
        {
            int rows = (Entries.Count + Columns - 1) / Columns + (more ? 1 : 0);
            scroll.LargeChange = Math.Max(1, ClientSize.Height);
            scroll.SmallChange = Math.Max(24, CellHeight / 3);
            scroll.Maximum = Math.Max(0, rows * CellHeight - 1);
            int maxValue = Math.Max(0, scroll.Maximum - scroll.LargeChange + 1);
            if (scroll.Value > maxValue) scroll.Value = maxValue;
            scroll.Enabled = maxValue > 0;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (scroll == null) return;
            UpdateScroll();
            Invalidate();
            DemandChanged?.Invoke();
        }

        public bool LoadOneThumbnail()
        {
            int first = Math.Max(0, FirstIndex - Columns);
            int end = Math.Min(Entries.Count, FirstIndex + (RowsVisible + 2) * Columns);
            var remove = new List<int>();
            foreach (var pair in thumbnails)
                if (pair.Key < first || pair.Key >= end) remove.Add(pair.Key);
            foreach (int index in remove) { thumbnails[index].Dispose(); thumbnails.Remove(index); }
            // Visible rows first, followed by a small look-ahead. Never decode in Paint.
            for (int i = FirstIndex; i < end; i++)
            {
                var entry = Entries[i];
                if (thumbnails.ContainsKey(i) || entry.Error != null) continue;
                try
                {
                    var png = CanvasSource.Resolve(entry.Node)?.Value as Wz_Png;
                    if (png == null) throw new InvalidOperationException("引用目标不是 Canvas");
                    using (var original = png.ExtractPng())
                    {
                        if (original == null) throw new InvalidOperationException("图片格式暂不支持");
                        double scale = Math.Min((double)cellSize / original.Width, (double)cellSize / original.Height);
                        var bitmap = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
                        try
                        {
                            using (var g = Graphics.FromImage(bitmap))
                            {
                                g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = PixelOffsetMode.Half;
                                g.DrawImage(original, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            }
                            thumbnails.Add(i, bitmap);
                        }
                        catch { bitmap.Dispose(); throw; }
                    }
                }
                catch (Exception ex) { entry.Error = ex.Message; }
                Invalidate();
                return true;
            }
            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int end = Math.Min(Entries.Count, FirstIndex + (RowsVisible + 1) * Columns);
            for (int i = FirstIndex; i < end; i++)
            {
                int x = (i % Columns) * CellWidth + 6;
                int y = (i / Columns) * CellHeight - scroll.Value + 6;
                var tile = new Rectangle(x, y, cellSize + 4, cellSize + 30);
                if (i == selected) e.Graphics.FillRectangle(SystemBrushes.Highlight, tile);
                var area = new Rectangle(x + 2, y + 2, cellSize, cellSize);
                CanvasPainting.Checker(e.Graphics, area);
                if (thumbnails.TryGetValue(i, out var bitmap))
                    e.Graphics.DrawImageUnscaled(bitmap, area.X + (area.Width - bitmap.Width) / 2, area.Y + (area.Height - bitmap.Height) / 2);
                else
                    TextRenderer.DrawText(e.Graphics, Entries[i].Error == null ? "加载中…" : "无法预览", Font, area,
                        Entries[i].Error == null ? SystemColors.GrayText : Color.Firebrick,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, Entries[i].Node.Text, Font,
                    new Rectangle(x + 2, y + cellSize + 6, cellSize, 22),
                    i == selected ? SystemColors.HighlightText : ForeColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            if (Entries.Count == 0)
                TextRenderer.DrawText(e.Graphics, more ? "正在查找 Canvas…" : "此目录没有 Canvas", Font,
                    ClientRectangle, SystemColors.GrayText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private int HitTest(Point p)
        {
            if (p.X < 0 || p.X >= Columns * CellWidth || p.X >= ClientSize.Width - scroll.Width) return -1;
            int index = ((p.Y + scroll.Value) / CellHeight) * Columns + p.X / CellWidth;
            return index >= 0 && index < Entries.Count ? index : -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            selected = HitTest(e.Location);
            Invalidate();
            if (e.Button == MouseButtons.Right && selected >= 0)
                ContextRequested?.Invoke(Entries[selected], PointToScreen(e.Location));
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && HitTest(e.Location) is int i && i >= 0) OpenRequested?.Invoke(Entries[i]);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            if (index == hovered) return;
            hovered = index;
            tip.SetToolTip(this, index < 0 ? null : Entries[index].Path +
                (Entries[index].Error == null ? "" : "\n" + Entries[index].Error));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                ResizeTiles(e.Delta);
            }
            else
            {
                int next = scroll.Value - Math.Sign(e.Delta) * Math.Max(32, CellHeight / 2);
                scroll.Value = Math.Max(0, Math.Min(Math.Max(0, scroll.Maximum - scroll.LargeChange + 1), next));
            }
            base.OnMouseWheel(e);
        }

        internal void ResizeTiles(int delta)
        {
            int anchor = FirstIndex;
            int next = Math.Max(48, Math.Min(512, cellSize + Math.Sign(delta) * 24));
            if (next == cellSize) return;
            cellSize = next;
            foreach (var bitmap in thumbnails.Values) bitmap.Dispose();
            thumbnails.Clear();
            UpdateScroll();
            scroll.Value = Math.Min(Math.Max(0, scroll.Maximum - scroll.LargeChange + 1), (anchor / Columns) * CellHeight);
            Invalidate();
            DemandChanged?.Invoke();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && selected >= 0) { OpenRequested?.Invoke(Entries[selected]); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Reset(); tip.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal static class CanvasPainting
    {
        public static void Checker(Graphics g, Rectangle area)
        {
            g.FillRectangle(Brushes.White, area);
            using (var brush = new HatchBrush(HatchStyle.LargeCheckerBoard, Color.FromArgb(230, 230, 230), Color.White))
                g.FillRectangle(brush, area);
        }
    }
}
