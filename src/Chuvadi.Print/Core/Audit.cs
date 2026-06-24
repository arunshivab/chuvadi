using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Chuvadi.Print
{
    /// <summary>A single auditable print event (one per job; supports AERB/NABH trails).</summary>
    public sealed class PrintAuditEvent
    {
        public Guid JobId { get; init; } = Guid.NewGuid();
        public string? DocumentTitle { get; init; }
        public int PageCount { get; init; }
        public int Copies { get; init; } = 1;
        public string PrinterName { get; init; } = string.Empty;
        public string MachineName { get; init; } = string.Empty;
        public string? UserName { get; init; }
        public PrintJobStatus Status { get; init; } = PrintJobStatus.Created;
        public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? CompletedAt { get; init; }
        public string? TenantId { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>Receives audit events. Phase 1 uses local sinks; Phase 2 supplies a coordinator sink.</summary>
    public interface IPrintAuditSink
    {
        void Record(PrintAuditEvent auditEvent);
    }

    /// <summary>An audit sink that discards events.</summary>
    public sealed class NullAuditSink : IPrintAuditSink
    {
        public static readonly NullAuditSink Instance = new();
        private NullAuditSink() { }
        public void Record(PrintAuditEvent auditEvent) { }
    }

    /// <summary>Appends one tab-delimited line per event to a local file. Thread-safe.</summary>
    public sealed class FileAuditSink : IPrintAuditSink
    {
        private readonly string _path;
        private readonly object _gate = new();

        public FileAuditSink(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public void Record(PrintAuditEvent e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            var sb = new StringBuilder();
            sb.Append(e.JobId).Append('\t')
              .Append(Escape(e.DocumentTitle)).Append('\t')
              .Append(e.PageCount.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(e.Copies.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(Escape(e.PrinterName)).Append('\t')
              .Append(Escape(e.MachineName)).Append('\t')
              .Append(Escape(e.UserName)).Append('\t')
              .Append(e.Status).Append('\t')
              .Append(e.StartedAt.ToString("o", CultureInfo.InvariantCulture)).Append('\t')
              .Append(e.CompletedAt?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty).Append('\t')
              .Append(Escape(e.TenantId)).Append('\t')
              .Append(Escape(e.Error));
            lock (_gate)
            {
                File.AppendAllText(_path, sb.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }

        private static string Escape(string? value)
            => value is null ? string.Empty : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
