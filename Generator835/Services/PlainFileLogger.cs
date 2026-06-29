// Edi.Generator835/Services/PlainFileLogger.cs
using System;
using System.IO;

namespace Edi.Generator835.Services
{
    public class PlainFileLogger : IEdiLogger
    {
        private readonly string _logFilePath;
        private readonly object _lock = new object();

        public PlainFileLogger(string logDirectory)
        {
            Directory.CreateDirectory(logDirectory);
            _logFilePath = Path.Combine(logDirectory, "edi.log");
        }

        private void Write(string level, string message, Exception ex = null)
        {
            var line = $"{DateTime.UtcNow:o} [{level}] {message}";
            if (ex != null)
                line += $"\n  Exception: {ex}";

            lock (_lock)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }

        public void Debug(string message)       => Write("DBG", message);
        public void Information(string message) => Write("INF", message);
        public void Warning(string message)     => Write("WRN", message);
        public void Error(string message, Exception ex = null) => Write("ERR", message, ex);
    }
}