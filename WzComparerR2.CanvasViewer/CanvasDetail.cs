using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WzComparerR2.CanvasViewer
{
    internal sealed class CanvasDetail : Control
    {
        private Bitmap bitmap;
        private float scale = 1;
        private PointF origin;
        private Point lastMouse;
        public event Action<Point> ContextRequested;
        public event Action ScaleChanged;
        public float Zoom => scale;
        public Size ImageSize => bitmap?.Size ?? Size.Empty;
        public CanvasDetail()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
        }
        public void SetImage(Bitmap value)
        {
            bitmap?.Dispose();
            bitmap = value;
            Fit();
        }
        public void Fit()
        {
            scale = bitmap == null ? 1 : Math.Min(1, Math.Min((float)Math.Max(1, Width) / bitmap.Width,
                (float)Math.Max(1, Height) / bitmap.Height));
            Center();
        }
        public void ActualSize() { scale = 1; Center(); }
        private void Center()
        {
            origin = bitmap == null ? PointF.Empty : new PointF((Width - bitmap.Width * scale) / 2, (Height - bitmap.Height * scale) / 2);
            Invalidate();
            ScaleChanged?.Invoke();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            CanvasPainting.Checker(e.Graphics, ClientRectangle);
            if (bitmap == null) return;
            e.Graphics.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(bitmap, new RectangleF(origin.X, origin.Y, bitmap.Width * scale, bitmap.Height * scale));
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (bitmap == null) return;
            float next = Math.Max(0.01f, Math.Min(64, scale * (e.Delta > 0 ? 1.2f : 1 / 1.2f)));
            origin = new PointF(e.X - (e.X - origin.X) * next / scale, e.Y - (e.Y - origin.Y) * next / scale);
            scale = next;
            Invalidate();
            ScaleChanged?.Invoke();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Right) ContextRequested?.Invoke(PointToScreen(e.Location));
            if (e.Button == MouseButtons.Left) { lastMouse = e.Location; Capture = true; Cursor = Cursors.Hand; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Capture) return;
            origin.X += e.X - lastMouse.X;
            origin.Y += e.Y - lastMouse.Y;
            lastMouse = e.Location;
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
            Cursor = Cursors.Default;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) bitmap?.Dispose();
            base.Dispose(disposing);
        }
    }
}
