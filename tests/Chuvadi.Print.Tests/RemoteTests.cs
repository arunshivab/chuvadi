using System.Net;
using System.Text;
using Chuvadi.Print.ManualTests;
using Chuvadi.Print.Remote;

namespace Chuvadi.Print.Tests;

public class RemoteTests
{
    [Fact]
    public async Task RunAsyncPrintsEveryJobAndStopsCleanly()
    {
        using var rig = new RemoteRig(overHttp: true);
        var ids = Enumerable.Range(0, 3).Select(_ => rig.Submit(pages: 1).JobId).ToList();
        PrintAgent agent = rig.NewAgent(o => o.PollWaitSeconds = 1);
        using var cts = new CancellationTokenSource();
        Task run = agent.RunAsync(cts.Token);

        // Wait on the agent's own count, not only the hub's state: the hub marks a job Printed while handling the
        // final report, before its reply reaches the agent. Cancelling in that gap leaves the outcome journalled
        // (resent on the next start) and the agent's count one short.
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (agent.JobsCompleted < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        // A job submitted while the agent is long-polling wakes it at once.
        Guid late = rig.Submit(pages: 1).JobId;
        while (agent.JobsCompleted < 4 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(ids.Append(late), id => Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(id)!.State));
        Assert.Equal(4, rig.HubAudit.Events.Count);
        Assert.Equal(4, agent.JobsCompleted);
    }

    [Fact]
    public async Task AgentReconnectsAfterNetworkFailures()
    {
        using var rig = new RemoteRig();
        Guid id = rig.Submit().JobId;
        var flaky = new FlakyHandler(new PrintHubMessageHandler(rig.Hub, rig.HubUri), failures: 4);
        PrintAgent agent = rig.NewAgent(o => o.HttpHandler = flaky);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task run = agent.RunAsync(cts.Token);
        while (rig.Hub.GetJob(id)!.State != HubJobState.Printed && !cts.IsCancellationRequested) await Task.Delay(20);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(id)!.State);
        Assert.Equal(0, flaky.Remaining);
        Assert.Contains(rig.AgentLog, l => l.Contains("retrying"));
    }

    [Fact]
    public async Task FinalReportIsResentAfterTheHubWasUnreachable()
    {
        using var rig = new RemoteRig();
        Guid id = rig.Submit().JobId;
        // Fail every final report once the job is printing: the agent must keep the outcome and resend it.
        var gate = new FailFinalReportsHandler(new PrintHubMessageHandler(rig.Hub, rig.HubUri));
        PrintAgent agent = rig.NewAgent(o => o.HttpHandler = gate);
        using var cts = new CancellationTokenSource();
        Task<bool> once = agent.RunOnceAsync(cts.Token);
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (gate.FinalAttempts < 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(gate.FinalAttempts >= 2);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => once);
        Assert.Equal(HubJobState.Accepted, rig.Hub.GetJob(id)!.State);

        // Restart with a working network: the journalled outcome is delivered, nothing is printed again.
        PrintAgent restarted = rig.NewAgent();
        await restarted.RunOnceAsync();
        Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(id)!.State);
        Assert.Equal(2, rig.Target.Pages.Count);
        Assert.Single(rig.HubAudit.Events);
    }

    [Fact]
    public void InMemoryStoreIsIdempotentAndIsolated()
    {
        var store = new InMemoryJobStore();
        var job = new StoredJob { JobId = Guid.NewGuid(), AgentId = "a", Printer = "p" };
        Assert.Null(store.Add(job, new byte[] { 1, 2, 3 }));
        job.Printer = "changed";
        Assert.Equal("p", store.Get(job.JobId)!.Printer);
        Assert.NotNull(store.Add(job, new byte[] { 9 }));
        using (Stream s = store.OpenPayload(job.JobId)!) Assert.Equal(3, s.Length);
        store.DeletePayload(job.JobId);
        Assert.Null(store.OpenPayload(job.JobId));
        Assert.Throws<KeyNotFoundException>(() => store.Update(new StoredJob { JobId = Guid.NewGuid() }));
    }

    [Fact]
    public void FileTokenAndCredentialStoresPersist()
    {
        string folder = Path.Combine(Path.GetTempPath(), "chuvadi-tokens-" + Guid.NewGuid().ToString("N"));
        try
        {
            string tokenFile = Path.Combine(folder, "hub", "agents.json");
            var auth = new TokenAgentAuthenticator(new FileAgentTokenStore(tokenFile));
            string token = auth.Issue("pc-1");
            Assert.DoesNotContain(token, File.ReadAllText(tokenFile));

            var reloaded = new TokenAgentAuthenticator(new FileAgentTokenStore(tokenFile));
            Assert.True(reloaded.Authenticate("pc-1", token).Succeeded);
            Assert.False(reloaded.Authenticate("pc-1", token + "x").Succeeded);
            Assert.False(reloaded.Authenticate("pc-2", token).Succeeded);

            string credFile = Path.Combine(folder, "agent", "token.txt");
            var creds = new FileAgentCredentialStore(credFile);
            Assert.Null(creds.GetToken());
            creds.SetToken(token);
            Assert.Equal(token, new FileAgentCredentialStore(credFile).GetToken());
            Assert.False(File.ReadAllBytes(credFile).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()));
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ReissueInvalidatesOldTokenAndPendingRotation()
    {
        var auth = new TokenAgentAuthenticator(new InMemoryAgentTokenStore());
        string t1 = auth.Issue("pc");
        string pending = auth.BeginRotation("pc");
        AgentAuthResult r = auth.Authenticate("pc", t1);
        Assert.True(r.Succeeded);
        Assert.Equal(pending, r.NewToken);
        string t2 = auth.Issue("pc");
        Assert.False(auth.Authenticate("pc", t1).Succeeded);
        Assert.False(auth.Authenticate("pc", pending).Succeeded);
        Assert.True(auth.Authenticate("pc", t2).Succeeded);
        Assert.Throws<InvalidOperationException>(() => auth.BeginRotation("nobody"));
        Assert.Equal(43, t2.Length);
    }

    [Fact]
    public void AuditRecordRoundTripsEveryField()
    {
        var e = new PrintAuditEvent
        {
            JobId = Guid.NewGuid(), DocumentTitle = "t", PageCount = 3, Copies = 2, PrinterName = "p", MachineName = "m",
            UserName = "u", Status = PrintJobStatus.Failed, StartedAt = DateTimeOffset.UnixEpoch,
            CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(5), TenantId = "x", Error = "boom"
        };
        PrintAuditEvent back = AuditRecord.From(e).ToEvent();
        foreach (var p in typeof(PrintAuditEvent).GetProperties())
            Assert.Equal(p.GetValue(e), p.GetValue(back));
    }

    private sealed class FlakyHandler : DelegatingHandler
    {
        public FlakyHandler(HttpMessageHandler inner, int failures) : base(inner) => Remaining = failures;
        public int Remaining { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Remaining > 0)
            {
                Remaining--;
                throw new HttpRequestException("Simulated network failure.");
            }
            return base.SendAsync(request, ct);
        }
    }

    private sealed class FailFinalReportsHandler : DelegatingHandler
    {
        public FailFinalReportsHandler(HttpMessageHandler inner) : base(inner) { }
        public int FinalAttempts;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/status") && request.Content is not null)
            {
                string body = await request.Content.ReadAsStringAsync(ct);
                if (body.Contains("\"Printed\""))
                {
                    Interlocked.Increment(ref FinalAttempts);
                    return new HttpResponseMessage(HttpStatusCode.BadGateway) { RequestMessage = request };
                }
            }
            return await base.SendAsync(request, ct);
        }
    }
}
