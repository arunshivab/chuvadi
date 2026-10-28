using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Random-access in-memory workbook model. Use this when you want to build a workbook by
/// setting individual cells (rather than streaming row-by-row), or when you need to compose
/// a workbook in arbitrary order.
///
/// <code>
/// var wb = new Workbook();
/// var sheet = wb.AddSheet("Data");
/// sheet.Cell("A1").Value = "Hello";
/// sheet.Cell("B1").Formula = "SUM(C:C)";
/// sheet.Range("A1:D1").Merge();
/// sheet.Columns[1].Width = 20;
/// sheet.FreezeRows(1);
/// wb.SaveTo("file.xlsx");
/// </code>
///
/// Memory cost is proportional to populated cells (plus sheet metadata). For very large
/// datasets (10K+ rows), prefer the streaming <see cref="XlsxWriter"/> API directly.
///
/// Supports both building workbooks from scratch and load → edit → save round-trips via
/// <see cref="Load(string)"/>.
///
/// PERFORMANCE NOTE: when saving, row gaps are filled with empty row elements — a sheet
/// with cells only at A1 and A100000 still emits 100,000 row elements. The model API is
/// intended for ordinary dense sheets; for huge or very sparse data, use the streaming
/// <see cref="XlsxWriter"/> instead.
/// </summary>
public sealed class Workbook
{
    private readonly List<Sheet> _sheets = new();

    /// <summary>The sheets in the workbook, in tab order.</summary>
    public IReadOnlyList<Sheet> Sheets => _sheets;

    /// <summary>Workbook-level defined names (named ranges and named scalars).</summary>
    public DefinedNameCollection DefinedNames { get; } = new();

    /// <summary>
    /// The complete style table of a loaded workbook (styles.xml plus theme), exactly as stored:
    /// fonts, fills, borders, number formats, cell and named-style formats, named styles,
    /// differential formats, the indexed palette and the theme. A cell's style index (the s=
    /// attribute) indexes <see cref="CellStyleTable.CellFormats"/>.
    /// <see cref="CellStyleTable.Empty"/> for a workbook built in memory (the writer builds its
    /// own table at save time). Read-only: changes are made through <see cref="Cell.Style"/>.
    /// </summary>
    public CellStyleTable StyleTable { get; internal set; } = CellStyleTable.Empty;

    internal bool IsStructureProtected { get; private set; }
    internal string? StructureProtectionPassword { get; private set; }
    internal bool LockStructure { get; private set; }
    internal bool LockWindows { get; private set; }

    /// <summary>
    /// Protects the workbook structure (sheet add/remove/rename/reorder) and/or window
    /// arrangement. Hashes the password at save time. Independent of per-sheet protection
    /// — use <see cref="Sheet.Protect(string, SheetProtectionOptions?)"/> to lock cells too.
    /// </summary>
    public Workbook Protect(string password, bool lockStructure = true, bool lockWindows = false)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password required.", nameof(password));
        IsStructureProtected = true;
        StructureProtectionPassword = password;
        LockStructure = lockStructure;
        LockWindows = lockWindows;
        return this;
    }

    /// <summary>Adds a new sheet at the end of the tab order.</summary>
    public Sheet AddSheet(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Sheet name required.", nameof(name));
        foreach (var existing in _sheets)
            if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"A sheet named '{name}' already exists.");
        var sheet = new Sheet(name) { Owner = this };
        _sheets.Add(sheet);
        return sheet;
    }

    /// <summary>Looks up a sheet by name (case-insensitive). Throws if not found.</summary>
    public Sheet this[string name]
    {
        get
        {
            foreach (var s in _sheets)
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                    return s;
            throw new KeyNotFoundException($"No sheet named '{name}'.");
        }
    }

    /// <summary>Saves the workbook to the given xlsx file path.</summary>
    public void SaveTo(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        if (_sheets.Count == 0) throw new InvalidOperationException("Workbook has no sheets.");

        using var writer = XlsxWriter.Create(path);
        WorkbookSerializer.WriteWorkbook(writer, this);
        writer.Save();
    }

    /// <summary>Saves the workbook to the given stream. The stream is not closed.</summary>
    public void SaveTo(System.IO.Stream output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (_sheets.Count == 0) throw new InvalidOperationException("Workbook has no sheets.");

        using var writer = XlsxWriter.Create(output);
        WorkbookSerializer.WriteWorkbook(writer, this);
        writer.Save();
    }

    /// <summary>
    /// Saves the workbook to an ENCRYPTED xlsx file. The file requires the password to open
    /// in Excel. Uses OOXML Agile Encryption (AES-256-CBC, PBKDF2-SHA512, HMAC-SHA512).
    /// </summary>
    public void SaveTo(string path, EncryptionOptions encryption)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        if (encryption is null) throw new ArgumentNullException(nameof(encryption));

        // 1. Spool the plaintext package to a temp file rather than holding the whole
        //    plaintext in managed memory. (The package writer closes the spool stream when
        //    assembly finishes, so the file is written by name and reopened for the
        //    encryption pass, then deleted.)
        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"chuvadi_sheets_pkg_{Guid.NewGuid():N}.tmp");
        try
        {
            using (var spool = new System.IO.FileStream(
                tempPath, System.IO.FileMode.CreateNew, System.IO.FileAccess.ReadWrite,
                System.IO.FileShare.None, bufferSize: 64 * 1024))
            {
                SaveTo(spool);
            }

            // 2. Encrypt + wrap in CFB, streaming the plaintext from the spool.
            using var spoolRead = new System.IO.FileStream(
                tempPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
            using var output = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
            Chuvadi.Internal.Crypto.EncryptedPackageWriter.WriteEncrypted(
                output, spoolRead, spoolRead.Length, encryption.Password, encryption.SpinCount);
        }
        finally
        {
            try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>
    /// Loads an existing xlsx file into a Workbook: cell values (strings, numbers, dates,
    /// booleans; formula cells load as their cached values), and, since 1.2.0, the read-side
    /// style data: <see cref="StyleTable"/>, each cell's <see cref="Cell.StyleIndex"/> and
    /// resolved <see cref="Cell.Style"/>, merged ranges, column spans, row attributes and the
    /// sheet format defaults.
    ///
    /// Round-trip (load → save) keeps values, merges, column widths and the style properties
    /// the writer supports. It does NOT keep: freeze panes, defined names, tables, charts,
    /// pivots, drawings, comments, conditional formats, row heights/visibility/grouping,
    /// column visibility/grouping/styles, formulas (cached values are written instead), or
    /// the extended <see cref="CellStyle"/> properties added in 1.2.0.
    ///
    /// For read-only access to large files, prefer <see cref="XlsxReader"/> directly.
    /// </summary>
    public static Workbook Load(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        return Load(path, password: null, options: null);
    }

    /// <summary>
    /// Loads a possibly-encrypted xlsx. If the file is encrypted, the password is required;
    /// if not encrypted, the password is ignored.
    /// </summary>
    public static Workbook Load(string path, string? password)
        => Load(path, password, options: null);

    /// <summary>
    /// Loads a possibly-encrypted xlsx with reader options (e.g.
    /// <see cref="XlsxReaderOptions.IncludeStyledEmptyCells"/>, <see cref="XlsxReaderOptions.MaxPartBytes"/>).
    /// Pass null for <paramref name="password"/> when the file is not encrypted.
    /// <see cref="XlsxReaderOptions.TreatFirstRowAsHeaders"/> is ignored: the model always loads row 1 as data.
    /// </summary>
    public static Workbook Load(string path, string? password, XlsxReaderOptions? options)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var opts = ModelOptions(options);

        using var fileStream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);

        // Detect CFB (encrypted) vs. raw zip (unencrypted).
        if (Chuvadi.Internal.Crypto.EncryptedPackageReader.IsEncryptedPackage(fileStream))
        {
            byte[] plaintext;
            try
            {
                plaintext = Chuvadi.Internal.Crypto.EncryptedPackageReader.DecryptToPlaintextPackage(fileStream, password);
            }
            catch (Chuvadi.Internal.Crypto.PackagePasswordException ex)
            {
                throw new XlsxPasswordRequiredException(ex.Message, ex);
            }
            using var plaintextMs = new System.IO.MemoryStream(plaintext);
            using var reader = XlsxReader.Open(plaintextMs, opts);
            return WorkbookLoader.Load(reader);
        }
        else
        {
            using var reader = XlsxReader.Open(fileStream, opts);
            return WorkbookLoader.Load(reader);
        }
    }

    /// <summary>Loads a workbook from an open stream. The stream is not closed.</summary>
    public static Workbook Load(System.IO.Stream input) => Load(input, options: null);

    /// <summary>Loads a workbook from an open stream with reader options. The stream is not closed.</summary>
    public static Workbook Load(System.IO.Stream input, XlsxReaderOptions? options)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        using var reader = XlsxReader.Open(input, ModelOptions(options));
        return WorkbookLoader.Load(reader);
    }

    /// <summary>The caller's options (copied, never mutated) with headers off for the model loader.</summary>
    private static XlsxReaderOptions ModelOptions(XlsxReaderOptions? options)
    {
        var o = options?.Clone() ?? new XlsxReaderOptions();
        o.TreatFirstRowAsHeaders = false;
        return o;
    }
}

/// <summary>
/// One sheet in a <see cref="Workbook"/>. Cells are stored in a sparse dictionary keyed by
/// (row, column) — empty cells consume no memory.
/// </summary>
public sealed class Sheet
{
    private readonly Dictionary<(int Row, int Col), Cell> _cells = new();
    private readonly Dictionary<int, double> _columnWidths = new();      // 1-based column → width set via Columns[n].Width
    private readonly List<ColumnRange> _columnRanges = new();            // <col> spans as loaded from a file
    private readonly Dictionary<int, RowData> _rows = new();             // 1-based row → stored <row> attributes
    private readonly List<string> _mergeRanges = new();
    private readonly List<(string Range, DataValidation Validation)> _validations = new();
    private readonly List<(string Range, ConditionalFormatRule Rule)> _conditionalFormats = new();

    internal Sheet(string name) { Name = name; }

    /// <summary>The workbook this sheet belongs to (for style-table lookups).</summary>
    internal Workbook? Owner { get; init; }

    internal CellStyleTable StyleTable => Owner?.StyleTable ?? CellStyleTable.Empty;

    public string Name { get; }
    public int FreezeRowCount { get; private set; }
    public int FreezeColumnCount { get; private set; }
    public string? AutoFilterRange { get; private set; }

    internal bool IsProtected { get; private set; }
    internal string? ProtectionPassword { get; private set; }
    internal SheetProtectionOptions? ProtectionOptions { get; private set; }

    /// <summary>
    /// Protects the sheet against editing. See <see cref="SheetProtectionOptions"/> for
    /// per-action flags. The password is hashed at save time.
    /// </summary>
    public Sheet Protect(string password, SheetProtectionOptions? options = null)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password required.", nameof(password));
        IsProtected = true;
        ProtectionPassword = password;
        ProtectionOptions = options ?? new SheetProtectionOptions();
        return this;
    }

    /// <summary>Per-column accessor: <c>sheet.Columns[3].Width = 20</c>.</summary>
    public ColumnIndexer Columns => new(this);

    /// <summary>
    /// Per-row accessor: <c>sheet.Rows[5].Height</c>. Read-only in this release; reports the
    /// row attributes of a loaded file (height, hidden, grouping, row style).
    /// </summary>
    public RowIndexer Rows => new(this);

    /// <summary>
    /// Sheet-wide row and column defaults (&lt;sheetFormatPr&gt;). For a loaded file these are the
    /// file's values; for a sheet built in memory they hold Excel's defaults.
    /// </summary>
    public SheetFormat Format { get; } = new();

    /// <summary>
    /// The &lt;col&gt; spans of a loaded file, in file order, exactly as stored (not expanded per
    /// column). Empty for a sheet built in memory. Use <see cref="Columns"/> for per-column lookup.
    /// </summary>
    public IReadOnlyList<ColumnRange> ColumnRanges => _columnRanges;

    /// <summary>Rows for which the file stored attributes (height, hidden, style, ...), in row order.</summary>
    public IEnumerable<Row> DefinedRows
    {
        get
        {
            var keys = new List<int>(_rows.Keys);
            keys.Sort();
            foreach (var k in keys) yield return new Row(k, _rows[k], StyleTable);
        }
    }

    /// <summary>Iteration over only the populated cells.</summary>
    public IEnumerable<Cell> PopulatedCells => _cells.Values;

    internal IReadOnlyDictionary<(int Row, int Col), Cell> CellMap => _cells;
    /// <summary>
    /// Merged cell ranges as A1 range strings (e.g. "A1:C2"), in the order they were added or,
    /// for a loaded workbook, in file order. The top-left cell of each range holds the value.
    /// Populated by <see cref="Workbook.Load(string)"/>; add ranges with <see cref="MergeCells"/>.
    /// </summary>
    public IReadOnlyList<string> MergeRanges => _mergeRanges;
    internal IReadOnlyList<(string Range, DataValidation Validation)> Validations => _validations;
    internal IReadOnlyList<(string Range, ConditionalFormatRule Rule)> ConditionalFormats => _conditionalFormats;

    /// <summary>Gets or creates a cell at the given A1 address.</summary>
    public Cell Cell(string address)
    {
        var (row, col) = CellAddress.ParseA1(address);
        return Cell(row, col);
    }

    /// <summary>Gets or creates a cell at the given (1-based row, 1-based column).</summary>
    public Cell Cell(int row, int column)
    {
        if (!_cells.TryGetValue((row, column), out var cell))
        {
            cell = new Cell(row, column);
            _cells[(row, column)] = cell;
        }
        return cell;
    }

    /// <summary>Returns a range descriptor for the given A1 range.</summary>
    public Range Range(string range)
    {
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        return new Range(this, range);
    }

    /// <summary>Merges a range of cells. The merged value comes from the top-left cell.</summary>
    public Sheet MergeCells(string range)
    {
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        _mergeRanges.Add(range);
        return this;
    }

    /// <summary>Adds an autofilter over the given range.</summary>
    public Sheet AutoFilter(string range)
    {
        AutoFilterRange = range ?? throw new ArgumentNullException(nameof(range));
        return this;
    }

    /// <summary>Freezes the first <paramref name="rows"/> rows.</summary>
    public Sheet FreezeRows(int rows)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        FreezeRowCount = rows;
        return this;
    }

    /// <summary>Freezes the first <paramref name="cols"/> columns.</summary>
    public Sheet FreezeColumns(int cols)
    {
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        FreezeColumnCount = cols;
        return this;
    }

    /// <summary>The page header shown when printing (Excel header/footer codes). Null = none.</summary>
    public string? PageHeader { get; private set; }

    /// <summary>The page footer shown when printing (Excel header/footer codes). Null = none.</summary>
    public string? PageFooter { get; private set; }

    /// <summary>
    /// Sets the page header and/or footer for printing and Page Layout view. Text uses Excel's
    /// header/footer codes (&amp;L/&amp;C/&amp;R sections, &amp;P page number, &amp;N page count,
    /// &amp;D date, &amp;F file name, &amp;A sheet name); plain text is centered.
    /// Pass null to leave a part unset.
    /// </summary>
    public Sheet SetHeaderFooter(string? header, string? footer)
    {
        PageHeader = header;
        PageFooter = footer;
        return this;
    }

    /// <summary>Adds a data validation rule over the given range.</summary>
    public Sheet AddDataValidation(string range, DataValidation validation)
    {
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        if (validation is null) throw new ArgumentNullException(nameof(validation));
        _validations.Add((range, validation));
        return this;
    }

    /// <summary>Adds a conditional formatting rule over the given range.</summary>
    public Sheet AddConditionalFormat(string range, ConditionalFormatRule rule)
    {
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        _conditionalFormats.Add((range, rule));
        return this;
    }

    // ---- Auto-fit ----------------------------------------------------------------------

    private AutoFitOptions? _autoFitAll;
    private AutoFitOptions? _autoFitColumnsOptions;
    private readonly SortedSet<int> _autoFitColumns = new();

    /// <summary>
    /// Sizes every column that has data to its content when the workbook is saved (cells as
    /// displayed: number format, font, size, bold). Widths set with <c>Columns[n].Width</c>
    /// win. Wrapped, merged, rotated and formula cells are not measured. Computed at save, so
    /// cells added after this call count. See <see cref="AutoFitOptions"/>.
    /// </summary>
    public Sheet AutoFitColumns(AutoFitOptions? options = null)
    {
        _autoFitAll = options ?? AutoFitOptions.Default;
        return this;
    }

    /// <summary>Turns off auto-fit set by <see cref="AutoFitColumns"/> or <see cref="Column.AutoFit"/>.</summary>
    public Sheet ClearAutoFit()
    {
        _autoFitAll = null;
        _autoFitColumns.Clear();
        return this;
    }

    internal void AddAutoFitColumn(int column, AutoFitOptions? options)
    {
        _autoFitColumns.Add(column);
        if (options is not null) _autoFitColumnsOptions = options;
    }

    internal bool IsAutoFitColumn(int column)
        => _autoFitAll is { } all ? (all.Columns is null || ContainsColumn(all.Columns, column))
                                  : _autoFitColumns.Contains(column);

    private static bool ContainsColumn(IReadOnlyCollection<int> cols, int c)
    {
        foreach (var x in cols) if (x == c) return true;
        return false;
    }

    /// <summary>The options the writer should auto-fit with, or null when auto-fit is off.</summary>
    internal AutoFitOptions? EffectiveAutoFit()
    {
        if (_autoFitAll is not null) return _autoFitAll;
        if (_autoFitColumns.Count == 0) return null;
        var o = _autoFitColumnsOptions ?? AutoFitOptions.Default;
        return new AutoFitOptions
        {
            MaxWidth = o.MaxWidth, MinWidth = o.MinWidth, MaxRowsToMeasure = o.MaxRowsToMeasure,
            ExtraWidth = o.ExtraWidth, AllowForFilterButton = o.AllowForFilterButton, Format = o.Format,
            Columns = new List<int>(_autoFitColumns),
        };
    }

    /// <summary>Width set through <c>Columns[n].Width</c> wins; otherwise the loaded span's width; otherwise 0.</summary>
    internal double GetColumnWidth(int column)
    {
        if (_columnWidths.TryGetValue(column, out var w)) return w;
        return FindColumnRange(column)?.Width ?? 0;
    }

    internal bool HasColumnWidthOverride(int column) => _columnWidths.ContainsKey(column);

    internal void SetColumnWidthInternal(int column, double width) =>
        _columnWidths[column] = width;

    /// <summary>The loaded span covering <paramref name="column"/>; the last one wins if a malformed file overlaps spans.</summary>
    internal ColumnRange? FindColumnRange(int column)
    {
        for (int i = _columnRanges.Count - 1; i >= 0; i--)
        {
            var r = _columnRanges[i];
            if (column >= r.Min && column <= r.Max) return r;
        }
        return null;
    }

    internal void AddColumnRange(ColumnRange range) => _columnRanges.Add(range);

    internal void SetRowData(int row, RowData data) => _rows[row] = data;

    internal Row GetRow(int row) => new(row, _rows.TryGetValue(row, out var d) ? d : null, StyleTable);

    /// <summary>
    /// The column widths the writer should emit: loaded spans that carry a width, split around
    /// columns whose width was set through <c>Columns[n].Width</c>, plus those set columns —
    /// sorted and non-overlapping, as Excel requires. A sheet built in memory yields exactly its
    /// set columns, as before.
    /// </summary>
    internal List<(int Min, int Max, double Width)> GetColumnWidthsForWrite()
    {
        var result = new List<(int Min, int Max, double Width)>();
        var overrides = new List<int>(_columnWidths.Keys);
        overrides.Sort();

        // Auto-fitted columns that have data drop their loaded span width (auto-fit decides);
        // a width set through Columns[n].Width still wins. Spans are split around both.
        var cut = new SortedSet<int>(overrides);
        if (_autoFitAll is not null || _autoFitColumns.Count > 0)
            foreach (var key in _cells.Keys)
                if (IsAutoFitColumn(key.Col)) cut.Add(key.Col);

        foreach (var span in _columnRanges)
        {
            if (span.Width <= 0) continue;
            int start = span.Min;
            foreach (var o in cut)
            {
                if (o < start || o > span.Max) continue;
                if (o > start) result.Add((start, o - 1, span.Width));
                start = o + 1;
            }
            if (start <= span.Max) result.Add((start, span.Max, span.Width));
        }
        foreach (var o in overrides)
            result.Add((o, o, _columnWidths[o]));

        result.Sort((a, b) => a.Min.CompareTo(b.Min));
        return result;
    }
}

/// <summary>
/// Indexer for <c>sheet.Columns[N]</c>. Returns a settable column accessor.
/// </summary>
public readonly struct ColumnIndexer
{
    private readonly Sheet _sheet;
    internal ColumnIndexer(Sheet sheet) { _sheet = sheet; }
    public Column this[int column1Based] => new(_sheet, column1Based);
}

/// <summary>
/// Per-column settings accessor. Class (not struct) so property setters work via the indexer.
/// <see cref="Width"/> is settable; the other properties report what a loaded file stores for
/// the &lt;col&gt; span covering this column and are read-only in this release.
/// </summary>
public sealed class Column
{
    private readonly Sheet _sheet;
    private readonly int _column;
    internal Column(Sheet sheet, int column) { _sheet = sheet; _column = column; }

    /// <summary>The 1-based column number.</summary>
    public int Number => _column;

    /// <summary>
    /// The column width in Excel character units. 0 means default width (see
    /// <see cref="SheetFormat.DefaultColumnWidth"/>). A width set here overrides a loaded value.
    /// </summary>
    public double Width
    {
        get => _sheet.GetColumnWidth(_column);
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _sheet.SetColumnWidthInternal(_column, value);
        }
    }

    /// <summary>The loaded &lt;col&gt; span covering this column, or null if the file defines none.</summary>
    public ColumnRange? Range => _sheet.FindColumnRange(_column);

    /// <summary>True if the width is user-set: set through <see cref="Width"/>, or customWidth in the file.</summary>
    public bool CustomWidth => _sheet.HasColumnWidthOverride(_column) || (Range?.CustomWidth ?? false);

    /// <summary>True if the file marks the width as best-fit (auto-fit).</summary>
    public bool BestFit => Range?.BestFit ?? false;

    /// <summary>True if the column is hidden.</summary>
    public bool Hidden => Range?.Hidden ?? false;

    /// <summary>True if phonetic (furigana) information is shown for the column.</summary>
    public bool Phonetic => Range?.Phonetic ?? false;

    /// <summary>Outline (grouping) level, 0..7. 0 = not grouped.</summary>
    public int OutlineLevel => Range?.OutlineLevel ?? 0;

    /// <summary>True if the outline group this column belongs to is collapsed.</summary>
    public bool Collapsed => Range?.Collapsed ?? false;

    /// <summary>Default cell-format (xf) index for the column's empty cells, or -1 when none.</summary>
    public int StyleIndex => Range?.StyleIndex ?? -1;

    /// <summary>
    /// Sizes this column to its content when the workbook is saved. A width set through
    /// <see cref="Width"/> wins. Options (caps, culture) apply to all individually auto-fitted
    /// columns of the sheet; the last options passed are used.
    /// </summary>
    public Column AutoFit(AutoFitOptions? options = null)
    {
        _sheet.AddAutoFitColumn(_column, options);
        return this;
    }

    /// <summary>True if this column will be auto-fitted on save.</summary>
    public bool IsAutoFit => _sheet.IsAutoFitColumn(_column);

    /// <summary>
    /// The resolved default style for the column's empty cells (from <see cref="StyleIndex"/>),
    /// or null when the column has none.
    /// </summary>
    public CellStyle? Style => StyleIndex < 0 ? null : _sheet.StyleTable.ResolveCellFormat(StyleIndex);
}

/// <summary>
/// One cell in a sheet. Created lazily by <see cref="Sheet.Cell(string)"/> — accessing a
/// cell that hasn't been touched materializes an empty one and stores it in the sheet.
/// </summary>
public sealed class Cell
{
    public int Row { get; }
    public int Column { get; }
    public string Address => CellAddress.ToA1(Row, Column);

    /// <summary>The cell's value. Setting <see cref="Value"/> clears <see cref="Formula"/> and vice versa.</summary>
    public object? Value
    {
        get => _value;
        set { _value = value; _formula = null; }
    }

    /// <summary>An Excel formula (without leading '='). Setting clears Value.</summary>
    public string? Formula
    {
        get => _formula;
        set { _formula = value; _value = null; }
    }

    /// <summary>
    /// Per-cell style. Null means "use default". For a loaded workbook this is the effective,
    /// fully resolved style of the cell's format (font, fill, border, number format, alignment,
    /// protection; theme and tint colours as RGB). It is null for cells whose format is the
    /// workbook default (no s= or s="0"): see <see cref="CellStyleTable.DefaultCellStyle"/>.
    /// Setting it marks the cell's style as set in code (<see cref="StyleIndex"/> becomes -1).
    /// </summary>
    public CellStyle? Style
    {
        get => _style;
        set { _style = value; StyleIndex = -1; }
    }

    /// <summary>
    /// The raw style index read from the file (the cell's s= attribute), indexing
    /// <see cref="Workbook.StyleTable"/>'s <see cref="CellStyleTable.CellFormats"/>.
    /// -1 when the file gave none, or once <see cref="Style"/> has been set in code.
    /// </summary>
    public int StyleIndex { get; private set; } = -1;

    /// <summary>Load path: record the file's style index and its resolved style together.</summary>
    internal void SetLoadedStyle(int styleIndex, CellStyle? style)
    {
        _style = style;
        StyleIndex = styleIndex;
    }

    private CellStyle? _style;
    private object? _value;
    private string? _formula;

    internal Cell(int row, int column) { Row = row; Column = column; }

    internal bool IsFormula => _formula is not null;
    internal bool IsEmpty => _value is null && _formula is null;
}

/// <summary>
/// A rectangular range descriptor returned by <see cref="Sheet.Range(string)"/>. Supports
/// merging the range as one operation.
/// </summary>
public readonly struct Range
{
    private readonly Sheet _sheet;
    public string Address { get; }

    internal Range(Sheet sheet, string address)
    {
        _sheet = sheet;
        Address = address;
    }

    /// <summary>Merges the range. The displayed value comes from the top-left cell.</summary>
    public Range Merge()
    {
        _sheet.MergeCells(Address);
        return this;
    }
}

/// <summary>
/// Workbook-level defined names — named ranges and named scalars usable in formulas.
/// Example: after <c>wb.DefinedNames.Add("TaxRate", "0.18")</c>, a formula
/// <c>"Subtotal * TaxRate"</c> resolves correctly when Excel opens the file.
/// </summary>
public sealed class DefinedNameCollection
{
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<KeyValuePair<string, string>> All => _names;
    public int Count => _names.Count;

    /// <summary>Adds or replaces a defined name.</summary>
    public void Add(string name, string formulaOrReference)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name required.", nameof(name));
        if (string.IsNullOrEmpty(formulaOrReference)) throw new ArgumentException("Reference required.", nameof(formulaOrReference));
        // Excel name rules (simplified): must start with letter or underscore; no spaces; reserved names disallowed.
        if (!IsValidName(name))
            throw new ArgumentException($"'{name}' is not a valid defined name. Names must start with a letter or '_' and contain no spaces or special characters.", nameof(name));
        _names[name] = formulaOrReference;
    }

    public bool Remove(string name) => _names.Remove(name);
    public bool TryGet(string name, out string value) => _names.TryGetValue(name, out value!);

    private static bool IsValidName(string n)
    {
        if (n.Length == 0) return false;
        if (n[0] != '_' && !char.IsLetter(n[0])) return false;
        foreach (var c in n)
            if (c != '_' && c != '.' && !char.IsLetterOrDigit(c)) return false;
        return true;
    }
}
