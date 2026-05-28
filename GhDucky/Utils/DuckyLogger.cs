using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Grasshopper;

namespace GhDucky.Utils
{
    /// <summary>
    /// A simple rolling file-based TraceListener that writes logs to the Grasshopper AppData folder.
    /// Used to provide persistent observability for background service failures in production.
    /// </summary>
    public sealed class DuckyLogger : TraceListener
    {
        private readonly string _logFilePath;
        private readonly object _lock = new();

        public DuckyLogger()
        {
            try
            {
                // Use the Grasshopper-specific AppData folder, which is idiomatic for both platforms.
                // Windows: %AppData%\Roaming\Grasshopper
                // macOS: ~/Library/Application Support/McNeel/Rhinoceros/[Version]/Grasshopper
                var logDir = Path.Combine(Folders.AppDataFolder, "Logs", "GhDucky");

                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                _logFilePath = Path.Combine(logDir, $"gh-ducky-{DateTime.Now:yyyyMMdd}.log");
                
                // Write a separator for the new session
                WriteLine($"--- Session Started: {DateTime.Now:O} ---");
            }
            catch
            {
                // If we can't create the log directory, the logger will just ignore Write calls.
                _logFilePath = null;
            }
        }

        public override void Write(string message)
        {
            if (_logFilePath == null) return;

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, message);
                }
                catch
                {
                    // Ignore logging failures to prevent crashing the host.
                }
            }
        }

        public override void WriteLine(string message)
        {
            Write($"{DateTime.Now:HH:mm:ss.fff} [{Thread.CurrentThread.ManagedThreadId:D2}] {message}{Environment.NewLine}");
        }

        /// <summary>
        /// Registers the logger as a global Trace listener.
        /// </summary>
        public static void Register()
        {
            // Ensure we don't register multiple times
            foreach (TraceListener listener in Trace.Listeners)
            {
                if (listener is DuckyLogger) return;
            }

            Trace.Listeners.Add(new DuckyLogger());
        }
    }
}
