using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace EarPicker
{
    partial class MainForm
    {
        void ResumeVideo()
        {
            if (streaming || startingStream) return;
            startingStream = true;
            VideoSession video = null;
            SensorSession sensor = null;
            try
            {
                video = CreateVideoSession();
                sensor = CreateSensorSession();

                ClearPendingDisplayFrame();
                currentRotation = pitch = roll = 0;
                picture.Rotation = 0;

                lock (streamLifecycleLock)
                {
                    videoSession = video;
                    sensorSession = sensor;
                    streaming = true;
                }
                video.DecodeThread = NewThread(delegate { DecodeLoop(video); }, "JPEG decoder", ThreadPriority.Normal);
                video.ReceiveThread = NewThread(delegate { ReceiveLoop(video); }, "UDP video", ThreadPriority.Normal);
                sensor.Thread = NewThread(delegate { SensorLoop(sensor); }, "Sensor", ThreadPriority.Normal);
                pauseButton.Text = "Pause";
                ReadBrightnessAsync();
            }
            catch (Exception ex)
            {
                lock (streamLifecycleLock)
                {
                    if (ReferenceEquals(videoSession, video)) videoSession = null;
                    if (ReferenceEquals(sensorSession, sensor)) sensorSession = null;
                    if (video != null) video.Running = false;
                    if (sensor != null) sensor.Running = false;
                    streaming = false;
                }
                StopVideoSession(video);
                StopSensorSession(sensor);
                TraceError("Video startup", ex);
                fpsLabel.Text = "FPS: 0.0";
                pauseButton.Text = "Resume";
                MessageBox.Show(this, "Could not start the camera stream:\n" + ex.Message, "Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                startingStream = false;
            }
        }

        void PauseVideo()
        {
            VideoSession video;
            SensorSession sensor;
            lock (streamLifecycleLock)
            {
                video = videoSession;
                sensor = sensorSession;
                if (video != null) video.Running = false;
                if (sensor != null) sensor.Running = false;
                videoSession = null;
                sensorSession = null;
                streaming = false;
            }
            StopVideoSession(video);
            StopSensorSession(sensor);
            publisher.ClearFrame();
            ClearPendingDisplayFrame();
            pauseButton.Text = "Resume";
        }

        VideoSession CreateVideoSession()
        {
            UdpClient socket = new UdpClient();
            try
            {
                socket.Client.ReceiveTimeout = 500;
                socket.Client.ReceiveBufferSize = 16 * 1024 * 1024;
                socket.Connect(DeviceProtocol.DeviceAddress, DeviceProtocol.VideoPort);
                socket.Send(DeviceProtocol.VideoStreamStop, DeviceProtocol.VideoStreamStop.Length);
                socket.Send(DeviceProtocol.VideoStreamStart, DeviceProtocol.VideoStreamStart.Length);
                return new VideoSession(socket);
            }
            catch
            {
                try { socket.Close(); } catch { }
                throw;
            }
        }

        void ReceiveLoop(VideoSession session)
        {
            int consecutiveErrors = 0, consecutiveTimeouts = 0;
            try
            {
                while (session.Running)
                {
                    try
                    {
                        IPEndPoint remote = null;
                        byte[] packet = session.Socket.Receive(ref remote);
                        if (packet.Length < 4) continue;
                        consecutiveErrors = 0;
                        consecutiveTimeouts = 0;

                        byte[] completeJpeg = session.Reassembler.AddPacket(packet);
                        if (completeJpeg != null) QueueFrame(session, completeJpeg);
                    }
                    catch (SocketException ex)
                    {
                        if (!session.Running) break;
                        if (ex.SocketErrorCode == SocketError.TimedOut)
                        {
                            consecutiveTimeouts++;
                            // Nudge the device to restart the stream at every power-of-two timeout.
                            if ((consecutiveTimeouts & (consecutiveTimeouts - 1)) == 0)
                            {
                                try
                                {
                                    session.Socket.Send(DeviceProtocol.VideoStreamStop, DeviceProtocol.VideoStreamStop.Length);
                                    session.Socket.Send(DeviceProtocol.VideoStreamStart, DeviceProtocol.VideoStreamStart.Length);
                                }
                                catch (Exception restartError) { TraceError("Video restart request", restartError); }
                            }
                            if (consecutiveTimeouts >= 20)
                            {
                                session.Error = "No video received; check the device connection";
                                session.Running = false;
                                session.FrameReady.Set();
                                UI(delegate { HandleVideoFailure(session); });
                                break;
                            }
                            continue;
                        }

                        consecutiveErrors++;
                        TraceError("Video receive", ex);
                        if (consecutiveErrors >= 5)
                        {
                            session.Error = "Video connection lost: " + ex.SocketErrorCode;
                            session.Running = false;
                            session.FrameReady.Set();
                            UI(delegate { HandleVideoFailure(session); });
                            break;
                        }
                        Thread.Sleep(Math.Min(1000, consecutiveErrors * 150));
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        TraceError("Video receive", ex);
                        session.Error = "Video stream failed";
                        session.Running = false;
                        session.FrameReady.Set();
                        UI(delegate { HandleVideoFailure(session); });
                        break;
                    }
                }
            }
            finally { session.Reassembler.Reset(); }
        }

        void QueueFrame(VideoSession session, byte[] jpeg)
        {
            if (!session.Running || !ReferenceEquals(videoSession, session)) return;
            // The decoder only needs the newest camera frame. A single replaceable
            // slot avoids queue churn and naturally sheds work when decoding lags.
            lock (session.FrameLock) session.PendingJpeg = jpeg;
            session.FrameReady.Set();
        }

        void DecodeLoop(VideoSession session)
        {
            while (session.Running)
            {
                session.FrameReady.WaitOne(250);
                if (!session.Running) break;

                byte[] jpeg;
                lock (session.FrameLock)
                {
                    jpeg = session.PendingJpeg;
                    session.PendingJpeg = null;
                }
                if (jpeg == null) continue;

                Bitmap bitmap = null;
                try
                {
                    // No embedded-color-management / validateImageData here: this runs on every
                    // frame, the reassembler already confirmed SOI/EOI, and a bad frame here just
                    // gets caught below and skipped. (SaveCapture uses the stricter, slower form -
                    // it only runs once per capture, and a corrupt saved file is worse than a slow save.)
                    using (MemoryStream input = new MemoryStream(jpeg, false))
                    using (Image source = Image.FromStream(input, false, false))
                    {
                        bitmap = new Bitmap(source);
                        Interlocked.Exchange(ref session.Width, source.Width);
                        Interlocked.Exchange(ref session.Height, source.Height);
                    }

                    bool accepted = false;
                    lock (streamLifecycleLock)
                    {
                        if (session.Running && ReferenceEquals(videoSession, session))
                        {
                            session.LatestJpeg = jpeg;
                            publisher.SetFrame(jpeg);
                            Thread.VolatileWrite(ref session.LastFrameTick, Environment.TickCount);
                            Interlocked.Increment(ref session.FramesSinceStatus);
                            accepted = true;
                        }
                    }
                    if (!accepted) { bitmap.Dispose(); bitmap = null; continue; }
                    PublishDisplayFrame(session, bitmap);
                    bitmap = null;
                }
                catch (Exception ex) { TraceError("JPEG decode", ex); }
                finally { if (bitmap != null) bitmap.Dispose(); }
            }
        }

        void PublishDisplayFrame(VideoSession session, Bitmap bitmap)
        {
            Bitmap old = null;
            bool accept;
            lock (displayFrameLock)
            {
                accept = session.Running && ReferenceEquals(videoSession, session);
                if (accept)
                {
                    old = pendingDisplayFrame;
                    pendingDisplayFrame = bitmap;
                    pendingDisplaySession = session;
                }
            }
            if (old != null) old.Dispose();
            if (!accept) bitmap.Dispose();
        }

        void ConsumeDisplayFrame()
        {
            Bitmap bitmap;
            VideoSession owner;
            lock (displayFrameLock)
            {
                bitmap = pendingDisplayFrame;
                owner = pendingDisplaySession;
                pendingDisplayFrame = null;
                pendingDisplaySession = null;
            }
            if (bitmap == null) return;
            if (owner != null && owner.Running && ReferenceEquals(videoSession, owner)) picture.SetImage(bitmap);
            else bitmap.Dispose();
        }

        void ClearPendingDisplayFrame()
        {
            Bitmap bitmap;
            lock (displayFrameLock)
            {
                bitmap = pendingDisplayFrame;
                pendingDisplayFrame = null;
                pendingDisplaySession = null;
            }
            if (bitmap != null) bitmap.Dispose();
        }

        void HandleVideoFailure(VideoSession session)
        {
            SensorSession sensor;
            lock (streamLifecycleLock)
            {
                if (!ReferenceEquals(videoSession, session)) return;
                sensor = sensorSession;
                session.Running = false;
                if (sensor != null) sensor.Running = false;
                videoSession = null;
                sensorSession = null;
                streaming = false;
            }
            StopVideoSession(session);
            StopSensorSession(sensor);
            ClearPendingDisplayFrame();
            pauseButton.Text = "Resume";
            fpsLabel.Text = "FPS: 0.0";
            publisher.ClearFrame();
        }

        void StopVideoSession(VideoSession session)
        {
            if (session == null) return;
            session.Running = false;
            session.FrameReady.Set();
            try { session.Socket.Send(DeviceProtocol.VideoStreamStop, DeviceProtocol.VideoStreamStop.Length); } catch { }
            try { session.Socket.Close(); } catch { }
            ThreadPool.QueueUserWorkItem(delegate
            {
                JoinThread(session.ReceiveThread, Timeout.Infinite);
                JoinThread(session.DecodeThread, Timeout.Infinite);
                try { session.FrameReady.Close(); } catch { }
            });
        }
    }
}
