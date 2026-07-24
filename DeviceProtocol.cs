namespace EarPicker
{
    /// <summary>
    /// Wire-level constants for the camera's UDP control protocol: the ports
    /// it listens on and the short command codes its firmware expects. Named
    /// here once so the rest of the app never touches a raw magic byte.
    /// </summary>
    static class DeviceProtocol
    {
        public const string DeviceAddress = "192.168.5.1";

        public const int CommandPort = 58090;
        public const int VideoPort = 58080;
        public const int SensorPort = 58098;

        public const int MaxJpegBytes = 8 * 1024 * 1024;
        public const int MaxFrameFragments = 4096;

        public static readonly byte[] ReadBoardInfo = { 0x66, 0x39, 0x01, 0x01 };
        public static readonly byte[] ReadBattery = { 0x66, 0x3A };
        public static readonly byte[] ReadBrightness = { 0x66, 0x3C, 0xFE };

        public static byte[] SetBrightness(byte value)
        {
            return new byte[] { 0x66, 0x3C, value };
        }

        public static readonly byte[] VideoStreamStop = { 0x20, 0x37 };
        public static readonly byte[] VideoStreamStart = { 0x20, 0x36 };

        public static readonly byte[] SensorStreamStart = { 0x86, 0x06, 0x01 };
        public static readonly byte[] SensorStreamStop = { 0x86, 0x06, 0x00 };
    }
}
