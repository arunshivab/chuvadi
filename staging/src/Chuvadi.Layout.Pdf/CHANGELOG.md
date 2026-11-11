# Chuvadi.Layout.Pdf — Changelog

## 0.1.0 (2026-11-12)

First release (D-214, D-304). Tagged as `layout-pdf-v0.1.0` right after `layout-v0.2.0`, so its dependency floor is
Chuvadi.Layout 0.2.0.

- **Output for Chuvadi.Layout pages**, memory-only (nothing is written except to the stream you pass):
  - `ToPdf`: a whole document into a stream, or one page as a byte array;
  - `ToPng`: one page as a PNG at a chosen resolution (150 dpi by default);
  - `ToSvg`: one page, through Chuvadi.Layout's own `LayoutSvg` writer (no PDF round trip); `ToSvgViaPdf` renders
    through chuvadi-pdf's SVG renderer instead, kept for comparison.
- **`PdfShaper`**, a Chuvadi.Layout `IShaper` for complex scripts. It shapes through Chuvadi.Pdf.Text.Shaping, a
  port of HarfBuzz's OpenType and Indic shapers that matches HarfBuzz 15.0.0 glyph for glyph on all 19,641 test
  syllables of the nine LiPi Indic scripts, Tamil included. Each face is loaded once and its shaping tables cached.
- **PDF text stays usable**: every shaped cluster gets a ToUnicode entry, or `/ActualText` when several glyphs draw
  it, so PDFs copy, search and extract as written. Faces are embedded and subset (TrueType, CFF .otf and .ttc).
- **What maps to what**: glyph runs (character scaling, synthetic italic and bold), rectangles, lines and paths with
  dashes, translucency, real PDF clipping, images with crops and PNG alpha as a soft mask, links to URIs or pages.
  The table is in the README.
- **Limitations are reported**, never silent, through `LayoutPdfOptions.Warning`: `PdfFontNotEmbedded` (for example
  CFF2; that text is left out), `PdfImageFormat` (GIF, EMF, WMF and SVG images become placeholders),
  `PdfImageUnreadable`, `PdfInternalLink`, `PdfLinkInvalid` and `PdfTextNotExtractable`.
- **Chuvadi.Pdf range `[3.31.2, 4.0.0)`** (D-213, D-261). NuGet picks the lowest version the range allows, so a plain
  reference gets 3.31.2. An application that wants a newer chuvadi-pdf 3.x references it directly. Nothing in Layout
  depends on chuvadi-pdf's exact output bytes.
- **The Chuvadi.Pdf packages are not part of this release.** Take them from the chuvadi-pdf release
  (https://github.com/arunshivab/chuvadi-pdf/releases/tag/v3.31.2). The release build fetched exactly those files and
  checked them against the hashes pinned in `tools/chuvadi-pdf/hashes/`.
- Trim- and NativeAOT-compatible (`IsTrimmable`, `IsAotCompatible`; D-217). Pure BCL apart from the Chuvadi.Pdf
  packages; no third-party code of its own (the HarfBuzz notice ships in Chuvadi.Pdf.Text.Shaping).
