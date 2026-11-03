# Chuvadi.Print.Pdf.Raster — Changelog

## 0.1.0 (unreleased)

Renamed from Chuvadi.Print.Pdf (D-107, D-125): package, assembly and namespace are now
`Chuvadi.Print.Pdf.Raster`; the tests are `staging/Chuvadi.Print.Pdf.Raster.Tests`.

A5 (decision D-14): first version. It consumes the Chuvadi.Pdf 3.22.1 packages from the local feed (D-29;
staging/nuget.config). It was tested against packages packed from chuvadi-pdf main (v3.22.1, commit 44623b6).

- `PdfPageRasterizer` (`IPageRasterizer` over Chuvadi.Pdf.Rendering `PageRasterizer`): page sizes with `/Rotate`
  applied, rotation and per-axis DPI from the `RasterRequest` (anisotropic DPI and DPI above `MaxRenderDpi` are
  resampled), Bgra32 / Rgb24 / Gray8 output (BlackWhite1 requests get Gray8; the job converts), annotations and
  form fields via `AnnotationFlattener`, cancellation, and every renderer option (`SuperSample`, `GammaCorrect`,
  `Hinting`, `AutohintUnhintedFonts`, `FlatnessTolerance`, `IgnoreOptionalContent`, `LayerVisibility`, `Password`).
- `PdfFileTarget` (print a job to a PDF file) was removed before release (D-48). Saving as PDF belongs to the
  Chuvadi.Pdf writer and Chuvadi.Layout's PDF output.
- Tests (`staging/Chuvadi.Print.Pdf.Raster.Tests`) build their PDFs by hand (`MiniPdf`) and check pixels: sizes,
  rotation, `/Rotate`, anisotropic DPI, formats, annotations, cancellation and a full `PrintJob`. A test writes PNG
  previews (`print-pdf-preview` under the test output folder) for visual checking.
