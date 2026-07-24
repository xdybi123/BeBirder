using System;
using System.Threading;

namespace EarPicker
{
    partial class MainForm
    {
        void UpdateLiveUi()
        {
            ConsumeDisplayFrame();
            int now = Environment.TickCount;

            VideoSession video;
            SensorSession sensor;
            bool streamActive;
            lock (streamLifecycleLock)
            {
                video = videoSession;
                sensor = sensorSession;
                streamActive = streaming;
            }

            if (video != null && streamActive)
            {
                int lastFrame = Thread.VolatileRead(ref video.LastFrameTick);
                int frameAge = lastFrame == 0 ? Int32.MaxValue : unchecked(now - lastFrame);

                if (unchecked(now - video.LastStatusTick) >= 1000)
                {
                    int elapsed = Math.Max(1, unchecked(now - video.LastStatusTick));
                    video.LastStatusTick = now;
                    int frameCount = Interlocked.Exchange(ref video.FramesSinceStatus, 0);
                    double fps = frameCount * 1000.0 / elapsed;
                    fpsLabel.Text = "FPS: " + (frameAge > 2000 ? 0.0 : fps).ToString("0.0");

                    int width = Thread.VolatileRead(ref video.Width);
                    int height = Thread.VolatileRead(ref video.Height);
                    resolutionLabel.Text = width > 0 && height > 0 ? "Resolution: " + width + " x " + height : "Resolution: --";
                }
            }

            if (sensor != null)
            {
                bool hasSample;
                double rotationValue, pitchValue, rollValue;
                int lastSample;
                string error;
                lock (sensor.Sync)
                {
                    hasSample = sensor.HasSample;
                    rotationValue = sensor.Rotation;
                    pitchValue = sensor.Pitch;
                    rollValue = sensor.Roll;
                    lastSample = sensor.LastSampleTick;
                    error = sensor.Error;
                }

                if (hasSample && unchecked(now - lastSample) <= 2500)
                {
                    currentRotation = rotationValue;
                    pitch = pitchValue;
                    roll = rollValue;
                    picture.Rotation = currentRotation;
                    rotationLabel.Text = "Rotation: " + currentRotation.ToString("0") + "°";
                    tiltLabel.Text = "Tilt: pitch " + pitch.ToString("0.0") + "° / roll " + roll.ToString("0.0") + "°";
                }
                else if (!String.IsNullOrEmpty(error) || streamActive)
                {
                    // No fresh sample (sensor errored, or the stream is up but hasn't reported yet) - show neutral.
                    currentRotation = pitch = roll = 0;
                    picture.Rotation = 0;
                    rotationLabel.Text = "Rotation: 0°";
                    tiltLabel.Text = "Tilt: pitch 0.0° / roll 0.0°";
                }
            }
        }
    }
}
