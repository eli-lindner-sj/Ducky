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
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
        private const int MaxRetentionDays = 7;

        private readonly string _logDir;
        private string _currentFilePath;
        private StreamWriter _writer;
        private readonly object _lock = new();

        public DuckyLogger() : this(Path.Combine(Folders.AppDataFolder, "Logs", "GhDucky"))
        {
        }

        internal DuckyLogger(string logDir)
        {
            try
            {
                _logDir = logDir;

                if (!Directory.Exists(_logDir))
                    Directory.CreateDirectory(_logDir);

                CleanupOldLogs();
                EnsureWriter();
            }
            catch
            {
                // If we can't create the log directory, the logger will just ignore Write calls.
            }
        }

        private void CleanupOldLogs()
        {
            try
            {
                var now = DateTime.Now;
                foreach (var file in Directory.GetFiles(_logDir, "gh-ducky-*.log"))
                {
                    var lastWrite = File.GetLastWriteTime(file);
                    if ((now - lastWrite).TotalDays > MaxRetentionDays)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        private void EnsureWriter()
        {
            if (_logDir == null) return;

            var baseFileName = $"gh-ducky-{DateTime.Now:yyyyMMdd}";
            var path = Path.Combine(_logDir, $"{baseFileName}.log");

            // Handle rotation if file exists and is too large
            int suffix = 1;
            while (File.Exists(path) && new FileInfo(path).Length > MaxFileSizeBytes)
            {
                path = Path.Combine(_logDir, $"{baseFileName}_{suffix:D2}.log");
                suffix++;
            }

            if (_writer != null && _currentFilePath == path)
                return;

            _writer?.Dispose();
            _currentFilePath = path;
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
            
            _writer.WriteLine($"--- Session Continued: {DateTime.Now:O} ---");
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            var level = eventType switch
            {
                TraceEventType.Error => "ERROR",
                TraceEventType.Warning => "WARN",
                TraceEventType.Information => "INFO",
                TraceEventType.Verbose => "DEBUG",
                _ => eventType.ToString().ToUpperInvariant()
            };

            WriteLine($"[{level}] {message}");
        }

        public override void Write(string message)
        {
            lock (_lock)
            {
                try
                {
                    EnsureWriter();
                    _writer?.Write(message);
                }
                catch
                {
                    // Ignore logging failures
                }
            }
        }

        public override void WriteLine(string message)
        {
            var formatted = $"{DateTime.Now:HH:mm:ss.fff} [{Thread.CurrentThread.ManagedThreadId:D2}] {message}{Environment.NewLine}";
            Write(formatted);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_lock)
                {
                    _writer?.Dispose();
                    _writer = null;
                }
            }
            base.Dispose(disposing);
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
