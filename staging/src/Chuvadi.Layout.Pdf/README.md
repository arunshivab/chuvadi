# Chuvadi.Layout.Pdf

PDF, SVG and PNG output for [Chuvadi.Layout](https://github.com/arunshivab/chuvadi) pages, plus `PdfShaper`
(Indic shaping) — built on chuvadi-pdf. Memory-only: nothing is written except to the stream you pass.

```csharp
var options = new LayoutOptions { Shaper = new PdfShaper() };
options.FontSources.Add(BundledFontSource.Instance);
using LaidOutDocument doc = DocumentLayout.Open(flowDocument, options);

string svg = doc.GetPage(0).ToSvg();          // on-screen viewer: one page, lazily (Chuvadi.Layout's own SVG writer)
byte[] png = doc.GetPage(0).ToPng(150);       // server-side preview image
using var pdf = File.Create("out.pdf");
doc.ToPdf(pdf);                               // Print / LiPi export / "convert to PDF, then edit"
```

## SVG

`ToSvg` uses `Chuvadi.Layout.Drawing.LayoutSvg` (in the core library, pure BCL): every glyph is drawn from its
TrueType outline, with an invisible text layer for selection and search, links (http/https/mailto only by default),
image crops, clips and translucency. Settings: `LayoutPdfOptions.LayoutSvg`. It needs no PDF round trip.

`ToSvgViaPdf` renders through chuvadi-pdf's SVG renderer instead (settings: `LayoutPdfOptions.Svg`). It is kept for
comparison.

## How the display list maps to PDF (Chuvadi.Pdf 3.30.2)

| Display list | PDF |
|---|---|
| GlyphRunOp | `DrawShapedRun` with the run's source text: each cluster gets a ToUnicode entry, or `/ActualText` when several glyphs draw it (conjuncts, split vowels), so PDFs copy, search and extract as written. Character scaling, synthetic italic (slant 0.2) and synthetic bold (fill plus stroke, 3% of the size) through `GlyphRunStyle`. Faces are embedded and subset with `AddFont`: TrueType, CFF (.otf) and collection (.ttc) faces |
| FillRectOp, StrokeLineOp, PathOp | `DrawRectangle`, `DrawLine`, `DrawPath`; dashes as `LineStyle` dash patterns scaled by the line width |
| Translucent colours | drawn inside `PushOpacity` / `PopOpacity` |
| PushClipOp / PopClipOp | `PushClip` / `PopClip` (real PDF clipping) |
| ImageOp | `DrawImage`, with `ImageCrop` for cropped pictures; PNG alpha becomes a soft mask |
| LinkOp | `AddLink` to the URI, or to the target page (GoTo) |

`PdfShaper` shapes with `TextShaper.Shape(FontFace, ...)`: each Layout face is loaded once as a Chuvadi.Pdf
`FontFace` (collection faces included), whose shaping tables and feature lookups are cached, so line fitting does no
font parsing. Its metrics are checked against Chuvadi.Layout's own font core in the tests.

Indic shaping is HarfBuzz-conformant: Chuvadi.Pdf's `TextShaper` (since 3.26; Layout now uses 3.30.2) is a port of HarfBuzz's OpenType and Indic
shapers and matches HarfBuzz 15.0.0 glyph for glyph (ids, clusters, advances and offsets) on all 19,641 test syllables
of the nine LiPi Indic scripts, Tamil included.

Limitations reported through `LayoutPdfOptions.Warning` (codes): `PdfFontNotEmbedded` (a face Chuvadi.Pdf cannot
embed, such as CFF2; its text is left out), `PdfImageFormat` (GIF/EMF/WMF/SVG → placeholder), `PdfImageUnreadable`,
`PdfInternalLink` (a bookmark link without a resolved page), `PdfLinkInvalid` (a target that is not an absolute URI),
`PdfTextNotExtractable` (a run without a text mapping). The codes of the 3.22.1 adapter (`PdfSyntheticItalic`,
`PdfHorizontalScale`, `PdfTranslucency`, `PdfImageClip`, `PdfImageCrop`, `PdfCffFontNotEmbedded`) are gone.

## Third-party notices

Chuvadi.Layout.Pdf contains no third-party code. Its Indic shaping (`PdfShaper`) calls Chuvadi.Pdf.Text.Shaping,
whose shaper is a port of parts of HarfBuzz: the HarfBuzz notice ships in that package
(`THIRD-PARTY-NOTICES.md` at the package root). The Unicode data in Chuvadi.Layout is listed in Chuvadi.Layout's
`THIRD-PARTY-NOTICES.md`.

## Build (D-18, D-29, D-37)

This project is not in Chuvadi.slnx or CI. It takes chuvadi-pdf as **version-pinned packages** from the
local NuGet feed (`%USERPROFILE%/Documents/local-nuget`), never from a source clone:
- the version is pinned once, in `ChuvadiPdf.props` (`ChuvadiPdfVersion`, currently 3.30.2);
- the feed is set by the `nuget.config` next to each project that uses it (this one, `staging/tests/Chuvadi.Layout.Pdf.Tests`,
  `staging/samples/Chuvadi.Layout.Acceptance`, `staging/tests/Chuvadi.Layout.Acceptance.Tests`). `Chuvadi.*` packages come only from
  the local feed; everything else from nuget.org.

```
dotnet build staging\src\Chuvadi.Layout.Pdf -c Release
dotnet test staging\tests\Chuvadi.Layout.Pdf.Tests -c Release
```

To move to a newer chuvadi-pdf, pack it into the feed (chuvadi-pdf `build\pack.ps1 -Version x.y.z`, then copy the
.nupkg files into the feed), change `ChuvadiPdfVersion`, and rebuild. The tests use the OFL fonts bundled in
Chuvadi.Layout.Fonts and two synthetic fonts from chuvadi-pdf's tests (a .ttc and a CFF .otf, in the test project's
`Fonts` folder).
