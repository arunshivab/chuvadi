# Chuvadi.Print 1.0 — Guide

Chuvadi.Print is a pure-BCL printing library for .NET 10 (no NuGet dependencies). It takes a PDF and the
settings for printing it. It works out exactly what goes on every sheet and delivers that to a printer, a file
or a remote agent, recording one audit event per job.

| Layer | Namespace / package | What it gives you | Since |
|---|---|---|---|
| A1 | `Chuvadi.Print` | Settings, paper sizes, margins, page selection, alignment, spool envelope, audit | 0.1.0 |
| A2 | `Chuvadi.Print` | `PrintJob`, `IPageRasterizer`, page layout, print plan, colour conversion, PNG preview | 0.2.0 |
| A3 | `Chuvadi.Print.Windows` | Windows printers through the GDI spooler (P/Invoke only) | 0.3.0 |
| A4 | `Chuvadi.Print.Ipp` | Driverless IPP printing on any OS: PWG Raster or PDF passthrough | 0.4.0 |
| A6 | `Chuvadi.Print.Remote` | An embeddable print hub and dial-out print agents | 0.5.0 |
| A5 | package **Chuvadi.Print.Pdf** (`staging/`) | PDF rendering (`PdfPageRasterizer`) and print-to-PDF (`PdfFileTarget`) over Chuvadi.Pdf | 0.1.0 |
| Product | `products/ChuvadiPdfPrinter` | The "Chuvadi PDF" virtual printer for every Windows application | 0.2.0 |

The architectural invariants (P01–P09) are in [BASELINE.md](BASELINE.md). How Docs/Sheets reports get printed is
in [PRINTING-REPORTS.md](PRINTING-REPORTS.md).

---

## 1. Settings (A1)

`PrintSettings` describes the job independently of any device. Every setting has its complete set of choices.

| Setting | Values (default first) |
|---|---|
| `Pages` | `PageSelection.All`, `None`, `Odd`, `Even`, `Current(n)`, `First(n)`, `Last(n)`, `Explicit(1, 4, 7)`, `Range("1-4, 7")` |
| `Copies`, `Collate` | 1, true |
| `Duplex` | `Simplex`, `Vertical` (long edge), `Horizontal` (short edge) |
| `Color` | `Color`, `Grayscale`, `Monochrome` |
| `Paper` | `PaperSize.A4`; A0–A6, B4, B5, Letter, Legal, Tabloid, Ledger, Executive, Statement, `FromMillimetres`, `FromInches`, `Custom` |
| `Orientation` | `Portrait`, `Landscape`, `ReversePortrait`, `ReverseLandscape` |
| `Scale` | `FitToPage`, `ActualSize`, `ShrinkToFit`, `FitToWidth`, `FitToHeight`, `Custom` (+ `CustomScalePercent`) |
| `Alignment` | `ContentAlignment.Center` and the other eight (`TopLeft` … `BottomRight`) |
| `Margins` | `Margins.Default` (0.5 in); `None`, `Narrow`, `Wide`, `Uniform`, `FromMillimetres`, `FromInches` |
| `AutoRotate` | false: when true, a page whose orientation differs from the printable area is turned 90° |
| `Silent` | false: no dialogs |

`PrintSettings.Clone()` gives an independent copy.

## 2. PrintJob (A2)

```csharp
PrintJob job = new PrintJob("report.pdf")          // or bytes, a stream, PrintJob.LoadSpool(...)
    .WithPages(PageSelection.Range("1-3"))
    .WithCopies(2, collate: true).WithDuplex(Duplex.Vertical)
    .WithPaper(PaperSize.A4).WithScale(ScaleMode.FitToPage).WithAlignment(ContentAlignment.Center)
    .WithColor(ColorMode.Monochrome).WithMonochrome(MonochromeConversion.ErrorDiffusion)
    .WithTitle("Discharge summary").WithUser("dr.arun").WithTenant("apulki")
    .WithRasterizer(new PdfPageRasterizer())          // Chuvadi.Print.Pdf
    .WithAuditSink(new FileAuditSink("print-audit.log"));

PrintPlan plan = job.Plan();                         // what will print, before printing
PrintResult result = job.PrintTo(target);            // rendered pages to an IPrintTarget
PrintResult sent = job.SubmitTo(documentTarget);     // the PDF itself to an IDocumentTarget
```

- **Every setting has a fluent `With*` method.** Job options cover the rest: `WithRenderDpi`,
  `WithCopyHandling` (`Auto`, `Target`, `Software`), `WithMonochrome` / `WithMonochromeThreshold`
  (`Threshold`, `OrderedDither`, `ErrorDiffusion`), `WithMarginReference`, `WithAnnotations`, `WithFormFields`,
  `WithAntiAlias`, `WithProgress`, `WithThrowOnFailure`, `WithJobId`.
- **Failures and cancellation are reported in the `PrintResult`** (`Status`, `PagesPrinted`,
  `ImpressionsPrinted`, `SheetsPrinted`, `Error`). They are only thrown when `ThrowOnFailure` is set.
- **One audit event per job** (P06), whatever the outcome. `FileAuditSink` writes one tab-delimited line
  per job; implement `IPrintAuditSink` for anything else.
- **Bounded memory** (P05): one page is rendered at a time, at the device's resolution, and released before
  the next.

### Layout and plan

- `PageLayout.Compute(page, settings, capabilities, marginReference)` returns a `PagePlacement`: the sheet,
  the effective margins, the printable area, the content bounds, the scale, the rotation and whether the
  content is clipped.
- `MarginReference` decides how the job's margins combine with the device's hard margins:
  - `SheetEdge`: the larger of the two.
  - `PrintableArea`: the requested margin added to the hard margin.
- `PrintPlan.Create(pageCount, settings, targetHandlesCopies)` expands the page selection, copies and
  collation into impressions. It pairs duplex sides onto sheets and inserts the blank backs each copy needs,
  so every copy starts on a fresh sheet. There is no blank back after the last sheet.

### Targets

| Target | Kind | Notes |
|---|---|---|
| `ImageFileTarget(folder)` | raster | One PNG per sheet side. `WithDpi`, `WithHardMargins`, `WithGuides`, `WithFileNamePattern`. For preview and visual checks. |
| `WindowsPrinterTarget(name)` | raster | A3, see §3. |
| `IppPrinterTarget(uri)` | raster | A4, PWG Raster. |
| `IppDocumentTarget(uri)` | document | A4, PDF passthrough. |
| `PdfFileTarget(path or stream)` | document | A5, vector print-to-PDF. |

Your own target implements `IPrintTarget` (raster pages) or `IDocumentTarget` (the PDF itself).

### Spool envelope

`job.SaveSpoolTo(path)` / `ToSpoolArray()` and `PrintJob.LoadSpool(...)` carry the PDF, the settings and
the job metadata (JobId, title, user, tenant) in one checksummed package (format v2; v1 still reads). It is
how a job travels to another machine (P04: the PDF travels, never bitmaps).

## 3. Windows printers (A3, `Chuvadi.Print.Windows`)

```csharp
foreach (PrinterInfo p in WindowsPrinters.List()) Console.WriteLine(p.Name + (p.IsDefault ? " (default)" : ""));
WindowsPrinterCapabilities caps = WindowsPrinters.GetCapabilities("Ward-3 LaserJet", job.Settings);

job.PrintTo(new WindowsPrinterTarget("Ward-3 LaserJet")
    .WithBin(caps.Bins.FirstOrDefault(b => b.Name == "Tray 2"))
    .WithQuality(PrintQuality.High)
    .WithMaxRasterDpi(300));

job.PrintTo(new WindowsPrinterTarget("Microsoft Print to PDF").WithOutputFile(@"C:\out\job.pdf"));   // no dialog
```

- **Discovery**: `List`, `Get`, `Exists`, `DefaultPrinterName`, `SetDefault`.
- **Capabilities**: every driver paper, tray, media type and resolution, plus copies, collate, duplex,
  colour and device geometry.
- **Driver settings**: the settings are mapped onto the driver's DEVMODE. The target adds `WithBin`,
  `WithMediaType`, `WithResolution`, `WithQuality`, `WithPaperMatching` (`ExactOrCustom`, `ExactOnly`,
  `CustomOnly`) and `WithStrictCapabilities`.
- **Drawing**: pages are drawn with `StretchDIBits` in bands of about 4 MB.
- **Platform**: Windows only. The code is marked `[SupportedOSPlatform("windows")]` and throws
  `PlatformNotSupportedException` elsewhere (P08).

## 4. IPP printers (A4, `Chuvadi.Print.Ipp`)

```csharp
using var pdfPrinter = new IppDocumentTarget("ipp://ward3-printer.local/ipp/print");
job.SubmitTo(pdfPrinter);                                  // the printer lays out the PDF

using var raster = new IppPrinterTarget("ipps://10.0.0.21/ipp/print", new IppClientOptions { AcceptAnyServerCertificate = true })
    .WithJobOptions(o => { o.MediaSource = "tray-2"; o.Quality = IppPrintQuality.High; });
job.PrintTo(raster);                                       // PWG Raster, laid out here
```

- **Building blocks**: a complete RFC 8010 encoder and decoder (`IppMessage`), `IppClient` (every common
  operation, plus raw `Send`), `IppPrinterCapabilities`, and a PWG Raster writer and reader.
- **Job ticket mapping**: settings IPP cannot express are listed in `LastApproximations`, or fail the job
  when `IppJobOptions.Strict` is set.

## 5. PDF rendering and print-to-PDF (A5, package Chuvadi.Print.Pdf)

- **`PdfPageRasterizer`** is the `IPageRasterizer` for PDFs. The page's `/Rotate` is applied, and pages are
  rendered at the resolution and pixel format the job asks for. Annotations and form fields are printed by
  flattening them first. All the renderer's options are exposed: `SuperSample`, `GammaCorrect`, `Hinting`,
  `FlatnessTolerance`, `IgnoreOptionalContent`, `LayerVisibility`, `Password` and `MaxRenderDpi`.
- **`PdfFileTarget`** writes the printed sheet sides as a new PDF, placing the original pages as vectors.
  Layout, plan, page selection, copies, collation and duplex blank backs all apply.
  - Options: `IncludeAnnotations`, `IncludeFormFields`, `MarginReference`, `HardMargins`,
    `IncludeBlankBacks`, `ClipToPrintableArea`, `Password` and `Encryption`.
  - Results: `LastPlan`, `LastPageCount`, `LastOutputPath`, `LastApproximations` (colour mode and duplex
    binding).
  - Output goes through a temporary file, so a failed job leaves no half-written PDF.
- **Building**: it lives in `staging/` and consumes the version-pinned Chuvadi.Pdf packages (3.22.1) from
  the local feed set in `staging/nuget.config`, never a source clone (D-29). It is outside `Chuvadi.slnx` and
  CI until Chuvadi.Pdf joins the monorepo or nuget.org (D-28). Building and packing are described in its README.

## 6. Remote printing (A6, `Chuvadi.Print.Remote`)

Branch PCs run a **PrintAgent** that dials out over HTTPS to a **PrintHub** embedded in the host application
(LiPi Connect). Nothing listens on the branch PC (P07).

**Hub (host side)**

```csharp
var tokens = new TokenAgentAuthenticator(new FileAgentTokenStore(@"D:\lipi\print\agents.json"));
var hub = new PrintHub(new FileJobStore(@"D:\lipi\print\jobs"), tokens, new FileAuditSink(@"D:\lipi\print\audit.log"),
                       new PrintHubOptions { LeaseSeconds = 120, MaxAttempts = 3 });
string token = tokens.Issue("ward3-pc");                    // give it to the agent's installer once
StoredJob queued = hub.Submit("ward3-pc", "Ward-3 LaserJet", job);   // idempotent per job.JobId
hub.JobFinished += (_, e) => Notify(e.Job.JobId, e.Job.State);
```

- **Mounting**: map `"/print-hub/{**path}"` in your web stack and forward each request to
  `hub.HandleAsync(new HubRequest(method, path) { … })`; the README has the full ASP.NET Core snippet.
  `PrintHubMessageHandler` runs hub and agent in one process.
- **Host API**: `GetJob`, `ListJobs`, `ListAgents`, `Cancel` (immediate when queued; reaches a printing
  agent within a third of the lease), `ExpireLeases`, and the events `JobFinished` and `BackgroundError`.
- **Job store**: `FileJobStore` is durable. It writes with atomic renames and recovers after a restart.
  `InMemoryJobStore` is for tests, and you can add a database store by implementing `IPrintJobStore`.
- **Tokens**: only SHA-256 hashes are stored. `BeginRotation` hands the agent a new token on its next
  request, and the old token keeps working until the new one is first used. `Revoke` is immediate.

**Agent (branch side)**

```csharp
using var agent = new PrintAgent(new PrintAgentOptions
{
    HubUri = new Uri("https://lipi.example.com/print-hub/"), AgentId = "ward3-pc",
    Credentials = new FileAgentCredentialStore(@"C:\ProgramData\Chuvadi\Agent\token.txt"),
    JournalFolder = @"C:\ProgramData\Chuvadi\Agent\journal",
    PrinterResolver = name => new WindowsPrinterTarget(name),
    DocumentTargetResolver = name => name.StartsWith("ipp") ? new IppDocumentTarget(name) : null,
    Rasterizer = new PdfPageRasterizer(),
    LocalAuditSink = new FileAuditSink(@"C:\ProgramData\Chuvadi\Agent\audit.log")
});
await agent.RunAsync(stoppingToken);
```

- **Polling and reconnecting**: the agent long-polls and backs off exponentially when the hub cannot be
  reached.
- **Downloads**: an interrupted download resumes with `Range`, and the payload's SHA-256 is checked.
- **Leases and cancel**: heartbeats renew the lease and pick up cancel requests.
- **Never prints twice**: the crash-safe journal means a job that was interrupted is reported as Failed
  instead of being reprinted, and outcomes the hub has not acknowledged are resent.
- **HTTPS**: required, except to loopback or with `AllowInsecureHttp`.
- **Protocol v1**: `POST agents/hello`, `POST agents/poll` (204 or a lease), `GET jobs/{id}/payload`
  (Range), `POST jobs/{id}/status`. Every request carries the headers `X-Chuvadi-Protocol: 1`,
  `X-Chuvadi-Agent` and `Authorization: Bearer`.

## 7. The Chuvadi PDF virtual printer (product)

`products/ChuvadiPdfPrinter` is a separate product built on the Chuvadi.Print package (D-11, D-23); install
it with `INSTALL.md`.

- **Windows 11 24H2+**: a Print Support App adds "Chuvadi PDF" (asks where to save) and "Chuvadi PDF
  (Auto-save)".
- **Windows 10 or any Windows**: a "Chuvadi PDF" queue on Windows' own "Microsoft Print To PDF" driver,
  whose port is the agent's named pipe. Each job is its own pipe connection, so jobs never share a file.
- **Agent service**: one service in every mode. It saves the PDFs into a private folder per user, writes the
  audit trail and, optionally, forwards each PDF to physical printers:
  - IPP printers get the PDF passed through.
  - Windows printers get it rendered by Chuvadi.Print.Pdf, with `Paper` (`Auto` matches the first page),
    `Color`, `Scale` and `MaxRasterDpi`.
- **Self-test**: `scripts\selftest.ps1` prints a test page through each installed variant and checks the
  PDF and the audit trail.

## 8. Testing and verification

- **Tests**: `dotnet test Chuvadi.slnx` runs Print's unit tests and its verification groups. Groups that do
  not apply on the current OS report SKIP.
- **ManualTests runner**: `dotnet run --project samples/Chuvadi.Print.ManualTests` runs the same groups. It
  also accepts:
  - `--preview <folder>`: writes PNG sheets for visual checking.
  - `--windows-printer "<name>" [--windows-output <folder>]`: an 11-job proof set.
  - `--ipp-printer <uri> [--ipp-pdf <file>] [--ipp-insecure]`: a proof set over IPP.
- **Chuvadi.Print.Pdf**: `dotnet test staging/Chuvadi.Print.Pdf.Tests` (needs the Chuvadi.Pdf packages in the local feed).
- **The product**: `dotnet test products/ChuvadiPdfPrinter/ChuvadiPdfPrinter.sln`.
- **Windows**: everything only Windows can prove is in [WINDOWS-VERIFICATION.md](WINDOWS-VERIFICATION.md).
