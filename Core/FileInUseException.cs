namespace PUP_AUTO.Core
{
    /// <summary>An output file could not be written because another program (usually Excel) has it open.</summary>
    public sealed class FileInUseException : IOException
    {
        public FileInUseException(string filePath, Exception inner)
            : base($"Файлът {Path.GetFileName(filePath)} е отворен в друга програма — затворете го и пуснете отново.", inner)
        {
            FilePath = filePath;
        }

        public string FilePath { get; }

        /// <summary>True for the Win32 sharing (32) and lock (33) violations, which is what an open file gives.</summary>
        public static bool IsSharingViolation(int hresult)
        {
            int code = hresult & 0xFFFF;
            return code == 32 || code == 33;
        }
    }
}
