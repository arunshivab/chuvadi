# Chuvadi.Layout — design

Status: approved by the Chuvadi coordinator on 2026-10-28 (decisions D-17 and D-18 below). This
document is the reference for the library; changes to it go through a PR like code.

## 0. Facts the design rests on (verified in the repositories on 2026-10-28)

1. Monorepo `origin/main` at `9456f70`: tags `sheets-v1.2.0`, `docs-v1.1.0`; Print 0.2.0 untagged;
   all projects `net10.0`. Docs 1.2 and Sheets 1.3.0/1.4.0 work was not pushed.
2. Docs 1.1.0's reader is text-only (`DocxReader.Paragraphs()` returns text and style id;
   `Tables()` returns `string[][]`). There is no run/format/section/numbering read model on main.
3. Sheets 1.2.0 on main: streaming `RowReader`, `CellStyleTable`, `GetStyleIndex`. No print setup,
   defined names or display text on main.
4. chuvadi-pdf v3.22.1 has two different `PageDisplayList` types
   (`Chuvadi.Pdf.Rendering.DisplayList` and `Chuvadi.Pdf.Rendering.Raster`). The DisplayList
   `TextOp` addresses fonts by `FontKey` into `PdfDictionary` font dictionaries.
   `SvgRenderer.Render(PageDisplayList)` is public.
5. `Chuvadi.Pdf.Text.Shaping` covers Latin plus nine Indic scripts. It has no Arabic, no CJK, no
   bidi and no line breaking, and `TextShaper.Shape(byte[] ttf, …)` reparses the font on every call.
6. OFL fonts available: Liberation Sans/Serif/Mono (chuvadi-pdf `tools/fonts`, version 2.1.5);
   LiPi Sans as variable WOFF2 (chuvadi-pdf `assets/fonts/lipi-sans`). Carlito and Caladea come
   from their upstream repositories (see `src/Chuvadi.Layout.Fonts/FONTS.md`).
7. Neither Chuvadi.Pdf nor Chuvadi.Sheets is published on nuget.org.

## 1. Decisions

| Id | Decision |
|---|---|
| D-07 | docx/xlsx are view-only in the desktop viewer; editing stays PDF. |
| D-08 | Chuvadi.Layout paginates Docs and Sheets content into fixed pages, print-faithful, with documented tolerance under font substitution. One engine for desktop viewers (SVG per page), server-side previews (PNG, memory-only, untrusted input), print-to-PDF and export to PDF from apps. |
| D-09 | HTML/MD/TXT on-screen viewing is done by the viewer app; their print/export goes through Layout later. |
| D-17 | Layout owns a neutral page display list. It does not expose chuvadi-pdf types. The consumer requirements were corrected accordingly. |
| D-18 | The PDF/SVG/PNG adapter is `Chuvadi.Layout.Pdf` in the monorepo (under `staging/` since D-37). It stays out of `Chuvadi.slnx` and CI. It starts with the one-page-PDF route. |
| D-29 | Projects never reference a sibling repository's source clone; nupkgs in the local NuGet feed are the handoff. The adapter, its tests and the acceptance harness take chuvadi-pdf as version-pinned PackageReferences (`staging/src/Chuvadi.Layout.Pdf/ChuvadiPdf.props`) through a `nuget.config` scoped to each of those projects. |
| D-30 | When a source states no paper size, it follows the region of `LayoutOptions.Culture`, the way Windows printer defaults do: Letter for US, PR, CA, MX, PH, BZ, CL, CO, CR, GT, NI, PA, SV and VE; A4 everywhere else, including the invariant culture (`PaperDefaults`). This applies to xlsx sheets (`SheetsImportOptions.DefaultPaperSizeCode` remains an explicit override), docx sections without `w:pgSz` (`PageSetup.SizeStated == false`, Chuvadi.Docs 1.5.0; orientation is kept), and plain text (`TextImportOptions.Page`). |
| D-31 | SVG is written by the core library (`Chuvadi.Layout.Drawing.LayoutSvg`, pure BCL), not through a PDF round trip. Glyphs are drawn from their TrueType outlines, with an invisible text layer for selection and search. Reason: chuvadi-pdf's SVG renderer derives characters from glyph ids, and shaped runs have no ToUnicode, so browsers showed the wrong letters. The adapter's `ToSvg` delegates to it; `ToSvgViaPdf` is kept for comparison. |
| D-36 | Sheets' copy of the shared `OoxmlPackage` is canonical. Apply order: Docs p1b, shared-ooxml, Sheets 1.7.0, then Docs 6b onward. |
| D-37 | Everything that takes chuvadi-pdf as packages lives under `staging/` (`staging/src/Chuvadi.Layout.Pdf`, `staging/tests/Chuvadi.Layout.Pdf.Tests`, `staging/tests/Chuvadi.Layout.Acceptance.Tests`, `staging/samples/Chuvadi.Layout.Acceptance`, `staging/samples/Chuvadi.Layout.Viewer`), so CI's pure-BCL check over `src/` and `shared/` needs no exemption. When chuvadi-pdf moves into the monorepo they return to `src/`, `tests/` and `samples/` with ProjectReferences (end state A). |
| D-32 | Markdown (and later the HTML subset) is laid out by a separate add-on package, `Chuvadi.Layout.Markup`, from Chuvadi.Markup's public Markdown AST. The core does not reference Chuvadi.Markup; `DocumentLayout.Open(FlowDocument, LayoutSourceFormat, importWarnings, ...)` lets add-ons report their format and warnings. TXT stays in the core (`TextImporter`). |
| D-113 | The LiPi Sans fonts are their own package, Chuvadi.Fonts (static TTF, about 3.9 MB, no dependencies), shared with Chuvadi.Pdf. Chuvadi.Layout uses them as built-in fonts (`LayoutOptions.UseBuiltInFonts`, default true) after `FontSources`; Chuvadi.Layout.Fonts keeps the Office metric-compatible substitutes. |
| D-115 | The importers are their own packages before the first tag: Chuvadi.Layout.Docs (Chuvadi.Docs) and Chuvadi.Layout.Sheets (Chuvadi.Sheets); the engine depends on neither. Chuvadi.Layout.Documents (`AutoDetectLayout.Open`) keeps the former one-call `Open(stream)` for every format, encrypted files included. `BundledFontSource.ReadResource` is internal. Package, namespace and class names avoid Microsoft format names (D-121); format identifiers such as `LayoutSourceFormat.Docx` stay. |
| — | Docs/Sheets coupling: one `Chuvadi.Layout` package with ProjectReferences to Docs and Sheets (added when the importers land), plus a public IR. Sheets was added in L7 (Chuvadi.Sheets 1.6.1). |
| — | Fonts: a separate `Chuvadi.Layout.Fonts` OFL pack; system fonts opt-in; Layout's own internal sfnt core, to be promoted to `shared/Chuvadi.Internal/Fonts` later; Excel max-digit width follows the documented rule. |
| — | Shaping: `IShaper`, with the Pdf shaper supplied through the adapter. Arabic/Hebrew bidi, Thai-family breaking and East Asian doc-grid are out of 1.0 and produce warnings. |
| — | Fidelity bar: tiered (§9). Reference PDFs must state the printer used. |
| — | Markdown parsing and HTML sanitising live in a separate library, Chuvadi.Markup. Layout consumes its AST for MD/HTML print/export. |
| — | EMF/WMF images: in 1.0, a placeholder box at the declared size plus a warning. |

## 2. Architecture

```
importers (own packages since D-115)            ┌─ Layout IR (public) ─┐
  .docx  → DocsImporter   (Layout.Docs)      ──► │ FlowDocument         │
  .xlsx  → SheetsImporter (Layout.Sheets)    ──► │ GridWorkbook         │
  .txt (core) / .md, .html (Layout.Markup)   ──► └──────────┬───────────┘
                                                            ▼
                     Engine (pure BCL, no chuvadi-pdf)
                       Fonts:  sfnt reader, resolver, substitution report, metrics
                       Text:   UAX #14 line breaks, UAX #29 clusters, IShaper
                       Flow paginator (docx/txt/md)   Grid paginator (xlsx)
                       Checkpoints, lazy pages, limits, cancellation
                                                            ▼
                     LaidOutDocument / LaidOutPage  →  LayoutDisplayList (public, neutral)
                                                            ▼
                     Chuvadi.Layout.Pdf (separate package): ToPdf / ToSvg / ToPng / PdfShaper
```

The core assembly builds and tests in any sandbox. Everything that needs chuvadi-pdf lives in the
adapter.

## 3. Public API (target)

```
namespace Chuvadi.Layout
static class DocumentLayout
  LaidOutDocument Open(Stream, LayoutSourceFormat, LayoutOptions?, CancellationToken)   // text; names the package for docx/xlsx
  LaidOutDocument OpenText(Stream | string, ...)
  // since D-115: DocsLayout.Open (Chuvadi.Layout.Docs), SheetsLayout.Open (Chuvadi.Layout.Sheets),
  //              AutoDetectLayout.Open (Chuvadi.Layout.Documents: sniffs docx/xlsx/txt, encrypted too)
  LaidOutDocument Open(FlowDocument ir, ...) / Open(GridWorkbook ir, ...)

sealed class LaidOutDocument : IDisposable       // GetPage calls serialise internally; pages immutable
  int PageCount; bool IsPageCountFinal; int ResolvePageCount(CancellationToken)
  LaidOutPage GetPage(int index, CancellationToken)
  IReadOnlyList<FontSubstitution> FontSubstitutions; IReadOnlyList<LayoutWarning> Warnings
  LayoutSourceInfo Source

sealed class LaidOutPage
  int Index; double WidthPt, HeightPt; bool IsProvisional
  LayoutDisplayList DisplayList; IReadOnlyList<LayoutBox> Boxes; string GetText()

sealed class LayoutOptions   FontSources, AllowSystemFonts, UseEmbeddedFonts, FontSubstitutions,
                             Shaper, Limits, Culture, Now, DocumentName, Password, PageCacheSize
LayoutLimitExceededException, LayoutFormatException, FontFormatException (all : LayoutException)
```

The adapter adds `doc.ToPdf(Stream, …)`, `page.ToSvg(…)`, `page.ToPng(dpi, …)` as extension
methods, and `PdfShaper : IShaper`.

## 4. Page display list

Units are points, origin top-left, y down. The list is immutable and in paint order.

| Op | Content |
|---|---|
| GlyphRun | font handle, size, baseline origin, glyphs {GlyphId, X, Y, Advance, Cluster}, source text, fill colour |
| FillRect, StrokeLine | colour, width, dash, cap (borders, gridlines, shading, underline, strike) |
| Path | simple vector path |
| Image | image handle (bytes, format, pixel size), destination rect, crop |
| PushClip / PopClip | rectangle clip |
| Link | rectangle + URI or internal target |

The font handle exposes the original bytes and the used-glyph set, so the adapter can subset and
embed fonts.

`LayoutSvg` (D-31) maps the list to SVG 1.1:
- each distinct glyph is one `<path>` in `<defs>`, read from 'glyf'/'loca' including composite glyphs, and placed with
  `<use>` transforms;
- synthetic italic becomes `skewX(-12)`, and synthetic bold a round-joined stroke of 1/40 em;
- an invisible `<text>` per run carries the source text, stretched over the run with `textLength`;
- clips become `<clipPath>`, dashes `stroke-dasharray`, and alpha becomes fill/stroke opacity;
- image crops become a nested viewport;
- links become `<a>`, filtered to the allowed URI schemes;
- ids carry a caller-chosen prefix, so several pages can share one HTML document.

CFF (.otf) faces are not read: their runs are written as `<text>` in the face's family, with warning SvgCffFontAsText.

## 5. Fonts

- **Resolution order:** fonts embedded in the document (ODTTF de-obfuscated) → `FontSources` in
  order → system fonts (only when `AllowSystemFonts`) → the family's substitutes → generic class →
  script fallback (per code point) → last resort. Every result other than an exact match is
  reported once in `FontSubstitutions`.
- **Metric-compatible substitutes:**
  - Arial/Helvetica → Liberation Sans
  - Times New Roman → Liberation Serif
  - Courier New → Liberation Mono
  - Calibri → Carlito
  - Cambria → Caladea
- **Non-metric substitutes** are reported as `Substitute`, so consumers can say "breaks may move".
- **Line height** for Word/Excel uses OS/2 usWinAscent + usWinDescent (`VerticalMetricsSource.Windows`).
- **Excel max digit width** (ECMA-376 Part 1, 18.3.1.13): ppem = round(size × dpi / 72). The device
  advance is taken from `hdmx` when present; otherwise the unhinted advance is floored to whole
  pixels. This reproduces Excel's reference points: Calibri 11 → 7 px, Arial 10 → 7 px. Real Office
  fonts carry `hdmx`, so with system fonts allowed the hinted value is used. Sheets' auto-fit will
  move onto this core when it is promoted to `shared/`.

## 6. Text

- **Line breaking:** UAX #14 from UCD 16.0.0 tables generated into C# (`docs/Chuvadi.Layout/tools`),
  plus Word-compatibility tailoring.
- **Clusters:** UAX #29 grapheme clusters. Indic text breaks only at cluster boundaries and is
  measured with shaped advances.
- **Shaping:** `IShaper`. The core ships a simple shaper (cmap, pair kerning). The adapter supplies
  `PdfShaper` for Indic. Without it, Indic text is measured cmap-only and a `ComplexScriptNotShaped`
  warning is raised. On Chuvadi.Pdf 3.26 the adapter's Indic shaping matches HarfBuzz 15.0.0 (100% of the Pdf
  library's 19,641-syllable conformance corpus across the nine LiPi Indic scripts).
- **Out of 1.0, with warnings:** bidi (Arabic/Hebrew), Thai/Lao/Khmer dictionary breaking, and East
  Asian doc-grid / vertical text.

## 7. Lazy layout

- **docx (flow).** Page N depends on every page before it. A forward break pass records a
  checkpoint per page. `GetPage(N)` runs the break pass to N (measurement only) and emits ops for
  page N only, with an LRU of built pages. `PageCount` starts from `docProps/app.xml <Pages>` (or an
  estimate) and becomes final when the pass reaches the end. Pages whose fields depend on the final
  count are `IsProvisional` until then.
- **xlsx (grid).** Breaks come from stored row heights and column widths, so `PageCount` is exact
  and cheap, and `GetPage` materialises only that page's window.
- **Cancellation** is checked per line, table row and sheet row block. **Limits** throw
  `LayoutLimitExceededException` before allocating.

## 8. Phase plan

| Phase | Content | Size |
|---|---|---|
| L0 | Scaffold, options, limits, exceptions, warnings | S |
| L1 | Font core, resolver, substitution report, ODTTF, Chuvadi.Layout.Fonts pack | M–L |
| L2 | UAX #14 + UAX #29, simple shaper, IShaper | M |
| L3 | Flow engine and display list, checkpoints, limits, cancellation | XL |
| L4 | Grid engine (print area, fit-to, titles, breaks, order, headers/footers) | L |
| L5 | Flow tables | XL |
| L6 | Headers/footers, fields, images | L |
| L7 | Xlsx importer (after Sheets 1.4 on main) | M |
| L8 | Docx importer (Chuvadi.Docs 1.6.0) | M |
| L9 | Chuvadi.Layout.Pdf adapter (built by the Pdf chat) | M |
| L10 | MD importer (Chuvadi.Layout.Markup on Chuvadi.Markup 1.0.0); TXT in the core | M |
| L11 | Acceptance harness, corpus, perf (ongoing) | M |

`layout-v0.1.0` (preview) follows L4; `1.0.0` follows the acceptance corpus.

## 9. Fidelity bar (tiered)

| Tier | Content | Target |
|---|---|---|
| A | Text, sections, lists, headers/footers, xlsx paging | page count, page-break positions and per-page text identical to the reference |
| B | Tables and floating objects | ≥ 90% of corpus files identical; the rest reported as Fidelity warnings |

- Layout boxes must match within ≤ 2 pt (or ≤ 1% of the page dimension) with the original font, and
  within a wider, documented band with a metric-compatible substitute.
- Reference PDFs come from Word/Excel (or Google's PDF export) on a Windows machine and must state the
  printer used, because Excel's pagination depends on the printer driver.

### 9.1 Acceptance harness (L11)

`staging/samples/Chuvadi.Layout.Acceptance` measures the corpus against these tiers. Like the PDF adapter, it reads
PDFs with chuvadi-pdf (pinned packages, D-29), so it is outside `Chuvadi.slnx` and CI. Its tests are in
`staging/tests/Chuvadi.Layout.Acceptance.Tests`.

- **Input:** a folder holding source documents and reference PDFs, paired by `manifest.tsv` (source,
  reference, printer, notes) or by file name.
- **Text:** both sides are read the same way. Glyphs with their positions come from the display list
  and from the PDF's text runs. They are grouped into lines by baseline. Text is normalised: whitespace
  dropped, NFKC, soft hyphens and zero-width characters removed.
- **Tier A:** same page count and identical normalised text on every page, which means identical page
  breaks.
- **Geometry:** lines are matched by longest common subsequence. Every reference line must be found,
  with its baseline and left edge within the tolerance: 2 pt by default, or 1% of the page when a font
  was substituted.
- **Output:** `acceptance-report.md` and `acceptance-report.tsv` (per file: verdicts, largest offsets,
  first text difference, layout timings). Formats whose importer has not landed yet are reported as
  skipped.
- **Self-test (`--self-test`):** generates references from Layout's own lines, then checks that the
  harness passes a faithful reference and catches a moved line, changed text, a missing page and a
  moved page break.

## 10. Engine constants and approximations (subject to the acceptance corpus)

These values are not published by Microsoft. They are chosen to match observed output and will be
tuned when a Word/Excel reference corpus is available. None of them moves a line break except where noted.

| Item | Value | Affects breaks? |
|---|---|---|
| Superscript / subscript size | 2/3 of the run size; rise 1/3 of the size, drop 0.14 of the size | yes (width) |
| Small caps | lowercase letters drawn as capitals at 0.8 of the size | yes (width) |
| Multiple line spacing | extra space goes below the text (baseline at the line top + ascent) | no |
| Exact / at-least line spacing | text bottom-aligned in the line box | no |
| Space before at the top of a page | suppressed when the page was reached by flow; kept at document start and after page or section breaks | yes |
| Paragraph borders | top and bottom borders add width + spacing to the paragraph height; adjacent identical borders are not merged | yes |
| Tab leaders | leader glyphs on a grid of their own advance | no |
| Keep with next | a chain taller than the page body is ignored | yes |
| Excel default column | baseColWidth-derived default rounded up to a multiple of 8 px | yes |
| Excel printed size | 96-dpi pixel geometry converted at 0.75 pt/px; real Excel output varies with the printer driver | yes |
| Excel cell padding | 2 px left/right; text 1 px from the top/bottom edge | no |
| Excel indent | 3 × max digit width per level | no |
| Excel auto row height for wrapped text | lines × line height + 2 px, never below the default row height | yes |
| Excel text overflow past the used range | not drawn (the page holds only used-range columns); to verify against Excel | no |
| Excel border widths (print) | hair 0.25 pt; thin, dotted, dashed, dash-dot 0.5 pt; medium styles 1 pt; thick 1.5 pt; double = two 0.5 pt lines | no |
| Excel pattern fills | foreground over background blended by ink coverage (darkGray 75%, mediumGray 50%, lightGray 25%, gray125 12.5%, gray0625 6.25%, dark lines 50%, light lines 25%); gradients as the average of their stops | no |
| Data bars | inset 2 px; length = Sheets' LengthPercent of the cell width; negative bars from the right edge; drawn solid | no |
| Paper when the file states none | `SheetsImportOptions.DefaultPaperSizeCode`, default 1 (Letter, the format's default); Excel uses the printer's default | yes |
| Shape text | Normal font, 7.2 pt / 3.6 pt insets, top-left | no |
| Narrowest space beside a floating object | 18 pt; narrower spaces are skipped | yes |
| Both-sides wrapping | each row fills the left space, then the right space | yes |
| Tight/through wrapping | per row, the polygon's horizontal extent over the row's height (gaps inside the polygon are not used) | yes |
| Floating object reaching above its anchor | page- and margin-relative objects reflow the text above their anchor (up to three extra layout passes per page); paragraph-relative objects do not | yes |
| Text height used to find a row's free space | the row's height at full width | yes |

## 11. Warning codes

| Code | Meaning |
|---|---|
| ComplexScriptNotShaped | A complex script was laid out with nominal glyphs (no shaper supports it). |
| BidiNotSupported | Right-to-left text was laid out left-to-right. |
| MultiColumnNotSupported | A multi-column section was laid out as one column. |
| NonMetricFontSubstitution | A substitute that is not metric-compatible was used; breaks may move. |
| ImagePlaceholder | An image format that cannot be drawn (EMF/WMF/…) became a placeholder box. |
| ContentTallerThanPage | A line or row taller than the page body was clipped. |
| UnsupportedFeature | A source feature is not laid out by this version. |
| ProvisionalPageCount | NUMPAGES used the estimate before the count was final. |
| DrawingApproximated | A sheet drawing was simplified: rotation not applied, or a preset shape drawn as its rectangle. |
| ChartPlaceholder | A chart was drawn as a labelled placeholder. |
| FillApproximated | A pattern or gradient cell fill was drawn as one blended colour. |
| ConditionalFormatNotEvaluated | A conditional format could not be evaluated (unsupported function, external reference) and was not applied. |
| IconSetApproximated | Conditional-format icons were drawn as simplified vector icons. |
| SvgCffFontAsText | (LayoutSvg) A CFF face's runs were written as SVG text in that family. |
| SvgImageFormat | (LayoutSvg) TIFF, EMF, WMF or an unknown image became a placeholder box. |
| SvgLinkDropped | (LayoutSvg) A link with a disallowed scheme, or an internal link without an href, was dropped. |
| TextWrapApproximated | Wrapping was approximated: through wrapping laid out as tight, or a table moved below a floating object instead of flowing beside it. |

## 12. Known gaps in 1.0

- No bidi.
- No Thai-family dictionary breaking.
- No doc-grid or vertical text.
- EMF/WMF images render as placeholders.
- Through wrapping does not place text in gaps inside the wrap polygon. Tables do not flow beside
  floating objects; they are placed below them.
- Paragraph-relative floating objects that reach above their anchor paragraph do not reflow the
  text above it. Page- and margin-relative objects do.
- A single row of a nested table does not split across pages (nested tables split between rows).
  Vertically merged cells do not split their content.
- xlsx (L7, Chuvadi.Sheets 1.7.0):
  - Charts, including chart sheets, print as labelled placeholders.
  - Icon sets are drawn as simplified vector icons (IconSetApproximated).
  - Rotated cell text is drawn horizontally.
  - Right-to-left sheets are mirrored as a block; explicit left/right cell alignments mirror with them.
  - A repeat-fill character other than space, '.', '-', '_' or '·' is drawn as space (FillApproximated).
  - Comments printed "at end" use one wide column at the Normal font's digit width; Excel's own layout of that list
    may break differently.
- docx (L8, Chuvadi.Docs 1.6.0):
  - Fields other than PAGE, NUMPAGES and SECTIONPAGES show their saved result (PAGEREF is not recomputed).
  - A footnote is laid out whole on the page of its reference (a note too long for the page is not continued on
    the next); footnotes in table cells and headers keep their label but their text is not laid out; numbering
    that restarts on each page counts through the section. Endnotes always go to the end of the document.
  - Tracked changes are laid out as accepted (no markup view); comment balloons are not drawn.
  - Floating tables are placed at their horizontal position in the flow: no vertical position and no wrapping.
  - Text boxes are drawn upright; tables inside an inline text box are not laid out. Shapes other than text boxes,
    frames (`w:framePr`) and content-control boundaries are not modelled by Chuvadi.Docs.
  - Multiple text columns, page borders, line numbers, vertical page alignment, diagonal and rotated cells,
    between/bar paragraph borders and right-aligned list numbers are reported and laid out simply.
- HTML (L12, Chuvadi.Markup 1.0.0):
  - Style sheets (`<style>`) are not applied, only inline styles and presentational attributes. Floats, positioning,
    flex and grid lay out as normal flow. Rounded corners, shadows and background images are not drawn.
  - Table column widths come from estimated text widths, not measured ones. Inline-block boxes (mail buttons) show
    only their text background, not their padding or border.
  - Nested block quotes show the innermost bar only. SVG, MathML, form controls, audio and video are left out with a
    warning.
- WOFF/WOFF2 are not read by the font core; the pack ships TTF.
- CFF-outline fonts are read for metrics; outline rendering is the adapter's concern. The PDF adapter embeds CFF and
  collection faces (since Chuvadi.Pdf 3.25); the core SVG writer draws CFF runs as SVG text (SvgCffFontAsText).

## 13. Performance (L11)

Budgets: open plus page 1 within 50 ms for flow text, and within 100 ms for long tables and large
sheets. ManualTests `--perf` fails a scenario only when it exceeds 3× its budget, so slower machines
pass. These timings are from a 2-vCPU Xeon at 2.8 GHz, .NET 10, Release:

| Scenario | Pages | Open + page 1 | Page count (forward pass) | Any page rebuilt |
|---|---|---|---|---|
| Text, 1 MB (OpenText) | 252 | 14 ms | 0.75 ms/page | 1.1 ms |
| Flow, 3000 paragraphs | 563 | 15 ms | 0.9 ms/page | 0.5 ms |
| Flow, 600 wrapped images | 125 | 3 ms | 0.75 ms/page | 0.65 ms |
| Table, 5000 rows × 5 | 115 | 50 ms | 2.2 ms/page | 2.4 ms |
| Grid, 100 000 cells | 417 | 74 ms | (computed at open) | 1.4 ms |

L11 changes behind these numbers:
- Table rows are measured on demand, one row ahead. A 5000-row table's first page went from 375 ms to
  50 ms.
- BMP lookup tables replace binary searches for the Unicode properties (two-level, 64-entry blocks)
  and scripts (64 KB).
- Line-breaking fast paths cover the commonest pairs (LB7, LB18, LB28). They stay under the full
  conformance test.
- Each face caches the glyph ids for U+0000..U+07FF.
- Font slot resolution is remembered per text piece. The font resolution cache is read without a
  lock.
- Together these doubled the speed of the forward pass, for example 3000 paragraphs went from 1.1 s to
  0.5 s.

## 14. Docx import (L8)

`DocsImporter` (Chuvadi.Docs 1.6.0) builds the flow IR from Chuvadi.Docs' effective formatting, as its
LAYOUT-INTEGRATION.md prescribes: `GetEffectiveRunFormat` / `GetEffectiveParagraphFormat` (with a `TableCellRef`
inside tables), `GetEffectiveTableFormat`, `GetEffectiveCellFormat` (every cell carries its four resolved edges, so
the table draws no borders of its own), `GetListLabels`, and `doc.Sections`.

- **Sections.** A section ends at the paragraph that carries its properties. Header and footer slots follow Word's
  linking: own content; else, when linked, the previous section's slot; else blank. Every slot is set explicitly, so
  the engine never inherits a second time. Odd/even headers apply when `DifferentOddAndEvenPages` is true, or unset
  with even content present (what Chuvadi.Docs writes). A watermark belongs to its section; a section whose headers
  are all linked shows the previous one.
- **Theme.** Theme colours with tint and shade (HSL lightness, as Word) and theme fonts resolve against theme1.xml,
  else the Office 2013–2022 theme. Automatic text colour is black, or white over a dark cell, paragraph or run
  background (perceived brightness below half; Word's threshold is not documented).
- **Defaults.** A run with no font gets Times New Roman, and no size 10 pt (ECMA-376 defaults). Widow control is on
  unless the paragraph or a style turns it off (Word's behaviour). "Auto" paragraph spacing is 14 pt
  (`DocsImportOptions.AutoParagraphSpacing`).
- **Tables.** The grid is the file's column widths, else the table width (points or percent) shared equally. Grid
  columns skipped by `gridBefore`/`gridAfter` become empty, borderless cells. Legacy `hMerge` runs become spans.
- **Watermarks** are drawn by the engine: the text is shaped, its glyph outlines are stretched to the watermark box
  (Word's WordArt "fit shape"; the box defaults to Chuvadi.Docs' own rule) and rotated 45° for diagonal ones, as
  translucent filled paths under everything else.
- **Pictures** are Chuvadi.Docs 6b's image runs, for loaded and built documents alike, in body, tables, headers and
  footers. Inline pictures flow at their run. Floating ones anchor to their paragraph.
  - Size: the relative size (percent of the page or a margin) wins over the stated size.
  - Position: alignment, then the percentage offset, then points.
  - Frames that start at the page's left or top edge (left/top margin) become page offsets.
  - Also carried: crop (an image source rectangle), wrap type, side, polygon and the four distances.
  - Rotation and flips are drawn upright (DrawingApproximated).
  - Linked pictures with no bytes draw a placeholder of their size.
- **Corpus.** All 130 .docx files of the Apache POI test corpus, run with `--layout-only`:
  - 113 lay out (425 pages);
  - the other 17 fail cleanly: fuzzer-made and truncated packages, two password-protected files, an XXE probe and a
    table nested beyond the XML-depth limit.
  - Of the 95 saved by Word that lay out, 85 match the page count Word recorded in docProps/app.xml exactly and 5
    are one page off.
- **Chuvadi.Docs 1.5.0 additions.** Loaded hyperlinks keep their targets (`Run.HyperlinkUrl`, a `w:anchor` appended as
  `#fragment`). Positional tabs (`Run.PositionalTab`) become `TabInline.Positional`: the engine moves to the left,
  centre or right of the margin or indent area, ignoring tab stops, with the leader. Sections without `w:pgSz`
  follow D-30. Tabs beyond the last stop use `Document.EffectiveDefaultTabStopPt` (settings `w:defaultTabStop`).
  Encrypted packages are tried as documents first, since Chuvadi.Docs now rejects a package whose main part is not
  a Word document (a workbook then opens through Chuvadi.Sheets).
- **POI corpus on 1.5.0.** 113 of 130 files lay out (425 pages); 17 fail cleanly as before. 85 of the 95 Word-saved
  files match Word's page count and 5 are one page off, the same files as on 6b. In this corpus, 8 files have
  positional tabs, 12 have external hyperlinks, 42 state a default tab stop other than 36 pt (35.4 or 35.45 pt) and
  6 have a section without `w:pgSz`; none of these changes a page count.
- **Chuvadi.Docs 1.6.0 additions** (LAYOUT-INTEGRATION 1.6.0):
  - Footnotes: `NoteReferenceInline` draws the label from `GetNoteReferences()`; the note's blocks (`FlowNote`) are
    laid out at the foot of the page that holds the reference, under a two-inch separator line in an empty line of
    the note's paragraph-mark format. A line with references is placed only if its notes fit as well, so the notes
    take their room from the body. The note's own mark (`IsNoteMark`) draws the same label.
  - Endnotes follow the text at the end of the document, under the same separator.
  - Tracked changes: a loaded file is laid out with `AcceptAllRevisions()` (deleted runs, rows and paragraph marks
    go); a `Document` passed in directly is not changed, its deleted runs and rows are skipped instead.
  - Markers (bookmarks, field begin/separate/end, comment ranges), comment references and annotation marks take no
    room. `w:sym` symbols are drawn in their font.
  - Text boxes: `TextBoxInline` (in the line, like a picture) and `AnchoredTextBoxInline` (floating, with the
    picture's position and wrap rules through the shared `AnchoredObjectInline` base); content inside the insets,
    clipped to the box, aligned top/centre/bottom; fill and outline; auto-fit grows the box to its content.
  - Floating tables (`TableFormat.FloatingPosition`): XAlign or X in points, from the column, margin or page,
    become the table's alignment or indent.
  - Embedded fonts (`doc.Fonts`, de-obfuscated by Chuvadi.Docs) go to `FlowDocument.EmbeddedFonts`, searched before
    every other source when `LayoutOptions.UseEmbeddedFonts` is on (default).
- **POI corpus on 1.6.0.** 113 of 130 files lay out (426 pages); the same 17 fail cleanly. 88 of the 95 Word-saved
  files match Word's page count (85 on 1.5.0): delins.docx (tracked changes now accepted) and
  bib-chernigovka...docx are exact now; 3 are one page off. 35 of the 95 have a footnotes part.

## 15. Markdown (L10)

`Chuvadi.Layout.Markup` maps Chuvadi.Markup's AST to the flow document: one section on the culture's paper, a page
footer, and paragraph and character formats from a `MarkdownStyleSheet` (see its README for the mapping). List labels
hang in the indent and use the engine's implicit hanging-indent tab, as Word lists do. Block quotes are a left border
plus indent; nested quotes show the innermost bar. Table columns share the text width in proportion to their longest
cell (3 to 40 characters). Images are never fetched: they come from `data:` URIs or the caller's `ImageResolver`.
Raw HTML is printed literally by default, as Chuvadi.Markup's renderer shows it; HTML documents go through the HTML
importer of the same package (§16).

## 16. HTML (L12)

`Chuvadi.Layout.Markup` also lays out HTML, so apps can print mail and web pages to PDF.

- **Pipeline.** Untrusted HTML goes through Chuvadi.Markup's sanitizer first (a new `SanitizerPolicy`, the safest
  policy, by default; the caller's policy on request). The sanitized DOM (`SanitizeResult.Fragment`) is mapped to the flow
  document. A caller that already holds a `SanitizeResult` (the one its web view shows), or a trusted DOM, passes that
  instead. Nothing is fetched: images are `data:` URIs (or the caller's `ImageResolver`), and blocked remote images
  print their alt text.
- **Style.** Each element's computed style is: the browser's user-agent defaults (`HtmlStyleSheet`: 16 px Times New
  Roman, 2em to 0.67em headings, 40 px indents, blue underlined links), then the presentational attributes of mail
  HTML (align, bgcolor, `<font>`, width, height, valign, nowrap, list types), then the `style` attribute (normal, then
  `!important`). Inherited properties follow CSS: colour, font, size, weight, style, decoration (propagated),
  alignment, indent, line height (a number inherits as a factor) and white-space. Style sheets (`<style>`) are not
  applied (UnsupportedFeature warning); floats and positioning are ignored.
- **Blocks.** Vertical margins collapse (the larger of adjacent margins is kept, and the first block on the page has
  none), padding and the element's own left and right borders become indents and paragraph bars, top and bottom borders
  go on the first and last paragraph, and a block's background shades its paragraphs. An empty block with a height
  (spacer `div`s) keeps that height. Whitespace collapses as in CSS (`pre`, `pre-wrap` and `pre-line` keep what they
  keep); `&nbsp;` is text.
- **Lists.** Markers by type and nesting (disc, circle, square, decimal, decimal-leading-zero, alpha, roman, Greek,
  none), `start`, `value` and `reversed`. Markers hang in the list's indent; in centred or right-aligned items they sit
  next to the text, as in a browser.
- **Tables.** HTML's table model: thead rows first (and repeated on each page), then tbody, then tfoot; colspan and
  rowspan (rowspan 0 to the end), missing cells left empty. Column widths approximate CSS automatic layout: minimum and
  preferred content widths (estimated from character counts at the run's size), fixed and percentage widths, the
  table's stated width, then the rest by preference. `cellpadding`, `cellspacing`, `border`, `bgcolor` (cell, row,
  row group, table), alignment, `valign` (middle by default), row heights and centring (`align=center`, `margin:auto`,
  `<center>`) are kept.
- **Layout tables.** A one-column table nested in a table, or one that wraps other tables, is page layout (the
  wrapper and container tables of mail). It is laid out as blocks: its width and centring become indents, its cells
  become blocks with their padding, background and alignment. Long mail then splits across pages like text, because
  the engine does not split a single row of a nested table. Tables nested deeper than `LayoutLimits.MaxNestingDepth`
  are laid out as paragraphs, with a warning.
- **Hidden content.** `display:none`, `visibility:hidden`, the `hidden` attribute, `mso-hide:all`, `opacity:0`,
  `max-height:0` with `overflow:hidden` (mail preheaders) and text at `font-size:0` are not printed.
- **Corpus.** 657 HTML files (Postmark's and Mailchimp's mail templates, two other template sets, the test pages of
  two HTML-to-text libraries, and 420 documentation pages on the build machine) lay out into 2,502 pages. The one
  failure is a clean `MarkupMaxInputChars` limit error (a 15 MB page against the strict 8 MB). No file has content
  clipped as taller than a page.
