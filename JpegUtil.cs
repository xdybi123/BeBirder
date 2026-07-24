namespace EarPicker
{
    static class JpegUtil
    {
        /// <summary>
        /// Verifies the SOI marker and trims off anything after the last EOI
        /// marker, discarding padding the device tacked onto the end of the
        /// frame. Returns null if this isn't a JPEG or has no EOI at all
        /// (a truncated frame).
        /// </summary>
        public static byte[] TrimToEndMarker(byte[] data)
        {
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return null;

            for (int i = data.Length - 2; i >= 2; i--)
            {
                if (data[i] == 0xFF && data[i + 1] == 0xD9)
                {
                    byte[] trimmed = new byte[i + 2];
                    System.Buffer.BlockCopy(data, 0, trimmed, 0, trimmed.Length);
                    return trimmed;
                }
            }
            return null;
        }
    }
}
