# Chuvadi.Sheets

A pure-BCL spreadsheet (xlsx) and zip library for .NET 10. **No NuGet dependencies.**

Part of the **Chuvadi** family (alongside `Chuvadi.Pdf`).

> சுவடி (Chuvadi) — palm-leaf manuscript, the original document format of South India.

---

## Capabilities

| Capability | Public API |
|---|---|
| Write xlsx (streaming, 100K+ rows) | `XlsxWriter` |
| Write xlsx (random-access model) | `Workbook` |
| Write xlsx (one-liner export) | `.ToXlsx()`, `XlsxExport` |
| Read xlsx (streaming) | `XlsxReader` |
| Read xlsx (model) | `Workbook.Load()` |
| Read styles: resolved per cell | `cell.Style`, `cell.StyleIndex` |
| Read styles: full style table + theme | `wb.StyleTable` / `reader.StyleTable` (`CellStyleTable`) |
| Read merges, column spans, row attributes | `sheet.MergeRanges`, `sheet.ColumnRanges`, `sheet.Columns[n]`, `sheet.Rows[n]`, `sheet.Format` |
| Read xlsx (typed/ergonomic) | `.ReadXlsx<T>()`, `.ReadXlsxSheet()` |
| Cell styling | `CellStyle`, `CellStyleBuilder` |
| Column auto-fit | `sheet.AutoFitColumns()`, `SheetWriter.AutoFitColumns()`, `cfg.AutoFitColumns()` |
| Number format rendering (display text) | `NumberFormatter.Format(value, formatCode)` |
| Cell display text on read | `cell.DisplayText`, `row.GetDisplayString(col)` |
| Page setup on read (print area, titles, paper, margins, breaks, header/footer) | `sheet.PageSetup`, streaming `sheetReader.ReadLayout().PageSetup` |
| Page setup on write | `SheetWriter.PageSetup`, `sheet.PageSetup`, `cfg.PageSetup(p => ...)` |
| Formulas on read (shared formulas expanded) with cached results | `row.GetFormula(col)`, `row.GetFormulaInfo(col)`, `cell.Formula`, `cell.CachedValue` |
| Formulas on write with cached results, array formulas | `rb.Formula(f, cachedValue)`, `rb.ArrayFormula(range, f, cached)`, `XlsxWriterOptions.FullCalculationOnLoad` |
| Layout for renderers (widths, merges, row attributes, effective styles) | `ReadLayout()`, `row.Attributes`, `row.GetStyle(col)`, `row.GetEffectiveStyleIndex(col, layout)`, `sheet.GetEffectiveStyle(r, c)` |
| Encrypted streaming read | `XlsxReader.Open(path, password, options)` |
| Hostile-input limits, cancellation | `XlsxReaderOptions.ForUntrustedInput()`, `MaxRowsPerSheet`, `CancellationToken` |
| Macro detection (never executed) | `reader.HasMacros`, `wb.HasMacros` |
| Tables, hyperlinks, comments | `SheetWriter.AddTable/Hyperlink/Comment` |
| Freeze panes, merges, autofilter | `SheetWriter.FreezeRows/MergeCells/AutoFilter` |
| Page headers/footers (print) | `SheetWriter.SetHeaderFooter`, `Sheet.SetHeaderFooter`, `cfg.PageHeaderFooter` |
| Data validation | `DataValidation.List/WholeNumber/...` |
| Conditional formatting | `ConditionalFormat.ColorScale/DataBar/CellIs`* |
| Row/column settings, views, panes, tab colour, visibility (read and write) | `sheet.Rows[n]`, `sheet.Columns[n]`, `sheet.View`, `RowFormat`, `SetColumns`, `SheetWriter.View`, `TabColor`, `Visibility`, `ActiveSheet` |
| Conditional formats and validation (read, write, evaluate per cell) | `sheet.ConditionalFormats`, `ConditionalRule`, `sheet.EvaluateConditionalFormats()`, `ConditionalFormatEvaluator`, `sheet.DataValidations` |
| Pictures, shapes, charts on sheets (read: anchors, image bytes) | `sheet.Drawings`, `sheetReader.ReadDrawings()`, `DrawingObject`, `Emu` |
| Defined names (workbook and sheet scope; read and write) | `wb.DefinedNames.Add(...)`, `XlsxWriter.AddDefinedName(...)`, `reader.DefinedNames` |
| Sheet protection | `sheet.Protect(password, options)` |
| Workbook structure protection | `wb.Protect(password, lockStructure)` |
| Workbook encryption (open-password) | `wb.SaveTo(path, new EncryptionOptions {...})` |
| Streaming export + encryption | `XlsxWriter` with `XlsxWriterOptions.Encryption` |
| Read encrypted xlsx | `Workbook.Load(path, password)` |
| Zip create/extract | `Chuvadi.Sheets.Zip.*` |
| Hostile-input caps | `ZipExtractionLimits`, `XlsxReaderOptions.MaxPartBytes` and the 1.4.0 limits |

\* Since 1.6.0 the style of a `CellIs` rule is written as a differential format and shows in
Excel (earlier versions wrote none).

## Quick examples

```csharp
// One-liner table export with a printed header/footer
patients.ToXlsx("report.xlsx", cfg => cfg
    .SheetName("Patients")
    .AsTable("PatientsTbl")
    .FreezeHeaderRow()
    .PageHeaderFooter("&CQuarterly Patient Report", "&LConfidential&RPage &P of &N"));

// Password-protected workbook (real AES-256 encryption)
wb.SaveTo("file.xlsx", new EncryptionOptions { Password = "secret" });
var loaded = Workbook.Load("file.xlsx", "secret");

// Streaming export of huge data, encrypted — plaintext never held in memory
using (var writer = XlsxWriter.Create("big.xlsx", new XlsxWriterOptions
{
    Encryption = new EncryptionOptions { Password = "secret" },
}))
{
    using var sheet = writer.AddSheet("Data");
    foreach (var row in MillionsOfRows()) sheet.WriteRow(r => r.Cell(row.Id).Cell(row.Name));
    writer.Save();
}

// Extracting an UNTRUSTED archive: caps stop decompression bombs and entry floods
zipPath.ExtractTo(targetDir, new ZipExtractionLimits
{
    MaxEntries = 10_000,
    MaxEntryBytes = 256 * 1024 * 1024,
    MaxTotalBytes = 1024L * 1024 * 1024,
});

// Sheet protection (file unencrypted; cells locked against editing)
sheet.Protect("password", new SheetProtectionOptions {
    AllowSelectLockedCells = true,
    AllowSort = true,
    AllowAutoFilter = true,
});

// Reading styles (1.2.0): three layers — resolved, raw index, full table
var book = Workbook.Load("in.xlsx", null, new XlsxReaderOptions { IncludeStyledEmptyCells = true });
var sh = book.Sheets[0];
var cell = sh.Cell("B2");
CellStyle effective = cell.Style ?? book.StyleTable.DefaultCellStyle ?? default;  // flat, colours as RGB
int xf = cell.StyleIndex < 0 ? 0 : cell.StyleIndex;          // raw s= index (-1 = none → xf 0)
var font = book.StyleTable.Fonts[book.StyleTable.CellFormats[xf].FontId];   // walk the chain yourself
string? rgb = book.StyleTable.ResolveColor(font.Color);      // theme / indexed / tint → "RRGGBB"
foreach (var m in sh.MergeRanges) { /* "A1:C2" */ }
double widthB = sh.Columns[2].Width;  bool hidden5 = sh.Rows[5].Hidden;

// Auto-fit columns to their content (explicit widths still win)
patients.ToXlsx("fit.xlsx", cfg => cfg.AutoFilter().AutoFitColumns());
sheet.AutoFitColumns(new AutoFitOptions { MaxWidth = 40 });     // Workbook model, computed at save

// Untrusted upload: limits, cancellation, password, display text
var opts = XlsxReaderOptions.ForUntrustedInput();
opts.CancellationToken = requestAborted;
using (var upload = XlsxReader.Open(uploadStream, password, opts))
    foreach (var row in upload.Sheet(1).Rows)
        Console.WriteLine(row.GetDisplayString(0));        // "15/03/2023", "1,234.50", ...

// Formulas with cached results (1.5.0): readers that do not calculate still show values
sheet.WriteRow(rb => rb.Cell(120).Cell(18).Formula("A2*B2/100", 21.6));
foreach (var row in reader.Sheet(1).Rows)
    Console.WriteLine($"{row.GetFormula(2)} = {row[2]}");   // "A2*B2/100 = 21.6" (shared formulas expanded)

// Page setup on write and on the streaming reader (1.5.0)
var page = sheet.PageSetup;                    // SheetWriter, or Workbook Sheet
page.PaperSizeCode = 9;                        // A4
page.Orientation = PageOrientation.Landscape;
page.FitTo(1, 0);                              // one page wide, as many tall as needed
page.Margins.Set(0.5, 0.5, 0.75, 0.75);
page.PrintTitleRows = (1, 1);                  // repeat the header row
page.SetPrintArea("A1:H200");
page.AddRowBreak(50);
var layout = reader.Sheet("Report").ReadLayout();   // before streaming rows
double widthA = layout.ColumnWidth(1);  var area = layout.PageSetup.PrintAreas;

// Conditional formats as Excel shows them, per cell (1.6.0), for renderers
var cf = book.Sheets[0].EvaluateConditionalFormats();
var cfStyle = cf.Evaluate(5, 2)?.ApplyTo(cell.Style ?? default);   // fill, font, number format, borders
// cf.Evaluate(r, c)?.DataBar / .Icon → draw them; .NotEvaluated → rules this library cannot evaluate

// Row, column, view and drawing round-trip (1.6.0)
sh.Rows[3].Height = 30;  sh.Columns[5].Hidden = true;  sh.View.Freeze(1, 1).ZoomScale = 90;
foreach (var d in sh.Drawings)
    if (d.Kind == DrawingKind.Picture) File.WriteAllBytes(d.Name + ".png", d.GetImageBytes()!);

// Render a value exactly as Excel displays it
string shown = NumberFormatter.Format(1234.5, "#,##0.00");        // "1,234.50"

// Zip a set of files
using (var zip = ZipWriter.Create("bundle.zip"))
{
    zip.AddFile("report.xlsx", "/out/report.xlsx");
    zip.AddText("readme.txt", "Generated by Chuvadi.Sheets");
}
```

## Encryption details

- OOXML Agile Encryption per [MS-OFFCRYPTO] §2.3.4.10–15.
- AES-256-CBC for content, iterated-SHA-512 key derivation (PBKDF2-style),
  HMAC-SHA512 for integrity — **verified on both write and read**. A tampered or
  corrupted encrypted file fails its integrity check instead of decrypting to garbage.
- 100,000-iteration default spin count (matches modern Excel); configurable via
  `EncryptionOptions.SpinCount` and honored when reading files written with
  non-default spin counts.
- Wrapped in a Compound File Binary (CFB) container — same format Excel uses.
  The reader supports DIFAT-extended containers (large encrypted files).
- Wrong password and missing password both throw `XlsxPasswordRequiredException`.
- Encryption is available on both the `Workbook` model and the streaming `XlsxWriter`
  (via `XlsxWriterOptions.Encryption`); the streaming path encrypts in 4096-byte
  segments with an incrementally computed HMAC, so even very large exports never hold
  the plaintext workbook in memory.
- Round-trip is verified in-library and against an independent third-party
  implementation of MS-OFFCRYPTO; final verification in desktop Excel is recommended
  for your own deployment.

Note that **sheet/workbook protection is not encryption** — it is Excel's cooperative
edit-locking, removable by a determined user. For confidentiality, use the workbook
open-password (`EncryptionOptions`).

### Security notes for integrators

- The streaming writer spools sheet XML to temp files (`chuvadi_sheets_*.tmp` under
  the OS temp directory, or `XlsxWriterOptions.TempDirectory`); they are deleted on
  dispose. Encrypted saves stage the plaintext package in a temp file that is deleted
  after the encryption pass. Consider both if you handle highly sensitive data.
- Decompression caps for untrusted input are opt-in: `ZipExtractionLimits` for
  extraction and `XlsxReaderOptions.MaxPartBytes` for reading xlsx. Set them whenever
  the input source is not fully trusted. Defaults are unlimited for compatibility.
- See [SECURITY.md](SECURITY.md) for the vulnerability-disclosure process and the full
  security model.
- Zip password protection is intentionally not provided (classic ZipCrypto is broken
  and AES-zip is not in the BCL). Use workbook encryption instead.

## Reading styles: what round-trips

`Workbook.Load` reads the full style data (see `CellStyleTable`), but the writer does not yet
emit everything it reads. On load → save, the extended `CellStyle` properties added in 1.2.0
(underline kind, strikethrough, super/subscript, non-solid pattern and gradient fills,
diagonal borders, indent, rotation, shrink-to-fit, reading order, protection, quote prefix),
and locale-dependent built-in number formats (e.g. id 44) are not written back. Fonts,
colours, solid fills, all border styles, number formats with a code, horizontal and vertical
alignment, wrap, merges and column widths are. Since 1.5.0, formulas (with cached results),
defined names and the full page setup round-trip too; formulas that refer to other workbooks
or to tables, and data-table cells, are saved as their cached values. Since 1.6.0, row and
column settings (height, width, hidden, outline, styles), the sheet view (panes, zoom,
gridlines, selection), tab colour, visibility, the active sheet, conditional formats and data
validation round-trip as well. Drawings are read (`sheet.Drawings`) but not written.

## Out of scope

Charts, pivots, custom table styles, embedded images, rich text, macros,
zip encryption. (Differential formats are read via `CellStyleTable.DifferentialFormats`
but not written.)

## Solution layout

```
Chuvadi.Sheets/
├── Chuvadi.Sheets.sln
├── Directory.Build.props
├── README.md
├── CHANGELOG.md
├── src/
│   └── Chuvadi.Sheets/
│       ├── Excel/        ← public xlsx namespace (Chuvadi.Sheets.Excel)
│       ├── Zip/          ← public zip namespace (Chuvadi.Sheets.Zip)
│       └── Internal/     ← OOXML packaging, writers, CFB/crypto plumbing
├── samples/
│   └── Chuvadi.Sheets.ManualTests/   ← end-to-end verification suite
└── tests/
    └── Chuvadi.Sheets.Tests/         ← xUnit suite (drives the groups above; CI entry point)
```

## Build

Requires the **.NET 10 SDK**.

```
dotnet build
```

## Tests & CI

Two complementary layers, both run by CI (`.github/workflows/ci.yml`) on every push and
pull request, on Linux and Windows:

```
dotnet test tests/Chuvadi.Sheets.Tests          # xUnit — granular pass/fail per area
dotnet run --project samples/Chuvadi.Sheets.ManualTests   # end-to-end suite + inspectable files
```

CI also enforces the pure-BCL rule (fails if any `PackageReference` appears under
`src/`) and packs the NuGet artifact. Tagged releases go through
`.github/workflows/release.yml`, which supports package signing — see
[RELEASING.md](RELEASING.md).

## Run the verification suite manually

```
dotnet run --project samples/Chuvadi.Sheets.ManualTests
```

Runs every test group in sequence (writer, styles, streaming/stress, features,
ergonomic export, reader round-trips, zip, protection + encryption). Output files are
written to `artifacts/bin/Chuvadi.Sheets.ManualTests/Debug/net10.0/output/` —
open them in Excel to verify visually.

## Design principles

- **Pure BCL.** No NuGet, no third-party DLLs. Only what ships in the .NET runtime.
- **Streaming-first.** 100K+ row exports without holding the workbook in memory.
- **Excel as ground truth.** Files must open in Excel without warnings.
- **One assembly, multiple namespaces.** `Chuvadi.Sheets.Excel` and
  `Chuvadi.Sheets.Zip` are public; `Chuvadi.Sheets.Internal` houses shared plumbing.

## Constraints baked in

- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- `<Nullable>enable</Nullable>`
- No NuGet package references in the SHIPPED LIBRARY (`src/`). Adding one is a
  deliberate violation, and CI enforces it by scanning the project files. Dev-time
  test tooling (xUnit) lives in `tests/` and never ships.

## License

MIT — see [LICENSE](LICENSE).
