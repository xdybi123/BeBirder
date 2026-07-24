using System;
using System.Drawing;
using System.Windows.Forms;

namespace EarPicker
{
    partial class MainForm
    {
        void BuildConnectPage()
        {
            connectPage.Dock = DockStyle.Fill;
            Controls.Add(connectPage);

            Label title = L("Ear Picker", 22, true);
            title.SetBounds(24, 18, 300, 38);
            connectPage.Controls.Add(title);

            connectState.SetBounds(27, 65, 290, 26);
            connectState.Text = "Disconnected";
            connectState.ForeColor = Color.Firebrick;
            connectPage.Controls.Add(connectState);

            connectBattery.SetBounds(27, 94, 290, 26);
            connectBattery.Text = "Battery: --";
            connectPage.Controls.Add(connectBattery);

            startButton = B("Start", 110);
            startButton.SetBounds(207, 128, 110, 34);
            startButton.Enabled = false;
            startButton.Click += delegate { ShowViewerPage(); };
            connectPage.Controls.Add(startButton);
        }

        void BuildViewerPage()
        {
            viewerPage.Dock = DockStyle.Fill;
            viewerPage.Visible = false;
            Controls.Add(viewerPage);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(10);
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            viewerPage.Controls.Add(layout);

            picture.Dock = DockStyle.Fill;
            picture.BackColor = Color.Black;
            layout.Controls.Add(picture, 0, 0);

            Panel sideHost = new Panel();
            sideHost.Dock = DockStyle.Fill;
            layout.Controls.Add(sideHost, 1, 0);

            FlowLayoutPanel side = new FlowLayoutPanel();
            side.Dock = DockStyle.Fill;
            side.FlowDirection = FlowDirection.TopDown;
            side.WrapContents = false;
            side.AutoScroll = true;
            side.Padding = new Padding(8);
            side.BackColor = BackColor;
            sideHost.Controls.Add(side);

            Label heading = L("Controls", 18, true);
            AddSide(side, heading, 32);

            pauseButton = B("Pause", 250);
            pauseButton.Click += delegate { if (streaming) PauseVideo(); else ResumeVideo(); };
            AddSide(side, pauseButton, 34);

            applyRotation.Text = "Apply sensor rotation";
            applyRotation.Checked = true;
            applyRotation.CheckedChanged += delegate { picture.ApplyRotation = applyRotation.Checked; };
            AddSide(side, applyRotation, 28);

            AddSide(side, L("Brightness", 10, true), 22);
            brightness.Minimum = 0;
            brightness.Maximum = 100;
            brightness.Value = 70;
            brightness.TickFrequency = 10;
            brightness.AutoSize = false;
            brightness.Scroll += delegate
            {
                brightLabel.Text = "Value: " + brightness.Value;
                brightnessTimer.Stop();
                brightnessTimer.Start();
            };
            AddSide(side, brightness, 32);
            brightLabel.Text = "Value: 70";
            AddSide(side, brightLabel, 22);

            side.Controls.Add(Separator());
            AddSide(side, L("Live status", 13, true), 24);
            foreach (Label label in new[] { fpsLabel, resolutionLabel, batteryLabel, rotationLabel, tiltLabel })
                AddSide(side, label, 22);

            side.Controls.Add(Separator());
            AddSide(side, L("Publish MJPEG", 13, true), 24);

            TableLayoutPanel publishRow = new TableLayoutPanel();
            publishRow.ColumnCount = 3;
            publishRow.RowCount = 1;
            publishRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            publishRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            publishRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            publishCheck.Text = "On";
            publishCheck.Dock = DockStyle.Fill;
            publishCheck.TextAlign = ContentAlignment.MiddleLeft;
            publishCheck.Margin = new Padding(8, 0, 0, 0);
            publishPort.Text = "8080";
            publishPort.Dock = DockStyle.Fill;
            publishPort.Margin = new Padding(4, 5, 0, 3);
            Label portLabel = L("Port:", 9, false);
            portLabel.Dock = DockStyle.Fill;
            portLabel.TextAlign = ContentAlignment.MiddleRight;
            portLabel.Margin = new Padding(0);
            publishRow.Controls.Add(publishCheck, 0, 0);
            publishRow.Controls.Add(portLabel, 1, 0);
            publishRow.Controls.Add(publishPort, 2, 0);
            AddSide(side, publishRow, 32);

            publishStatus.Text = "Publisher off";
            AddSide(side, publishStatus, 36);
            publishCheck.CheckedChanged += delegate { if (!changingPublisherCheck) TogglePublisher(); };

            side.Controls.Add(Separator());
            AddSide(side, L("Capture", 13, true), 24);
            capturePath.Text = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            AddSide(side, capturePath, 25);
            captureButton.Text = "Capture";
            captureButton.FlatStyle = FlatStyle.System;
            captureButton.Click += delegate { CaptureImages(); };
            AddSide(side, captureButton, 34);
        }

        static Label L(string text, float size, bool bold)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Width = 230;
            label.Height = 28;
            label.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            return label;
        }

        static Button B(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 32;
            button.FlatStyle = FlatStyle.System;
            return button;
        }

        static void AddSide(FlowLayoutPanel side, Control control, int height)
        {
            control.Width = 250;
            control.Height = height;
            control.Margin = new Padding(0, 2, 0, 2);
            side.Controls.Add(control);
        }

        static Control Separator()
        {
            Panel panel = new Panel();
            panel.Width = 250;
            panel.Height = 1;
            panel.BackColor = Color.LightGray;
            panel.Margin = new Padding(0, 6, 0, 6);
            return panel;
        }
    }
}
