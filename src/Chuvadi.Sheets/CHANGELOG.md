# Changelog

## 1.7.0 (2026-10-29)

The Layout importer's requests from running it over the Apache POI corpus: rich text,
hyperlinks, the fill position, files from other writers, Strict OOXML, cells outside the grid,
faster number formatting, comments, header/footer pictures and chart sheets. Also adopts the
shared-crypto change (D-25). Additive and source-compatible. A workbook that uses none of the
new features writes the same bytes as 1.6.1.

### Features
- **Rich text.** Strings made of runs with their own fonts (shared strings and inline strings)
  are read with their runs.
  - New types: `RichText` (`Runs`, `Text`), `RichTextRun` (`Text`, `Font`, `ResolvedColor`)
    and `RichTextFont`. `RichTextFont` holds every `<rPr>` property as stored: name, size,
    bold, italic, underline, strike, super/subscript, colour (`ColorRef`), family, charset,
    scheme, outline, shadow, condense, extend. A property the run does not state is null.
  - Model: `Cell.RichText`. Streaming: `RowReader.GetRichText(col)`. `Value` stays the plain
    text, so code that reads strings is unaffected.
  - Written as well: set `Cell.RichText` (or `Value` to a `RichText`), or pass a `RichText` as
    a value to the streaming writer. Shared and inline strings are both supported, and load →
    save keeps the runs. Before 1.7.0 they were flattened to plain text.
  - Phonetic guides (`<rPh>`) are not part of the text. This was already so for shared
    strings and is now also so for inline strings, where 1.6.1 appended the phonetic text.
- **Hyperlinks.** `Hyperlink` (`Reference`, `Target`, `Location`, `Tooltip`, `Display`, the
  parsed range and `Contains`), read from `<hyperlinks>` with targets resolved through the
  sheet's relationships.
  - Model: `Sheet.Hyperlinks` (editable), `AddHyperlink`, `FindHyperlink`. Streaming:
    `SheetLayoutInfo.Hyperlinks`.
  - Load → save keeps them; before 1.7.0 they were dropped. A loaded link whose target
    relationship is missing (no target and no location) is not written.
  - New `SheetWriter.AddHyperlink(Hyperlink)` links ranges and places within the workbook on
    the streaming writer; external targets become relationships as before.
- **Fill position.** New `FormattedValue.FillIndex`: the index in `Text` where the repeat fill
  (`*x`) goes. For `_($* #,##0.00_)` and 1234.5 it is 2 (" $" | "1,234.50 "). It is an init
  property, so positional construction and deconstruction are unchanged.
- **Comments.** `CellComment` (`Reference`, `Row`, `Column`, `Author`, `Text`, `RichText`,
  `Anchor` (`CommentAnchor`), `Visible`), read from the comments part and its VML shapes.
  - Model: `Sheet.Comments` (editable), `AddComment`. Streaming: `SheetReader.ReadComments()`.
    New `SheetWriter.AddComment(CellComment)`.
  - Load → save keeps text runs, box anchor and visibility; before 1.7.0 comments were dropped.
- **Header/footer pictures.** `HeaderFooterPicture` (`Section`, `Part`, `Kind`, size in points,
  `ImagePartName`, `ContentType`, `GetImageBytes()`), from the legacyDrawingHF part.
  - Model: `Sheet.HeaderFooterPictures`. Streaming: `SheetReader.ReadHeaderFooterPictures()`.
  - Read-only: not written on save.
- **Chart sheets.** `ChartSheet` (`Name`, `Visibility`, `WorkbookIndex`, `PageSetup`, `View`,
  `TabColor`, `Drawings`, `Chart`).
  - Model: `Workbook.ChartSheets`. Streaming: `XlsxReader.ChartSheets` and
    `ReadChartSheet(info)`.
  - Read-only: `SaveTo` writes worksheets only, as before.
- **Files from other writers.**
  - Part names are matched case-insensitively and '\' is accepted as a separator, so
    workbooks like POI's 49609.xlsx ("[content_types].xml", `xl\styles.xml`) now open. This is
    in the shared `OoxmlPackage` (see the shared-ooxml change).
  - Shared strings are found through the workbook's relationship first.
- **Strict OOXML.** Workbooks in the ISO/IEC 29500 Strict namespaces
  (http://purl.oclc.org/ooxml/...) are read like Transitional ones: sheets, shared strings,
  styles, formulas, hyperlinks, drawings and charts. Before 1.7.0 they loaded with no sheets.
  - New `Workbook.IsStrict` and `XlsxReader.IsStrict`.
  - Saving writes Transitional.
  - The Strict `dateCompatibility` attribute is not applied.
- **Cells outside the grid.** Rows numbered below 1 or above 1,048,576, and cells whose column
  is beyond XFD (or whose reference has no column letters), are left out instead of loaded.
  Values are never moved or altered. They are reported:
  - Model: new `Workbook.LoadWarnings` (`LoadWarning`: `Kind`
    = `LoadWarningKind.CellsOutsideGrid`, `SheetName`, `Count`, `Reference`, `Message`).
  - Streaming: new `SheetReader.OutOfGridRows`, `OutOfGridCells` and
    `FirstOutOfGridReference`.
  - New constants `SheetReader.MaxRow` and `MaxColumn`.
  - Column letters in cell references are now matched case-insensitively.
- **Shared crypto (D-25).** Takes the Docs chat's Crypto files.
  - New `XlsxReaderOptions.MaxSpinCount`, default 500,000, also set by
    `ForUntrustedInput()`. An encrypted file that declares a higher key-derivation spin
    count fails before derivation with `XlsxLimitExceededException` ("MaxSpinCount"). Set it
    to null to lift the cap.
  - The reader's `CancellationToken` now interrupts decryption inside key derivation, on
    both `Workbook.Load` and `XlsxReader.Open`.
  - `XlsxWriter.SaveAsync`'s token cancels the encryption pass.
  - The Docs chat reports the encrypted output byte-identical to before, with key derivation
    about 3.4x faster.
- **`ColorRef` factories:** `FromRgb`, `FromTheme`, `FromIndexed`, `Automatic`.

### Performance
- **Number formatting is about 4x faster and allocates about 25x less.** For 100k values with
  "#,##0.00": ~40–60 ms and 63 bytes per value, down from ~150 ms and ~1,650 bytes.
  - The layout of a digit section is computed once per format code.
  - The culture's group sizes are cached (`NumberFormatInfo.NumberGroupSizes` copies its
    array on every read).
  - Digits are written straight into a reused buffer.
  - The output was checked identical to 1.6.1 for 471,744 combinations: 39 format codes ×
    3,024 values (plus text) × invariant, en-IN, de-DE and fr-FR.

### Fixes
- Load → save no longer fails on a sheet whose last stored row has customFormat but no style
  (POI 57828.xlsx). This was a 1.6.0 regression.

### Tests
- **New `layout17` group: 19 checks.** It covers:
  - lenient part names, and case-only duplicate entries;
  - a Strict workbook (sheets, strings, styles, formulas, hyperlinks, a drawing), and its
    re-save;
  - rows and cells outside the grid, with the warnings and counters;
  - `FillIndex` for accounting, leading, trailing, date, text and fraction fills;
  - number-format text and allocations;
  - rich shared and inline strings with every font property, theme colours and phonetic
    runs, round-trip, and rich text built in code;
  - hyperlinks of every kind, round-trip and the streaming writer;
  - comments with Excel-style VML, and round-trip;
  - header/footer pictures;
  - chart sheets;
  - the customFormat-row fix;
  - `MaxSpinCount`, cancellation inside key derivation, and `SaveAsync` cancellation.
- **New xUnit fact** running the group.
- **Cross-check.** Apache POI workbooks load, save, reload with identical values and open in
  LibreOffice: 49609, the four Strict files, poc-shared-strings, SimpleWithComments,
  comments, 52425, 57828, WithVariousData, chart_sheet and others. LibreOffice reads the
  saved rich-text runs, links and comments.

## 1.6.1 (not released separately; included in 1.7.0)

Chart metadata on sheets, plus two small helpers for the Layout importer. Read-side and
additive; writer output is unchanged.

### Features
- **Chart metadata.** `DrawingObject.Chart` (`ChartInfo`) is filled for chart frames, both in
  `Sheet.Drawings` and in `SheetReader.ReadDrawings()`.
  - `ChartInfo` gives:
    - `Type` (`ChartType`) and `Plots`, of which combo charts have several (`ChartPlot`:
      type, 3-D, grouping, scatter style);
    - `Title` (rich text as plain text, or the cached text of a title cell), `TitleFormula`
      and `AutoTitleDeleted`;
    - `AxisTitles`, `LegendPosition` and `Style`.
  - Each `ChartSeries` has `Name` and `NameFormula`, `Categories`, `Values`, `XValues`,
    `YValues`, `BubbleSizes` and an explicit `Color`.
  - Each data reference (`ChartDataReference`) has its `Formula`, `IsNumeric`, `FormatCode`,
    `PointCount` and `CachedValues`, kept as stored text (with gaps as null), plus
    `CachedNumbers`.
  - Excel 2016 charts (chartex: waterfall, funnel, treemap, sunburst, histogram, box and
    whisker, map) come back as `ChartType.Extended` with `ExtendedType`, the title and the
    series data.
  - In `mc:AlternateContent`, the chart frame now wins over Excel's "chart not available"
    fallback text box.
  - Metadata only: charts are not rendered or written. `Chart` is null when the chart part is
    missing.
- **`Sheet.FindCell(row, col)` / `FindCell(address)`** look up a cell without creating it.
- **Default column width.** New `Column.EffectiveWidth` and `SheetFormat.DefaultWidth()`, which
  `SheetLayoutInfo.ColumnWidth` now shares.

### Docs
- **New `docs/Chuvadi.Sheets/LAYOUT-INTEGRATION.md`.** It lists the exact APIs Layout should
  call for display text, effective styles, dimensions, merges, page setup, conditional
  formats, drawings and charts.

### Tests
- **New `charts` group: 6 checks.** It covers:
  - a combo bar+line chart with a rich title, caches with gaps, axis titles, legend, style
    and series colour;
  - a pie chart with a cell-reference title;
  - a 3-D bar chart together with a scatter plot;
  - a chartex waterfall chart inside mc:AlternateContent;
  - the streaming path;
  - a missing chart part;
  - the new helpers.
- **Cross-check.** Charts written by openpyxl, and the same file re-saved by LibreOffice, read
  correctly.

## 1.6.0 (not released separately; included in 1.7.0)

Sheet fidelity for the Layout engine: the rest of a loaded sheet round-trips (rows, columns,
views, panes, tab colour, visibility), conditional formatting and data validation are read,
preserved and evaluated per cell, and pictures and shapes on sheets are read. Additive and
source-compatible, apart from the behaviour changes listed below. A sheet that uses none of
the new options writes the same bytes as 1.5.0.

### Features
- **Rows.**
  - `sheet.Rows[n]` is settable: `Height`, `CustomHeight`, `Hidden`, `OutlineLevel` (0–7),
    `Collapsed`, `Style`, `ThickTop` and `ThickBottom`.
  - Rows that have settings but no cells are written too.
  - Streaming writer: `RowFormat` with `WriteRow(RowFormat, values)` and
    `WriteRow(RowFormat, Action<RowBuilder>)`.
  - A `Row` from the streaming reader (`RowReader.Attributes`) stays read-only.
- **Columns.**
  - `sheet.Columns[n]` gains settable `Hidden`, `OutlineLevel`, `Collapsed`, `BestFit` and
    `Style`.
  - Loaded `<col>` spans that are hidden, grouped or styled are kept even without a width.
  - Streaming writer: `SetColumns(from, to, ColumnFormat)` covers width, hidden, outline,
    collapsed, best-fit and style.
- **Sheet format.** `SheetFormat` is settable: default row height and column width, base
  width, zero/custom height, thick borders, outline summary positions. It is written when set
  or loaded. `outlineLevelRow` and `outlineLevelCol` are computed at save. Exposed as
  `Sheet.Format` and `SheetWriter.Format`.
- **Sheet view.** New `SheetView` class, exposed as `Sheet.View` and `SheetWriter.View`.
  - Settings: gridlines, headings, zeros, ruler, formulas, outline symbols and white space;
    right-to-left; tab selected; view mode; top-left cell; zoom for each view.
  - Panes: frozen, split or frozen-from-split (`SheetPane`), with `Freeze(rows, cols)` and
    `Split(xTwips, yTwips, topLeft)`.
  - `Selections` per pane, plus `Select(cell)`.
  - The 1.4.0 `Sheet.ShowGridLines`, `RightToLeft`, `ViewMode` and `ZoomScale` become
    settable shortcuts into `View`. `FreezeRows`, `FreezeColumns`, `FreezeRowCount` and
    `FreezeColumnCount` now use `View`, so loaded frozen panes are kept.
- **Tab colour, visibility, active sheet.**
  - `Sheet.TabColor` and `SheetWriter.TabColor` (RGB; a loaded theme or indexed colour is
    resolved).
  - Settable `Sheet.Visibility` and `SheetWriter.Visibility`.
  - `Workbook.ActiveSheet`, `XlsxWriter.ActiveSheetIndex` and `XlsxReader.ActiveTab`.
  - Saving checks that at least one sheet is visible and that the active sheet is visible.
- **Streaming writer prologue.** `sheetPr`, `sheetViews`, `sheetFormatPr` and `cols` are built at
  save and inserted after `<worksheet>`. View, format, columns, tab colour and fit-to-page can
  therefore be set at any time until save. `AutoFitColumns` still has to come before the first
  row, so that every row is measured.
- **Conditional formatting, read.**
  - `Sheet.ConditionalFormats` and `SheetLayoutInfo.ConditionalFormats` hold
    `ConditionalFormatting` objects: `Ranges`, `Rules`, `Pivot`.
  - Each `ConditionalRule` has a type: cellIs, expression, colorScale, dataBar, iconSet, top10,
    aboveAverage, duplicate/unique, the four text rules, blanks, errors or timePeriod.
  - Rule fields: priority, stopIfTrue, operator, formulas, text, period, rank/percent/bottom,
    above/equal/stdDev.
  - Styling: the stored `DifferentialFormat` (`DxfId`, `LoadedFormat`), plus `Style`, a
    resolved `DifferentialStyle` with RGB colours.
  - Scale details: `ColorScaleInfo`, `DataBarInfo` and `IconSetInfo`, with `ConditionalValue`
    thresholds.
  - Excel 2010 (x14) extension blocks are read. Data-bar details (solid or gradient, border,
    negative colours, axis, automatic min/max, lengths) are merged into the rule they extend.
  - Rules stored only in the extension (e.g. one referring to another sheet) come back with
    `FromExtension` set.
- **Conditional formatting, write.**
  - Every rule is written back: loaded rules, `new ConditionalRule(...)` and the existing
    helpers.
  - `AddConditionalFormatting(ConditionalFormatting)` is new on `Sheet` and `SheetWriter`.
  - The writer now emits `<dxfs>`.
  - Priorities left at 0 are numbered after the highest priority in use.
  - Text and time-period rules built in code get the formulas Excel itself writes for them.
  - Extension rules and data-bar details are written to the x14 `extLst`.
  - New helpers: `ConditionalFormat.Expression(formula, style)` and a two-colour
    `ColorScale.Custom`.
- **Conditional formatting, evaluate.** `sheet.EvaluateConditionalFormats(options)` builds a
  `ConditionalFormatEvaluator`; for streaming, use
  `new ConditionalFormatEvaluator(formats, styles, ICellValueSource, options)`.
  - `Evaluate(row, col)` returns a `ConditionalFormatResult`:
    - effective fill, pattern, font colour, bold, italic, underline, strikethrough, number
      format and borders;
    - `DataBar` (fraction, length, min, max, negative, colour) and `Icon` (set, index,
      count);
    - `ApplyTo(CellStyle)`, `MatchedRules` and `NotEvaluated`.
  - Precedence follows Excel: priority order, the higher-priority property wins, and
    stopIfTrue stops lower-priority rules.
  - Evaluated:
    - cellIs, with literals, references or expressions as operands;
    - expressions through a small evaluator, with comparisons, arithmetic, `&`, `%` and about
      50 functions (listed on `ConditionalFormatEvaluator`);
    - 2- and 3-colour scales;
    - data bars and icon sets, as metadata;
    - top/bottom N and N%;
    - above/below average, with sample standard deviation;
    - duplicate and unique values;
    - the text rules, blanks and errors;
    - time periods against an injectable today (`ConditionalFormatOptions.Today`).
  - Not evaluated, and reported in `NotEvaluated`: other functions, references to other sheets
    or workbooks, defined names, structured references, and table styles.
- **Data validation.**
  - Read and written back: `Sheet.DataValidations` and `SheetLayoutInfo.DataValidations`.
  - `DataValidation` gains public `Kind` (`DataValidationType`), `Comparison`, `Formula1` and
    `Formula2`.
  - New settable options: `ErrorStyle` (`DataValidationErrorStyle`), `ShowErrorMessage`,
    `ShowInputMessage` and `ImeMode`. The two show flags are nullable; null keeps the 1.5.0
    behaviour.
  - New factories: `Custom(formula)`, `Time(min, max)` and `Create(kind, comparison, f1, f2)`.
  - Lists stored in the Excel 2010 extension (cross-sheet lists) are read too, and written back
    in the main element.
- **Drawings, read.** `Sheet.Drawings` and `SheetReader.ReadDrawings()` return
  `DrawingObject` items. Each has:
  - `Kind`: Picture, Shape, Connector, Group (with `Children`), Chart or Other;
  - name, alt text and title, hidden, rotation, flips, own transform and a group's child
    space;
  - `Anchor` (`DrawingAnchor`): two-cell (with `EditAs`), one-cell or absolute;
    `DrawingPoint` From/To with 1-based column and row plus EMU offsets; size and position in
    EMU.
  - Pictures: content type, part name, crop, `GetImageBytes()` and linked target.
  - Shapes: preset geometry, plain text, fill and line colour (srgb, sys, preset and theme
    colours with lumMod/lumOff/tint/shade, and colours from the shape style), and line width.
  - Charts: the chart part name.

  `mc:AlternateContent` is unwrapped. The model loads image bytes up front;
  `XlsxReaderOptions.LoadImageData = false` skips them. The streaming reader reads them on
  demand while it is open. Read limits apply to every part. `Emu` helpers are included.
- **Streaming layout.** `SheetLayoutInfo` adds `View`, `TabColor`, `ConditionalFormats` and
  `DataValidations`. `ShowGridLines`, `RightToLeft`, `ViewMode` and `ZoomScale` now read from
  `View`.

### Behaviour changes
- **`ConditionalFormat.CellIs` styles now show.** The style is written as a dxf, and the
  rule points at it. The writer no longer adds an unused cell format for it, so `styles.xml`
  changes for code that uses CellIs.
- **Load → save keeps more.** Row and column settings, the sheet format, the sheet view
  (including frozen panes), tab colour, sheet visibility, the active sheet, conditional
  formats and data validation are now kept. Before, they were dropped and every sheet came
  back visible. A loaded file whose active tab is a hidden sheet saves with the first visible
  sheet active.
- **Streaming writer shape rules relaxed.** `SetColumnWidth`, `FreezeRows` and
  `FreezeColumns` no longer throw after the first row. Column spans are written sorted by
  column.
- **`ConditionalFormatRule` can no longer be subclassed outside the library.** The writer
  never wrote such rules anyway. Every rule is now a `ConditionalRule`.
- **`Sheet.ConditionalFormats` and `Sheet.DataValidations` are public.** They were internal
  in 1.5.0.

### Not in 1.6.0
- Drawings are not written: load → save drops pictures and shapes, as before.
- Header/footer images, comment shapes, form controls and in-cell pictures are not read.
- Cancellable decryption waits for the shared-crypto change from the Docs chat.

### Tests
- **New `fidelity` group: 17 checks.** It runs on an Excel-shaped fixture with x14 blocks,
  a picture, a text box, a chart frame and a group. It covers:
  - row, column, view, pane, selection, tab colour and visibility, read and round-trip;
  - model setters and validation;
  - every writer shape option in schema order, and save validation;
  - LibreOffice re-save keeping panes, hidden rows and columns, tab colour and height;
  - conditional formats read with all details, round-trip, and kept by LibreOffice;
  - CellIs dxf bytes;
  - the evaluator on the fixture and on every evaluated rule type, including stopIfTrue and
    not-evaluated reporting;
  - every rule type written and read back;
  - data validation factories and options;
  - streaming `ReadLayout` extras;
  - drawings in the model and streaming.

## 1.5.0 (not released separately; included in 1.7.0)

Formulas with cached results on read and write, page setup on the streaming reader and on
write, defined names on read and write, for the Layout engine. Additive and
source-compatible, apart from the behaviour changes listed below. A sheet that sets no new
option writes the same bytes as 1.4.0, except where the schema-order fix applies.

### Features
- **Formulas on read.**
  - Streaming: `RowReader.GetFormula(col)`, `GetFormulaInfo(col)` (a `CellFormula` record
    with `Text`, `Kind` and `Range`) and `HasCachedValue(col)`. The indexer keeps returning
    the cached result.
  - `FormulaKind` is Normal, Shared, Array or DataTable.
  - Shared formulas: each child cell gets its own text. Relative references are shifted by
    the child's offset, `$` parts are kept, and a reference shifted off the sheet becomes
    `#REF!`. The text is expanded only when it is asked for.
  - Model: `Cell.Formula` is filled on load. New `Cell.CachedValue` (settable only on
    formula cells), `Cell.HasCachedValue` and `Cell.ArrayFormulaRange`.
  - `Cell.Value` on a formula cell returns the cached result, so existing readers and
    `DisplayText` work unchanged. Setting `Value` clears the formula; setting `Formula`
    clears the cached result.
- **Formulas on write.**
  - `RowBuilder.Formula(formula, cachedValue)` and `Formula(formula, cachedValue, style)`.
  - `RowBuilder.ArrayFormula(range, formula, cachedValue)`, plus a styled overload.
  - A cached value is written as a number, string, bool, date serial (with the default date
    style when unstyled), duration, or error. A string is written as an error only when it
    equals one of Excel's error literals (`#N/A`, `#DIV/0!`, `#VALUE!`, `#REF!`, `#NAME?`,
    `#NUM!`, `#NULL!`, `#SPILL!`, `#CALC!`, ...).
  - `XlsxWriterOptions.FullCalculationOnLoad` writes `<calcPr fullCalcOnLoad="1"/>`.
  - Auto-fit now measures a formula cell by its cached result, when it has one.
- **Page setup on the streaming reader.**
  - `SheetReader.ReadLayout()` returns `SheetLayoutInfo`:
    - `PageSetup`, including print areas and titles;
    - `Format`, `ColumnRanges`, `MergeRanges` and the stored `UsedRange`;
    - `ShowGridLines`, `RightToLeft`, `ViewMode` and `ZoomScale`;
    - the helpers `FindColumn`, `ColumnWidth`, `IsColumnHidden`, `ColumnStyleIndex` and
      `ColumnStyle`.
  - `ReadLayout()` makes one pass over the sheet part and skips the cell data without parsing
    it. The result is cached. Read limits and the cancellation token apply.
  - Per row: `RowReader.Attributes` (the existing `Row` type: height, hidden, outline level,
    row style).
  - Per cell: `RowReader.HasCell(col)` and `RowReader.GetStyle(col)`, which resolves and caches
    the style.
  - `RowReader.GetEffectiveStyleIndex(col, layout)` applies Excel's order: the cell's own
    format, then the row style (customFormat), then the column style.
  - Model equivalent: `Sheet.GetEffectiveStyle(row, col)`, which does not create cells.
- **Page setup on write.** `PageSetup` is now read-write, and the same class is used on load
  and save.
  - Validated public setters cover paper, orientation, scale (10–400), `FitToPage`,
    `FitToWidth` and `FitToHeight` (0–32767), first page number, page order,
    black-and-white, draft, comments, errors, DPI and copies.
  - Also settable: `Margins.*` (plus `Margins.Set`), every part of `HeaderFooter` (odd, even
    and first page, with their flags), `PrintGridlines`, `PrintHeadings`,
    `CenterHorizontally` and `CenterVertically`.
  - New methods: `FitTo(wide, tall)`, `SetPrintArea(params ranges)`, `ClearPrintArea()`,
    `AddRowBreak`, `AddColumnBreak`, `RemoveRowBreak`, `RemoveColumnBreak`,
    `ClearRowBreaks()`, `ClearColumnBreaks()` and `CopyFrom(other)`. `PrintTitleRows` and
    `PrintTitleColumns` are now settable, and `PageSetup` has a public constructor.
  - Where it applies:
    - `SheetWriter.PageSetup` can be changed until save. `<sheetPr>` is inserted at save, so
      fit-to-page can be set after the rows.
    - `Sheet.PageSetup` is saved with the workbook.
    - `cfg.PageSetup(p => ...)` works on both export configurations.
  - Only values that are set are written.
  - Print area and titles are written as `_xlnm.Print_Area` / `_xlnm.Print_Titles` scoped
    with `localSheetId`. Sheet names are quoted when needed.
  - `SheetWriter.SetHeaderFooter`, `Sheet.SetHeaderFooter`, `Sheet.PageHeader` and
    `Sheet.PageFooter` are now shortcuts to the odd-page header/footer.
- **Defined names on write.**
  - Streaming: `XlsxWriter.AddDefinedName(name, formula, scopeSheetName, hidden, comment)`.
  - Model: new `DefinedName` type (Name, Formula, Scope, Hidden, Comment).
    `DefinedNameCollection` gains `Entries`, `Add(name, formula, scope, hidden, comment)`,
    `Find(name, scope)` and `Remove(name, scope)`. The existing members keep their
    workbook-scope meaning.
  - Adding `_xlnm.Print_Area` or `_xlnm.Print_Titles` by hand throws and points you to
    `PageSetup`. A sheet scope that does not exist at save time throws.
- **Defined names on read.**
  - `DefinedNameInfo` gains `Comment`.
  - `Workbook.Load` fills `Workbook.DefinedNames`, with the scope mapped to the sheet name.
  - Not filled: the print names, which go to `PageSetup`; names that refer to other workbooks
    or to tables; and names scoped to sheets the model does not load. These remain in
    `LoadedDefinedNames`.

### Fixes
- **Formula cells without a cached value were lost on load.** `Workbook.Load` skipped any
  formula cell that had no stored result, which is common in files from tools that do not
  calculate, so load → save deleted those formulas. They now load and save.
- **Worksheet element order.**
  - `<hyperlinks>` was written before `<conditionalFormatting>` and `<dataValidations>`.
  - `<tableParts>` was written before `<legacyDrawing>`.
  - Both broke the CT_Worksheet order whenever a sheet combined these features. The writer
    now follows ECMA-376 order, and a test checks every child element.

### Behaviour changes
- **Load → save keeps formulas.** Until now it wrote cached values only; now formulas and
  their cached results are saved. Two exceptions keep the saved values identical:
  - Formulas that refer to other workbooks (`[1]Sheet!A1`), to tables (`Table1[Col]`), or to
    a loaded defined name the model could not keep, are saved as their cached value, because
    those targets are not in the saved file.
  - Data-table (`TABLE()`) cells load and save as values.
- **Load → save keeps defined names and the full page setup.** Previously only the odd-page
  header/footer was kept.
- **Element order.** Sheets that combine hyperlinks with conditional formats or data
  validations, or tables with comments, write their elements in a different (now valid)
  order.
- **Cancellation during decryption.** The token is now checked while an encrypted file is
  read, per 512-byte sector. The in-memory decryption that follows (key derivation, AES,
  HMAC) still cannot be interrupted; it measured about 0.4 s for a 3 MB file, mostly key
  derivation. Interrupting it too needs a token in `shared/Chuvadi.Internal/Crypto`.

### Findings
- **A zip that lies about its sizes cannot inflate more than it declares.** .NET's zip
  reader stops at the declared uncompressed size, so the part arrives truncated and fails as
  malformed XML (`XmlException`). The declared-size checks and the inflate counters stay as
  defence in depth.

### Tests
- **New `formulaprint` group: 20 checks.** It covers:
  - reference shifting (14 cases);
  - Excel-shaped shared, array, data-table, string, error and bool formulas, read both
    streaming and through the model;
  - Value/Formula/CachedValue rules;
  - cached values by type, read back, and LibreOffice recalculating them to the same
    values;
  - auto-fit by cached value;
  - `ReadLayout` before rows, with row attributes and effective styles;
  - cancellation and read limits in `ReadLayout`;
  - every page-setup setting written and read back through both read paths, and
    `<sheetPr>` inserted together with auto-fit `<cols>`;
  - validation;
  - model round-trip with edits, and the export-config hook;
  - CT_Worksheet order;
  - defined names, both writer and model;
  - a lying-size zip;
  - cancellation while an encrypted file is read.
- **Cross-checks.**
  - The written page setup survives a LibreOffice re-save (paper, orientation, fit,
    print area, titles, header, margins).
  - Streaming read speed is unchanged (200,000 rows × 10 columns).

## 1.4.0 (not released separately; included in 1.7.0)

Read-side additions for Anjal Mail and the Layout engine: display text, password on the
streaming reader, hostile-input limits and cancellation, macro detection, full page setup.
Additive and source-compatible; writer output is unchanged.

### Features
- **C1: display text.** These return a cell's value rendered with its number format,
  exactly as Excel shows it:
  - `Cell.DisplayText`, plus `Cell.GetDisplayText(options)`.
  - `RowReader.GetDisplayString(column)` and `RowReader.GetDisplayString(name)`.

  Details:
  - Rendering uses `NumberFormatter`, the culture in `XlsxReaderOptions.DisplayCulture` or
    `Workbook.DisplayCulture`, and the file's date system.
  - Formula cells show their cached result.
  - Locale-dependent built-ins (currency ids 5–8, accounting ids 41–44) render as Excel
    en-US does.
  - Unstyled DateTime and TimeSpan values in in-memory workbooks use the writer's default
    formats.
- **C3: password on the streaming reader.** `XlsxReader.Open(path, password, options)` and
  `XlsxReader.Open(stream, password, options)`.
  - Decryption happens in memory.
  - A missing or wrong password throws `XlsxPasswordRequiredException`; an unencrypted file
    ignores the password.
  - The old `Open` overloads now also throw `XlsxPasswordRequiredException` for an encrypted
    file, instead of failing with a zip error.
  - Also added: `Workbook.Load(stream, password, options)`, and password/options overloads
    of `ReadXlsx`, `ReadXlsxSheet` and `ReadXlsx<T>`.
- **C4: hostile-input limits.** New `XlsxReaderOptions` settings:
  - `MaxRowsPerSheet`, `MaxTotalUncompressedBytes`, `MaxCompressionRatio` and
    `MaxPackageEntries`.
  - A preset, `XlsxReaderOptions.ForUntrustedInput()`.

  How they are enforced:
  - The zip is checked before anything is parsed: entry count, declared sizes and ratios,
    plus the actual inflated size of the manifest parts.
  - Every part read is then counted as it inflates, because declared sizes can lie.
  - The new limits throw `XlsxLimitExceededException` (an `IOException`, carrying
    `LimitName`, `Limit` and `Actual`).
  - `MaxPartBytes` keeps throwing `InvalidDataException`, as before.
- **C4: cancellation.** `XlsxReaderOptions.CancellationToken` is checked:
  - while the package is opened and shared strings are read;
  - on every row while streaming;
  - on every row in `Workbook.Load`.

  Cancelling surfaces as `OperationCanceledException`, typically on the next row; the test
  measures under 1 s. Decrypting an encrypted file is one step that cannot be interrupted;
  bound it with `MaxTotalUncompressedBytes`.
- **C5: macros.** `XlsxReader.HasMacros` and `Workbook.HasMacros` detect a VBA project or
  Excel 4.0 macro sheets (.xlsm / .xltm / .xlam). Detection only: macros are never run, and
  load → save writes a macro-free .xlsx.
- **Page setup, for the Layout engine.** New `Sheet.PageSetup` (`PageSetup`) holds:
  - paper size code, with name and millimetre size (`PaperSizeInfo`, ECMA codes 1–68);
  - orientation, scale, and fit-to-page with width × height;
  - first page number, page order, black-and-white, draft, comments and errors printing,
    DPI and copies;
  - margins;
  - header/footer text for odd, even and first pages, kept verbatim with its codes, plus its
    flags;
  - print gridlines and headings, centring;
  - manual row and column page breaks;
  - print area(s) and print titles, from `_xlnm.Print_Area` / `_xlnm.Print_Titles` scoped to
    the sheet.
- **More sheet and workbook properties:**
  - `Sheet.Visibility` (`SheetVisibility`) and `SheetInfo.Visibility` / `WorkbookIndex`.
  - `Sheet.ShowGridLines`, `RightToLeft`, `ViewMode` and `ZoomScale`.
  - `SheetFormat.OutlineSummaryBelow` / `OutlineSummaryRight`.
  - `XlsxReader.DefinedNames`, and `Workbook.LoadedDefinedNames` (read-only, never written
    back).
  - Hidden rows and columns and outline levels were already readable (1.2.0).
- **Date system.** `XlsxReader.Date1904` and `Workbook.Date1904`.
- **Formatter types.** `NumberFormatter` now also accepts `DateOnly` and `TimeOnly`.

### Fixes
- **Formula cells lost their cached value on read.** Any formula written as
  `<f>…</f><v>…</v>` with no whitespace between them, which is how Excel and LibreOffice
  write it, loaded as null. `Workbook.Load` and `XlsxReader` now return the cached result.
- **1904 workbooks.** Dates were 1,462 days early; they are now converted in the file's date
  system.
- **1900 workbooks before 1-Mar-1900.** Serials 1–59 were a day early (DateTime.FromOADate);
  they are now exact. Serial 60, Excel's fictitious 29-Feb-1900, is returned as a number,
  while its display text shows 29-Feb-1900, as in Excel.
- **Caller-owned streams.** `XlsxReader.Open(stream)` and `Workbook.Load(stream)` no longer
  close the caller's stream, as their documentation always said.

### Behaviour changes
- **Header/footer round-trip.** On load, `Sheet.PageHeader` and `PageFooter` are filled from
  the file's odd-page header/footer, so load → save keeps them. Previously they were dropped.
- **Value changes from the fixes.** The date and formula fixes above change the values
  returned for affected files.

### Tests
- **New `readsafety` group: 20 checks.** It covers display text against Excel results and
  the en-IN culture; encrypted streaming with stream ownership; row caps; zip-bomb guards;
  non-seekable input; cancellation latency; macro detection; full page setup; 1904 and
  pre-1900-03-01 dates.
- **Cross-checks.** Page setup and formula cached values were also checked on
  LibreOffice-saved files.

## 1.3.0 (not released separately; included in 1.7.0)

Column auto-fit and a public number-format renderer. All additions are source- and
binary-compatible; output of code that does not use auto-fit is byte-identical to 1.2.0.

### Features
- **Column auto-fit** sizes columns to their content, as displayed. It works in all three
  ways of writing a workbook:
  - `Sheet.AutoFitColumns(options)` and `sheet.Columns[n].AutoFit()` on the Workbook model.
  - `SheetWriter.AutoFitColumns(options)` on the streaming writer. Call it before the first
    row.
  - `cfg.AutoFitColumns()` on the `.ToXlsx()` / `XlsxExport` configurations, for both
    `IEnumerable<T>` and `DataTable` sources.

  Each cell is rendered with its number format and measured with its font, size and
  boldness. The rules:
  - A width you set explicitly always wins (`Columns[n].Width`, `SetColumnWidth`,
    `Column(..., width)`, `[Column(Width = ...)]`).
  - Wrapped, merged, rotated and formula cells are not measured.
  - Header cells of an autofilter or table get room for the filter button.
  - Widths are clamped to `AutoFitOptions.MinWidth` (default 8.43, Excel's standard width)
    and `MaxWidth` (default 60).
  - Columns that have no measurable cell keep their default width.

  `AutoFitOptions` also controls which columns are fitted (`Columns`), extra width
  (`ExtraWidth`), the culture used for rendering (`Format`), and streaming sampling
  (`MaxRowsToMeasure`). When a loaded workbook is auto-fitted, its stored column widths are
  replaced only for fitted columns that have data. `Sheet.ClearAutoFit()` turns auto-fit
  off; `Column.IsAutoFit` reports it.
- **`NumberFormatter`** is new and public. It renders a value with an Excel format code to
  the text Excel displays: `Format(value, code, options)`, plus `FormatDetailed`, which also
  returns the colour, the fill character and whether the value rendered as a date.
  - Covered: General; the digit placeholders 0, # and ?; decimals; grouping and scaling
    commas; percent; scientific and engineering notation; fractions; up to four sections
    with conditions; colours; literals, escapes, `_x` and `*x`; currency tags; text
    sections.
  - Dates and times: elapsed time ([h] [mm] [ss]), AM/PM, fractional seconds, and the 1900
    system (including the fictitious 29-Feb-1900) or the 1904 system.
  - Culture-aware separators, group sizes (e.g. en-IN 1,00,000) and month/day names come
    from `NumberFormatOptions`.
  - `NumberFormatter.ToSerial(DateTime)` converts a date to an Excel serial.

### Implementation notes
- **Text widths.** Widths come from embedded advance-width tables generated from the
  metric-compatible fonts: Carlito (Calibri), Caladea (Cambria), and Liberation Sans, Serif
  and Mono (Arial, Times New Roman, Courier New).
  - Other common fonts map to the nearest table with a scale factor.
  - Against real rendering, Latin text measures within about 2%, erring wide.
  - Indic, Arabic, CJK and other scripts use per-script estimates.
  - Kerning and ligatures are ignored.
- **Streaming writes `<cols>` late.** The streaming writer leaves `<cols>` out of the temp
  file and records its byte offset. At save time the measured widths are inserted there, so
  the sheet data is not passed over a second time.
- **Number-format checks.** The renderer was diffed against LibreOffice over a 26 × 60
  value/format grid. The remaining differences are all places where LibreOffice itself
  departs from Excel: dates before 1-Mar-1900, negative dates, built-ins 14/22/47, General
  digit limits, the trailing point in `#.##`, rounding times to the second, and fraction
  padding.

### Tests
- **`numfmt` group: 109 checks.** Excel-known renderings for every feature above, plus all
  ECMA-376 built-in formats.
- **`autofit` group: 9 checks.** They cover the model, streaming (shared and inline strings,
  encryption, sampling) and export. Expected widths come from real font rendering, not from
  the library's own tables.

## 1.2.0 (2026-10-28)

Read-side style API ("option c": raw index, pre-resolved style, and the full style table),
closing Reader gaps SHEETS-01, SHEETS-02 and SHEETS-03. All additions are source- and
binary-compatible.

### Features
- **SHEETS-01: resolved cell styles.** `Workbook.Load` now populates `Cell.Style` with the
  effective style of each cell's format: font, fill, border, number format, alignment and
  protection looked up through their tables; theme, indexed and tinted colours turned into
  RGB hex; and the named-style parent applied. New `Cell.StyleIndex` is the raw s= index
  (-1 when absent or once `Style` is set in code). Cells on the workbook default format
  (no s= or s="0") keep `Style == null`; `CellStyleTable.DefaultCellStyle` describes them.
- **Full style table.** New `Workbook.StyleTable` and `XlsxReader.StyleTable`
  (`CellStyleTable`) expose every styles.xml list as stored: custom number formats, fonts,
  fills (pattern and gradient), borders, cellStyleXfs, cellXfs (with raw apply* flags),
  named cell styles, differential formats (dxfs), the indexed palette (custom or default)
  and recent colours. Also exposes the theme (`ThemeInfo`): the 12 scheme colours and the
  major/minor fonts, including per-script faces such as Tamil and Malayalam.
  Helpers: `ResolveCellFormat(index)`, `ResolveColor(ColorRef)`, `GetNumberFormatCode(id)`,
  `IsLocaleDependentNumberFormat(id)`, `BuiltInNumberFormats` (the ECMA-376 "All Languages"
  table), `DefaultIndexedColors`.
- **Extended `CellStyle`.** New init-only properties: `UnderlineKind`, `Strikethrough`,
  `FontVerticalPosition`, `FontFamily`, `FontCharset`, `FontScheme`, `FillPattern`,
  `FillForegroundColor`, `FillBackgroundColor`, `FillGradient`, `BorderDiagonal`,
  `DiagonalUp`, `DiagonalDown`, `Indent`, `TextRotation`, `ShrinkToFit`, `ReadingOrder`,
  `JustifyLastLine`, `Locked`, `FormulaHidden`, `QuotePrefix`, `NumberFormatId`,
  `StyleName`. They sit outside the positional constructor, so existing code is unaffected.
- **Vertical "top" alignment is now written.** New init-only `CellStyle.VerticalAlignment`
  (`VerticalAlign?`) records an explicit vertical alignment and is always written.
  `CellStyleBuilder.VAlign(...)` and loaded styles set it. Previously `VerticalAlign.Top`
  (the enum's default value) was indistinguishable from "not set" and was silently dropped,
  so Excel showed Bottom. Styles that never set vertical alignment, including positional
  `new CellStyle(..., VAlign: Top, ...)`, write exactly what they did before. Note: a
  builder style with `.VAlign(x)` now carries `VerticalAlignment = x`, so it no longer
  compares equal to the same positional `CellStyle` without it.
- **`BorderStyle` covers all 14 Excel styles.** Added `MediumDashed`, `DashDot`,
  `MediumDashDot`, `DashDotDot`, `MediumDashDotDot`, `SlantDashDot`. Existing values are
  unchanged, and the writer emits the new ones.
- **SHEETS-02: dimensions.** `Sheet.Format` (sheetFormatPr defaults), `Sheet.ColumnRanges`
  (<col> spans kept as stored, not expanded), richer `sheet.Columns[n]` (`Hidden`,
  `OutlineLevel`, `Collapsed`, `BestFit`, `CustomWidth`, `StyleIndex`, `Style`, `Range`),
  and new `sheet.Rows[n]` / `Sheet.DefinedRows` (`Height`, `CustomHeight`, `Hidden`,
  `OutlineLevel`, `Collapsed`, `StyleIndex`, `CustomFormat`, `Style`, ...).
- **SHEETS-03: merged ranges.** `Sheet.MergeRanges` is now public and populated on load.
- **Streaming reader.** `RowReader.GetStyleIndex(column)` returns each cell's s= index.
- **Options.** `XlsxReaderOptions.IncludeStyledEmptyCells` (default false) loads cells that
  carry a style but no value. New overloads `Workbook.Load(path, password, options)` and
  `Workbook.Load(stream, options)`.

### Behaviour notes
- **Single pass.** Each sheet part is still parsed once: layout metadata is collected
  during the existing row stream.
- **Spans on save.** Loaded column spans are written back as spans. A width set through
  `Columns[n].Width` splits the span around that column. Column widths are now emitted
  sorted.
- **apply* inheritance.** A cellXf component uses the parent named-style's value only when
  its apply* flag is explicitly "0". Excel writes expanded cellXfs, so Excel files resolve
  identically either way; LibreOffice-written files rely on the flags.
- **Tint.** Resolution uses the spec formula in continuous HLS. It can differ from Excel's
  own palette swatches by up to about 2 units per channel.
- **Locale-dependent formats.** Number format ids that ECMA-376 leaves to the UI language
  (e.g. 5-8 currency, 41-44 accounting) resolve to `NumberFormat == null` with
  `NumberFormatId` kept.

### Known limitations (write side)
- **Extended properties are read-only.** The extended `CellStyle` properties are not yet
  written. Load → save keeps fonts, colours, solid fills, all border styles, number formats
  with a code, horizontal and vertical alignment, and wrap. It drops underline kind (double becomes single),
  strikethrough, super/subscript, non-solid pattern and gradient fills, diagonals, indent,
  rotation, shrink, reading order, protection, quote prefix, and locale-dependent number
  formats.
- **Layout is read-only.** Row heights, visibility, grouping and row styles, and column
  visibility, grouping and styles, are not written.

### Fixes
- **cellXfs detection.** Replaced a heuristic that told cellXfs from cellStyleXfs by the
  presence of xfId. styles.xml is now parsed by section, so date detection uses the right
  list.
- **Vertical "top" alignment.** It was dropped by the writer; see Features.
- **Malformed s=.** A non-numeric cell s= attribute is treated as absent, not as xf 0.
- **Absolute relationship targets.** Workbook relationship targets starting with "/" now
  resolve correctly. Styles and theme are now located through the workbook relationships,
  with the previous filename search as fallback.

### Tests
- **New `StyleApiTest` group: 37 checks.** It runs against Excel-shaped fixtures assembled
  from raw parts. It is wired into the manual runner as `styleapi` and into xUnit as
  `StyleApi`.
- **Cross-checks against other tools.** Results were cross-checked against openpyxl-written
  and LibreOffice-written files.

## 1.1.2 (2026-06-26)

### Performance
- `SharedStringTable.GetOrAdd` uses `CollectionsMarshal.GetValueRefOrAddDefault` for a
  single dictionary probe per call instead of two. Hot path on every string-cell write;
  observable speedup at 100K+ row scale.
- `XlsxWriter` placeholder scanner unified between sync and async paths. The async path
  previously allocated two `byte[]` per buffer-fill via `Encoding.ASCII.GetBytes`; now
  uses static UTF-8 byte arrays, zero allocations per call.
- `XlsxReader` lazy-allocates its inline-string `StringBuilder` only when an `<is>`
  element is actually encountered. Most cells are not inline strings, so this removes one
  per-cell allocation in the common case.

### Engineering
- `SheetWriter`'s async XML methods (`EnsureWorksheetOpenAsync`, `BeginRowAsync`,
  `EndRowAsync`, `AppendCellAsync`) collapsed into thin sync-call wrappers. The previous
  explicit-async XmlWriter calls duplicated ~60 lines of sync logic without adding real
  async value (the underlying FileStream is already async-buffered).
- No public API changes. No output-format changes. No crypto changes. All 60 tests pass on
  both Linux and Windows.

## 1.1.1 (2026-06-11)

- Restructured into the `chuvadi` monorepo. Shared internals (OOXML packaging, MS-OFFCRYPTO
  crypto, hardening utilities) extracted to `shared/Chuvadi.Internal` and source-linked, so the
  shipped DLL remains a single zero-dependency assembly. No functional or public API changes.
- Repository URL now points to https://github.com/arunshivab/chuvadi.

## 1.1.0 — 2026-06-11

Implements the audit's outstanding recommendations.

### Features
- **Streaming encryption.** `XlsxWriterOptions.Encryption` lets the streaming
  `XlsxWriter` produce encrypted output: the package is assembled into a temp file and
  encrypted in 4096-byte segments with an incrementally computed HMAC, so the plaintext
  workbook is never fully resident in memory. `Workbook.SaveTo(path, encryption)` now
  spools through a temp file instead of buffering the plaintext package in memory.
- **Hostile-input caps.** `ZipExtractionLimits` (MaxEntries / MaxEntryBytes /
  MaxTotalBytes, counted on actual decompressed bytes — declared sizes are not trusted)
  for zip extraction, and `XlsxReaderOptions.MaxPartBytes` capping the decompressed size
  of any xlsx package part. Both opt-in; exceeding a cap throws with a clear message.

### Engineering
- **xUnit test project** (`tests/Chuvadi.Sheets.Tests`) drives every verification group
  as individual facts plus targeted unit tests (tamper rejection, stream/array
  encryption equivalence, header/footer XML).
- **CI** (`.github/workflows/ci.yml`): build with warnings-as-errors, xUnit, the full
  end-to-end suite, and packing, on Linux and Windows; enforces the pure-BCL rule for
  the shipped library.
- **Release pipeline** (`.github/workflows/release.yml`): tag-triggered build/test/pack
  with optional NuGet package signing and NuGet.org publication; procedure in
  RELEASING.md (signed tags).
- **SECURITY.md** added: private disclosure channel, response expectations, and the
  documented security model.

## 1.0.0 — 2026-06-11

Audit-driven hardening and feature completion.

### Security / correctness
- **Encrypted-file integrity is now enforced on read.** `AgileEncryption.Decrypt`
  verifies the [MS-OFFCRYPTO] §2.3.4.14 HMAC-SHA512 over the encrypted package
  (constant-time comparison) before returning plaintext. Tampered or corrupted files
  now throw a clear integrity error instead of decrypting to garbage.
- **Spin count honored end-to-end.** `EncryptionOptions.SpinCount` is now actually
  applied when encrypting (it was previously silently ignored), and files written by
  other producers with non-default spin counts now decrypt correctly (content-key
  derivation previously hard-coded 100,000 iterations).
- **CFB reader: DIFAT-extension support.** Large encrypted containers whose FAT
  exceeds the 109 header DIFAT slots are now readable.
- **CFB reader: hostile-input hardening.** Directory-entry name lengths are bounds-
  checked against the 64-byte field.
- Removed dead code (`hmacIv`) and the obsolete `[Serializable]` attribute on
  `XlsxPasswordRequiredException`.

### Features
- **Page headers/footers.** `SheetWriter.SetHeaderFooter`, `Sheet.SetHeaderFooter`
  (model API), and `PageHeaderFooter(...)` on both export configs emit the OOXML
  `<headerFooter>` element (Excel header/footer codes supported: `&L/&C/&R`, `&P`,
  `&N`, `&D`, `&T`, `&F`, `&A`).

### Documentation
- README rewritten around capabilities; build-sequence narrative retired to this file.
- Corrected the encryption summary (100,000-iteration default; "PBKDF2-style iterated
  SHA-512" wording), the CFB DIFAT capacity figure, the stale `Workbook` model
  comments, and the `Workbook.Load` docs (now accurately describe value-only
  round-trips). The `CellIs` conditional-format rendering limitation (no dxf records)
  is documented on the public API.
- Internal codename strings removed from doc examples and tests.

### Pre-1.0 history
The library was built in eight incremental steps: (1) OOXML packaging,
(2) shared strings + styles, (3) streaming writer, (4) tables/hyperlinks/comments/
freeze/merge, (5) ergonomic export + workbook model, (6) reader + round-trips,
(7) zip namespace, (8) protection + agile encryption.
