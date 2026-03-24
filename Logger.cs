using System;
using System.IO;

namespace leeyez_kai
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private const string LogFile = "leeyez_debug.log";

        public static void Log(string message)
        {
            lock (_lock)
            {
                try
                {
                    string safe = (message ?? "").Replace("\0", "[NULL]");
                    File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {safe}\n");
                }
                catch { }
            }
        }
    }
}
