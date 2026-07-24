using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace EarPicker
{
    /// <summary>
    /// The app's only window. Has two pages: a small "connect" page that
    /// polls the device until it answers, and a "viewer" page with the live
    /// video, controls, and status readouts. The rest of this class lives in
    /// the MainForm.*.cs files, grouped by what they're responsible for.
    /// </summary>
    sealed partial class MainForm : Form
    {
        // --- Connect page ---
        readonly Panel connectPage = new Panel();
        readonly Label connectState = new Label();
        readonly Label connectBattery = new Label();
        Button startButton = new Button();

        // --- Viewer page ---
        readonly Panel viewerPage = new Panel();
        readonly RotatingView picture = new RotatingView();
        Button pauseButton = new Button();
        readonly CheckBox applyRotation = new CheckBox();
        readonly TrackBar brightness = new TrackBar();
        readonly Label brightLabel = new Label();
        readonly Label fpsLabel = new Label();
        readonly Label resolutionLabel = new Label();
        readonly Label batteryLabel = new Label();
        readonly Label rotationLabel = new Label();
        readonly Label tiltLabel = new Label();
        readonly CheckBox publishCheck = new CheckBox();
        readonly TextBox publishPort = new TextBox();
        readonly Label publishStatus = new Label();
        readonly TextBox capturePath = new TextBox();
        readonly Button captureButton = new Button();

        // --- Device I/O ---
        readonly DeviceClient device = new DeviceClient();
        readonly MjpegPublisher publisher = new MjpegPublisher();
        readonly object streamLifecycleLock = new object();
        readonly object displayFrameLock = new object();
        volatile bool streaming, closing;
        VideoSession videoSession;
        SensorSession sensorSession;
        Bitmap pendingDisplayFrame;
        VideoSession pendingDisplaySession;
        double currentRotation, pitch, roll;
        bool startingStream, changingPublisherCheck, captureSaving, closeAfterCapture;
        int connectionCheckBusy, batteryReadBusy;

        // --- Timers ---
        readonly System.Windows.Forms.Timer brightnessTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer batteryTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer connectionTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer liveUiTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer captureResetTimer = new System.Windows.Forms.Timer();

        public MainForm()
        {
            Text = "Ear Picker";
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("Segoe UI", 9F);

            BuildConnectPage();
            BuildViewerPage();
            ShowConnectPage();

            brightnessTimer.Interval = 120; // debounces the slider before sending a command
            brightnessTimer.Tick += delegate { brightnessTimer.Stop(); SetBrightness(brightness.Value); };
            batteryTimer.Interval = 3000;
            batteryTimer.Tick += delegate { ReadBatteryAsync(); };
            connectionTimer.Interval = 1000;
            connectionTimer.Tick += delegate { CheckDevice(); };
            liveUiTimer.Interval = 33; // ~30 Hz UI refresh
            liveUiTimer.Tick += delegate { UpdateLiveUi(); };
            captureResetTimer.Interval = 1200;
            captureResetTimer.Tick += delegate { captureResetTimer.Stop(); if (!captureSaving) captureButton.Text = "Capture"; };

            FormClosing += HandleFormClosing;
            FormClosed += delegate
            {
                brightnessTimer.Dispose();
                batteryTimer.Dispose();
                connectionTimer.Dispose();
                liveUiTimer.Dispose();
                captureResetTimer.Dispose();
            };
            Shown += delegate { CheckDevice(); };
        }

        void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            if (captureSaving && e.CloseReason == CloseReason.UserClosing)
            {
                // Let a capture finish writing before the process goes away.
                closeAfterCapture = true;
                e.Cancel = true;
                return;
            }

            closing = true;
            DisplaySleep.Allow();
            brightnessTimer.Stop();
            batteryTimer.Stop();
            connectionTimer.Stop();
            liveUiTimer.Stop();
            captureResetTimer.Stop();
            publisher.Stop();
            StopAll();
        }

        void ShowConnectPage()
        {
            batteryTimer.Stop();
            liveUiTimer.Stop();
            connectionTimer.Start();
            viewerPage.Visible = false;
            connectPage.Visible = true;
            ClientSize = new Size(345, 185);
            MinimumSize = new Size(361, 224);
            MaximumSize = new Size(361, 224);
        }

        void ShowViewerPage()
        {
            connectionTimer.Stop();
            connectPage.Visible = false;
            viewerPage.Visible = true;
            MaximumSize = Size.Empty;
            MinimumSize = new Size(915, 670);
            DisplaySleep.Keep();
            picture.ApplyRotation = applyRotation.Checked;
            ResumeVideo();
            ReadBatteryAsync();
            batteryTimer.Start();
            liveUiTimer.Start();
        }

        void StopAll()
        {
            PauseVideo();
            ClearPendingDisplayFrame();
            picture.ClearImage();
        }

        /// <summary>Marshals an action onto the UI thread, silently dropping it once the form is closing or gone.</summary>
        void UI(MethodInvoker action)
        {
            if (closing || IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(action); }
                catch (Exception ex) { TraceError("UI dispatch", ex); }
            }
            else
            {
                action();
            }
        }

        static void TraceError(string context, Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(DateTime.Now.ToString("s") + " " + context + ": " + ex);
        }

        static Thread NewThread(ThreadStart action, string name, ThreadPriority priority)
        {
            Thread thread = new Thread(action);
            thread.IsBackground = true;
            thread.Name = name;
            thread.Priority = priority;
            thread.Start();
            return thread;
        }

        static bool JoinThread(Thread thread, int timeoutMs)
        {
            if (thread == null || thread == Thread.CurrentThread) return true;
            try { return !thread.IsAlive || thread.Join(timeoutMs); }
            catch { return false; }
        }
    }
}
