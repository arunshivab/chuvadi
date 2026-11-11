using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Reflection;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Model;

/// <summary>
/// One-liner xlsx read methods. Thin convenience wrappers over <see cref="XlsxReader"/>.
/// For streaming over very large files or fine-grained control, use <c>XlsxReader.Open</c>
/// directly.
/// </summary>
public static class XlsxReadExtensions
{
    // ---- xlsx → DataSet -----------------------------------------------------------

    /// <summary>Reads every sheet into a DataSet (one DataTable per sheet). All cell values are object?.</summary>
    public static DataSet ReadXlsx(this string path) => ReadXlsx(path, null, null);

    /// <summary>
    /// Reads every sheet into a DataSet, with a password (ignored for unencrypted files) and
    /// reader options (limits, cancellation, culture). The first row is always the header row.
    /// </summary>
    public static DataSet ReadXlsx(this string path, string? password, XlsxReaderOptions? options)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        using var reader = XlsxReader.Open(path, password, WithHeaders(options));
        return ReadDataSet(reader);
    }

    /// <summary>
    /// Reads every sheet of an xlsx stream into a DataSet (since 2.1.0): the same result as
    /// <see cref="ReadXlsx(string)"/> on the same bytes. The stream is not closed.
    /// </summary>
    public static DataSet ReadXlsx(this Stream input) => ReadXlsx(input, null, null);

    /// <summary>
    /// Reads every sheet of an xlsx stream into a DataSet, with a password and reader options (since 2.1.0;
    /// see <see cref="ReadXlsx(string, string?, XlsxReaderOptions?)"/>). The stream is not closed.
    /// </summary>
    public static DataSet ReadXlsx(this Stream input, string? password, XlsxReaderOptions? options)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        using var reader = XlsxReader.Open(input, password, WithHeaders(options));
        return ReadDataSet(reader);
    }

    private static DataSet ReadDataSet(XlsxReader reader)
    {
        var ds = new DataSet();
        foreach (var sheet in reader.Sheets)
            ds.Tables.Add(ReadSheetIntoDataTable(reader.Sheet(sheet.Name), sheet.Name));
        return ds;
    }

    // ---- xlsx → DataTable ---------------------------------------------------------

    /// <summary>Reads a specific sheet into a DataTable.</summary>
    public static DataTable ReadXlsxSheet(this string path, string sheetName)
        => ReadXlsxSheet(path, sheetName, null, null);

    /// <summary>
    /// Reads a specific sheet (or the first sheet when <paramref name="sheetName"/> is null) into
    /// a DataTable, with a password and reader options. The first row is always the header row.
    /// </summary>
    public static DataTable ReadXlsxSheet(this string path, string? sheetName, string? password, XlsxReaderOptions? options)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        using var reader = XlsxReader.Open(path, password, WithHeaders(options));
        return ReadDataTable(reader, sheetName);
    }

    /// <summary>Reads the first sheet into a DataTable.</summary>
    public static DataTable ReadXlsxSheet(this string path) => ReadXlsxSheet(path, null, null, null);

    /// <summary>
    /// Reads the first sheet of an xlsx stream into a DataTable (since 2.1.0). The stream is not closed.
    /// </summary>
    public static DataTable ReadXlsxSheet(this Stream input) => ReadXlsxSheet(input, null, null, null);

    /// <summary>Reads a specific sheet of an xlsx stream into a DataTable (since 2.1.0). The stream is not closed.</summary>
    public static DataTable ReadXlsxSheet(this Stream input, string sheetName)
        => ReadXlsxSheet(input, sheetName, null, null);

    /// <summary>
    /// Reads a specific sheet (or the first when <paramref name="sheetName"/> is null) of an xlsx stream into
    /// a DataTable, with a password and reader options (since 2.1.0; see
    /// <see cref="ReadXlsxSheet(string, string?, string?, XlsxReaderOptions?)"/>). The stream is not closed.
    /// </summary>
    public static DataTable ReadXlsxSheet(this Stream input, string? sheetName, string? password, XlsxReaderOptions? options)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        using var reader = XlsxReader.Open(input, password, WithHeaders(options));
        return ReadDataTable(reader, sheetName);
    }

    private static DataTable ReadDataTable(XlsxReader reader, string? sheetName)
    {
        if (sheetName is null)
        {
            if (reader.Sheets.Count == 0) throw new XlsxFormatException("Workbook has no sheets.");
            sheetName = reader.Sheets[0].Name;
        }
        var sheet = reader.Sheet(sheetName);
        return ReadSheetIntoDataTable(sheet, sheetName);
    }

    // ---- xlsx → IEnumerable<T> ----------------------------------------------------

    /// <summary>Reads the first sheet into a list of T, mapping by header name (case-insensitive) to public properties.</summary>
    public static List<T> ReadXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this string path) where T : new()
        => ReadXlsx<T>(path, sheetName: null);

    /// <summary>Reads a specific sheet into a list of T.</summary>
    public static List<T> ReadXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this string path, string? sheetName) where T : new()
        => ReadXlsx<T>(path, sheetName, null, null);

    /// <summary>
    /// Reads a sheet (the first when <paramref name="sheetName"/> is null) into a list of T, with a
    /// password and reader options (limits, cancellation). The first row is always the header row.
    /// </summary>
    public static List<T> ReadXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this string path, string? sheetName, string? password, XlsxReaderOptions? options) where T : new()
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));

        using var reader = XlsxReader.Open(path, password, WithHeaders(options));
        return ReadList<T>(reader, sheetName);
    }

    /// <summary>
    /// Reads the first sheet of an xlsx stream into a list of T (since 2.1.0). The stream is not closed.
    /// </summary>
    public static List<T> ReadXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this Stream input) where T : new()
        => ReadXlsx<T>(input, sheetName: null);

    /// <summary>Reads a specific sheet of an xlsx stream into a list of T (since 2.1.0). The stream is not closed.</summary>
    public static List<T> ReadXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this Stream input, string? sheetName) where T : new()
        => ReadXlsx<T>(input, sheetName, null, null);

    /// <summary>
    /// Reads a sheet (the first when <paramref name="sheetName"/> is null) of an xlsx stream into a list of T,
    /// with a password and reader options (since 2.1.0; see
    /// <see cref="ReadXlsx{T}(string, string?, string?, XlsxReaderOptions?)"/>). The stream is not closed.
    /// </summary>
    public static List<T> ReadXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this Stream input, string? sheetName, string? password, XlsxReaderOptions? options) where T : new()
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        using var reader = XlsxReader.Open(input, password, WithHeaders(options));
        return ReadList<T>(reader, sheetName);
    }

    private static List<T> ReadList<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(XlsxReader reader, string? sheetName) where T : new()
    {
        SheetReader sheet;
        if (sheetName is null)
        {
            if (reader.Sheets.Count == 0) throw new XlsxFormatException("Workbook has no sheets.");
            sheet = reader.Sheet(reader.Sheets[0].Name);
        }
        else
        {
            sheet = reader.Sheet(sheetName);
        }

        return MapSheetToList<T>(sheet);
    }

    // ---- Helpers ------------------------------------------------------------------

    /// <summary>The caller's options (copied) with header mapping on, as these methods require.</summary>
    private static XlsxReaderOptions WithHeaders(XlsxReaderOptions? options)
    {
        var o = options?.Clone() ?? new XlsxReaderOptions();
        o.TreatFirstRowAsHeaders = true;
        return o;
    }

    private static DataTable ReadSheetIntoDataTable(SheetReader sheet, string tableName)
    {
        var dt = new DataTable(tableName);
        ColumnNamer? namer = null;

        foreach (var row in sheet.Rows)
        {
            // The header row has been consumed before the first data row is yielded.
            namer ??= AddHeaderColumns(dt, sheet);

            // A data row wider than the header row: the extra columns are named as empty headers.
            while (dt.Columns.Count < row.Count)
                dt.Columns.Add(namer.Name(null), typeof(object));

            var newRow = dt.NewRow();
            for (int i = 0; i < dt.Columns.Count; i++)
                newRow[i] = row[i] ?? (object)DBNull.Value;
            dt.Rows.Add(newRow);
        }

        // A sheet with a header row and no data rows still gets its columns.
        if (namer is null) AddHeaderColumns(dt, sheet);

        return dt;
    }

    /// <summary>
    /// One column per header cell, by position. A header's name is its text exactly, or for a number,
    /// date, boolean or error header its display text; an empty header has none. The columns are then
    /// named as Excel names a table's columns (see <see cref="ColumnNamer"/>).
    /// </summary>
    private static ColumnNamer AddHeaderColumns(DataTable dt, SheetReader sheet)
    {
        var values = sheet.HeaderValues ?? Array.Empty<object?>();
        var styles = sheet.HeaderStyles ?? Array.Empty<int>();
        var book = sheet.Book;
        var names = new string?[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            names[i] = values[i] switch
            {
                null => null,
                string s => s.Length == 0 ? null : s,
                var v => NonEmpty(NumberFormatter.Format(v, NullIfEmpty(book.DisplayCode(styles[i])), book.DisplayOptions)),
            };
        }

        var namer = new ColumnNamer(names);
        foreach (var name in names)
            dt.Columns.Add(namer.Name(name), typeof(object));
        return namer;
    }

    /// <summary>
    /// Names DataTable columns as Excel names the columns of a table made from a range (Insert &gt; Table),
    /// as observed on 22 header rows (2.0.4, D-292):
    /// <list type="number">
    /// <item>Every header name in the row is reserved for its first occurrence, ignoring case, wherever it is.</item>
    /// <item>Left to right, each repeat and each empty header gets a made-up name from one shared counter that
    /// starts at 1: an empty header is "Column" + the counter, a repeat is its name + the counter but never 1.
    /// A name already used, ignoring case, moves the number on until it is free; the counter then continues
    /// from the number used plus 1.</item>
    /// </list>
    /// Examples: Qty, Qty, Qty2 gives Qty, Qty3, Qty2; Name, (empty), Town gives Name, Column1, Town;
    /// Qty, Qty, Town, Town gives Qty, Qty2, Town, Town3.
    /// </summary>
    private sealed class ColumnNamer
    {
        private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _kept = new(StringComparer.OrdinalIgnoreCase);
        private int _counter = 1;

        public ColumnNamer(IEnumerable<string?> headerNames)
        {
            foreach (var n in headerNames)
                if (n is not null) _used.Add(n);
        }

        /// <summary>The column name for a header (null for an empty header), in left-to-right order.</summary>
        public string Name(string? header)
        {
            if (header is not null && _kept.Add(header)) return header;   // first occurrence keeps its name
            var baseName = header ?? "Column";
            int k = header is null ? _counter : Math.Max(_counter, 2);
            string name;
            while (!_used.Add(name = baseName + k.ToString(CultureInfo.InvariantCulture))) k++;
            _counter = k + 1;
            return name;
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static string? NonEmpty(string s) => s.Length == 0 ? null : s;

    private static List<T> MapSheetToList<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(SheetReader sheet) where T : new()
    {
        var result = new List<T>();
        var props = BuildPropertyMap<T>();

        foreach (var row in sheet.Rows)
        {
            var item = new T();
            if (sheet.HeaderIndex is null) continue;

            foreach (var (headerName, propInfo) in props)
            {
                if (!sheet.HeaderIndex.TryGetValue(headerName, out var colIdx)) continue;
                var raw = row[colIdx];
                if (raw is null) continue;

                try
                {
                    var converted = ConvertToPropertyType(raw, propInfo.PropertyType);
                    propInfo.SetValue(item, converted);
                }
                catch
                {
                    // Skip malformed cell; leave property at default.
                }
            }
            result.Add(item);
        }

        return result;
    }

    private static List<(string Header, PropertyInfo Prop)> BuildPropertyMap<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>()
    {
        var list = new List<(string, PropertyInfo)>();
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite || p.GetIndexParameters().Length > 0) continue;
            if (p.GetCustomAttribute<ColumnIgnoreAttribute>() is not null) continue;

            var colAttr = p.GetCustomAttribute<ColumnAttribute>();
            var header = !string.IsNullOrEmpty(colAttr?.Header) ? colAttr!.Header! : p.Name;
            list.Add((header, p));
        }
        return list;
    }

    private static object? ConvertToPropertyType(object raw, Type targetType)
    {
        // Strip Nullable<>.
        var nonNull = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (nonNull.IsInstanceOfType(raw)) return raw;

        // DateTime from double serial.
        if (nonNull == typeof(DateTime) && raw is double d) return DateTime.FromOADate(d);

        // Boolean from numeric.
        if (nonNull == typeof(bool) && raw is double db) return db != 0;

        // Everything else: defer to ChangeType.
        return Convert.ChangeType(raw, nonNull, System.Globalization.CultureInfo.InvariantCulture);
    }
}
