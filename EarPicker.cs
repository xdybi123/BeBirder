using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace EarPicker
{
    static class Program
    {
        [STAThread] static void Main() { Application.EnableVisualStyles(); Application.Run(new MainForm()); }
    }

    sealed class MainForm : Form
    {
        readonly Panel connectPage = new Panel(), viewerPage = new Panel();
        readonly Label connectState = new Label(), connectBattery = new Label();
        Button startButton = new Button(), pauseButton = new Button();
        readonly RotatingView picture = new RotatingView();
        readonly CheckBox applyRotation = new CheckBox();
        readonly TrackBar brightness = new TrackBar();
        readonly Label brightLabel = new Label(), fpsLabel = new Label(), resolutionLabel = new Label();
        readonly Label batteryLabel = new Label(), rotationLabel = new Label(), tiltLabel = new Label();
        readonly CheckBox publishCheck = new CheckBox();
        readonly TextBox publishPort = new TextBox();
        readonly Label publishStatus = new Label();
        readonly object commandLock = new object(), frameLock = new object();
        readonly AutoResetEvent frameReady = new AutoResetEvent(false);
        readonly Queue<byte[]> frames = new Queue<byte[]>();
        volatile bool streaming, sensorRunning, closing;
        UdpClient videoSocket, sensorSocket;
        Thread receiveThread, decodeThread, sensorThread;
        int shownFrames, droppedFrames, decodedWidth, decodedHeight;
        double currentRotation, pitch, roll;
        DateTime fpsStart;
        readonly MjpegPublisher publisher = new MjpegPublisher();
        readonly System.Windows.Forms.Timer brightnessTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer batteryTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer connectionTimer = new System.Windows.Forms.Timer();
        int connectionCheckBusy;

        public MainForm()
        {
            Text = "Ear Picker"; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250); Font = new Font("Segoe UI", 9F);
            BuildConnectPage(); BuildViewerPage(); ShowConnectPage();
            brightnessTimer.Interval = 120;
            brightnessTimer.Tick += delegate { brightnessTimer.Stop(); SetBrightness(brightness.Value); };
            batteryTimer.Interval = 3000;
            batteryTimer.Tick += delegate { ReadBatteryAsync(false); };
            connectionTimer.Interval = 1000;
            connectionTimer.Tick += delegate { CheckDevice(); };
            FormClosing += delegate { closing = true; brightnessTimer.Stop(); batteryTimer.Stop(); connectionTimer.Stop(); publisher.Stop(); StopAll(); };
            Shown += delegate { CheckDevice(); };
        }

        void BuildConnectPage()
        {
            connectPage.Dock = DockStyle.Fill; Controls.Add(connectPage);
            Label title = L("Ear Picker", 22, true); title.SetBounds(24, 18, 300, 38); connectPage.Controls.Add(title);
            connectState.SetBounds(27, 65, 290, 26); connectState.Text = "Disconnected"; connectState.ForeColor = Color.Firebrick; connectPage.Controls.Add(connectState);
            connectBattery.SetBounds(27, 94, 290, 26); connectBattery.Text = "Battery: --"; connectPage.Controls.Add(connectBattery);
            startButton = B("Start", 110); startButton.SetBounds(207, 128, 110, 34); startButton.Enabled = false;
            startButton.Click += delegate { ShowViewerPage(); }; connectPage.Controls.Add(startButton);
        }

        void BuildViewerPage()
        {
            viewerPage.Dock = DockStyle.Fill; viewerPage.Visible = false; Controls.Add(viewerPage);
            TableLayoutPanel layout = new TableLayoutPanel(); layout.Dock = DockStyle.Fill; layout.Padding = new Padding(10);
            layout.ColumnCount = 2; layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            viewerPage.Controls.Add(layout);
            picture.Dock = DockStyle.Fill; picture.BackColor = Color.Black; layout.Controls.Add(picture, 0, 0);
            FlowLayoutPanel side = new FlowLayoutPanel(); side.Dock = DockStyle.Fill; side.FlowDirection = FlowDirection.TopDown; side.WrapContents = false; side.Padding = new Padding(8); layout.Controls.Add(side, 1, 0);
            Label heading = L("Controls", 18, true); heading.Width = 250; heading.Height = 36; side.Controls.Add(heading);
            pauseButton = B("Pause", 250); pauseButton.Height = 38; pauseButton.Click += delegate { if (streaming) PauseVideo(); else ResumeVideo(); }; side.Controls.Add(pauseButton);
            applyRotation.Text = "Apply sensor rotation"; applyRotation.Width = 250; applyRotation.Height = 32; applyRotation.Checked = true;
            applyRotation.CheckedChanged += delegate { picture.ApplyRotation = applyRotation.Checked; }; side.Controls.Add(applyRotation);
            side.Controls.Add(L("Brightness", 10, true));
            brightness.Minimum = 0; brightness.Maximum = 100; brightness.Value = 70; brightness.TickFrequency = 10; brightness.Width = 250;
            brightness.Scroll += delegate { brightLabel.Text = "Value: " + brightness.Value; brightnessTimer.Stop(); brightnessTimer.Start(); }; side.Controls.Add(brightness);
            brightLabel.Text = "Value: 70"; brightLabel.Width = 100; side.Controls.Add(brightLabel);
            side.Controls.Add(Separator()); side.Controls.Add(L("Live status", 13, true));
            foreach (Label l in new[] { fpsLabel, resolutionLabel, batteryLabel, rotationLabel, tiltLabel }) { l.Width = 250; l.Height = 27; side.Controls.Add(l); }
            side.Controls.Add(Separator()); side.Controls.Add(L("Publish MJPEG", 13, true));
            TableLayoutPanel publishRow = new TableLayoutPanel(); publishRow.Width = 250; publishRow.Height = 34; publishRow.ColumnCount = 3; publishRow.RowCount = 1; publishRow.Margin = new Padding(0, 3, 0, 3);
            publishRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58)); publishRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); publishRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            publishCheck.Text = "On"; publishCheck.Dock = DockStyle.Fill; publishCheck.TextAlign = ContentAlignment.MiddleLeft; publishCheck.Margin = new Padding(8, 0, 0, 0);
            publishPort.Text = "8080"; publishPort.Dock = DockStyle.Fill; publishPort.Margin = new Padding(4, 5, 0, 3);
            Label portLabel = L("Port:", 9, false); portLabel.Dock = DockStyle.Fill; portLabel.TextAlign = ContentAlignment.MiddleRight; portLabel.Margin = new Padding(0);
            publishRow.Controls.Add(publishCheck, 0, 0); publishRow.Controls.Add(portLabel, 1, 0); publishRow.Controls.Add(publishPort, 2, 0); side.Controls.Add(publishRow);
            publishStatus.Text = "Publisher off"; publishStatus.Width = 250; publishStatus.Height = 42; side.Controls.Add(publishStatus);
            publishCheck.CheckedChanged += delegate { TogglePublisher(); };
        }

        void ShowConnectPage()
        {
            batteryTimer.Stop(); connectionTimer.Start(); viewerPage.Visible = false; connectPage.Visible = true; ClientSize = new Size(345, 185); MinimumSize = new Size(361, 224); MaximumSize = new Size(361, 224);
        }

        void ShowViewerPage()
        {
            connectionTimer.Stop(); connectPage.Visible = false; viewerPage.Visible = true; MaximumSize = Size.Empty; MinimumSize = new Size(850, 600); ClientSize = new Size(920, 650);
            picture.ApplyRotation = applyRotation.Checked; ResumeVideo(); ReadBatteryAsync(false); batteryTimer.Start();
        }

        void CheckDevice()
        {
            if (Interlocked.Exchange(ref connectionCheckBusy, 1) != 0) return;
            startButton.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string board = ReadBoard(); BatteryInfo battery = ReadBattery();
                    UI(delegate { connectState.Text = "Connected"; connectState.ForeColor = Color.SeaGreen; connectBattery.Text = BatteryText(battery); startButton.Enabled = true; });
                }
                catch (Exception) { UI(delegate { connectState.Text = "Disconnected"; connectState.ForeColor = Color.Firebrick; connectBattery.Text = "Battery: --"; }); }
                finally { Interlocked.Exchange(ref connectionCheckBusy, 0); }
            });
        }

        string ReadBoard()
        {
            lock (commandLock) using (UdpClient c = CommandClient(700))
            {
                byte[] q = { 0x66, 0x39, 1, 1 }; c.Send(q, q.Length); MemoryStream m = new MemoryStream();
                for (int i = 0; i < 8; i++) try { IPEndPoint ep = null; byte[] p = c.Receive(ref ep); m.Write(p, 0, p.Length); if (p.Length > 0 && p[p.Length - 1] == '}') break; } catch (SocketException) { break; }
                string s = Encoding.UTF8.GetString(m.ToArray()).TrimEnd('\0'); if (s.Length == 0) throw new IOException("board did not reply"); return s;
            }
        }

        BatteryInfo ReadBattery()
        {
            lock (commandLock) using (UdpClient c = CommandClient(900))
            {
                byte[] q = { 0x66, 0x3A }; c.Send(q, q.Length); IPEndPoint ep = null; byte[] p = c.Receive(ref ep); if (p.Length < 4) throw new IOException("short battery reply");
                int packed = (p[0] << 24) | (p[1] << 16) | (p[2] << 8) | p[3]; return new BatteryInfo { Percent = packed & 0xffff, Status = unchecked((short)(packed >> 16)) };
            }
        }

        void ReadBatteryAsync(bool ignored)
        {
            ThreadPool.QueueUserWorkItem(delegate { try { BatteryInfo b = ReadBattery(); UI(delegate { batteryLabel.Text = BatteryText(b); }); } catch { UI(delegate { batteryLabel.Text = "Battery: unavailable"; }); } });
        }

        void ReadBrightnessAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int value;
                    lock (commandLock) using (UdpClient c = CommandClient(900))
                    {
                        byte[] q = { 0x66, 0x3C, 0xFE }; c.Send(q, q.Length);
                        IPEndPoint ep = null; byte[] p = c.Receive(ref ep); if (p.Length == 0) return; value = p[0];
                    }
                    UI(delegate { brightness.Value = Math.Max(0, Math.Min(100, value)); brightLabel.Text = "Value: " + value; });
                }
                catch { }
            });
        }

        static string BatteryText(BatteryInfo b) { return "Battery: " + b.Percent + "%  " + (b.Status == 2 ? "(charging)" : ""); }
        UdpClient CommandClient(int timeout) { UdpClient c = new UdpClient(); c.Client.ReceiveTimeout = timeout; c.Connect("192.168.5.1", 58090); return c; }
        void SetBrightness(int value) { ThreadPool.QueueUserWorkItem(delegate { try { lock (commandLock) using (UdpClient c = CommandClient(500)) { byte[] q = { 0x66, 0x3C, (byte)value }; c.Send(q, q.Length); } } catch { } }); }

        void ResumeVideo()
        {
            if (streaming) return; videoSocket = new UdpClient(); videoSocket.Client.ReceiveTimeout = 500; videoSocket.Client.ReceiveBufferSize = 16 * 1024 * 1024; videoSocket.Connect("192.168.5.1", 58080);
            videoSocket.Send(new byte[] { 0x20, 0x37 }, 2); videoSocket.Send(new byte[] { 0x20, 0x36 }, 2); streaming = true; pauseButton.Text = "Pause";
            shownFrames = droppedFrames = 0; fpsStart = DateTime.UtcNow; lock (frameLock) frames.Clear();
            decodeThread = NewThread(DecodeLoop, "JPEG decoder", ThreadPriority.AboveNormal); receiveThread = NewThread(ReceiveLoop, "UDP video", ThreadPriority.Highest);
            StartSensor();
            ReadBrightnessAsync();
        }

        void PauseVideo()
        {
            streaming = false; frameReady.Set(); UdpClient c = videoSocket; videoSocket = null;
            if (c != null) { try { c.Send(new byte[] { 0x20, 0x37 }, 2); } catch { } try { c.Close(); } catch { } }
            StopSensor();
            pauseButton.Text = "Resume";
        }

        void ReceiveLoop()
        {
            MemoryStream frame = null; int fid = -1, frag = 0;
            while (streaming) try
            {
                IPEndPoint ep = null; byte[] p = videoSocket.Receive(ref ep); if (p.Length < 4) continue;
                int nf = p[0], nfrag = p[2]; bool last = p[1] != 0;
                if (nfrag == 1 && !last) { if (frame != null) { frame.Dispose(); droppedFrames++; } frame = new MemoryStream(262144); frame.Write(p, 4, p.Length - 4); fid = nf; frag = 1; }
                else if (last) { if (frame == null) continue; if (nf != fid || nfrag != frag + 1) { frame.Dispose(); frame = null; droppedFrames++; continue; } frame.Write(p, 4, p.Length - 4); byte[] j = CompleteJpeg(frame.ToArray()); frame.Dispose(); frame = null; if (j == null) droppedFrames++; else QueueFrame(j); }
                else { if (frame == null || nf != fid || nfrag != frag + 1) { if (frame != null) { frame.Dispose(); frame = null; droppedFrames++; } continue; } frame.Write(p, 4, p.Length - 4); frag = nfrag; }
            }
            catch (SocketException x) { if (streaming && x.SocketErrorCode == SocketError.TimedOut) try { videoSocket.Send(new byte[] { 0x20, 0x37 }, 2); videoSocket.Send(new byte[] { 0x20, 0x36 }, 2); } catch { } }
            catch (ObjectDisposedException) { break; }
            if (frame != null) frame.Dispose();
        }

        void QueueFrame(byte[] j) { publisher.SetFrame(j); lock (frameLock) { while (frames.Count >= 2) frames.Dequeue(); frames.Enqueue(j); } frameReady.Set(); }

        void TogglePublisher()
        {
            if (!publishCheck.Checked) { publisher.Stop(); publishPort.Enabled = true; publishStatus.Text = "Publisher off"; return; }
            int port;
            if (!Int32.TryParse(publishPort.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                publishStatus.Text = "Invalid port"; publishCheck.Checked = false; return;
            }
            try
            {
                publisher.Start(port); publishPort.Enabled = false;
                publishStatus.Text = "http://127.0.0.1:" + port + "/\nNetwork: http://<PC-IP>:" + port + "/";
            }
            catch (Exception ex) { publishStatus.Text = "Publish error: " + ex.Message; publishCheck.Checked = false; }
        }
        void DecodeLoop()
        {
            int lastUiTick = Environment.TickCount;
            Queue<int> recentFrames = new Queue<int>();
            while (streaming) { frameReady.WaitOne(250); byte[] j = null; lock (frameLock) while (frames.Count > 0) j = frames.Dequeue(); if (j == null) continue;
                try { using (MemoryStream m = new MemoryStream(j, false)) using (Image im = Image.FromStream(m, true, true)) { decodedWidth = im.Width; decodedHeight = im.Height; picture.SetImage(new Bitmap(im)); shownFrames++; } } catch { droppedFrames++; }
                int now = Environment.TickCount;
                recentFrames.Enqueue(now);
                while (recentFrames.Count > 0 && unchecked(now - recentFrames.Peek()) > 2000) recentFrames.Dequeue();
                double windowSeconds = Math.Min(2.0, (DateTime.UtcNow - fpsStart).TotalSeconds);
                if (windowSeconds > 0 && unchecked(now - lastUiTick) >= 1000) { lastUiTick = now; double fps = recentFrames.Count / windowSeconds; UI(delegate { fpsLabel.Text = "FPS: " + fps.ToString("0.0"); resolutionLabel.Text = "Resolution: " + decodedWidth + " x " + decodedHeight; }); }
            }
        }

        void StartSensor()
        {
            if (sensorRunning) return; sensorSocket = new UdpClient(); sensorSocket.Client.ReceiveTimeout = 1000; sensorSocket.Connect("192.168.5.1", 58098); sensorSocket.Send(new byte[] { 0x86, 6, 1 }, 3); sensorRunning = true;
            sensorThread = NewThread(SensorLoop, "Sensor", ThreadPriority.AboveNormal);
        }

        void SensorLoop()
        {
            while (sensorRunning) try { IPEndPoint ep = null; byte[] p = sensorSocket.Receive(ref ep); if (p.Length < 20) continue; short x = S(p, 0), y = S(p, 1), z = S(p, 2), r = S(p, 9);
                pitch = Math.Atan2(-x, Math.Sqrt((double)y * y + (double)z * z)) * 180 / Math.PI; roll = Math.Atan2(y, z) * 180 / Math.PI; currentRotation = r;
                UI(delegate { picture.Rotation = currentRotation; rotationLabel.Text = "Rotation: " + currentRotation.ToString("0") + "°"; tiltLabel.Text = "Tilt: pitch " + pitch.ToString("0.0") + "° / roll " + roll.ToString("0.0") + "°"; }); }
            catch (SocketException) { } catch (ObjectDisposedException) { break; }
        }

        void StopAll()
        {
            PauseVideo(); StopSensor();
            picture.ClearImage();
        }

        void StopSensor()
        {
            sensorRunning = false; UdpClient s = sensorSocket; sensorSocket = null;
            if (s != null) { try { s.Send(new byte[] { 0x86, 6, 0 }, 3); } catch { } try { s.Close(); } catch { } }
        }

        static short S(byte[] p, int i) { int n = i * 2; return unchecked((short)((p[n] << 8) | p[n + 1])); }
        static Thread NewThread(ThreadStart a, string name, ThreadPriority priority) { Thread t = new Thread(a); t.IsBackground = true; t.Name = name; t.Priority = priority; t.Start(); return t; }
        static byte[] CompleteJpeg(byte[] d) { if (d.Length < 4 || d[0] != 255 || d[1] != 216) return null; for (int i = d.Length - 2; i >= 2; i--) if (d[i] == 255 && d[i + 1] == 217) { byte[] r = new byte[i + 2]; Buffer.BlockCopy(d, 0, r, 0, r.Length); return r; } return null; }
        static string Pretty(string s) { StringBuilder o = new StringBuilder(); int n = 0; bool q = false; foreach (char c in s) { if (c == '"') q = !q; if (!q && (c == '{' || c == '[')) { o.Append(c).AppendLine(); o.Append(new string(' ', ++n * 2)); } else if (!q && (c == '}' || c == ']')) { o.AppendLine().Append(new string(' ', --n * 2)).Append(c); } else if (!q && c == ',') o.Append(c).AppendLine().Append(new string(' ', n * 2)); else o.Append(c); } return o.ToString(); }
        void UI(MethodInvoker a) { if (closing || IsDisposed) return; if (InvokeRequired) try { BeginInvoke(a); } catch { } else a(); }
        static Label L(string text, float size, bool bold) { Label l = new Label(); l.Text = text; l.AutoSize = false; l.Width = 230; l.Height = 28; l.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular); return l; }
        static Button B(string text, int width) { Button b = new Button(); b.Text = text; b.Width = width; b.Height = 32; b.FlatStyle = FlatStyle.System; return b; }
        static Control Separator() { Panel p = new Panel(); p.Width = 250; p.Height = 1; p.BackColor = Color.LightGray; p.Margin = new Padding(0, 12, 0, 12); return p; }
        struct BatteryInfo { public int Percent; public short Status; }
    }

    sealed class MjpegPublisher
    {
        readonly object frameLock = new object();
        volatile bool running;
        TcpListener listener;
        byte[] latestFrame;
        int sequence;

        public void Start(int port)
        {
            Stop();
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            running = true;
            Thread t = new Thread(AcceptLoop); t.IsBackground = true; t.Name = "MJPEG publisher"; t.Start();
        }

        public void Stop()
        {
            running = false;
            TcpListener l = listener; listener = null;
            if (l != null) try { l.Stop(); } catch { }
        }

        public void SetFrame(byte[] jpeg)
        {
            if (!running) return;
            lock (frameLock) { latestFrame = jpeg; sequence++; }
        }

        void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Thread t = new Thread(new ThreadStart(delegate { Serve(client); })); t.IsBackground = true; t.Start();
                }
                catch (SocketException) { if (!running) break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    NetworkStream stream = client.GetStream();
                    stream.ReadTimeout = 2000; stream.WriteTimeout = 3000;
                    byte[] request = new byte[4096];
                    try { stream.Read(request, 0, request.Length); } catch { }
                    byte[] header = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n" +
                        "Connection: close\r\n" +
                        "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
                        "Pragma: no-cache\r\n" +
                        "Access-Control-Allow-Origin: *\r\n" +
                        "Content-Type: multipart/x-mixed-replace; boundary=frame\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    int sentSequence = -1;
                    while (running && client.Connected)
                    {
                        byte[] frame = null; int seq;
                        lock (frameLock) { seq = sequence; if (seq != sentSequence) frame = latestFrame; }
                        if (frame == null) { Thread.Sleep(10); continue; }
                        byte[] part = Encoding.ASCII.GetBytes("--frame\r\nContent-Type: image/jpeg\r\nContent-Length: " + frame.Length + "\r\n\r\n");
                        stream.Write(part, 0, part.Length); stream.Write(frame, 0, frame.Length);
                        byte[] end = { 13, 10 }; stream.Write(end, 0, end.Length); stream.Flush(); sentSequence = seq;
                    }
                }
                catch { }
            }
        }
    }

    sealed class RotatingView : Control
    {
        readonly object sync = new object(); Image image; double rotation; public bool ApplyRotation { get; set; }
        public double Rotation { get { return rotation; } set { rotation = value; Invalidate(); } }
        public RotatingView() { DoubleBuffered = true; ApplyRotation = true; }
        public void SetImage(Image value) { if (InvokeRequired) { try { BeginInvoke(new Action<Image>(SetImage), value); } catch { value.Dispose(); } return; } lock (sync) { Image old = image; image = value; if (old != null) old.Dispose(); } Invalidate(); }
        public void ClearImage() { lock (sync) { if (image != null) image.Dispose(); image = null; } Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); lock (sync) { if (image == null) return; Graphics g = e.Graphics; g.InterpolationMode = InterpolationMode.HighQualityBilinear; float side = Math.Min(Width, Height); g.TranslateTransform(Width / 2f, Height / 2f); if (ApplyRotation) { GraphicsPath mask = new GraphicsPath(); mask.AddEllipse(-side / 2, -side / 2, side, side); g.SetClip(mask); g.RotateTransform((float)rotation); mask.Dispose(); g.DrawImage(image, -side / 2, -side / 2, side, side); } else { float scale = Math.Min((float)Width / image.Width, (float)Height / image.Height); float w = image.Width * scale, h = image.Height * scale; g.DrawImage(image, -w / 2, -h / 2, w, h); } } }
        protected override void Dispose(bool disposing) { if (disposing) ClearImage(); base.Dispose(disposing); }
    }
}
