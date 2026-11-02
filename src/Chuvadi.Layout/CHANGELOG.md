# Chuvadi.Layout — Changelog

## 0.1.0 (unreleased)

D-115 / D-121 (the importers as their own packages):

- The engine no longer references Chuvadi.Docs or Chuvadi.Sheets. The docx importer is Chuvadi.Layout.Docs
  (`DocsLayout`, `DocsImporter`, `DocsImportOptions`; were `DocumentLayout.OpenDocx`, `DocxImporter`,
  `DocxImportOptions`), the xlsx importer is Chuvadi.Layout.Sheets (`SheetsLayout`, `SheetsImporter`,
  `SheetsImportOptions`; were `DocumentLayout.OpenXlsx`, `XlsxImporter`, `XlsxImportOptions`), and
  Chuvadi.Layout.Documents (`AutoDetectLayout.Open`) keeps the one-call open for every format, encrypted files
  included. `DocumentLayout.Open(Stream)` lays out text and, for a docx, an xlsx or an encrypted Office file, throws
  `LayoutFormatException` naming the package (it used to lay them out).
- New: `DocumentLayout.Open(GridWorkbook, LayoutSourceFormat, importWarnings, options, ct)`, the grid twin of the
  flow overload, so importers outside the engine report their format and warnings.
- The importer tests moved with their code (Chuvadi.Layout.Docs.Tests, .Sheets.Tests, .Documents.Tests); the counts
  add up to the former total plus three new detection tests. No output change: corpus, fixtures, SVG hashes,
  warnings and font reports identical.

D-113 / D-115 (built-in fonts from Chuvadi.Fonts):

- Chuvadi.Layout references the new Chuvadi.Fonts package and adds its 22 LiPi Sans faces to every resolver as the
  built-in fonts, after `LayoutOptions.FontSources` (new `LayoutOptions.UseBuiltInFonts`, default true). Indic text
  and the script fallback no longer need `BundledFontSource.Instance`. The faces report `FontOrigin.Bundled`, as
  before. With `UseBuiltInFonts = false` and no other source, resolving still throws the "No fonts are available"
  `LayoutException`, whose message now names the switch.
- Chuvadi.Layout.Fonts keeps the 20 Office metric-compatible faces (Liberation, Carlito, Caladea); the 22 LiPi files
  and their licence moved unchanged to Chuvadi.Fonts (same bytes, checked by SHA-256). `BundledFontSource.ReadResource`
  is internal (D-115). Tests and samples read LiPi through `Chuvadi.Fonts.FontPack`. The reference corpus font
  configuration lists both font folders.
- No output change: the corpus, the fixtures and their SVG hashes, page counts, warnings and font reports are
  identical.

D-90 (page-dependent field values as text runs):

- Field values (PAGE, NUMPAGES, SECTIONPAGES, DATE, TIME, FILENAME and cached results) are evaluated when the
  paragraph is measured and laid out as ordinary text: shaped by the shaper for their script, with font fallback
  per character and UTF-16 clusters. New test `Page_dependent_field_values_are_shaped_as_text_runs` (a Tamil month
  name and a Tamil file name with an emoji in a header). The unused draw-time field override in `FlowEmitter`
  (one glyph per UTF-16 unit, never called) is removed. No output change: the reference corpus report is identical
  apart from timings.

D-60 (generic names only):

- No app, product or company names in Layout's code comments, docs, tests, samples or fixtures. INTEGRATION.md now
  describes a desktop viewer and a server-side preview service. No personal names or personal paths either: comments and docs
  name the local feed as `%USERPROFILE%\Documents\local-nuget` (the configured value is unchanged), and the cell
  comment in an xlsx test is by "Reviewer". No behaviour, default, output or API changed in this
  step.
- The reference-corpus cases d24, d27 and x18 and the preview scenarios use "Example Medical Center"; their sources and
  LibreOffice 24.2.7.2 references were regenerated (results unchanged: d24 and d27 pass, x18 stays calc-vs-excel).

D-44 (Microsoft Defender scans encrypted OOXML files on open by trying the default password, which can take seconds
before any library code runs):

- The encrypted docx and xlsx tests no longer write or open an encrypted package through a file path: three
  pre-generated packages (`tests/Chuvadi.Layout.Tests/Fixtures/encrypted`, SpinCount 1000) are embedded in the test
  assembly and read through a `MemoryStream`.
- The Viewer and the acceptance harness read the file into memory before their clocks start; the Viewer's `--timeout`
  counts from there and its read time is logged on its own line.

L8 on Chuvadi.Docs 1.6.0 (requires Docs 1.6.0; LAYOUT-INTEGRATION 1.6.0):

- Footnotes: `NoteReferenceInline` and `FlowNote`. A footnote is laid out at the foot of the page that holds its
  reference, under a two-inch separator; a line with references is placed only if its notes fit too. Endnotes follow
  the text at the end of the document. New `LayoutBoxKind.Footnotes`.
- Tracked changes are laid out as accepted (a loaded file goes through `AcceptAllRevisions()`; a `Document` passed in
  directly has its deleted runs and rows skipped). Bookmark, field and comment-range markers, comment references and
  annotation marks take no room. `w:sym` symbols are drawn in their font.
- Text boxes: `FlowTextBox`, `TextBoxInline` (inline) and `AnchoredTextBoxInline` (floating). Floating pictures and
  text boxes now share the `AnchoredObjectInline` base (geometry and wrap); `AnchoredImageInline` keeps its members.
  Fill, outline, insets, vertical alignment and auto-fit; content clipped to the box. New `LayoutBoxKind.TextBox`.
- Floating tables take their horizontal position (alignment or X from the column, margin or page); vertical position
  and wrapping are not applied (warning).
- Embedded fonts: `FlowDocument.EmbeddedFonts`, filled from the document's font table and searched first when
  `LayoutOptions.UseEmbeddedFonts` is on. New warning code `EmbeddedFontUnreadable`.
- POI corpus: 88/95 Word page counts exact (was 86); delins.docx and bib-chernigovka...docx now exact.
- Reference corpus: d42 to d48 (footnotes and endnotes, tracked changes and comments, fields, text boxes, floating
  table, symbols, embedded font); 48 docx and 25 xlsx.

D-40 reference corpus (staging/tests/Chuvadi.Layout.ReferenceCorpus: 41 docx and 25 xlsx with LibreOffice 24.2.7.2
references; REFERENCE-CORPUS.md). Layout fixes it found:

- Space between paragraphs is the larger of the first's space after and the next one's space before, not their sum.
- Line height follows Word: winAscent + winDescent plus the hhea line gap beyond them as leading
  (`FontFace.WordLineMetrics`).
- A paragraph holding only a page break no longer starts the next page with an empty line: the break run is a page
  break character (it was mapped to "page break before"), and the paragraph mark stays with the break.
- Table rows make room for the horizontal border above them; the last row (and a vertical merge reaching it) for the
  table's bottom border.
- A tab draws the leader of the stop it goes to (it took the next stop that had a leader).
- POI corpus: 86/95 Word page counts exact (was 85); drawing.docx now exact, bug59058.docx 56 to 54 pages (Word 52).
- A space between two complex-script words takes the complex-script font and size (it took the Latin slot), so Tamil
  lines are spaced as in LibreOffice.

Chuvadi.Layout.Pdf on Chuvadi.Pdf 3.26.0 (staging/, D-29 pinned packages):

- Indic output is no longer provisional: Chuvadi.Pdf 3.26 matches HarfBuzz 15.0.0 on all 19,641 conformance syllables
  of the nine LiPi Indic scripts.
- `PdfShaper` shapes the whole run and reports Chuvadi.Pdf's HarfBuzz clusters (UTF-16 indices) instead of shaping one
  syllable at a time, so cross-syllable kerning and contextual forms apply (Bengali প্রতিবেদন now matches HarfBuzz).
  The internal `PdfShaper.Syllables` helper is gone.
- Tests: Tamil extraction compares the text exactly (3.26 no longer inserts a space after an /ActualText span), and
  translucency expects sRGB blending (alpha 128 black over white gives 127).

L8 on Chuvadi.Docs 1.5.0 (requires Docs 1.5.0):

- Loaded hyperlinks keep their targets, so PDF and SVG links work for Word files.
- Positional tabs (`w:ptab`): new `TabInline.Positional` (`PositionalTab`: alignment, margin or indent area, leader).
  The engine places them ignoring tab stops. POI ThreeColHeadFoot.docx's header now reads in three columns.
- D-30 for docx: a section without `w:pgSz` uses the culture's paper (Letter or A4), in its orientation.
- The document's default tab stop (`w:defaultTabStop`) sets `FlowDocument.DefaultTabStop`.
- Encrypted packages are opened as documents first; Chuvadi.Docs 1.5.0 rejects non-Word packages, and a workbook
  then opens through Chuvadi.Sheets (the Sheets-first workaround is gone).
- POI corpus: unchanged at 113/130 laid out and 85/95 Word page counts exact (same files as on 6b).

Chuvadi.Layout.Pdf on Chuvadi.Pdf 3.25.0 (staging/, D-29 pinned packages):

- `PdfShaper` shapes with `TextShaper.Shape(FontFace, ...)` on a Chuvadi.Pdf face loaded once per Layout face; CFF and
  collection faces are shaped too. The adapter's own collection extraction (`SfntExtractor`) is gone.
- PDF text carries its source text (ToUnicode, or /ActualText for conjuncts and split vowels): Layout PDFs copy,
  search and extract as written. The text-extraction test runs (no skipped tests).
- Real clipping, translucency (opacity scopes), dash patterns, link annotations (URI and page), character scaling,
  synthetic italic and bold (text matrix and render mode), image crops, and CFF/.ttc embedding. The warnings
  PdfSyntheticItalic, PdfHorizontalScale, PdfTranslucency, PdfImageClip, PdfImageCrop and PdfCffFontNotEmbedded are
  gone; PdfFontNotEmbedded, PdfLinkInvalid and PdfTextNotExtractable are new.
- A test checks Chuvadi.Pdf's `FontFace` metrics against Layout's font core (units per em, glyph count, cmap,
  advances). Indic output is marked provisional until Chuvadi.Pdf 3.26.

L12 support: `LayoutSourceFormat.Html` (reported by Chuvadi.Layout.Markup's `HtmlLayout`);
`DocumentLayout.Open(stream, LayoutSourceFormat.Html)` explains that HTML is laid out by the add-on. A test now awaits
its background page-count task (xUnit1031).

D-37: the projects that take chuvadi-pdf as packages moved to `staging/` (`staging/src/Chuvadi.Layout.Pdf`,
`staging/tests/Chuvadi.Layout.Pdf.Tests`, `staging/tests/Chuvadi.Layout.Acceptance.Tests`,
`staging/samples/Chuvadi.Layout.Acceptance`, `staging/samples/Chuvadi.Layout.Viewer`), so CI's pure-BCL check needs no
exemption. No code changed. They return to `src/`, `tests/` and `samples/` with ProjectReferences when chuvadi-pdf
joins the monorepo.

L8 on Chuvadi.Docs 1.4.0 with Phase 6b (requires the Phase 6b bundle):

- **Pictures are the document's image runs**, loaded or built: in body, nested tables, headers and footers, at their
  exact run. The anchor-text heuristic, `DocxImportOptions.AnchorLoadedImages` and the ImageAnchorApproximated
  warning are gone.
- **Floating pictures** carry:
  - relative sizes;
  - alignment, then percentage, then point positions, with margin frames turned into page offsets;
  - simple positions;
  - wrap side, polygon and the four distances.
- **Crops** are applied (new `ImageCrop` on `ImageInline` and `AnchoredImageInline`). Rotation and flips warn
  (DrawingApproximated). Linked pictures draw a placeholder of their size.
- **Apache POI corpus:** 85 of the 95 Word-saved documents now match Word's page count (was 84). issue_51265_3.docx
  draws its three header pictures and four body pictures on one page, as Word does.

L7 on Chuvadi.Sheets 1.7.0 (requires Sheets 1.7.0; the importer no longer builds against 1.6.1).

- **Rich-text cells.** Each run gets its own font, size, bold/italic, underline, strike, super/subscript and
  colour, over the cell font. Theme-scheme run fonts resolve to the theme's fonts.
- **Cell hyperlinks.** External targets become links. Places in the workbook become link anchors
  (`GridCell.HyperlinkLocation`).
- **Repeat-fill number formats** (`FillIndex`). Accounting formats put the currency at the cell's left edge and the
  number at its right edge. Fill characters '.', '-', '_' and '·' are drawn as leaders (`GridCell.FillCharacter` /
  `FillIndex`).
- **Comments.** "At end of sheet" prints a "Cell: / Comment:" list after the sheet's pages. "As displayed" prints
  the shown comments in their boxes.
- **Header and footer pictures** (&G) are drawn where the code is (`GridPageSetup.Pictures`, keyed by Excel's
  shape ids).
- **Chart sheets** print one page each, in tab order, with the chart placeholder filling the printable area.
- **Load warnings.** Chuvadi.Sheets' load warnings, such as cells outside the grid, are reported.
- **Strict OOXML** and part names in any case or with '\' separators now read.
- **Apache POI corpus** (353 workbooks): 330 lay out (15,437 pages), up from 329 (14,506 pages). The other 23
  fail cleanly:
  - 14 fuzzed, truncated or corrupt packages;
  - 4 DTD/XML-bomb probes;
  - 3 encrypted or legacy OLE files;
  - a sniffing reject;
  - one 1,048,576-character cell over `MaxCharactersPerBlock`.

L10 support in the core:
- `DocumentLayout.Open(FlowDocument, LayoutSourceFormat, importWarnings, options, ct)` lets add-on importers report
  their format and warnings.
- `DocumentLayout.Open(stream, LayoutSourceFormat.Markdown)` points to Chuvadi.Layout.Markup.
- The Viewer and the acceptance harness lay out .md files through Chuvadi.Layout.Markup.

L8: docx importer (requires Chuvadi.Docs 1.4.0).

- **`DocxImporter` / `DocumentLayout.OpenDocx` / `DocumentLayout.Open(Document)`.** Documents are read with
  Chuvadi.Docs and laid out the way Word lays them out, from Chuvadi.Docs' effective formatting. Values are never
  altered. `DocumentLayout.Open(Stream)` sniffs .docx by its main part, and opens encrypted documents when
  `LayoutOptions.Password` is set (a workbook is tried first, then a document).
  - **Sections.** Page size, orientation, margins (an "exact" top or bottom margin too), gutter, mirror margins,
    start type, page-number format and restart.
  - **Headers and footers.** Default, first-page and even-page slots, with Word's linking rules.
  - **Text watermarks.** Drawn behind the text as rotated glyph outlines (new `FlowSection.Watermark`).
  - **Paragraphs.** Alignment, indents, spacing (auto spacing too), line spacing, keep and widow settings, page break
    before, tabs with leaders, borders, shading, contextual spacing, list labels from `GetListLabels`.
  - **Runs.** Fonts per script, with theme fonts; sizes and bold/italic per script; all underline styles and
    colours; strikes; theme colours with tint/shade; automatic colour, white on dark shading; highlight and shading;
    caps; super/subscript; spacing; scaling; kerning; baseline shift; hidden text (`IncludeHiddenText`).
  - **Breaks, tabs and fields.** PAGE, NUMPAGES and SECTIONPAGES are computed; other fields show their saved result.
    Hyperlinks are carried.
  - **Tables.** Grid widths, spans, legacy horizontal merges, vertical merges, resolved cell borders and shading,
    cell margins, row heights and rules, header rows, cant-split rows, hidden rows, nested tables, ragged rows.
  - **Images.** Inline and floating, with anchors, alignment, offsets and wrapping. Loaded pictures are anchored
    approximately (Chuvadi.Docs 1.4.0 does not keep them in the paragraph model), with warning
    ImageAnchorApproximated.
  - **Hints.** The page count Word recorded is the initial estimate, and the core title becomes the document title.
  - **Limits.** They map onto Chuvadi.Docs' reader limits, and its security-limit exceptions become
    `LayoutLimitExceededException`.
- **Acceptance harness.** Docx files are now compared. A new `--layout-only` mode lays out sources without a
  reference PDF. Unexpected exceptions are reported as CRASH and the run continues.
- **Apache POI corpus.** All 130 .docx files: 113 lay out (426 pages) and 17 fail cleanly with format or limit
  errors. Of the 95 saved by Word, 84 match Word's recorded page count exactly and 5 are one page off.

L7 checked against docs/Chuvadi.Sheets/LAYOUT-INTEGRATION.md (Sheets 1.6.1). These differences were
fixed:
- **Right-to-left sheets** (`sheetView/@rightToLeft`): column A is printed at the right, by mirroring
  the printed block (`GridSheet.RightToLeft`). Explicit left and right alignments are mirrored with
  their cells (UnsupportedFeature note). Headers and footers are not mirrored.
- **`zeroHeight`**: rows are hidden unless the file shows them (`GridSheet.RowsHiddenByDefault`).
- **"Show formulas" view**: prints `=formula`, left-aligned, instead of the value.
- **Icon sets**: drawn at the cell's left edge as simplified vector icons, with warning
  IconSetApproximated.
  - Arrows, traffic lights, signs, symbols, flags, ratings, boxes, quarters, stars and triangles are
    covered.
  - `ShowValue = false` hides the value, and the text is kept clear of the icon.
  - New model: `GridCell.Icon` (`GridIcon`).
- **Printer-specific paper codes** (no standard size) fall back to the D-30 culture paper, no longer
  Letter.
- **Flipped connectors and lines** run between the flipped corners. Flipped pictures warn
  (DrawingApproximated). New model: `GridDrawing.FlipHorizontal` / `FlipVertical`.

Viewer and integration (D-30, D-31).

- **`LayoutSvg`** (`Chuvadi.Layout.Drawing`). A pure-BCL SVG writer for pages and display lists.
  - Glyphs are drawn from their TrueType outlines (defs plus `<use>`), so the page looks the same in every
    browser and WebView2 without the font installed.
  - An invisible text layer gives selection, copy and find.
  - Links are filtered to `AllowedUriSchemes` (http, https, mailto by default).
  - Internal links go to `PageLinkHref` / `AnchorHref`.
  - Also: image crops, clips, dashes, translucency, an `ImageHref` hook for hosted images, an `IdPrefix` for
    inlining several pages, responsive sizing, cancellation, and output warnings SvgCffFontAsText,
    SvgImageFormat and SvgLinkDropped.
  - The output never contains script, event attributes or external references the caller did not ask for.
- **Chuvadi.Layout.Pdf.** `ToSvg` now uses `LayoutSvg` (settings: `LayoutPdfOptions.LayoutSvg`). The
  chuvadi-pdf route is kept as `ToSvgViaPdf`: it showed the wrong characters in browsers, because shaped
  glyphs have no ToUnicode.
- **D-30 paper default.** `PaperDefaults.ForCulture` / `PageForCulture` give Letter for the Letter regions
  and A4 elsewhere.
  - `XlsxImportOptions.DefaultPaperSizeCode` is now `int?`: null follows the culture, and a value overrides it.
  - `TextImportOptions.Page` is now `PageGeometry?`: null follows the culture.
- **`LayoutLimits.ForUntrustedInput()`.** Preset limits for server-side rendering of attachments.
- **Background resolve.** `LaidOutDocument.ResolvePageCount` releases the document lock after each page,
  so `GetPage` calls from another thread interleave with it.
- **`samples/Chuvadi.Layout.Viewer`.** Opens any docx, xlsx or txt and writes per-page SVG and PNG, a lazy
  `index.html` and a report.
  - Options: page ranges, password, culture, system fonts, `--untrusted` limits and `--timeout`.
  - Exit codes: 0 done, 1 unreadable, 2 bad arguments, 3 cancelled, 4 limit exceeded.
- **`docs/Chuvadi.Layout/INTEGRATION.md`.**
  - Desktop viewer (web view): lazy SVG per page, background page count, virtual host, provisional pages.
  - Server-side previews: PNG, memory-only, untrusted limits, timeouts, DPI and concurrency sizing, error
    mapping.

D-29: Chuvadi.Layout.Pdf, its tests and the acceptance harness take chuvadi-pdf as version-pinned
packages (3.22.1, from `src/Chuvadi.Layout.Pdf/ChuvadiPdf.props`) from the local NuGet feed. Each of
those projects has its own `nuget.config` naming the feed. The `ChuvadiPdfRoot` source-clone
references are gone, and the adapter tests use the fonts bundled in Chuvadi.Layout.Fonts.

L7: xlsx importer (requires Chuvadi.Sheets 1.6.1).

- **`XlsxImporter` / `DocumentLayout.OpenXlsx`.** Workbooks are read with Chuvadi.Sheets and printed
  the way Excel prints them. `DocumentLayout.Open(Stream)` now sniffs .xlsx (also when part names use
  `\`). It also opens an encrypted workbook when `LayoutOptions.Password` is set.
  `DocumentLayout.Open(Workbook)` takes an already loaded workbook.
  - **Cells.**
    - The displayed text comes from Chuvadi.Sheets' number formatter. Values are never altered.
    - The number format's [colour] is applied.
    - "Show zeros" off and the "print errors as" setting (blank, `--`, `#N/A`) are honoured.
    - Formatted empty cells print their fill and borders.
    - Cells are typed as text, number, boolean or error.
  - **Styles.**
    - The effective style is the cell's, then the row's, then the column's.
    - Fonts, theme and indexed colours, underline, strikethrough, superscript and subscript.
    - Solid fills. Pattern and gradient fills are drawn as one blended colour, with the
      FillApproximated warning.
    - All 13 border styles, including diagonals.
    - Alignment, wrap, shrink, indent and rotation (rotation is still drawn horizontally).
  - **Sheet.** Column widths (with `baseColWidth`), row heights, hidden rows and columns, merges, and
    hidden sheets (skipped by default).
  - **Page setup.**
    - Paper: the file's code, or `DefaultPaperSizeCode` when none is stated (code 0 counts as none).
    - Orientation, margins, scale, fit-to (0 means automatic), centring, gridlines, headings, page
      order and first page number.
    - Odd, even and first headers and footers, with their flags.
    - Black-and-white printing (no fills, black text and borders).
    - Draft printing (no gridlines and no drawings).
    - Print areas, including whole columns `A:C` and whole rows `1:5`, print titles, and manual page
      breaks.
  - **Conditional formats.**
    - Evaluated by Chuvadi.Sheets over every formatted cell in the used range, including empty cells,
      so zebra stripes print.
    - Applied: fills, fonts, borders, number formats, colour scales and data bars.
    - Icon sets are not drawn (warning).
    - Unevaluable rules give the ConditionalFormatNotEvaluated warning.
  - **Drawings.**
    - Pictures (with crop), shapes (rect, roundRect, ellipse and lines, with fill, outline and text)
      and groups, clipped to each page.
    - The printed range extends to include them.
    - Charts are drawn as labelled placeholders (ChartPlaceholder warning).
    - Rotation is not applied (DrawingApproximated warning).
- **Grid IR additions:**
  - `GridCellStyle.BorderDiagonalUp` and `BorderDiagonalDown`;
  - `GridCell.DataBar` (`GridDataBar`);
  - `GridSheet.BaseColumnWidth` and `GridSheet.Drawings` (`GridDrawing`, `GridAnchorPoint`,
    `GridDrawingKind`).
- **`LayoutImage.FromEncoded`** detects PNG, JPEG, GIF, BMP, TIFF, EMF, WMF and SVG, and reads the
  pixel size from the header.
- **New warning codes:** DrawingApproximated, ChartPlaceholder, FillApproximated and
  ConditionalFormatNotEvaluated.

L11: text beside floating objects, nested tables across pages, performance, acceptance harness.

- **Performance**: the first page of a long table no longer waits for the whole table, because rows
  are measured on demand. Faster lookups for Unicode properties, scripts and glyph ids, plus
  line-breaking fast paths, make paragraph measurement about twice as fast. DESIGN.md section 13 has
  the timings and budgets.
- **Acceptance harness** (`samples/Chuvadi.Layout.Acceptance`, outside Chuvadi.slnx like the PDF
  adapter):
  - Compares each corpus file with its Word or Excel reference PDF: page count, per-page text and
    line positions.
  - Writes a Markdown and a TSV report.
  - `--self-test` checks the harness itself.

- **Square, tight and through wrapping**: text flows beside floating images line by line instead of
  being moved below them.
  - Each row takes the free space beside the object and its wrap distances.
  - `AnchoredImageInline.WrapSide` (OOXML wrapText): both sides (the left space, then the right
    space, of each row), left only, right only, or the larger side.
  - Tight and through follow `AnchoredImageInline.WrapPolygon` (OOXML wp:wrapPolygon, given in
    points) row by row; without a polygon they use the rectangle.
  - Through does not fill gaps inside the polygon; the TextWrapApproximated warning says so.
  - Spaces narrower than 18 pt are skipped. When a row has no usable space, text continues below
    the object.
  - Paragraphs beside an object continue on the next page from the exact character where they
    stopped. Widow and orphan control count rows, and a row's pieces stay on one page.
  - Page- and margin-relative objects anchored lower on the page also push aside the text above
    their anchor: the page is laid out again with them placed first.
  - Tables next to an object are placed below it, with the TextWrapApproximated warning.
- **Nested tables split across pages** between their rows. The outer row splits with them. A single
  nested row is still not split.

L6: headers and footers, fields, floating images.

- **Headers/footers** (`FlowSection.HeadersFooters`):
  - Default, first-page (`DifferentFirstPage`) and even-page (`DifferentOddEven`) parts; each part
    inherits from earlier sections as in OOXML.
  - Paragraphs and tables; laid out per page at the header/footer distances.
  - A tall header pushes the body down and a tall footer pushes it up.
  - Header and Footer boxes.
- **Fields** evaluated per page: PAGE (section number format: decimal, roman, letters), NUMPAGES,
  SECTIONPAGES, DATE/TIME (`LayoutOptions.Now`, .NET format string), FILENAME
  (`LayoutOptions.DocumentName`); other fields show their cached result.
  - Pages drawn before the page count is final are `IsProvisional` (warning ProvisionalPageCount).
  - `LaidOutDocument` rebuilds them on request once the count is final.
- **Floating images** (`AnchoredImageInline`):
  - Positioned relative to page, margin/column or paragraph, by offset or alignment
    (start/centre/end).
  - Appear on the page of the anchor paragraph's first line.
  - Behind-text images paint under the text and in-front images over it; neither moves text.
  - Top-and-bottom wrapping moves text below the image (with its distances).
  - Square/tight/through wrapping was laid out as top-and-bottom in L6; L11 flows text beside
    the object.
- **Paragraph decorations**: shading and borders are now drawn in headers, footers and table
  cells, and border spacing counts there too.

L5: flow tables.

- **Grid**: column widths from w:tblGrid; missing widths share the remaining width equally; column
  spans; table indent and alignment (left/centre/right).
- **Cells**: margins (table defaults and per-cell overrides); paragraphs and nested tables; space
  before/after inside cells; vertical alignment top/centre/bottom; shading.
- **Rows**: height rules auto/at-least/exact (exact clips).
- **Vertical merges**: restart cells span their rows; the last spanned row grows when the merged
  content needs more room.
- **Pagination**:
  - Header rows (w:tblHeader, the leading run) repeat on every continuation page.
  - Rows that do not fit split at item boundaries and continue on the next page.
  - Rows that cannot split (w:cantSplit, exact height, vertical merges) move whole when they fit on a
    fresh page.
  - Nested tables were kept together in L5; L11 splits them between rows.
  - `MaxNestingDepth` is enforced.
- **Borders**: table outer and inside borders with per-cell overrides; each edge is drawn once;
  continuation pages draw a top edge; merged cells draw no inner edges; double borders.
- **Output**: Table and TableCell boxes with source paths; extracted text has tab-separated cells
  and one line per row.

L4: grid engine (Excel printing).

- **Grid IR** (`Chuvadi.Layout.Model`):
  - `GridWorkbook`, `GridSheet`: sparse cells; Normal font; default width/height; columns and rows
    with hidden flags; merges; print areas; print titles; manual row/column breaks.
  - `GridCell`: displayed text as Excel shows it (never recalculated), value kind, rich-text runs,
    hyperlink.
  - `GridCellStyle`: font, fill, borders, horizontal/vertical alignment, wrap, shrink-to-fit, indent,
    rotation.
  - `GridPageSetup`: paper, orientation, Excel default margins, header/footer margins, scale,
    fit-to-page width/height, centring, gridlines, headings, page order, first page number,
    odd/even/first headers and footers, align-with-margins, scale-with-document.
  - Helpers: `CellRange`, `CellAddress` (A1 parse/format).
- **Geometry**: Excel column widths from the max digit width (ECMA-376 18.3.1.13). Default columns
  derived from baseColWidth are rounded up to a multiple of 8 px (Calibri 11 → 64 px). Stored or
  default row heights; rows without a stored height grow to fit wrapped text.
- **Pagination**:
  - Print areas (each separately) or the used range.
  - Fit-to-page: largest whole percent, 10–100%, at which the area fits; manual breaks are ignored
    when fitting. Otherwise scale 10–400%.
  - Bands across and down; manual breaks; print titles repeated on later pages and counted in the
    available space.
  - Page order down-then-over or over-then-down; hidden rows, columns and sheets skipped.
  - Page numbering continues across sheets unless a first page number is set.
  - The page count is final at open; cells are laid out only for the page requested.
- **Cells**:
  - General alignment (numbers right; text left; booleans and errors centred); left, centre,
    right, fill, justify, centre-across, distributed; indent (3 × MDW per level).
  - Wrap; shrink to fit; `####` for numbers that do not fit.
  - Text spills into empty neighbours in the direction of its alignment and is clipped at
    non-empty ones.
  - Vertical top/centre/bottom; merged cells drawn once across their range.
  - Paint order: fills → gridlines → headings → text → borders. Gridlines are suppressed inside
    merges; printed row/column headings.
- **Headers/footers**: Excel codes &L &C &R, &P (±n), &N, &D, &T, &F, &Z, &A, &"font,style", &nn,
  &B, &I, &U, &E, &S, &X, &Y, &K (RGB), &&. Multi-line sections; anchored to header/footer margins;
  scale with the document. &G pictures warn.
- `DocumentLayout.Open(GridWorkbook)`. Limits: MaxCellsPerSheet, MaxPages. Rotated text warns.

L3: flow engine.

- **Entry points**: `DocumentLayout.Open(FlowDocument)`, `Open(Stream, LayoutSourceFormat)` (sniffs ZIP Office /
  OLE / text), `OpenText(Stream | string, TextImportOptions)`. Inputs are read into memory and never
  modified; nothing is written to disk.
- **LaidOutDocument**: lazy pages.
  - `GetPage` lays out from the nearest checkpoint; LRU page cache.
  - `PageCount`: estimate until `IsPageCountFinal`; `ResolvePageCount`.
  - `GetPages`, `FontSubstitutions`, `Warnings`, `Source`.
  - Calls serialise internally.
- **LaidOutPage**: `DisplayList`, `Boxes` (paragraph/line/image with source paths), `GetText()`,
  `PageLabel`, `IsProvisional`.
- **Display list** (D-17, `Chuvadi.Layout.Drawing`):
  - Ops: `GlyphRunOp` (face, size, positioned glyphs, source text, colour, synthetic bold/italic,
    horizontal scale), `FillRectOp`, `StrokeLineOp` (dashes), `PathOp`, `ImageOp`,
    `PushClipOp`/`PopClipOp`, `LinkOp`.
  - Supporting types: `LayoutColor`, `LayoutRect`.
  - `LayoutDisplayList.GetUsedGlyphs()` for subsetting.
- **Flow IR** (`Chuvadi.Layout.Model`): `FlowDocument`, sections (`PageGeometry`, start type, page
  numbering), paragraphs (`ParagraphFormat`, list labels), inlines (`TextRun` with hyperlinks,
  `TabInline`, `BreakInline`, `FieldInline`, `ImageInline`), effective `CharacterFormat` (ASCII /
  high-ANSI / East Asian / complex-script font slots and sizes, bold/italic per slot, underline styles,
  strike, double strike, colour, highlight, caps, small caps, super/subscript, spacing, scale, kerning
  threshold, baseline shift, hidden). Table and header/footer types are defined for L5/L6.
- **Paragraph layout**:
  - Per-character font slots with per-code-point fallback; shaping through `IShaper`.
  - Greedy first-fit line breaking on UAX #14 opportunities; trailing spaces hang; emergency breaks
    at cluster boundaries; soft hyphens shown only at a break.
  - Tabs: left/centre/right/decimal with leaders; default stops; custom stops clear defaults to
    their left; implicit stop at a hanging indent.
  - Indents; alignment left/centre/right/justify (last line ragged; lines ending in a line break
    stretched; only spaces after the last tab)/distribute.
  - Line spacing multiple/exact/at-least using Windows (Word) font metrics.
  - Space before/after (added, contextual spacing).
  - Paragraph shading and borders; top and bottom borders take their width plus spacing.
- **Pagination**:
  - Widow/orphan control; keep with next (chains); keep lines together; page break before; page and
    column breaks.
  - Space before suppressed at the top of a page reached by flow.
  - Sections: new page, continuous (same page size), odd/even (blank page inserted).
  - Page numbering restarts and formats (decimal, roman, letters); mirror margins; gutter.
  - Multi-column sections warn and lay out as one column.
- **Rendering details**: underline and strike from the font's post/OS/2 metrics; highlight behind
  text; inline images; EMF/WMF/other formats as placeholder boxes with `ImagePlaceholder`.
- **Plain text**: BOM detection (UTF-8/16/32), UTF-8 with Latin-1 fallback, one paragraph per line, tab
  size in characters, form feed = page break. Default font Courier New 10 pt (→ Liberation Mono).
- **Limits**: MaxPages, MaxBlocks, MaxCharactersPerBlock, MaxLayoutBytes (measurement cache),
  MaxSourceBytes. Cancellation is checked per paragraph and per page.
- **Warning codes**: `LayoutWarningCodes` (ComplexScriptNotShaped, BidiNotSupported,
  MultiColumnNotSupported, NonMetricFontSubstitution, ImagePlaceholder, ContentTallerThanPage,
  UnsupportedFeature, ProvisionalPageCount).
- **ManualTests**: `--preview <folder>` writes diagnostic SVG pages with glyphs drawn from their
  TrueType outlines, plus "-boxes" overlays and a font/warning report per scenario.

L2: text segmentation and shaping.

- **LineBreaker**: UAX #14 revision 53 (Unicode 16.0), rules LB1–LB31, including LB28a for Brahmic
  orthographic syllables.
  - `GetOpportunities` returns NoBreak / Allowed / Mandatory per UTF-16 position.
  - Passes the complete Unicode `LineBreakTest.txt`.
- **GraphemeClusters**: UAX #29 revision 45 extended grapheme clusters, including GB9c (Indic
  conjuncts) and GB11 (emoji ZWJ sequences). Passes the complete `GraphemeBreakTest.txt`.
- **UnicodeTextProperties**: Line_Break, Grapheme_Cluster_Break, Indic_Conjunct_Break, East Asian
  wide, Extended_Pictographic and combining-mark lookups, generated from UCD 16.0.0 by
  `docs/Chuvadi.Layout/tools/gen_textprops.py`.
- **Shaping**: `IShaper`, `ShapedGlyph`, `ShapingOptions` (script, language, kerning, ligatures).
  - `SimpleShaper`: cmap + pair kerning; default-ignorable characters produce no glyph; declines
    complex scripts.
  - `LayoutOptions.Shaper` takes a complex-script shaper (PdfShaper from Chuvadi.Layout.Pdf).
- **ScriptItemizer**: script runs with Common/Inherited resolved to their neighbours.
- `THIRD-PARTY-NOTICES.md` carries the Unicode License v3 for the generated tables.

L1: font core and font resolution.

- **FontFace**: parses TrueType, OpenType/CFF and TrueType collections (`.ttc`/`.otc`, by face index).
  - Identity: legacy and typographic family/subfamily, full and PostScript names, version, weight,
    width, italic, fixed pitch, embedding rights (fsType).
  - Metrics: head/hhea/OS/2/post metrics (Windows, hhea and typographic line metrics; underline;
    strikeout; x-height; cap height).
  - Characters and advances: cmap formats 0/4/6/10/12/13 with the Windows Symbol fallback;
    advance widths; `hdmx` device widths.
  - Pair kerning: GPOS `kern` lookups (pair adjustment formats 1 and 2, extension lookups), falling
    back to the legacy `kern` table.
  - Every read is bounds-checked; malformed fonts throw `FontFormatException`; damaged kerning data
    degrades to no kerning.
- **Excel max digit width**: `FontFace.MaxDigitWidthPixels(size, dpi)`, per ECMA-376 18.3.1.13
  (hdmx when present, otherwise floored unhinted advances). Reproduces Calibri 11 → 7 px and
  Arial 10 → 7 px.
- **Font sources**: `IFontSource`, `FontFaceDescriptor`.
  - `MemoryFontSource` (caller bytes).
  - `DirectoryFontSource`: lazy scan that reads only directories and name/OS/2/head; unreadable
    files are skipped.
  - `SystemFontSource`: Windows, macOS and Linux font directories; opt-in only.
- **FontResolver**: resolution order is family across sources → substitutes → generic class →
  last resort. Per-code-point script fallback via `ResolveForCodepoint`.
  - Style matching follows CSS Fonts 4 for style and weight; synthetic bold/italic flags.
  - `ReportedSubstitutions`: each substitution reported once, with match kind, metric
    compatibility, origin and script.
- **FontSubstitutionTable**: editable family, generic, script and last-resort rules.
  `CreateDefault()` maps Arial/Times New Roman/Courier New/Calibri/Cambria to metric-compatible OFL
  substitutes and the Indic scripts to LiPi Sans.
- **EmbeddedFontObfuscation**: ECMA-376 17.8.1 de-obfuscation of embedded `.odttf` fonts. The input
  is never modified.
- **UnicodeScripts**: Script property lookup generated from UCD 16.0.0, plus Indic, right-to-left and
  complex-shaping classification.

L0: scaffold.

- `LayoutOptions`: font sources, system fonts opt-in, embedded fonts, substitution table, limits,
  culture, clock, document name, password, page cache.
- `LayoutLimits`: pages, cells per sheet, blocks, nesting depth, resource bytes, layout bytes,
  characters per block.
- `LayoutWarning` with severities.
- Exception hierarchy: `LayoutException`, `LayoutFormatException`, `FontFormatException`,
  `LayoutLimitExceededException`.
