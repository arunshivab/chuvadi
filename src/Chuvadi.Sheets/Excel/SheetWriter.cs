using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

    // "Sheet shape" — the prologue (sheetPr, sheetViews, sheetFormatPr, cols) is built at
    // save and inserted at the offset recorded when the worksheet opened, so all of it can be
    // set at any time until save.
    private readonly List<ColumnEntry> _columns = new();
    private readonly SheetView _view = new();
    private readonly SheetFormat _format = new();
    private string? _tabColor;
    private int _maxRowOutline;

    // Auto-fit: cells are measured as written; widths go into <cols> at save.
    private ColumnAutoFitter? _autoFit;
    private bool _measureRow;

    // "After sheetData" features — emitted at sheet finalization.
    private readonly List<string> _mergeRanges = new();
    private string? _autoFilterRange;
    private readonly List<SheetHyperlink> _hyperlinks = new();
    private readonly List<SheetComment> _comments = new();
    private readonly List<SheetTable> _tables = new();
    private readonly List<(string Range, DataValidation V)> _dataValidations = new();
    private readonly List<ConditionalFormatting> _conditionalFormats = new();

    // Print settings — written at finalization.
    private readonly PageSetup _pageSetup = new();
    private long _prologueInsertOffset = -1;
    private byte[]? _prologueXml;

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

    // ---- Sheet-shape API (any time until save) ---------------------------------------

    /// <summary>
    /// Sets the width (in Excel column-width units) for one or more contiguous columns. Can be
    /// called at any time until the workbook is saved.
    /// </summary>
    public SheetWriter SetColumnWidth(int columnFrom, int columnTo, double width)
    {
        EnsureNotDisposed();
        if (columnFrom < 1 || columnTo < columnFrom)
            throw new ArgumentException("Column range must be 1-based and non-empty.");
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _columns.Add(new ColumnEntry(columnFrom, columnTo, width));
        return this;
    }

    /// <summary>Convenience overload for a single column.</summary>
    public SheetWriter SetColumnWidth(int column, double width)
        => SetColumnWidth(column, column, width);

    /// <summary>
    /// Sets width, visibility, outline level, collapsed state, best-fit flag and default style
    /// for a span of columns (1-based, inclusive). Spans must not overlap. A span with a width
    /// wins over auto-fit for its columns. Can be called at any time until save.
    /// </summary>
    public SheetWriter SetColumns(int columnFrom, int columnTo, ColumnFormat format)
    {
        EnsureNotDisposed();
        if (columnFrom < 1 || columnTo < columnFrom || columnTo > 16384)
            throw new ArgumentException("Column range must be 1-based, non-empty and within 16384.");
        if (format is null) throw new ArgumentNullException(nameof(format));
        _columns.Add(new ColumnEntry(columnFrom, columnTo, format.Width ?? 0)
        {
            Hidden = format.Hidden,
            OutlineLevel = format.OutlineLevel,
            Collapsed = format.Collapsed,
            BestFit = format.BestFit,
            StyleId = format.Style is CellStyle st ? _state.Styles.GetCellXfId(st) : null,
        });
        return this;
    }

    /// <summary>
    /// Sizes columns to their content: every cell written afterwards is measured as displayed
    /// (number format, font, size, bold) and the widths are written when the sheet is saved.
    /// Widths set with <see cref="SetColumnWidth(int, double)"/> or <see cref="SetColumns"/> win
    /// over auto-fit. Wrapped, merged and rotated cells are not measured; formula cells are
    /// measured by their cached result (<see cref="RowBuilder.Formula(string, object?)"/>) when
    /// given. Must be called before any rows are written (so every row is measured). See
    /// <see cref="AutoFitOptions"/>.
    /// </summary>
    public SheetWriter AutoFitColumns(AutoFitOptions? options = null)
    {
        EnsureNotDisposed();
        if (_worksheetOpened)
            throw new InvalidOperationException("AutoFitColumns must be called before the first row is written.");
        _autoFit = new ColumnAutoFitter(options);
        foreach (var m in _mergeRanges) _autoFit.AddMerge(m);
        return this;
    }

    /// <summary>Freezes the first <paramref name="rows"/> rows (keeps frozen columns). Pass 0 to clear. Same as <c>View.Freeze</c>.</summary>
    public SheetWriter FreezeRows(int rows)
    {
        EnsureNotDisposed();
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        _view.Freeze(rows, _view.FrozenColumns);
        return this;
    }

    /// <summary>Freezes the first <paramref name="cols"/> columns (keeps frozen rows). Pass 0 to clear. Same as <c>View.Freeze</c>.</summary>
    public SheetWriter FreezeColumns(int cols)
    {
        EnsureNotDisposed();
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        _view.Freeze(_view.FrozenRows, cols);
        return this;
    }

    /// <summary>How the sheet shows in Excel: gridlines, headings, zeros, zoom, view mode, frozen or split panes, selection. Settable until save.</summary>
    public SheetView View => _view;

    /// <summary>Sheet-wide row and column defaults (&lt;sheetFormatPr&gt;) and outline summary positions. Settable until save.</summary>
    public SheetFormat Format => _format;

    /// <summary>The sheet tab colour, "RRGGBB" (a leading '#' or an alpha byte is accepted), or null.</summary>
    public string? TabColor
    {
        get => _tabColor;
        set => _tabColor = value is null ? null : CellStyleBuilder.NormalizeHex(value)
            ?? throw new ArgumentException($"'{value}' is not an RGB colour (\"RRGGBB\").", nameof(value));
    }

    /// <summary>Visible (default), hidden or very hidden. Saving needs at least one visible sheet.</summary>
    public SheetVisibility Visibility { get; set; }

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

    /// <summary>
    /// Writes a row with row settings (height, hidden, outline level, collapsed, row style,
    /// thick borders) and the given values. With no values, writes an empty row that carries
    /// only its settings (e.g. a hidden or tall spacer row).
    /// </summary>
    public SheetWriter WriteRow(RowFormat format, params object?[] values)
    {
        if (format is null) throw new ArgumentNullException(nameof(format));
        EnsureWorksheetOpen();
        BeginRow(format);
        foreach (var v in values) AppendCell(v, styleId: null);
        EndRow();
        return this;
    }

    /// <summary>Writes a row with row settings, building its cells with a <see cref="RowBuilder"/>.</summary>
    public SheetWriter WriteRow(RowFormat format, Action<RowBuilder> build)
    {
        if (format is null) throw new ArgumentNullException(nameof(format));
        if (build is null) throw new ArgumentNullException(nameof(build));
        EnsureWorksheetOpen();
        BeginRow(format);
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

    /// <summary>
    /// Adds a hyperlink over a cell or range that is written (or will be written) separately,
    /// e.g. to link a range or to link within the workbook: an external
    /// <see cref="Hyperlink.Target"/> becomes a relationship, a <see cref="Hyperlink.Location"/>
    /// is stored on the link, and both may be given. The cell text and style are not changed
    /// (<see cref="RowBuilder.Hyperlink(string, string, string?)"/> writes a linked cell in one
    /// step). Since 1.7.0.
    /// </summary>
    public SheetWriter AddHyperlink(Hyperlink hyperlink)
    {
        EnsureNotDisposed();
        if (hyperlink is null) throw new ArgumentNullException(nameof(hyperlink));
        _hyperlinks.Add(new SheetHyperlink(hyperlink.Reference, hyperlink.Target, hyperlink.Tooltip, hyperlink.Location, hyperlink.Display));
        return this;
    }

    /// <summary>
    /// Adds a comment (note) to a cell that is written (or will be written) separately, with
    /// its text runs, box anchor and visibility when given. Since 1.7.0.
    /// (<see cref="RowBuilder.Comment(object?, string, string)"/> writes a cell and its comment in one step.)
    /// </summary>
    public SheetWriter AddComment(CellComment comment)
    {
        EnsureNotDisposed();
        if (comment is null) throw new ArgumentNullException(nameof(comment));
        _comments.Add(new SheetComment(CellAddress.ToA1(comment.Row, comment.Column), comment.Author, comment.Text, comment.RichText, comment.Anchor, comment.Visible));
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

    /// <summary>Adds a conditional formatting rule over the given range (or space-separated ranges).</summary>
    public SheetWriter AddConditionalFormat(string range, ConditionalFormatRule rule)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        _conditionalFormats.Add(new ConditionalFormatting(SheetExtrasParser.SplitRanges(range), new[] { (ConditionalRule)rule }));
        return this;
    }

    /// <summary>Adds a conditional formatting block (several ranges and rules), e.g. one loaded from a file.</summary>
    public SheetWriter AddConditionalFormatting(ConditionalFormatting formatting)
    {
        EnsureNotDisposed();
        _conditionalFormats.Add(formatting ?? throw new ArgumentNullException(nameof(formatting)));
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

    private void BeginRow(RowFormat? format = null)
    {
        if (_rowInProgress) throw new InvalidOperationException("A row is already in progress.");
        _rowsWritten++;
        _currentColumn = 0;
        _rowInProgress = true;
        _measureRow = _autoFit?.BeginRow() ?? false;

        _xml.WriteStartElement("row", SsNs);
        _xml.WriteAttributeString("r", _rowsWritten.ToString(CultureInfo.InvariantCulture));
        if (format is not null && format.HasContent)
        {
            if (format.Style is CellStyle st)
            {
                var sid = _state.Styles.GetCellXfId(st);
                _xml.WriteAttributeString("s", sid.ToString(CultureInfo.InvariantCulture));
                _xml.WriteAttributeString("customFormat", "1");
            }
            if (format.Height is double h)
            {
                _xml.WriteAttributeString("ht", h.ToString("R", CultureInfo.InvariantCulture));
                if (format.CustomHeight) _xml.WriteAttributeString("customHeight", "1");
            }
            if (format.Hidden) _xml.WriteAttributeString("hidden", "1");
            if (format.OutlineLevel > 0)
            {
                _xml.WriteAttributeString("outlineLevel", format.OutlineLevel.ToString(CultureInfo.InvariantCulture));
                if (format.OutlineLevel > _maxRowOutline) _maxRowOutline = format.OutlineLevel;
            }
            if (format.Collapsed) _xml.WriteAttributeString("collapsed", "1");
            if (format.ThickTop) _xml.WriteAttributeString("thickTop", "1");
            if (format.ThickBottom) _xml.WriteAttributeString("thickBot", "1");
        }
    }

    private void EndRow()
    {
        if (!_rowInProgress) return;
        _xml.WriteEndElement();
        _rowInProgress = false;
    }

    /// <summary>
    /// Lazily opens the worksheet on the first cell-write: emits &lt;worksheet&gt; and
    /// &lt;sheetData&gt;. The prologue between them is inserted at save.
    /// </summary>
    private void EnsureWorksheetOpen()
    {
        EnsureNotDisposed();
        if (_worksheetOpened) return;

        _xml.WriteStartDocument(standalone: true);
        _xml.WriteStartElement("worksheet", SsNs);
        _xml.WriteAttributeString("xmlns", "r", null, RelNs);

        // Close the start tag, flush, and remember where the prologue (sheetPr, sheetViews,
        // sheetFormatPr, cols) belongs; it is built at save and inserted there.
        _xml.WriteString(string.Empty);
        _xml.Flush();
        _prologueInsertOffset = _tempStream.Position;

        _xml.WriteStartElement("sheetData", SsNs);
        _worksheetOpened = true;
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
        if (value is RichText rt)      { WriteRichStringCell(column, rt, styleId); return; }
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

    /// <summary>A rich-text string cell: an inline &lt;is&gt; with runs, or a rich shared string.</summary>
    private void WriteRichStringCell(int column, RichText value, int? styleId)
    {
        if (_state.Options.UseInlineStrings)
        {
            WriteCellOpen(column, styleId, "inlineStr");
            _xml.WriteStartElement("is", SsNs);
            RichTextXml.WriteRuns(_xml, value);
            _xml.WriteEndElement();
            _xml.WriteEndElement();
        }
        else
        {
            var ssId = _state.SharedStrings.GetOrAdd(value);
            WriteCellOpen(column, styleId, "s");
            WriteValue(SharedStringPlaceholderPrefix + ssId.ToString(CultureInfo.InvariantCulture) + SharedStringPlaceholderSuffix);
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
        _prologueXml = BuildPrologue();

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
        var extFormats = WriteConditionalFormats();

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

        if (_hyperlinks.Exists(h => h.Url is not null || h.Location is not null))
        {
            _xml.WriteStartElement("hyperlinks", SsNs);
            // Each hyperlink will get an rId allocated by the parent XlsxWriter at save time;
            // for now we generate the IDs ourselves following the convention "rIdHL{N}" and
            // pair them with their URLs for the writer to materialize as relationships.
            // Only links with an external target get a relationship (and so an r:id).
            int n = 1;
            foreach (var hl in _hyperlinks)
            {
                if (hl.Url is null && hl.Location is null) continue;   // a loaded link whose target is missing: nothing to point at
                _xml.WriteStartElement("hyperlink", SsNs);
                _xml.WriteAttributeString("ref", hl.CellAddress);
                if (hl.Url is not null)
                {
                    _xml.WriteAttributeString("id", RelNs, $"rIdHL{n}");
                    n++;
                }
                if (hl.Location is not null)
                    _xml.WriteAttributeString("location", hl.Location);
                if (hl.Tooltip is not null)
                    _xml.WriteAttributeString("tooltip", hl.Tooltip);
                if (hl.Display is not null)
                    _xml.WriteAttributeString("display", hl.Display);
                _xml.WriteEndElement();
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

        WriteExtensionList(extFormats);

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

    /// <summary>Byte offset just after the &lt;worksheet&gt; start tag, where <see cref="PrologueXml"/> is inserted.</summary>
    internal long PrologueInsertOffset => _prologueInsertOffset;

    /// <summary>The prologue elements (sheetPr, sheetViews, sheetFormatPr, cols), built at finalization; empty when none.</summary>
    internal byte[]? PrologueXml => _prologueXml;

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
        if (v.ErrorStyle != DataValidationErrorStyle.Stop)
            _xml.WriteAttributeString("errorStyle", v.ErrorStyle == DataValidationErrorStyle.Warning ? "warning" : "information");
        if (v.ImeMode is not null) _xml.WriteAttributeString("imeMode", v.ImeMode);
        if (v.Operator is not null)
            _xml.WriteAttributeString("operator", v.Operator);
        _xml.WriteAttributeString("allowBlank", v.AllowBlank ? "1" : "0");
        if (v.Type == "list")
            _xml.WriteAttributeString("showDropDown", v.ShowDropdown ? "0" : "1"); // OOXML inverted
        bool showError = v.ShowErrorMessage ?? (v.ErrorTitle is not null || v.ErrorMessage is not null);
        if (showError || v.ErrorTitle is not null || v.ErrorMessage is not null)
        {
            if (showError) _xml.WriteAttributeString("showErrorMessage", "1");
            if (v.ErrorTitle is not null) _xml.WriteAttributeString("errorTitle", v.ErrorTitle);
            if (v.ErrorMessage is not null) _xml.WriteAttributeString("error", v.ErrorMessage);
        }
        bool showInput = v.ShowInputMessage ?? (v.PromptTitle is not null || v.PromptMessage is not null);
        if (showInput || v.PromptTitle is not null || v.PromptMessage is not null)
        {
            if (showInput) _xml.WriteAttributeString("showInputMessage", "1");
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

    // ---- Conditional formatting -----------------------------------------------------

    private const string X14Ns = Chuvadi.Sheets.Internal.SheetExtrasParser.X14Ns;
    private const string XmNs = Chuvadi.Sheets.Internal.SheetExtrasParser.XmNs;
    private const string ExtCfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";
    private const string ExtRuleIdUri = "{B025F937-C7B1-47D3-B67F-A62EFF666E3E}";

    /// <summary>A rule written in the x14 extension: (sqref, rule, id, also-in-main).</summary>
    private sealed record ExtRule(string SqRef, ConditionalRule Rule, string Id, bool LinkedToMain);

    /// <summary>
    /// Writes the main &lt;conditionalFormatting&gt; blocks. Rules with priority 0 are numbered
    /// after the highest priority in use, in order. Returns the rules that go in the x14
    /// extension (extension-only rules, and data-bar details linked by id).
    /// </summary>
    private List<ExtRule> WriteConditionalFormats()
    {
        var ext = new List<ExtRule>();
        if (_conditionalFormats.Count == 0) return ext;

        int next = 0;
        foreach (var f in _conditionalFormats)
            foreach (var r in f.Rules) next = Math.Max(next, r.Priority);
        var priorities = new Dictionary<ConditionalRule, int>(ReferenceEqualityComparer.Instance);
        foreach (var f in _conditionalFormats)
            foreach (var r in f.Rules) priorities[r] = r.Priority > 0 ? r.Priority : ++next;

        foreach (var f in _conditionalFormats)
        {
            var main = new List<ConditionalRule>();
            foreach (var r in f.Rules)
            {
                // Colour scales have no x14 form here: always written in the main block.
                if (r.FromExtension && r.Type != ConditionalRuleType.ColorScale)
                    ext.Add(new ExtRule(f.SqRef, r, NewId(), LinkedToMain: false));
                else main.Add(r);
            }
            if (main.Count == 0) continue;
            _xml.WriteStartElement("conditionalFormatting", SsNs);
            if (f.Pivot) _xml.WriteAttributeString("pivot", "1");
            _xml.WriteAttributeString("sqref", f.SqRef);
            foreach (var r in main)
            {
                string? extId = null;
                if (r.Type == ConditionalRuleType.DataBar && r.DataBar is { HasExtension: true })
                {
                    extId = NewId();
                    ext.Add(new ExtRule(f.SqRef, r, extId, LinkedToMain: true));
                }
                WriteRule(r, priorities[r], FirstCell(f), extId);
            }
            _xml.WriteEndElement();
        }
        return ext;
    }

    private static string NewId() => "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";

    /// <summary>Top-left cell of the first range (what relative references in the rule's formulas are relative to).</summary>
    private static string FirstCell(ConditionalFormatting f)
    {
        var first = f.Ranges[0];
        int colon = first.IndexOf(':');
        return (colon < 0 ? first : first.Substring(0, colon)).Replace("$", "");
    }

    private void WriteRule(ConditionalRule r, int priority, string firstCell, string? extId)
    {
        string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        _xml.WriteStartElement("cfRule", SsNs);
        _xml.WriteAttributeString("type", Chuvadi.Sheets.Internal.SheetExtrasParser.RuleTypeName(r.Type));
        _xml.WriteAttributeString("priority", I(priority));
        if (r.StopIfTrue) _xml.WriteAttributeString("stopIfTrue", "1");
        if (OperatorText(r) is string op) _xml.WriteAttributeString("operator", op);
        if (r.Style is not null && UsesStyle(r.Type))
            _xml.WriteAttributeString("dxfId", I(_state.Styles.GetDxfId(r.Style)));
        if (r.Text is not null) _xml.WriteAttributeString("text", r.Text);
        if (r.Type == ConditionalRuleType.TimePeriod && r.TimePeriod is ConditionalTimePeriod tp)
            _xml.WriteAttributeString("timePeriod", Chuvadi.Sheets.Internal.SheetExtrasParser.PeriodName(tp));
        if (r.Type == ConditionalRuleType.Top10)
        {
            _xml.WriteAttributeString("rank", I(r.Rank ?? 10));
            if (r.Percent) _xml.WriteAttributeString("percent", "1");
            if (r.Bottom) _xml.WriteAttributeString("bottom", "1");
        }
        if (r.Type == ConditionalRuleType.AboveAverage)
        {
            if (!r.AboveAverage) _xml.WriteAttributeString("aboveAverage", "0");
            if (r.EqualAverage) _xml.WriteAttributeString("equalAverage", "1");
            if (r.StdDev is int sd) _xml.WriteAttributeString("stdDev", I(sd));
        }

        foreach (var f in FormulasFor(r, firstCell))
        {
            _xml.WriteStartElement("formula", SsNs);
            _xml.WriteString(f);
            _xml.WriteEndElement();
        }
        if (r.ColorScale is { } cs && r.Type == ConditionalRuleType.ColorScale)
        {
            _xml.WriteStartElement("colorScale", SsNs);
            foreach (var v in cs.Values) WriteCfvo(v, SsNs, null, x14: false);
            foreach (var c in cs.Colors) WriteRgb("color", SsNs, null, c);
            _xml.WriteEndElement();
        }
        if (r.DataBar is { } db && r.Type == ConditionalRuleType.DataBar)
        {
            _xml.WriteStartElement("dataBar", SsNs);
            if (db.MinLength != 10) _xml.WriteAttributeString("minLength", I(db.MinLength));
            if (db.MaxLength != 90) _xml.WriteAttributeString("maxLength", I(db.MaxLength));
            if (!db.ShowValue) _xml.WriteAttributeString("showValue", "0");
            // The 2007 form only knows min/max/num/percent/percentile/formula thresholds.
            WriteCfvo(Main2007(db.Min, isMin: true), SsNs, null, x14: false);
            WriteCfvo(Main2007(db.Max, isMin: false), SsNs, null, x14: false);
            WriteRgb("color", SsNs, null, db.Color);
            _xml.WriteEndElement();
        }
        if (r.IconSet is { } ic && r.Type == ConditionalRuleType.IconSet)
        {
            _xml.WriteStartElement("iconSet", SsNs);
            if (ic.IconSet != "3TrafficLights1") _xml.WriteAttributeString("iconSet", ic.IconSet);
            if (!ic.ShowValue) _xml.WriteAttributeString("showValue", "0");
            if (ic.Reverse) _xml.WriteAttributeString("reverse", "1");
            foreach (var v in ic.Values) WriteCfvo(v, SsNs, null, x14: false);
            _xml.WriteEndElement();
        }
        if (extId is not null)
        {
            _xml.WriteStartElement("extLst", SsNs);
            _xml.WriteStartElement("ext", SsNs);
            _xml.WriteAttributeString("xmlns", "x14", null, X14Ns);
            _xml.WriteAttributeString("uri", ExtRuleIdUri);
            _xml.WriteStartElement("x14", "id", X14Ns);
            _xml.WriteString(extId);
            _xml.WriteEndElement();
            _xml.WriteEndElement();
            _xml.WriteEndElement();
        }
        _xml.WriteEndElement(); // </cfRule>
    }

    private static ConditionalValue Main2007(ConditionalValue v, bool isMin) => v.Type switch
    {
        ConditionalValueType.AutoMin => new ConditionalValue(ConditionalValueType.Min),
        ConditionalValueType.AutoMax => new ConditionalValue(ConditionalValueType.Max),
        _ => v,
    };

    private static bool UsesStyle(ConditionalRuleType t)
        => t is not (ConditionalRuleType.ColorScale or ConditionalRuleType.DataBar or ConditionalRuleType.IconSet);

    private static string? OperatorText(ConditionalRule r)
    {
        if (r.Type == ConditionalRuleType.CellIs && r.Operator is ComparisonOp op) return OpToOoxml(op);
        if (r.RawOperator is not null) return r.RawOperator;
        return r.Type switch
        {
            ConditionalRuleType.ContainsText => "containsText",
            ConditionalRuleType.NotContainsText => "notContains",
            ConditionalRuleType.BeginsWith => "beginsWith",
            ConditionalRuleType.EndsWith => "endsWith",
            _ => null,
        };
    }

    /// <summary>The rule's formulas; for rules built in code without one, the formula Excel itself writes for that rule type.</summary>
    private static IEnumerable<string> FormulasFor(ConditionalRule r, string c)
    {
        if (r.Formulas.Count > 0) return r.Formulas;
        string Q(string? t) => "\"" + (t ?? string.Empty).Replace("\"", "\"\"") + "\"";
        string? f = r.Type switch
        {
            ConditionalRuleType.ContainsText => $"NOT(ISERROR(SEARCH({Q(r.Text)},{c})))",
            ConditionalRuleType.NotContainsText => $"ISERROR(SEARCH({Q(r.Text)},{c}))",
            ConditionalRuleType.BeginsWith => $"LEFT({c},LEN({Q(r.Text)}))={Q(r.Text)}",
            ConditionalRuleType.EndsWith => $"RIGHT({c},LEN({Q(r.Text)}))={Q(r.Text)}",
            ConditionalRuleType.ContainsBlanks => $"LEN(TRIM({c}))=0",
            ConditionalRuleType.NotContainsBlanks => $"LEN(TRIM({c}))>0",
            ConditionalRuleType.ContainsErrors => $"ISERROR({c})",
            ConditionalRuleType.NotContainsErrors => $"NOT(ISERROR({c}))",
            ConditionalRuleType.TimePeriod => r.TimePeriod switch
            {
                ConditionalTimePeriod.Today => $"FLOOR({c},1)=TODAY()",
                ConditionalTimePeriod.Yesterday => $"FLOOR({c},1)=TODAY()-1",
                ConditionalTimePeriod.Tomorrow => $"FLOOR({c},1)=TODAY()+1",
                ConditionalTimePeriod.Last7Days => $"AND(TODAY()-FLOOR({c},1)<=6,FLOOR({c},1)<=TODAY())",
                ConditionalTimePeriod.ThisMonth => $"AND(MONTH({c})=MONTH(TODAY()),YEAR({c})=YEAR(TODAY()))",
                ConditionalTimePeriod.LastMonth => $"AND(MONTH({c})=MONTH(EDATE(TODAY(),0-1)),YEAR({c})=YEAR(EDATE(TODAY(),0-1)))",
                ConditionalTimePeriod.NextMonth => $"AND(MONTH({c})=MONTH(EDATE(TODAY(),0+1)),YEAR({c})=YEAR(EDATE(TODAY(),0+1)))",
                ConditionalTimePeriod.ThisWeek => $"AND(TODAY()-ROUNDDOWN({c},0)<=WEEKDAY(TODAY())-1,ROUNDDOWN({c},0)-TODAY()<=7-WEEKDAY(TODAY()))",
                ConditionalTimePeriod.LastWeek => $"AND(TODAY()-ROUNDDOWN({c},0)>=(WEEKDAY(TODAY())),TODAY()-ROUNDDOWN({c},0)<(WEEKDAY(TODAY())+7))",
                ConditionalTimePeriod.NextWeek => $"AND(ROUNDDOWN({c},0)-TODAY()>(7-WEEKDAY(TODAY())),ROUNDDOWN({c},0)-TODAY()<(15-WEEKDAY(TODAY())))",
                _ => null,
            },
            _ => null,
        };
        return f is null ? Array.Empty<string>() : new[] { f };
    }

    private void WriteCfvo(ConditionalValue v, string ns, string? prefix, bool x14)
    {
        if (prefix is null) _xml.WriteStartElement("cfvo", ns); else _xml.WriteStartElement(prefix, "cfvo", ns);
        _xml.WriteAttributeString("type", v.Type switch
        {
            ConditionalValueType.Number => "num",
            ConditionalValueType.Percent => "percent",
            ConditionalValueType.Percentile => "percentile",
            ConditionalValueType.Min => "min",
            ConditionalValueType.Max => "max",
            ConditionalValueType.Formula => "formula",
            ConditionalValueType.AutoMin => "autoMin",
            _ => "autoMax",
        });
        if (!x14 && v.Value is not null) _xml.WriteAttributeString("val", v.Value);
        if (!v.GreaterOrEqual) _xml.WriteAttributeString("gte", "0");
        if (x14 && v.Value is not null)
        {
            _xml.WriteStartElement("xm", "f", XmNs);
            _xml.WriteString(v.Value);
            _xml.WriteEndElement();
        }
        _xml.WriteEndElement();
    }

    private void WriteRgb(string name, string ns, string? prefix, string? rgb)
    {
        if (prefix is null) _xml.WriteStartElement(name, ns); else _xml.WriteStartElement(prefix, name, ns);
        _xml.WriteAttributeString("rgb", "FF" + (rgb ?? "000000"));
        _xml.WriteEndElement();
    }

    /// <summary>The worksheet &lt;extLst&gt; with x14 conditional formats, when there are any.</summary>
    private void WriteExtensionList(List<ExtRule> ext)
    {
        if (ext.Count == 0) return;
        string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        int maxPriority = 0;
        foreach (var f in _conditionalFormats) foreach (var r in f.Rules) maxPriority = Math.Max(maxPriority, r.Priority);

        _xml.WriteStartElement("extLst", SsNs);
        _xml.WriteStartElement("ext", SsNs);
        _xml.WriteAttributeString("xmlns", "x14", null, X14Ns);
        _xml.WriteAttributeString("uri", ExtCfUri);
        _xml.WriteStartElement("x14", "conditionalFormattings", X14Ns);
        foreach (var e in ext)
        {
            var r = e.Rule;
            _xml.WriteStartElement("x14", "conditionalFormatting", X14Ns);
            _xml.WriteAttributeString("xmlns", "xm", null, XmNs);
            _xml.WriteStartElement("x14", "cfRule", X14Ns);
            _xml.WriteAttributeString("type", Chuvadi.Sheets.Internal.SheetExtrasParser.RuleTypeName(r.Type));
            if (!e.LinkedToMain)
            {
                _xml.WriteAttributeString("priority", I(r.Priority > 0 ? r.Priority : ++maxPriority));
                if (r.StopIfTrue) _xml.WriteAttributeString("stopIfTrue", "1");
                if (OperatorText(r) is string op) _xml.WriteAttributeString("operator", op);
                if (r.Text is not null) _xml.WriteAttributeString("text", r.Text);
            }
            _xml.WriteAttributeString("id", e.Id);

            if (!e.LinkedToMain)
                foreach (var f in FormulasFor(r, FirstCell(new ConditionalFormatting(Chuvadi.Sheets.Internal.SheetExtrasParser.SplitRanges(e.SqRef), Array.Empty<ConditionalRule>()))))
                {
                    _xml.WriteStartElement("xm", "f", XmNs);
                    _xml.WriteString(f);
                    _xml.WriteEndElement();
                }
            if (r.DataBar is { } db && r.Type == ConditionalRuleType.DataBar)
            {
                _xml.WriteStartElement("x14", "dataBar", X14Ns);
                _xml.WriteAttributeString("minLength", I(db.MinLength));
                _xml.WriteAttributeString("maxLength", I(db.MaxLength));
                if (!db.ShowValue) _xml.WriteAttributeString("showValue", "0");
                if (!db.Gradient) _xml.WriteAttributeString("gradient", "0");
                if (db.BorderColor is not null) _xml.WriteAttributeString("border", "1");
                if (db.NegativeFillColor is null) _xml.WriteAttributeString("negativeBarColorSameAsPositive", "1");
                if (db.BorderColor is not null && db.NegativeBorderColor is not null) _xml.WriteAttributeString("negativeBarBorderColorSameAsPositive", "0");
                if (db.AxisPosition != DataBarAxisPosition.Automatic)
                    _xml.WriteAttributeString("axisPosition", db.AxisPosition == DataBarAxisPosition.Middle ? "middle" : "none");
                if (db.Direction != DataBarDirection.Context)
                    _xml.WriteAttributeString("direction", db.Direction == DataBarDirection.LeftToRight ? "leftToRight" : "rightToLeft");
                WriteCfvo(db.Min, X14Ns, "x14", x14: true);
                WriteCfvo(db.Max, X14Ns, "x14", x14: true);
                if (!e.LinkedToMain) WriteRgb("fillColor", X14Ns, "x14", db.Color);
                if (db.BorderColor is not null) WriteRgb("borderColor", X14Ns, "x14", db.BorderColor);
                if (db.NegativeFillColor is not null) WriteRgb("negativeFillColor", X14Ns, "x14", db.NegativeFillColor);
                if (db.NegativeBorderColor is not null) WriteRgb("negativeBorderColor", X14Ns, "x14", db.NegativeBorderColor);
                if (db.AxisColor is not null) WriteRgb("axisColor", X14Ns, "x14", db.AxisColor);
                _xml.WriteEndElement();
            }
            if (r.IconSet is { } ic && r.Type == ConditionalRuleType.IconSet && !e.LinkedToMain)
            {
                _xml.WriteStartElement("x14", "iconSet", X14Ns);
                _xml.WriteAttributeString("iconSet", ic.IconSet);
                if (!ic.ShowValue) _xml.WriteAttributeString("showValue", "0");
                if (ic.Reverse) _xml.WriteAttributeString("reverse", "1");
                foreach (var v in ic.Values) WriteCfvo(v, X14Ns, "x14", x14: true);
                _xml.WriteEndElement();
            }
            if (!e.LinkedToMain && r.Style is not null && UsesStyle(r.Type))
                Chuvadi.Sheets.Internal.StyleRegistry.WriteDxf(_xml, r.Style, SsNs, code => _state.Styles.NumberFormatId(code), "x14", X14Ns);
            _xml.WriteEndElement(); // </x14:cfRule>
            _xml.WriteStartElement("xm", "sqref", XmNs);
            _xml.WriteString(e.SqRef);
            _xml.WriteEndElement();
            _xml.WriteEndElement(); // </x14:conditionalFormatting>
        }
        _xml.WriteEndElement(); // </x14:conditionalFormattings>
        _xml.WriteEndElement(); // </ext>
        _xml.WriteEndElement(); // </extLst>
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

    // ---- Prologue (built at save) ---------------------------------------------------

    /// <summary>
    /// sheetPr (tab colour, outline summary, fit-to-page), sheetViews, sheetFormatPr and cols,
    /// as bytes for insertion after the &lt;worksheet&gt; start tag. Elements with nothing to say
    /// are left out, so a plain sheet gets an empty prologue (same bytes as before).
    /// </summary>
    private byte[] BuildPrologue()
    {
        var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
            CloseOutput = false,
        }))
        {
            string I(int v) => v.ToString(CultureInfo.InvariantCulture);
            string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);

            // Columns: explicit spans, then auto-fitted columns not covered by an explicit width.
            var cols = new List<ColumnEntry>(_columns);
            if (_autoFit is not null)
            {
                var headers = new List<(int Row, int C1, int C2)>();
                if (_autoFilterRange is not null && ColumnAutoFitter.TryParseRange(_autoFilterRange, out var af))
                    headers.Add((af.R1, af.C1, af.C2));
                foreach (var t in _tables)
                    if (ColumnAutoFitter.TryParseRange(t.Range, out var tr)) headers.Add((tr.R1, tr.C1, tr.C2));
                foreach (var kv in _autoFit.ComputeWidths(headers))
                {
                    bool explicitWidth = false;
                    foreach (var c in _columns)
                        if (c.Width > 0 && kv.Key >= c.Min && kv.Key <= c.Max) { explicitWidth = true; break; }
                    if (!explicitWidth) cols.Add(new ColumnEntry(kv.Key, kv.Key, kv.Value));
                }
            }
            cols = cols.OrderBy(c => c.Min).ToList();
            int maxColOutline = 0;
            foreach (var c in cols) maxColOutline = Math.Max(maxColOutline, c.OutlineLevel);

            // sheetPr
            bool outlinePr = !_format.OutlineSummaryBelow || !_format.OutlineSummaryRight;
            if (_tabColor is not null || outlinePr || _pageSetup.FitToPage)
            {
                w.WriteStartElement("sheetPr");
                if (_tabColor is not null) { w.WriteStartElement("tabColor"); w.WriteAttributeString("rgb", "FF" + _tabColor); w.WriteEndElement(); }
                if (outlinePr)
                {
                    w.WriteStartElement("outlinePr");
                    if (!_format.OutlineSummaryBelow) w.WriteAttributeString("summaryBelow", "0");
                    if (!_format.OutlineSummaryRight) w.WriteAttributeString("summaryRight", "0");
                    w.WriteEndElement();
                }
                if (_pageSetup.FitToPage) { w.WriteStartElement("pageSetUpPr"); w.WriteAttributeString("fitToPage", "1"); w.WriteEndElement(); }
                w.WriteEndElement();
            }

            // sheetViews
            var v = _view;
            if (v.HasContent)
            {
                w.WriteStartElement("sheetViews");
                w.WriteStartElement("sheetView");
                w.WriteAttributeString("workbookViewId", "0");
                if (v.TabSelected) w.WriteAttributeString("tabSelected", "1");
                if (!v.ShowGridLines) w.WriteAttributeString("showGridLines", "0");
                if (!v.ShowHeadings) w.WriteAttributeString("showRowColHeaders", "0");
                if (!v.ShowZeros) w.WriteAttributeString("showZeros", "0");
                if (v.ShowFormulas) w.WriteAttributeString("showFormulas", "1");
                if (v.RightToLeft) w.WriteAttributeString("rightToLeft", "1");
                if (!v.ShowOutlineSymbols) w.WriteAttributeString("showOutlineSymbols", "0");
                if (!v.ShowRuler) w.WriteAttributeString("showRuler", "0");
                if (!v.ShowWhiteSpace) w.WriteAttributeString("showWhiteSpace", "0");
                if (v.ViewMode != SheetViewMode.Normal) w.WriteAttributeString("view", v.ViewMode == SheetViewMode.PageLayout ? "pageLayout" : "pageBreakPreview");
                if (v.TopLeftCell is not null) w.WriteAttributeString("topLeftCell", v.TopLeftCell);
                if (v.ZoomScale != 100) w.WriteAttributeString("zoomScale", I(v.ZoomScale));
                if (v.ZoomScaleNormal is int zn) w.WriteAttributeString("zoomScaleNormal", I(zn));
                if (v.ZoomScalePageLayout is int zl) w.WriteAttributeString("zoomScalePageLayoutView", I(zl));
                if (v.ZoomScalePageBreakPreview is int zb) w.WriteAttributeString("zoomScaleSheetLayoutView", I(zb));
                if (v.Pane is { } p)
                {
                    w.WriteStartElement("pane");
                    bool frozen = p.State != PaneState.Split;
                    if (p.XSplit > 0) w.WriteAttributeString("xSplit", frozen ? I((int)p.XSplit) : D(p.XSplit));
                    if (p.YSplit > 0) w.WriteAttributeString("ySplit", frozen ? I((int)p.YSplit) : D(p.YSplit));
                    if (p.TopLeftCell is not null) w.WriteAttributeString("topLeftCell", p.TopLeftCell);
                    w.WriteAttributeString("activePane", PaneName(p.ActivePane));
                    w.WriteAttributeString("state", p.State switch { PaneState.Frozen => "frozen", PaneState.FrozenSplit => "frozenSplit", _ => "split" });
                    w.WriteEndElement();
                }
                foreach (var sel in v.Selections)
                {
                    w.WriteStartElement("selection");
                    if (sel.Pane != PaneKind.TopLeft) w.WriteAttributeString("pane", PaneName(sel.Pane));
                    if (sel.ActiveCell is not null) w.WriteAttributeString("activeCell", sel.ActiveCell);
                    if (sel.Ranges is not null) w.WriteAttributeString("sqref", sel.Ranges);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteEndElement();
            }

            // sheetFormatPr — when stated, or when outline levels need declaring.
            if (_format.IsDefined || _maxRowOutline > 0 || maxColOutline > 0)
            {
                var f = _format;
                w.WriteStartElement("sheetFormatPr");
                if (f.BaseColumnWidth != 8) w.WriteAttributeString("baseColWidth", I(f.BaseColumnWidth));
                if (f.DefaultColumnWidth is double dcw) w.WriteAttributeString("defaultColWidth", D(dcw));
                w.WriteAttributeString("defaultRowHeight", D(f.DefaultRowHeight));
                if (f.CustomHeight) w.WriteAttributeString("customHeight", "1");
                if (f.ZeroHeight) w.WriteAttributeString("zeroHeight", "1");
                if (f.ThickTop) w.WriteAttributeString("thickTop", "1");
                if (f.ThickBottom) w.WriteAttributeString("thickBottom", "1");
                if (_maxRowOutline > 0) w.WriteAttributeString("outlineLevelRow", I(_maxRowOutline));
                if (maxColOutline > 0) w.WriteAttributeString("outlineLevelCol", I(maxColOutline));
                w.WriteEndElement();
            }

            // cols
            if (cols.Count > 0)
            {
                w.WriteStartElement("cols");
                foreach (var c in cols)
                {
                    w.WriteStartElement("col");
                    w.WriteAttributeString("min", I(c.Min));
                    w.WriteAttributeString("max", I(c.Max));
                    if (c.Width > 0)
                    {
                        w.WriteAttributeString("width", c.Width.ToString(CultureInfo.InvariantCulture));
                        if (c.StyleId is int sid0) w.WriteAttributeString("style", I(sid0));
                        if (c.Hidden) w.WriteAttributeString("hidden", "1");
                        if (c.BestFit) w.WriteAttributeString("bestFit", "1");
                        w.WriteAttributeString("customWidth", "1");
                    }
                    else
                    {
                        if (c.StyleId is int sid1) w.WriteAttributeString("style", I(sid1));
                        if (c.Hidden) w.WriteAttributeString("hidden", "1");
                        if (c.BestFit) w.WriteAttributeString("bestFit", "1");
                    }
                    if (c.OutlineLevel > 0) w.WriteAttributeString("outlineLevel", I(c.OutlineLevel));
                    if (c.Collapsed) w.WriteAttributeString("collapsed", "1");
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
        }
        return ms.ToArray();
    }

    private static string PaneName(PaneKind p) => p switch
    {
        PaneKind.TopRight => "topRight",
        PaneKind.BottomLeft => "bottomLeft",
        PaneKind.BottomRight => "bottomRight",
        _ => "topLeft",
    };

    /// <summary>The visibility state for workbook.xml.</summary>
    internal string? StateAttribute => Visibility switch
    {
        SheetVisibility.Hidden => "hidden",
        SheetVisibility.VeryHidden => "veryHidden",
        _ => null,
    };

    /// <summary>One &lt;col&gt; span set on the writer.</summary>
    private sealed record ColumnEntry(int Min, int Max, double Width)
    {
        public bool Hidden { get; init; }
        public int OutlineLevel { get; init; }
        public bool Collapsed { get; init; }
        public bool BestFit { get; init; }
        public int? StyleId { get; init; }
    }

    // ---- Guards / Disposal ---------------------------------------------------------

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);


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
