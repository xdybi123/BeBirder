using System;
using System.IO;

namespace EarPicker
{
    /// <summary>
    /// Reassembles the device's fragmented UDP video packets into complete
    /// JPEG frames. One instance is owned per <see cref="VideoSession"/> and
    /// fed packets in receive order; it is not thread-safe on its own.
    /// Packet layout: [frameId][isLastFragment][fragmentNumber][...payload].
    /// </summary>
    sealed class JpegFragmentReassembler
    {
        MemoryStream buffer;
        int frameId = -1;
        int fragmentNumber;
        int fragmentCount;

        /// <summary>
        /// Feeds one packet in (must be at least 4 bytes - callers filter shorter
        /// ones out before this point). Returns a complete, trimmed JPEG when a
        /// frame has just finished assembling; otherwise null.
        /// </summary>
        public byte[] AddPacket(byte[] packet)
        {
            int packetFrameId = packet[0];
            bool isLast = packet[1] != 0;
            int packetFragmentNumber = packet[2];
            int payloadLength = packet.Length - 4;

            int expectedFragment = (fragmentNumber + 1) & 0xff;
            bool continuation = buffer != null && packetFrameId == frameId && packetFragmentNumber == expectedFragment;

            if (packetFragmentNumber == 1 && !continuation)
            {
                Reset();
                if (payloadLength > DeviceProtocol.MaxJpegBytes) return null;

                if (isLast)
                {
                    byte[] single = new byte[payloadLength];
                    Buffer.BlockCopy(packet, 4, single, 0, payloadLength);
                    return JpegUtil.TrimToEndMarker(single);
                }

                buffer = new MemoryStream(Math.Min(262144, DeviceProtocol.MaxJpegBytes));
                buffer.Write(packet, 4, payloadLength);
                frameId = packetFrameId;
                fragmentNumber = 1;
                fragmentCount = 1;
                return null;
            }

            if (!continuation || fragmentCount >= DeviceProtocol.MaxFrameFragments ||
                buffer.Length + payloadLength > DeviceProtocol.MaxJpegBytes)
            {
                Reset();
                return null;
            }

            buffer.Write(packet, 4, payloadLength);
            fragmentNumber = packetFragmentNumber;
            fragmentCount++;
            if (!isLast) return null;

            byte[] complete = JpegUtil.TrimToEndMarker(buffer.ToArray());
            Reset();
            return complete;
        }

        /// <summary>Discards any in-progress frame. Safe to call at any time, including from a finally block.</summary>
        public void Reset()
        {
            if (buffer != null) { buffer.Dispose(); buffer = null; }
        }
    }
}
