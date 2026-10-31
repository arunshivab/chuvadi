using System;
using System.Collections.Generic;

namespace ChuvadiPdfPrinter
{
    /// <summary>Spooler job status flags (Win32 JOB_STATUS_*) that matter for capture.</summary>
    [Flags]
    public enum SpoolJobStatus : uint
    {
        None = 0,
        Paused = 0x00000001,
        Error = 0x00000002,
        Deleting = 0x00000004,
        Spooling = 0x00000008,
        Printing = 0x00000010,
        Offline = 0x00000020,
        PaperOut = 0x00000040,
        Printed = 0x00000080,
        Deleted = 0x00000100,
        Blocked = 0x00000200,
        UserIntervention = 0x00000400,
        Restart = 0x00000800,
        Complete = 0x00001000,
        Retained = 0x00002000
    }

    /// <summary>What is known about the print job being captured.</summary>
    public sealed class JobInfo
    {
        public int JobId { get; init; }

        /// <summary>Document name as the printing application gave it.</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>User who printed (as the spooler reports it, possibly DOMAIN\user).</summary>
        public string UserName { get; init; } = string.Empty;

        public string MachineName { get; init; } = string.Empty;
        public DateTimeOffset Submitted { get; init; } = DateTimeOffset.Now;
        public SpoolJobStatus Status { get; init; }
        public int TotalPages { get; init; }

        /// <summary>Metadata used when the spooler could not be asked (the job is still captured).</summary>
        public static JobInfo Unknown(DateTimeOffset now) => new() { Title = "Untitled", Submitted = now };
    }

    /// <summary>Reads the jobs currently in a print queue.</summary>
    public interface IJobInfoProvider
    {
        IReadOnlyList<JobInfo> GetJobs(string queueName);
    }

    /// <summary>Chooses which queued job a port connection belongs to.</summary>
    public static class SpoolerJobSelector
    {
        /// <summary>
        /// The spooler sends one job at a time to a port, so the job flagged Printing is the one writing to
        /// the pipe. If none is flagged yet (the status lags the port open), the oldest job that has finished
        /// spooling is taken, then simply the oldest job. Returns null for an empty queue.
        /// </summary>
        public static JobInfo? SelectPrinting(IEnumerable<JobInfo> jobs)
        {
            if (jobs is null) throw new ArgumentNullException(nameof(jobs));
            JobInfo? printing = null, spooled = null, any = null;
            foreach (JobInfo job in jobs)
            {
                if ((job.Status & (SpoolJobStatus.Deleting | SpoolJobStatus.Deleted)) != 0) continue;
                if ((job.Status & SpoolJobStatus.Printing) != 0 && Older(job, printing)) printing = job;
                if ((job.Status & SpoolJobStatus.Spooling) == 0 && Older(job, spooled)) spooled = job;
                if (Older(job, any)) any = job;
            }
            return printing ?? spooled ?? any;
        }

        private static bool Older(JobInfo candidate, JobInfo? current)
            => current is null || candidate.Submitted < current.Submitted
               || (candidate.Submitted == current.Submitted && candidate.JobId < current.JobId);
    }
}
