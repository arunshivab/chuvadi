using System.Net;
using System.Text;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.ManualTests;
using Chuvadi.Print.Remote;

namespace Chuvadi.Print.Tests;

/// <summary>
/// 1.0.0-rc.5 (D-349, D-355): PrintSettings.Private keeps the title out of job names, audit events, envelopes, the hub
/// and the agent's journal (S2); PrintAgentOptions.JobStorage = Memory keeps the document off the agent's disk (S1).
/// </summary>
public class PrivateJobsTests
{
    private const string Title = "Recovery codes for arun";
    private static readonly PageDimensions A4Page = PageDimensions.FromPaper(PaperSize.A4);

    private static PrintJob FakeJob(int pages = 1)
        => new PrintJob(FakeDocument.Uniform(pages, A4Page)).WithRasterizer(new FakeRasterizer()).WithTitle(Title);

    private static byte[] FakePdf()
        => Encoding.ASCII.GetBytes("%PDF-1.7\n% Chuvadi private job test\n%%EOF\n");

    // ------------------------------------------------------------------ S2: core

    [Fact]
    public void Private_is_off_by_default_cloned_and_set_by_WithPrivate()
    {
        Assert.False(new PrintSettings().Private);
        Assert.True(new PrintSettings { Private = true }.Clone().Private);
        Assert.True(FakeJob().WithPrivate().Settings.Private);
        Assert.False(FakeJob().WithPrivate().WithPrivate(false).Settings.Private);
        Assert.Equal("Chuvadi job", PrintSettings.PrivateJobName);
    }

    [Fact]
    public void A_private_raster_job_is_named_Chuvadi_job_and_audited_without_its_title()
    {
        var target = new RecordingTarget();
        var sink = new MemoryAuditSink();
        PrintResult result = FakeJob().WithPrivate().WithAuditSink(sink).PrintTo(target);

        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(PrintSettings.PrivateJobName, target.Context!.DocumentTitle);
        Assert.Null(Assert.Single(sink.Events).DocumentTitle);
    }

    [Fact]
    public void A_job_that_is_not_private_keeps_its_title_as_before()
    {
        var target = new RecordingTarget();
        var sink = new MemoryAuditSink();
        FakeJob().WithAuditSink(sink).PrintTo(target);
        Assert.Equal(Title, target.Context!.DocumentTitle);
        Assert.Equal(Title, Assert.Single(sink.Events).DocumentTitle);

        var doc = new RecordingDocumentTarget();
        var docSink = new MemoryAuditSink();
        new PrintJob(FakePdf()).WithTitle(Title).WithAuditSink(docSink).SubmitTo(doc);
        Assert.Equal(Title, Assert.Single(doc.Submissions).DocumentTitle);
        Assert.Equal(Title, Assert.Single(docSink.Events).DocumentTitle);
        Assert.Equal(Title, SpoolEnvelope.FromArray(new PrintJob(FakePdf()).WithTitle(Title).ToSpoolArray()).DocumentTitle);
    }

    [Fact]
    public void A_private_document_job_is_named_Chuvadi_job_and_audited_without_its_title()
    {
        var target = new RecordingDocumentTarget();
        var sink = new MemoryAuditSink();
        PrintResult result = new PrintJob(FakePdf()).WithTitle(Title).WithPrivate().WithAuditSink(sink).SubmitTo(target);

        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(PrintSettings.PrivateJobName, Assert.Single(target.Submissions).DocumentTitle);
        Assert.Null(Assert.Single(sink.Events).DocumentTitle);
    }

    [Fact]
    public void A_private_job_that_no_route_can_print_is_audited_without_its_title()
    {
        var sink = new MemoryAuditSink();
        var router = new PrintRouter(new SkippingRoute());
        PrintResult result = router.Print(FakeJob().WithPrivate().WithAuditSink(sink)).Result;
        Assert.NotEqual(PrintJobStatus.Printed, result.Status);
        Assert.Null(Assert.Single(sink.Events).DocumentTitle);

        var plainSink = new MemoryAuditSink();
        router.Print(FakeJob().WithAuditSink(plainSink));
        Assert.Equal(Title, Assert.Single(plainSink.Events).DocumentTitle);
    }

    [Fact]
    public void Ipp_printers_get_the_neutral_job_name_for_a_private_job()
    {
        using var printer = new FakeIppPrinter();
        using (var raster = new IppPrinterTarget(printer.Uri).WithMaxRasterDpi(100))
            Assert.Equal(PrintJobStatus.Printed, FakeJob().WithPrivate().PrintTo(raster).Status);
        using (var document = new IppDocumentTarget(printer.Uri))
            Assert.Equal(PrintJobStatus.Printed, new PrintJob(FakePdf()).WithTitle(Title).WithPrivate().SubmitTo(document).Status);
        using (var plain = new IppDocumentTarget(printer.Uri))
            new PrintJob(FakePdf()).WithTitle(Title).SubmitTo(plain);

        string[] names = printer.PrintJobs
            .Select(j => j.Request.Group(IppTag.OperationAttributes)!["job-name"]!.Value.AsString()).ToArray();
        Assert.Equal(new[] { PrintSettings.PrivateJobName, PrintSettings.PrivateJobName, Title }, names);
    }

    // ------------------------------------------------------------------ S2: spool envelope

    [Fact]
    public void A_private_envelope_is_version_4_without_a_title_and_round_trips()
    {
        byte[] bytes = new PrintJob(FakePdf()).WithTitle(Title).WithUser("u").WithPrivate()
            .WithPrinterDefaults(PrinterDefault.Paper).ToSpoolArray();
        Assert.Equal(4, bytes[4]);
        Assert.Equal(SpoolEnvelope.CurrentVersion, bytes[4]);
        Assert.DoesNotContain(Title, Encoding.UTF8.GetString(bytes));

        SpoolEnvelope back = SpoolEnvelope.FromArray(bytes);
        Assert.True(back.Settings.Private);
        Assert.Equal(PrinterDefault.Paper, back.Settings.PrinterDefaults);
        Assert.Null(back.DocumentTitle);
        Assert.Equal("u", back.UserName);
    }

    [Fact]
    public void A_private_envelope_cannot_be_written_in_an_older_version_and_unknown_options_are_refused()
    {
        var envelope = new SpoolEnvelope(new byte[] { 1 }, new PrintSettings { Private = true });
        using (var ms = new MemoryStream())
            Assert.Throws<InvalidOperationException>(() => envelope.Serialize(ms, 3));

        // Version 4 with an option bit this version does not know, and a correct checksum, so the reader reaches the
        // options byte: the header is 14 bytes; the body ends with the options byte, the 8-byte PDF length and the
        // 1-byte PDF.
        byte[] bytes = envelope.ToArray();
        int optionsAt = bytes.Length - 1 - 8 - 1;
        Assert.Equal(1, bytes[optionsAt]);
        bytes[optionsAt] = 3;
        BinaryPrimitivesWriteCrc(bytes);
        var ex = Assert.Throws<InvalidDataException>(() => SpoolEnvelope.FromArray(bytes));
        Assert.Contains("Unknown job options", ex.Message);
    }

    [Fact]
    public void Envelopes_without_private_keep_their_earlier_versions()
    {
        Assert.Equal(2, new PrintJob(FakePdf()).ToSpoolArray()[4]);
        Assert.Equal(3, new PrintJob(FakePdf()).WithPrinterDefaults(PrinterDefault.Copies).ToSpoolArray()[4]);
    }

    // ------------------------------------------------------------------ S2: hub

    [Fact]
    public async Task The_hub_never_stores_leases_or_audits_a_private_jobs_title()
    {
        using var rig = new RemoteRig();
        var job = new PrintJob(FakeDocument.Uniform(1, A4Page)).WithTitle(Title).WithPrivate();
        StoredJob stored = rig.Hub.Submit(RemoteRig.AgentId, RemoteRig.PrinterName, job);
        Assert.Null(stored.Title);

        // Even an envelope built by hand with a title: the hub drops it.
        var envelope = new SpoolEnvelope(FakeDocument.Uniform(1, A4Page), new PrintSettings { Private = true }) { DocumentTitle = Title };
        StoredJob handMade = rig.Hub.Submit(RemoteRig.AgentId, RemoteRig.PrinterName, envelope);
        Assert.Null(handMade.Title);

        PrintAgent agent = rig.NewAgent();
        Assert.True(await agent.RunOnceAsync());
        Assert.True(await agent.RunOnceAsync());
        Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(stored.JobId)!.State);
        Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(handMade.JobId)!.State);
        Assert.All(rig.HubAudit.Events, e => Assert.Null(e.DocumentTitle));
        Assert.All(rig.AgentAudit.Events, e => Assert.Null(e.DocumentTitle));
        Assert.Equal(PrintSettings.PrivateJobName, rig.Target.Context!.DocumentTitle);
        AssertNoFileContains(rig.Folder, Title);
    }

    [Fact]
    public async Task Hub_PrivateJobs_makes_every_job_private()
    {
        using var rig = new RemoteRig(configure: o => o.PrivateJobs = true);
        StoredJob stored = rig.Submit(pages: 1);           // titled "Invoice 42", not private
        Assert.Null(stored.Title);
        PrintAgent agent = rig.NewAgent();
        Assert.True(await agent.RunOnceAsync());
        Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(stored.JobId)!.State);
        Assert.Equal(PrintSettings.PrivateJobName, rig.Target.Context!.DocumentTitle);
        Assert.True(rig.Target.Context.Settings.Private);
        Assert.Null(Assert.Single(rig.HubAudit.Events).DocumentTitle);
    }

    [Fact]
    public async Task Agent_PrivateJobs_keeps_titles_out_of_its_own_records()
    {
        using var rig = new RemoteRig();
        StoredJob stored = rig.Submit(pages: 1);           // titled "Invoice 42", not private
        Assert.Equal("Invoice 42", stored.Title);
        PrintAgent agent = rig.NewAgent(o => o.PrivateJobs = true);
        Assert.True(await agent.RunOnceAsync());
        Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(stored.JobId)!.State);
        Assert.Equal(PrintSettings.PrivateJobName, rig.Target.Context!.DocumentTitle);
        Assert.Null(Assert.Single(rig.AgentAudit.Events).DocumentTitle);
        AssertNoFileContains(rig.JournalFolder, "Invoice 42");
    }

    // ------------------------------------------------------------------ S1: memory-only agent jobs

    [Fact]
    public async Task Memory_mode_prints_without_writing_the_job_to_disk_and_journals_no_title()
    {
        using var rig = new RemoteRig();
        StoredJob stored = rig.Submit(pages: 2);           // titled "Invoice 42"
        var filesWhilePrinting = new List<string>();
        byte[]? held = null;
        PrintAgent agent = rig.NewAgent(o =>
        {
            o.JobStorage = AgentJobStorage.Memory;
            o.ConfigureJob = (job, _) =>
            {
                held = job.PdfBytes;
                filesWhilePrinting.AddRange(Directory.EnumerateFiles(rig.JournalFolder).Select(Path.GetFileName)!);
            };
        });

        Assert.True(await agent.RunOnceAsync());
        Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(stored.JobId)!.State);
        Assert.Equal(2, rig.Target.Pages.Count);

        // Nothing but journal entries in the folder, while printing and after.
        Assert.All(filesWhilePrinting, f => Assert.EndsWith(".journal.json", f));
        string[] after = Directory.EnumerateFiles(rig.JournalFolder).Select(Path.GetFileName).ToArray()!;
        Assert.Equal(stored.JobId.ToString("N") + ".journal.json", Assert.Single(after));
        AssertNoFileContains(rig.JournalFolder, "Invoice 42");

        // The hub still gets the job's audit with its title (the job is not private, only memory-only).
        Assert.Equal("Invoice 42", Assert.Single(rig.HubAudit.Events).DocumentTitle);

        // The document the job held was cleared after printing.
        Assert.NotNull(held);
        Assert.True(held!.Length > 0);
        Assert.All(held, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task Disk_mode_is_still_the_default_and_keeps_the_job_file_while_printing()
    {
        using var rig = new RemoteRig();
        StoredJob stored = rig.Submit(pages: 1);
        var filesWhilePrinting = new List<string>();
        PrintAgent agent = rig.NewAgent(o => o.ConfigureJob = (_, _) =>
            filesWhilePrinting.AddRange(Directory.EnumerateFiles(rig.JournalFolder).Select(Path.GetFileName)!));
        Assert.Equal(AgentJobStorage.Disk, new PrintAgentOptions().JobStorage);
        Assert.Equal(256L * 1024 * 1024, new PrintAgentOptions().MemoryJobLimit);
        Assert.False(new PrintAgentOptions().PrivateJobs);

        Assert.True(await agent.RunOnceAsync());
        Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(stored.JobId)!.State);
        Assert.Contains(stored.JobId.ToString("N") + ".cvpr", filesWhilePrinting);
        Assert.DoesNotContain(Directory.EnumerateFiles(rig.JournalFolder), f => f.EndsWith(".cvpr"));
        // The journal keeps the title, as before.
        Assert.Contains("Invoice 42", File.ReadAllText(Path.Combine(rig.JournalFolder, stored.JobId.ToString("N") + ".journal.json")));
    }

    [Fact]
    public async Task Memory_mode_refuses_a_job_over_the_limit_without_writing_it()
    {
        using var rig = new RemoteRig();
        StoredJob stored = rig.Submit(pages: 1);
        PrintAgent agent = rig.NewAgent(o =>
        {
            o.JobStorage = AgentJobStorage.Memory;
            o.MemoryJobLimit = 16;
        });
        Assert.True(await agent.RunOnceAsync());
        StoredJob job = rig.Hub.GetJob(stored.JobId)!;
        Assert.Equal(HubJobState.Failed, job.State);
        Assert.Contains("MemoryJobLimit", job.Error);
        Assert.Empty(rig.Target.Pages);
        Assert.All(Directory.EnumerateFiles(rig.JournalFolder), f => Assert.EndsWith(".journal.json", f));
    }

    [Fact]
    public async Task Memory_mode_resumes_an_interrupted_download_in_memory()
    {
        using var rig = new RemoteRig();
        StoredJob stored = rig.Submit(pages: 3);
        var cut = new CutPayloadHandler(new PrintHubMessageHandler(rig.Hub, rig.HubUri), cuts: 2);
        PrintAgent agent = rig.NewAgent(o =>
        {
            o.JobStorage = AgentJobStorage.Memory;
            o.HttpHandler = cut;
        });
        Assert.True(await agent.RunOnceAsync());
        Assert.Equal(HubJobState.Printed, rig.Hub.GetJob(stored.JobId)!.State);
        Assert.Equal(3, rig.Target.Pages.Count);
        Assert.Equal(0, cut.Remaining);
        Assert.True(cut.Resumed >= 1, "the second attempt asked for the rest with a Range header");
        Assert.Contains(rig.AgentLog, l => l.Contains("interrupted"));
        Assert.All(Directory.EnumerateFiles(rig.JournalFolder), f => Assert.EndsWith(".journal.json", f));
    }

    [Fact]
    public async Task Memory_mode_fails_a_payload_whose_hash_never_matches()
    {
        using var rig = new RemoteRig();
        StoredJob stored = rig.Submit(pages: 1);
        PrintAgent agent = rig.NewAgent(o =>
        {
            o.JobStorage = AgentJobStorage.Memory;
            o.HttpHandler = new CorruptPayloadHandler(new PrintHubMessageHandler(rig.Hub, rig.HubUri));
            o.DownloadAttempts = 2;
        });
        Assert.True(await agent.RunOnceAsync());
        StoredJob job = rig.Hub.GetJob(stored.JobId)!;
        Assert.Equal(HubJobState.Failed, job.State);
        Assert.Contains("SHA-256", job.Error);
        Assert.Empty(rig.Target.Pages);
    }

    [Fact]
    public async Task Memory_mode_still_never_prints_a_job_twice_after_a_crash()
    {
        using var rig = new RemoteRig();
        StoredJob stored = rig.Submit(pages: 1);
        // A journal entry left by a crash mid-print: started, no outcome.
        Directory.CreateDirectory(rig.JournalFolder);
        File.WriteAllText(Path.Combine(rig.JournalFolder, stored.JobId.ToString("N") + ".journal.json"),
                          "{\"jobId\":\"" + stored.JobId + "\",\"startedAt\":\"2026-01-01T09:00:00+00:00\"}");
        PrintAgent agent = rig.NewAgent(o => o.JobStorage = AgentJobStorage.Memory);
        await agent.RunOnceAsync();
        Assert.Empty(rig.Target.Pages);
        Assert.Equal(HubJobState.Failed, rig.Hub.GetJob(stored.JobId)!.State);
        Assert.Contains("not printed again", rig.Hub.GetJob(stored.JobId)!.Error);
    }

    [Fact]
    public void Agent_options_check_the_storage_and_the_memory_limit()
    {
        using var rig = new RemoteRig();
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.NewAgent(o => o.MemoryJobLimit = 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.NewAgent(o => o.MemoryJobLimit = PrintAgentOptions.MaxMemoryJobLimit + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.NewAgent(o => o.JobStorage = (AgentJobStorage)7));
        rig.NewAgent(o => o.MemoryJobLimit = PrintAgentOptions.MaxMemoryJobLimit);
    }

    // ------------------------------------------------------------------ R5-H (D-375): private errors keep only the type and code

    private const string SecretPath = "/home/arun/secret/Recovery codes for arun.pdf";

    [Fact]
    public void ErrorCode_is_the_type_and_hresult_without_the_message()
    {
        var ex = new IOException("Could not write " + SecretPath);
        Assert.Equal("IOException (0x" + ex.HResult.ToString("X8") + ")", PrintAuditEvent.ErrorCode(ex));
        Assert.DoesNotContain("secret", PrintAuditEvent.ErrorCode(ex));
        Assert.Throws<ArgumentNullException>(() => PrintAuditEvent.ErrorCode(null!));
    }

    [Fact]
    public void A_private_raster_job_that_fails_audits_only_the_error_code_and_the_result_keeps_the_message()
    {
        var sink = new MemoryAuditSink();
        PrintResult result = FakeJob().WithPrivate().WithAuditSink(sink).WithThrowOnFailure(false).PrintTo(new PathFailingTarget());
        Assert.Equal(PrintJobStatus.Failed, result.Status);
        Assert.Contains(SecretPath, result.Error);                        // to the calling app only
        PrintAuditEvent audit = Assert.Single(sink.Events);
        Assert.StartsWith("IOException (0x", audit.Error);
        Assert.DoesNotContain("secret", audit.Error);

        // Not private: the audit keeps the message, as before.
        var plain = new MemoryAuditSink();
        FakeJob().WithAuditSink(plain).WithThrowOnFailure(false).PrintTo(new PathFailingTarget());
        Assert.Contains(SecretPath, Assert.Single(plain.Events).Error);
    }

    [Fact]
    public void A_private_document_job_that_fails_audits_only_the_error_code()
    {
        var sink = new MemoryAuditSink();
        PrintResult result = new PrintJob(FakePdf()).WithPrivate().WithAuditSink(sink).WithThrowOnFailure(false)
            .SubmitTo(new PathFailingDocumentTarget());
        Assert.Equal(PrintJobStatus.Failed, result.Status);
        Assert.Contains(SecretPath, result.Error);
        Assert.StartsWith("IOException (0x", Assert.Single(sink.Events).Error);
    }

    [Fact]
    public void A_private_job_no_route_can_print_audits_no_reasons()
    {
        var sink = new MemoryAuditSink();
        new PrintRouter(new SkippingRoute()).Print(FakeJob().WithPrivate().WithAuditSink(sink));
        PrintAuditEvent audit = Assert.Single(sink.Events);
        Assert.Equal(PrintRouter.NoRouteForPrivateJob, audit.Error);
        Assert.Equal("None (Raster)", audit.Route);
        Assert.DoesNotContain("never fits", audit.Route);

        // Not private: the reasons stay in both, as before.
        var plain = new MemoryAuditSink();
        new PrintRouter(new SkippingRoute()).Print(FakeJob().WithAuditSink(plain));
        PrintAuditEvent before = Assert.Single(plain.Events);
        Assert.Contains("never fits", before.Error);
        Assert.Equal("None (Raster: never fits)", before.Route);
    }

    [Fact]
    public void A_routed_private_job_records_route_kinds_without_skip_reasons()
    {
        var sink = new MemoryAuditSink();
        var target = new RecordingTarget();
        new PrintRouter(new SkippingRoute(), new RasterRoute(target)).Print(FakeJob().WithPrivate().WithAuditSink(sink));
        Assert.Equal("Raster (fallback: Raster)", Assert.Single(sink.Events).Route);
    }

    [Fact]
    public async Task A_private_job_that_fails_on_the_agent_reports_only_the_error_code_to_the_hub()
    {
        using var rig = new RemoteRig();
        var job = new PrintJob(FakeDocument.Uniform(1, A4Page)).WithTitle(Title).WithPrivate();
        StoredJob stored = rig.Hub.Submit(RemoteRig.AgentId, RemoteRig.PrinterName, job);
        PrintAgent agent = rig.NewAgent(o => o.PrinterResolver = _ => new PathFailingTarget());
        Assert.True(await agent.RunOnceAsync());

        StoredJob after = rig.Hub.GetJob(stored.JobId)!;
        Assert.Equal(HubJobState.Failed, after.State);
        Assert.StartsWith("IOException (0x", after.Error);
        Assert.StartsWith("IOException (0x", Assert.Single(rig.HubAudit.Events).Error);
        Assert.StartsWith("IOException (0x", Assert.Single(rig.AgentAudit.Events).Error);
        Assert.DoesNotContain(rig.AgentLog, l => l.Contains("secret"));
        AssertNoFileContains(rig.Folder, "secret");
    }

    [Fact]
    public async Task A_private_job_the_agent_has_no_printer_for_reports_a_fixed_text()
    {
        using var rig = new RemoteRig();
        var job = new PrintJob(FakeDocument.Uniform(1, A4Page)).WithPrivate();
        StoredJob stored = rig.Hub.Submit(RemoteRig.AgentId, "Not-here", job);
        PrintAgent agent = rig.NewAgent();
        Assert.True(await agent.RunOnceAsync());
        Assert.Equal(PrintAuditEvent.PrivateErrorText, rig.Hub.GetJob(stored.JobId)!.Error);
        Assert.Equal(PrintAuditEvent.PrivateErrorText, Assert.Single(rig.AgentAudit.Events).Error);
    }

    [Fact]
    public async Task An_agent_with_PrivateJobs_reports_a_failed_download_by_its_code()
    {
        using var rig = new RemoteRig();
        StoredJob stored = rig.Submit(pages: 1);
        PrintAgent agent = rig.NewAgent(o =>
        {
            o.PrivateJobs = true;
            o.JobStorage = AgentJobStorage.Memory;
            o.HttpHandler = new CorruptPayloadHandler(new PrintHubMessageHandler(rig.Hub, rig.HubUri));
            o.DownloadAttempts = 1;
        });
        Assert.True(await agent.RunOnceAsync());
        string? error = rig.Hub.GetJob(stored.JobId)!.Error;
        Assert.StartsWith("InvalidDataException (0x", error);
        Assert.DoesNotContain("SHA-256", error);
    }

    // ------------------------------------------------------------------ helpers

    private static void BinaryPrimitivesWriteCrc(byte[] envelope)
    {
        uint crc = Crc32.Compute(envelope.AsSpan(14).ToArray());
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(6), crc);
    }

    private static void AssertNoFileContains(string folder, string text)
    {
        if (!Directory.Exists(folder)) return;
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            string content = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            Assert.False(content.Contains(text, StringComparison.Ordinal), file + " contains \"" + text + "\"");
        }
    }

    /// <summary>Fails when the job starts, with a message that names a file path.</summary>
    private sealed class PathFailingTarget : IPrintTarget
    {
        public string Name => "Failing";
        public PrintTargetCapabilities GetCapabilities(PrintSettings settings) => new() { DpiX = 72, DpiY = 72 };
        public void StartJob(PrintJobContext context) => throw new IOException("Could not write " + SecretPath);
        public void PrintPage(PrintedPage page) { }
        public void EndJob() { }
        public void AbortJob(Exception? reason) { }
    }

    private sealed class PathFailingDocumentTarget : IDocumentTarget
    {
        public string Name => "Failing document";
        public void Submit(DocumentSubmission submission, CancellationToken cancellationToken)
            => throw new IOException("Could not write " + SecretPath);
    }

    private sealed class SkippingRoute : IPrintRoute
    {
        public PrintRouteKind Kind => PrintRouteKind.Raster;
        public string Name => "skips";
        public PrintRouteCheck Check(PrintRouteContext context, CancellationToken cancellationToken) => PrintRouteCheck.Skip("never fits");
        public PrintResult Print(PrintJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>The first payload responses stop with a network error halfway through the body.</summary>
    private sealed class CutPayloadHandler : DelegatingHandler
    {
        public CutPayloadHandler(HttpMessageHandler inner, int cuts) : base(inner) => Remaining = cuts;
        public int Remaining { get; private set; }
        public int Resumed { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            HttpResponseMessage response = await base.SendAsync(request, ct);
            if (!request.RequestUri!.AbsolutePath.EndsWith("/payload")) return response;
            if (request.Headers.Range is not null) Resumed++;
            if (Remaining == 0) return response;
            Remaining--;
            byte[] body = await response.Content.ReadAsByteArrayAsync(ct);
            var cut = new HttpResponseMessage(response.StatusCode) { RequestMessage = request, Content = new StreamContent(new BreakingStream(body, body.Length / 2)) };
            foreach (var h in response.Content.Headers) cut.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            return cut;
        }
    }

    /// <summary>Every payload response has one byte changed.</summary>
    private sealed class CorruptPayloadHandler : DelegatingHandler
    {
        public CorruptPayloadHandler(HttpMessageHandler inner) : base(inner) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            HttpResponseMessage response = await base.SendAsync(request, ct);
            if (!request.RequestUri!.AbsolutePath.EndsWith("/payload")) return response;
            byte[] body = await response.Content.ReadAsByteArrayAsync(ct);
            if (body.Length > 0) body[^1] ^= 0xFF;
            var bad = new HttpResponseMessage(response.StatusCode) { RequestMessage = request, Content = new ByteArrayContent(body) };
            foreach (var h in response.Content.Headers) bad.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            return bad;
        }
    }

    /// <summary>Returns the first <c>stopAt</c> bytes, then throws as a dropped connection would.</summary>
    private sealed class BreakingStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _stopAt;
        private int _position;

        public BreakingStream(byte[] data, int stopAt)
        {
            _data = data;
            _stopAt = stopAt;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _stopAt) throw new IOException("Simulated dropped connection.");
            int n = Math.Min(count, _stopAt - _position);
            Array.Copy(_data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
