using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Print.Remote
{
    /// <summary>Settings for <see cref="PrintAgent"/>.</summary>
    public sealed class PrintAgentOptions
    {
        /// <summary>The hub's base address, e.g. https://lipi.example.com/print-hub/. Required. HTTPS unless loopback or <see cref="AllowInsecureHttp"/>.</summary>
        public Uri? HubUri { get; set; }

        /// <summary>This agent's id, as registered with the host. Required.</summary>
        public string AgentId { get; set; } = string.Empty;

        /// <summary>Holds the bearer token (rotated tokens are written back here). Required.</summary>
        public IAgentCredentialStore? Credentials { get; set; }

        /// <summary>
        /// Maps a printer name from a job to a raster target (e.g. a <c>WindowsPrinterTarget</c> or
        /// <c>IppPrinterTarget</c>), or null when the agent does not have that printer. Targets that implement
        /// <see cref="IDisposable"/> are disposed after the job unless <see cref="DisposeTargets"/> is false.
        /// </summary>
        public Func<string, IPrintTarget?>? PrinterResolver { get; set; }

        /// <summary>
        /// Maps a printer name to a document target (e.g. an <c>IppDocumentTarget</c> for PDF passthrough). Tried
        /// before <see cref="PrinterResolver"/>; return null to fall through. Disposed like raster targets.
        /// </summary>
        public Func<string, IDocumentTarget?>? DocumentTargetResolver { get; set; }

        /// <summary>Dispose targets returned by the resolvers after each job (when they implement <see cref="IDisposable"/>). Default true.</summary>
        public bool DisposeTargets { get; set; } = true;

        /// <summary>The printer names the agent announces to the hub in its hello (informational).</summary>
        public Func<IReadOnlyList<string>>? PrinterListProvider { get; set; }

        /// <summary>The rasteriser for raster targets (PrintTo). Optional for document targets.</summary>
        public IPageRasterizer? Rasterizer { get; set; }

        /// <summary>Folder for the journal and payload downloads. Required; keep it private to the agent's account.</summary>
        public string? JournalFolder { get; set; }

        /// <summary>How long each poll may wait on the hub. Default 25 s.</summary>
        public int PollWaitSeconds { get; set; } = 25;

        /// <summary>First delay after a failed exchange; doubles up to <see cref="MaxBackoff"/>. Default 1 s.</summary>
        public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Longest delay between reconnection attempts. Default 60 s.</summary>
        public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Timeout for requests other than polls (polls get their wait plus 30 s). Default 100 s.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

        /// <summary>How many times a payload download is attempted (resuming where it stopped). Default 5.</summary>
        public int DownloadAttempts { get; set; } = 5;

        /// <summary>The HTTP handler (proxy, client certificates, in-process hub). Default: a new <see cref="HttpClientHandler"/>.</summary>
        public HttpMessageHandler? HttpHandler { get; set; }

        /// <summary>Whether the agent disposes <see cref="HttpHandler"/>. Default true.</summary>
        public bool DisposeHttpHandler { get; set; } = true;

        /// <summary>Allow a plain-HTTP hub address that is not loopback. Default false: tokens travel only over HTTPS.</summary>
        public bool AllowInsecureHttp { get; set; }

        /// <summary>Machine name announced to the hub. Default <see cref="Environment.MachineName"/>.</summary>
        public string MachineName { get; set; } = Environment.MachineName;

        /// <summary>Version string announced to the hub. Default: the Chuvadi.Print assembly version.</summary>
        public string? AgentVersion { get; set; }

        /// <summary>Last chance to adjust each job (render DPI, monochrome conversion...) before it prints.</summary>
        public Action<PrintJob, JobLease>? ConfigureJob { get; set; }

        /// <summary>Optional local audit sink that also receives each job's (single) event.</summary>
        public IPrintAuditSink? LocalAuditSink { get; set; }

        /// <summary>Progress of the job being printed, forwarded from <see cref="PrintJob"/>.</summary>
        public IProgress<PrintProgress>? Progress { get; set; }

        /// <summary>How long reported journal entries are kept. Default 7 days.</summary>
        public TimeSpan JournalRetention { get; set; } = TimeSpan.FromDays(7);

        /// <summary>Diagnostic log lines (connection problems, jobs). Optional.</summary>
        public Action<string>? Log { get; set; }

        internal Uri ValidatedHub()
        {
            if (HubUri is null) throw new ArgumentException("PrintAgentOptions.HubUri is required.");
            if (!HubUri.IsAbsoluteUri) throw new ArgumentException("PrintAgentOptions.HubUri must be absolute.");
            bool https = HubUri.Scheme == Uri.UriSchemeHttps;
            if (!https && HubUri.Scheme != Uri.UriSchemeHttp) throw new ArgumentException("The hub address must be http or https.");
            if (!https && !HubUri.IsLoopback && !AllowInsecureHttp)
                throw new ArgumentException("The hub address must be HTTPS (tokens are sent with every request). Set AllowInsecureHttp only for test networks.");
            TokenAgentAuthenticator.ValidateAgentId(AgentId);
            if (Credentials is null) throw new ArgumentException("PrintAgentOptions.Credentials is required.");
            if (PrinterResolver is null && DocumentTargetResolver is null)
                throw new ArgumentException("Set PrinterResolver and/or DocumentTargetResolver.");
            if (string.IsNullOrWhiteSpace(JournalFolder)) throw new ArgumentException("PrintAgentOptions.JournalFolder is required.");
            if (PollWaitSeconds < 0 || PollWaitSeconds > 300) throw new ArgumentOutOfRangeException(nameof(PollWaitSeconds));
            if (DownloadAttempts < 1) throw new ArgumentOutOfRangeException(nameof(DownloadAttempts));
            if (InitialBackoff <= TimeSpan.Zero || MaxBackoff < InitialBackoff) throw new ArgumentOutOfRangeException(nameof(MaxBackoff));
            string s = HubUri.AbsoluteUri;
            return new Uri(s.EndsWith('/') ? s : s + "/");
        }
    }

    /// <summary>
    /// The print agent: runs next to the printers, dials out to a <see cref="PrintHub"/> (P07: it never
    /// listens), long-polls for jobs, downloads each job's spool envelope (resuming interrupted downloads),
    /// prints it on a local target and reports progress and the outcome with the job's single audit event.
    /// </summary>
    /// <remarks>
    /// <para>Resilient: network failures back off exponentially (up to <see cref="PrintAgentOptions.MaxBackoff"/>)
    /// and the agent reconnects on its own. Final reports that could not be delivered are kept in the journal
    /// and resent.</para>
    /// <para>Never prints twice: before printing, the agent journals the job as started; if the hub hands
    /// the same job out again (lease expired, agent restarted), the agent reports the recorded outcome, or
    /// Failed ("interrupted") when it never finished, instead of printing it again.</para>
    /// <para>Token rotation: when the hub sends a new token, it is written to the credential store and used
    /// from the next request on.</para>
    /// <para>Jobs are printed one at a time.</para>
    /// </remarks>
    public sealed class PrintAgent : IDisposable
    {
        private readonly PrintAgentOptions _options;
        private readonly Uri _hub;
        private readonly HttpClient _http;
        private readonly AgentJournal _journal;
        private bool _helloDone;
        private bool _disposed;

        public PrintAgent(PrintAgentOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _hub = options.ValidatedHub();
            _journal = new AgentJournal(options.JournalFolder!);
            HttpMessageHandler handler = options.HttpHandler ?? new HttpClientHandler();
            _http = new HttpClient(handler, options.HttpHandler is null || options.DisposeHttpHandler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        private int _jobsCompleted;

        /// <summary>
        /// Jobs this agent has finished (printed, failed or cancelled) and whose outcome the hub has acknowledged,
        /// since it was created. Safe to read from any thread. The hub records a job's final state while handling the
        /// report, so the hub can show a job as finished slightly before this count includes it.
        /// </summary>
        public int JobsCompleted => Volatile.Read(ref _jobsCompleted);

        /// <summary>
        /// Runs until <paramref name="cancellationToken"/> is cancelled: hello, then poll, print and report,
        /// reconnecting with back-off after failures.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            TimeSpan backoff = _options.InitialBackoff;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(cancellationToken).ConfigureAwait(false);
                    backoff = _options.InitialBackoff;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    _helloDone = false;
                    Log("Hub exchange failed (" + Describe(ex) + "); retrying in " + backoff.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s.");
                    try { await Task.Delay(backoff, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _options.MaxBackoff.Ticks));
                }
            }
        }

        /// <summary>
        /// One cycle: hello (first time or after a failure), deliver outstanding reports, one poll, and print the
        /// job if one came. Returns true when a job was handled. Failures throw (see <see cref="RunAsync"/>).
        /// </summary>
        public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (!_helloDone)
            {
                await HelloAsync(cancellationToken).ConfigureAwait(false);
                _helloDone = true;
            }
            await RecoverAsync(cancellationToken).ConfigureAwait(false);

            JobLease? lease = await PollAsync(cancellationToken).ConfigureAwait(false);
            if (lease is null) return false;
            await ProcessAsync(lease, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ================================================================== protocol

        private async Task HelloAsync(CancellationToken ct)
        {
            var hello = new HelloRequest
            {
                MachineName = _options.MachineName,
                AgentVersion = _options.AgentVersion ?? typeof(PrintAgent).Assembly.GetName().Version?.ToString(),
                Printers = (_options.PrinterListProvider?.Invoke() ?? Array.Empty<string>()).ToList()
            };
            using HttpResponseMessage response = await SendJsonAsync(HttpMethod.Post, "agents/hello", hello, _options.RequestTimeout, ct).ConfigureAwait(false);
            await EnsureAsync(response, ct, HttpStatusCode.OK).ConfigureAwait(false);
        }

        private async Task<JobLease?> PollAsync(CancellationToken ct)
        {
            var poll = new PollRequest { WaitSeconds = _options.PollWaitSeconds };
            TimeSpan timeout = TimeSpan.FromSeconds(_options.PollWaitSeconds + 30);
            using HttpResponseMessage response = await SendJsonAsync(HttpMethod.Post, "agents/poll", poll, timeout, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent) return null;
            await EnsureAsync(response, ct, HttpStatusCode.OK).ConfigureAwait(false);
            return await ReadJsonAsync<JobLease>(response, ct).ConfigureAwait(false);
        }

        private async Task<StatusResponse> ReportAsync(Guid jobId, StatusReport report, CancellationToken ct)
        {
            using HttpResponseMessage response = await SendJsonAsync(HttpMethod.Post, "jobs/" + jobId.ToString("D") + "/status", report,
                                                                      _options.RequestTimeout, ct).ConfigureAwait(false);
            await EnsureAsync(response, ct, HttpStatusCode.OK).ConfigureAwait(false);
            return await ReadJsonAsync<StatusResponse>(response, ct).ConfigureAwait(false);
        }

        // ================================================================== a job

        private async Task ProcessAsync(JobLease lease, CancellationToken ct)
        {
            Guid jobId = lease.JobId;
            Log("Job " + jobId + " for '" + lease.Printer + "' (attempt " + lease.Attempt + ").");

            AgentJournalEntry? entry = _journal.Get(jobId);
            if (entry is not null)
            {
                // Seen before: never print twice. Report what is known.
                if (entry.FinalReport is null)
                {
                    entry.FinalReport = Interrupted(lease, entry);
                    entry.FinishedAt = DateTimeOffset.UtcNow;
                    _journal.Save(entry);
                    RecordLocally(entry.FinalReport);
                }
                entry.FinalReport.LeaseId = lease.LeaseId;
                await DeliverFinalAsync(entry, ct).ConfigureAwait(false);
                return;
            }

            entry = new AgentJournalEntry { JobId = jobId };
            string spool;
            try
            {
                spool = await DownloadAsync(lease, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException || (ex is RemotePrintException r && r.StatusCode is 404 or 410))
            {
                await FinishWithoutPrintingAsync(lease, entry, PrintJobStatus.Failed, "The payload could not be downloaded: " + ex.Message, ct).ConfigureAwait(false);
                return;
            }

            StatusResponse accepted = await ReportAsync(jobId, new StatusReport { LeaseId = lease.LeaseId, State = HubJobState.Accepted }, ct).ConfigureAwait(false);
            if (accepted.Final)
            {
                _journal.DeleteFiles(jobId);
                return;
            }
            if (accepted.CancelRequested)
            {
                await FinishWithoutPrintingAsync(lease, entry, PrintJobStatus.Cancelled, null, ct).ConfigureAwait(false);
                return;
            }

            entry.StartedAt = DateTimeOffset.UtcNow;
            _journal.Save(entry);

            StatusReport final = await PrintAsync(lease, spool, ct).ConfigureAwait(false);
            entry.FinalReport = final;
            entry.FinishedAt = DateTimeOffset.UtcNow;
            _journal.Save(entry);
            _journal.DeleteFiles(jobId);
            await DeliverFinalAsync(entry, ct).ConfigureAwait(false);
        }

        private async Task<StatusReport> PrintAsync(JobLease lease, string spool, CancellationToken ct)
        {
            var capture = new CapturingAuditSink();
            var progress = new LatestProgress(_options.Progress);
            PrintResult result;
            object? target = null;
            try
            {
                PrintJob job = PrintJob.LoadSpool(spool);
                job.JobId = lease.JobId;
                job.WithAuditSink(capture).WithThrowOnFailure(false).WithProgress(progress);
                if (_options.Rasterizer is not null) job.WithRasterizer(_options.Rasterizer);
                _options.ConfigureJob?.Invoke(job, lease);
                job.WithAuditSink(capture).WithThrowOnFailure(false).WithProgress(progress);

                IDocumentTarget? documentTarget = _options.DocumentTargetResolver?.Invoke(lease.Printer);
                IPrintTarget? rasterTarget = documentTarget is null ? _options.PrinterResolver?.Invoke(lease.Printer) : null;
                target = (object?)documentTarget ?? rasterTarget;
                if (target is null)
                    return Synthesized(lease, job, PrintJobStatus.Failed, "This agent has no printer named '" + lease.Printer + "'.");

                using var printCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task<PrintResult> work = Task.Run(() => documentTarget is not null
                    ? job.SubmitTo(documentTarget, printCts.Token)
                    : job.PrintTo(rasterTarget!, printCts.Token), CancellationToken.None);

                TimeSpan beat = TimeSpan.FromSeconds(Math.Max(1, lease.LeaseSeconds / 3.0));
                while (!work.IsCompleted)
                {
                    Task delay = Task.Delay(beat, CancellationToken.None);
                    if (await Task.WhenAny(work, delay).ConfigureAwait(false) == work) break;
                    try
                    {
                        StatusResponse r = await ReportAsync(lease.JobId, new StatusReport
                        {
                            LeaseId = lease.LeaseId,
                            State = HubJobState.Printing,
                            PagesPrinted = progress.CurrentPage,
                            ImpressionsPrinted = progress.Impressions
                        }, ct).ConfigureAwait(false);
                        if (r.CancelRequested || r.Final)
                        {
                            Log("Job " + lease.JobId + ": cancel requested by the hub.");
                            printCts.Cancel();
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // Agent stopping: printCts is linked, the job cancels itself.
                    }
                    catch (Exception ex) when (IsTransient(ex))
                    {
                        Log("Heartbeat for job " + lease.JobId + " failed (" + Describe(ex) + "); still printing.");
                    }
                }
                result = await work.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
            {
                // Spool unreadable or job misconfigured before PrintTo ran (PrintTo itself never throws here).
                if (capture.Count == 0)
                    return Synthesized(lease, null, PrintJobStatus.Failed, ex.Message);
                result = new PrintResult { JobId = lease.JobId, Status = PrintJobStatus.Failed, Error = ex.Message };
            }
            finally
            {
                if (_options.DisposeTargets) (target as IDisposable)?.Dispose();
            }

            PrintAuditEvent audit = capture.Last ?? throw new InvalidOperationException("The print job recorded no audit event.");
            _options.LocalAuditSink?.Record(audit);
            return new StatusReport
            {
                LeaseId = lease.LeaseId,
                State = ToHubState(result.Status),
                PagesPrinted = result.PagesPrinted,
                ImpressionsPrinted = result.ImpressionsPrinted,
                SheetsPrinted = result.SheetsPrinted,
                Error = result.Error,
                Audit = AuditRecord.From(audit)
            };
        }

        private async Task FinishWithoutPrintingAsync(JobLease lease, AgentJournalEntry entry, PrintJobStatus status, string? error, CancellationToken ct)
        {
            entry.FinalReport = Synthesized(lease, null, status, error);
            entry.FinishedAt = DateTimeOffset.UtcNow;
            _journal.Save(entry);
            _journal.DeleteFiles(lease.JobId);
            await DeliverFinalAsync(entry, ct).ConfigureAwait(false);
        }

        /// <summary>The single audit event for a job that never reached PrintTo/SubmitTo.</summary>
        private StatusReport Synthesized(JobLease lease, PrintJob? job, PrintJobStatus status, string? error)
        {
            var audit = new PrintAuditEvent
            {
                JobId = lease.JobId,
                DocumentTitle = job?.DocumentTitle ?? lease.Title,
                PageCount = 0,
                Copies = job?.Settings.Copies ?? 1,
                PrinterName = lease.Printer,
                MachineName = _options.MachineName,
                UserName = job?.UserName,
                Status = status,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                TenantId = job?.TenantId,
                Error = status == PrintJobStatus.Failed ? error : null
            };
            _options.LocalAuditSink?.Record(audit);
            return new StatusReport
            {
                LeaseId = lease.LeaseId,
                State = ToHubState(status),
                Error = error,
                Audit = AuditRecord.From(audit)
            };
        }

        private StatusReport Interrupted(JobLease? lease, AgentJournalEntry entry)
        {
            const string message = "The agent stopped while this job was printing (crash or power loss). It is not printed again, " +
                                   "to avoid duplicate output; check the printer and resubmit if pages are missing.";
            return new StatusReport
            {
                LeaseId = lease?.LeaseId ?? Guid.Empty,
                State = HubJobState.Failed,
                Error = message,
                Audit = new AuditRecord
                {
                    JobId = entry.JobId,
                    DocumentTitle = lease?.Title,
                    PrinterName = lease?.Printer ?? string.Empty,
                    MachineName = _options.MachineName,
                    Status = PrintJobStatus.Failed,
                    StartedAt = entry.StartedAt ?? DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Error = message
                }
            };
        }

        private void RecordLocally(StatusReport report)
        {
            if (report.Audit is not null) _options.LocalAuditSink?.Record(report.Audit.ToEvent());
        }

        /// <summary>Sends a journalled final report, retrying until the hub takes it (or the agent stops).</summary>
        private async Task DeliverFinalAsync(AgentJournalEntry entry, CancellationToken ct)
        {
            TimeSpan backoff = _options.InitialBackoff;
            while (true)
            {
                try
                {
                    await ReportAsync(entry.JobId, entry.FinalReport!, ct).ConfigureAwait(false);
                    break;
                }
                catch (RemotePrintException ex) when (ex.StatusCode is 404 or 410)
                {
                    Log("Job " + entry.JobId + " is gone from the hub; its outcome is kept in the journal only.");
                    break;
                }
                catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
                {
                    Log("Final report for job " + entry.JobId + " failed (" + Describe(ex) + "); retrying.");
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _options.MaxBackoff.Ticks));
                }
            }
            entry.Reported = true;
            _journal.Save(entry);
            Interlocked.Increment(ref _jobsCompleted);
            Log("Job " + entry.JobId + ": " + entry.FinalReport!.State + (entry.FinalReport.Error is null ? "" : " - " + entry.FinalReport.Error));
        }

        /// <summary>After a restart: report interrupted jobs and outcomes the hub has not acknowledged; purge old entries.</summary>
        private async Task RecoverAsync(CancellationToken ct)
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow - _options.JournalRetention;
            foreach (AgentJournalEntry entry in _journal.List())
            {
                if (entry.Reported)
                {
                    if (entry.FinishedAt is DateTimeOffset done && done < cutoff) _journal.Delete(entry.JobId);
                    continue;
                }
                if (entry.FinalReport is null)
                {
                    if (entry.StartedAt is null)
                    {
                        _journal.Delete(entry.JobId);
                        continue;
                    }
                    entry.FinalReport = Interrupted(null, entry);
                    entry.FinishedAt = DateTimeOffset.UtcNow;
                    _journal.Save(entry);
                    RecordLocally(entry.FinalReport);
                }
                _journal.DeleteFiles(entry.JobId);
                await DeliverFinalAsync(entry, ct).ConfigureAwait(false);
            }
        }

        // ================================================================== download

        private async Task<string> DownloadAsync(JobLease lease, CancellationToken ct)
        {
            string final = _journal.SpoolPath(lease.JobId);
            string partial = _journal.PartialPath(lease.JobId);
            if (File.Exists(final))
            {
                if (HashMatches(final, lease)) return final;
                File.Delete(final);
            }

            Exception? last = null;
            for (int attempt = 1; attempt <= _options.DownloadAttempts; attempt++)
            {
                long have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                if (have > lease.PayloadBytes)
                {
                    File.Delete(partial);
                    have = 0;
                }
                try
                {
                    if (have < lease.PayloadBytes)
                        await FetchAsync(lease, partial, have, ct).ConfigureAwait(false);

                    long length = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                    if (length != lease.PayloadBytes)
                        throw new IOException("Downloaded " + length + " of " + lease.PayloadBytes + " bytes.");
                    if (!HashMatches(partial, lease))
                    {
                        File.Delete(partial);
                        throw new InvalidDataException("The payload's SHA-256 does not match the hub's.");
                    }
                    File.Move(partial, final, overwrite: true);
                    return final;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException or OperationCanceledException
                                           || (ex is RemotePrintException r && r.StatusCode >= 500))
                {
                    last = ex;
                    Log("Download of job " + lease.JobId + " interrupted (" + Describe(ex) + "), attempt " + attempt + " of " + _options.DownloadAttempts + ".");
                    if (attempt < _options.DownloadAttempts)
                        await Task.Delay(TimeSpan.FromTicks(Math.Min(_options.InitialBackoff.Ticks * attempt, _options.MaxBackoff.Ticks)), ct).ConfigureAwait(false);
                }
            }
            throw last is IOException or InvalidDataException ? last : new IOException(last?.Message ?? "Download failed.", last);
        }

        private async Task FetchAsync(JobLease lease, string partial, long have, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_options.RequestTimeout);
            using HttpRequestMessage request = NewRequest(HttpMethod.Get, "jobs/" + lease.JobId.ToString("D") + "/payload");
            if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            TakeRotatedToken(response);

            FileMode mode;
            if (response.StatusCode == HttpStatusCode.PartialContent && have > 0)
            {
                long? from = response.Content.Headers.ContentRange?.From;
                if (from != have) throw new IOException("The hub resumed at byte " + from + " instead of " + have + ".");
                mode = FileMode.Append;
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                mode = FileMode.Create;
            }
            else if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                File.Delete(partial);
                throw new IOException("The hub rejected the resume range; starting over.");
            }
            else
            {
                await EnsureAsync(response, ct, HttpStatusCode.OK).ConfigureAwait(false);
                return;
            }

            using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var file = new FileStream(partial, mode, FileAccess.Write, FileShare.None, 81920);
            await body.CopyToAsync(file, 81920, timeout.Token).ConfigureAwait(false);
            await file.FlushAsync(timeout.Token).ConfigureAwait(false);
        }

        private static bool HashMatches(string path, JobLease lease)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            string hash = Convert.ToHexStringLower(SHA256.HashData(fs));
            return string.Equals(hash, lease.PayloadSha256, StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================== HTTP plumbing

        private HttpRequestMessage NewRequest(HttpMethod method, string relative)
        {
            string token = _options.Credentials!.GetToken()
                ?? throw new InvalidOperationException("The agent has no token; install one in the credential store.");
            var request = new HttpRequestMessage(method, new Uri(_hub, relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation(RemoteProtocol.AgentHeader, _options.AgentId);
            request.Headers.TryAddWithoutValidation(RemoteProtocol.ProtocolHeader, RemoteProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return request;
        }

        private async Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string relative, T body, TimeSpan timeout, CancellationToken ct)
        {
            using HttpRequestMessage request = NewRequest(method, relative);
            request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, RemoteProtocol.Json));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(RemoteProtocol.JsonContentType);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
            TakeRotatedToken(response);
            return response;
        }

        private void TakeRotatedToken(HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.Unauthorized &&
                response.Headers.TryGetValues(RemoteProtocol.NewTokenHeader, out IEnumerable<string>? values))
            {
                string? token = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(token) && token != _options.Credentials!.GetToken())
                {
                    _options.Credentials.SetToken(token.Trim());
                    Log("The hub rotated this agent's token; the new token is saved.");
                }
            }
        }

        private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct, HttpStatusCode expected)
        {
            if (response.StatusCode == expected) return;
            string message = "HTTP " + (int)response.StatusCode;
            try
            {
                byte[] body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                ErrorResponse? error = body.Length == 0 ? null : JsonSerializer.Deserialize<ErrorResponse>(body, RemoteProtocol.Json);
                if (!string.IsNullOrEmpty(error?.Error)) message += ": " + error.Error;
            }
            catch (JsonException) { }
            throw new RemotePrintException((int)response.StatusCode, message);
        }

        private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
        {
            byte[] body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(body, RemoteProtocol.Json)
                ?? throw new RemotePrintException((int)response.StatusCode, "The hub sent an empty " + typeof(T).Name + ".");
        }

        private static bool IsTransient(Exception ex) =>
            ex is HttpRequestException or IOException or RemotePrintException or JsonException or TimeoutException
            || ex is OperationCanceledException; // request timeouts (the caller's own cancellation is filtered first)

        private static string Describe(Exception ex) => ex is RemotePrintException r ? r.Message : ex.GetType().Name + ": " + ex.Message;

        private static HubJobState ToHubState(PrintJobStatus status) => status switch
        {
            PrintJobStatus.Printed => HubJobState.Printed,
            PrintJobStatus.Cancelled => HubJobState.Cancelled,
            _ => HubJobState.Failed
        };

        private void Log(string line) => _options.Log?.Invoke(line);

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PrintAgent));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }
    }
}
