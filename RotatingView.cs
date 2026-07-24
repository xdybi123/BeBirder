using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EarPicker
{
    /// <summary>
    /// Displays the live camera frame either as a rotated, circularly-cropped
    /// disc (matching the device's round lens, and following the sensor's
    /// reported rotation) or, with rotation display off, as a plain
    /// letterboxed frame.
    /// </summary>
    sealed class RotatingView : Control
    {
        readonly object sync = new object();
        Image image;
        double rotation;
        bool applyRotation = true;

        public bool ApplyRotation
        {
            get { return applyRotation; }
            set { if (applyRotation != value) { applyRotation = value; Invalidate(); } }
        }

        public double Rotation
        {
            get { return rotation; }
            set { if (rotation != value) { rotation = value; Invalidate(); } }
        }

        public RotatingView()
        {
            DoubleBuffered = true;
        }

        /// <summary>Adopts a decoded frame, disposing whatever was shown before. Safe to call from any thread.</summary>
        public void SetImage(Image value)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<Image>(SetImage), value); }
                catch { value.Dispose(); }
                return;
            }
            Image old;
            lock (sync) { old = image; image = value; }
            if (old != null) old.Dispose();
            Invalidate();
        }

        public void ClearImage()
        {
            Image old;
            lock (sync) { old = image; image = null; }
            if (old != null) old.Dispose();
            if (!IsDisposed && !Disposing) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            lock (sync)
            {
                if (image == null || Width <= 0 || Height <= 0) return;

                Graphics g = e.Graphics;
                GraphicsState state = g.Save();
                try
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.TranslateTransform(Width / 2f, Height / 2f);

                    if (ApplyRotation)
                    {
                        float side = Math.Min(Width, Height);
                        CircularFrame.Draw(g, image, side, rotation);
                    }
                    else
                    {
                        float scale = Math.Min((float)Width / image.Width, (float)Height / image.Height);
                        float width = image.Width * scale, height = image.Height * scale;
                        g.DrawImage(image, -width / 2f, -height / 2f, width, height);
                    }
                }
                finally { g.Restore(state); }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ClearImage();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Shared geometry for drawing an image center-cropped to a square,
    /// clipped to a circle, and rotated. Used for both the live preview and
    /// the "_rotated" capture file so the two always agree pixel-for-pixel.
    /// The caller must already have translated the Graphics origin to the
    /// desired center point and set whatever smoothing/interpolation mode
    /// it wants (preview favors speed, capture favors quality).
    /// </summary>
    static class CircularFrame
    {
        public static void Draw(Graphics g, Image source, float size, double rotationDegrees)
        {
            using (GraphicsPath mask = new GraphicsPath())
            {
                mask.AddEllipse(-size / 2f, -size / 2f, size, size);
                g.SetClip(mask);
            }
            g.RotateTransform((float)rotationDegrees);

            float cropSide = Math.Min(source.Width, source.Height);
            RectangleF sourceCrop = new RectangleF(
                (source.Width - cropSide) / 2f, (source.Height - cropSide) / 2f, cropSide, cropSide);
            RectangleF destination = new RectangleF(-size / 2f, -size / 2f, size, size);
            g.DrawImage(source, destination, sourceCrop, GraphicsUnit.Pixel);
        }
    }
}
