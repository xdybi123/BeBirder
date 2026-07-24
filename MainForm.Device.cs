using System;
using System.Drawing;
using System.Threading;

namespace EarPicker
{
    partial class MainForm
    {
        void CheckDevice()
        {
            if (Interlocked.Exchange(ref connectionCheckBusy, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    device.ReadBoardInfo();
                    UI(delegate
                    {
                        connectState.Text = "Connected";
                        connectState.ForeColor = Color.SeaGreen;
                        startButton.Enabled = true;
                    });
                    try
                    {
                        BatteryStatus battery = device.ReadBattery();
                        UI(delegate { connectBattery.Text = battery.ToString(); });
                    }
                    catch (Exception ex)
                    {
                        TraceError("Battery probe", ex);
                        UI(delegate { connectBattery.Text = "Battery: unavailable"; });
                    }
                }
                catch (Exception ex)
                {
                    TraceError("Device probe", ex);
                    UI(delegate
                    {
                        connectState.Text = "Disconnected";
                        connectState.ForeColor = Color.Firebrick;
                        connectBattery.Text = "Battery: --";
                        startButton.Enabled = false;
                    });
                }
                finally { Interlocked.Exchange(ref connectionCheckBusy, 0); }
            });
        }

        void ReadBatteryAsync()
        {
            if (Interlocked.Exchange(ref batteryReadBusy, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    BatteryStatus battery = device.ReadBattery();
                    UI(delegate { batteryLabel.Text = battery.ToString(); });
                }
                catch (Exception ex)
                {
                    TraceError("Battery read", ex);
                    UI(delegate { batteryLabel.Text = "Battery: unavailable"; });
                }
                finally { Interlocked.Exchange(ref batteryReadBusy, 0); }
            });
        }

        void ReadBrightnessAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int value;
                    if (!device.TryReadBrightness(out value)) return;
                    UI(delegate { brightness.Value = value; brightLabel.Text = "Value: " + value; });
                }
                catch (Exception ex)
                {
                    TraceError("Brightness read", ex);
                    UI(delegate { brightLabel.Text = "Value: unavailable"; });
                }
            });
        }

        void SetBrightness(int value)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { device.SetBrightness((byte)value); }
                catch (Exception ex)
                {
                    TraceError("Brightness write", ex);
                    UI(delegate { brightLabel.Text = "Value: " + value + " (not sent)"; });
                }
            });
        }
    }
}
