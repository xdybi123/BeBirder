using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EarPicker
{
    /// <summary>Battery level as reported by the device.</summary>
    struct BatteryStatus
    {
        public int Percent;
        public bool Charging;

        public override string ToString()
        {
            return "Battery: " + Percent + "%  " + (Charging ? "(charging)" : "");
        }
    }

    /// <summary>
    /// Request/response client for the device's UDP command channel (board
    /// info, battery, brightness). Every call opens a fresh short-lived
    /// socket and all calls are serialized against each other, matching the
    /// device's single-outstanding-command protocol.
    /// </summary>
    sealed class DeviceClient
    {
        readonly object sync = new object();

        /// <summary>Probes the device and returns its raw board-info reply. Throws if it doesn't answer.</summary>
        public string ReadBoardInfo()
        {
            lock (sync)
            using (UdpClient client = OpenSocket(700))
            {
                client.Send(DeviceProtocol.ReadBoardInfo, DeviceProtocol.ReadBoardInfo.Length);
                using (MemoryStream received = new MemoryStream())
                {
                    // The reply can arrive in a few packets; keep reading until it
                    // looks complete (ends in '}'), we time out, or hit the retry cap.
                    for (int i = 0; i < 8; i++)
                    {
                        byte[] packet;
                        try
                        {
                            IPEndPoint remote = null;
                            packet = client.Receive(ref remote);
                        }
                        catch (SocketException) { break; }

                        received.Write(packet, 0, packet.Length);
                        if (packet.Length > 0 && packet[packet.Length - 1] == (byte)'}') break;
                    }

                    string text = Encoding.UTF8.GetString(received.ToArray()).TrimEnd('\0');
                    if (text.Length == 0) throw new IOException("board did not reply");
                    return text;
                }
            }
        }

        public BatteryStatus ReadBattery()
        {
            lock (sync)
            using (UdpClient client = OpenSocket(900))
            {
                client.Send(DeviceProtocol.ReadBattery, DeviceProtocol.ReadBattery.Length);
                IPEndPoint remote = null;
                byte[] reply = client.Receive(ref remote);
                if (reply.Length < 4) throw new IOException("short battery reply");

                int packed = (reply[0] << 24) | (reply[1] << 16) | (reply[2] << 8) | reply[3];
                BatteryStatus status = new BatteryStatus();
                status.Percent = packed & 0xffff;
                status.Charging = unchecked((short)(packed >> 16)) == 2;
                if (status.Percent < 0 || status.Percent > 100) throw new InvalidDataException("invalid battery percentage");
                return status;
            }
        }

        /// <summary>
        /// Reads the device's current brightness (0-100). Returns false, with no
        /// exception, if the device answered with an empty packet - that's the
        /// firmware's "not ready" response, not an error.
        /// </summary>
        public bool TryReadBrightness(out int value)
        {
            lock (sync)
            using (UdpClient client = OpenSocket(900))
            {
                client.Send(DeviceProtocol.ReadBrightness, DeviceProtocol.ReadBrightness.Length);
                IPEndPoint remote = null;
                byte[] reply = client.Receive(ref remote);
                if (reply.Length == 0) { value = 0; return false; }
                value = Math.Max(0, Math.Min(100, (int)reply[0]));
                return true;
            }
        }

        public void SetBrightness(byte value)
        {
            lock (sync)
            using (UdpClient client = OpenSocket(500))
            {
                byte[] command = DeviceProtocol.SetBrightness(value);
                client.Send(command, command.Length);
            }
        }

        static UdpClient OpenSocket(int receiveTimeoutMs)
        {
            UdpClient client = new UdpClient();
            client.Client.ReceiveTimeout = receiveTimeoutMs;
            client.Connect(DeviceProtocol.DeviceAddress, DeviceProtocol.CommandPort);
            return client;
        }
    }
}
