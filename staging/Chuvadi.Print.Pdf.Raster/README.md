# Chuvadi.Print.Pdf.Raster

Connects [Chuvadi.Print](../Chuvadi.Print/README.md) to the Chuvadi PDF library
([chuvadi-pdf](https://github.com/arunshivab/chuvadi-pdf)). Pure managed code, no third-party packages.

- **`PdfPageRasterizer`** (`IPageRasterizer`): renders PDF pages for any Chuvadi.Print raster target — Windows
  printers, IPP printers, PNG preview — one page at a time at the device resolution, with the page's `/Rotate`
  applied. Annotations and form fields print when asked (they are flattened into a copy of the document first).
  Exposes the renderer's options: supersampling, gamma, hinting, flatness, layers, password, a render-DPI cap.

```csharp
using Chuvadi.Print;
using Chuvadi.Print.Pdf.Raster;
using Chuvadi.Print.Windows;

var job = new PrintJob("discharge-summary.pdf")
    .WithRasterizer(new PdfPageRasterizer())
    .WithCopies(2).WithDuplex(Duplex.Vertical).WithPaper(PaperSize.A4);

job.PrintTo(new WindowsPrinterTarget("Ward-3 Laser"));       // rendered pages to a printer
```

## Building

This project lives in `staging/` and is **not** in `Chuvadi.slnx` or the monorepo's CI (D-28, D-29). It
consumes the Chuvadi.Pdf packages (`Chuvadi.Pdf.Rendering`, `Chuvadi.Pdf.Operations`, pinned by
`ChuvadiPdfVersion` in the csproj, currently 3.22.1) from your local package folder (see `staging/README.md`),
configured by `staging/nuget.config` for the projects under `staging/` only. It never references a chuvadi-pdf
source clone.

```powershell
# once per Chuvadi.Pdf version, in the chuvadi-pdf clone:
.\build\pack.ps1 -Version 3.22.1
Copy-Item artifacts\nupkg\*.nupkg C:\src\local-nuget
# in the monorepo:
dotnet test staging/Chuvadi.Print.Pdf.Raster.Tests -c Release
dotnet pack staging/Chuvadi.Print.Pdf.Raster -c Release -o C:\src\local-nuget
```
The package depends on Chuvadi.Print and on Chuvadi.Pdf.Rendering / Chuvadi.Pdf.Operations at
`ChuvadiPdfVersion`. To move to a newer Chuvadi.Pdf, pack it into the feed and change `ChuvadiPdfVersion`.

## Notes

- Chuvadi.Pdf's renderer draws the media box; `GetPageSize` reports the media box (turned by `/Rotate`).
- Annotation flattening skips hidden and no-view annotations; the annotation Print flag is not consulted.
- Saving a document as PDF is not this package's job: use the Chuvadi.Pdf writer or Chuvadi.Layout's PDF output.

Licence: MIT (this adapter). Chuvadi.Pdf is Apache-2.0.
