using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Streaming writer for a single worksheet. Obtained via <see cref="XlsxWriter.AddSheet"/>.
///
/// The worksheet XML structure is built up in this order, deferred until the first row is
/// written so that "sheet shape" operations (column widths, freeze panes, sheet views) can
/// be set after AddSheet but before the first row:
///
///   &lt;worksheet&gt;
///     &lt;sheetPr&gt;...&lt;/sheetPr&gt;             — fit-to-page (inserted at save when set)
///     &lt;sheetViews&gt;...&lt;/sheetViews&gt;       — freeze panes (deferred until first row)
///     &lt;cols&gt;...&lt;/cols&gt;                    — column widths (deferred until first row)
///     &lt;sheetData&gt;...&lt;/sheetData&gt;           — rows (streamed)
///     sheetProtection, autoFilter, mergeCells, conditionalFormatting, dataValidations,
///     hyperlinks, printOptions, pageMargins, pageSetup, headerFooter, rowBreaks, colBreaks,
///     legacyDrawing (comments), tableParts                       — emitted at sheet close
///   &lt;/worksheet&gt;
///
/// Cells are written to a per-sheet temporary file; strings get placeholders that the parent
/// XlsxWriter resolves to final shared-string indices at save time. Temp files live under
/// the OS temp directory and are deleted on dispose.
/// </summary>
public sealed class SheetWriter : IDisposable, IAsyncDisposable, IRowSink
{
    // ---- Constants -----------------------------------------------------------------

    private const string SsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Shared-string placeholder marker prefix. Used by XlsxWriter at save time.</summary>
    internal const string SharedStringPlaceholderPrefix = "__CHUVADI_SS_";
    /// <summary>Shared-string placeholder marker suffix.</summary>
    internal const string SharedStringPlaceholderSuffix = "_END__";

    // ---- State ---------------------------------------------------------------------

    private readonly WorkbookWriteState _state;
    private readonly string _name;
    private readonly int _sheetIndex;
    private readonly string _tempPath;
    private readonly FileStream _tempStream;
    private readonly XmlWriter _xml;

    // "Sheet shape" — must be settable until the first row is written.
    private readonly List<(int Min, int Max, double Width)> _columnWidths = new();
    private int _freezeRows = 0;
    private int _freezeCols = 0;

    // Auto-fit: when enabled, <cols> is not written at open; its byte offset in the temp file
    // is recorded and XlsxWriter inserts the computed <cols> there at save time.
    private ColumnAutoFitter? _autoFit;
    private bool _measureRow;
    private long _colsInsertOffset = -1;
    private byte[]? _colsXml;

    // "After sheetData" features — emitted at sheet finalization.
    private readonly List<string> _mergeRanges = new();
    private string? _autoFilterRange;
    private readonly List<SheetHyperlink> _hyperlinks = new();
    private readonly List<SheetComment> _comments = new();
    private readonly List<SheetTable> _tables = new();
    private readonly List<(string Range, DataValidation V)> _dataValidations = new();
    private readonly List<(string Range, ConditionalFormatRule Rule)> _conditionalFormats = new();

    // Print settings — written at finalization; <sheetPr> (fit-to-page) is inserted at the
    // prologue offset recorded when the worksheet opens.
    private readonly PageSetup _pageSetup = new();
    private long _prologueInsertOffset = -1;
    private byte[]? _sheetPrXml;

    // Sheet protection — set via Protect().
    private bool _sheetProtected;
    private string? _protectionHashB64;
    private string? _protectionSaltB64;
    private int _protectionSpinCount;
    private SheetProtectionOptions? _protectionOptions;

    // Row tracking.
    private int _rowsWritten = 0;
    private int _currentColumn = 0;
    private bool _rowInProgress = false;
    private bool _worksheetOpened = false;     // True once we've emitted <worksheet>+<sheetViews>+<cols>+<sheetData>.
    private bool _disposed = false;
    private bool _finalized = false;

    // ---- Construction --------------------------------------------------------------

    internal SheetWriter(WorkbookWriteState state, string name, int sheetIndex, string tempPath)
    {
        _state = state;
        _name = name;
        _sheetIndex = sheetIndex;
        _tempPath = tempPath;

        _tempStream = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.Read,
            bufferSize: 64 * 1024,
            // useAsync=true: enables ReadAsync/WriteAsync without thread-pool blocking
            options: FileOptions.SequentialScan | FileOptions.Asynchronous);

        _xml = XmlWriter.Create(_tempStream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = false,
            // Allow Async = true is what gates the *Async XmlWriter methods.
            Async = true,
        });

        // NOTE: We do NOT call WriteStartElement("worksheet") here. That happens lazily
        // in EnsureWorksheetOpen(), invoked when the first row is written. This gives the
        // caller a chance to set freeze panes and column widths after AddSheet.
    }

    /// <summary>The sheet's user-facing name (as it will appear in Excel's tab bar).</summary>
    public string Name => _name;

    /// <summary>Number of rows written so far.</summary>
    public int RowsWritten => _rowsWritten;

    // ---- Sheet-shape API (must be called BEFORE the first row) ---------------------

    /// <summary>
    /// Sets the width (in Excel column-width units) for one or more contiguous columns.
    /// Must be called before any rows are written.
    /// </summary>
    public SheetWriter SetColumnWidth(int columnFrom, int columnTo, double width)
    {
        EnsureNotDisposed();
        EnsureShapeMutable();
        if (columnFrom < 1 || columnTo < columnFrom)
            throw new ArgumentException("Column range must be 1-based and non-empty.");
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _columnWidths.Add((columnFrom, columnTo, width));
        return this;
    }

    /// <summary>Convenience overload for a single column.</summary>
    public SheetWriter SetColumnWidth(int column, double width)
        => SetColumnWidth(column, column, width);

    /// <summary>
    /// Sizes columns to their content: every cell written afterwards is measured as displayed
    /// (number format, font, size, bold) and the widths are written when the sheet is saved.
    /// Widths set with <see cref="SetColumnWidth(int, double)"/> win over auto-fit. Wrapped,
    /// merged and rotated cells are not measured; formula cells are measured by their cached
    /// result (<see cref="RowBuilder.Formula(string, object?)"/>) when given. Must be called before any rows are
    /// written. See <see cref="AutoFitOptions"/> for caps, column selection and sampling.
    /// </summary>
    public SheetWriter AutoFitColumns(AutoFitOptions? options = null)
    {
        EnsureNotDisposed();
        EnsureShapeMutable();
        _autoFit = new ColumnAutoFitter(options);
        foreach (var m in _mergeRanges) _autoFit.AddMerge(m);
        return this;
    }

    /// <summary>
    /// Freezes the first <paramref name="rows"/> rows so they remain visible while scrolling.
    /// Must be called before any rows are written. Pass 0 to clear.
    /// </summary>
    public SheetWriter FreezeRows(int rows)
    {
        EnsureNotDisposed();
        EnsureShapeMutable();
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        _freezeRows = rows;
        return this;
    }

    /// <summary>
    /// Freezes the first <paramref name="cols"/> columns so they remain visible while scrolling.
    /// Must be called before any rows are written. Pass 0 to clear.
    /// </summary>
    public SheetWriter FreezeColumns(int cols)
    {
        EnsureNotDisposed();
        EnsureShapeMutable();
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        _freezeCols = cols;
        return this;
    }

    /// <summary>
    /// The sheet's print settings: paper, orientation, scale or fit-to, margins, header/footer,
    /// gridlines and headings, centring, page breaks, print area and print titles. Can be
    /// changed at any time until the workbook is saved. Nothing is written for settings left at
    /// their defaults.
    /// </summary>
    public PageSetup PageSetup => _pageSetup;

    // ---- Row API (sync) ------------------------------------------------------------

    /// <summary>Writes a header row of strings using the default style.</summary>
    public SheetWriter WriteHeader(params string[] columnNames)
    {
        EnsureWorksheetOpen();
        BeginRow();
        foreach (var name in columnNames) AppendCell(name, styleId: null);
        EndRow();
        return this;
    }

    /// <summary>Writes a row from a params array of values, with runtime type dispatch.</summary>
    public SheetWriter WriteRow(params object?[] values)
    {
        EnsureWorksheetOpen();
        BeginRow();
        foreach (var v in values) AppendCell(v, styleId: null);
        EndRow();
        return this;
    }

    /// <summary>Writes a row using a fluent <see cref="RowBuilder"/> for per-cell control.</summary>
    public SheetWriter WriteRow(Action<RowBuilder> build)
    {
        if (build is null) throw new ArgumentNullException(nameof(build));
        EnsureWorksheetOpen();
        BeginRow();
        build(new RowBuilder(this));
        EndRow();
        return this;
    }

    // ---- Row API (async) -----------------------------------------------------------

    /// <summary>Async equivalent of <see cref="WriteHeader"/>. Same XML output; non-blocking I/O.</summary>
    public async Task<SheetWriter> WriteHeaderAsync(params string[] columnNames)
    {
        await EnsureWorksheetOpenAsync().ConfigureAwait(false);
        await BeginRowAsync().ConfigureAwait(false);
        foreach (var name in columnNames) await AppendCellAsync(name, styleId: null).ConfigureAwait(false);
        await EndRowAsync().ConfigureAwait(false);
        return this;
    }

    /// <summary>Async equivalent of <see cref="WriteRow(object?[])"/>.</summary>
    public async Task<SheetWriter> WriteRowAsync(params object?[] values)
    {
        await EnsureWorksheetOpenAsync().ConfigureAwait(false);
        await BeginRowAsync().ConfigureAwait(false);
        foreach (var v in values) await AppendCellAsync(v, styleId: null).ConfigureAwait(false);
        await EndRowAsync().ConfigureAwait(false);
        return this;
    }

    // ---- Features collected and emitted at sheet close -----------------------------

    /// <summary>Merges a range of cells, e.g. <c>"A1:D1"</c>.</summary>
    public SheetWriter MergeCells(string range)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        _mergeRanges.Add(range);
        _autoFit?.AddMerge(range);
        return this;
    }

    /// <summary>Adds an autofilter over the given range.</summary>
    public SheetWriter AutoFilter(string range)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        _autoFilterRange = range;
        return this;
    }

    /// <summary>Adds a data validation rule over the given range.</summary>
    public SheetWriter AddDataValidation(string range, DataValidation validation)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        if (validation is null) throw new ArgumentNullException(nameof(validation));
        _dataValidations.Add((range, validation));
        return this;
    }

    /// <summary>Adds a conditional formatting rule over the given range.</summary>
    public SheetWriter AddConditionalFormat(string range, ConditionalFormatRule rule)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        _conditionalFormats.Add((range, rule));
        return this;
    }

    /// <summary>
    /// Protects the sheet against editing. The password is hashed (not stored in plaintext);
    /// Excel users must enter the same password to unprotect. Pass <paramref name="options"/>
    /// to control which actions are permitted while protected.
    /// </summary>
    /// <param name="password">Password required to unprotect. Cannot be null/empty.</param>
    /// <param name="options">Permission flags. If null, uses defaults (most restrictive).</param>
    public SheetWriter Protect(string password, SheetProtectionOptions? options = null)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password required.", nameof(password));

        var salt = Chuvadi.Internal.Crypto.PasswordHasher.GenerateSalt();
        _protectionHashB64 = Chuvadi.Internal.Crypto.PasswordHasher.ComputeHashBase64(
            password, salt, Chuvadi.Internal.Crypto.PasswordHasher.DefaultSpinCount);
        _protectionSaltB64 = Convert.ToBase64String(salt);
        _protectionSpinCount = Chuvadi.Internal.Crypto.PasswordHasher.DefaultSpinCount;
        _protectionOptions = options ?? new SheetProtectionOptions();
        _sheetProtected = true;
        return this;
    }

    /// <summary>
    /// Adds a structured table over the given range. The first row of the range MUST contain
    /// the header strings — same strings you wrote via WriteHeader. Excel will render filter
    /// dropdowns on the header row and apply <paramref name="style"/>'s appearance.
    /// </summary>
    /// <param name="range">The full table range including headers, e.g. <c>"A1:E100"</c>.</param>
    /// <param name="displayName">Display name shown in Excel's Name Box; also the structured-reference name.</param>
    /// <param name="style">Built-in table style; <see cref="TableStyle.None"/> for unstyled.</param>
    /// <param name="columnHeaders">The header strings, in column order. Must match what's actually in the header row.</param>
    public SheetWriter AddTable(string range, string displayName, TableStyle style, params string[] columnHeaders)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(range))       throw new ArgumentException("Range required.", nameof(range));
        if (string.IsNullOrEmpty(displayName)) throw new ArgumentException("Display name required.", nameof(displayName));
        if (columnHeaders is null || columnHeaders.Length == 0)
            throw new ArgumentException("At least one column header is required.", nameof(columnHeaders));

        // Tables get a workbook-unique internal name (table1, table2, ...) generated at save.
        // The user's displayName goes into the displayName attribute.
        var internalName = $"Table{_state.NextTableId()}";
        _tables.Add(new SheetTable(range, internalName, displayName, style, columnHeaders));
        return this;
    }

    /// <summary>
    /// Sets the page header and/or footer shown when the sheet is printed or viewed in
    /// Page Layout view. Text uses Excel's header/footer codes: <c>&amp;L</c>/<c>&amp;C</c>/<c>&amp;R</c>
    /// for left/center/right sections, <c>&amp;P</c> page number, <c>&amp;N</c> page count,
    /// <c>&amp;D</c> date, <c>&amp;T</c> time, <c>&amp;F</c> file name, <c>&amp;A</c> sheet name.
    /// Example: <c>sheet.SetHeaderFooter("&amp;CQuarterly Report", "&amp;LConfidential&amp;RPage &amp;P of &amp;N")</c>.
    /// Plain text without codes is centered by Excel. Pass null to leave a part unset.
    /// May be called at any point before the sheet is finalized.
    /// </summary>
    public SheetWriter SetHeaderFooter(string? header, string? footer)
    {
        EnsureNotDisposed();
        _pageSetup.HeaderFooter.OddHeader = header;
        _pageSetup.HeaderFooter.OddFooter = footer;
        return this;
    }

    // ---- IRowSink (for RowBuilder) -------------------------------------------------

    void IRowSink.AppendCell(object? value, int? styleId) => AppendCell(value, styleId);

    void IRowSink.AppendFormula(string formula, int? styleId, object? cachedValue, string? arrayRange)
    {
        if (!_rowInProgress) throw new InvalidOperationException("Cell appended outside of a row.");
        _currentColumn++;
        int? sid = cachedValue is null ? styleId : EffectiveStyleId(cachedValue, styleId);
        if (_measureRow && cachedValue is not null) Measure(_currentColumn, cachedValue, sid);
        WriteFormulaCell(_currentColumn, formula, sid, cachedValue, arrayRange);
    }

    int IRowSink.RegisterStyle(CellStyle style) => _state.Styles.GetCellXfId(style);

    void IRowSink.AppendHyperlink(string url, string display, int? styleId, string? tooltip)
    {
        if (!_rowInProgress) throw new InvalidOperationException("Cell appended outside of a row.");
        _currentColumn++;
        var address = CellAddress.ToA1(_rowsWritten, _currentColumn);
        // Apply the default hyperlink style (blue + underline) if user didn't supply one.
        var effectiveStyle = styleId ?? _state.DefaultHyperlinkStyleId;
        if (_measureRow) Measure(_currentColumn, display, effectiveStyle);
        WriteStringCell(_currentColumn, display, effectiveStyle);
        _hyperlinks.Add(new SheetHyperlink(address, url, tooltip));
    }

    void IRowSink.AppendComment(object? cellValue, int? styleId, string author, string text)
    {
        // First write the cell normally, then record the comment for emission at finalize.
        if (!_rowInProgress) throw new InvalidOperationException("Cell appended outside of a row.");
        var nextColumn = _currentColumn + 1;
        var address = CellAddress.ToA1(_rowsWritten, nextColumn);
        AppendCell(cellValue, styleId);
        _comments.Add(new SheetComment(address, author, text));
    }

    // ---- Internal sync row machinery -----------------------------------------------

    private void BeginRow()
    {
        if (_rowInProgress) throw new InvalidOperationException("A row is already in progress.");
        _rowsWritten++;
        _currentColumn = 0;
        _rowInProgress = true;
        _measureRow = _autoFit?.BeginRow() ?? false;

        _xml.WriteStartElement("row", SsNs);
        _xml.WriteAttributeString("r", _rowsWritten.ToString(CultureInfo.InvariantCulture));
    }

    private void EndRow()
    {
        if (!_rowInProgress) return;
        _xml.WriteEndElement();
        _rowInProgress = false;
    }

    /// <summary>
    /// Lazily opens the worksheet on the first cell-write. Emits &lt;worksheet&gt;, &lt;sheetViews&gt;
    /// (only if freeze panes are set), &lt;cols&gt;, and &lt;sheetData&gt; in that order.
    /// After this point, freeze panes and column widths cannot be changed.
    /// </summary>
    private void EnsureWorksheetOpen()
    {
        EnsureNotDisposed();
        if (_worksheetOpened) return;

        _xml.WriteStartDocument(standalone: true);
        _xml.WriteStartElement("worksheet", SsNs);
        _xml.WriteAttributeString("xmlns", "r", null, RelNs);

        // Close the start tag, flush, and remember where <sheetPr> belongs (fit-to-page can be
        // set until save). The bytes written are the same as before.
        _xml.WriteString(string.Empty);
        _xml.Flush();
        _prologueInsertOffset = _tempStream.Position;

        WriteSheetViews();
        if (_autoFit is null)
        {
            WriteCols();
        }
        else
        {
            // Close any open start tag, flush, and remember where <cols> belongs.
            _xml.WriteString(string.Empty);
            _xml.Flush();
            _colsInsertOffset = _tempStream.Position;
        }

        _xml.WriteStartElement("sheetData", SsNs);
        _worksheetOpened = true;
    }

    private void WriteSheetViews()
    {
        if (_freezeRows == 0 && _freezeCols == 0) return;

        _xml.WriteStartElement("sheetViews", SsNs);
        _xml.WriteStartElement("sheetView", SsNs);
        _xml.WriteAttributeString("workbookViewId", "0");

        _xml.WriteStartElement("pane", SsNs);
        if (_freezeCols > 0)
            _xml.WriteAttributeString("xSplit", _freezeCols.ToString(CultureInfo.InvariantCulture));
        if (_freezeRows > 0)
            _xml.WriteAttributeString("ySplit", _freezeRows.ToString(CultureInfo.InvariantCulture));
        _xml.WriteAttributeString("topLeftCell",
            CellAddress.ToA1(_freezeRows + 1, _freezeCols + 1));
        _xml.WriteAttributeString("activePane",
            (_freezeRows > 0 && _freezeCols > 0) ? "bottomRight"
            : (_freezeRows > 0)                  ? "bottomLeft"
                                                 : "topRight");
        _xml.WriteAttributeString("state", "frozen");
        _xml.WriteEndElement(); // </pane>

        _xml.WriteEndElement(); // </sheetView>
        _xml.WriteEndElement(); // </sheetViews>
    }

    private void WriteCols()
    {
        if (_columnWidths.Count == 0) return;
        _xml.WriteStartElement("cols", SsNs);
        foreach (var (min, max, width) in _columnWidths)
        {
            _xml.WriteStartElement("col", SsNs);
            _xml.WriteAttributeString("min", min.ToString(CultureInfo.InvariantCulture));
            _xml.WriteAttributeString("max", max.ToString(CultureInfo.InvariantCulture));
            _xml.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture));
            _xml.WriteAttributeString("customWidth", "1");
            _xml.WriteEndElement();
        }
        _xml.WriteEndElement();
    }

    /// <summary>The core sync cell-typing function.</summary>
    private void AppendCell(object? value, int? styleId)
    {
        if (!_rowInProgress) throw new InvalidOperationException("Cell appended outside of a row.");
        _currentColumn++;
        var column = _currentColumn;
        if (_measureRow && value is not null) Measure(column, value, EffectiveStyleId(value, styleId));

        if (value is null)
        {
            WriteCellOpen(column, styleId, type: null);
            _xml.WriteEndElement();
            return;
        }
        if (value is string s)         { WriteStringCell(column, s, styleId); return; }
        if (value is bool b)           { WriteCellOpen(column, styleId, "b"); WriteValue(b ? "1" : "0"); _xml.WriteEndElement(); return; }
        if (value is DateTime dt)
        {
            var hasTime = dt.TimeOfDay != TimeSpan.Zero;
            var sid = styleId ?? (hasTime ? _state.DefaultDateTimeStyleId : _state.DefaultDateStyleId);
            WriteCellOpen(column, sid, null);
            WriteValue(dt.ToOADate().ToString("R", CultureInfo.InvariantCulture));
            _xml.WriteEndElement();
            return;
        }
        if (value is DateOnly d)
        {
            var sid = styleId ?? _state.DefaultDateStyleId;
            WriteCellOpen(column, sid, null);
            WriteValue(d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString("R", CultureInfo.InvariantCulture));
            _xml.WriteEndElement();
            return;
        }
        if (value is DateTimeOffset dto)
        {
            var hasTime = dto.TimeOfDay != TimeSpan.Zero;
            var sid = styleId ?? (hasTime ? _state.DefaultDateTimeStyleId : _state.DefaultDateStyleId);
            WriteCellOpen(column, sid, null);
            WriteValue(dto.DateTime.ToOADate().ToString("R", CultureInfo.InvariantCulture));
            _xml.WriteEndElement();
            return;
        }
        if (value is TimeSpan ts)
        {
            var sid = styleId ?? _state.DefaultTimeSpanStyleId;
            WriteCellOpen(column, sid, null);
            WriteValue(ts.TotalDays.ToString("R", CultureInfo.InvariantCulture));
            _xml.WriteEndElement();
            return;
        }
        if (value is double dbl)    { WriteNumericCell(column, dbl.ToString("R", CultureInfo.InvariantCulture), styleId); return; }
        if (value is float fl)      { WriteNumericCell(column, ((double)fl).ToString("R", CultureInfo.InvariantCulture), styleId); return; }
        if (value is decimal dec)   { WriteNumericCell(column, ((double)dec).ToString("R", CultureInfo.InvariantCulture), styleId); return; }
        if (value is int i)         { WriteNumericCell(column, i.ToString(CultureInfo.InvariantCulture), styleId); return; }
        if (value is long l)        { WriteNumericCell(column, l.ToString(CultureInfo.InvariantCulture), styleId); return; }
        if (value is short sh)      { WriteNumericCell(column, sh.ToString(CultureInfo.InvariantCulture), styleId); return; }
        if (value is byte by)       { WriteNumericCell(column, by.ToString(CultureInfo.InvariantCulture), styleId); return; }
        if (value is uint ui)       { WriteNumericCell(column, ui.ToString(CultureInfo.InvariantCulture), styleId); return; }
        if (value is ulong ul)      { WriteNumericCell(column, ul.ToString(CultureInfo.InvariantCulture), styleId); return; }
        if (value is sbyte sb)      { WriteNumericCell(column, sb.ToString(CultureInfo.InvariantCulture), styleId); return; }
        if (value is ushort us)     { WriteNumericCell(column, us.ToString(CultureInfo.InvariantCulture), styleId); return; }

        WriteStringCell(column, value.ToString() ?? string.Empty, styleId);
    }

    /// <summary>The style id a value is written with (type defaults for dates and times).</summary>
    private int? EffectiveStyleId(object value, int? styleId) => styleId ?? value switch
    {
        DateTime dt => dt.TimeOfDay != TimeSpan.Zero ? _state.DefaultDateTimeStyleId : _state.DefaultDateStyleId,
        DateTimeOffset dto => dto.TimeOfDay != TimeSpan.Zero ? _state.DefaultDateTimeStyleId : _state.DefaultDateStyleId,
        DateOnly => _state.DefaultDateStyleId,
        TimeSpan => _state.DefaultTimeSpanStyleId,
        _ => null,
    };

    private void Measure(int column, object value, int? styleId)
    {
        CellStyle? style = styleId is int id && _state.Styles.TryGetStyle(id, out var st) ? st : null;
        if (value is DateOnly d) value = d.ToDateTime(TimeOnly.MinValue);
        _autoFit!.Observe(_rowsWritten, column, value, style);
    }

    /// <summary>Byte offset in the temp file where XlsxWriter inserts <see cref="ColsXml"/>; -1 when not auto-fitting.</summary>
    internal long ColsInsertOffset => _colsInsertOffset;

    /// <summary>The &lt;cols&gt; element computed at finalization (auto-fit mode), or null.</summary>
    internal byte[]? ColsXml => _colsXml;

    /// <summary>Auto-fit mode: combine explicit widths and fitted widths into the &lt;cols&gt; bytes.</summary>
    private void BuildAutoFitCols()
    {
        var headers = new List<(int Row, int C1, int C2)>();
        if (_autoFilterRange is not null && ColumnAutoFitter.TryParseRange(_autoFilterRange, out var af))
            headers.Add((af.R1, af.C1, af.C2));
        foreach (var t in _tables)
            if (ColumnAutoFitter.TryParseRange(t.Range, out var tr)) headers.Add((tr.R1, tr.C1, tr.C2));

        var fitted = _autoFit!.ComputeWidths(headers);
        var cols = new List<(int Min, int Max, double Width)>(_columnWidths);
        foreach (var kv in fitted)
        {
            bool explicitWidth = false;
            foreach (var (min, max, _) in _columnWidths)
                if (kv.Key >= min && kv.Key <= max) { explicitWidth = true; break; }
            if (!explicitWidth) cols.Add((kv.Key, kv.Key, kv.Value));
        }
        cols.Sort((a, b) => a.Min.CompareTo(b.Min));

        var sb = new StringBuilder();
        if (cols.Count > 0)
        {
            sb.Append("<cols>");
            foreach (var (min, max, width) in cols)
            {
                sb.Append("<col min=\"").Append(min.ToString(CultureInfo.InvariantCulture))
                  .Append("\" max=\"").Append(max.ToString(CultureInfo.InvariantCulture))
                  .Append("\" width=\"").Append(width.ToString(CultureInfo.InvariantCulture))
                  .Append("\" customWidth=\"1\" />");
            }
            sb.Append("</cols>");
        }
        _colsXml = new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private void WriteCellOpen(int column, int? styleId, string? type)
    {
        _xml.WriteStartElement("c", SsNs);
        _xml.WriteAttributeString("r", CellAddress.ToA1(_rowsWritten, column));
        if (styleId is int sid && sid != 0)
            _xml.WriteAttributeString("s", sid.ToString(CultureInfo.InvariantCulture));
        if (type is not null)
            _xml.WriteAttributeString("t", type);
    }

    private void WriteValue(string text)
    {
        _xml.WriteStartElement("v", SsNs);
        _xml.WriteString(text);
        _xml.WriteEndElement();
    }

    private void WriteNumericCell(int column, string numericText, int? styleId)
    {
        WriteCellOpen(column, styleId, null);
        WriteValue(numericText);
        _xml.WriteEndElement();
    }

    private void WriteStringCell(int column, string value, int? styleId)
    {
        if (_state.Options.UseInlineStrings)
        {
            WriteCellOpen(column, styleId, "inlineStr");
            _xml.WriteStartElement("is", SsNs);
            _xml.WriteStartElement("t", SsNs);
            if (HasSignificantWhitespace(value))
                _xml.WriteAttributeString("xml", "space", null, "preserve");
            _xml.WriteString(value);
            _xml.WriteEndElement();
            _xml.WriteEndElement();
            _xml.WriteEndElement();
        }
        else
        {
            var ssId = _state.SharedStrings.GetOrAdd(value);
            var placeholder = SharedStringPlaceholderPrefix
                + ssId.ToString(CultureInfo.InvariantCulture)
                + SharedStringPlaceholderSuffix;
            WriteCellOpen(column, styleId, "s");
            WriteValue(placeholder);
            _xml.WriteEndElement();
        }
    }

    private void WriteFormulaCell(int column, string formula, int? styleId, object? cachedValue, string? arrayRange)
    {
        var (type, text) = CachedValueText(cachedValue);
        WriteCellOpen(column, styleId, type);
        _xml.WriteStartElement("f", SsNs);
        if (arrayRange is not null)
        {
            _xml.WriteAttributeString("t", "array");
            _xml.WriteAttributeString("ref", arrayRange);
        }
        _xml.WriteString(formula);
        _xml.WriteEndElement();
        if (text is not null) WriteValue(text);
        _xml.WriteEndElement();
    }

    /// <summary>Excel's error literals: a cached string equal to one of these is written as an error (t="e").</summary>
    private static readonly HashSet<string> ErrorLiterals = new(StringComparer.Ordinal)
    {
        "#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A", "#GETTING_DATA",
        "#SPILL!", "#CALC!", "#FIELD!", "#BLOCKED!", "#CONNECT!", "#BUSY!", "#UNKNOWN!",
    };

    /// <summary>The cell type and &lt;v&gt; text for a formula's cached result (null text = none).</summary>
    private static (string? Type, string? Text) CachedValueText(object? v)
    {
        switch (v)
        {
            case null: return (null, null);
            case string s: return (ErrorLiterals.Contains(s) ? "e" : "str", s);
            case bool b: return ("b", b ? "1" : "0");
            case DateTime dt: return (null, dt.ToOADate().ToString("R", CultureInfo.InvariantCulture));
            case DateOnly d: return (null, d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString("R", CultureInfo.InvariantCulture));
            case DateTimeOffset dto: return (null, dto.DateTime.ToOADate().ToString("R", CultureInfo.InvariantCulture));
            case TimeSpan ts: return (null, ts.TotalDays.ToString("R", CultureInfo.InvariantCulture));
            case double dbl: return (null, dbl.ToString("R", CultureInfo.InvariantCulture));
            case float fl: return (null, ((double)fl).ToString("R", CultureInfo.InvariantCulture));
            case decimal dec: return (null, ((double)dec).ToString("R", CultureInfo.InvariantCulture));
            case int or long or short or byte or uint or ulong or sbyte or ushort:
                return (null, Convert.ToString(v, CultureInfo.InvariantCulture));
            default: return ("str", v.ToString() ?? string.Empty);
        }
    }

    private static bool HasSignificantWhitespace(string s)
    {
        if (s.Length == 0) return false;
        return char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[s.Length - 1]);
    }

    // ---- Internal async row machinery ----------------------------------------------
    //
    // The XmlWriter writes into a 64KB buffered FileStream opened with FileOptions.Asynchronous.
    // The XmlWriter's per-call writes hit that in-memory buffer; the buffer drains to disk
    // asynchronously when it fills. Calling the XmlWriter's *Async methods individually doesn't
    // add value here — they just defer the synchronous buffer copy through a Task wrapper.
    // So all async row APIs route through the sync implementations and return CompletedTask.

    private Task EnsureWorksheetOpenAsync()      { EnsureWorksheetOpen(); return Task.CompletedTask; }
    private Task BeginRowAsync()                 { BeginRow();             return Task.CompletedTask; }
    private Task EndRowAsync()                   { EndRow();               return Task.CompletedTask; }
    private Task AppendCellAsync(object? value, int? styleId)
    {
        AppendCell(value, styleId);
        return Task.CompletedTask;
    }

    // ---- Finalization --------------------------------------------------------------

    /// <summary>
    /// Closes the sheet XML structure and flushes the temp file. Called by XlsxWriter at save
    /// time and by Dispose. Idempotent.
    /// </summary>
    internal void FinalizeSheet()
    {
        if (_finalized) return;
        if (_rowInProgress) EndRow();

        // If the user never wrote a row, we still need a valid empty worksheet.
        if (!_worksheetOpened) EnsureWorksheetOpen();
        if (_autoFit is not null) BuildAutoFitCols();
        _sheetPrXml = _pageSetup.FitToPage
            ? new UTF8Encoding(false).GetBytes("<sheetPr><pageSetUpPr fitToPage=\"1\" /></sheetPr>")
            : null;

        _xml.WriteEndElement(); // </sheetData>

        // sheetProtection — must precede autoFilter, mergeCells, conditionalFormatting,
        // dataValidations, hyperlinks per OOXML schema.
        if (_sheetProtected)
        {
            _xml.WriteStartElement("sheetProtection", SsNs);
            _xml.WriteAttributeString("algorithmName", "SHA-512");
            _xml.WriteAttributeString("hashValue", _protectionHashB64);
            _xml.WriteAttributeString("saltValue", _protectionSaltB64);
            _xml.WriteAttributeString("spinCount", _protectionSpinCount.ToString(CultureInfo.InvariantCulture));
            _xml.WriteAttributeString("sheet", "1");

            var o = _protectionOptions!;
            // Excel inverts most flags: attribute = "1" means RESTRICTED.
            // "selectLockedCells" / "selectUnlockedCells" — "1" means user CANNOT select those cells.
            if (!o.AllowSelectLockedCells)   _xml.WriteAttributeString("selectLockedCells", "1");
            if (!o.AllowSelectUnlockedCells) _xml.WriteAttributeString("selectUnlockedCells", "1");
            // Format/insert/delete/sort/etc — attribute "1" means CAN do it (these are positive flags).
            if (o.AllowFormatCells)      _xml.WriteAttributeString("formatCells", "0");
            if (o.AllowFormatColumns)    _xml.WriteAttributeString("formatColumns", "0");
            if (o.AllowFormatRows)       _xml.WriteAttributeString("formatRows", "0");
            if (o.AllowInsertColumns)    _xml.WriteAttributeString("insertColumns", "0");
            if (o.AllowInsertRows)       _xml.WriteAttributeString("insertRows", "0");
            if (o.AllowInsertHyperlinks) _xml.WriteAttributeString("insertHyperlinks", "0");
            if (o.AllowDeleteColumns)    _xml.WriteAttributeString("deleteColumns", "0");
            if (o.AllowDeleteRows)       _xml.WriteAttributeString("deleteRows", "0");
            if (o.AllowSort)             _xml.WriteAttributeString("sort", "0");
            if (o.AllowAutoFilter)       _xml.WriteAttributeString("autoFilter", "0");
            if (o.AllowPivotTables)      _xml.WriteAttributeString("pivotTables", "0");
            if (o.AllowEditObjects)      _xml.WriteAttributeString("objects", "0");
            if (o.AllowEditScenarios)    _xml.WriteAttributeString("scenarios", "0");

            _xml.WriteEndElement();
        }

        // After-sheetData elements in OOXML schema order:
        //   autoFilter, mergeCells, conditionalFormatting, dataValidations, hyperlinks, print
        //   settings, legacyDrawing (for comments), tableParts
        if (_autoFilterRange is not null)
        {
            _xml.WriteStartElement("autoFilter", SsNs);
            _xml.WriteAttributeString("ref", _autoFilterRange);
            _xml.WriteEndElement();
        }

        if (_mergeRanges.Count > 0)
        {
            _xml.WriteStartElement("mergeCells", SsNs);
            _xml.WriteAttributeString("count", _mergeRanges.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var range in _mergeRanges)
            {
                _xml.WriteStartElement("mergeCell", SsNs);
                _xml.WriteAttributeString("ref", range);
                _xml.WriteEndElement();
            }
            _xml.WriteEndElement();
        }

        // Conditional formatting — one <conditionalFormatting> element per range we register.
        // CT_Worksheet order: mergeCells, conditionalFormatting, dataValidations, hyperlinks,
        // printOptions, pageMargins, pageSetup, headerFooter, rowBreaks, colBreaks, ...,
        // legacyDrawing, tableParts.
        if (_conditionalFormats.Count > 0)
        {
            foreach (var (range, rule) in _conditionalFormats)
            {
                _xml.WriteStartElement("conditionalFormatting", SsNs);
                _xml.WriteAttributeString("sqref", range);
                WriteConditionalFormatRule(rule);
                _xml.WriteEndElement();
            }
        }

        // Data validations.
        if (_dataValidations.Count > 0)
        {
            _xml.WriteStartElement("dataValidations", SsNs);
            _xml.WriteAttributeString("count",
                _dataValidations.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var (range, v) in _dataValidations)
                WriteDataValidation(range, v);
            _xml.WriteEndElement();
        }

        if (_hyperlinks.Count > 0)
        {
            _xml.WriteStartElement("hyperlinks", SsNs);
            // Each hyperlink will get an rId allocated by the parent XlsxWriter at save time;
            // for now we generate the IDs ourselves following the convention "rIdHL{N}" and
            // pair them with their URLs for the writer to materialize as relationships.
            int n = 1;
            foreach (var hl in _hyperlinks)
            {
                _xml.WriteStartElement("hyperlink", SsNs);
                _xml.WriteAttributeString("ref", hl.CellAddress);
                _xml.WriteAttributeString("id", RelNs, $"rIdHL{n}");
                if (hl.Tooltip is not null)
                    _xml.WriteAttributeString("tooltip", hl.Tooltip);
                _xml.WriteEndElement();
                n++;
            }
            _xml.WriteEndElement();
        }

        WritePrintSettings();

        // legacyDrawing — links the sheet to the VML drawing that defines comment shapes.
        if (_comments.Count > 0)
        {
            _xml.WriteStartElement("legacyDrawing", SsNs);
            _xml.WriteAttributeString("id", RelNs, "rIdVml");
            _xml.WriteEndElement();
        }

        // tableParts — references to the table parts the parent writer will create.
        if (_tables.Count > 0)
        {
            _xml.WriteStartElement("tableParts", SsNs);
            _xml.WriteAttributeString("count", _tables.Count.ToString(CultureInfo.InvariantCulture));
            int n = 1;
            foreach (var _ in _tables)
            {
                _xml.WriteStartElement("tablePart", SsNs);
                _xml.WriteAttributeString("id", RelNs, $"rIdTbl{n}");
                _xml.WriteEndElement();
                n++;
            }
            _xml.WriteEndElement();
        }

        _xml.WriteEndElement(); // </worksheet>
        _xml.WriteEndDocument();
        _xml.Flush();
        _xml.Dispose();
        _tempStream.Flush();
        _tempStream.Dispose();

        _finalized = true;
    }

    /// <summary>printOptions, pageMargins, pageSetup, headerFooter, rowBreaks, colBreaks — each only when set.</summary>
    private void WritePrintSettings()
    {
        var p = _pageSetup;
        string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        if (p.HasPrintOptions)
        {
            _xml.WriteStartElement("printOptions", SsNs);
            if (p.CenterHorizontally) _xml.WriteAttributeString("horizontalCentered", "1");
            if (p.CenterVertically) _xml.WriteAttributeString("verticalCentered", "1");
            if (p.PrintHeadings) _xml.WriteAttributeString("headings", "1");
            if (p.PrintGridlines) _xml.WriteAttributeString("gridLines", "1");
            _xml.WriteEndElement();
        }

        if (p.Margins.IsDefined)
        {
            var m = p.Margins;
            _xml.WriteStartElement("pageMargins", SsNs);
            _xml.WriteAttributeString("left", D(m.Left));
            _xml.WriteAttributeString("right", D(m.Right));
            _xml.WriteAttributeString("top", D(m.Top));
            _xml.WriteAttributeString("bottom", D(m.Bottom));
            _xml.WriteAttributeString("header", D(m.Header));
            _xml.WriteAttributeString("footer", D(m.Footer));
            _xml.WriteEndElement();
        }

        if (p.IsDefined)
        {
            _xml.WriteStartElement("pageSetup", SsNs);
            if (p.PaperSizeCode is int ps) _xml.WriteAttributeString("paperSize", I(ps));
            if (p.Scale != 100) _xml.WriteAttributeString("scale", I(p.Scale));
            if (p.FirstPageNumber != 1) _xml.WriteAttributeString("firstPageNumber", I(p.FirstPageNumber));
            if (p.FitToWidth != 1) _xml.WriteAttributeString("fitToWidth", I(p.FitToWidth));
            if (p.FitToHeight != 1) _xml.WriteAttributeString("fitToHeight", I(p.FitToHeight));
            if (p.PageOrder == PageOrder.OverThenDown) _xml.WriteAttributeString("pageOrder", "overThenDown");
            if (p.Orientation != PageOrientation.Default)
                _xml.WriteAttributeString("orientation", p.Orientation == PageOrientation.Landscape ? "landscape" : "portrait");
            if (p.BlackAndWhite) _xml.WriteAttributeString("blackAndWhite", "1");
            if (p.Draft) _xml.WriteAttributeString("draft", "1");
            if (p.CellComments != PrintCellComments.None)
                _xml.WriteAttributeString("cellComments", p.CellComments == PrintCellComments.AtEnd ? "atEnd" : "asDisplayed");
            if (p.UseFirstPageNumber) _xml.WriteAttributeString("useFirstPageNumber", "1");
            if (p.Errors != PrintErrors.Displayed)
                _xml.WriteAttributeString("errors", p.Errors switch { PrintErrors.Blank => "blank", PrintErrors.Dash => "dash", _ => "NA" });
            if (p.HorizontalDpi is int hd) _xml.WriteAttributeString("horizontalDpi", I(hd));
            if (p.VerticalDpi is int vd) _xml.WriteAttributeString("verticalDpi", I(vd));
            if (p.Copies != 1) _xml.WriteAttributeString("copies", I(p.Copies));
            _xml.WriteEndElement();
        }

        var h = p.HeaderFooter;
        if (h.HasContent)
        {
            _xml.WriteStartElement("headerFooter", SsNs);
            if (h.DifferentOddEven) _xml.WriteAttributeString("differentOddEven", "1");
            if (h.DifferentFirst) _xml.WriteAttributeString("differentFirst", "1");
            if (!h.ScaleWithDocument) _xml.WriteAttributeString("scaleWithDoc", "0");
            if (!h.AlignWithMargins) _xml.WriteAttributeString("alignWithMargins", "0");
            void Text(string element, string? text)
            {
                if (text is null) return;
                _xml.WriteStartElement(element, SsNs);
                _xml.WriteString(text);
                _xml.WriteEndElement();
            }
            Text("oddHeader", h.OddHeader);
            Text("oddFooter", h.OddFooter);
            Text("evenHeader", h.EvenHeader);
            Text("evenFooter", h.EvenFooter);
            Text("firstHeader", h.FirstHeader);
            Text("firstFooter", h.FirstFooter);
            _xml.WriteEndElement();
        }

        WriteBreaks("rowBreaks", p.RowBreaks);
        WriteBreaks("colBreaks", p.ColumnBreaks);

        void WriteBreaks(string element, IReadOnlyList<PageBreak> breaks)
        {
            if (breaks.Count == 0) return;
            int manual = 0;
            foreach (var b in breaks) if (b.Manual) manual++;
            _xml.WriteStartElement(element, SsNs);
            _xml.WriteAttributeString("count", I(breaks.Count));
            _xml.WriteAttributeString("manualBreakCount", I(manual));
            foreach (var b in breaks)
            {
                _xml.WriteStartElement("brk", SsNs);
                _xml.WriteAttributeString("id", I(b.Id));
                if (b.Min != 0) _xml.WriteAttributeString("min", I(b.Min));
                if (b.Max != 0) _xml.WriteAttributeString("max", I(b.Max));
                if (b.Manual) _xml.WriteAttributeString("man", "1");
                _xml.WriteEndElement();
            }
            _xml.WriteEndElement();
        }
    }

    /// <summary>Byte offset just after the &lt;worksheet&gt; start tag, where <see cref="SheetPrXml"/> is inserted.</summary>
    internal long PrologueInsertOffset => _prologueInsertOffset;

    /// <summary>The &lt;sheetPr&gt; element (fit-to-page), or null when not needed.</summary>
    internal byte[]? SheetPrXml => _sheetPrXml;

    // ---- Internal accessors used by XlsxWriter at save time ------------------------

    internal string TempPath => _tempPath;
    internal int SheetIndex => _sheetIndex;
    internal string PartUri => $"/xl/worksheets/sheet{_sheetIndex.ToString(CultureInfo.InvariantCulture)}.xml";

    internal IReadOnlyList<SheetHyperlink> Hyperlinks => _hyperlinks;
    internal IReadOnlyList<SheetComment> Comments => _comments;
    internal IReadOnlyList<SheetTable> Tables => _tables;

    private void WriteDataValidation(string range, DataValidation v)
    {
        _xml.WriteStartElement("dataValidation", SsNs);
        _xml.WriteAttributeString("type", v.Type);
        if (v.Operator is not null)
            _xml.WriteAttributeString("operator", v.Operator);
        _xml.WriteAttributeString("allowBlank", v.AllowBlank ? "1" : "0");
        if (v.Type == "list")
            _xml.WriteAttributeString("showDropDown", v.ShowDropdown ? "0" : "1"); // OOXML inverted
        if (v.ErrorTitle is not null || v.ErrorMessage is not null)
        {
            _xml.WriteAttributeString("showErrorMessage", "1");
            if (v.ErrorTitle is not null) _xml.WriteAttributeString("errorTitle", v.ErrorTitle);
            if (v.ErrorMessage is not null) _xml.WriteAttributeString("error", v.ErrorMessage);
        }
        if (v.PromptTitle is not null || v.PromptMessage is not null)
        {
            _xml.WriteAttributeString("showInputMessage", "1");
            if (v.PromptTitle is not null) _xml.WriteAttributeString("promptTitle", v.PromptTitle);
            if (v.PromptMessage is not null) _xml.WriteAttributeString("prompt", v.PromptMessage);
        }
        _xml.WriteAttributeString("sqref", range);

        if (v.Formula1 is not null)
        {
            _xml.WriteStartElement("formula1", SsNs);
            _xml.WriteString(v.Formula1);
            _xml.WriteEndElement();
        }
        if (v.Formula2 is not null)
        {
            _xml.WriteStartElement("formula2", SsNs);
            _xml.WriteString(v.Formula2);
            _xml.WriteEndElement();
        }

        _xml.WriteEndElement(); // </dataValidation>
    }

    private void WriteConditionalFormatRule(ConditionalFormatRule rule)
    {
        _xml.WriteStartElement("cfRule", SsNs);
        if (rule is ColorScaleRule cs)
        {
            _xml.WriteAttributeString("type", "colorScale");
            _xml.WriteAttributeString("priority", "1");
            _xml.WriteStartElement("colorScale", SsNs);
            _xml.WriteStartElement("cfvo", SsNs); _xml.WriteAttributeString("type", "min"); _xml.WriteEndElement();
            _xml.WriteStartElement("cfvo", SsNs); _xml.WriteAttributeString("type", "percentile"); _xml.WriteAttributeString("val", "50"); _xml.WriteEndElement();
            _xml.WriteStartElement("cfvo", SsNs); _xml.WriteAttributeString("type", "max"); _xml.WriteEndElement();
            WriteColor(cs.MinColor); WriteColor(cs.MidColor); WriteColor(cs.MaxColor);
            _xml.WriteEndElement();
        }
        else if (rule is DataBarRule db)
        {
            _xml.WriteAttributeString("type", "dataBar");
            _xml.WriteAttributeString("priority", "1");
            _xml.WriteStartElement("dataBar", SsNs);
            _xml.WriteStartElement("cfvo", SsNs); _xml.WriteAttributeString("type", "min"); _xml.WriteEndElement();
            _xml.WriteStartElement("cfvo", SsNs); _xml.WriteAttributeString("type", "max"); _xml.WriteEndElement();
            WriteColor(db.Color);
            _xml.WriteEndElement();
        }
        else if (rule is CellIsRule ci)
        {
            _xml.WriteAttributeString("type", "cellIs");
            _xml.WriteAttributeString("priority", "1");
            _xml.WriteAttributeString("operator", OpToOoxml(ci.Operator));
            // The OOXML schema requires cellIs rules to reference a dxf (differential
            // formatting) record, which this library does not currently emit — dxfs support
            // is out of scope. To stay schema-valid we emit dxfId="0" (the default record),
            // which means the rule is structurally correct and evaluates, but the supplied
            // CellStyle will NOT render visibly via this rule in Excel. This limitation is
            // documented on ConditionalFormat.CellIs and in the README.
            var styleId = _state.Styles.GetCellXfId(ci.Style);
            _xml.WriteAttributeString("dxfId", "0");
            _xml.WriteStartElement("formula", SsNs);
            _xml.WriteString(ci.Formula1);
            _xml.WriteEndElement();
            if (ci.Formula2 is not null)
            {
                _xml.WriteStartElement("formula", SsNs);
                _xml.WriteString(ci.Formula2);
                _xml.WriteEndElement();
            }
            _ = styleId; // suppressed — see comment above
        }
        _xml.WriteEndElement(); // </cfRule>
    }

    private void WriteColor(string hexNoHash)
    {
        _xml.WriteStartElement("color", SsNs);
        _xml.WriteAttributeString("rgb", "FF" + hexNoHash);
        _xml.WriteEndElement();
    }

    private static string OpToOoxml(ComparisonOp op) => op switch
    {
        ComparisonOp.Equal              => "equal",
        ComparisonOp.NotEqual           => "notEqual",
        ComparisonOp.GreaterThan        => "greaterThan",
        ComparisonOp.GreaterThanOrEqual => "greaterThanOrEqual",
        ComparisonOp.LessThan           => "lessThan",
        ComparisonOp.LessThanOrEqual    => "lessThanOrEqual",
        ComparisonOp.Between            => "between",
        ComparisonOp.NotBetween         => "notBetween",
        _ => "equal",
    };

    // ---- Guards / Disposal ---------------------------------------------------------

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void EnsureShapeMutable()
    {
        if (_worksheetOpened)
            throw new InvalidOperationException(
                "Sheet shape (column widths, freeze panes) must be set before the first row is written.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { FinalizeSheet(); } catch { /* swallow during Dispose */ }
        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
