namespace PUP_AUTO.CadRegister
{
    /// <summary>
    /// Decoder for MIK, the Bulgarian DOS code page used by AGKK .cad files (not windows-1251).
    /// 0x00-0x7F is ASCII; 0x80-0xBF is the Cyrillic alphabet in alphabetical order:
    /// 0x80-0x9F = А-Я, 0xA0-0xAF = а-п, 0xB0-0xBF = р-я. 0xC0-0xFF are graphics/symbols that never
    /// occur in names; they decode to U+FFFD and are counted so the caller can warn.
    /// </summary>
    public static class MikEncoding
    {
        public const char Undecodable = '�';

        private const int CyrillicCapitalA = 0x0410;
        private const int CyrillicSmallA = 0x0430;

        public static string Decode(byte[] data) => Decode(data, out _);

        public static string Decode(byte[] data, out int undecodableCount)
        {
            undecodableCount = 0;
            var chars = new char[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b < 0x80)
                {
                    chars[i] = (char)b;
                }
                else if (b < 0xA0)
                {
                    chars[i] = (char)(CyrillicCapitalA + (b - 0x80));
                }
                else if (b < 0xC0)
                {
                    chars[i] = (char)(CyrillicSmallA + (b - 0xA0));
                }
                else
                {
                    chars[i] = Undecodable;
                    undecodableCount++;
                }
            }
            return new string(chars);
        }
    }
}
