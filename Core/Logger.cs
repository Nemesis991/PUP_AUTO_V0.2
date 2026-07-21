using System;
using System.IO;

namespace PUP_AUTO.Core
{
    public class Logger
    {
        private readonly string _logFilePath;

        public Logger(string logFilePath = "PUP_AUTO_Logs.txt")
        {
            _logFilePath = logFilePath;
            
            // Ensure the directory exists if a path is provided
            string dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public void LogSuccess(string message)
        {
            WriteLog("SUCCESS", message);
        }

        public void LogError(string message)
        {
            WriteLog("ERROR", message);
        }

        public void LogWarning(string message)
        {
            WriteLog("WARNING", message);
        }

        private void WriteLog(string level, string message)
        {
            try
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, logEntry);
            }
            catch
            {
                // Safely ignore file write exceptions to prevent crashing the main process
            }
        }
    }
}
