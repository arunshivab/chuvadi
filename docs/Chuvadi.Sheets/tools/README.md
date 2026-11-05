# Chuvadi.Sheets guide tools

The guide's source is `docs/Chuvadi.Sheets/GUIDE.md`. These two tools check its examples and render it to PDF. Neither
is packaged, and neither is in `Chuvadi.slnx` or CI.

## GuideSnippets: check every example

Pure BCL; it references `src/Chuvadi.Sheets` directly.

```powershell
dotnet run -c Release --project docs\Chuvadi.Sheets\tools\GuideSnippets -- --extract
dotnet run -c Release --project docs\Chuvadi.Sheets\tools\GuideSnippets
```

- `--extract` turns each `csharp` block of GUIDE.md into a method in `Snippets.g.cs`. Blocks that declare types go
  to file scope. `#line` directives make compile errors point at GUIDE.md.
- The second command builds the examples, runs them in order in a temporary folder, prints their output, and
  lists each failure with its GUIDE.md line. It exits 1 if any example fails.
- Run both after every change to GUIDE.md, and commit the regenerated `Snippets.g.cs`.

## GuideRenderer: render the PDF

```powershell
dotnet run -c Release --project docs\Chuvadi.Sheets\tools\GuideRenderer
```

- It writes `docs/Chuvadi.Sheets/Chuvadi-Sheets-Guide-v2.0.pdf` (A4) with Chuvadi's own tools:
  - Chuvadi.Layout.Markup lays out the Markdown;
  - Chuvadi.Layout.Pdf writes the PDF;
  - Chuvadi.Pdf's `PdfCompressor` compresses the page content, losslessly;
  - the fonts are Carlito and Liberation Mono from Chuvadi.Layout.Fonts, and LiPi Sans Tamil from Chuvadi.Fonts,
    all under the SIL OFL 1.1. No installed fonts are used, so every machine gives the same layout.
- The page numbers in the Contents table are filled in from the laid-out pages. Do not type them into GUIDE.md.
- Any layout or PDF warning (for example a missing glyph) fails the run (exit 1).
- `--png <folder>` also writes each page as a 96 dpi PNG, so you can check it by eye.

Like `staging/samples/Chuvadi.Layout.Viewer`, the renderer takes chuvadi-pdf as version-pinned packages:
- the version is the `ChuvadiPdfVersion` in `staging/src/Chuvadi.Layout.Pdf/ChuvadiPdf.props`, currently 3.30.2;
- the packages come from the local feed `%USERPROFILE%/Documents/local-nuget`, set by the `nuget.config` here;
- it needs the same packages as Chuvadi.Layout.Pdf, plus `Chuvadi.Pdf.Operations`.

Pack chuvadi-pdf into that feed first, as `staging/README.md` describes.
