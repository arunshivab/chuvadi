# Chuvadi.Docs — integration guide for a paginator (Chuvadi.Layout)

This page lists exactly which public APIs a layout engine should call to lay out a loaded `.docx` the way Word does. It is accurate as of Chuvadi.Docs **1.4.0 + Phase 6b** (exact image anchoring). Everything lives in the `Chuvadi.Docs.Word` namespace.

## 1. Load

```csharp
var doc = Document.Load(path);                    // or Load(stream, password, DocxReaderOptions, CancellationToken)
```

Untrusted input goes through `DocxReaderOptions`, which holds the limits; a breach throws `DocxSecurityLimitException`. Any malformed part throws `XmlException` / `DocxFormatException`, except a malformed header, footer or settings part, which loads blank.

## 2. Document structure — walk in this order

| What | API |
|---|---|
| Body content in order | `doc.Blocks` → each item is a `Paragraph` or a `DocTable` |
| Sections | `doc.Sections`. A section ends at the paragraph whose `Paragraph.SectionProperties` is set; the last section is `doc.Section` (the body sectPr). |
| Table content | `DocTable.Rows[r].Cells[c]` has one entry per **grid column**: a spanned cell sits at its first column with `ColumnSpan` > 1, and the columns it covers hold empty placeholder cells. `TableCell.Blocks` gives paragraphs and nested `DocTable`s in order. `DocTable.Columns`, `DocTable.ColumnWidthsPt`, `TableRow.GridBefore` / `GridAfter`. |
| Merges | `TableCell.VerticalMerge` (`Restart` / `Continue`), `TableCell.HorizontalMerge` (legacy), `TableCell.ColumnSpan` |

## 3. Formatting — always use the *effective* values

Never read raw `Paragraph.Format` or `Run.Format` for layout: they hold direct formatting only. The resolvers apply document defaults, the table style (with its conditional regions), the numbering level, the paragraph style chain, the character style chain, and direct formatting. Toggles combine by XOR, as in ECMA-376 §17.7.

| Need | API |
|---|---|
| Run (character) formatting | `doc.GetEffectiveRunFormat(paragraph, run)`; inside a table cell `doc.GetEffectiveRunFormat(paragraph, run, new TableCellRef(table, row, column))` |
| Paragraph formatting (spacing, indents, tabs, borders, shading, keep/widow, alignment) | `doc.GetEffectiveParagraphFormat(paragraph)`; inside a table `GetEffectiveParagraphFormat(paragraph, TableCellRef)`. List items get their level's indents and number tab. |
| Table formatting | `doc.GetEffectiveTableFormat(table)` (width, alignment, indent, layout, borders, default cell margins, cell spacing, look, band sizes) |
| Cell formatting | `doc.GetEffectiveCellFormat(new TableCellRef(table, row, column))`. All four edges are resolved (`Borders.Top`/`Left`/`Bottom`/`Right`, where null means no border), plus shading, margins, vertical alignment, text direction, no-wrap and fit-text. |
| Row properties | `TableRow.HeightPt` + `HeightRule`, `IsHeader` (repeat on each page), `CantSplit`, `Hidden`, `CellSpacingPt` |
| Fonts per script | Effective `TextFormat`: `AsciiFont`, `HighAnsiFont`, `EastAsiaFont`, `ComplexScriptFont`, the theme slots (`AsciiThemeFont`, `HighAnsiThemeFont`, `EastAsiaThemeFont`, `ComplexScriptThemeFont`) and `FontHint`. `SizePt` / `SizeComplexScriptPt` and `Bold` / `BoldComplexScript` select by script. Theme fonts and colours resolve against `doc.ThemeXml` (raw theme1.xml). |

Styles themselves are in `doc.Styles` (`StyleDefinition`) and `doc.Defaults`. A paginator needs them only for introspection; the resolvers already apply them.

## 4. Text inside a run

Each `Run` is one `w:r`:
- `Run.TextContent` carries `\t` for tabs and `\n` for line breaks.
- `Run.Break` (`Page`, `Column`, `TextWrappingClear*`) is an explicit break run.
- `Paragraph.PageBreakBefore` means a page break before the paragraph.
- U+2011 is a non-breaking hyphen and U+00AD a soft hyphen.
- `Run.HyperlinkUrl` marks a link; style it as `Hyperlink`.

## 5. List numbering

```csharp
var labels = doc.GetListLabels();                 // IReadOnlyDictionary<Paragraph, ListLabel>, computed once per call
if (labels.TryGetValue(paragraph, out var l))
    // l.Text ("2.3.", "iv)", "•"), l.Format (effective label TextFormat), l.Suffix (Tab / Space / Nothing),
    // l.LevelDefinition.Alignment (label alignment), l.Level, l.Number
```

Counting follows Word: per story (the body, and each header/footer separately), restarts, legal numbering, and numbering that comes from styles.

## 6. Sections, page geometry, headers and footers

For each `Section s` in `doc.Sections`:

| Need | API |
|---|---|
| Page size as laid out | `s.Page.WidthPt`, `s.Page.HeightPt` (orientation applied) |
| Margins | `s.Page.TopMarginPt` / `BottomMarginPt` (a negative value means "exactly"), `LeftMarginPt`, `RightMarginPt`, `GutterPt`, `HeaderDistancePt`, `FooterDistancePt`; document-wide `doc.MirrorMargins` and `doc.GutterAtTop` |
| Section start | `s.Start` (null = next page; `Continuous`, `EvenPage`, `OddPage`, `NextColumn`) |
| Columns | `s.Columns.Count` / `SpacingPt` / `Separator`; `EqualWidth == false` → use `s.Columns.Columns` (`WidthPt`, `SpaceAfterPt`) |
| Vertical alignment, borders, line numbers, grid, direction | `s.VerticalAlignment`, `s.PageBorders`, `s.LineNumbering`, `s.Grid`, `s.TextDirection`, `s.RightToLeft` |
| Page numbers | `s.PageNumbering.Format` (a `NumberFormat`; format the value with the same rules as list labels), `Start` (restart value, null = continue), `ChapterStyle` / `ChapterSeparator` |
| Headers/footers | `s.Header` / `s.Footer` (default), `s.FirstPageHeader` / `FirstPageFooter` (used when `s.DifferentFirstPage`), `s.EvenPageHeader` / `EvenPageFooter` (used when `doc.DifferentOddAndEvenPages == true`; odd pages use the default) |
| Linked headers | If a slot has no content (`HeaderFooterContent.Count == 0` with no tables) and `LinkToPrevious == true`, use the same slot of the previous section. If `LinkToPrevious == false`, the slot is blank. |
| Header content | `HeaderFooterContent.Blocks` (paragraphs and `DocTable`s in order). Lay it out like body content, using the same effective-format APIs. |
| Watermark | `s.Watermark` (`TextWatermark`: `Text`, `Font`, `ColorHex`, `Opacity`, `Diagonal` (315° when true), `WidthPt` / `HeightPt`, where null means auto). Draw it centred on the margins, behind the text. |

## 7. Images

Since 6b, every picture is an image run in the paragraph model, for documents built in code and for loaded documents alike:

```csharp
foreach (var placed in doc.GetImagePlacements())   // body (tables, nested tables), then headers/footers
{
    ImageSpec img = placed.Image;                    // Bytes, ContentType, WidthPt, HeightPt, Crop, RotationDegrees, FlipHorizontal/Vertical, EffectExtent
    Paragraph anchor = placed.Paragraph;             // the paragraph holding the run (anchor paragraph of a floating image)
    int at = placed.RunIndex;                        // position among anchor.Runs (inline images flow here)
    // placed.Host (Body/Header/Footer), placed.Section, placed.HeaderFooter, placed.Cell (TableCellRef?)
}
```

| Need | API |
|---|---|
| Inline picture | `img.Placement == ImagePlacement.Inline`. It flows as a glyph of `WidthPt` × `HeightPt` (plus `InlineDistance`, `EffectExtent`) at `RunIndex`. |
| Floating picture | `img.Placement == ImagePlacement.Floating`, geometry in `img.Position` (`FloatingPosition`) |
| Position | `HorizontalAnchor` / `VerticalAnchor` (page, margin, column, character, paragraph, line, the margin variants) with `HorizontalOffsetPt` / `VerticalOffsetPt`. Precedence: `HAlign` / `VAlign`, then `HorizontalOffsetPercent` / `VerticalOffsetPercent` (percent of the anchor frame's width/height; Word 2010+), then the point offset. `UseSimplePosition` + `SimplePositionXPt/YPt` means page-absolute. `LayoutInCell` decides whether the frame is the table cell. |
| Relative size | `img.RelativeWidth` / `img.RelativeHeight` (`Percent` of `RelativeTo`: page, margin …). When set, these win over `WidthPt` / `HeightPt` (which are the fallback). |
| Wrap | `Wrap` (None, Square, Tight, Through, TopAndBottom), `WrapSide`, `WrapPolygon` (21600×21600 units over the extent; null = the rectangle), `EffectiveDistances` (top, bottom, left, right in pt) |
| Stacking | `BehindText` (behind or in front of the text, for any wrap), `RelativeHeight` (higher = in front), `AllowOverlap`, `LockAnchor` |
| Cropping and transforms | `img.Crop` (fractions removed per side), `RotationDegrees` (clockwise), `FlipHorizontal` / `FlipVertical` |
| Alt text | `img.AltText`, `img.Title`; `img.Hidden` means don't draw it |
| Linked pictures | `img.LinkUrl` set and `img.Bytes` empty: reserve the box (`WidthPt` × `HeightPt`); there are no pixels to draw. `ContentType` may be `application/octet-stream` for formats Docs does not recognise: draw a placeholder of the right size. |

Drawings that are not pictures (charts, SmartArt, shapes, groups, text boxes) are not image runs; their text is flattened into the paragraph. VML pictures from Word 2003-era files load as text only. Pictures in footnotes come with Phase 8. `doc.Images` (`ImageInfo`) is a separate streaming listing (`DocxReader.Images()`). It counts differently: it also lists the first picture inside groups and text boxes. Use the image runs for layout.

## 8. Fields

| Field | How it appears |
|---|---|
| `PAGE`, `NUMPAGES`, `SECTIONPAGES` | A `Run` with `FieldInstruction` set (e.g. `" PAGE "`) and the cached value as `TextContent`. Replace the cached value with the real page number at layout time, formatted with the section's `PageNumbering.Format`. |
| Every other field (DATE, REF, TOC …) | Its cached result text, as ordinary runs, until Phase 8 (fields and notes) models fields |

## 9. Not yet available

| Not available | Comes with |
|---|---|
| Footnotes/endnotes | Phase 8 |
| Bookmarks and internal links | Phase 8 |
| Comments and tracked changes | Phase 9 |
| Content-control boundaries | the sdt content is loaded; the control itself is not kept |
| Text boxes and shapes | text is flattened into the paragraph; geometry is not kept |
| Floating tables | positioning is carried but not exposed |
| Paragraph frames (`w:framePr`) | not modelled |
