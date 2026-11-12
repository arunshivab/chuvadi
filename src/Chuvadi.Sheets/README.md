# Chuvadi.Sheets

A pure-BCL spreadsheet (xlsx) library for .NET 10. **No NuGet dependencies.** (Its zip API,
`Chuvadi.Sheets.Zip`, is obsolete since 2.0.0: use the Chuvadi.Zip package.)

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
| Hostile-input limits, cancellation | `XlsxReaderOptions.ForUntrustedInput()`, `MaxRowsPerSheet`, `MaxSpinCount`, `CancellationToken` |
| Macro detection (never executed) | `reader.HasMacros`, `wb.HasMacros` |
| Tables, hyperlinks, comments | `SheetWriter.AddTable/Hyperlink/Comment` |
| Freeze panes, merges, autofilter | `SheetWriter.FreezeRows/MergeCells/AutoFilter` |
| Page headers/footers (print) | `SheetWriter.SetHeaderFooter`, `Sheet.SetHeaderFooter`, `cfg.PageHeaderFooter` |
| Data validation | `DataValidation.List/WholeNumber/...` |
| Conditional formatting | `ConditionalFormat.ColorScale/DataBar/CellIs`* |
| Row/column settings, views, panes, tab colour, visibility (read and write) | `sheet.Rows[n]`, `sheet.Columns[n]`, `sheet.View`, `RowFormat`, `SetColumns`, `SheetWriter.View`, `TabColor`, `Visibility`, `ActiveSheet` |
| Conditional formats and validation (read, write, evaluate per cell) | `sheet.ConditionalFormats`, `ConditionalRule`, `sheet.EvaluateConditionalFormats()`, `ConditionalFormatEvaluator`, `sheet.DataValidations` |
| Pictures, shapes, charts on sheets (read: anchors, image bytes, chart metadata) | `sheet.Drawings`, `sheetReader.ReadDrawings()`, `DrawingObject`, `drawing.Chart` (`ChartInfo`), `Emu` |
| Rich text runs (read and write) | `cell.RichText`, `row.GetRichText(col)`, `RichText`, `RichTextRun`, `RichTextFont` |
| Hyperlinks (read and write; cells, ranges, in-workbook) | `sheet.Hyperlinks`, `sheet.FindHyperlink(r, c)`, `layout.Hyperlinks`, `SheetWriter.AddHyperlink` |
| Comments (read and write; runs, anchors, visibility) | `sheet.Comments`, `sheetReader.ReadComments()`, `SheetWriter.AddComment` |
| Header/footer pictures, chart sheets (read) | `sheet.HeaderFooterPictures`, `wb.ChartSheets`, `reader.ReadChartSheet(info)` |
| Strict OOXML and lenient part names (read) | `wb.IsStrict`, `reader.IsStrict` |
| Load report (cells outside the grid) | `wb.LoadWarnings`, `sheetReader.OutOfGridCells` |
| Integration guide for renderers | `docs/Chuvadi.Sheets/LAYOUT-INTEGRATION.md` |
| Defined names (workbook and sheet scope; read and write) | `wb.DefinedNames.Add(...)`, `XlsxWriter.AddDefinedName(...)`, `reader.DefinedNames` |
| Sheet protection | `sheet.Protect(password, options)` |
| Workbook structure protection | `wb.Protect(password, lockStructure)` |
| Workbook encryption (open-password) | `wb.SaveTo(path or stream, new EncryptionOptions {...})` (stream since 2.1.0) |
| Writer options on every save (2.1.0) | `wb.SaveTo(path or stream, XlsxWriterOptions)`, `ToXlsx(..., options, configure)`, `new XlsxExport(path or stream, options)`, `table.ToXlsx(stream)` |
| Saves with no temp-folder file (2.1.0) | `XlsxWriterOptions.TempStorage = TempStorage.Memory` |
| Header check on the streaming reader (2.1.0) | `row.HasColumn(name)`, `row.HeaderNames`, `sheetReader.HeaderNames` |
| One-line reads from a stream (2.1.0) | `stream.ReadXlsx()`, `stream.ReadXlsxSheet(...)`, `stream.ReadXlsx<T>(...)` |
| Column widths in pixels and points, A1 references (2.1.0) | `column.WidthInPixels()`, `WidthInPoints()`, `sheet.Format.DefaultWidthInPixels()`, `CellReference` |
| Trimming and NativeAOT (2.1.0) | `IsTrimmable`, `IsAotCompatible`; typed APIs annotated |
| Streaming export + encryption | `XlsxWriter` with `XlsxWriterOptions.Encryption` |
| Read encrypted xlsx | `Workbook.Load(path, password)` |
| Zip create/extract (obsolete since 2.0.0; use the Chuvadi.Zip package) | `Chuvadi.Sheets.Zip.*` |
| Hostile-input caps | `XlsxReaderOptions.MaxPartBytes` and the 1.4.0 limits |

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

// Zip archives: use the Chuvadi.Zip package (Chuvadi.Sheets.Zip is obsolete since 2.0.0).

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

// Cancel a slow open (a large or encrypted file): a timeout, or a token a Cancel button sets
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    var book = Workbook.Load("large.xlsx", null, new XlsxReaderOptions { CancellationToken = timeout.Token });
}
catch (OperationCanceledException) { /* the user cancelled, or the timeout passed */ }
// Checked while the package is checked, every 256 shared strings, every 4,096 key-derivation spins and every
// row. Run it off the UI thread so the Cancel button stays responsive.

// 2.1.0: check an upload's columns before reading values
using (var check = XlsxReader.Open(uploadStream))
    foreach (var row in check.Sheet(1).Rows)
    {
        if (!row.HasColumn("Patient Id")) throw new InvalidDataException("Missing column: Patient Id");
        break;                                              // row.HeaderNames lists them all
    }

// 2.1.0: every save takes XlsxWriterOptions; TempStorage.Memory leaves no file in the temp folder
wb.SaveTo(responseBody, new XlsxWriterOptions
{
    Encryption = new EncryptionOptions { Password = password },
    TempStorage = TempStorage.Memory,
});
List<Patient> fromUpload = uploadStream.ReadXlsx<Patient>();   // one-line reads on a stream too

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
var acct = NumberFormatter.FormatDetailed(1234.5, "_($* #,##0.00_)");   // " $1,234.50 ", fill ' ' at FillIndex 2

// Rich text, hyperlinks and comments (1.7.0)
var title = new RichText(new RichTextRun("Total: ", new RichTextFont { Bold = true }),
                         new RichTextRun("42", new RichTextFont { Color = ColorRef.FromRgb("FF00B050") }));
sh.Cell("A1").RichText = title;                                    // Value is "Total: 42"
sh.AddHyperlink("A2", target: "https://example.com", tooltip: "site");
sh.AddHyperlink("A3", location: "'Other Sheet'!B5");
sh.AddComment("B2", "Asha", "Checked");
foreach (var run in book.Sheets[0].FindCell(1, 1)?.RichText?.Runs ?? Array.Empty<RichTextRun>())
    Console.WriteLine($"{run.Text}: bold={run.Font?.Bold} colour={run.ResolvedColor}");
```

## Encryption details

- OOXML Agile Encryption per [MS-OFFCRYPTO] §2.3.4.10–15.
- AES-256-CBC for content, iterated-SHA-512 key derivation (PBKDF2-style),
  HMAC-SHA512 for integrity — **verified on both write and read**. A tampered or
  corrupted encrypted file fails its integrity check instead of decrypting to garbage.
- 100,000-iteration default spin count (matches modern Excel); configurable via
  `EncryptionOptions.SpinCount` and honored when reading files written with
  non-default spin counts. Reading caps the declared spin count at
  `XlsxReaderOptions.MaxSpinCount` (default 500,000), and the reader's `CancellationToken`
  interrupts key derivation (1.7.0).
- On Windows, real-time antivirus (Microsoft Defender) scans an encrypted Office file when
  it is opened, by trying Excel's default password, which runs the full key derivation at
  the file's declared spin count. That happens inside the operating system's file-open call,
  before the library reads a byte, so neither `MaxSpinCount` nor the `CancellationToken` can
  shorten it: a 3,000,000-spin file took about 13 s to open the first time on a test machine,
  then 9 ms once the scan result was cached. Both limits apply in full to the library's own
  work, and to streams that are already open or held in memory.
- Wrapped in a Compound File Binary (CFB) container — same format Excel uses. Above about
  7 MB the container is extended with DIFAT sectors ([MS-CFB] §2.5); the reader supports them too.
  **Before 2.0.1 the writer did not write DIFAT sectors:** an encrypted file larger than about
  7 MB was saved without an error but could not be opened by any reader. Save such files again.
- An encrypted save to a stream writes from the stream's current position (bytes already in it
  are kept), works on a non-seekable stream, and leaves the stream open (2.0.1).
- Reading an encrypted file decrypts the whole package into memory before it is read, so memory
  grows with the file size, and on a server with the number of encrypted files open at once.
  `XlsxReaderOptions.MaxTotalUncompressedBytes` caps the encrypted file's size before it is
  decrypted (`ForUntrustedInput()` sets 1 GiB); use it for untrusted input.
- Wrong password and missing password both throw `XlsxPasswordRequiredException`. A damaged
  container or a failed integrity check throws `XlsxFormatException` (see Errors below).
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
  the OS temp directory, or `XlsxWriterOptions.TempDirectory`). Since 2.0.1 every temp file is
  opened exclusively, readable by the current user only on Linux and macOS (mode 600), and
  deleted when closed, including by the operating system if the process dies.
- Since 2.0.1 an encrypted save (`Workbook.SaveTo(path, EncryptionOptions)` or
  `XlsxWriterOptions.Encryption`) writes no plaintext to disk: the package spool and the sheet
  spools are encrypted with AES-256-CTR under a one-time key held only in memory. (Before 2.0.1
  the package was staged in plaintext and could survive a crash.)
- Since 2.0.1 a save to a path writes `.<name>.<8 hex>.tmp` beside the target and replaces the
  target only when the save succeeds; a failed or cancelled save, or a writer disposed without
  `Save`, leaves an existing file untouched. A replaced file keeps its permissions (and on Windows
  its attributes and security settings); a symbolic link is kept. A save to a stream leaves the
  stream open.
- Decompression caps for untrusted input are opt-in: `XlsxReaderOptions.MaxPartBytes` and
  the other read limits (`XlsxReaderOptions.ForUntrustedInput()`) for reading xlsx. Set them whenever
  the input source is not fully trusted. Defaults are unlimited for compatibility.
- See [SECURITY.md](SECURITY.md) for the vulnerability-disclosure process and the full
  security model.
- Zip password protection is intentionally not provided (classic ZipCrypto is broken
  and AES-zip is not in the BCL). Use workbook encryption instead.

## Errors

Since 2.0.0 the read API reports a malformed file with one exception type. Every exception the
readers throw is one of these:

| Exception | When |
|---|---|
| `XlsxFormatException` | **The file is malformed:** not a zip or a damaged zip, malformed XML in any part, a missing required part (the workbook part or a worksheet part, whether or not `[Content_Types].xml` declares it; since 2.0.2 also when it does not), a damaged relationship (`.rels`) entry, a truncated part, a text cell whose shared-string reference does not exist (2.0.2), or (with a password given) a damaged encrypted container or a failed integrity check. **Since 2.0.5 also an old binary file** (an Excel 97-2003 .xls, or another compound file that is not an encrypted xlsx), named in the message, for example "This is an Excel 97-2003 binary file (.xls), not an Office Open XML package, and it is not encrypted."; it asked for a password before. `InnerException` holds the original error (`XmlException`, `InvalidDataException`, `FileNotFoundException`, ...), when there is one. |
| `XlsxPasswordRequiredException` | The file is encrypted and no password was given, or the password is wrong. Without a password the encrypted container is not read, so damage to it is found only once a password is supplied. |
| `XlsxLimitExceededException` (an `IOException`) | A read limit was exceeded: `MaxRowsPerSheet`, `MaxTotalUncompressedBytes`, `MaxCompressionRatio`, `MaxPackageEntries`, `MaxSpinCount`. Since 2.1.0 it also has a constructor with an inner exception, as Chuvadi.Docs' `DocxLimitExceededException`. |
| `InvalidDataException` | `MaxPartBytes` was exceeded (a limit, as in 1.x; not a malformed file). |
| `OperationCanceledException` | The `CancellationToken` was cancelled. |
| `ArgumentException`, `ArgumentNullException`, `KeyNotFoundException`, `InvalidCastException`, `InvalidOperationException` | Caller mistakes: a bad argument, no sheet with that name, a typed getter on a value of another type, a call in the wrong state. |
| I/O exceptions (`FileNotFoundException`, `UnauthorizedAccessException`, ...) | Opening a path failed (the file itself is missing or cannot be read). A part missing inside the package is `XlsxFormatException`. |

This applies to `XlsxReader.Open`, `Workbook.Load`, row enumeration, `ReadLayout`,
`ReadDrawings`, `ReadComments`, `ReadHeaderFooterPictures`, `ReadChartSheet`,
`GetImageBytes` and the `ReadXlsx` extensions. With `StrictXml` off (the default), a malformed
theme or chart part is still skipped rather than reported, as before. The obsolete
`Chuvadi.Sheets.Zip` API keeps its own `ZipFormatException`.

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
validation round-trip as well. Since 1.7.0, rich-text runs, hyperlinks and comments (with their
runs, anchors and visibility) round-trip. Drawings, header/footer pictures and chart sheets are
read (`sheet.Drawings`, `sheet.HeaderFooterPictures`, `wb.ChartSheets`) but not written.

## Out of scope

Writing charts, chart sheets and drawings; pivots; custom table styles; macros; zip
encryption. (Differential formats are read via `CellStyleTable.DifferentialFormats`
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
│       ├── Model/        ← public xlsx namespace (Chuvadi.Sheets.Model)
│       ├── Zip/          ← public zip namespace (Chuvadi.Sheets.Zip), obsolete since 2.0.0
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

## Platforms and threads

- **One managed DLL:** no native code, no font files, no NuGet dependencies. It runs wherever .NET 10 runs: Windows,
  Linux and macOS (CI runs the suites on all three), Android and iOS, containers and servers.
- **Threads:** static state (the format-code cache, the font metrics) is safe from any thread. Instances are not
  thread-safe: use one writer, reader or workbook per thread, or per request on a server.
- **Writable folders:** a path save writes beside its target, and every save spools to the temp folder unless
  `XlsxWriterOptions.TempStorage = TempStorage.Memory` (2.1.0). With `Memory` and a stream, a save needs no writable
  folder. Reading never writes a file.
- **Invariant globalization:** builds with `InvariantGlobalization` work; display text uses the invariant culture
  unless `DisplayCulture` is given.
- **Trimming and NativeAOT (2.1.0):** the package is marked `IsTrimmable` and `IsAotCompatible`, the build runs the
  analyzers with warnings as errors, and `ToXlsx<T>`, `XlsxExport.AddSheet<T>` and `ReadXlsx<T>` keep `T`'s public
  properties (`[DynamicallyAccessedMembers]`). Before 2.1.0, under NativeAOT, they wrote empty cells and read empty
  properties.
- **Encrypted reads** decrypt the package into memory, so memory grows with file size times files open at once;
  `MaxTotalUncompressedBytes` bounds it.

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
- **One assembly, multiple namespaces.** `Chuvadi.Sheets.Model` and
  `Chuvadi.Sheets.Zip` are public; `Chuvadi.Sheets.Internal` houses shared plumbing.

## Constraints baked in

- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- `<Nullable>enable</Nullable>`
- No NuGet package references in the SHIPPED LIBRARY (`src/`). Adding one is a
  deliberate violation, and CI enforces it by scanning the project files. Dev-time
  test tooling (xUnit) lives in `tests/` and never ships.

## License

MIT — see [LICENSE](LICENSE).
