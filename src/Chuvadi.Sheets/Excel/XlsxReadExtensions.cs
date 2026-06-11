using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// One-liner xlsx read methods. Thin convenience wrappers over <see cref="XlsxReader"/>.
/// For streaming over very large files or fine-grained control, use <c>XlsxReader.Open</c>
/// directly.
/// </summary>
public static class XlsxReadExtensions
{
    // ---- xlsx → DataSet -----------------------------------------------------------

    /// <summary>Reads every sheet into a DataSet (one DataTable per sheet). All cell values are object?.</summary>
    public static DataSet ReadXlsx(this string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        using var reader = XlsxReader.Open(path);
        var ds = new DataSet();
        foreach (var sheet in reader.Sheets)
            ds.Tables.Add(ReadSheetIntoDataTable(reader.Sheet(sheet.Name), sheet.Name));
        return ds;
    }

    // ---- xlsx → DataTable ---------------------------------------------------------

    /// <summary>Reads a specific sheet into a DataTable.</summary>
    public static DataTable ReadXlsxSheet(this string path, string sheetName)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        if (string.IsNullOrEmpty(sheetName)) throw new ArgumentException("Sheet name required.", nameof(sheetName));
        using var reader = XlsxReader.Open(path);
        var sheet = reader.Sheet(sheetName);
        return ReadSheetIntoDataTable(sheet, sheetName);
    }

    /// <summary>Reads the first sheet into a DataTable.</summary>
    public static DataTable ReadXlsxSheet(this string path)
    {
        using var reader = XlsxReader.Open(path);
        if (reader.Sheets.Count == 0) throw new XlsxFormatException("Workbook has no sheets.");
        var first = reader.Sheets[0];
        return ReadSheetIntoDataTable(reader.Sheet(first.Name), first.Name);
    }

    // ---- xlsx → IEnumerable<T> ----------------------------------------------------

    /// <summary>Reads the first sheet into a list of T, mapping by header name (case-insensitive) to public properties.</summary>
    public static List<T> ReadXlsx<T>(this string path) where T : new()
        => ReadXlsx<T>(path, sheetName: null);

    /// <summary>Reads a specific sheet into a list of T.</summary>
    public static List<T> ReadXlsx<T>(this string path, string? sheetName) where T : new()
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));

        using var reader = XlsxReader.Open(path);
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

    private static DataTable ReadSheetIntoDataTable(SheetReader sheet, string tableName)
    {
        var dt = new DataTable(tableName);
        bool headersAdded = false;

        foreach (var row in sheet.Rows)
        {
            // First yielded row (after header consumption) — establish column count.
            if (!headersAdded)
            {
                // Use the sheet's HeaderIndex if present (preserves header text); otherwise generic.
                if (sheet.HeaderIndex is not null && sheet.HeaderIndex.Count > 0)
                {
                    var ordered = new string[sheet.HeaderIndex.Count];
                    foreach (var kv in sheet.HeaderIndex) ordered[kv.Value] = kv.Key;
                    foreach (var name in ordered) dt.Columns.Add(name ?? string.Empty, typeof(object));
                }
                else
                {
                    for (int i = 0; i < row.Count; i++) dt.Columns.Add($"Column{i + 1}", typeof(object));
                }
                headersAdded = true;
            }

            // Ensure DataTable has enough columns for this row.
            while (dt.Columns.Count < row.Count)
                dt.Columns.Add($"Column{dt.Columns.Count + 1}", typeof(object));

            var newRow = dt.NewRow();
            for (int i = 0; i < dt.Columns.Count; i++)
                newRow[i] = row[i] ?? (object)DBNull.Value;
            dt.Rows.Add(newRow);
        }

        return dt;
    }

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
