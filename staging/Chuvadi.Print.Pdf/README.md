# Chuvadi.Print.Pdf

Connects [Chuvadi.Print](../Chuvadi.Print/README.md) to the Chuvadi PDF library
([chuvadi-pdf](https://github.com/arunshivab/chuvadi-pdf)). Pure managed code, no third-party packages.

- **`PdfPageRasterizer`** (`IPageRasterizer`): renders PDF pages for any Chuvadi.Print raster target — Windows
  printers, IPP printers, PNG preview — one page at a time at the device resolution, with the page's `/Rotate`
  applied. Annotations and form fields print when asked (they are flattened into a copy of the document first).
  Exposes the renderer's options: supersampling, gamma, hinting, flatness, layers, password, a render-DPI cap.
- **`PdfFileTarget`** (`IDocumentTarget`): "print to PDF". Writes a new PDF whose pages are the printed sheet
  sides, placing the original pages as vectors (text stays text) with every Chuvadi.Print layout setting —
  paper, orientation, scale mode, alignment, margins, auto-rotate — and the print plan's page selection, copies,
  collation and duplex blank backs. Optional clipping to the printable area, simulated hard margins, output
  encryption. Writes through a temporary file, so a failed job leaves no half-written PDF.

```csharp
using Chuvadi.Print;
using Chuvadi.Print.Pdf;
using Chuvadi.Print.Windows;

var job = new PrintJob("discharge-summary.pdf")
    .WithRasterizer(new PdfPageRasterizer())
    .WithCopies(2).WithDuplex(Duplex.Vertical).WithPaper(PaperSize.A4);

job.PrintTo(new WindowsPrinterTarget("Ward-3 LaserJet"));       // rendered pages to a printer
job.SubmitTo(new PdfFileTarget(@"C:\out\summary-printed.pdf"));  // the same job as a vector PDF
```

## Building

This project lives in `staging/` and is **not** in `Chuvadi.slnx` or the monorepo's CI (D-28, D-29). It
consumes the Chuvadi.Pdf packages (`Chuvadi.Pdf.Rendering`, `Chuvadi.Pdf.Operations`, pinned by
`ChuvadiPdfVersion` in the csproj, currently 3.22.1) from the local feed `C:\Users\aruns\Documents\local-nuget`,
configured by `staging/nuget.config` for the projects under `staging/` only. It never references a chuvadi-pdf
source clone.

```powershell
# once per Chuvadi.Pdf version, in the chuvadi-pdf clone:
.\build\pack.ps1 -Version 3.22.1
Copy-Item artifacts\nupkg\*.nupkg C:\Users\aruns\Documents\local-nuget
# in the monorepo:
dotnet test staging/Chuvadi.Print.Pdf.Tests -c Release
dotnet pack staging/Chuvadi.Print.Pdf -c Release -o C:\Users\aruns\Documents\local-nuget
```
The package depends on Chuvadi.Print and on Chuvadi.Pdf.Rendering / Chuvadi.Pdf.Operations at
`ChuvadiPdfVersion`. To move to a newer Chuvadi.Pdf, pack it into the feed and change `ChuvadiPdfVersion`.

## Notes

- Chuvadi.Pdf's renderer draws the media box; `GetPageSize` reports the media box (turned by `/Rotate`).
  `PdfFileTarget` places the crop box, as viewers show the page.
- Annotation flattening skips hidden and no-view annotations; the annotation Print flag is not consulted.
- A vector PDF keeps original colours: Grayscale/Monochrome are listed in `LastApproximations`.

Licence: MIT (this adapter). Chuvadi.Pdf is Apache-2.0.
