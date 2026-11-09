using System;
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
        if (sheetName is null)
        {
            if (reader.Sheets.Count == 0) throw new XlsxFormatException("Workbook has no sheets.");
            sheetName = reader.Sheets[0].Name;
        }
        var sheet = reader.Sheet(sheetName);
        return ReadSheetIntoDataTable(sheet, sheetName);
    }

    /// <summary>Reads the first sheet into a DataTable.</summary>
    public static DataTable ReadXlsxSheet(this string path) => ReadXlsxSheet(path, null, null, null);

    // ---- xlsx → IEnumerable<T> ----------------------------------------------------

    /// <summary>Reads the first sheet into a list of T, mapping by header name (case-insensitive) to public properties.</summary>
    public static List<T> ReadXlsx<T>(this string path) where T : new()
        => ReadXlsx<T>(path, sheetName: null);

    /// <summary>Reads a specific sheet into a list of T.</summary>
    public static List<T> ReadXlsx<T>(this string path, string? sheetName) where T : new()
        => ReadXlsx<T>(path, sheetName, null, null);

    /// <summary>
    /// Reads a sheet (the first when <paramref name="sheetName"/> is null) into a list of T, with a
    /// password and reader options (limits, cancellation). The first row is always the header row.
    /// </summary>
    public static List<T> ReadXlsx<T>(this string path, string? sheetName, string? password, XlsxReaderOptions? options) where T : new()
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));

        using var reader = XlsxReader.Open(path, password, WithHeaders(options));
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
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool headersAdded = false;

        foreach (var row in sheet.Rows)
        {
            // The header row has been consumed before the first data row is yielded.
            if (!headersAdded)
            {
                AddHeaderColumns(dt, sheet, used);
                headersAdded = true;
            }

            // A data row wider than the header row gets generated column names.
            while (dt.Columns.Count < row.Count)
                AddColumn(dt, used, null, dt.Columns.Count);

            var newRow = dt.NewRow();
            for (int i = 0; i < dt.Columns.Count; i++)
                newRow[i] = row[i] ?? (object)DBNull.Value;
            dt.Rows.Add(newRow);
        }

        // A sheet with a header row and no data rows still gets its columns.
        if (!headersAdded) AddHeaderColumns(dt, sheet, used);

        return dt;
    }

    /// <summary>
    /// One column per header cell, by position: a text header keeps its text exactly; a number,
    /// date, boolean or error header is named by its display text; an empty header is "ColumnN"
    /// (N = its 1-based position). Names are made unique as in Excel tables (see <see cref="AddColumn"/>).
    /// </summary>
    private static void AddHeaderColumns(DataTable dt, SheetReader sheet, HashSet<string> used)
    {
        var values = sheet.HeaderValues;
        if (values is null) return;
        var styles = sheet.HeaderStyles!;
        var book = sheet.Book;
        for (int i = 0; i < values.Length; i++)
        {
            string? name = values[i] switch
            {
                null => null,
                string s => s.Length == 0 ? null : s,
                var v => NonEmpty(NumberFormatter.Format(v, NullIfEmpty(book.DisplayCode(styles[i])), book.DisplayOptions)),
            };
            AddColumn(dt, used, name, i);
        }
    }

    /// <summary>
    /// Adds a column named <paramref name="name"/>, or "ColumnN" when it is null. A name already
    /// in use (ignoring case, as <see cref="SheetReader"/> header lookups and DataTable do) gets
    /// the smallest number from 2 up that makes it unique: Qty, Qty2, Qty3.
    /// </summary>
    private static void AddColumn(DataTable dt, HashSet<string> used, string? name, int index)
    {
        var baseName = name ?? "Column" + (index + 1).ToString(CultureInfo.InvariantCulture);
        var unique = baseName;
        for (int n = 2; !used.Add(unique); n++)
            unique = baseName + n.ToString(CultureInfo.InvariantCulture);
        dt.Columns.Add(unique, typeof(object));
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static string? NonEmpty(string s) => s.Length == 0 ? null : s;

    private static List<T> MapSheetToList<T>(SheetReader sheet) where T : new()
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

    private static List<(string Header, PropertyInfo Prop)> BuildPropertyMap<T>()
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
