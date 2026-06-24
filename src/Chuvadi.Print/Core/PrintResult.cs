using System;

namespace Chuvadi.Print
{
    /// <summary>The outcome of a print or submit operation.</summary>
    public sealed class PrintResult
    {
        public Guid JobId { get; init; } = Guid.NewGuid();
        public PrintJobStatus Status { get; init; } = PrintJobStatus.Created;
        public int PagesPrinted { get; init; }
        public string? Error { get; init; }

        public static PrintResult Printed(Guid jobId, int pagesPrinted)
            => new() { JobId = jobId, Status = PrintJobStatus.Printed, PagesPrinted = pagesPrinted };

        public static PrintResult Failed(Guid jobId, string error)
            => new() { JobId = jobId, Status = PrintJobStatus.Failed, Error = error };

        public static PrintResult Cancelled(Guid jobId)
            => new() { JobId = jobId, Status = PrintJobStatus.Cancelled };
    }
}
