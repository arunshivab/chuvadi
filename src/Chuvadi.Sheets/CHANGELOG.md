# Changelog

## 1.4.0 (unreleased)

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

## 1.3.0 (unreleased)

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
