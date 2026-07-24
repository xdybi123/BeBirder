using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace EarPicker
{
    partial class MainForm
    {
        SensorSession CreateSensorSession()
        {
            UdpClient socket = new UdpClient();
            try
            {
                socket.Client.ReceiveTimeout = 1000;
                socket.Connect(DeviceProtocol.DeviceAddress, DeviceProtocol.SensorPort);
                socket.Send(DeviceProtocol.SensorStreamStart, DeviceProtocol.SensorStreamStart.Length);
                return new SensorSession(socket);
            }
            catch
            {
                try { socket.Close(); } catch { }
                throw;
            }
        }

        void SensorLoop(SensorSession session)
        {
            int consecutiveErrors = 0;
            while (session.Running)
            {
                try
                {
                    IPEndPoint remote = null;
                    byte[] packet = session.Socket.Receive(ref remote);

                    double rotationValue, pitchValue, rollValue;
                    if (!SensorSession.TryParse(packet, out rotationValue, out pitchValue, out rollValue)) continue;

                    lock (session.Sync)
                    {
                        session.Rotation = rotationValue;
                        session.Pitch = pitchValue;
                        session.Roll = rollValue;
                        session.LastSampleTick = Environment.TickCount;
                        session.HasSample = true;
                        session.Error = null;
                    }
                    consecutiveErrors = 0;
                }
                catch (SocketException ex)
                {
                    if (!session.Running) break;
                    if (ex.SocketErrorCode == SocketError.TimedOut) continue;

                    consecutiveErrors++;
                    TraceError("Sensor receive", ex);
                    if (consecutiveErrors >= 5)
                    {
                        lock (session.Sync) session.Error = "Sensor connection lost";
                        session.Running = false;
                        UI(delegate { HandleSensorFailure(session); });
                        break;
                    }
                    Thread.Sleep(Math.Min(1000, consecutiveErrors * 150));
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    TraceError("Sensor receive", ex);
                    lock (session.Sync) session.Error = "Sensor failed";
                    session.Running = false;
                    UI(delegate { HandleSensorFailure(session); });
                    break;
                }
            }
        }

        void HandleSensorFailure(SensorSession session)
        {
            lock (streamLifecycleLock)
            {
                if (!ReferenceEquals(sensorSession, session)) return;
                session.Running = false;
                sensorSession = null;
            }
            StopSensorSession(session);
            currentRotation = pitch = roll = 0;
            picture.Rotation = 0;
            rotationLabel.Text = "Rotation: 0°";
            tiltLabel.Text = "Tilt: pitch 0.0° / roll 0.0°";
        }

        void StopSensorSession(SensorSession session)
        {
            if (session == null) return;
            session.Running = false;
            try { session.Socket.Send(DeviceProtocol.SensorStreamStop, DeviceProtocol.SensorStreamStop.Length); } catch { }
            try { session.Socket.Close(); } catch { }
            ThreadPool.QueueUserWorkItem(delegate { JoinThread(session.Thread, Timeout.Infinite); });
        }
    }
}
