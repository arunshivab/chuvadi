# Chuvadi.Print — Changelog

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
