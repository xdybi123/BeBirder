using System;
using System.Net.Sockets;
using System.Threading;

namespace EarPicker
{
    /// <summary>
    /// State for one live orientation-sensor UDP session: the socket, the
    /// background receive thread, and the most recent sample.
    /// </summary>
    sealed class SensorSession
    {
        public readonly UdpClient Socket;
        public readonly object Sync = new object();

        public volatile bool Running = true;
        public Thread Thread;
        public bool HasSample;
        public double Rotation, Pitch, Roll;
        public int LastSampleTick;
        public string Error;

        public SensorSession(UdpClient socket)
        {
            Socket = socket;
        }

        /// <summary>
        /// Decodes one raw orientation packet from the device's IMU stream: six
        /// big-endian 16-bit words, where words 0-2 are the raw accelerometer
        /// axes and word 9 is the device's own computed heading. Returns false
        /// (leaving the out parameters at zero) if the packet is too short.
        /// </summary>
        public static bool TryParse(byte[] packet, out double rotation, out double pitch, out double roll)
        {
            if (packet.Length < 20)
            {
                rotation = pitch = roll = 0;
                return false;
            }

            short x = ReadWord(packet, 0);
            short y = ReadWord(packet, 1);
            short z = ReadWord(packet, 2);
            rotation = ReadWord(packet, 9);
            pitch = Math.Atan2(-x, Math.Sqrt((double)y * y + (double)z * z)) * 180.0 / Math.PI;
            roll = Math.Atan2(y, z) * 180.0 / Math.PI;
            return true;
        }

        static short ReadWord(byte[] packet, int wordIndex)
        {
            int byteIndex = wordIndex * 2;
            return unchecked((short)((packet[byteIndex] << 8) | packet[byteIndex + 1]));
        }
    }
}
