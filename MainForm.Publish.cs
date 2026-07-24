using System;
using System.Net;
using System.Net.Sockets;

namespace EarPicker
{
    partial class MainForm
    {
        void TogglePublisher()
        {
            if (!publishCheck.Checked)
            {
                publisher.Stop();
                publishPort.Enabled = true;
                publishStatus.Text = "Publisher off";
                return;
            }

            int port;
            if (!Int32.TryParse(publishPort.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                SetPublisherChecked(false);
                publishStatus.Text = "Invalid port";
                return;
            }

            try
            {
                publisher.Start(port);
                publishPort.Enabled = false;
                string networkAddress = GetLocalNetworkAddress();
                string networkLine = networkAddress != null
                    ? "Network: http://" + networkAddress + ":" + port + "/"
                    : "Network: (LAN address unavailable)";
                publishStatus.Text = "http://127.0.0.1:" + port + "/\n" + networkLine;
            }
            catch (Exception ex)
            {
                TraceError("Publisher start", ex);
                SetPublisherChecked(false);
                publishStatus.Text = "Publish error: " + ex.Message;
            }
        }

        void SetPublisherChecked(bool value)
        {
            changingPublisherCheck = true;
            try { publishCheck.Checked = value; }
            finally { changingPublisherCheck = false; }
        }

        /// <summary>
        /// Best-effort local IPv4 address for the "Network:" line - specifically
        /// the address of whichever interface this PC would use to reach the
        /// camera's own subnet, since that's the network other viewers need to
        /// be on too. A UDP "connect" is just a routing-table lookup here; no
        /// packet is actually sent. Returns null if it can't be determined.
        /// </summary>
        static string GetLocalNetworkAddress()
        {
            try
            {
                using (Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    probe.Connect(DeviceProtocol.DeviceAddress, DeviceProtocol.CommandPort);
                    IPEndPoint local = probe.LocalEndPoint as IPEndPoint;
                    return local != null ? local.Address.ToString() : null;
                }
            }
            catch { return null; }
        }
    }
}
