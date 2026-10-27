using System;

namespace Chuvadi.Print
{
    /// <summary>The outcome of a print or submit operation.</summary>
    public sealed class PrintResult
    {
        public Guid JobId { get; init; } = Guid.NewGuid();
        public PrintJobStatus Status { get; init; } = PrintJobStatus.Created;

        /// <summary>Distinct document pages sent to the target.</summary>
        public int PagesPrinted { get; init; }

        /// <summary>Sides carrying a document page sent to the target, counting software copies (excludes inserted blanks).</summary>
        public int ImpressionsPrinted { get; init; }

        /// <summary>Physical sheets sent to the target (copies made by the target itself are not counted).</summary>
        public int SheetsPrinted { get; init; }

        public string? Error { get; init; }

        public static PrintResult Printed(Guid jobId, int pagesPrinted)
            => new() { JobId = jobId, Status = PrintJobStatus.Printed, PagesPrinted = pagesPrinted };

        public static PrintResult Failed(Guid jobId, string error)
            => new() { JobId = jobId, Status = PrintJobStatus.Failed, Error = error };

        public static PrintResult Cancelled(Guid jobId)
            => new() { JobId = jobId, Status = PrintJobStatus.Cancelled };
    }
}
