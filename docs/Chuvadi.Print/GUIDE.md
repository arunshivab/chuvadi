# Chuvadi.Print 1.0 — Guide

Chuvadi.Print is a pure-BCL printing library for .NET 10 (no NuGet dependencies). It takes a PDF and the
settings for printing it. It works out exactly what goes on every sheet and delivers that to a printer, a file
or a remote agent, recording one audit event per job.

Each layer below is its own package (D-107); the namespace is the package name. Chuvadi.Print.Windows,
Chuvadi.Print.Ipp and Chuvadi.Print.Remote each need only the core, Chuvadi.Print.

| Layer | Package (= namespace) | What it gives you | Since |
|---|---|---|---|
| A1 | `Chuvadi.Print` | Settings, paper sizes, margins, page selection, alignment, spool envelope, audit | 0.1.0 |
| A2 | `Chuvadi.Print` | `PrintJob`, `IPageRasterizer`, page layout, print plan, colour conversion, PNG preview | 0.2.0 |
| A3 | `Chuvadi.Print.Windows` | Windows printers through the GDI spooler (P/Invoke only) | 0.3.0 |
| A4 | `Chuvadi.Print.Ipp` | Driverless IPP printing on any OS: PWG Raster or PDF passthrough | 0.4.0 |
| A6 | `Chuvadi.Print.Remote` | An embeddable print hub and dial-out print agents | 0.5.0 |
| A5 | package **Chuvadi.Print.Pdf.Raster** (`staging/`) | PDF rendering (`PdfPageRasterizer`) and the font check (`PdfFontInspector`) over Chuvadi.Pdf | 0.1.0 |

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
| `PrinterDefaults` | `PrinterDefault.None`; any of `Paper`, `Duplex`, `Color`, `Copies` combined with `\|` (since 1.0.0-rc.4) |

`PrintSettings.Clone()` gives an independent copy.

### Leaving settings to the printer's defaults (1.0.0-rc.4)

```csharp
job.WithPrinterDefaults(PrinterDefault.Paper | PrinterDefault.Duplex);   // the printer's own paper and sides
```

- **What it does:** a flagged setting is not sent. The IPP job ticket leaves out `media`, `sides`,
  `print-color-mode` or `copies`; the Windows DEVMODE (and the XPS PrintTicket made from it) keeps the driver's own
  value. The printer applies its default.
- **When Chuvadi.Print renders the pages itself** (`PrintTo`, the XPS target), it needs that default for the
  layout: the sheet size, the back of a two-sided sheet, grey or colour. The target reports it
  (`PrintTargetCapabilities.DefaultPaper`, `DefaultDuplex`, `DefaultColor`, `DefaultCopies`):
  - IPP reads `media-default` (or `media-col-default`), `sides-default`, `print-color-mode-default` and
    `copies-default` (`IppPrinterCapabilities.DefaultPaper` and the others). "auto" colour is not a default.
  - Windows reads the driver's default DEVMODE (`WindowsPrinterCapabilities.DefaultPaper` and the others; driver
    "monochrome" is `Grayscale`).
  - `PrintSettings.ResolvePrinterDefaults(capabilities, notes)` does the replacement, for your own targets.
- **When the printer reports no default**, the job is laid out with the value in `PrintSettings`, nothing is sent,
  and the audit says so. The job is not failed, and the approximation is not counted by `IppJobOptions.Strict`.
- **Copies:** one set is produced and the printer's own number of copies applies. Where the copies would be made by
  Chuvadi.Print rather than the printer, the flag means one copy.
- **Audit:** `PrintAuditEvent.PrinterDefaults` says what was used, for example
  `paper: printer default (Letter)` or `paper: printer default not reported; laid out for A4`. When the printer lays
  out the document itself (`SubmitTo`), it says `paper: printer default`. `FileAuditSink` writes it as a 14th column,
  only on lines for such jobs.
- **Not included:** orientation (it belongs to the document's layout) and the tray (an unset `MediaSource` or `Bin`
  already means the printer's default).
- With `PrinterDefault.None` (the default) every ticket, DEVMODE, page and audit line is as before.

## 2. PrintJob (A2)

```csharp
PrintJob job = new PrintJob("report.pdf")          // or bytes, a stream, PrintJob.LoadSpool(...)
    .WithPages(PageSelection.Range("1-3"))
    .WithCopies(2, collate: true).WithDuplex(Duplex.Vertical)
    .WithPaper(PaperSize.A4).WithScale(ScaleMode.FitToPage).WithAlignment(ContentAlignment.Center)
    .WithColor(ColorMode.Monochrome).WithMonochrome(MonochromeConversion.ErrorDiffusion)
    .WithTitle("Discharge summary").WithUser("user1").WithTenant("tenant-a")
    .WithRasterizer(new PdfPageRasterizer())          // Chuvadi.Print.Pdf.Raster
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

Your own target implements `IPrintTarget` (raster pages) or `IDocumentTarget` (the PDF itself).

### Spool envelope

`job.SaveSpoolTo(path)` / `ToSpoolArray()` and `PrintJob.LoadSpool(...)` carry the PDF, the settings and
the job metadata (JobId, title, user, tenant) in one checksummed package (format v2; v1 still reads). A job with
`PrinterDefaults` set is written as format v3 (1.0.0-rc.4), which agents on rc.3 and earlier cannot read; every
other job is still written as v2, byte for byte as before. It is
how a job travels to another machine (P04: the PDF travels, never bitmaps).

## 3. Windows printers (A3, `Chuvadi.Print.Windows`)

```csharp
foreach (PrinterInfo p in WindowsPrinters.List()) Console.WriteLine(p.Name + (p.IsDefault ? " (default)" : ""));
WindowsPrinterCapabilities caps = WindowsPrinters.GetCapabilities("Ward-3 Laser", job.Settings);

job.PrintTo(new WindowsPrinterTarget("Ward-3 Laser")
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
- **AutoRotate when the printer lays out the PDF** (`IppDocumentTarget`, and the PdfAsIs and CUPS routes, since
  1.0.0-rc.4): with `AutoRotate` set and `Orientation` portrait, the orientation is left to the printer. The ticket
  sends `orientation-requested` "none" (7) when the printer lists it, and otherwise no orientation, instead of
  portrait (3). Forcing portrait stops CUPS and printers turning landscape pages, which then print cut off or
  shrunk (D-288, checked on a CUPS driverless queue, D-289). A landscape or reversed orientation is still sent as
  set, and the raster routes are unchanged.
- **Spool**: `IppPrinterTarget` holds the PWG Raster job until it is sent as one Print-Job.
  - The default, `IppSpoolMode.TemporaryFile`, is a file in `TemporaryFolder` (or the system temp folder),
    deleted when the job ends.
  - `IppSpoolMode.Memory` keeps it in memory, for machines without a writable temp folder:
    `target.WithSpool(IppSpoolMode.Memory)`.
  - `MemorySpoolLimit` caps it (default 256 MB; `WithSpool(IppSpoolMode.Memory, limit)` sets both). A job that grows
    past the limit fails before anything is sent, with an `IppException`; it does not switch to a file.
  - The memory is released as soon as the job is sent or fails. A colour A4 page at 300 dpi is about 25 MB before
    PWG Raster's run-length coding, which usually shrinks it a great deal.

## 4a. Choosing a route, and raw delivery (Phase 1)

A `PrintRouter` decides how each job reaches one printer (D-96). It asks each route, in this order, whether it can
print the job, and uses the first that can:

1. `PdfAsIs`: the PDF unchanged over IPP. Only when every font in the PDF is embedded (D-132), the printer is
   accepting jobs, and it lists `application/pdf` itself (`application/octet-stream` does not count).
2. `PageDescription`: PostScript or PCL XL made here (later phases).
3. `Cups`: the PDF to a CUPS queue (Linux, macOS), under the same font rule.
4. `WindowsQueue`: a Windows queue with vector output, through XPS (`WindowsQueueRoute`, section 4b).
5. `Raster`: pages rendered here (PWG Raster, the GDI spooler, PNG).

```csharp
using PrintRouter router = IppRoutes.CreateRouter("ipp://ward3-printer.local/ipp/print");
PrintRouteResult r = router.Print(job.WithFontsEmbedded());   // the app made this PDF with embedded fonts
// r.Route == PdfAsIs; the audit event's Route column says "PdfAsIs".
// Without WithFontsEmbedded: Route "Raster (fallback: PdfAsIs: fonts not known to be embedded)".
```

- **Fonts**: `PrintJobOptions.DocumentFonts` (or `WithFontsEmbedded`) says what the application knows. When it is
  `Unknown`, the router asks its `FontInspector`; with none, the PDF is not sent as-is. A Chuvadi.Pdf inspector
  comes with Chuvadi.Print.Pdf.Raster. `NeedsGeneratedAppearances` means every font is embedded but some form
  fields or comments have no stored appearance; such a PDF is not sent as-is either (the audit says
  "form fields or comments need appearances generated"), because our own rendering draws those appearances
  and the printer would not.
- **Overrides**: `router.ForcedRoute` and `router.DisabledRoutes` for a site, `router.Print(job, PrintRouteKind.Raster)`
  for one job (wins over both). `router.Choose(job)` tells you the decision without printing.
- **Audit**: every job records its route as the last column of the audit line: `Raster` for `PrintTo`,
  `Document` for `SubmitTo`, or the router's choice with `(fallback: Kind: reason; ...)`. `None (...)` when no
  route can print; the job then fails with every reason. A route that fails while printing is not retried on
  another route, so nothing prints twice.
- **Capabilities**: `IppCapabilityCache` keeps each printer's attributes for 10 minutes and reads them again after
  a failed read or a failed job. Share one cache between routers for the same printers.
- **CUPS**: `IppRoutes.CreateCupsRouter("ipp://localhost:631/printers/<queue>")`.
- **Certificate pinning**: `IppClientOptions.PinnedCertificates` takes SHA-256 fingerprints; a pinned ipps://
  printer is trusted with its own self-signed certificate and any other certificate is refused.
  `IppCertificates.GetServerCertificate(uri)` and `IppCertificates.Fingerprint(cert)` read the fingerprint to pin.
- **Raw delivery** (bytes unchanged, settings not applied): `RawSocketTarget` (TCP 9100, any OS, Chuvadi.Print.Ipp)
  and `WindowsRawPrinterTarget` (a Windows queue with the RAW datatype, Chuvadi.Print.Windows). Both implement
  `IRawPrintTarget.Send(bytes, title, token)` and `IDocumentTarget` (for `SubmitTo` with an audit event).

### Saved printers, discovery and CUPS queues

```csharp
// Find printers on the local network (only when you ask; nothing listens in the background).
IReadOnlyList<DiscoveredPrinter> found = NetworkPrinterDiscovery.Browse();   // 3 seconds by default
var registry = PrinterRegistry.Load("printers.json");                         // empty when the file is missing
registry.Set(found[0].ToSavedPrinter("Ward 3 Laser"));
registry.SaveTo("printers.json");

// Later: a router for a saved printer, with its pinned certificate, forced route and saved capabilities.
registry.TryGet("Ward 3 Laser", out SavedPrinter? ward3);
var cache = new IppCapabilityCache();
using PrintRouter router = IppRoutes.CreateRouter(ward3!, null, cache);
router.Print(job);
if (cache.StoreIn(ward3!)) { registry.Set(ward3!); registry.SaveTo("printers.json"); }

// The queues of a CUPS server (Linux, macOS).
foreach (CupsQueue q in CupsQueues.List()) Console.WriteLine(q.Name + " " + q.Uri);

// The queues of the CUPS server this computer is set up to use (CUPS_SERVER or client.conf), else the local one.
IReadOnlyList<CupsQueue> configured = CupsQueues.ListConfigured();

// One list: local CUPS, the configured server, any other servers, and DNS-SD, each printer once (1.0.0-rc.4).
var options = new PrinterListingOptions();
options.CupsServers.Add("print-server.hospital.local");
PrinterListingResult listing = PrinterListing.List(options);
foreach (ListedPrinter p in listing.Printers) Console.WriteLine(p.Name + " " + p.Uri + (p.IsCupsQueue ? " (CUPS)" : ""));
foreach (PrinterListingProblem problem in listing.Problems) Console.WriteLine("Not listed: " + problem);
```

- **Discovery** uses DNS-SD over multicast DNS (RFC 6762/6763) on every active IPv4 interface. It sends one-shot
  queries from a temporary port and reads the answers; it never listens on port 5353. One entry per advertised
  service: a printer offering IPP and the raw port appears twice. `Uri` is ready for `IppRoutes.CreateRouter` or
  `RawSocketTarget.FromUri`.
- **The registry** keeps names, connections, addresses, pins, forced routes and the last-read capabilities with
  their age. It never stores credentials.
- **CUPS queues** come from the CUPS-Get-Printers operation over IPP; each `CupsQueue.Uri` goes to
  `IppRoutes.CreateCupsRouter`. Since 1.0.0-rc.4 a queue also has `PrinterType`, `IsRemote` (shared by another CUPS
  server, bit 0x2), `Uuid` (printer-uuid) and `DeviceUri` (where CUPS sends its jobs; some systems send it only to
  an administrator).
- **The configured CUPS server** (`CupsQueues.ConfiguredServer()`, 1.0.0-rc.4) is the `CUPS_SERVER` environment
  variable, else `ServerName` in `~/.cups/client.conf`, else in `/etc/cups/client.conf`, as an IPP URI with port 631
  when none is given. A local socket path means the local server. Null when nothing is set, and on Windows.
- **The merged list** (`PrinterListing.List`, 1.0.0-rc.4):
  - It asks the local CUPS server (not on Windows), the configured server and `CupsServers`, each once, and browses
    DNS-SD; each of these can be turned off in `PrinterListingOptions`.
  - Entries are one printer when they share a UUID (a queue's printer-uuid, the `uuid` in a CUPS `dnssd://` device
    URI, or the DNS-SD `UUID` key) or a URI (a queue's device URI or own URI, or the advertised URI; compared
    without case, a trailing slash or the default port).
  - **The CUPS queue comes first** when a printer is found both ways: `Name` and `Uri` are the first queue's, so
    CUPS converts and spools the job. Every queue is kept in `Queues` (for example a queue added by hand and the one
    CUPS made by itself for the same printer), and every advertisement in `Discovered`.
  - A server or browse that fails goes into `Problems`; the rest is still listed.
- **Duplex backs** for PWG Raster follow PWG 5102.4 Table 9 (`pwg-raster-document-sheet-back`).

## 4b. Vector pages and XPS (Phase 4, `Chuvadi.Print.Windows`)

`VectorPage` (core) describes a page as vector content: paths, glyph runs, images and groups. Sizes and
coordinates are in points, with the origin at the top left and y running down. Every element has its own
`Transform` (into its parent's space), `Clip` (in its own space), `Opacity` and `BlendMode`.

```csharp
var page = new VectorPage(595.28, 841.89);                       // A4
var font = new VectorFont(File.ReadAllBytes(@"C:\Windows\Fonts\arial.ttf"), "Arial");
var run = new VectorGlyphRun(font, 12, new VectorSolidBrush(VectorColor.Black));
run.Glyphs.Add(new VectorGlyph(glyphId: 43, x: 72, y: 100, advance: 8.67, text: "H"));   // id and advance from the font's cmap and hmtx
page.Elements.Add(run);
page.Elements.Add(new VectorImageElement(VectorImage.FromJpeg(File.ReadAllBytes("logo.jpg")), 72, 120, 144, 72));

if (!XpsWriter.CanWrite(new[] { page }, out string? reason)) Console.WriteLine(reason);   // send to raster instead
else XpsWriter.Write(new[] { page }, @"C:\out\report.xps");
```

- **Fonts** are TrueType files (the bytes of the .ttf, or a subset). Each is stored once, obfuscated as ECMA-388
  requires. Glyph ids are the font's own; put the glyph's characters in `VectorGlyph.Text` so the document can
  be searched and copied. Glyphs on different baselines become separate XPS Glyphs elements.
- **Images** are PNG or JPEG files, stored once each; PNG transparency is kept. `VectorImage.FromPixels`
  encodes grey, RGB, grey with alpha or RGB with alpha pixels as PNG. An image fills the unit square of its own space; the four-number `VectorImageElement` constructor places
  it as a rectangle. How a viewer smooths a scaled image is its own choice (XPS has no PDF /Interpolate).
- **Groups** are written as nested Canvas elements; their opacity applies to the group as a whole.
- **Soft masks**: an alpha `VectorSoftMask` on a group is written as the group's opacity mask, a VisualBrush that
  draws the mask content in the group's own coordinates. Where the content is transparent, the group is hidden.
  A luminosity soft mask has no XPS equivalent (an opacity mask uses alpha only), so `CanWrite` refuses it.
- **Refused content**: `CanWrite` names the first page and item XPS cannot express. Such pages go to raster;
  the writer never approximates them. The list is in the CHANGELOG (Phase 4 part 1).
- **Colour** is sRGB, 8 bits per channel with alpha. Gradients interpolate in sRGB and always extend (pad).

### Printing vector pages to a Windows queue (Phase 4 part 3)

`WindowsXpsPrinterTarget` sends vector pages to an installed Windows printer as XPS, through the Print Document
Package API (Windows 8 and later). The driver draws vectors: text and lines stay sharp at any resolution, and the
spool file is small. Pages come from an `IVectorPageSource` (core), the vector counterpart of `IPageRasterizer`.
The PDF converter (Phase 4 part d) will be one; an application can also print pages it drew itself.

```csharp
var target = new WindowsXpsPrinterTarget("Ward-3 Laser");
var settings = new PrintSettings { Paper = PaperSize.A4, Duplex = Duplex.Vertical, Copies = 2 };
if (target.CanPrint(pages, settings, out string? why))
    target.PrintPages(pages, settings, "Discharge summary");      // waits for the job status (StatusTimeout)
Console.WriteLine(target.LastJobStatus);                          // job 42: Completed, 4 pages processed, ...

// In a router, after the IPP routes and before raster (D-96 position 4):
using var router = new PrintRouter(
    new WindowsQueueRoute(new WindowsXpsPrinterTarget("Ward-3 Laser", pdfVectorSource)),
    new RasterRoute(new WindowsPrinterTarget("Ward-3 Laser")));
PrintRouteResult result = router.Print(job);   // a page XPS cannot express falls back to raster, with the reason in the audit
```

- **Same layout as raster.** Pages are placed with `PageLayout`, the same as the raster routes: page selection,
  scale mode, alignment, margins, orientation and auto-rotate. `VectorLayout.Place` does the placement and is
  public for other vector writers. Software copies and duplex blank backs come from the same `PrintPlan`.
- **Same settings as GDI.** The settings become the DEVMODE that `WindowsPrinterTarget` builds: paper,
  orientation, duplex, colour, copies, collation, tray, media and resolution. Windows turns it into the job's
  PrintTicket. `CreatePrintTicket(settings)` returns that ticket without printing.
- **Grey and black-and-white** output is made by the driver; `PrintJobOptions.Monochrome` applies to rendered pages only.
- **Copies** (`CopyHandling`):
  - `Auto` (the default) puts the copies in the PrintTicket when the driver can make them, and repeats the sheets otherwise.
  - `Software` always repeats the sheets.
- **Status**: after the last page, the target waits for the print system to report the job finished.
  - The wait is `StatusTimeout`: two minutes by default; null means do not wait.
  - Failed or cancelled jobs fail.
  - No report in time fails too; the message says the job may still be printing.
  - `LastJobStatus`, `LastSpoolJobId` and `LastPrintTicket` describe the latest job.
- **Errors**: printing failures are `WindowsPrintException`; pages XPS cannot express are
  `NotSupportedException`. In a router these give a Failed result, and no other route is tried.
- **Threads**: the submission runs on a thread of its own in COM's multithreaded apartment, so calling from a
  UI thread is safe (it blocks that thread until the job is submitted; call it from a worker for a responsive UI).
- **Saving instead of printing**: `SaveXps(document, settings, stream)` writes the exact package the target would
  send.

## 5. PDF rendering (A5, package Chuvadi.Print.Pdf.Raster)

- **`PdfPageRasterizer`** is the `IPageRasterizer` for PDFs.
  - Pages are rendered at the resolution and pixel format the job asks for, with the page's `/Rotate` applied.
  - The printed area is the **crop box** (D-235 L1.3a): the page as a viewer shows it, as ISO 32000 says.
    `PageBox` picks another box (`MediaBox`, `BleedBox`, `TrimBox`, `ArtBox`); the box is clipped to the media box.
  - Annotations and form fields are printed by flattening them first. With Chuvadi.Pdf 3.31, fields and comments
    without a stored appearance get one generated, so they print where they printed blank before.
  - All the renderer's options are exposed: `SuperSample`, `GammaCorrect`, `Hinting`, `FlatnessTolerance`,
    `IgnoreOptionalContent`, `LayerVisibility`, `Password` and `MaxRenderDpi`.
- **`PdfFontInspector`** is the `IPdfFontInspector` for `PrintRouter.FontInspector` (D-235 L2). It answers
  `AllEmbedded`, `NeedsGeneratedAppearances`, `NotAllEmbedded` or `Unknown` from Chuvadi.Pdf's font report.
  `LastReport` lists each font that is not embedded with its page; `Password` opens encrypted PDFs; `LastError`
  says why an answer is `Unknown`. With it set, a PDF whose fonts are all embedded goes as-is to IPP and CUPS
  printers that take PDF.
- **Saving as PDF** is not a print target: use the Chuvadi.Pdf writer or Chuvadi.Layout's PDF output.
- **Chuvadi.Pdf version**: 0.2.0 references `[3.32.0, 4.0.0)` (D-235, D-258, D-326). NuGet uses the lowest version a
  range allows, so an application that wants a newer Chuvadi.Pdf references it directly.
  - The floor is 3.32.0 because 3.32.0 paints form XObjects with the inherited graphics state (LibreOffice
    watermarks print in their real shade), and the adapter sets 3.32.0's `RenderOptions.DisplayBox`.
  - Don't use 0.1.0 with Chuvadi.Pdf 3.32.0 or later: a page whose crop box differs from its media box prints
    enlarged and cut. 0.1.0's floor was 3.31.2.
- **Building**: it lives in `staging/` and consumes the Chuvadi.Pdf packages from the local feed set in
  `staging/nuget.config`, never a source clone (D-29). It is outside `Chuvadi.slnx` and the pull-request CI until
  Chuvadi.Pdf joins the monorepo or nuget.org (D-28). Building, testing and packing are described in its README.
- **Releases**: GitHub releases tagged `print-pdf-raster-v*` (0.1.0 first, then 0.2.0), not nuget.org. The release job builds and
  tests the package against the Chuvadi.Pdf packages of the chuvadi-pdf release, checked against hashes pinned in
  `tools/chuvadi-pdf/hashes/`. Take Chuvadi.Pdf from that chuvadi-pdf release.

## 6. Remote printing (A6, `Chuvadi.Print.Remote`)

Branch PCs run a **PrintAgent** that dials out over HTTPS to a **PrintHub** embedded in the host application
. Nothing listens on the branch PC (P07).

**Hub (host side)**

```csharp
var tokens = new TokenAgentAuthenticator(new FileAgentTokenStore(@"D:\print\agents.json"));
var hub = new PrintHub(new FileJobStore(@"D:\print\jobs"), tokens, new FileAuditSink(@"D:\print\audit.log"),
                       new PrintHubOptions { LeaseSeconds = 120, MaxAttempts = 3 });
string token = tokens.Issue("ward3-pc");                    // give it to the agent's installer once
StoredJob queued = hub.Submit("ward3-pc", "Ward-3 Laser", job);   // idempotent per job.JobId
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
    HubUri = new Uri("https://hub.example.com/print-hub/"), AgentId = "ward3-pc",
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

## 7. Testing and verification

- **Tests**: `dotnet test Chuvadi.slnx` runs Print's unit tests and its verification groups. Groups that do
  not apply on the current OS report SKIP.
- **ManualTests runner**: `dotnet run --project samples/Chuvadi.Print.ManualTests` runs the same groups. It
  also accepts:
  - `--preview <folder>`: writes PNG sheets for visual checking.
  - `--windows-printer "<name>" [--windows-output <folder>]`: an 11-job proof set.
  - `--ipp-printer <uri> [--ipp-pdf <file>] [--ipp-insecure]`: a proof set over IPP.
  - `--route-printer <uri> [--ipp-insecure]`: the route an IPP printer gets for unknown and for embedded fonts (prints nothing).
  - `--ipp-certificate <ipps-uri>`: the printer's certificate and SHA-256 fingerprint, for pinning.
  - `--discover [seconds]`: the printers that answer on the local network.
  - `--cups-server <uri>`: the queues of a CUPS server.
  - `--raw-printer socket://host[:port] [--raw-file <file>]`: a text line or a file to a raw port.
  - `--windows-raw-printer "<queue>" [--windows-raw-file <port file>]`: a RAW job to a Windows queue.
- **Chuvadi.Print.Pdf.Raster**: `dotnet test staging/Chuvadi.Print.Pdf.Raster.Tests` (needs the Chuvadi.Pdf packages in the local feed).
- **Windows**: everything only Windows can prove is in [WINDOWS-VERIFICATION.md](WINDOWS-VERIFICATION.md).

## 8. Thread safety

Checked against the code for rc.3. "Shareable" means one instance can be used from several threads at once;
everything else is meant for one caller at a time.

**Shareable** (internal locks, or no state):
- `PrinterRegistry`: every read and change takes a lock; reads return copies. `SaveTo` writes a snapshot to a
  temporary file and then replaces the target file. Two processes saving the same file are not coordinated.
- `IppCapabilityCache`: share one between routers for the same printers.
- `IppClient`: an `HttpClient` underneath and an atomic request id, no other state. Concurrent requests are fine.
- `PrintHub`, `InMemoryJobStore` and `FileJobStore` (within one process).
- `FileAuditSink` (writes one line at a time under a lock), `NullAuditSink`.
- `XpsWriter` and the other static helpers (`PageLayout`, `VectorLayout`, `WindowsPrinters`, `CupsQueues`,
  `NetworkPrinterDiscovery`): no shared state.

**One per job, or one per caller**:
- `PrintJob`, `PrintSettings` and `PrintJobOptions` are mutable: build one per job.
- Every target holds the state of the job in progress and its `Last...` results:
  - `WindowsPrinterTarget`, `WindowsXpsPrinterTarget`, `WindowsRawPrinterTarget`;
  - `IppPrinterTarget`, `IppDocumentTarget`, `RawSocketTarget`;
  - the image file targets.

  Use one per thread, or serialise jobs on it. Several targets for the same printer are fine.
- Routes and `PrintRouter` hold targets, so they follow the same rule.
- `PrintAgent`: one per agent process.

**No static mutable state** anywhere in the Print packages. Two jobs on two threads, each with its own job and
target, never touch each other. `WindowsXpsPrinterTarget` does its submission on a thread of its own in COM's
multithreaded apartment, so the caller's apartment does not matter.

## 9. Android

Chuvadi.Print targets `net10.0` and is pure managed code, so an Android app can reference it. It is not yet in
CI's Android smoke build (`tests/Chuvadi.AndroidSmoke`, which checks Zip and Fonts today). Its network routes need
only what any networked Android app needs. Print does not wrap Android's own print dialog; that stays with the app.

- **IPP, IPPS, raw TCP 9100 and the remote agent** need the `android.permission.INTERNET` permission in the
  app's manifest.
- **Plain `ipp://` is cleartext HTTP.** Apps targeting Android 9 (API level 28) or later have cleartext traffic
  disabled by default. Either use `ipps://`, or allow cleartext for the printers' addresses in the app's network
  security configuration (`cleartextTrafficPermitted="true"` on a `domain-config` for those hosts).
- **`NetworkPrinterDiscovery.Browse`** uses multicast DNS. Android's Wi-Fi stack filters out multicast packets
  unless the app holds a `WifiManager.MulticastLock`, which needs the `android.permission.CHANGE_WIFI_MULTICAST_STATE`
  permission. Acquire the lock just before browsing and release it right after: Android notes that the extra packets
  cost battery.
- **Windows-only parts** (`Chuvadi.Print.Windows`) are not used on Android: guard calls with
  `OperatingSystem.IsWindows()`, as the platform analyzer asks.
- **Spooling**: if the app has no convenient writable folder, use `IppSpoolMode.Memory` (section 4).
