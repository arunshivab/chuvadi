using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Chuvadi.Sheets.Internal;
using Chuvadi.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Streaming xlsx reader. Open with <see cref="Open(string, XlsxReaderOptions?)"/>, enumerate
/// rows via <see cref="Sheet(string)"/> or <see cref="Sheet(int)"/>, then dispose.
///
/// <code>
/// using var reader = XlsxReader.Open("file.xlsx");
/// var sheet = reader.Sheet("Data");
/// foreach (var row in sheet.Rows)
/// {
///     var id = row.GetInt32("Id");
///     var name = row.GetString("Name");
/// }
/// </code>
///
/// The shared strings table and styles are loaded fully into memory at open time (small
/// relative to sheet data; referenced by integer index). Sheet content streams via XmlReader,
/// with each <see cref="RowReader"/> reusing a buffer — DO NOT retain RowReader instances
/// across iterations.
/// </summary>
public sealed class XlsxReader : IDisposable
{
    private readonly OoxmlPackage _package;
    private readonly XlsxReaderOptions _options;

    /// <summary>Shared strings, indexed by SST id. May be empty if the file uses only inline strings.</summary>
    internal string[] SharedStrings { get; }

    /// <summary>Style entries, indexed by cellXf id. May be empty for files with no styles.xml.</summary>
    internal StyleEntry[] Styles { get; }

    /// <summary>The sheets in the workbook, in tab order.</summary>
    public IReadOnlyList<SheetInfo> Sheets { get; }

    internal XlsxReaderOptions Options => _options;

    private XlsxReader(OoxmlPackage package, string[] sst, StyleEntry[] styles,
                       IReadOnlyList<SheetInfo> sheets, XlsxReaderOptions options)
    {
        _package = package;
        _options = options;
        SharedStrings = sst;
        Styles = styles;
        Sheets = sheets;
    }

    // ---- Open ---------------------------------------------------------------------

    public static XlsxReader Open(string path, XlsxReaderOptions? options = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return OpenCore(stream, options ?? new XlsxReaderOptions(), ownsStream: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static XlsxReader Open(Stream input, XlsxReaderOptions? options = null)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        return OpenCore(input, options ?? new XlsxReaderOptions(), ownsStream: false);
    }

    private static XlsxReader OpenCore(Stream input, XlsxReaderOptions options, bool ownsStream)
    {
        OoxmlPackage? pkg = null;
        try
        {
            pkg = OoxmlPackage.Open(input);

            // Find the workbook part via root relationships.
            string? workbookUri = null;
            foreach (var rel in pkg.GetRelationships("/"))
            {
                if (rel.Type.EndsWith("/officeDocument", StringComparison.Ordinal))
                {
                    workbookUri = "/" + rel.Target.TrimStart('/');
                    break;
                }
            }
            if (workbookUri is null)
                throw new XlsxFormatException("Package has no officeDocument relationship; not a valid xlsx.");

            // Load shared strings (optional).
            var sst = TryLoadSharedStrings(pkg, options);

            // Load styles (optional but almost always present).
            var styles = TryLoadStyles(pkg, options);

            // Parse workbook.xml for the sheet list and definedNames.
            var sheets = LoadWorkbookSheets(pkg, workbookUri, options);

            return new XlsxReader(pkg, sst, styles, sheets, options);
        }
        catch
        {
            pkg?.Dispose();
            if (ownsStream) input.Dispose();
            throw;
        }
    }

    // ---- Sheet access -------------------------------------------------------------

    /// <summary>Returns a sheet reader by 1-based tab order.</summary>
    public SheetReader Sheet(int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > Sheets.Count)
            throw new ArgumentOutOfRangeException(nameof(oneBasedIndex));
        return new SheetReader(this, Sheets[oneBasedIndex - 1]);
    }

    /// <summary>Returns a sheet reader by name (case-insensitive).</summary>
    public SheetReader Sheet(string name)
    {
        foreach (var s in Sheets)
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                return new SheetReader(this, s);
        throw new KeyNotFoundException($"No sheet named '{name}'.");
    }

    /// <summary>Opens the sheet part's XML stream (decompression-capped when
    /// <see cref="XlsxReaderOptions.MaxPartBytes"/> is set). Caller disposes.</summary>
    internal Stream OpenSheetPart(SheetInfo info)
        => Limit(_package.OpenPart(info.PartUri), info.PartUri, _options);

    /// <summary>Wraps a part stream in a decompression-size guard when a cap is configured.</summary>
    private static Stream Limit(Stream s, string partUri, XlsxReaderOptions options)
        => options.MaxPartBytes is long max
            ? new Chuvadi.Internal.LimitedReadStream(s, max, $"package part '{partUri}'")
            : s;

    // ---- Loading -----------------------------------------------------------------

    private static string[] TryLoadSharedStrings(OoxmlPackage pkg, XlsxReaderOptions options)
    {
        foreach (var part in pkg.Parts)
        {
            if (part.Uri.EndsWith("/sharedStrings.xml", StringComparison.Ordinal))
            {
                using var s = Limit(pkg.OpenPart(part.Uri), part.Uri, options);
                return SharedStringReader.Read(s);
            }
        }
        return Array.Empty<string>();
    }

    private static StyleEntry[] TryLoadStyles(OoxmlPackage pkg, XlsxReaderOptions options)
    {
        foreach (var part in pkg.Parts)
        {
            if (part.Uri.EndsWith("/styles.xml", StringComparison.Ordinal))
            {
                using var s = Limit(pkg.OpenPart(part.Uri), part.Uri, options);
                return StyleSheetReader.Read(s);
            }
        }
        return Array.Empty<StyleEntry>();
    }

    private static IReadOnlyList<SheetInfo> LoadWorkbookSheets(OoxmlPackage pkg, string workbookUri, XlsxReaderOptions options)
    {
        // Read workbook.xml — list each <sheet> with its r:id, then resolve to a part URI
        // via the workbook's relationships.
        var rels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in pkg.GetRelationships(workbookUri))
        {
            if (rel.Type.EndsWith("/worksheet", StringComparison.Ordinal))
            {
                // Resolve relative target to absolute URI.
                // Targets are relative to the workbook part's directory ("xl/"), e.g.
                // "worksheets/sheet1.xml" → "/xl/worksheets/sheet1.xml".
                var workbookDir = workbookUri.Substring(0, workbookUri.LastIndexOf('/'));  // "/xl"
                var target = rel.Target.TrimStart('/');
                // Handle "../" prefixes (sometimes seen in foreign files).
                string absolute;
                if (target.StartsWith("../", StringComparison.Ordinal))
                {
                    var parentDir = workbookDir.Substring(0, workbookDir.LastIndexOf('/'));
                    absolute = parentDir + "/" + target.Substring(3);
                }
                else
                {
                    absolute = workbookDir + "/" + target;
                }
                rels[rel.Id] = absolute;
            }
        }

        var sheets = new List<SheetInfo>();
        using (var s = Limit(pkg.OpenPart(workbookUri), workbookUri, options))
        using (var r = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true, CloseInput = false }))
        {
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element || r.LocalName != "sheet") continue;

                var name = r.GetAttribute("name");
                var sheetId = r.GetAttribute("sheetId");
                var rId = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                if (name is null || rId is null) continue;
                if (!rels.TryGetValue(rId, out var partUri)) continue;

                sheets.Add(new SheetInfo(name, sheetId ?? "1", partUri));
            }
        }
        return sheets;
    }

    // ---- Disposal -----------------------------------------------------------------

    public void Dispose() => _package.Dispose();
}

/// <summary>
/// Lightweight descriptor for a sheet — name + part URI. Available before opening the sheet's content.
/// </summary>
public sealed class SheetInfo
{
    public string Name { get; }
    public string SheetId { get; }
    internal string PartUri { get; }

    internal SheetInfo(string name, string sheetId, string partUri)
    {
        Name = name; SheetId = sheetId; PartUri = partUri;
    }
}

/// <summary>
/// Per-sheet reader. Iterate <see cref="Rows"/> to stream rows in order.
/// </summary>
public sealed class SheetReader
{
    private readonly XlsxReader _book;
    private readonly SheetInfo _info;

    /// <summary>Maps header text → 0-based column index (built from row 1 if enabled).</summary>
    private Dictionary<string, int>? _headerIndex;

    internal SheetReader(XlsxReader book, SheetInfo info)
    {
        _book = book;
        _info = info;
    }

    public string Name => _info.Name;

    /// <summary>
    /// Streaming row enumeration. Each <see cref="RowReader"/> yielded is a transient struct
    /// over a reused buffer — do NOT retain across iterations.
    /// </summary>
    public IEnumerable<RowReader> Rows => new RowEnumerable(this);

    internal Dictionary<string, int>? HeaderIndex => _headerIndex;

    internal void SetHeaderIndex(Dictionary<string, int> headers) => _headerIndex = headers;

    internal XlsxReader Book => _book;
    internal SheetInfo Info => _info;
}

/// <summary>
/// Internal enumerable that opens the sheet XML on iteration and yields rows. Implemented as
/// a class (not iterator method) so we can manage the disposal of the XmlReader cleanly.
/// </summary>
internal sealed class RowEnumerable : IEnumerable<RowReader>
{
    private readonly SheetReader _sheet;
    public RowEnumerable(SheetReader sheet) { _sheet = sheet; }
    public IEnumerator<RowReader> GetEnumerator() => new RowEnumerator(_sheet);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Pulls rows from the sheet's XML stream one at a time. Reuses internal buffers so each
/// row yielded has zero allocations in the steady state.
/// </summary>
internal sealed class RowEnumerator : IEnumerator<RowReader>
{
    private readonly SheetReader _sheet;
    private readonly Stream _stream;
    private readonly XmlReader _xml;
    private readonly bool _autoDetectDates;
    private readonly XlsxReader _book;

    // Row buffer — reused across all rows. _cellValues[col-1] holds the parsed value.
    private object?[] _cellValues = new object?[16];
    private int _maxColumnSeen;
    private int _currentRowNumber;
    private bool _hasCurrent;
    private bool _headersConsumed;

    public RowEnumerator(SheetReader sheet)
    {
        _sheet = sheet;
        _book = sheet.Book;
        _stream = _book.OpenSheetPart(sheet.Info);
        _xml = XmlReader.Create(_stream, new XmlReaderSettings
        {
            IgnoreWhitespace = false,
            IgnoreComments = true,
            CloseInput = false,
        });
        _autoDetectDates = _book.Options.AutoDetectDates;
    }

    public RowReader Current
        => _hasCurrent
            ? new RowReader(_cellValues, _maxColumnSeen, _currentRowNumber, _sheet)
            : throw new InvalidOperationException("Enumeration has not started or already ended.");

    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        // Loop in case we need to skip past the header row internally.
        while (true)
        {
            if (!ReadNextRow()) return false;

            // Build header index from the first row if enabled and not yet built.
            if (_book.Options.TreatFirstRowAsHeaders && !_headersConsumed)
            {
                BuildHeaderIndex();
                _headersConsumed = true;
                // Don't yield the header row; advance to the next.
                continue;
            }

            return true;
        }
    }

    private bool ReadNextRow()
    {
        ClearRowBuffer();

        // Advance to the next <row> element.
        while (_xml.Read())
        {
            if (_xml.NodeType != XmlNodeType.Element) continue;
            if (_xml.LocalName != "row") continue;

            var rAttr = _xml.GetAttribute("r");
            _currentRowNumber = int.TryParse(rAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rn)
                ? rn
                : _currentRowNumber + 1;

            // Read cells within this row.
            ReadCellsInRow();
            _hasCurrent = true;
            return true;
        }

        _hasCurrent = false;
        return false;
    }

    private void ReadCellsInRow()
    {
        if (_xml.IsEmptyElement) return;

        int rowDepth = _xml.Depth;
        int impliedCol = 0;  // for cells without r= attribute

        while (_xml.Read())
        {
            if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == rowDepth)
                return;

            if (_xml.NodeType != XmlNodeType.Element) continue;
            if (_xml.LocalName != "c") continue;

            // Parse the cell.
            var rAttr = _xml.GetAttribute("r");
            int col;
            if (rAttr is not null)
            {
                col = ParseCellColumn(rAttr);
            }
            else
            {
                col = ++impliedCol;
            }
            impliedCol = col;

            var t = _xml.GetAttribute("t");
            var s = _xml.GetAttribute("s");
            int styleIndex = -1;
            if (s is not null)
                int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out styleIndex);

            // Read the cell's contents.
            var value = ReadCellValue(t, styleIndex);

            // Store in buffer; grow if needed.
            EnsureCapacity(col);
            _cellValues[col - 1] = value;
            if (col > _maxColumnSeen) _maxColumnSeen = col;
        }
    }

    private object? ReadCellValue(string? cellType, int styleIndex)
    {
        if (_xml.IsEmptyElement) return null;

        int cellDepth = _xml.Depth;
        string? rawValue = null;
        bool isFormula = false;
        bool inInlineStr = false;
        var inlineSb = new System.Text.StringBuilder();

        while (_xml.Read())
        {
            if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == cellDepth)
                break;

            if (_xml.NodeType == XmlNodeType.Element)
            {
                if (_xml.LocalName == "v")
                {
                    rawValue = ReadElementText();
                }
                else if (_xml.LocalName == "f")
                {
                    isFormula = true;
                    // Skip the formula text — we return cached value via the <v> sibling.
                    if (!_xml.IsEmptyElement) _xml.Skip();
                }
                else if (_xml.LocalName == "is")
                {
                    // Inline string container — extract its text content.
                    inInlineStr = true;
                    ExtractStringFromInlineStrContainer(inlineSb);
                }
            }
        }

        if (inInlineStr) return inlineSb.ToString();
        if (rawValue is null) return null;

        return InterpretRawValue(cellType, rawValue, styleIndex, isFormula);
    }

    private string ReadElementText()
    {
        // Read everything inside the current element until its close.
        if (_xml.IsEmptyElement) return string.Empty;
        int depth = _xml.Depth;
        var sb = new System.Text.StringBuilder();
        while (_xml.Read())
        {
            if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == depth)
                return sb.ToString();
            if (_xml.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
                sb.Append(_xml.Value);
        }
        return sb.ToString();
    }

    private void ExtractStringFromInlineStrContainer(System.Text.StringBuilder sb)
    {
        // <is> may contain a simple <t> or rich-text <r>/<t> structure.
        if (_xml.IsEmptyElement) return;
        int depth = _xml.Depth;
        while (_xml.Read())
        {
            if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == depth) return;
            if (_xml.NodeType == XmlNodeType.Element && _xml.LocalName == "t")
            {
                if (!_xml.IsEmptyElement)
                {
                    int td = _xml.Depth;
                    while (_xml.Read())
                    {
                        if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == td) break;
                        if (_xml.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
                            sb.Append(_xml.Value);
                    }
                }
            }
        }
    }

    private object? InterpretRawValue(string? cellType, string rawValue, int styleIndex, bool isFormula)
    {
        // OOXML cell type semantics:
        //   t="s"        — value is an SST id
        //   t="str"      — value is an inline string (typically formula result)
        //   t="inlineStr"— actually handled above via <is>
        //   t="b"        — "1"/"0"
        //   t="e"        — error code
        //   t="d"        — ISO date string
        //   t=null/"n"   — numeric (default)
        //
        // For numerics, apply date auto-detect from the style if enabled.

        switch (cellType)
        {
            case "s":
            {
                if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                {
                    var arr = _book.SharedStrings;
                    if (idx >= 0 && idx < arr.Length) return arr[idx];
                }
                return rawValue;  // malformed — return raw as fallback
            }
            case "str":
                return rawValue;
            case "b":
                return rawValue == "1";
            case "e":
                return rawValue;
            case "d":
                return DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? dt
                    : (object)rawValue;
            default:
                // Numeric.
                if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    return rawValue;

                if (_autoDetectDates && styleIndex >= 0 && styleIndex < _book.Styles.Length
                    && _book.Styles[styleIndex].IsDateFormat)
                {
                    try { return DateTime.FromOADate(num); }
                    catch { return num; }  // out-of-range serial — fall back to double
                }
                return num;
        }
    }

    /// <summary>
    /// Parses the column-letter portion of an A1 address: "A1" → 1, "B23" → 2, "AA1" → 27.
    /// </summary>
    private static int ParseCellColumn(string a1)
    {
        int col = 0;
        for (int i = 0; i < a1.Length; i++)
        {
            var c = a1[i];
            if (c >= 'A' && c <= 'Z') col = col * 26 + (c - 'A' + 1);
            else break;
        }
        return col;
    }

    private void ClearRowBuffer()
    {
        for (int i = 0; i < _maxColumnSeen; i++) _cellValues[i] = null;
        _maxColumnSeen = 0;
    }

    private void EnsureCapacity(int column)
    {
        if (column <= _cellValues.Length) return;
        int newSize = _cellValues.Length;
        while (newSize < column) newSize *= 2;
        Array.Resize(ref _cellValues, newSize);
    }

    private void BuildHeaderIndex()
    {
        if (!_hasCurrent) return;
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _maxColumnSeen; i++)
        {
            var v = _cellValues[i];
            if (v is string s && !string.IsNullOrEmpty(s) && !map.ContainsKey(s))
                map[s] = i;  // 0-based
        }
        _sheet.SetHeaderIndex(map);
    }

    public void Reset() => throw new NotSupportedException("Sheet rows are forward-only.");

    public void Dispose()
    {
        _xml.Dispose();
        _stream.Dispose();
    }
}

/// <summary>
/// One row's cells. <strong>This is a transient struct — do not retain it across enumeration
/// steps. The underlying buffer is reused on each <c>MoveNext</c>.</strong>
///
/// To use a row's data later, copy out the specific values you need:
/// <code>
/// var copied = new List&lt;(int Id, string Name)&gt;();
/// foreach (var row in sheet.Rows)
/// {
///     copied.Add((row.GetInt32("Id"), row.GetString("Name")));
/// }
/// </code>
///
/// (We use <c>readonly struct</c> rather than <c>ref struct</c> so that the type can be
/// used with standard <see cref="System.Collections.Generic.IEnumerable{T}"/> patterns.
/// The "don't retain" rule is enforced by convention, not the compiler.)
/// </summary>
public readonly struct RowReader
{
    private readonly object?[] _buffer;
    private readonly int _count;
    public readonly int RowNumber;
    private readonly SheetReader _sheet;

    internal RowReader(object?[] buffer, int count, int rowNumber, SheetReader sheet)
    {
        _buffer = buffer;
        _count = count;
        RowNumber = rowNumber;
        _sheet = sheet;
    }

    /// <summary>Number of cells in this row (the highest column index seen).</summary>
    public int Count => _count;

    /// <summary>Raw value access by 0-based column index. Returns null for missing cells.</summary>
    public object? this[int column0Based]
        => column0Based >= 0 && column0Based < _count ? _buffer[column0Based] : null;

    /// <summary>Raw value access by header name (requires TreatFirstRowAsHeaders).</summary>
    public object? this[string columnName]
    {
        get
        {
            if (_sheet.HeaderIndex is null)
                throw new InvalidOperationException("Header index not built; enable TreatFirstRowAsHeaders.");
            return _sheet.HeaderIndex.TryGetValue(columnName, out var idx) ? this[idx] : null;
        }
    }

    // ---- Typed accessors — throw on missing/wrong type --------------------------

    public string GetString(int col) => Get<string>(col);
    public string GetString(string name) => Get<string>(name);

    public int GetInt32(int col) => GetNumeric<int>(col, Convert.ToInt32);
    public int GetInt32(string name) => GetNumeric<int>(name, Convert.ToInt32);

    public long GetInt64(int col) => GetNumeric<long>(col, Convert.ToInt64);
    public long GetInt64(string name) => GetNumeric<long>(name, Convert.ToInt64);

    public double GetDouble(int col) => GetNumeric<double>(col, Convert.ToDouble);
    public double GetDouble(string name) => GetNumeric<double>(name, Convert.ToDouble);

    public decimal GetDecimal(int col) => GetNumeric<decimal>(col, Convert.ToDecimal);
    public decimal GetDecimal(string name) => GetNumeric<decimal>(name, Convert.ToDecimal);

    public bool GetBool(int col) => Get<bool>(col);
    public bool GetBool(string name) => Get<bool>(name);

    public DateTime GetDateTime(int col)
    {
        var v = this[col];
        if (v is DateTime dt) return dt;
        if (v is double d) return DateTime.FromOADate(d);
        throw new InvalidCastException($"Cell at column {col + 1} is not a date.");
    }
    public DateTime GetDateTime(string name)
    {
        var v = this[name];
        if (v is DateTime dt) return dt;
        if (v is double d) return DateTime.FromOADate(d);
        throw new InvalidCastException($"Cell '{name}' is not a date.");
    }

    public T Get<T>(int col)
    {
        var v = this[col] ?? throw new InvalidOperationException($"Cell at column {col + 1} is null.");
        if (v is T t) return t;
        try { return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); }
        catch { throw new InvalidCastException($"Cell at column {col + 1} ({v.GetType().Name}) cannot be converted to {typeof(T).Name}."); }
    }

    public T Get<T>(string name)
    {
        var v = this[name] ?? throw new InvalidOperationException($"Cell '{name}' is null or missing.");
        if (v is T t) return t;
        try { return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); }
        catch { throw new InvalidCastException($"Cell '{name}' ({v.GetType().Name}) cannot be converted to {typeof(T).Name}."); }
    }

    // ---- TryGet pattern --------------------------------------------------------

    public bool TryGet<T>(int col, out T value)
    {
        var v = this[col];
        if (v is T t) { value = t; return true; }
        if (v is not null)
        {
            try { value = (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); return true; }
            catch { }
        }
        value = default!;
        return false;
    }

    public bool TryGet<T>(string name, out T value)
    {
        var v = this[name];
        if (v is T t) { value = t; return true; }
        if (v is not null)
        {
            try { value = (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); return true; }
            catch { }
        }
        value = default!;
        return false;
    }

    // ---- Helpers ---------------------------------------------------------------

    private T GetNumeric<T>(int col, Func<object, T> convert)
    {
        var v = this[col] ?? throw new InvalidOperationException($"Cell at column {col + 1} is null.");
        try { return convert(v); }
        catch (Exception ex) { throw new InvalidCastException($"Cell at column {col + 1} ({v.GetType().Name}) cannot be converted to {typeof(T).Name}.", ex); }
    }

    private T GetNumeric<T>(string name, Func<object, T> convert)
    {
        var v = this[name] ?? throw new InvalidOperationException($"Cell '{name}' is null or missing.");
        try { return convert(v); }
        catch (Exception ex) { throw new InvalidCastException($"Cell '{name}' ({v.GetType().Name}) cannot be converted to {typeof(T).Name}.", ex); }
    }
}
