# Chuvadi.Print.Pdf.Raster — Changelog

## 0.2.0 (2026-11-11)

**Chuvadi.Pdf floor 3.32.0 (D-326 R1, D-259).** The package references Chuvadi.Pdf.Rendering and
Chuvadi.Pdf.Operations at `[3.32.0, 4.0.0)` (`ChuvadiPdf.props`), instead of `[3.31.2, 4.0.0)`. It still brings
Chuvadi.Fonts 1.0.0 or later. It depends on Chuvadi.Print 1.0.0-rc.3 or later, as before.

- **Output change (rule 5): form XObjects are painted with the graphics state in effect where they are drawn.**
  - That covers fill and stroke colour, line style, text state, constant alpha and blend mode.
  - Before, a form that set no colour was painted black.
  - Measured on the 91 PDFs and 174 pages of the L1 corpus at 150 dpi: **173 pages are pixel-identical**. One
    changes, d39, the LibreOffice DRAFT watermark: its 75 % grey at half alpha now prints at 224, where it printed
    at 128.
- **The renderer's display box.** Chuvadi.Pdf 3.32.0 renders the crop box by default. The adapter sets
  `RenderOptions.DisplayBox` to the media box, so `PageBox` (crop, media, bleed, trim or art box) works as in 0.1.0
  and the printed area is unchanged.
  - **0.1.0 with Chuvadi.Pdf 3.32.0 or later prints a page whose crop box differs from its media box enlarged and
    cut.** Use 0.2.0 with 3.32.0 or later.
- **Known limits (Chuvadi.Pdf ticket 42):** a pattern set before a form XObject is drawn is not passed into the form,
  and a soft mask set before it isn't applied to the form's content.
- **Tests:** 44, two of them new. A form XObject must paint in the inherited 75 % grey (about 191), and at half
  alpha in about 223. Both fail on 3.31.2. All 44 pass on Chuvadi.Pdf 3.32.0.

## 0.1.0 (2026-11-10)

Renamed from Chuvadi.Print.Pdf (D-107, D-125): package, assembly and namespace are now
`Chuvadi.Print.Pdf.Raster`; the tests are `staging/Chuvadi.Print.Pdf.Raster.Tests`.

A5 (decision D-14): first version. It depends on Chuvadi.Print 1.0.0-rc.3 or later.

**Chuvadi.Pdf 3.31 (D-235 L1, D-258).** The package references Chuvadi.Pdf.Rendering and Chuvadi.Pdf.Operations at
`[3.31.2, 4.0.0)` (`ChuvadiPdf.props`), instead of exactly 3.22.1. It brings Chuvadi.Fonts 1.0.0 or later.
- **Floor 3.31.2 (D-258):** 3.31.1 renders pages whose media box does not start at 0,0 blank, so the range starts at
  3.31.2.
- **Tests:** every one passes on Chuvadi.Pdf 3.31.2 (and passed on 3.31.1 before the floor was raised).
- **Feed:** the packages come from the local feed (D-29; `staging/nuget.config`, whose path now uses forward slashes
  so it also restores on Linux).
- **Output changes** (rule 5; measured on 91 PDFs, 174 pages, at 150 dpi; the full list is in the L1 measurement
  record):
  - Pages whose media box does not start at 0,0 print their content. They printed blank on 3.22.1 (and on 3.31.1).
  - Constant alpha (`ca`/`CA`) is composited as ISO 32000 says: 50 % blue over white is (128, 128, 255), where it
    was (188, 188, 255). This changes semi-transparent watermarks and fills.
  - Dashed strokes print dashed (3.22.1 drew them solid).
  - Form fields under `/NeedAppearances` and comments without an appearance stream print (they were blank).
  - A few line ends differ by up to 3 pixels.
  - A damaged CFF glyph no longer fails the page.
- **The crop box (L1.3a, rule 5 output change):** `PdfPageRasterizer` prints the crop box, clipped to the media box,
  where it printed the whole media box. `GetPageSize` reports the crop box. New `PageBox` property (`PdfPageBox`:
  `CropBox`, the default; `MediaBox`; `BleedBox`; `TrimBox`; `ArtBox`).
- **`PdfFontInspector` (L2):** the `IPdfFontInspector` over Chuvadi.Pdf's `FontEmbeddingReport`.
  - `AllEmbedded` only when every font is embedded and no field or comment needs a generated appearance;
    `NeedsGeneratedAppearances` when fonts are embedded but appearances are missing; `NotAllEmbedded`; `Unknown`
    for unreadable files and missing or wrong passwords.
  - Also `Password`, `LastReport` (the full report) and `LastError`.

Also in 0.1.0, from the first version:

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
