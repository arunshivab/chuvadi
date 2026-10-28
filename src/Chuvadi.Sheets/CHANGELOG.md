# Changelog

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
