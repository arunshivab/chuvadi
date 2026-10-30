using System.IO.Pipes;
using System.Text;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.ManualTests;

namespace ChuvadiPdfPrinter.Tests;

/// <summary>A temporary folder deleted after the test.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder() => Directory.CreateDirectory(Path);
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chuvadi-pdf-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
}

internal static class Samples
{
    public static readonly DateTimeOffset Clock = new(2026, 10, 28, 9, 5, 7, TimeSpan.FromMinutes(330));

    public static byte[] Pdf(int pages = 2)
    {
        var sb = new StringBuilder("%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
        sb.Append("2 0 obj << /Type /Pages /Count ").Append(pages).Append(" /Kids [] >> endobj\n");
        for (int i = 0; i < pages; i++) sb.Append(3 + i).Append(" 0 obj << /Type /Page /Parent 2 0 R >> endobj\n");
        sb.Append("trailer << /Root 1 0 R >>\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public static JobInfo Job(string title = "Discharge summary.docx", string user = @"APULKI\dr.arun") => new()
    {
        JobId = 42, Title = title, UserName = user, MachineName = @"\\WARD3-PC", Submitted = Clock
    };

    public static PrinterConfig Config(string folder) => new()
    {
        OutputFolder = folder, FileNamePattern = "{date:yyyyMMdd-HHmmss} {title}", PipeName = "chuvadi-test-" + Guid.NewGuid().ToString("N")[..12]
    };

    public static AgentEngine Engine(PrinterConfig config, MemoryAuditSink audit, IForwarderFactory? forwarders = null)
        => new(config, audit, new MemoryLog(), forwarders, () => Clock);
}

public class ConfigTests
{
    [Fact]
    public void Defaults_round_trip_through_json_with_enum_names()
    {
        var config = new PrinterConfig { Collision = CollisionPolicy.Fail };
        config.ForwardTo.Add(new ForwardTarget { Kind = ForwardKind.Ipp, Printer = "ipp://p/ipp/print", Duplex = Duplex.Vertical });
        string json = config.ToJson();
        Assert.Contains("\"Fail\"", json);
        Assert.Contains("\"Ipp\"", json);
        using var temp = new TempFolder();
        string path = Path.Combine(temp.Path, "sub", "config.json");
        config.Save(path);
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "no BOM");
        PrinterConfig back = PrinterConfig.Load(path);
        Assert.Equal(CollisionPolicy.Fail, back.Collision);
        Assert.Equal(Duplex.Vertical, back.ForwardTo[0].Duplex);
        Assert.Equal("Chuvadi PDF", back.QueueName);
    }

    [Fact]
    public void Missing_or_partial_files_use_defaults_and_bad_json_is_reported()
    {
        using var temp = new TempFolder();
        Assert.Equal("ChuvadiPdfPrinter", PrinterConfig.Load(Path.Combine(temp.Path, "none.json")).PipeName);
        string partial = Path.Combine(temp.Path, "partial.json");
        File.WriteAllText(partial, "{ // comment\n \"queueName\": \"Ward PDF\", }");
        PrinterConfig loaded = PrinterConfig.Load(partial);
        Assert.Equal("Ward PDF", loaded.QueueName);
        Assert.True(loaded.PerUserSubfolder);
        string bad = Path.Combine(temp.Path, "bad.json");
        File.WriteAllText(bad, "{ not json");
        Assert.Throws<InvalidDataException>(() => PrinterConfig.Load(bad));
    }

    [Fact]
    public void Installer_written_config_loads()
    {
        // Exactly what scripts\install.ps1 writes on first install (Both mode, before the package family is known).
        const string json = "{\r\n  \"QueueName\": \"Chuvadi PDF (Compatibility)\",\r\n  \"PipeName\": \"ChuvadiPdfPrinter\",\r\n"
            + "  \"OutputFolder\": \"%PUBLIC%\\\\Documents\\\\Chuvadi PDF\",\r\n  \"PerUserSubfolder\": true,\r\n"
            + "  \"FileNamePattern\": \"{date:yyyy-MM-dd HHmmss} {title}\",\r\n  \"Collision\": \"AppendNumber\",\r\n"
            + "  \"PsaPackageFamilyName\": null,\r\n  \"ForwardTo\": []\r\n}\r\n";
        using var temp = new TempFolder();
        string path = Path.Combine(temp.Path, "config.json");
        File.WriteAllText(path, json);
        PrinterConfig config = PrinterConfig.Load(path);
        Assert.Equal("Chuvadi PDF (Compatibility)", config.QueueName);
        Assert.Equal(@"%PUBLIC%\Documents\Chuvadi PDF", config.OutputFolder);
        Assert.Null(config.PsaPackageFamilyName);
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void Validation_reports_problems()
    {
        var config = new PrinterConfig { PipeName = @"bad\name", MaxJobBytes = 0 };
        config.ForwardTo.Add(new ForwardTarget { Printer = "", Copies = 0 });
        Assert.Equal(4, config.Validate().Count);
        Assert.Empty(new PrinterConfig().Validate());
    }

    [Fact]
    public void Output_folder_expands_variables_and_adds_user_subfolder()
    {
        Environment.SetEnvironmentVariable("CHUVADI_TEST_ROOT", "/tmp/chuvadi-root");
        var config = new PrinterConfig { OutputFolder = "%CHUVADI_TEST_ROOT%" + Path.DirectorySeparatorChar + "PDF" };
        Assert.Equal(Path.Combine("/tmp/chuvadi-root", "PDF", "dr.arun"), config.ResolveOutputFolder(@"APULKI\dr.arun"));
        config.PerUserSubfolder = false;
        Assert.Equal(Path.Combine("/tmp/chuvadi-root", "PDF"), config.ResolveOutputFolder("x"));
    }
}

public class NamingTests
{
    [Fact]
    public void Pattern_tokens_expand()
    {
        string name = OutputNamer.Format("{date:yyyy-MM-dd} {title} - {user} on {machine} #{job} {unknown}", Samples.Job(), Samples.Clock);
        Assert.Equal("2026-10-28 Discharge summary.docx - dr.arun on WARD3-PC #42 {unknown}", name);
    }

    [Theory]
    [InlineData(@"C:\Reports\Lab report.pdf", "Lab report")]
    [InlineData("https://his/x?id=5", "x_id=5")]
    [InlineData("Plan: A/B <draft>|v2*?", "Plan_ A_B _draft__v2__")]
    [InlineData("trailing dots...", "trailing dots")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("   ", "Untitled")]
    public void Titles_become_valid_file_names(string title, string expected)
        => Assert.Equal(expected, OutputNamer.Format("{title}", Samples.Job(title: title), Samples.Clock));

    [Fact]
    public void Long_names_are_capped_and_bad_date_formats_kept_literal()
    {
        Assert.Equal(OutputNamer.MaxBaseNameLength, OutputNamer.Format("{title}", Samples.Job(title: new string('x', 500)), Samples.Clock).Length);
        Assert.Equal("{date_%}", OutputNamer.Format("{date:%}", Samples.Job(), Samples.Clock));
    }

    [Fact]
    public void Placement_numbers_overwrites_or_fails_as_configured()
    {
        using var temp = new TempFolder();
        string Source() { string p = Path.Combine(temp.Path, Guid.NewGuid() + ".tmp"); File.WriteAllText(p, "x"); return p; }
        string out1 = OutputNamer.Place(Source(), Path.Combine(temp.Path, "out"), "Report", CollisionPolicy.AppendNumber);
        string out2 = OutputNamer.Place(Source(), Path.Combine(temp.Path, "out"), "Report", CollisionPolicy.AppendNumber);
        string out3 = OutputNamer.Place(Source(), Path.Combine(temp.Path, "out"), "Report", CollisionPolicy.AppendNumber);
        Assert.Equal(new[] { "Report.pdf", "Report (2).pdf", "Report (3).pdf" }, new[] { out1, out2, out3 }.Select(Path.GetFileName));
        Assert.Equal(out1, OutputNamer.Place(Source(), Path.Combine(temp.Path, "out"), "Report", CollisionPolicy.Overwrite));
        Assert.Throws<IOException>(() => OutputNamer.Place(Source(), Path.Combine(temp.Path, "out"), "Report", CollisionPolicy.Fail));
    }

    [Fact]
    public void Concurrent_jobs_with_the_same_name_never_collide()
    {
        using var temp = new TempFolder();
        string folder = Path.Combine(temp.Path, "out");
        var sources = Enumerable.Range(0, 24).Select(i => { string p = Path.Combine(temp.Path, i + ".tmp"); File.WriteAllText(p, i.ToString()); return p; }).ToArray();
        string[] placed = new string[sources.Length];
        Parallel.For(0, sources.Length, i => placed[i] = OutputNamer.Place(sources[i], folder, "Same", CollisionPolicy.AppendNumber));
        Assert.Equal(24, placed.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 24).Select(i => i.ToString()).OrderBy(s => s), placed.Select(File.ReadAllText).OrderBy(s => s));
    }
}

public class SpoolerAndPdfTests
{
    private static JobInfo J(int id, int minute, SpoolJobStatus status) => new() { JobId = id, Submitted = Samples.Clock.AddMinutes(minute), Status = status };

    [Fact]
    public void Printing_job_is_selected_over_older_queued_jobs()
    {
        Assert.Equal(7, SpoolerJobSelector.SelectPrinting(new[] { J(5, 0, SpoolJobStatus.None), J(7, 1, SpoolJobStatus.Printing) })!.JobId);
        Assert.Equal(5, SpoolerJobSelector.SelectPrinting(new[] { J(6, 0, SpoolJobStatus.Spooling), J(5, 1, SpoolJobStatus.None) })!.JobId);
        Assert.Equal(6, SpoolerJobSelector.SelectPrinting(new[] { J(6, 0, SpoolJobStatus.Spooling) })!.JobId);
        Assert.Equal(9, SpoolerJobSelector.SelectPrinting(new[] { J(8, 0, SpoolJobStatus.Deleting | SpoolJobStatus.Printing), J(9, 1, SpoolJobStatus.None) })!.JobId);
        Assert.Null(SpoolerJobSelector.SelectPrinting(Array.Empty<JobInfo>()));
    }

    [Fact]
    public void Pdf_quick_checks_recognise_pdfs_and_count_pages()
    {
        using var temp = new TempFolder();
        string pdf = Path.Combine(temp.Path, "a.pdf");
        File.WriteAllBytes(pdf, Samples.Pdf(3));
        Assert.True(PdfQuickInfo.LooksLikePdf(pdf));
        Assert.Equal(3, PdfQuickInfo.CountPages(pdf));

        string noCount = Path.Combine(temp.Path, "b.pdf");
        File.WriteAllText(noCount, "%PDF-1.4\n<</Type /Page>> <</Type/Page>> <</Type /Pages /Kids []>>\n%%EOF");
        Assert.Equal(2, PdfQuickInfo.CountPages(noCount));

        string text = Path.Combine(temp.Path, "c.pdf");
        File.WriteAllText(text, "hello world, not a pdf");
        Assert.False(PdfQuickInfo.LooksLikePdf(text));
    }
}

public sealed class RecordingForwarders : IForwarderFactory
{
    public List<(string Path, JobInfo Job, ForwardTarget Target, bool ExistedAtForward)> Calls { get; } = new();
    public bool Fail { get; set; }

    public IForwarder Create(ForwardTarget target) => new Forwarder(this);

    private sealed class Forwarder : IForwarder
    {
        private readonly RecordingForwarders _owner;
        public Forwarder(RecordingForwarders owner) => _owner = owner;

        public PrintResult Forward(string pdfPath, JobInfo job, ForwardTarget target, IPrintAuditSink audit)
        {
            _owner.Calls.Add((pdfPath, job, target, File.Exists(pdfPath)));
            if (_owner.Fail) throw new InvalidOperationException("printer on fire");
            return PrintResult.Printed(Guid.NewGuid(), 1);
        }
    }
}

public class EngineTests
{
    [Fact]
    public void Captured_job_is_saved_named_audited_and_forwarded()
    {
        using var temp = new TempFolder();
        PrinterConfig config = Samples.Config(temp.Path);
        config.ForwardTo.Add(new ForwardTarget { Printer = "Ward-3 LaserJet" });
        config.ForwardTo.Add(new ForwardTarget { Printer = "Disabled", Enabled = false });
        var audit = new MemoryAuditSink();
        var forwarders = new RecordingForwarders();
        CaptureResult result = Samples.Engine(config, audit, forwarders).CaptureFromStream(new MemoryStream(Samples.Pdf(2)), Samples.Job());

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(Path.Combine(temp.Path, "dr.arun", "20261028-090507 Discharge summary.docx.pdf"), result.OutputPath);
        Assert.Equal(Samples.Pdf(2), File.ReadAllBytes(result.OutputPath!));
        Assert.True(result.IsPdf);
        Assert.Equal(2, result.Pages);
        PrintAuditEvent e = Assert.Single(audit.Events);
        Assert.Equal((PrintJobStatus.Printed, "Discharge summary.docx", @"APULKI\dr.arun", 2), (e.Status, e.DocumentTitle, e.UserName, e.PageCount));
        var call = Assert.Single(forwarders.Calls);
        Assert.Equal(("Ward-3 LaserJet", true), (call.Target.Printer, call.ExistedAtForward));
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Path, "dr.arun", ".chuvadi-incoming")));
    }

    [Fact]
    public void New_user_folders_are_reported_once_for_securing()
    {
        using var temp = new TempFolder();
        var created = new List<(string Folder, string User)>();
        AgentEngine engine = Samples.Engine(Samples.Config(temp.Path), new MemoryAuditSink());
        engine.UserFolderCreated = (folder, user) => created.Add((folder, user));
        engine.CaptureFromStream(new MemoryStream(Samples.Pdf()), Samples.Job());
        engine.CaptureFromStream(new MemoryStream(Samples.Pdf()), Samples.Job());
        var only = Assert.Single(created);
        Assert.Equal((Path.Combine(temp.Path, "dr.arun"), @"APULKI\dr.arun"), only);
    }

    [Fact]
    public void Non_pdf_data_is_kept_and_flagged()
    {
        using var temp = new TempFolder();
        var audit = new MemoryAuditSink();
        CaptureResult result = Samples.Engine(Samples.Config(temp.Path), audit).CaptureFromStream(new MemoryStream(Encoding.ASCII.GetBytes("PK raw data")), Samples.Job());
        Assert.True(result.Succeeded);
        Assert.False(result.IsPdf);
        Assert.Equal("PK raw data", File.ReadAllText(result.OutputPath!));
        Assert.Equal("output is not a complete PDF", audit.Events[0].Error);
    }

    [Theory]
    [InlineData(0, "no data")]
    [InlineData(5000, "MaxJobBytes")]
    public void Empty_or_oversized_jobs_fail_cleanly(int size, string message)
    {
        using var temp = new TempFolder();
        PrinterConfig config = Samples.Config(temp.Path);
        config.MaxJobBytes = 1000;
        var audit = new MemoryAuditSink();
        CaptureResult result = Samples.Engine(config, audit).CaptureFromStream(new MemoryStream(new byte[size]), Samples.Job());
        Assert.False(result.Succeeded);
        Assert.Contains(message, result.Error);
        Assert.Equal(PrintJobStatus.Failed, Assert.Single(audit.Events).Status);
        Assert.Empty(Directory.GetFiles(temp.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_failing_printer_does_not_lose_the_pdf()
    {
        using var temp = new TempFolder();
        PrinterConfig config = Samples.Config(temp.Path);
        config.ForwardTo.Add(new ForwardTarget { Printer = "Broken" });
        CaptureResult result = Samples.Engine(config, new MemoryAuditSink(), new RecordingForwarders { Fail = true })
            .CaptureFromStream(new MemoryStream(Samples.Pdf()), Samples.Job());
        Assert.True(result.Succeeded);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(PrintJobStatus.Failed, Assert.Single(result.Forwards).Result.Status);
        Assert.Contains("printer on fire", result.Forwards[0].Result.Error);
    }

    [Fact]
    public void Unavailable_forwarding_explains_itself_and_audits()
    {
        var audit = new MemoryAuditSink();
        IForwarder forwarder = new UnavailableForwarder("Forwarding to a Windows printer is switched off.");
        PrintResult result = forwarder.Forward("x.pdf", Samples.Job(), new ForwardTarget { Printer = "Ward-3" }, audit);
        Assert.Equal(PrintJobStatus.Failed, result.Status);
        Assert.Contains("switched off", result.Error);
        Assert.Equal("Ward-3", Assert.Single(audit.Events).PrinterName);
    }

    [Fact]
    public void Ipp_forwarding_passes_the_pdf_through_to_the_printer()
    {
        using var temp = new TempFolder();
        using var printer = new FakeIppPrinter();
        PrinterConfig config = Samples.Config(temp.Path);
        config.ForwardTo.Add(new ForwardTarget { Kind = ForwardKind.Ipp, Printer = printer.Uri, Copies = 2, Duplex = Duplex.Vertical });
        var audit = new MemoryAuditSink();
        CaptureResult result = Samples.Engine(config, audit).CaptureFromStream(new MemoryStream(Samples.Pdf()), Samples.Job());
        Assert.True(result.Succeeded);
        Assert.Equal(PrintJobStatus.Printed, Assert.Single(result.Forwards).Result.Status);
        var (request, document) = Assert.Single(printer.PrintJobs);
        Assert.Equal(Samples.Pdf(), document);
        Assert.Equal(2, request.Group(IppTag.JobAttributes)!["copies"]!.Value.AsInteger());
        Assert.Equal("dr.arun", request.Group(IppTag.OperationAttributes)!["requesting-user-name"]!.Value.AsString());
        Assert.Equal(2, audit.Events.Count); // the capture and the forward
    }
}

public sealed class FakeJobs : IJobInfoProvider
{
    public Func<IReadOnlyList<JobInfo>> Jobs { get; set; } = () => Array.Empty<JobInfo>();
    public IReadOnlyList<JobInfo> GetJobs(string queueName) => Jobs();
}

public class PipeCaptureTests
{
    private static async Task Send(string pipeName, byte[] data)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        await client.ConnectAsync(10_000);
        await client.WriteAsync(data);
        await client.FlushAsync();
    }

    [Fact]
    public async Task Each_pipe_connection_becomes_its_own_pdf()
    {
        using var temp = new TempFolder();
        PrinterConfig config = Samples.Config(temp.Path);
        var audit = new MemoryAuditSink();
        int next = 100;
        var jobs = new FakeJobs { Jobs = () => new[] { new JobInfo { JobId = Interlocked.Increment(ref next), Title = "Same title", UserName = "nurse", Status = SpoolJobStatus.Printing, Submitted = Samples.Clock } } };
        var service = new QueueCaptureService(Samples.Engine(config, audit), jobs);
        var captured = new List<CaptureResult>();
        var done = new TaskCompletionSource();
        service.JobCaptured += (_, r) => { lock (captured) { captured.Add(r); if (captured.Count == 5) done.TrySetResult(); } };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task server = service.RunAsync(cts.Token);
        for (int i = 0; i < 5; i++) await Send(config.PipeName, Samples.Pdf(i + 1)); // back to back, like a busy queue
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        cts.Cancel();
        await server.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(captured, r => Assert.True(r.Succeeded, r.Error));
        string[] files = captured.Select(r => r.OutputPath!).ToArray();
        Assert.Equal(5, files.Distinct().Count());
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, captured.Select(r => r.Pages).OrderBy(p => p));
        Assert.All(files, f => Assert.StartsWith(Path.Combine(temp.Path, "nurse"), f));
        Assert.Equal(5, audit.Events.Count);
    }

    [Fact]
    public async Task Queue_read_failure_still_saves_the_job()
    {
        using var temp = new TempFolder();
        PrinterConfig config = Samples.Config(temp.Path);
        config.PerUserSubfolder = false;
        var jobs = new FakeJobs { Jobs = () => throw new InvalidOperationException("spooler unavailable") };
        var service = new QueueCaptureService(Samples.Engine(config, new MemoryAuditSink()), jobs);
        var done = new TaskCompletionSource<CaptureResult>();
        service.JobCaptured += (_, r) => done.TrySetResult(r);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task server = service.RunAsync(cts.Token);
        await Send(config.PipeName, Samples.Pdf());
        CaptureResult result = await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        cts.Cancel();
        await server.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Succeeded);
        Assert.EndsWith("Untitled.pdf", result.OutputPath);
    }
}

public class OutboxTests
{
    [Fact]
    public void Committed_entries_are_placed_and_removed_from_the_outbox()
    {
        using var temp = new TempFolder();
        string outbox = Path.Combine(temp.Path, "outbox");
        string output = Path.Combine(temp.Path, "out");
        var audit = new MemoryAuditSink();
        var forwarders = new RecordingForwarders();
        PrinterConfig config = Samples.Config(output);
        config.ForwardTo.Add(new ForwardTarget { Printer = "Ward-3" });
        var collector = new OutboxCollector(Samples.Engine(config, audit, forwarders), () => new[] { outbox, Path.Combine(temp.Path, "missing") });

        OutboxWriter.Write(outbox, new MemoryStream(Samples.Pdf(4)), new OutboxEntry { Title = "Echo report", UserName = "tech", Printer = "Chuvadi PDF (Auto-save)" });
        File.WriteAllText(Path.Combine(outbox, "half.json.partial"), "{"); // not committed yet: ignored
        Assert.Equal(1, collector.ScanOnce());

        string placed = Assert.Single(Directory.GetFiles(Path.Combine(output, "tech"), "*.pdf"));
        Assert.EndsWith("Echo report.pdf", placed);
        Assert.Equal(Samples.Pdf(4), File.ReadAllBytes(placed));
        Assert.Equal(new[] { "half.json.partial" }, Directory.GetFiles(outbox).Select(Path.GetFileName));
        Assert.Equal(4, audit.Events[0].PageCount);
        Assert.Single(forwarders.Calls);
        Assert.Equal(0, collector.ScanOnce());
    }

    [Fact]
    public void Save_as_entries_are_audited_and_forwarded_but_not_placed_again()
    {
        using var temp = new TempFolder();
        string outbox = Path.Combine(temp.Path, "outbox");
        var audit = new MemoryAuditSink();
        var forwarders = new RecordingForwarders();
        PrinterConfig config = Samples.Config(Path.Combine(temp.Path, "out"));
        config.ForwardTo.Add(new ForwardTarget { Printer = "Ward-3" });
        var collector = new OutboxCollector(Samples.Engine(config, audit, forwarders), () => new[] { outbox });
        OutboxWriter.Write(outbox, new MemoryStream(Samples.Pdf()), new OutboxEntry { Title = "Consent", UserName = "clerk", SavedTo = @"C:\Users\clerk\Desktop\Consent.pdf" });
        collector.ScanOnce();
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "out")));
        Assert.Contains(@"C:\Users\clerk\Desktop\Consent.pdf", audit.Events[0].PrinterName);
        Assert.True(Assert.Single(forwarders.Calls).ExistedAtForward);
        Assert.Empty(Directory.GetFiles(outbox));
    }

    [Fact]
    public void Broken_records_are_set_aside()
    {
        using var temp = new TempFolder();
        string outbox = Path.Combine(temp.Path, "outbox");
        Directory.CreateDirectory(outbox);
        File.WriteAllText(Path.Combine(outbox, "a.json"), "{ broken");
        OutboxWriter.Commit(outbox, new OutboxEntry { Id = "b", Title = "No PDF" });
        var collector = new OutboxCollector(Samples.Engine(Samples.Config(Path.Combine(temp.Path, "out")), new MemoryAuditSink()), () => new[] { outbox });
        Assert.Equal(0, collector.ScanOnce());
        Assert.Equal(new[] { "a.json.bad", "b.json.bad" }, Directory.GetFiles(outbox).Select(Path.GetFileName).OrderBy(n => n));
    }

    [Fact]
    public void Psa_outboxes_are_found_for_every_user()
    {
        using var temp = new TempFolder();
        foreach (string user in new[] { "alice", "bob" })
            Directory.CreateDirectory(Path.Combine(temp.Path, user, "AppData", "Local", "Packages", "Chuvadi.Pdf_abc", "LocalState", "outbox"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "carol", "AppData"));
        Assert.Equal(2, OutboxCollector.PsaOutboxes(temp.Path, "Chuvadi.Pdf_abc").Count());
        Assert.Empty(OutboxCollector.PsaOutboxes(Path.Combine(temp.Path, "nope"), "Chuvadi.Pdf_abc"));
    }
}

public class TestPageTests
{
    [Fact]
    public void Test_page_renders_border_text_and_ramp()
    {
        IRasterDocument doc = new TestPageRasterizer().Open(Encoding.UTF8.GetBytes("CHUVADI PDF SELF-TEST\nToken 0123456789\n"));
        Assert.Equal(1, doc.PageCount);
        using RasterPage page = doc.Render(1, new RasterRequest { DpiX = 72, DpiY = 72, PixelFormat = RasterPixelFormat.Gray8 }, default);
        Assert.Equal((595, 842), (page.PixelWidth, page.PixelHeight));
        Assert.Equal(255, page.GetRow(5)[5]);       // margin is paper
        Assert.Equal(0, page.GetRow(300)[19]);      // left border
        Assert.Equal(0, page.GetRow(40)[40]);       // top-left corner mark
        int dark = 0;
        for (int y = 80; y < 110; y++) for (int x = 60; x < 400; x++) if (page.GetRow(y)[x] == 0) dark++;
        Assert.True(dark > 500, "title text drawn: " + dark);
        byte rampLeft = page.GetRow(740)[65], rampRight = page.GetRow(740)[530];
        Assert.True(rampLeft < 30 && rampRight > 220, "ramp " + rampLeft + ".." + rampRight);
    }

    [Fact]
    public void Test_page_prints_through_a_print_job()
    {
        using var temp = new TempFolder();
        var target = new ImageFileTarget(temp.Path).WithDpi(50);
        PrintResult result = new PrintJob(Encoding.UTF8.GetBytes("SELF TEST\n"))
            .WithRasterizer(new TestPageRasterizer()).WithColor(ColorMode.Monochrome).PrintTo(target);
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Single(target.WrittenFiles);
    }
}
