using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Chuvadi.Print
{
    /// <summary>A single auditable print event (one per job; supports regulatory audit trails, for example AERB and NABH in India).</summary>
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
        /// <summary>
        /// Why the job failed: the error message, or, for a <see cref="PrintSettings.Private"/> job, only the error's
        /// type and code (<see cref="ErrorCode"/>, for example "IOException (0x80131620)"), so nothing identifying such
        /// as a file path is recorded (D-375). Null when the job did not fail.
        /// </summary>
        public string? Error { get; init; }

        /// <summary>
        /// How the job was printed (D-96): <c>Raster</c> for <see cref="PrintJob.PrintTo"/>, <c>Document</c> for
        /// <see cref="PrintJob.SubmitTo"/>, or the route a <see cref="PrintRouter"/> chose (<c>PdfAsIs</c>,
        /// <c>PageDescription</c>, <c>Cups</c>, <c>WindowsQueue</c>, <c>Raster</c>), followed by
        /// <c> (fallback: ...)</c> listing the routes skipped before it. <c>None</c> when no route could print the job.
        /// Null for events recorded by older versions.
        /// </summary>
        public string? Route { get; init; }

        /// <summary>
        /// What was used for each setting left to the printer (<see cref="PrintSettings.PrinterDefaults"/>), separated by
        /// "; ": for example "paper: printer default (A4); copies: printer default (not reported)". Null when no
        /// setting was left to the printer, and for events recorded by older versions.
        /// </summary>
        public string? PrinterDefaults { get; init; }

        /// <summary>
        /// The error text recorded for a private job: the exception's type name and its HRESULT, without its message,
        /// for example "IOException (0x80131620)" (since 1.0.0-rc.5).
        /// </summary>
        public static string ErrorCode(Exception error)
        {
            if (error is null) throw new ArgumentNullException(nameof(error));
            return error.GetType().Name + " (0x" + error.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>The error text recorded for a private job that failed without an exception.</summary>
        public const string PrivateErrorText = "Failed (details withheld for a private job)";
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

    /// <summary>
    /// Appends one tab-delimited line per event to a local file. Thread-safe. Columns: job id, title, pages,
    /// copies, printer, machine, user, status, started, completed, tenant, error, route (added in 1.0.0-rc.2), then,
    /// only for a job that left settings to the printer, printer defaults (added in 1.0.0-rc.4). New columns go at
    /// the end, so older columns keep their positions.
    /// </summary>
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
              .Append(Escape(e.Error)).Append('\t')
              .Append(Escape(e.Route));
            // The printer defaults column is written only when a setting was left to the printer, so lines for
            // other jobs stay exactly as earlier versions wrote them.
            if (e.PrinterDefaults is not null) sb.Append('\t').Append(Escape(e.PrinterDefaults));
            lock (_gate)
            {
                File.AppendAllText(_path, sb.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }

        private static string Escape(string? value)
            => value is null ? string.Empty : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
