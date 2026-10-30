using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Print;

namespace ChuvadiPdfPrinter
{
    /// <summary>The outcome of capturing one job.</summary>
    public sealed class CaptureResult
    {
        public bool Succeeded { get; init; }

        /// <summary>Where the PDF was saved (null when the job failed).</summary>
        public string? OutputPath { get; init; }

        public long Bytes { get; init; }
        public bool IsPdf { get; init; }
        public int Pages { get; init; }
        public string? Error { get; init; }
        public IReadOnlyList<(ForwardTarget Target, PrintResult Result)> Forwards { get; init; } = Array.Empty<(ForwardTarget, PrintResult)>();
    }

    /// <summary>
    /// Turns captured print data into saved PDFs: writes to a hidden staging file, names and places it
    /// without collisions, records one audit event, then sends it to every configured physical printer.
    /// Shared by the Windows 10 queue path (pipe) and the Windows 11 Print Support App path (outbox).
    /// </summary>
    public sealed class AgentEngine
    {
        internal const string StagingFolderName = ".chuvadi-incoming";

        private readonly IForwarderFactory _forwarders;
        private readonly Func<DateTimeOffset> _clock;

        public AgentEngine(PrinterConfig config, IPrintAuditSink audit, ILog log,
                           IForwarderFactory? forwarders = null, Func<DateTimeOffset>? clock = null)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            Log = log ?? throw new ArgumentNullException(nameof(log));
            _forwarders = forwarders ?? new DefaultForwarderFactory();
            _clock = clock ?? (() => DateTimeOffset.Now);
        }

        public PrinterConfig Config { get; }
        public IPrintAuditSink Audit { get; }
        public ILog Log { get; }

        /// <summary>
        /// Called once when a user's output folder is first created, with the folder and the user name; the
        /// Windows agent uses it to make the folder private to that user.
        /// </summary>
        public Action<string, string>? UserFolderCreated { get; set; }

        /// <summary>The user's output folder, created (and reported through <see cref="UserFolderCreated"/>) if missing.</summary>
        internal string EnsureUserFolder(string? userName)
        {
            string folder = Config.ResolveOutputFolder(userName);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                if (Config.PerUserSubfolder && !string.IsNullOrWhiteSpace(userName))
                {
                    try
                    {
                        UserFolderCreated?.Invoke(folder, userName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Could not secure the output folder " + folder, ex);
                    }
                }
            }
            return folder;
        }

        /// <summary>Reads one job's data (the spooler's output for the queue) and saves it.</summary>
        public CaptureResult CaptureFromStream(Stream input, JobInfo job, CancellationToken cancellationToken = default)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (job is null) throw new ArgumentNullException(nameof(job));
            string? staging = null;
            try
            {
                string folder = EnsureUserFolder(job.UserName);
                string stagingFolder = Path.Combine(folder, StagingFolderName);
                Directory.CreateDirectory(stagingFolder);
                TryHide(stagingFolder);
                staging = Path.Combine(stagingFolder, Guid.NewGuid().ToString("N") + ".partial");
                long bytes = 0;
                using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        bytes += read;
                        if (bytes > Config.MaxJobBytes)
                            throw new InvalidDataException("Job exceeds MaxJobBytes (" + Config.MaxJobBytes + " bytes).");
                        output.Write(buffer, 0, read);
                    }
                }
                if (bytes == 0) throw new InvalidDataException("The job contained no data.");
                CaptureResult result = Complete(staging, job, savedPath: null);
                staging = null;
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordFailure(job, ex);
                return new CaptureResult { Succeeded = false, Error = ex.Message };
            }
            finally
            {
                if (staging is not null) TryDelete(staging);
            }
        }

        /// <summary>
        /// Saves a PDF that already exists on disk (from the Print Support App outbox). When
        /// <paramref name="savedPath"/> is set the user already saved it there (Save As); the file at
        /// <paramref name="pdfPath"/> is then only used for auditing and forwarding and is deleted afterwards.
        /// </summary>
        public CaptureResult CompleteFile(string pdfPath, JobInfo job, string? savedPath)
        {
            if (pdfPath is null) throw new ArgumentNullException(nameof(pdfPath));
            if (job is null) throw new ArgumentNullException(nameof(job));
            try
            {
                if (savedPath is null)
                {
                    // Move into the staging folder first so placement is a same-volume atomic move.
                    string stagingFolder = Path.Combine(EnsureUserFolder(job.UserName), StagingFolderName);
                    Directory.CreateDirectory(stagingFolder);
                    TryHide(stagingFolder);
                    string staging = Path.Combine(stagingFolder, Guid.NewGuid().ToString("N") + ".partial");
                    File.Copy(pdfPath, staging);
                    CaptureResult placed = Complete(staging, job, null);
                    if (placed.Succeeded) TryDelete(pdfPath);
                    return placed;
                }
                CaptureResult saved = Complete(pdfPath, job, savedPath);
                TryDelete(pdfPath);
                return saved;
            }
            catch (Exception ex)
            {
                RecordFailure(job, ex);
                return new CaptureResult { Succeeded = false, Error = ex.Message };
            }
        }

        private CaptureResult Complete(string stagedPdf, JobInfo job, string? savedPath)
        {
            bool isPdf = PdfQuickInfo.LooksLikePdf(stagedPdf);
            if (!isPdf) Log.Error("Job '" + job.Title + "' (" + job.JobId + ") is not a complete PDF; saved as received.");
            int pages = isPdf ? PdfQuickInfo.CountPages(stagedPdf) : 0;
            long bytes = new FileInfo(stagedPdf).Length;

            string finalPath;
            string forwardSource;
            if (savedPath is null)
            {
                string folder = Config.ResolveOutputFolder(job.UserName);
                string baseName = OutputNamer.Format(Config.FileNamePattern, job, _clock());
                finalPath = OutputNamer.Place(stagedPdf, folder, baseName, Config.Collision);
                forwardSource = finalPath;
            }
            else
            {
                finalPath = savedPath;
                forwardSource = stagedPdf;
            }

            Audit.Record(new PrintAuditEvent
            {
                JobId = Guid.NewGuid(),
                DocumentTitle = job.Title,
                PageCount = pages,
                Copies = 1,
                PrinterName = Config.QueueName + " -> " + finalPath,
                MachineName = string.IsNullOrEmpty(job.MachineName) ? Environment.MachineName : job.MachineName.TrimStart('\\'),
                UserName = job.UserName,
                Status = PrintJobStatus.Printed,
                StartedAt = job.Submitted,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = isPdf ? null : "output is not a complete PDF"
            });
            Log.Info("Saved '" + job.Title + "' for " + job.UserName + " (" + bytes + " bytes, " + pages + " pages) to " + finalPath);

            var forwards = new List<(ForwardTarget, PrintResult)>();
            foreach (ForwardTarget target in Config.ForwardTo)
            {
                if (!target.Enabled) continue;
                PrintResult result;
                try
                {
                    result = _forwarders.Create(target).Forward(forwardSource, job, target, Audit);
                }
                catch (Exception ex)
                {
                    result = PrintResult.Failed(Guid.NewGuid(), ex.Message);
                }
                forwards.Add((target, result));
                if (result.Status == PrintJobStatus.Printed) Log.Info("Forwarded '" + job.Title + "' to " + target.Printer);
                else Log.Error("Forwarding '" + job.Title + "' to " + target.Printer + " failed: " + result.Error);
            }

            return new CaptureResult
            {
                Succeeded = true,
                OutputPath = finalPath,
                Bytes = bytes,
                IsPdf = isPdf,
                Pages = pages,
                Forwards = forwards
            };
        }

        private void RecordFailure(JobInfo job, Exception ex)
        {
            Log.Error("Capturing '" + job.Title + "' (" + job.JobId + ") failed", ex);
            try
            {
                Audit.Record(new PrintAuditEvent
                {
                    JobId = Guid.NewGuid(),
                    DocumentTitle = job.Title,
                    PrinterName = Config.QueueName,
                    MachineName = Environment.MachineName,
                    UserName = job.UserName,
                    Status = PrintJobStatus.Failed,
                    StartedAt = job.Submitted,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Error = ex.Message
                });
            }
            catch (Exception auditEx)
            {
                Log.Error("Audit write failed", auditEx);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void TryHide(string folder)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var info = new DirectoryInfo(folder);
                if ((info.Attributes & FileAttributes.Hidden) == 0) info.Attributes |= FileAttributes.Hidden;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
