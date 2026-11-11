# Chuvadi.Print.Pdf.Raster

Connects [Chuvadi.Print](../Chuvadi.Print/README.md) to the Chuvadi PDF library
([chuvadi-pdf](https://github.com/arunshivab/chuvadi-pdf)). Pure managed code, no third-party packages.

- **`PdfPageRasterizer`** (`IPageRasterizer`): renders PDF pages for any Chuvadi.Print raster target (Windows
  printers, IPP printers, PNG preview), one page at a time at the device resolution, with the page's `/Rotate`
  applied. It prints the **crop box**, the page as a viewer shows it, as ISO 32000 says; `PageBox` picks another
  box. Annotations and form fields print when asked: they are flattened into a copy of the document first, and
  fields or comments without a stored appearance get one generated.
  Exposes the renderer's options: supersampling, gamma, hinting, flatness, layers, password, a render-DPI cap.
- **`PdfFontInspector`** (`IPdfFontInspector`): tells a `PrintRouter` whether a PDF can go to the printer as it is.
  Every font must be embedded, and no field or comment may need its appearance generated. `LastReport` lists any
  font that is not embedded, with its page.

```csharp
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.Pdf.Raster;
using Chuvadi.Print.Windows;

var job = new PrintJob("discharge-summary.pdf")
    .WithRasterizer(new PdfPageRasterizer())
    .WithCopies(2).WithDuplex(Duplex.Vertical).WithPaper(PaperSize.A4);

job.PrintTo(new WindowsPrinterTarget("Ward-3 Laser"));       // rendered pages to a printer

// Send the PDF itself to an IPP printer when its fonts allow it; otherwise render it.
using PrintRouter router = IppRoutes.CreateRouter("ipp://10.0.0.21/ipp/print");
router.FontInspector = new PdfFontInspector();
PrintRouteResult result = router.Print(job);
```

## Chuvadi.Pdf version

The package references Chuvadi.Pdf.Rendering and Chuvadi.Pdf.Operations at **`[3.32.0, 4.0.0)`**
(`ChuvadiPdf.props`; D-235, D-258, D-326), which brings Chuvadi.Fonts 1.0.0 or later with it.
- NuGet resolves a range to the **lowest** version it allows, so an application gets 3.32.0 unless it references a
  newer Chuvadi.Pdf directly.
- The floor is 3.32.0 (0.2.0):
  - 3.32.0 paints a form XObject with the graphics state in effect where it is drawn (fill and stroke colour, line
    style, text state, constant alpha, blend mode). On 3.31.2 a form that set no colour was painted black, so
    LibreOffice watermarks printed far too dark.
  - 3.32.0 renders the crop box by default (`RenderOptions.DisplayBox`). This package renders the whole media box
    and cuts out `PageBox` itself, so it sets that option, which exists only from 3.32.0.
- **Chuvadi.Print.Pdf.Raster 0.1.0 must not be used with Chuvadi.Pdf 3.32.0 or later.** Its range allows them, but
  it doesn't set `DisplayBox`, so a page whose crop box differs from its media box prints enlarged and cut. Use 0.2.0
  with 3.32.0 or later, or keep 0.1.0 on 3.31.2 exactly.
- **Known limits (Chuvadi.Pdf ticket 42):** a pattern set before a form XObject is drawn is not passed into the form,
  and a soft mask set before it isn't applied to the form's content.
- Earlier floors: 3.31.2 in 0.1.0, because 3.31.1 renders pages whose media box does not start at 0,0 blank (D-258).
- Chuvadi.Pdf's dependencies add about 4.3 MB to an application: Annotations, Forms, Text.Shaping and Chuvadi.Fonts
  (D-170).
- Trimming and NativeAOT: this package does not declare itself trimmable or AOT-compatible yet. That follows
  Chuvadi.Pdf's own status (L1.8).

## Building

**Releases:** the package is attached to the GitHub release of its tag (`print-pdf-raster-v0.1.0` and later), not
nuget.org. Take the Chuvadi.Pdf packages from the chuvadi-pdf GitHub release named in the release notes, and
Chuvadi.Fonts from our `fonts-v*` release. The release job builds and tests this project against the Chuvadi.Pdf
packages of that release, each checked against the hashes pinned in `tools/chuvadi-pdf/hashes/` (D-236, D-241).

This project lives in `staging/` and is **not** in `Chuvadi.slnx` or the pull-request CI (D-28, D-29). It consumes the
Chuvadi.Pdf packages from your local package folder (see `staging/README.md`), configured by `staging/nuget.config`
for the projects under `staging/` only. It never references a chuvadi-pdf source clone. The tests run on one exact
Chuvadi.Pdf version, 3.32.0 by default (`-p:ChuvadiPdfVersion=<version>` runs them on a later 3.x):

```powershell
dotnet test staging/Chuvadi.Print.Pdf.Raster.Tests -c Release
dotnet pack staging/Chuvadi.Print.Pdf.Raster -c Release -o artifacts\pdf-raster
```

The package depends on Chuvadi.Print (the version in the same tree, 1.0.0-rc.3 or later) and on the Chuvadi.Pdf range
above.

## Notes

- `GetPageSize` reports the printed box (the crop box by default), clipped to the media box and turned by `/Rotate`.
  A crop box that does not overlap the media box is ignored, as viewers do.
- Annotation flattening skips hidden and no-view annotations. The annotation Print flag is not consulted.
- Saving a document as PDF is not this package's job: use the Chuvadi.Pdf writer or Chuvadi.Layout's PDF output.

Licence: MIT (this adapter). Chuvadi.Pdf is Apache-2.0.
