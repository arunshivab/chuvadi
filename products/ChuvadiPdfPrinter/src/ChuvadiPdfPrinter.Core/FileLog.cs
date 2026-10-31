using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ChuvadiPdfPrinter
{
    /// <summary>Diagnostic log.</summary>
    public interface ILog
    {
        void Info(string message);
        void Error(string message, Exception? exception = null);
    }

    /// <summary>Appends timestamped lines to a file; rolls to "&lt;name&gt;.1" past <see cref="MaxBytes"/>. Thread-safe.</summary>
    public sealed class FileLog : ILog
    {
        private readonly string _path;
        private readonly object _gate = new();

        public FileLog(string path, long maxBytes = 5 * 1024 * 1024)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            MaxBytes = maxBytes;
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        }

        public long MaxBytes { get; }

        /// <summary>Also echo to the console (agent --console mode).</summary>
        public bool Echo { get; set; }

        public void Info(string message) => Write("INFO ", message);

        public void Error(string message, Exception? exception = null)
            => Write("ERROR", exception is null ? message : message + " | " + exception.GetType().Name + ": " + exception.Message);

        private void Write(string level, string message)
        {
            string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) + " " + level + " " + message;
            lock (_gate)
            {
                try
                {
                    var info = new FileInfo(_path);
                    if (info.Exists && info.Length > MaxBytes) File.Move(_path, _path + ".1", overwrite: true);
                    File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (IOException)
                {
                    // Logging must never stop printing.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            if (Echo) Console.WriteLine(line);
        }
    }

    /// <summary>Collects log lines in memory (tests).</summary>
    public sealed class MemoryLog : ILog
    {
        private readonly object _gate = new();
        private readonly System.Collections.Generic.List<string> _lines = new();

        public System.Collections.Generic.IReadOnlyList<string> Lines
        {
            get { lock (_gate) return _lines.ToArray(); }
        }

        public void Info(string message) { lock (_gate) _lines.Add("INFO " + message); }
        public void Error(string message, Exception? exception = null) { lock (_gate) _lines.Add("ERROR " + message + (exception is null ? "" : " | " + exception.Message)); }
    }
}
