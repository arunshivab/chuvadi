# Chuvadi.Print — Changelog

## 0.4.0 (unreleased)

A4: driverless printing over IPP (`Chuvadi.Print.Ipp`), on any OS, pure BCL (System.Net.Http only).

- **IPP encoding** (RFC 8010): `IppMessage` (requests and responses, any version), `IppAttributeGroup` (with `Set` / `Remove`), `IppAttribute` (1setOf), `IppValue` for every value type — integer, boolean, enum, octetString, dateTime, resolution, rangeOfInteger, collections (nested, 1setOf), text/name with and without language, keyword, uri, uriScheme, charset, naturalLanguage, mimeMediaType, out-of-band values, and unknown or extension tags kept as raw bytes so a decoded message re-encodes byte for byte. `IppTag`, `IppOperation`, `IppStatusCode`, `IppJobState`, `IppPrinterState`, `IppPrintQuality`.
- **IppClient**: ipp://, ipps://, http://, https:// (ipp/ipps default to port 631); Get-Printer-Attributes, Print-Job, Validate-Job, Create-Job, Send-Document, Get-Job-Attributes, Cancel-Job, Get-Jobs, and raw `Send` for any operation. `IppClientOptions`: user name, natural language, IPP version, timeout, credentials, acceptance of self-signed printer certificates. `IppException` carries the IPP status and response.
- **IppPrinterCapabilities**: formats, sides, media (with sources and types), colour modes, copies, collation, scaling, quality, orientations, resolutions, PWG Raster types/resolutions/sheet-back, margins, state; every raw attribute is kept.
- **PWG Raster** (PWG 5102.4): `PwgRasterWriter` and `PwgRasterReader` (black_1, sgray_8, srgb_8; PackBits-style line compression with line repeats and white fill), `PwgPageHeader`.
- **IppPrinterTarget** (`IPrintTarget`): prints rendered pages as PWG Raster. Chooses the raster resolution under `MaxRasterDpi`, turns landscape sheets to portrait feed, applies the printer's duplex back-side transform (`PwgSheetBack`, overridable), spools to a temporary file (one sheet in memory), then one Print-Job.
- **IppDocumentTarget** (`IDocumentTarget`): passes the original PDF through to printers that accept it, with the settings as job attributes.
- **Job ticket mapping** (`IppJobTicket`, `IppJobOptions`): copies and collation, sides, PWG media names (`PwgMediaNames`) or `media-col` with tray and media type, colour mode (bi-level for monochrome when offered), quality, resolution; in document mode also orientation, page-ranges and print-scaling. Settings IPP cannot express are listed in `LastApproximations`, or fail the job with `Strict`.
- **Core**: `IDocumentTarget`, `DocumentSubmission` and `PrintJob.SubmitTo(target)` send the document itself (no rasterising) with exactly one audit event; a configured rasteriser is used only to count pages.
- **Verification**: codec and raster round trips, header offsets, mapping rules, and end-to-end jobs against an in-process IPP printer (`FakeIppPrinter`); ManualTests `--preview` also writes the decoded IPP pages as PNG; `--ipp-printer <uri> [--ipp-pdf <file>] [--ipp-insecure]` sends the proof set (and a PDF) to a real printer.

## 0.3.0 (unreleased)

A3: printing to Windows printers through the GDI spooler (`Chuvadi.Print.Windows`).

- **Single assembly, plain net10.0** (BASELINE P08 amended, decision D-12): Windows code lives in the `Chuvadi.Print.Windows` namespace, is marked `[SupportedOSPlatform("windows")]`, and reaches `winspool.drv` / `gdi32.dll` through P/Invoke only. No native binaries, no `net10.0-windows` target; Linux builds and tests are unchanged.
- **Discovery**: `WindowsPrinters.List(includeConnections)`, `Get`, `Exists`, `DefaultPrinterName`, `SetDefault`; `PrinterInfo` with server, share, port, driver, comment, location, `PrinterAttributes`, `PrinterStatus` (every spooler flag) and job count.
- **Capabilities**: `WindowsPrinters.GetCapabilities(name, settings)` returns every driver paper (`PrinterPaper`), tray (`PrinterBin`), media type (`PrinterMediaType`) and resolution (`PrinterResolution`), maximum copies, collation, duplex, colour, landscape rotation, custom-size limits, and the device geometry for those settings (resolution, sheet, hard margins). `ToTargetCapabilities(maxRasterDpi)` feeds the A2 planner.
- **WindowsPrinterTarget** (`IPrintTarget`): maps `PrintSettings` to the driver's DEVMODE (paper, orientation, duplex, colour, copies and collation when the driver makes them) plus tray, media type, resolution or `PrintQuality` preset; `PaperMatching` (ExactOrCustom, ExactOnly, CustomOnly); `OutputFileName` (print to file; with "Microsoft Print to PDF" writes a PDF without a dialog); `MaxRasterDpi` (default 300) bounds page bitmaps on high-resolution devices; `StrictCapabilities` fails the job when the driver cannot honour the paper or duplex, otherwise prints and drops blank backs on simplex printers; `LastSpoolJobId`, `LastDeviceSheet`. Fluent `With*` for each.
- **Drawing**: pages are placed at their planned position with `StretchDIBits` from top-down 32-, 24-, 8- (gray palette) or 1-bit (white/black palette) DIBs, in bands of about 4 MB; HALFTONE stretching for colour and gray, COLORONCOLOR for bi-level.
- **Errors**: `WindowsPrintException` (with `NativeErrorCode`), `PrinterNotFoundException`; on non-Windows `PlatformNotSupportedException`.
- **Verification**: portable unit tests for DEVMODE editing (offsets, flags, short DEVMODEs, driver-private bytes), paper matching, geometry and DIB banding; Windows-only tests and a verification group that print through "Microsoft Print to PDF" to a file; ManualTests `--windows-printer "<name>" [--windows-output <folder>]` prints an 11-job proof set; groups that do not apply report SKIP.

## 0.2.0 (unreleased)

A2: `PrintJob` orchestration with pluggable rasterisation.

- **PrintJob**: `new PrintJob(bytes | path | stream)`, `PrintJob.LoadSpool(path | stream | bytes)`, `PrintJob.FromEnvelope(...)`; property setters plus a fluent `With*` method for every setting, piece of metadata and option; `Plan(...)`, `PrintTo(target, cancellationToken)`, `SaveSpoolTo(path | stream)`, `ToSpoolArray()`, `ToEnvelope()`.
- **Pluggable rasterisation**: `IPageRasterizer` / `IRasterDocument`, `RasterRequest` (DPI per axis, pixel format, rotation, annotations, form fields, anti-aliasing), `RasterPage` (pooled buffers via `RasterPage.Rent`), `PageDimensions`, `RasterPixelFormat` (Bgra32, Rgb24, Gray8, BlackWhite1), `PageRotation`. Print takes no dependency on any renderer; the PDF adapter ships separately as Chuvadi.Print.Pdf.
- **Layout**: `PageLayout.Compute` places a page on the sheet for every orientation (including the reverse ones), every scale mode, all nine alignments and both `MarginReference` modes (`SheetEdge` = larger of requested and hard margin; `PrintableArea` = requested plus hard margin). Reports printable area, content bounds, scale, rotation and clipping (`PagePlacement`, `RectanglePoints`).
- **Auto-rotate**: new `PrintSettings.AutoRotate` turns a page whose orientation differs from the printable area's by 90 degrees.
- **Planning**: `PrintPlan` / `PlannedImpression` expand page selection, copies and collation, pair duplex sides onto sheets, and insert blank backs so each duplex copy (or repeated sheet) starts on a fresh sheet. Uncollated duplex repeats whole sheets.
- **Targets**: `IPrintTarget` with `PrintTargetCapabilities` (DPI, hard margins, copies, collate, duplex, colour, preferred pixel format), `PrintJobContext` and `PrintedPage`. `CopyHandling` (Auto, Target, Software) decides who makes copies.
- **ImageFileTarget**: portable target that writes each impression as a PNG of the whole sheet (pure-BCL PNG encoder), with configurable DPI, simulated hard margins, file-name pattern and optional layout guides.
- **Colour**: output is Bgra32/Rgb24 for colour, Gray8 for grayscale (or colour on a mono device), BlackWhite1 for monochrome, with `MonochromeConversion` Threshold (adjustable), OrderedDither (8x8 Bayer) or ErrorDiffusion (Floyd-Steinberg).
- **Bounded memory**: one page is rendered at a time and released before the next; consecutive impressions of the same page reuse it.
- **Outcome and audit**: exactly one `PrintAuditEvent` per `PrintTo`, on success, failure or cancellation. Failures and cancellation are returned in `PrintResult` (or rethrown with `ThrowOnFailure`). `PrintResult` gains `ImpressionsPrinted` and `SheetsPrinted`. Progress via `IProgress<PrintProgress>`.
- **Spool envelope v2**: carries `AutoRotate`, `JobId`, `DocumentTitle`, `UserName` and `TenantId`. Version 1 envelopes are still read.

## 0.1.0 (unreleased)

A1: portable core — `PrintSettings`, `PaperSize`, `Margins`, `ContentAlignment`, `PageSelection`, `SpoolEnvelope` (v1), `PrintResult`, audit (`PrintAuditEvent`, `IPrintAuditSink`, `NullAuditSink`, `FileAuditSink`).
