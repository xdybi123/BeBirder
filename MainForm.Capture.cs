using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace EarPicker
{
    partial class MainForm
    {
        void CaptureImages()
        {
            byte[] jpeg;
            double angle = 0;
            lock (streamLifecycleLock)
            {
                VideoSession session = videoSession;
                jpeg = CanCaptureSession(session, Environment.TickCount) ? session.LatestJpeg : null;
                if (jpeg != null && applyRotation.Checked)
                {
                    SensorSession sensor = sensorSession;
                    if (sensor != null)
                    {
                        lock (sensor.Sync)
                        {
                            if (sensor.Running && sensor.HasSample && unchecked(Environment.TickCount - sensor.LastSampleTick) <= 2500)
                                angle = sensor.Rotation;
                        }
                    }
                }
            }

            if (jpeg == null)
            {
                MessageBox.Show(this, "No video frame is available yet.", "Capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string folder = capturePath.Text.Trim();
            if (folder.Length == 0) folder = AppDomain.CurrentDomain.BaseDirectory;

            captureSaving = true;
            captureResetTimer.Stop();
            captureButton.Enabled = false;
            captureButton.Text = "Saving...";

            ThreadPool.QueueUserWorkItem(delegate { SaveCapture(jpeg, angle, folder); });
        }

        void SaveCapture(byte[] jpeg, double angle, string folder)
        {
            try
            {
                folder = Path.GetFullPath(folder);
                Directory.CreateDirectory(folder);

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string originalFile = Path.Combine(folder, stamp + "_original.jpg");
                string rotatedFile = Path.Combine(folder, stamp + "_rotated.jpg");
                string temporarySuffix = ".tmp-" + Guid.NewGuid().ToString("N");
                string originalTemporary = originalFile + temporarySuffix;
                string rotatedTemporary = rotatedFile + temporarySuffix;

                try
                {
                    File.WriteAllBytes(originalTemporary, jpeg);
                    using (MemoryStream input = new MemoryStream(jpeg, false))
                    using (Image source = Image.FromStream(input, true, true))
                    using (Bitmap output = new Bitmap(source.Width, source.Height))
                    using (Graphics g = Graphics.FromImage(output))
                    {
                        g.Clear(Color.Black);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.TranslateTransform(source.Width / 2f, source.Height / 2f);
                        CircularFrame.Draw(g, source, Math.Min(source.Width, source.Height), angle);
                        output.Save(rotatedTemporary, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                    File.Move(originalTemporary, originalFile);
                    File.Move(rotatedTemporary, rotatedFile);
                }
                finally
                {
                    DeleteTemporaryFile(originalTemporary);
                    DeleteTemporaryFile(rotatedTemporary);
                }

                UI(delegate
                {
                    captureSaving = false;
                    capturePath.Text = folder;
                    if (closeAfterCapture) { closeAfterCapture = false; Close(); return; }
                    captureButton.Enabled = true;
                    captureButton.Text = "Captured";
                    captureResetTimer.Stop();
                    captureResetTimer.Start();
                });
            }
            catch (Exception ex)
            {
                TraceError("Capture", ex);
                UI(delegate
                {
                    captureSaving = false;
                    if (closeAfterCapture) { closeAfterCapture = false; Close(); return; }
                    captureButton.Enabled = true;
                    captureButton.Text = "Capture";
                    MessageBox.Show(this, "Capture failed:\n" + ex.Message, "Capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
        }

        bool CanCaptureSession(VideoSession session, int now)
        {
            if (session == null || !streaming || !session.Running || !ReferenceEquals(videoSession, session) || session.LatestJpeg == null) return false;
            int lastFrame = Thread.VolatileRead(ref session.LastFrameTick);
            return lastFrame != 0 && unchecked(now - lastFrame) <= 2000;
        }

        static void DeleteTemporaryFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
