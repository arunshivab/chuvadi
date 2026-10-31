using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Print.Remote
{
    /// <summary>Settings for <see cref="PrintHub"/>.</summary>
    public sealed class PrintHubOptions
    {
        /// <summary>How long an agent holds a job without reporting before it goes back to the queue. Default 120 s.</summary>
        public int LeaseSeconds { get; set; } = 120;

        /// <summary>Longest a poll waits for a job before answering 204 (long poll). Default 25 s; 0 answers at once.</summary>
        public int PollWaitSeconds { get; set; } = 25;

        /// <summary>
        /// How many leases a job may use. When the last one expires the job fails ("lease expired") and is
        /// audited. Default 3.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>Delete a job's payload as soon as the job is final. Default true.</summary>
        public bool DeletePayloadOnCompletion { get; set; } = true;

        /// <summary>Remove final jobs from the store this long after they finished; null (default) keeps them.</summary>
        public TimeSpan? RemoveFinishedJobsAfter { get; set; }

        /// <summary>How often the hub expires leases on its own (also done on every poll). Zero disables the timer. Default 15 s.</summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Largest spool envelope <see cref="PrintHub.Submit(string, string, SpoolEnvelope)"/> accepts. Default 1 GiB.</summary>
        public long MaxPayloadBytes { get; set; } = 1L << 30;

        /// <summary>Clock for leases and timestamps. Default <see cref="TimeProvider.System"/>.</summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        internal void Validate()
        {
            if (LeaseSeconds < 5) throw new ArgumentOutOfRangeException(nameof(LeaseSeconds), "LeaseSeconds must be at least 5.");
            if (PollWaitSeconds < 0 || PollWaitSeconds > 300) throw new ArgumentOutOfRangeException(nameof(PollWaitSeconds), "PollWaitSeconds must be 0..300.");
            if (MaxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(MaxAttempts), "MaxAttempts must be at least 1.");
            if (SweepInterval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(SweepInterval));
            if (MaxPayloadBytes < 1) throw new ArgumentOutOfRangeException(nameof(MaxPayloadBytes));
            if (TimeProvider is null) throw new ArgumentNullException(nameof(TimeProvider));
        }
    }

    /// <summary>
    /// The hub side of remote printing, embedded in a host application (LiPi Connect is the reference host).
    /// Print agents on other machines dial out to it over HTTPS (P07: the hub never connects to an agent),
    /// authenticate with a per-agent token, long-poll for jobs, download each job's spool envelope and report
    /// progress and the outcome. The host submits jobs, watches them, cancels them and reads the audit trail.
    /// </summary>
    /// <remarks>
    /// <para>Host-agnostic: the host's web stack forwards each request to <see cref="HandleAsync"/> as a
    /// <see cref="HubRequest"/> and writes the <see cref="HubResponse"/> back. Mount it under any base path
    /// and serve it over HTTPS only.</para>
    /// <para>Exactly one audit event per job (P06): the agent's own event (from <c>PrintTo</c>/<c>SubmitTo</c>)
    /// arrives with the final status and is recorded once into the host's sink; a job that ends on the hub
    /// (cancelled while queued, leases exhausted) gets one event from the hub. Further final reports for the
    /// same job are acknowledged and ignored.</para>
    /// <para>Idempotent: the job id is the envelope's (or the <see cref="PrintJob"/>'s) JobId; submitting the
    /// same id again returns the existing job. An expired lease puts the job back in the queue; the agent's
    /// journal makes sure a job it already started is not printed twice.</para>
    /// <para>One hub instance per store. Thread-safe.</para>
    /// </remarks>
    public sealed class PrintHub : IDisposable
    {
        private readonly object _gate = new();
        private readonly IPrintJobStore _store;
        private readonly IAgentAuthenticator _auth;
        private readonly IPrintAuditSink _audit;
        private readonly PrintHubOptions _options;
        private readonly TimeProvider _time;
        private readonly Dictionary<string, AgentInfo> _agents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource<bool>> _signals = new(StringComparer.Ordinal);
        private readonly Timer? _timer;
        private bool _disposed;

        public PrintHub(IPrintJobStore store, IAgentAuthenticator authenticator, IPrintAuditSink auditSink, PrintHubOptions? options = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _auth = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
            _audit = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
            _options = options ?? new PrintHubOptions();
            _options.Validate();
            _time = _options.TimeProvider;
            if (_options.SweepInterval > TimeSpan.Zero)
                _timer = new Timer(_ => TimerSweep(), null, _options.SweepInterval, _options.SweepInterval);
        }

        /// <summary>Raised once per job when it reaches a final state, after its audit event was recorded.</summary>
        public event EventHandler<HubJobFinishedEventArgs>? JobFinished;

        /// <summary>Raised when the background sweep (lease expiry) throws, e.g. the store or audit sink failed.</summary>
        public event EventHandler<Exception>? BackgroundError;

        // ================================================================== host API

        /// <summary>Queues a job for <paramref name="agentId"/>'s local printer <paramref name="printer"/>.</summary>
        public StoredJob Submit(string agentId, string printer, PrintJob job)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            return Submit(agentId, printer, job.ToEnvelope());
        }

        /// <summary>
        /// Queues a spool envelope for <paramref name="agentId"/>'s local printer <paramref name="printer"/> and
        /// wakes the agent if it is polling. The job id is the envelope's JobId (a new one when it has none).
        /// Idempotent: when the id is already known, nothing changes and the existing job is returned (whatever
        /// its agent and state).
        /// </summary>
        public StoredJob Submit(string agentId, string printer, SpoolEnvelope envelope)
        {
            TokenAgentAuthenticator.ValidateAgentId(agentId);
            if (string.IsNullOrWhiteSpace(printer)) throw new ArgumentException("A printer name is required.", nameof(printer));
            if (envelope is null) throw new ArgumentNullException(nameof(envelope));
            ThrowIfDisposed();

            Guid id = envelope.JobId is Guid g && g != Guid.Empty ? g : Guid.NewGuid();
            var normalized = new SpoolEnvelope(envelope.PdfBytes, envelope.Settings)
            {
                JobId = id,
                DocumentTitle = envelope.DocumentTitle,
                UserName = envelope.UserName,
                TenantId = envelope.TenantId
            };
            byte[] payload = normalized.ToArray();
            if (payload.LongLength > _options.MaxPayloadBytes)
                throw new ArgumentException("The job is " + payload.LongLength + " bytes; the hub accepts at most " + _options.MaxPayloadBytes + ".", nameof(envelope));

            var job = new StoredJob
            {
                JobId = id,
                AgentId = agentId,
                Printer = printer,
                State = HubJobState.Queued,
                SubmittedAt = _time.GetUtcNow(),
                Title = envelope.DocumentTitle,
                UserName = envelope.UserName,
                TenantId = envelope.TenantId,
                Copies = envelope.Settings.Copies,
                PayloadBytes = payload.LongLength,
                PayloadSha256 = Convert.ToHexStringLower(SHA256.HashData(payload))
            };

            StoredJob? existing;
            lock (_gate) existing = _store.Add(job, payload);
            if (existing is not null) return existing;
            Signal(agentId);
            return job.Clone();
        }

        /// <summary>The job, or null.</summary>
        public StoredJob? GetJob(Guid jobId) => _store.Get(jobId);

        /// <summary>All jobs (optionally for one agent), oldest first.</summary>
        public IReadOnlyList<StoredJob> ListJobs(string? agentId = null)
        {
            IReadOnlyList<StoredJob> all = _store.List();
            return agentId is null ? all : all.Where(j => string.Equals(j.AgentId, agentId, StringComparison.Ordinal)).ToList();
        }

        /// <summary>Agents that have contacted this hub since it started (most recent first).</summary>
        public IReadOnlyList<AgentInfo> ListAgents()
        {
            lock (_gate)
                return _agents.Values.Select(CopyAgent).OrderByDescending(a => a.LastSeen).ToList();
        }

        /// <summary>
        /// Cancels a job. A queued job is cancelled at once (and audited). A job an agent holds is flagged; the
        /// agent learns it on its next report (within a third of the lease), stops and reports Cancelled; if it
        /// never reports, the job is cancelled when its lease expires. Returns false when the job is unknown or
        /// already final.
        /// </summary>
        public bool Cancel(Guid jobId, string? reason = null)
        {
            var finished = new List<HubJobFinishedEventArgs>();
            lock (_gate)
            {
                StoredJob? job = _store.Get(jobId);
                if (job is null || job.IsFinal) return false;
                if (job.State == HubJobState.Queued)
                {
                    FinishLocked(job, HubJobState.Cancelled, reason ?? "Cancelled by the host before the agent took it.", null, finished);
                }
                else
                {
                    job.CancelRequested = true;
                    _store.Update(job);
                }
            }
            Publish(finished);
            return true;
        }

        /// <summary>
        /// Expires overdue leases now (requeue, or fail after <see cref="PrintHubOptions.MaxAttempts"/>, or
        /// cancel when cancellation was requested) and removes old final jobs. Runs on its own every
        /// <see cref="PrintHubOptions.SweepInterval"/> and on every poll. Returns the number of jobs changed.
        /// </summary>
        public int ExpireLeases()
        {
            ThrowIfDisposed();
            var finished = new List<HubJobFinishedEventArgs>();
            int changed;
            lock (_gate) changed = SweepLocked(finished);
            Publish(finished);
            return changed;
        }

        // ================================================================== agent protocol

        /// <summary>
        /// Handles one agent request. Routes (below the hub's base path): <c>POST agents/hello</c>,
        /// <c>POST agents/poll</c>, <c>GET jobs/{id}/payload</c> (supports <c>Range: bytes=N-</c>),
        /// <c>POST jobs/{id}/status</c>. Every request carries <c>X-Chuvadi-Protocol: 1</c>,
        /// <c>X-Chuvadi-Agent</c> and <c>Authorization: Bearer &lt;token&gt;</c>.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Cancels a waiting poll (client gone, host stopping).</param>
        public async Task<HubResponse> HandleAsync(HubRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            ThrowIfDisposed();

            if (!request.Headers.TryGetValue(RemoteProtocol.ProtocolHeader, out string? version) ||
                version.Trim() != RemoteProtocol.Version.ToString(CultureInfo.InvariantCulture))
                return HubResponse.Error(400, "Unsupported protocol version '" + version + "'; this hub speaks " + RemoteProtocol.Version + ".");

            request.Headers.TryGetValue(RemoteProtocol.AgentHeader, out string? agentId);
            request.Headers.TryGetValue("Authorization", out string? authorization);
            string? token = null;
            if (authorization is not null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = authorization.Substring(7).Trim();
            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(token))
                return HubResponse.Error(401, "Agent id and bearer token are required.");
            AgentAuthResult auth = _auth.Authenticate(agentId, token);
            if (!auth.Succeeded) return HubResponse.Error(401, "Unknown agent or invalid token.");

            Touch(agentId);
            HubResponse response;
            try
            {
                response = await RouteAsync(request, agentId, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                response = HubResponse.Error(400, "Malformed JSON: " + ex.Message);
            }
            if (auth.NewToken is not null) response.Headers[RemoteProtocol.NewTokenHeader] = auth.NewToken;
            return response;
        }

        private async Task<HubResponse> RouteAsync(HubRequest request, string agentId, CancellationToken ct)
        {
            string path = request.Path;
            int query = path.IndexOf('?');
            if (query >= 0) path = path.Substring(0, query);
            string[] parts = path.Trim('/').Split('/');
            string method = request.Method.ToUpperInvariant();

            if (parts.Length == 2 && parts[0] == "agents")
            {
                if (parts[1] == "hello") return method == "POST" ? Hello(request, agentId) : MethodNotAllowed();
                if (parts[1] == "poll") return method == "POST" ? await PollAsync(request, agentId, ct).ConfigureAwait(false) : MethodNotAllowed();
            }
            else if (parts.Length == 3 && parts[0] == "jobs" && Guid.TryParse(parts[1], out Guid jobId))
            {
                if (parts[2] == "payload") return method == "GET" ? Payload(request, agentId, jobId) : MethodNotAllowed();
                if (parts[2] == "status") return method == "POST" ? Status(request, agentId, jobId) : MethodNotAllowed();
            }
            return HubResponse.Error(404, "No such endpoint: " + request.Method + " " + request.Path);
        }

        private static HubResponse MethodNotAllowed() => HubResponse.Error(405, "Method not allowed.");

        private HubResponse Hello(HubRequest request, string agentId)
        {
            HelloRequest hello = Read<HelloRequest>(request) ?? new HelloRequest();
            lock (_gate)
            {
                AgentInfo info = _agents[agentId];
                info.MachineName = hello.MachineName;
                info.AgentVersion = hello.AgentVersion;
                info.Printers = (hello.Printers ?? new List<string>()).ToArray();
            }
            return HubResponse.Json(200, new HelloResponse
            {
                PollWaitSeconds = _options.PollWaitSeconds,
                LeaseSeconds = _options.LeaseSeconds,
                ServerTime = _time.GetUtcNow()
            });
        }

        private async Task<HubResponse> PollAsync(HubRequest request, string agentId, CancellationToken ct)
        {
            PollRequest poll = Read<PollRequest>(request) ?? new PollRequest();
            var wait = TimeSpan.FromSeconds(Math.Clamp(poll.WaitSeconds, 0, _options.PollWaitSeconds));
            var clock = Stopwatch.StartNew();
            while (true)
            {
                var finished = new List<HubJobFinishedEventArgs>();
                JobLease? lease;
                Task signal;
                lock (_gate)
                {
                    SweepLocked(finished);
                    lease = TryLeaseLocked(agentId, finished);
                    signal = SignalFor(agentId).Task;
                }
                Publish(finished);
                if (lease is not null) return HubResponse.Json(200, lease);

                TimeSpan remaining = wait - clock.Elapsed;
                if (remaining <= TimeSpan.Zero) return new HubResponse(204);
                try
                {
                    await signal.WaitAsync(remaining, ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // One last look, then answer 204.
                    wait = TimeSpan.Zero;
                }
            }
        }

        private HubResponse Payload(HubRequest request, string agentId, Guid jobId)
        {
            StoredJob? job = _store.Get(jobId);
            if (job is null || !string.Equals(job.AgentId, agentId, StringComparison.Ordinal))
                return HubResponse.Error(404, "No such job for this agent.");
            if (job.IsFinal) return HubResponse.Error(410, "The job is " + job.State + ".");
            if (job.State == HubJobState.Queued) return HubResponse.Error(409, "The job is not leased to this agent.");

            Stream? stream = _store.OpenPayload(jobId);
            if (stream is null) return HubResponse.Error(410, "The job's payload is gone.");
            long length = stream.Length;

            long start = 0;
            if (request.Headers.TryGetValue("Range", out string? range) && !string.IsNullOrWhiteSpace(range))
            {
                if (!TryParseRange(range, length, out start, out long end))
                {
                    stream.Dispose();
                    var bad = HubResponse.Error(416, "Unsatisfiable range '" + range + "'.");
                    bad.Headers["Content-Range"] = "bytes */" + length.ToString(CultureInfo.InvariantCulture);
                    return bad;
                }
                stream.Position = start;
                HubResponse partial = HubResponse.Stream(206, stream, end - start + 1, RemoteProtocol.PayloadContentType);
                partial.Headers["Content-Range"] = "bytes " + start.ToString(CultureInfo.InvariantCulture) + "-" +
                    end.ToString(CultureInfo.InvariantCulture) + "/" + length.ToString(CultureInfo.InvariantCulture);
                partial.Headers["Accept-Ranges"] = "bytes";
                return partial;
            }

            HubResponse full = HubResponse.Stream(200, stream, length, RemoteProtocol.PayloadContentType);
            full.Headers["Accept-Ranges"] = "bytes";
            return full;
        }

        private HubResponse Status(HubRequest request, string agentId, Guid jobId)
        {
            StatusReport? report = Read<StatusReport>(request);
            if (report is null) return HubResponse.Error(400, "A status report is required.");
            bool reportIsFinal = report.State is HubJobState.Printed or HubJobState.Failed or HubJobState.Cancelled;
            if (!reportIsFinal && report.State is not (HubJobState.Accepted or HubJobState.Printing))
                return HubResponse.Error(400, "An agent reports Accepted, Printing, Printed, Failed or Cancelled.");

            var finished = new List<HubJobFinishedEventArgs>();
            HubResponse response;
            lock (_gate)
            {
                StoredJob? job = _store.Get(jobId);
                if (job is null || !string.Equals(job.AgentId, agentId, StringComparison.Ordinal))
                    return HubResponse.Error(404, "No such job for this agent.");

                if (job.IsFinal)
                {
                    response = HubResponse.Json(200, new StatusResponse { Final = true, CancelRequested = job.State == HubJobState.Cancelled });
                }
                else if (reportIsFinal)
                {
                    // The owning agent's outcome is the truth, even when its lease has lapsed meanwhile.
                    job.PagesPrinted = report.PagesPrinted;
                    job.ImpressionsPrinted = report.ImpressionsPrinted;
                    job.SheetsPrinted = report.SheetsPrinted;
                    FinishLocked(job, report.State, report.Error, report.Audit, finished);
                    response = HubResponse.Json(200, new StatusResponse { Final = true });
                }
                else if (job.LeaseId != report.LeaseId || job.State == HubJobState.Queued)
                {
                    return HubResponse.Error(409, "The lease is no longer held; the job went back to the queue.");
                }
                else
                {
                    job.State = report.State;
                    job.PagesPrinted = report.PagesPrinted;
                    job.ImpressionsPrinted = report.ImpressionsPrinted;
                    job.SheetsPrinted = report.SheetsPrinted;
                    job.LeaseExpiresAt = _time.GetUtcNow().AddSeconds(_options.LeaseSeconds);
                    _store.Update(job);
                    response = HubResponse.Json(200, new StatusResponse
                    {
                        CancelRequested = job.CancelRequested,
                        LeaseSeconds = _options.LeaseSeconds
                    });
                }
            }
            Publish(finished);
            return response;
        }

        // ================================================================== internals (call with _gate held)

        private JobLease? TryLeaseLocked(string agentId, List<HubJobFinishedEventArgs> finished)
        {
            foreach (StoredJob job in _store.List())
            {
                if (job.State != HubJobState.Queued || !string.Equals(job.AgentId, agentId, StringComparison.Ordinal)) continue;
                if (job.CancelRequested)
                {
                    FinishLocked(job, HubJobState.Cancelled, "Cancelled by the host.", null, finished);
                    continue;
                }
                job.State = HubJobState.Leased;
                job.LeaseId = Guid.NewGuid();
                job.LeaseExpiresAt = _time.GetUtcNow().AddSeconds(_options.LeaseSeconds);
                job.Attempts++;
                _store.Update(job);
                return new JobLease
                {
                    JobId = job.JobId,
                    LeaseId = job.LeaseId.Value,
                    LeaseSeconds = _options.LeaseSeconds,
                    Printer = job.Printer,
                    PayloadBytes = job.PayloadBytes,
                    PayloadSha256 = job.PayloadSha256,
                    Title = job.Title,
                    Attempt = job.Attempts
                };
            }
            return null;
        }

        private int SweepLocked(List<HubJobFinishedEventArgs> finished)
        {
            DateTimeOffset now = _time.GetUtcNow();
            int changed = 0;
            foreach (StoredJob job in _store.List())
            {
                if (job.IsFinal)
                {
                    if (_options.RemoveFinishedJobsAfter is TimeSpan keep && job.CompletedAt is DateTimeOffset done && done + keep <= now)
                    {
                        _store.Remove(job.JobId);
                        changed++;
                    }
                    continue;
                }
                if (job.State == HubJobState.Queued || job.LeaseExpiresAt is not DateTimeOffset expires || expires > now) continue;

                changed++;
                if (job.CancelRequested)
                {
                    FinishLocked(job, HubJobState.Cancelled, "Cancelled by the host; the agent did not confirm before its lease expired.", null, finished);
                }
                else if (job.Attempts >= _options.MaxAttempts)
                {
                    FinishLocked(job, HubJobState.Failed,
                        "The agent's lease expired " + job.Attempts + " time(s) without a final report.", null, finished);
                }
                else
                {
                    job.State = HubJobState.Queued;
                    job.LeaseId = null;
                    job.LeaseExpiresAt = null;
                    _store.Update(job);
                    SignalLocked(job.AgentId);
                }
            }
            return changed;
        }

        private void FinishLocked(StoredJob job, HubJobState state, string? error, AuditRecord? agentAudit,
                                  List<HubJobFinishedEventArgs> finished)
        {
            DateTimeOffset now = _time.GetUtcNow();
            job.State = state;
            job.Error = error;
            job.CompletedAt = now;
            job.LeaseId = null;
            job.LeaseExpiresAt = null;

            PrintAuditEvent? audit = null;
            if (!job.Audited)
            {
                if (agentAudit is not null)
                {
                    agentAudit.JobId = job.JobId;
                    audit = agentAudit.ToEvent();
                }
                else
                {
                    _agents.TryGetValue(job.AgentId, out AgentInfo? agent);
                    audit = new PrintAuditEvent
                    {
                        JobId = job.JobId,
                        DocumentTitle = job.Title,
                        PageCount = job.PagesPrinted,
                        Copies = job.Copies,
                        PrinterName = job.Printer,
                        MachineName = agent?.MachineName ?? job.AgentId,
                        UserName = job.UserName,
                        Status = state switch
                        {
                            HubJobState.Printed => PrintJobStatus.Printed,
                            HubJobState.Cancelled => PrintJobStatus.Cancelled,
                            _ => PrintJobStatus.Failed
                        },
                        StartedAt = job.SubmittedAt,
                        CompletedAt = now,
                        TenantId = job.TenantId,
                        Error = state == HubJobState.Failed ? error : null
                    };
                }
                job.Audited = true;
            }
            _store.Update(job);
            if (_options.DeletePayloadOnCompletion) _store.DeletePayload(job.JobId);
            if (audit is not null) finished.Add(new HubJobFinishedEventArgs(job.Clone(), audit));
        }

        private void Publish(List<HubJobFinishedEventArgs> finished)
        {
            foreach (HubJobFinishedEventArgs e in finished)
            {
                _audit.Record(e.Audit);
                JobFinished?.Invoke(this, e);
            }
        }

        private void Touch(string agentId)
        {
            lock (_gate)
            {
                DateTimeOffset now = _time.GetUtcNow();
                if (!_agents.TryGetValue(agentId, out AgentInfo? info))
                {
                    info = new AgentInfo { AgentId = agentId, FirstSeen = now };
                    _agents[agentId] = info;
                }
                info.LastSeen = now;
            }
        }

        private TaskCompletionSource<bool> SignalFor(string agentId)
        {
            if (!_signals.TryGetValue(agentId, out TaskCompletionSource<bool>? tcs))
            {
                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _signals[agentId] = tcs;
            }
            return tcs;
        }

        private void Signal(string agentId)
        {
            lock (_gate) SignalLocked(agentId);
        }

        private void SignalLocked(string agentId)
        {
            if (_signals.Remove(agentId, out TaskCompletionSource<bool>? tcs)) tcs.TrySetResult(true);
        }

        private void TimerSweep()
        {
            if (_disposed) return;
            try { ExpireLeases(); }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { BackgroundError?.Invoke(this, ex); }
        }

        private static T? Read<T>(HubRequest request) where T : class
        {
            if (request.Body is null || request.Body.Length == 0) return null;
            return JsonSerializer.Deserialize<T>(request.Body, RemoteProtocol.Json);
        }

        internal static bool TryParseRange(string header, long length, out long start, out long end)
        {
            start = 0;
            end = length - 1;
            string h = header.Trim();
            if (!h.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || h.Contains(',')) return false;
            string spec = h.Substring(6).Trim();
            int dash = spec.IndexOf('-');
            if (dash < 0) return false;
            string first = spec.Substring(0, dash).Trim();
            string last = spec.Substring(dash + 1).Trim();
            if (first.Length == 0)
            {
                // Suffix range: the last N bytes.
                if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out long suffix) || suffix <= 0 || length == 0) return false;
                start = Math.Max(0, length - suffix);
                return true;
            }
            if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out start) || start >= length) return false;
            if (last.Length > 0)
            {
                if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out long e) || e < start) return false;
                end = Math.Min(e, length - 1);
            }
            return true;
        }

        private static AgentInfo CopyAgent(AgentInfo a) => new()
        {
            AgentId = a.AgentId,
            MachineName = a.MachineName,
            AgentVersion = a.AgentVersion,
            Printers = a.Printers.ToArray(),
            LastSeen = a.LastSeen,
            FirstSeen = a.FirstSeen
        };

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PrintHub));
        }

        /// <summary>Stops the background sweep. The store is not disposed (the host owns it).</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            lock (_gate)
            {
                foreach (TaskCompletionSource<bool> tcs in _signals.Values) tcs.TrySetResult(false);
                _signals.Clear();
            }
        }
    }
}
