# Chuvadi.Print.Pdf — Changelog

## 0.1.0 (unreleased)

A5 (decision D-14): first version. It consumes the Chuvadi.Pdf 3.22.1 packages from the local feed (D-29;
staging/nuget.config). It was tested against packages packed from chuvadi-pdf main (v3.22.1, commit 44623b6).

- `PdfPageRasterizer` (`IPageRasterizer` over Chuvadi.Pdf.Rendering `PageRasterizer`): page sizes with `/Rotate`
  applied, rotation and per-axis DPI from the `RasterRequest` (anisotropic DPI and DPI above `MaxRenderDpi` are
  resampled), Bgra32 / Rgb24 / Gray8 output (BlackWhite1 requests get Gray8; the job converts), annotations and
  form fields via `AnnotationFlattener`, cancellation, and every renderer option (`SuperSample`, `GammaCorrect`,
  `Hinting`, `AutohintUnhintedFonts`, `FlatnessTolerance`, `IgnoreOptionalContent`, `LayerVisibility`, `Password`).
- `PdfFileTarget` (`IDocumentTarget`, vector imposition through Chuvadi.Pdf `PageComposer`): Chuvadi.Print
  `PageLayout` and `PrintPlan` decide each sheet side; options for annotations, form fields, margin reference,
  hard margins, blank backs, clipping, source password and output encryption; `LastPlan`, `LastPageCount`,
  `LastOutputPath`, `LastApproximations`; atomic file output or a stream.
- Tests (`staging/Chuvadi.Print.Pdf.Tests`) build their PDFs by hand (`MiniPdf`) and check pixels: sizes,
  rotation, `/Rotate`, anisotropic DPI, formats, annotations, cancellation, a full `PrintJob`, and for
  `PdfFileTarget` placement, auto-rotate, reverse orientation, fit-to-page, clipping, copies/collation/duplex
  blank backs, page selection, annotations, atomic file output and the audit event. A test writes PNG
  previews (`print-pdf-preview` under the test output folder) for visual checking.
