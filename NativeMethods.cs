using System.Runtime.InteropServices;

namespace EarPicker
{
    /// <summary>
    /// Keeps the display awake while the live video is on screen, the way a
    /// video player would, so the picture doesn't go dark mid-inspection.
    /// </summary>
    static class DisplaySleep
    {
        const uint EsContinuous = 0x80000000;
        const uint EsSystemRequired = 0x00000001;
        const uint EsDisplayRequired = 0x00000002;

        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint executionState);

        public static void Keep()
        {
            SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
        }

        public static void Allow()
        {
            SetThreadExecutionState(EsContinuous);
        }
    }
}
