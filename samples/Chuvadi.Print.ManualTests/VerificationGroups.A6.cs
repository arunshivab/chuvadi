using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Print.Remote;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// A6 verification groups: the remote print hub and agent end to end (real HTTP and in process), the
    /// one-audit-event rule, idempotency, resume, lease expiry, the never-print-twice journal, cancellation,
    /// token rotation and revocation, the durable file store and protocol checks.
    /// </summary>
    public static partial class VerificationGroups
    {
        private static Action[] A6Groups() => new Action[]
        {
            RemoteJobPrintsOverHttpAndAuditsOnce,
            RemoteSubmitIsIdempotent,
            RemoteDownloadResumesAfterCut,
            RemoteLeaseExpiryRequeuesThenFails,
            RemoteInterruptedJobIsNotReprinted,
            RemoteCancelQueuedAndInFlight,
            RemoteTokenRotatesAndRevokes,
            RemoteFileStoreSurvivesRestart,
            RemoteProtocolRejectsBadRequests,
            RemoteAgentDemandsHttps,
            RemoteDocumentTargetPassesPdfThrough,
            RemoteUnknownPrinterFailsOnce
        };

        public static void RemoteJobPrintsOverHttpAndAuditsOnce()
        {
            using var rig = new RemoteRig(overHttp: true);
            StoredJob queued = rig.Submit(pages: 2, copies: 2);
            Require(queued.State == HubJobState.Queued, "queued");
            HubJobFinishedEventArgs? finished = null;
            rig.Hub.JobFinished += (_, e) => finished = e;

            PrintAgent agent = rig.NewAgent();
            Require(agent.RunOnceAsync().GetAwaiter().GetResult(), "the agent took a job");

            StoredJob job = rig.Hub.GetJob(queued.JobId)!;
            Require(job.State == HubJobState.Printed, "printed: " + job.Error);
            Require(job.PagesPrinted == 2 && job.ImpressionsPrinted == 4, "2 pages x 2 copies");
            Require(rig.Target.Pages.Count == 4, "four impressions reached the printer");
            Require(rig.HubAudit.Events.Count == 1, "exactly one hub audit event");
            PrintAuditEvent audit = rig.HubAudit.Events[0];
            Require(audit.JobId == queued.JobId && audit.Status == PrintJobStatus.Printed, "audit is the job's");
            Require(audit.PrinterName == RemoteRig.PrinterName && audit.MachineName == Environment.MachineName, "the agent's own PrintTo event");
            Require(audit.UserName == "arun" && audit.TenantId == "lipi" && audit.DocumentTitle == "Invoice 42", "metadata travels");
            Require(rig.AgentAudit.Events.Count == 1 && rig.AgentAudit.Events[0].JobId == queued.JobId, "one local agent event");
            Require(finished is not null && finished.Job.JobId == queued.JobId, "JobFinished raised");
            Require(rig.Store.OpenPayload(queued.JobId) is null, "payload deleted when final");
            Require(rig.Hub.ListAgents().Single().MachineName == "BRANCH-PC", "hello registered the agent");
            Require(rig.Hub.ListAgents().Single().Printers.SequenceEqual(new[] { RemoteRig.PrinterName }), "printers announced");

            var lines = rig.Http!.Requests;
            Require(lines.Any(l => l.StartsWith("POST agents/hello")) && lines.Any(l => l.StartsWith("POST agents/poll")), "hello and poll over HTTP");
            Require(lines.Count(l => l.Contains("/status")) == 2, "Accepted + final status");
            Require(!agent.RunOnceAsync().GetAwaiter().GetResult(), "nothing more to do");
        }

        public static void RemoteSubmitIsIdempotent()
        {
            using var rig = new RemoteRig();
            Guid id = Guid.NewGuid();
            StoredJob first = rig.Submit(jobId: id);
            StoredJob again = rig.Submit(pages: 9, jobId: id);
            Require(first.JobId == id && again.JobId == id, "same id");
            Require(rig.Hub.ListJobs().Count == 1, "one job stored");
            Require(again.PayloadSha256 == first.PayloadSha256, "the original payload is kept");

            rig.NewAgent().RunOnceAsync().GetAwaiter().GetResult();
            StoredJob afterPrint = rig.Submit(jobId: id);
            Require(afterPrint.State == HubJobState.Printed, "resubmitting a printed job returns it, it is not requeued");
            Require(!rig.NewAgent().RunOnceAsync().GetAwaiter().GetResult(), "nothing queued");
            Require(rig.Target.Pages.Count == 2 && rig.HubAudit.Events.Count == 1, "printed and audited once");
        }

        public static void RemoteDownloadResumesAfterCut()
        {
            using var rig = new RemoteRig(overHttp: true);
            StoredJob queued = rig.Submit(pages: 40);
            Require(queued.PayloadBytes > 300, "payload large enough to cut: " + queued.PayloadBytes);
            rig.Http!.CutNextPayloadAfter = 200;

            rig.NewAgent().RunOnceAsync().GetAwaiter().GetResult();
            var payloadRequests = rig.Http.Requests.Where(l => l.Contains("/payload")).ToList();
            Require(payloadRequests.Count == 2, "two payload requests: " + string.Join(" | ", payloadRequests));
            Require(!payloadRequests[0].Contains("Range="), "first download from the start");
            Require(payloadRequests[1].EndsWith("Range=bytes=200-"), "second resumes at byte 200: " + payloadRequests[1]);
            StoredJob job = rig.Hub.GetJob(queued.JobId)!;
            Require(job.State == HubJobState.Printed && rig.Target.Pages.Count == 40, "printed in full: " + job.Error);
            Require(rig.HubAudit.Events.Count == 1, "one audit event");
        }

        public static void RemoteLeaseExpiryRequeuesThenFails()
        {
            using var rig = new RemoteRig(configure: o => { o.LeaseSeconds = 10; o.MaxAttempts = 2; o.PollWaitSeconds = 0; });
            StoredJob queued = rig.Submit();

            using HubResponse first = rig.Raw("POST", "agents/poll", new PollRequest { WaitSeconds = 0 });
            Require(first.StatusCode == 200, "leased");
            JobLease lease1 = RemoteRig.ReadJson<JobLease>(first);
            Require(lease1.JobId == queued.JobId && lease1.Attempt == 1 && lease1.LeaseSeconds == 10, "lease 1");

            using HubResponse accepted = rig.Raw("POST", "jobs/" + queued.JobId + "/status", new StatusReport { LeaseId = lease1.LeaseId, State = HubJobState.Accepted });
            Require(accepted.StatusCode == 200, "accepted");
            rig.Clock.Advance(TimeSpan.FromSeconds(8));
            using HubResponse beat = rig.Raw("POST", "jobs/" + queued.JobId + "/status", new StatusReport { LeaseId = lease1.LeaseId, State = HubJobState.Printing });
            Require(beat.StatusCode == 200, "heartbeat renews");
            rig.Clock.Advance(TimeSpan.FromSeconds(8));
            Require(rig.Hub.ExpireLeases() == 0, "renewed lease has not expired yet");

            rig.Clock.Advance(TimeSpan.FromSeconds(3));
            Require(rig.Hub.ExpireLeases() == 1, "lease expired");
            StoredJob requeued = rig.Hub.GetJob(queued.JobId)!;
            Require(requeued.State == HubJobState.Queued && requeued.Attempts == 1, "back in the queue");
            using HubResponse stale = rig.Raw("POST", "jobs/" + queued.JobId + "/status", new StatusReport { LeaseId = lease1.LeaseId, State = HubJobState.Printing });
            Require(stale.StatusCode == 409, "a lapsed lease cannot be renewed");

            using HubResponse second = rig.Raw("POST", "agents/poll", new PollRequest { WaitSeconds = 0 });
            JobLease lease2 = RemoteRig.ReadJson<JobLease>(second);
            Require(lease2.Attempt == 2 && lease2.LeaseId != lease1.LeaseId, "lease 2");
            rig.Clock.Advance(TimeSpan.FromSeconds(11));
            using HubResponse none = rig.Raw("POST", "agents/poll", new PollRequest { WaitSeconds = 0 });
            Require(none.StatusCode == 204, "the poll swept it: nothing to lease");
            StoredJob failed = rig.Hub.GetJob(queued.JobId)!;
            Require(failed.State == HubJobState.Failed && failed.Error!.Contains("lease expired"), "failed after MaxAttempts: " + failed.Error);
            Require(rig.HubAudit.Events.Count == 1 && rig.HubAudit.Events[0].Status == PrintJobStatus.Failed, "audited once by the hub");

            using HubResponse late = rig.Raw("POST", "jobs/" + queued.JobId + "/status", new StatusReport { LeaseId = lease2.LeaseId, State = HubJobState.Printed });
            Require(late.StatusCode == 200 && RemoteRig.ReadJson<StatusResponse>(late).Final, "late final report acknowledged");
            Require(rig.HubAudit.Events.Count == 1, "and ignored: still one event");
        }

        public static void RemoteInterruptedJobIsNotReprinted()
        {
            using var rig = new RemoteRig();
            StoredJob queued = rig.Submit(pages: 3);

            // The first agent "crashes" after journalling the job as started (its resolver throws something unexpected).
            PrintAgent crashing = rig.NewAgent(o => o.PrinterResolver = _ => throw new NotSupportedException("simulated crash"));
            bool crashed = false;
            try { crashing.RunOnceAsync().GetAwaiter().GetResult(); }
            catch (NotSupportedException) { crashed = true; }
            Require(crashed, "the first run crashed mid-job");
            Require(rig.Hub.GetJob(queued.JobId)!.State == HubJobState.Accepted, "the hub still thinks the agent has it");

            // Restarted agent, same journal: the job is reported as interrupted, never printed again.
            PrintAgent restarted = rig.NewAgent();
            restarted.RunOnceAsync().GetAwaiter().GetResult();
            StoredJob job = rig.Hub.GetJob(queued.JobId)!;
            Require(job.State == HubJobState.Failed && job.Error!.Contains("not printed again"), "reported interrupted: " + job.Error);
            Require(rig.Target.Pages.Count == 0, "nothing printed twice (or at all)");
            Require(rig.HubAudit.Events.Count == 1 && rig.HubAudit.Events[0].Status == PrintJobStatus.Failed, "one audit event");
            Require(rig.AgentAudit.Events.Count == 1, "one local event");
            Require(!Directory.EnumerateFiles(rig.JournalFolder, "*.cvpr*").Any(), "downloaded payload removed");
        }

        public static void RemoteCancelQueuedAndInFlight()
        {
            using var rig = new RemoteRig(configure: o => o.LeaseSeconds = 5);

            StoredJob early = rig.Submit();
            Require(rig.Hub.Cancel(early.JobId), "cancel a queued job");
            Require(rig.Hub.GetJob(early.JobId)!.State == HubJobState.Cancelled, "cancelled at once");
            Require(!rig.Hub.Cancel(early.JobId), "cancelling twice is a no-op");
            Require(rig.HubAudit.Events.Count == 1 && rig.HubAudit.Events[0].Status == PrintJobStatus.Cancelled, "audited once");

            StoredJob running = rig.Submit(pages: 60);
            rig.Target.AfterImpression = _ => Thread.Sleep(100);
            PrintAgent agent = rig.NewAgent();
            Task<bool> work = agent.RunOnceAsync();
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (rig.Hub.GetJob(running.JobId)!.State is HubJobState.Queued or HubJobState.Leased && DateTime.UtcNow < deadline)
                Thread.Sleep(20);
            Require(rig.Hub.Cancel(running.JobId), "cancel while the agent holds it");
            Require(rig.Hub.GetJob(running.JobId)!.CancelRequested, "flagged for the agent");
            Require(work.Wait(TimeSpan.FromSeconds(30)), "the agent finished");

            StoredJob job = rig.Hub.GetJob(running.JobId)!;
            Require(job.State == HubJobState.Cancelled, "cancelled by the agent: " + job.State + " " + job.Error);
            Require(rig.Target.Pages.Count < 60 && rig.Target.AbortReason is null && rig.Target.Events.Contains("abort"), "printing stopped and the job was aborted");
            Require(rig.HubAudit.Events.Count == 2 && rig.HubAudit.Events[1].Status == PrintJobStatus.Cancelled, "one event per job");
            Require(rig.HubAudit.Events[1].MachineName == Environment.MachineName, "the agent's PrintTo event");
        }

        public static void RemoteTokenRotatesAndRevokes()
        {
            using var rig = new RemoteRig();
            PrintAgent agent = rig.NewAgent();
            agent.RunOnceAsync().GetAwaiter().GetResult();

            string pending = rig.Auth.BeginRotation(RemoteRig.AgentId);
            Require(rig.Auth.IsRotationPending(RemoteRig.AgentId), "rotation pending");
            agent.RunOnceAsync().GetAwaiter().GetResult();
            Require(rig.Credentials.GetToken() == pending, "the agent stored the new token");
            Require(rig.Auth.IsRotationPending(RemoteRig.AgentId), "old token still in use until the agent switches");
            agent.RunOnceAsync().GetAwaiter().GetResult();
            Require(!rig.Auth.IsRotationPending(RemoteRig.AgentId), "the new token replaced the old one on first use");

            using HubResponse old = rig.Raw("POST", "agents/poll", new PollRequest(), token: rig.Token);
            Require(old.StatusCode == 401, "the old token no longer works");
            Require(rig.Auth.ListAgents().Single().TokenHash == TokenAgentAuthenticator.Hash(pending), "only the hash is stored");

            Require(rig.Auth.Revoke(RemoteRig.AgentId), "revoked");
            int status = 0;
            try { agent.RunOnceAsync().GetAwaiter().GetResult(); }
            catch (RemotePrintException ex) { status = ex.StatusCode; }
            Require(status == 401, "a revoked agent is refused");
        }

        public static void RemoteFileStoreSurvivesRestart()
        {
            string folder = Path.Combine(Path.GetTempPath(), "chuvadi-store-" + Guid.NewGuid().ToString("N"));
            try
            {
                Guid id;
                using (var rig = new RemoteRig(store: new FileJobStore(folder)))
                {
                    id = rig.Submit(pages: 2).JobId;
                }

                // Debris of a crash: a half-written record, an orphan payload and an unreadable record.
                File.WriteAllText(Path.Combine(folder, "jobs", "x.json.0.tmp"), "{");
                File.WriteAllText(Path.Combine(folder, "payloads", Guid.NewGuid().ToString("N") + ".cvpr"), "orphan");
                File.WriteAllText(Path.Combine(folder, "jobs", Guid.NewGuid().ToString("N") + ".json"), "not json");

                var reopened = new FileJobStore(folder);
                Require(reopened.Get(id)?.State == HubJobState.Queued, "the queued job survived");
                Require(reopened.CorruptRecords.Count == 1, "an unreadable record is set aside");
                Require(Directory.GetFiles(Path.Combine(folder, "payloads")).Length == 1, "orphan payload removed");
                Require(!Directory.EnumerateFiles(folder, "*.tmp", SearchOption.AllDirectories).Any(), "temporary files removed");

                using (var rig = new RemoteRig(store: reopened))
                {
                    rig.NewAgent().RunOnceAsync().GetAwaiter().GetResult();
                    Require(rig.Hub.GetJob(id)!.State == HubJobState.Printed && rig.Target.Pages.Count == 2, "printed after the restart");
                }
                var third = new FileJobStore(folder);
                StoredJob done = third.Get(id)!;
                Require(done.State == HubJobState.Printed && done.Audited && done.CompletedAt is not null, "final state is durable");
                Require(third.OpenPayload(id) is null, "payload deleted");
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            }
        }

        public static void RemoteProtocolRejectsBadRequests()
        {
            using var rig = new RemoteRig();
            using (HubResponse r = rig.Raw("POST", "agents/poll", new PollRequest(), protocol: null)) Require(r.StatusCode == 400, "protocol header required");
            using (HubResponse r = rig.Raw("POST", "agents/poll", new PollRequest(), protocol: "2")) Require(r.StatusCode == 400, "unknown protocol version");
            using (HubResponse r = rig.Raw("POST", "agents/poll", new PollRequest(), token: "")) Require(r.StatusCode == 401, "token required");
            using (HubResponse r = rig.Raw("POST", "agents/poll", new PollRequest(), token: "wrong")) Require(r.StatusCode == 401, "bad token");
            using (HubResponse r = rig.Raw("POST", "agents/poll", new PollRequest(), agentId: "branch-02")) Require(r.StatusCode == 401, "token belongs to another agent");
            using (HubResponse r = rig.Raw("POST", "agents/nope")) Require(r.StatusCode == 404, "unknown endpoint");
            using (HubResponse r = rig.Raw("GET", "agents/poll")) Require(r.StatusCode == 405, "wrong method");
            using (HubResponse r = rig.Raw("GET", "jobs/" + Guid.NewGuid() + "/payload")) Require(r.StatusCode == 404, "unknown job");

            StoredJob job = rig.Submit();
            using (HubResponse r = rig.Raw("GET", "jobs/" + job.JobId + "/payload")) Require(r.StatusCode == 409, "payload only after a lease");
            using (HubResponse r = rig.Raw("POST", "agents/poll", new PollRequest { WaitSeconds = 0 })) Require(r.StatusCode == 200, "leased");
            using (HubResponse r = rig.Raw("GET", "jobs/" + job.JobId + "/payload", range: "bytes=10-19"))
            {
                Require(r.StatusCode == 206 && r.ContentLength == 10, "range served");
                Require(r.Headers["Content-Range"] == "bytes 10-19/" + job.PayloadBytes, "Content-Range");
            }
            using (HubResponse r = rig.Raw("GET", "jobs/" + job.JobId + "/payload", range: "bytes=" + job.PayloadBytes + "-"))
                Require(r.StatusCode == 416, "range past the end");
            using (HubResponse r = rig.Raw("POST", "jobs/" + job.JobId + "/status", new StatusReport { State = HubJobState.Queued }))
                Require(r.StatusCode == 400, "agents cannot report Queued");

            Require(PrintHub.TryParseRange("bytes=-5", 100, out long s, out long e) && s == 95 && e == 99, "suffix range");
            Require(!PrintHub.TryParseRange("bytes=1-2,4-5", 100, out _, out _), "multi-range refused");
        }

        public static void RemoteAgentDemandsHttps()
        {
            var options = new PrintAgentOptions
            {
                HubUri = new Uri("http://hub.example.com/print/"),
                AgentId = "a1",
                Credentials = new InMemoryAgentCredentialStore("t"),
                PrinterResolver = _ => null,
                JournalFolder = Path.Combine(Path.GetTempPath(), "chuvadi-https-" + Guid.NewGuid().ToString("N"))
            };
            bool refused = false;
            try { new PrintAgent(options).Dispose(); }
            catch (ArgumentException) { refused = true; }
            Require(refused, "plain HTTP to a remote hub is refused");

            options.HubUri = new Uri("http://127.0.0.1:5000/print/");
            new PrintAgent(options).Dispose();
            options.HubUri = new Uri("http://hub.example.com/print/");
            options.AllowInsecureHttp = true;
            new PrintAgent(options).Dispose();
            options.AgentId = "bad id/../";
            refused = false;
            try { new PrintAgent(options).Dispose(); }
            catch (ArgumentException) { refused = true; }
            Require(refused, "agent ids are restricted to safe characters");
            try { Directory.Delete(options.JournalFolder, recursive: true); } catch (IOException) { }
        }

        public static void RemoteDocumentTargetPassesPdfThrough()
        {
            using var rig = new RemoteRig();
            var documents = new RecordingDocumentTarget { Name = "IPP passthrough" };
            StoredJob queued = rig.Submit(pages: 3, copies: 2);
            rig.NewAgent(o => o.DocumentTargetResolver = name => name == RemoteRig.PrinterName ? documents : null)
                .RunOnceAsync().GetAwaiter().GetResult();
            Require(documents.Submissions.Count == 1 && rig.Target.Pages.Count == 0, "went to the document target, not the raster one");
            DocumentSubmission sub = documents.Submissions[0];
            Require(sub.JobId == queued.JobId && sub.Settings.Copies == 2 && sub.PageCount == 3, "job id, settings, page count");
            Require(SeqEqualBytes(sub.Document.ToArray(), FakeDocument.Uniform(3, PageDimensions.FromPaper(PaperSize.A4))), "document bytes unchanged");
            Require(rig.HubAudit.Events.Count == 1 && rig.HubAudit.Events[0].PrinterName == "IPP passthrough", "one event from SubmitTo");
        }

        public static void RemoteUnknownPrinterFailsOnce()
        {
            using var rig = new RemoteRig();
            StoredJob queued = rig.Submit(printer: "Plotter");
            rig.NewAgent().RunOnceAsync().GetAwaiter().GetResult();
            StoredJob job = rig.Hub.GetJob(queued.JobId)!;
            Require(job.State == HubJobState.Failed && job.Error!.Contains("Plotter"), "failed with the printer named: " + job.Error);
            Require(rig.HubAudit.Events.Count == 1 && rig.HubAudit.Events[0].MachineName == "BRANCH-PC", "one event, from the agent");
            Require(rig.AgentAudit.Events.Count == 1, "one local event");
        }
    }
}
