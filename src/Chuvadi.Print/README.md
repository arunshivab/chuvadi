# Chuvadi.Print

Pure-BCL printing for .NET — zero NuGet dependencies, part of the [Chuvadi](https://github.com/arunshivab/chuvadi) family.

What v1.0 gives you:

- **Device-independent print settings** with every choice exposed: page selection, copies, collation, duplex, colour, all paper sizes, all four orientations, six scale modes, nine alignments, margins, auto-rotate, silent printing.
- **`PrintJob`**: plans the job, renders one page at a time (memory does not grow with document length), lays each page out on the sheet, converts colour, and sends it to a print target. One audit event per job.
- **Spool envelope**: a checksummed binary package of PDF + settings + job metadata, for handing a job from a web host to a print agent.
- **`ImageFileTarget`**: writes each printed sheet as a PNG — print preview, archiving, and visual verification.
- **Windows printing** (`Chuvadi.Print.Windows`): list printers, read every driver paper, tray, media type and resolution, and print through the GDI spooler with full control of the driver settings. Plain P/Invoke — no native binaries, and the rest of the library still runs everywhere.
- **Driverless IPP printing** (`Chuvadi.Print.Ipp`), on any OS: send rendered pages as PWG Raster to any IPP Everywhere printer, or pass the PDF itself through to printers that accept it. Includes a complete IPP encoder/decoder, client, and PWG Raster reader/writer.
- **Remote printing** (`Chuvadi.Print.Remote`): your web application embeds a `PrintHub`; a `PrintAgent` on each branch PC dials out to it over HTTPS (no inbound port), fetches jobs, prints them on local or IPP printers and reports back. Per-agent tokens with rotation and revocation, a durable file-based job store, resumable downloads, and a journal that never prints a job twice. Still one audit event per job.

Rasterisation is pluggable through `IPageRasterizer`; the PDF renderer plugs in through the separate **Chuvadi.Print.Pdf** adapter.

```csharp
using Chuvadi.Print;

// Describe the job, with full control over layout.
PrintJob job = new PrintJob("discharge-summary.pdf")
    .WithPages(PageSelection.Range("1-4, 7"))
    .WithCopies(2, collate: true)
    .WithDuplex(Duplex.Vertical)
    .WithPaper(PaperSize.A4)
    .WithScale(ScaleMode.FitToPage)
    .WithAlignment(ContentAlignment.Center)
    .WithColor(ColorMode.Monochrome)
    .WithMonochrome(MonochromeConversion.ErrorDiffusion)
    .WithUser("dr.arun").WithTenant("apulki-pune")
    .WithRasterizer(rasterizer)                       // e.g. from Chuvadi.Print.Pdf
    .WithAuditSink(new FileAuditSink("print-audit.log"));

// Print it (here: to PNG files of each sheet).
PrintResult result = job.PrintTo(new ImageFileTarget("preview").WithDpi(150));

// Know what will print before printing it.
PrintPlan plan = job.Plan();          // pages, impressions, sheets, blank backs

// Hand the job to another machine, and pick it up there.
job.SaveSpoolTo("job.cvpr");
PrintJob received = PrintJob.LoadSpool("job.cvpr").WithRasterizer(rasterizer);
```

Printing to a Windows printer:

```csharp
using Chuvadi.Print.Windows;

foreach (PrinterInfo p in WindowsPrinters.List())
    Console.WriteLine(p.Name + (p.IsDefault ? " (default)" : ""));

WindowsPrinterCapabilities caps = WindowsPrinters.GetCapabilities("Ward-3 LaserJet", job.Settings);
var target = new WindowsPrinterTarget("Ward-3 LaserJet")
    .WithBin(caps.Bins.FirstOrDefault(b => b.Name == "Tray 2"))
    .WithQuality(PrintQuality.High);

PrintResult printed = job.PrintTo(target);          // target.LastSpoolJobId = spooler job id

// "Microsoft Print to PDF" with an output file writes a PDF, no dialog.
job.PrintTo(new WindowsPrinterTarget("Microsoft Print to PDF").WithOutputFile(@"C:\out\summary.pdf"));
```

Printing over IPP (no driver, any OS):

```csharp
using Chuvadi.Print.Ipp;

// The printer receives the PDF and lays it out itself.
using var pdfPrinter = new IppDocumentTarget("ipp://ward3-printer.local/ipp/print");
PrintResult sent = job.SubmitTo(pdfPrinter);        // pdfPrinter.LastApproximations lists anything IPP cannot express

// Or send finished pages exactly as laid out here, as PWG Raster.
using var rasterPrinter = new IppPrinterTarget("ipps://10.0.0.21/ipp/print",
                                               new IppClientOptions { AcceptAnyServerCertificate = true })
    .WithJobOptions(o => { o.MediaSource = "tray-2"; o.Quality = IppPrintQuality.High; });
job.PrintTo(rasterPrinter);
```

Remote printing: the hub inside your web application (here ASP.NET Core; any web stack works)…

```csharp
using Chuvadi.Print.Remote;

var tokens = new TokenAgentAuthenticator(new FileAgentTokenStore(@"D:\lipi\print\agents.json"));
var hub = new PrintHub(new FileJobStore(@"D:\lipi\print\jobs"), tokens, new FileAuditSink(@"D:\lipi\print\audit.log"));

string token = tokens.Issue("ward3-pc");      // give it to the agent's installer, once

app.Map("/print-hub/{**path}", async (HttpContext http, string? path) =>
{
    var request = new HubRequest(http.Request.Method, path ?? "");
    foreach (var h in http.Request.Headers) request.Headers[h.Key] = h.Value.ToString();
    using (var body = new MemoryStream()) { await http.Request.Body.CopyToAsync(body); request.Body = body.ToArray(); }

    using HubResponse response = await hub.HandleAsync(request, http.RequestAborted);
    http.Response.StatusCode = response.StatusCode;
    foreach (var h in response.Headers) http.Response.Headers[h.Key] = h.Value;
    if (response.ContentType is not null) http.Response.ContentType = response.ContentType;
    http.Response.ContentLength = response.ContentLength;
    await response.CopyBodyToAsync(http.Response.Body, http.RequestAborted);
});

hub.Submit("ward3-pc", "Ward-3 LaserJet", job);     // idempotent per job.JobId
hub.JobFinished += (_, e) => Console.WriteLine(e.Job.JobId + " " + e.Job.State);
```

…and the agent next to the printers (for example inside a Windows service):

```csharp
using var agent = new PrintAgent(new PrintAgentOptions
{
    HubUri = new Uri("https://lipi.example.com/print-hub/"),
    AgentId = "ward3-pc",
    Credentials = new FileAgentCredentialStore(@"C:\ProgramData\Chuvadi\Agent\token.txt"),
    JournalFolder = @"C:\ProgramData\Chuvadi\Agent\journal",
    PrinterResolver = name => new WindowsPrinterTarget(name),
    PrinterListProvider = () => WindowsPrinters.List().Select(p => p.Name).ToList(),
    Rasterizer = rasterizer,                        // e.g. from Chuvadi.Print.Pdf
    LocalAuditSink = new FileAuditSink(@"C:\ProgramData\Chuvadi\Agent\audit.log")
});
await agent.RunAsync(stoppingToken);              // reconnects on its own
```

Full guide: [docs/Chuvadi.Print/GUIDE.md](https://github.com/arunshivab/chuvadi/blob/main/docs/Chuvadi.Print/GUIDE.md).

MIT licensed.
