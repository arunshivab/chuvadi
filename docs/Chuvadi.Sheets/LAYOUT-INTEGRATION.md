# Chuvadi.Sheets → Chuvadi.Layout: integration guide

This guide lists the public Chuvadi.Sheets APIs (1.6.1) that the Layout xlsx importer
should call. Each section names the exact member to use and the units and conventions it
works in. All rows and columns are **1-based** unless a member says `column0Based`.

There are two ways in:

| | Workbook model | Streaming reader |
|---|---|---|
| Open | `Workbook.Load(path or stream, password, XlsxReaderOptions)` | `XlsxReader.Open(path or stream, password, XlsxReaderOptions)` |
| Sheets | `wb.Sheets`, `wb[name]`, `wb.ActiveSheet` | `reader.Sheets` (`SheetInfo`), `reader.Sheet(name or index)`, `reader.ActiveTab` |
| Memory | All cells in memory | One row at a time |

Use the model for ordinary reports. For very large sheets, use the streaming reader and a
one-page window. With the streaming reader, call in this order:

1. `XlsxReader.Open(...)`
2. `reader.Sheet(...).ReadLayout()`
3. `ReadDrawings()`
4. Enumerate `Rows`.

`ReadLayout` and `ReadDrawings` are cached. Each makes its own pass over the sheet part, so
call them before the rows or between enumerations.

**Options worth setting:**
- `XlsxReaderOptions.TreatFirstRowAsHeaders = false`. The streaming reader defaults to
  `true` and skips row 1; the model always loads row 1.
- `DisplayCulture`, for culture-dependent display text such as en-IN grouping or date
  separators.
- `IncludeStyledEmptyCells = true` on the model, so that empty cells with borders or fills
  are loaded.
- For untrusted files: `XlsxReaderOptions.ForUntrustedInput()` plus a `CancellationToken`.

**Which sheets to render:** skip sheets whose `Visibility` is not `SheetVisibility.Visible`
(`Sheet.Visibility` or `SheetInfo.Visibility`).

## 1. Cell values and display text

| Need | Model | Streaming |
|---|---|---|
| Cell lookup (no creation) | `sheet.FindCell(row, col)`, `sheet.PopulatedCells` | rows as they stream |
| Raw value | `cell.Value` (formula cells: cached result) | `row[col0]` |
| Text exactly as Excel shows it | `cell.DisplayText`, `cell.GetDisplayText(NumberFormatOptions)` | `row.GetDisplayString(col0)` |
| Formula (for tooltips/debug only) | `cell.Formula`, `cell.HasCachedValue` | `row.GetFormula(col0)`, `row.HasCachedValue(col0)` |

**Colour and fill characters:** for a number format that sets a colour (`[Red]`, `[Color10]`)
or a repeat-fill (`*-`), call
`NumberFormatter.FormatDetailed(value, formatCode, NumberFormatOptions)`. It returns a
`FormattedValue`:
- `Text`
- `Color`, as "RRGGBB": this overrides the font colour;
- `FillCharacter`, which Layout repeats to fill the cell width;
- `IsDateTime`.

The format code to use is the effective style's `CellStyle.NumberFormat`, or the
conditional format's `NumberFormat` when one applies (section 6).

**Options:** `NumberFormatOptions { Culture, Date1904 }`; use `wb.Date1904` or
`reader.Date1904`.

## 2. Effective cell style

This is the style Excel renders a cell with: the cell's own format, else the row format
(when the row has customFormat), else the column format, else the workbook default.

| Model | Streaming |
|---|---|
| `sheet.GetEffectiveStyle(row, col)`. Null means the workbook default, `wb.StyleTable.DefaultCellStyle`. It does not create cells. | `row.GetEffectiveStyleIndex(col0, layout)`, then `reader.StyleTable.ResolveCellFormat(index)`. Index 0 resolves to the default. For the cell's own s= index only, use `row.GetStyle(col0)` (cached). |

`CellStyle` holds the following (colours are already resolved to "RRGGBB"; theme, indexed
and tint colours are applied):
- **Font:** `FontName`, `FontSize` (points), `Bold`, `Italic`, `Underline`, `UnderlineKind`,
  `Strikethrough`, `FontVerticalPosition`, `FontColor`.
- **Fill:** `FillColor` (solid). Patterns use `FillPattern`, `FillForegroundColor` and
  `FillBackgroundColor`; gradients use `FillGradient`, whose stops are `ColorRef` values that
  you resolve with `StyleTable.ResolveColor`.
- **Borders:** `BorderTop`, `BorderBottom`, `BorderLeft`, `BorderRight`, `BorderDiagonal` with
  `DiagonalUp` / `DiagonalDown`. Each is a `Border(Style, Color)`; `BorderStyle` has 14 kinds.
- **Alignment:** `HAlign`, `VerticalAlignment` (use this rather than `VAlign`; null means
  Bottom), `WrapText`, `Indent`, `TextRotation` (0–90 counter-clockwise, 91–180 clockwise,
  255 stacked), `ShrinkToFit`, `ReadingOrder`.
- **Number format:** `NumberFormat`, with `NumberFormatId` for locale-dependent built-ins.
  Display text already handles these.

The full style table is available as `wb.StyleTable` / `reader.StyleTable`
(`CellStyleTable`), for anything not flattened into `CellStyle`.

## 3. Dimensions: widths, heights, hidden rows and columns, outline

**Column widths** are in Excel's `<col width>` units, which count characters of the default
font's maximum digit width (Calibri 11: mdw = 7 px).
- Model: `sheet.Columns[c].EffectiveWidth`, which applies the default width.
  `sheet.Columns[c].Width` returns 0 for a column without its own width.
- Streaming: `layout.ColumnWidth(c)`, which already applies the default:
  `SheetFormat.DefaultColumnWidth` if set, else derived from `BaseColumnWidth`, giving
  9.140625 = 64 px for Calibri 11.
- To pixels (Excel's rule): `px = Truncate(((256 * width + Truncate(128 / mdw)) / 256) * mdw)`.
  The default itself is `SheetFormat.DefaultWidth()`.

**Hidden and grouped columns:**
- Model: `sheet.Columns[c].Hidden`, `.OutlineLevel`, `.Collapsed`.
- Streaming: `layout.IsColumnHidden(c)` and `layout.FindColumn(c)` (`ColumnRange`). Raw
  spans are in `sheet.ColumnRanges` / `layout.ColumnRanges`.

**Row heights** are in points, and 0 means "no stored height".
- Model: `sheet.Rows[r].Height`; streaming: `row.Attributes.Height`.
- Use `SheetFormat.DefaultRowHeight` for 0 (`sheet.Format` / `layout.Format`).
- `CustomHeight` tells a manual height from Excel's stored auto-fit height.
- Pixels = points × 96 / 72.
- Rows absent from the file use the default height. Wrapped-text rows without a stored
  height need Layout's own text measurement; Sheets does not auto-fit row heights.

**Hidden rows:**
- Model: `sheet.Rows[r].Hidden`; streaming: `row.Attributes.Hidden`.
- When `SheetFormat.ZeroHeight` is true, rows are hidden by default unless a row says
  otherwise.
- The streaming reader yields only rows present in the file. Gaps are default-height,
  unhidden rows (or hidden ones when ZeroHeight is set).

**Outline:** `Row.OutlineLevel` / `Collapsed`, and `SheetFormat.OutlineSummaryBelow` /
`OutlineSummaryRight`.

**Used range:** `layout.UsedRange` is the file's `<dimension>`, as written by the producing
tool and not verified. In the model, compute it from `sheet.PopulatedCells`.

**View** (`sheet.View` / `layout.View`):
- `RightToLeft` mirrors the column order.
- `ShowGridLines` is for screen only; printing uses `PageSetup.PrintGridlines`.
- `ShowZeros = false` blanks zero values.
- `ShowFormulas` displays formulas instead of values.
- `ViewMode`, `ZoomScale`, and `Pane` (frozen rows and columns) are not relevant for print.

## 4. Merged cells

- Model: `sheet.MergeRanges`; streaming: `layout.MergeRanges`. Both give A1 strings such as
  "A1:C2", in file order.
- The top-left cell holds the value and the style. Its borders apply to the merged block
  edges as Excel draws them.
- Clip the text to the merged rectangle.

## 5. Page setup (pagination)

`sheet.PageSetup` / `layout.PageSetup` (`PageSetup`):

**Paper:**
- `PaperSizeCode` and `Paper` (`PaperSizeInfo`: `Name`, `WidthMm` and `HeightMm` in portrait).
- `Orientation`: `Default` means Portrait.
- Printer-specific codes have `Paper == null`; fall back to the code or to A4/Letter by
  policy.

**Scaling:**
- `FitToPage` with `FitToWidth` / `FitToHeight` (0 = as many pages as needed). Otherwise use
  `Scale` (percent).

**Margins:** `Margins.Left/Right/Top/Bottom/Header/Footer`, in inches. `Margins.IsDefined`
is false when the file had none, in which case Excel's "Normal" values apply. These are the
defaults the properties already return.

**Page order and centring:** `PageOrder` (`DownThenOver` / `OverThenDown`),
`CenterHorizontally`, `CenterVertically`.

**What prints:**
- `PrintAreas`: A1 ranges with no sheet prefix ("A1:H200", "A:C", "1:5"). Each area prints
  on its own pages. Empty means the used range.
- `PrintTitleRows` / `PrintTitleColumns`: `(First, Last)?` ranges repeated on every page.
- `RowBreaks` / `ColumnBreaks` (`PageBreak`): a break with `Id = n` means the new page
  starts at row (or column) n + 1. Breaks with `Manual = false` are stored automatic breaks;
  Layout normally recomputes those.
- `PrintGridlines`, `PrintHeadings`, `BlackAndWhite`, `Draft`.
- `CellComments`: not drawn by Sheets.
- `Errors` (`PrintErrors`): show error cells as displayed, blank, dash or #N/A.

**Page numbers:** `FirstPageNumber` when `UseFirstPageNumber` is true.

**Headers and footers:** `HeaderFooter` (`HeaderFooterText`) holds `OddHeader/OddFooter`,
`EvenHeader/EvenFooter` (when `DifferentOddEven`) and `FirstHeader/FirstFooter` (when
`DifferentFirst`), plus `ScaleWithDocument` and `AlignWithMargins`.
- The texts are raw Excel codes, kept verbatim: `&L`, `&C`, `&R` sections; `&P`, `&N`,
  `&D`, `&T`, `&F`, `&A`; `&"font,style"`; `&nn` size; `&K` colour; `&G` picture.
- Layout parses these codes itself. Header/footer images (`&G`) are not read in 1.6.1.

## 6. Conditional formatting

**Build an evaluator once per sheet, after loading:**
- Model: `var cf = sheet.EvaluateConditionalFormats(new ConditionalFormatOptions { Today = printDate })`.
- Streaming: `new ConditionalFormatEvaluator(layout.ConditionalFormats, reader.StyleTable, mySource, options)`,
  where `mySource` implements `ICellValueSource`:
  - `GetValue(row, col)`;
  - `GetValues(r1, c1, r2, c2)`, covering the non-blank cells of a box.

  Colour scales, top-N, averages and duplicates need whole-range statistics. With a one-page
  window, either feed the evaluator a full-value source (a first pass over the sheet), or
  accept that range-statistic rules are approximate.

**Per cell:** `var r = cf.Evaluate(row, col)` returns null when no rule covers the cell.
`ConditionalFormatResult` contains:
- **Style:** `r.ApplyTo(effectiveStyle)` gives the `CellStyle` to paint: fill, font colour,
  bold, italic, underline, strike, borders.
- **Number format:** when `r.NumberFormat` is set, re-render the display text with
  `NumberFormatter.FormatDetailed(value, r.NumberFormat, options)`.
- **Data bar:** `r.DataBar` (`DataBarResult`). Draw a bar of `LengthPercent` of the cell
  width in `FillColor`. Use `Bar.Gradient`, `Bar.BorderColor`, `IsNegative`,
  `Bar.AxisPosition` and `Bar.Direction` as needed, and hide the value text when
  `Bar.ShowValue` is false.
- **Icon:** `r.Icon` (`IconResult`) gives `IconSet` (Excel's name, e.g. "3TrafficLights1")
  and `Index` out of `Count`. Hide the value text when `ShowValue` is false. Icon artwork is
  Layout's job.
- **`r.NotEvaluated`:** rules covering the cell that Sheets cannot evaluate, such as other
  functions, other-sheet references or defined names. Log or flag these; don't guess.
- `r.MatchedRules`, for diagnostics.

The rules themselves are in `sheet.ConditionalFormats` / `layout.ConditionalFormats`
(`ConditionalFormatting`, `ConditionalRule`), if Layout needs them.

Data validation (`sheet.DataValidations`) never prints; Layout can ignore it.

## 7. Drawings: pictures, shapes, charts

- Model: `sheet.Drawings`. Image bytes load up front; `XlsxReaderOptions.LoadImageData = false`
  skips them.
- Streaming: `sheetReader.ReadDrawings()`. Bytes load on demand while the reader is open.

**`DrawingObject`:**
- `Kind`: Picture, Shape, Connector, Group, Chart or Other.
- `Name`, `Description` (alt text), `Hidden` (don't draw).
- `Rotation` in degrees clockwise, `FlipHorizontal`, `FlipVertical`.

**Placement (`Anchor`, a `DrawingAnchor`):**
- `TwoCell`: from `From` to `To`. Each `DrawingPoint` has `Column`/`Row` (1-based, the cell
  whose left/top edge the offset starts from) plus `ColumnOffsetEmu`/`RowOffsetEmu`. Turn the
  points into pixels with section 3's widths and heights; hidden rows and columns take no
  space.
- `OneCell`: `From`, plus `WidthEmu`/`HeightEmu`.
- `Absolute`: `XEmu`/`YEmu` plus `WidthEmu`/`HeightEmu`, from the sheet's top-left corner.
- Units: `Emu.PerPoint = 12700`, `Emu.PerInch = 914400`, `Emu.PerPixel = 9525` (96 DPI);
  helpers `Emu.ToPoints`, `Emu.ToPixels`.

**Groups:** children share the group's anchor. Map each child's `OffsetXEmu/OffsetYEmu/ExtentWidthEmu/ExtentHeightEmu`
from the group's child space (`ChildOffset*` / `ChildExtent*`) into the group's own extent.

**Pictures:**
- `GetImageBytes()` returns the image bytes; `ContentType` is image/png, jpeg, gif, bmp,
  tiff, x-emf, x-wmf or svg+xml.
- `Crop` gives fractions to cut from each side.
- A linked picture has `LinkedImageTarget` and no bytes.

**Shapes and connectors:** `Geometry` (preset name), `Text` (plain, '\n' between
paragraphs), `FillColor` / `NoFill`, `LineColor` / `NoLine`, `LineWidthEmu`.

**Charts:** draw a labelled placeholder from `obj.Chart` (`ChartInfo`):
- `Type` (`ChartType`; `Extended` for Excel 2016 charts, with `ExtendedType` such as
  "waterfall");
- `Title`;
- `Plots[i].Series[j]`: `Name` and `Values.CachedValues`;
- `AxisTitles`, `LegendPosition`.

`obj.Chart` is null when the chart part is missing.

Not read in 1.6.1: header/footer images, comment shapes (VML), form controls, in-cell
pictures, and charts on chart sheets.

## 8. Minimal render loop (model)

```csharp
var wb = Workbook.Load(path, null, new XlsxReaderOptions { IncludeStyledEmptyCells = true, DisplayCulture = culture });
foreach (var sheet in wb.Sheets.Where(s => s.Visibility == SheetVisibility.Visible))
{
    var page = sheet.PageSetup;                       // §5: paper, margins, areas, titles, breaks
    var cf = sheet.EvaluateConditionalFormats(new ConditionalFormatOptions { Today = printDate });
    var opts = new NumberFormatOptions { Culture = culture, Date1904 = wb.Date1904 };
    // For each visible cell (r, c) in the print area, skipping hidden rows/columns (§3) and
    // non-top-left cells of merges (§4):
    //   var style = sheet.GetEffectiveStyle(r, c) ?? wb.StyleTable.DefaultCellStyle ?? default;
    //   var res   = cf.Evaluate(r, c);
    //   if (res is not null) style = res.ApplyTo(style);
    //   var value = sheet.FindCell(r, c)?.Value;      // does not create cells
    //   var shown = NumberFormatter.FormatDetailed(value, res?.NumberFormat ?? style.NumberFormat, opts);
    //   paint fill, borders, text (shown.Text, shown.Color ?? style.FontColor), then res?.DataBar / res?.Icon
    // Then sheet.Drawings (§7), positioned from the anchors.
}
```

Use `sheet.FindCell(r, c)?.DisplayText` when no conditional number format applies. It
already handles built-in format ids and the writer's default date formats for unstyled
dates. Don't use `sheet.Cell(r, c)` to read: it creates the cell if it is missing.
