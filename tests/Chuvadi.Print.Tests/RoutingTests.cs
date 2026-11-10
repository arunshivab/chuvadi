using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.ManualTests;
using Chuvadi.Print.Remote;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.Tests;

/// <summary>A clock tests move by hand.</summary>
internal sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

public class AuditRouteTests
{
    private static readonly PageDimensions A4 = PageDimensions.FromPaper(PaperSize.A4);

    [Fact]
    public void File_sink_writes_the_route_as_the_last_of_13_columns()
    {
        string path = Path.Combine(Path.GetTempPath(), "chuvadi-route-audit-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var sink = new FileAuditSink(path);
            sink.Record(new PrintAuditEvent { DocumentTitle = "Old style", Error = "e" });
            sink.Record(new PrintAuditEvent { DocumentTitle = "Routed", Route = "Raster (fallback: PdfAsIs: fonts not known to be embedded)" });
            string[] lines = File.ReadAllLines(path);
            string[] first = lines[0].Split('\t');
            string[] second = lines[1].Split('\t');
            Assert.Equal(13, first.Length);
            Assert.Equal("e", first[11]);      // error keeps its position
            Assert.Equal(string.Empty, first[12]);
            Assert.Equal("Routed", second[1]);
            Assert.Equal("Raster (fallback: PdfAsIs: fonts not known to be embedded)", second[12]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Direct_print_and_submit_record_raster_and_document()
    {
        var sink = new MemoryAuditSink();
        new PrintJob(FakeDocument.Uniform(1, A4)).WithRasterizer(new FakeRasterizer()).WithAuditSink(sink)
            .PrintTo(new ImageFileTarget(Path.Combine(Path.GetTempPath(), "chuvadi-route-png-" + Guid.NewGuid().ToString("N"))));
        using var printer = new FakeIppPrinter();
        using var target = new IppDocumentTarget(printer.Uri);
        new PrintJob(FakeDocument.Uniform(1, A4)).WithAuditSink(sink).SubmitTo(target);
        Assert.Equal(new[] { "Raster", "Document" }, sink.Events.Select(e => e.Route).ToArray());
    }

    [Fact]
    public void Remote_audit_record_carries_the_route()
    {
        var e = new PrintAuditEvent { DocumentTitle = "t", Route = "PdfAsIs" };
        Assert.Equal("PdfAsIs", AuditRecord.From(e).ToEvent().Route);
        Assert.Null(AuditRecord.From(new PrintAuditEvent()).ToEvent().Route);
    }
}

public class PrintRouterTests
{
    private static readonly PageDimensions A4 = PageDimensions.FromPaper(PaperSize.A4);

    private static PrintJob Job(MemoryAuditSink sink, bool rasterizer = true)
    {
        var job = new PrintJob(FakeDocument.Uniform(2, A4)).WithTitle("Routed job").WithAuditSink(sink);
        return rasterizer ? job.WithRasterizer(new FakeRasterizer()) : job;
    }

    private static string Format(IppMessage request) => request.Group(IppTag.OperationAttributes)!["document-format"]!.Value.AsString();

    [Fact]
    public void Unknown_fonts_fall_back_to_raster_and_the_fallback_is_audited()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        var sink = new MemoryAuditSink();
        PrintRouteResult result = router.Print(Job(sink));

        Assert.True(result.Result.Status == PrintJobStatus.Printed, result.Result.Error);
        Assert.Equal(PrintRouteKind.Raster, result.Route);
        Assert.True(result.IsFallback);
        Assert.Equal(IppPrinterTarget.PwgRasterFormat, Format(Assert.Single(printer.PrintJobs).Request));
        PrintAuditEvent audit = Assert.Single(sink.Events);
        Assert.Equal("Raster (fallback: PdfAsIs: fonts not known to be embedded)", audit.Route);
    }

    [Fact]
    public void Embedded_fonts_send_the_pdf_as_is()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        var sink = new MemoryAuditSink();
        PrintJob job = Job(sink).WithFontsEmbedded();
        PrintRouteResult result = router.Print(job);

        Assert.True(result.Result.Status == PrintJobStatus.Printed, result.Result.Error);
        Assert.Equal(PrintRouteKind.PdfAsIs, result.Route);
        Assert.False(result.IsFallback);
        var (request, document) = Assert.Single(printer.PrintJobs);
        Assert.Equal("application/pdf", Format(request));
        Assert.Equal(job.PdfBytes, document);
        Assert.Equal("PdfAsIs", Assert.Single(sink.Events).Route);
        Assert.Null(job.RouteLabel); // reset after the routed print
    }

    [Fact]
    public void Fonts_not_all_embedded_are_never_sent_as_is()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        var sink = new MemoryAuditSink();
        PrintRouteResult result = router.Print(Job(sink).WithFontsEmbedded(false));
        Assert.Equal(PrintRouteKind.Raster, result.Route);
        Assert.Equal("Raster (fallback: PdfAsIs: not every font is embedded)", sink.Events[0].Route);
    }

    [Fact]
    public void Missing_appearances_keep_the_pdf_from_going_as_is()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        var sink = new MemoryAuditSink();
        PrintJob job = Job(sink);
        job.Options.DocumentFonts = FontEmbedding.NeedsGeneratedAppearances;
        PrintRouteResult result = router.Print(job);
        Assert.Equal(PrintRouteKind.Raster, result.Route);
        Assert.Equal(FontEmbedding.NeedsGeneratedAppearances, result.Decision.Fonts);
        Assert.Equal("Raster (fallback: PdfAsIs: form fields or comments need appearances generated)", sink.Events[0].Route);
    }

    [Fact]
    public void An_inspector_reporting_missing_appearances_keeps_the_pdf_from_going_as_is()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        router.FontInspector = new FixedInspector(FontEmbedding.NeedsGeneratedAppearances);
        PrintRouteResult result = router.Print(Job(new MemoryAuditSink()));
        Assert.Equal(PrintRouteKind.Raster, result.Route);
        Assert.Equal(3, (int)FontEmbedding.NeedsGeneratedAppearances); // a new value; the existing three keep theirs
        Assert.Equal((0, 1, 2), ((int)FontEmbedding.Unknown, (int)FontEmbedding.AllEmbedded, (int)FontEmbedding.NotAllEmbedded));
    }

    [Fact]
    public void Only_an_explicit_application_pdf_counts()
    {
        using var printer = new FakeIppPrinter(a => a.Set("document-format-supported",
            IppValue.MimeMediaType("image/pwg-raster"), IppValue.MimeMediaType("application/octet-stream")));
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        var sink = new MemoryAuditSink();
        PrintRouteResult result = router.Print(Job(sink).WithFontsEmbedded());
        Assert.Equal(PrintRouteKind.Raster, result.Route);
        Assert.Equal("Raster (fallback: PdfAsIs: the printer does not list application/pdf)", sink.Events[0].Route);
    }

    [Fact]
    public void A_font_inspector_answers_when_the_job_does_not_say()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        router.FontInspector = new FixedInspector(FontEmbedding.AllEmbedded);
        PrintRouteResult result = router.Print(Job(new MemoryAuditSink()));
        Assert.Equal(PrintRouteKind.PdfAsIs, result.Route);
        Assert.Equal(FontEmbedding.AllEmbedded, result.Decision.Fonts);

        // The job's own statement wins over the inspector.
        PrintRouteResult marked = router.Print(Job(new MemoryAuditSink()).WithFontsEmbedded(false));
        Assert.Equal(PrintRouteKind.Raster, marked.Route);
    }

    [Fact]
    public void No_usable_route_fails_with_one_audit_event_and_every_reason()
    {
        using var printer = new FakeIppPrinter(a => a.Set("printer-is-accepting-jobs", IppValue.Boolean(false)));
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        var sink = new MemoryAuditSink();
        PrintRouteResult result = router.Print(Job(sink).WithFontsEmbedded());

        Assert.Equal(PrintJobStatus.Failed, result.Result.Status);
        Assert.Null(result.Route);
        Assert.Equal("No print route can print this job: PdfAsIs: the printer is not accepting jobs; Raster: the printer is not accepting jobs.",
                     result.Result.Error);
        PrintAuditEvent audit = Assert.Single(sink.Events);
        Assert.Equal(PrintJobStatus.Failed, audit.Status);
        Assert.Equal("None (PdfAsIs: the printer is not accepting jobs; Raster: the printer is not accepting jobs)", audit.Route);
        Assert.Equal(printer.Uri, audit.PrinterName);
        Assert.Empty(printer.PrintJobs);

        var throwing = Job(new MemoryAuditSink()).WithThrowOnFailure();
        Assert.Throws<InvalidOperationException>(() => router.Print(throwing));
    }

    [Fact]
    public void Without_a_rasteriser_both_reasons_are_listed()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        PrintRouteDecision decision = router.Choose(Job(new MemoryAuditSink(), rasterizer: false));
        Assert.Null(decision.Route);
        Assert.Equal("None (PdfAsIs: fonts not known to be embedded; Raster: no rasteriser is configured)", decision.AuditText);
        Assert.Empty(printer.PrintJobs); // Choose never prints
    }

    [Fact]
    public void Per_job_and_site_overrides()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);

        // Per job: raster even though the PDF could go as-is.
        Assert.Equal(PrintRouteKind.Raster, router.Print(Job(new MemoryAuditSink()).WithFontsEmbedded(), PrintRouteKind.Raster).Route);

        // Site: PdfAsIs disabled.
        router.DisabledRoutes.Add(PrintRouteKind.PdfAsIs);
        var sink = new MemoryAuditSink();
        Assert.Equal(PrintRouteKind.Raster, router.Print(Job(sink).WithFontsEmbedded()).Route);
        Assert.Equal("Raster", sink.Events[0].Route); // a disabled route is not a fallback

        // A per-job force wins over the site's disabled list.
        Assert.Equal(PrintRouteKind.PdfAsIs, router.Print(Job(new MemoryAuditSink()).WithFontsEmbedded(), PrintRouteKind.PdfAsIs).Route);

        // Site force to a kind that cannot print this job.
        router.DisabledRoutes.Clear();
        router.ForcedRoute = PrintRouteKind.PdfAsIs;
        PrintRouteResult forced = router.Print(Job(new MemoryAuditSink()));
        Assert.Null(forced.Route);
        Assert.Contains("PdfAsIs: fonts not known to be embedded", forced.Result.Error);

        // Forcing a kind with no route at all.
        PrintRouteDecision none = router.Choose(Job(new MemoryAuditSink()), PrintRouteKind.Cups);
        Assert.Equal("None (Cups: no Cups route is configured)", none.AuditText);
    }

    [Fact]
    public void Routes_are_tried_in_d96_order_whatever_order_they_are_given_in()
    {
        var raster = new RasterRoute(new ImageFileTarget(Path.GetTempPath()));
        var pdf = new StubRoute(PrintRouteKind.PdfAsIs, "a");
        var cups = new StubRoute(PrintRouteKind.Cups, "b");
        var router = new PrintRouter(raster, cups, pdf);
        Assert.Equal(new[] { PrintRouteKind.PdfAsIs, PrintRouteKind.Cups, PrintRouteKind.Raster },
                     router.Routes.Select(r => r.Kind).ToArray());
    }

    [Fact]
    public void A_route_whose_check_throws_is_skipped_with_the_message()
    {
        var router = new PrintRouter(new ThrowingRoute(), new RasterRoute(new ImageFileTarget(Path.Combine(Path.GetTempPath(), "chuvadi-route-" + Guid.NewGuid().ToString("N")))));
        var sink = new MemoryAuditSink();
        PrintRouteResult result = router.Print(Job(sink));
        Assert.Equal(PrintRouteKind.Raster, result.Route);
        Assert.Equal("Raster (fallback: PdfAsIs: printer unreachable)", sink.Events[0].Route);
    }

    [Fact]
    public void Unreachable_ipp_printer_is_a_skip_reason_not_a_crash()
    {
        // Closed (a connection is refused at once), and no fake in this test run is given it meanwhile.
        using ReservedPort closed = LoopbackPorts.ReserveClosedTcpPort();
        int port = closed.Port;
        using PrintRouter router = IppRoutes.CreateRouter("ipp://127.0.0.1:" + port + "/ipp/print", new IppClientOptions { Timeout = TimeSpan.FromSeconds(5) });
        PrintRouteResult result = router.Print(Job(new MemoryAuditSink()).WithFontsEmbedded());
        Assert.Equal(PrintJobStatus.Failed, result.Result.Status);
        Assert.Contains("Could not reach the printer", result.Result.Error);
    }

    [Fact]
    public void Cups_router_uses_the_cups_kind()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateCupsRouter(printer.Uri);
        var sink = new MemoryAuditSink();
        PrintRouteResult result = router.Print(Job(sink).WithFontsEmbedded());
        Assert.Equal(PrintRouteKind.Cups, result.Route);
        Assert.Equal("Cups", sink.Events[0].Route);
        Assert.Equal("Raster (fallback: Cups: fonts not known to be embedded)", router.Choose(Job(sink)).AuditText);
    }

    [Fact]
    public void Cancellation_during_the_checks_is_audited_once()
    {
        using var printer = new FakeIppPrinter();
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
        var sink = new MemoryAuditSink();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        PrintRouteResult result = router.Print(Job(sink), null, cts.Token);
        Assert.Equal(PrintJobStatus.Cancelled, result.Result.Status);
        Assert.Equal(PrintJobStatus.Cancelled, Assert.Single(sink.Events).Status);
        Assert.Equal("None", sink.Events[0].Route);
    }

    private sealed class FixedInspector : IPdfFontInspector
    {
        private readonly FontEmbedding _answer;
        public FixedInspector(FontEmbedding answer) => _answer = answer;
        public FontEmbedding Inspect(ReadOnlyMemory<byte> pdf) => _answer;
    }

    private sealed class StubRoute : IPrintRoute
    {
        public StubRoute(PrintRouteKind kind, string name) { Kind = kind; Name = name; }
        public PrintRouteKind Kind { get; }
        public string Name { get; }
        public PrintRouteCheck Check(PrintRouteContext context, CancellationToken cancellationToken) => PrintRouteCheck.Skip("stub");
        public PrintResult Print(PrintJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingRoute : IPrintRoute
    {
        public PrintRouteKind Kind => PrintRouteKind.PdfAsIs;
        public string Name => "throws";
        public PrintRouteCheck Check(PrintRouteContext context, CancellationToken cancellationToken) => throw new IppException("printer\nunreachable");
        public PrintResult Print(PrintJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

public class IppCapabilityCacheTests
{
    private static readonly PageDimensions A4 = PageDimensions.FromPaper(PaperSize.A4);

    private static int AttributeReads(FakeIppPrinter printer)
        => printer.Requests.Count(r => r.Request.Operation == IppOperation.GetPrinterAttributes);

    [Fact]
    public void Capabilities_are_reused_for_ten_minutes_then_read_again()
    {
        using var printer = new FakeIppPrinter();
        var clock = new ManualTime();
        var cache = new IppCapabilityCache(null, clock);
        Assert.Equal(TimeSpan.FromMinutes(10), cache.RefreshAfter);
        using var client = new IppClient(printer.Uri);

        cache.Get(client);
        cache.Get(client);
        Assert.Equal(1, AttributeReads(printer));
        Assert.True(cache.TryGetCached(printer.Uri, out IppPrinterCapabilities? caps, out DateTimeOffset readAt));
        Assert.NotNull(caps);
        Assert.Equal(clock.Now, readAt);

        clock.Now += TimeSpan.FromMinutes(9.9);
        cache.Get(client);
        Assert.Equal(1, AttributeReads(printer));

        clock.Now += TimeSpan.FromMinutes(0.1);
        cache.Get(client);
        Assert.Equal(2, AttributeReads(printer));

        cache.Invalidate(printer.Uri);
        Assert.False(cache.TryGetCached(printer.Uri, out _, out _));
    }

    [Fact]
    public void A_failed_job_makes_the_next_job_read_the_printer_again()
    {
        using var printer = new FakeIppPrinter();
        var cache = new IppCapabilityCache(null, new ManualTime());
        using PrintRouter router = IppRoutes.CreateRouter(printer.Uri, null, cache);
        PrintJob Job() => new PrintJob(FakeDocument.Uniform(1, A4)).WithFontsEmbedded();

        router.Print(Job());
        router.Print(Job());
        // One read by the cache; IppDocumentTarget still reads the printer itself for each job (unchanged, D-132 item 4).
        Assert.Equal(1 + 2, AttributeReads(printer));

        printer.FailNextJobWith = IppStatusCode.ServerErrorBusy;
        Assert.Equal(PrintJobStatus.Failed, router.Print(Job()).Result.Status);
        Assert.False(cache.TryGetCached(printer.Uri, out _, out _));
        router.Print(Job());
        Assert.Equal(1 + 2 + 1 + 1 + 1, AttributeReads(printer));
    }

    [Fact]
    public void A_failed_read_is_not_cached()
    {
        // Closed (a connection is refused at once), and no fake in this test run is given it meanwhile.
        using ReservedPort closed = LoopbackPorts.ReserveClosedTcpPort();
        int port = closed.Port;
        var cache = new IppCapabilityCache();
        using var client = new IppClient("ipp://127.0.0.1:" + port + "/ipp/print", new IppClientOptions { Timeout = TimeSpan.FromSeconds(5) });
        Assert.Throws<IppException>(() => cache.Get(client));
        Assert.False(cache.TryGetCached(client.PrinterUri, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IppCapabilityCache(TimeSpan.FromSeconds(-1)));
    }
}

public class RawTargetTests
{
    /// <summary>A loopback "printer" that reads one connection to the end and keeps the bytes.</summary>
    private sealed class RawPrinter : IDisposable
    {
        /// <summary>How long a printer made with <c>closeAfterReading: false</c> keeps the connection open after the data ends.</summary>
        public static readonly TimeSpan Hold = TimeSpan.FromSeconds(10);

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task<byte[]> _received;
        private readonly TaskCompletionSource<long> _dataEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RawPrinter(bool closeAfterReading = true)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _received = Task.Run(() =>
            {
                using Socket socket = _listener.AcceptSocket();
                var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int read;
                while ((read = socket.Receive(chunk)) > 0) buffer.Write(chunk, 0, read);
                _dataEnded.TrySetResult(System.Diagnostics.Stopwatch.GetTimestamp());
                if (!closeAfterReading) Thread.Sleep(Hold);
                socket.Shutdown(SocketShutdown.Both);
                return buffer.ToArray();
            });
        }

        public int Port { get; }
        public byte[] Received => _received.Wait(TimeSpan.FromSeconds(20)) ? _received.Result : throw new TimeoutException();

        /// <summary>
        /// When the printer saw the end of the data (<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>): the
        /// target's <c>Shutdown(Send)</c>, made just before it starts draining.
        /// </summary>
        public long DataEnded => _dataEnded.Task.Wait(TimeSpan.FromSeconds(20)) ? _dataEnded.Task.Result : throw new TimeoutException();

        public void Dispose() => _listener.Stop();
    }

    [Fact]
    public void Socket_target_sends_the_bytes_unchanged()
    {
        byte[] data = new byte[300_000];
        new Random(9100).NextBytes(data);
        using var printer = new RawPrinter();
        var target = new RawSocketTarget("127.0.0.1", printer.Port);
        Assert.Equal("socket://127.0.0.1:" + printer.Port, target.Name);
        target.Send(data, "zpl labels", CancellationToken.None);
        Assert.Equal(data, printer.Received);
        Assert.Equal(data.Length, target.LastBytesSent);
    }

    [Fact]
    public void Socket_target_does_not_wait_longer_than_the_drain_timeout()
    {
        // The printer keeps the connection open for RawPrinter.Hold (10 s) after the data ends, so Send can only
        // return sooner because its 300 ms drain timeout ran out. The time is measured from when the drain starts
        // (the printer sees the end of the data), not from before connecting, so a slow, loaded machine does not count
        // connecting and sending against the drain (D-320, as D-294). Load can only make the printer's timestamp
        // later, which shortens the measured time, so it cannot fail this test falsely.
        using var printer = new RawPrinter(closeAfterReading: false);
        var target = new RawSocketTarget("127.0.0.1", printer.Port) { DrainTimeout = TimeSpan.FromMilliseconds(300) };
        target.Send(Encoding.ASCII.GetBytes("^XA^FDTest^FS^XZ"), null, CancellationToken.None);
        long returned = System.Diagnostics.Stopwatch.GetTimestamp();
        TimeSpan sinceDrainStarted = System.Diagnostics.Stopwatch.GetElapsedTime(printer.DataEnded, returned);
        Assert.True(sinceDrainStarted < TimeSpan.FromSeconds(5), "Send returned " + sinceDrainStarted + " after the drain started.");
    }

    [Fact]
    public void Submit_through_a_job_sends_the_pdf_and_audits_it()
    {
        using var printer = new RawPrinter();
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% raw\n%%EOF\n");
        var sink = new MemoryAuditSink();
        PrintResult result = new PrintJob(pdf).WithAuditSink(sink).SubmitTo(new RawSocketTarget("127.0.0.1", printer.Port));
        Assert.True(result.Status == PrintJobStatus.Printed, result.Error);
        Assert.Equal(pdf, printer.Received);
        Assert.Equal("Document", sink.Events[0].Route);
        Assert.Equal("socket://127.0.0.1:" + printer.Port, sink.Events[0].PrinterName);
    }

    [Fact]
    public void Socket_uris_and_arguments_are_checked()
    {
        RawSocketTarget t = RawSocketTarget.FromUri("socket://printer.local");
        Assert.Equal(("printer.local", 9100), (t.Host, t.Port));
        Assert.Equal(9101, RawSocketTarget.FromUri("socket://10.0.0.5:9101").Port);
        Assert.Throws<ArgumentException>(() => RawSocketTarget.FromUri("ipp://printer.local"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RawSocketTarget("h", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RawSocketTarget("h") { ConnectTimeout = TimeSpan.Zero });
    }

    [Fact]
    public void Refused_connection_is_an_io_exception()
    {
        // Closed (a connection is refused at once), and no fake in this test run is given it meanwhile.
        using ReservedPort closed = LoopbackPorts.ReserveClosedTcpPort();
        int port = closed.Port;
        var target = new RawSocketTarget("127.0.0.1", port);
        IOException ex = Assert.Throws<IOException>(() => target.Send(new byte[] { 1, 2, 3 }, null, CancellationToken.None));
        Assert.Contains("socket://127.0.0.1:" + port, ex.Message);
    }

#pragma warning disable CA1416 // checks the non-Windows behaviour too
    [Fact]
    public void Windows_raw_target_needs_windows_or_an_existing_queue()
    {
        var target = new WindowsRawPrinterTarget("Chuvadi no such queue " + Guid.NewGuid().ToString("N"));
        Assert.Equal("RAW", target.DataType);
        Assert.Throws<ArgumentException>(() => target.DataType = " ");
        if (OperatingSystem.IsWindows())
            Assert.Throws<PrinterNotFoundException>(() => target.Send(new byte[] { 1 }, null, CancellationToken.None));
        else
            Assert.Throws<PlatformNotSupportedException>(() => target.Send(new byte[] { 1 }, null, CancellationToken.None));
    }
#pragma warning restore CA1416
}

public class IppPinningTests
{
    /// <summary>
    /// A minimal IPPS printer on loopback: TLS with a self-signed certificate, answers Get-Printer-Attributes over
    /// HTTP/1.1, one request per connection.
    /// </summary>
    private sealed class TlsIppPrinter : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _certificate;
        private readonly Thread _thread;

        public TlsIppPrinter(X509Certificate2 certificate)
        {
            _certificate = certificate;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _thread = new Thread(Serve) { IsBackground = true };
            _thread.Start();
        }

        public int Port { get; }
        public string Uri => "ipps://localhost:" + Port + "/ipp/print";

        private void Serve()
        {
            while (true)
            {
                Socket socket;
                try { socket = _listener.AcceptSocket(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                ThreadPool.QueueUserWorkItem(_ => Handle(socket));
            }
        }

        private void Handle(Socket socket)
        {
            try
            {
                using var network = new NetworkStream(socket, ownsSocket: true);
                using var tls = new SslStream(network);
                tls.AuthenticateAsServer(_certificate, false, false);
                var head = new List<byte>();
                while (head.Count < 4 || !(head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
                {
                    int b = tls.ReadByte();
                    if (b < 0) return;
                    head.Add((byte)b);
                }
                string headers = Encoding.ASCII.GetString(head.ToArray());
                int length = 0;
                foreach (string line in headers.Split("\r\n"))
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
                byte[] body = new byte[length];
                tls.ReadExactly(body);
                IppMessage request = IppMessage.Parse(body);
                var response = new IppMessage((short)IppStatusCode.SuccessfulOk, request.RequestId);
                IppAttributeGroup op = response.AddGroup(IppTag.OperationAttributes);
                op.Add("attributes-charset", IppValue.Charset("utf-8"));
                op.Add("attributes-natural-language", IppValue.NaturalLanguage("en"));
                IppAttributeGroup printer = response.AddGroup(IppTag.PrinterAttributes);
                foreach (IppAttribute a in FakeIppPrinter.DefaultAttributes(Uri).Attributes) printer.Add(a);
                byte[] payload = response.ToArray();
                tls.Write(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/ipp\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n"));
                tls.Write(payload);
                tls.Flush();
            }
            catch (Exception)
            {
                // the client refused our certificate or went away
            }
        }

        public void Dispose() => _listener.Stop();
    }

    private static X509Certificate2 SelfSigned(string name)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        // Reload through PKCS#12 so the private key is usable by SslStream on every OS.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
    }

    private static string Fingerprint(X509Certificate2 cert) => Convert.ToHexString(SHA256.HashData(cert.RawData));

    [Fact]
    public void A_pinned_self_signed_certificate_is_trusted_and_others_are_refused()
    {
        using X509Certificate2 cert = SelfSigned("chuvadi-test-printer");
        using X509Certificate2 other = SelfSigned("someone-else");
        using var printer = new TlsIppPrinter(cert);

        // Without a pin a self-signed certificate is refused, as before.
        using (var plain = new IppClient(printer.Uri, new IppClientOptions { Timeout = TimeSpan.FromSeconds(10) }))
            Assert.Throws<IppException>(() => plain.GetPrinterAttributes());

        // The printer's own fingerprint, in colon format and lower case: trusted (although issued for another name).
        string pin = IppCertificates.Fingerprint(cert).ToLowerInvariant();
        Assert.Equal(95, pin.Length);
        var pinned = new IppClientOptions { Timeout = TimeSpan.FromSeconds(10) };
        pinned.PinnedCertificates.Add(pin);
        using (var client = new IppClient(printer.Uri, pinned))
            Assert.Equal("Chuvadi Fake IPP", new IppPrinterCapabilities(client.GetPrinterAttributes().Group(IppTag.PrinterAttributes)!).PrinterName);

        // A different pin: refused.
        var wrong = new IppClientOptions { Timeout = TimeSpan.FromSeconds(10) };
        wrong.PinnedCertificates.Add(Fingerprint(other));
        using (var client = new IppClient(printer.Uri, wrong))
            Assert.Throws<IppException>(() => client.GetPrinterAttributes());

        // AcceptAnyServerCertificate still wins over the list.
        var any = new IppClientOptions { Timeout = TimeSpan.FromSeconds(10), AcceptAnyServerCertificate = true };
        any.PinnedCertificates.Add(Fingerprint(other));
        using (var client = new IppClient(printer.Uri, any))
            client.GetPrinterAttributes();
    }

    [Fact]
    public void The_printer_certificate_can_be_read_for_pinning()
    {
        using X509Certificate2 cert = SelfSigned("chuvadi-test-printer");
        using var printer = new TlsIppPrinter(cert);
        using X509Certificate2 presented = IppCertificates.GetServerCertificate(printer.Uri, TimeSpan.FromSeconds(10));
        Assert.Equal(Fingerprint(cert), Fingerprint(presented));
        Assert.True(IppCertificates.Matches(presented, Fingerprint(cert)));
        Assert.Throws<ArgumentException>(() => IppCertificates.GetServerCertificate("ipp://localhost:" + printer.Port + "/ipp/print"));
    }

    [Fact]
    public void Malformed_fingerprints_are_rejected_when_the_client_is_created()
    {
        var options = new IppClientOptions();
        options.PinnedCertificates.Add("12:34");
        Assert.Throws<ArgumentException>(() => new IppClient("ipps://printer.local/ipp/print", options));
        options.PinnedCertificates[0] = new string('Z', 64);
        Assert.Throws<ArgumentException>(() => new IppClient("ipps://printer.local/ipp/print", options));
    }
}
