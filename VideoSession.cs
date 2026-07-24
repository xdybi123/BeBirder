using System;
using System.Net.Sockets;
using System.Threading;

namespace EarPicker
{
    /// <summary>
    /// State for one live video UDP session: the socket, the two worker
    /// threads (raw packet receive + JPEG decode), and the frame handoff
    /// slots between them.
    /// </summary>
    sealed class VideoSession
    {
        public readonly UdpClient Socket;
        public readonly AutoResetEvent FrameReady = new AutoResetEvent(false);
        public readonly object FrameLock = new object();
        public readonly JpegFragmentReassembler Reassembler = new JpegFragmentReassembler();

        public volatile bool Running = true;
        public Thread ReceiveThread, DecodeThread;
        public int FramesSinceStatus, Width, Height, LastFrameTick, LastStatusTick;
        public byte[] PendingJpeg, LatestJpeg;
        public string Error;

        public VideoSession(UdpClient socket)
        {
            Socket = socket;
            LastStatusTick = Environment.TickCount;
        }
    }
}
