using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// One-liner xlsx export for the common cases. These are thin convenience wrappers over
/// <see cref="XlsxWriter"/>; for multi-sheet workbooks or full control, use <see cref="XlsxExport"/>
/// or <see cref="XlsxWriter"/> directly.
/// </summary>
public static class XlsxExtensions
{
    // ---- IEnumerable<T> → xlsx -----------------------------------------------------

    /// <summary>Exports the sequence to a single-sheet xlsx file using reflection on T's public properties.</summary>
    public static void ToXlsx<T>(this IEnumerable<T> source, string path)
        => ToXlsx(source, path, configure: null);

    /// <summary>Exports the sequence with a configuration lambda. Configuration overrides any attributes on T.</summary>
    public static void ToXlsx<T>(this IEnumerable<T> source, string path, Action<XlsxExportConfig<T>>? configure)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));

        var cfg = new XlsxExportConfig<T>();
        configure?.Invoke(cfg);

        using var writer = XlsxWriter.Create(path);
        ExportEngine.WriteEnumerableSheet(writer, source, cfg);
        writer.Save();
    }

    /// <summary>Exports the sequence to a stream. The stream is not closed by this method.</summary>
    public static void ToXlsx<T>(this IEnumerable<T> source, Stream output, Action<XlsxExportConfig<T>>? configure = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (output is null) throw new ArgumentNullException(nameof(output));

        var cfg = new XlsxExportConfig<T>();
        configure?.Invoke(cfg);

        using var writer = XlsxWriter.Create(output);
        ExportEngine.WriteEnumerableSheet(writer, source, cfg);
        writer.Save();
    }

    // ---- DataTable → xlsx ----------------------------------------------------------

    /// <summary>Exports the DataTable to a single-sheet xlsx file. The sheet name defaults to the table's name (or "Sheet1").</summary>
    public static void ToXlsx(this DataTable table, string path)
        => ToXlsx(table, path, configure: null);

    /// <summary>Exports the DataTable with a configuration lambda.</summary>
    public static void ToXlsx(this DataTable table, string path, Action<DataTableExportConfig>? configure)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));

        var cfg = new DataTableExportConfig();
        if (!string.IsNullOrEmpty(table.TableName)) cfg.SheetName(table.TableName);
        configure?.Invoke(cfg);

        using var writer = XlsxWriter.Create(path);
        ExportEngine.WriteDataTableSheet(writer, table, cfg);
        writer.Save();
    }
}

/// <summary>
/// Multi-sheet workbook export. Add multiple sheets from different sources, then save.
///
/// <code>
/// new XlsxExport("multi.xlsx")
///     .AddSheet("Patients", patients,    cfg =&gt; cfg.AutoFilter().FreezeHeaderRow())
///     .AddSheet("Visits",   visitsTable)
///     .AddSheet("Billing",  billingList, cfg =&gt; cfg.AsTable("Billing", TableStyle.Medium2))
///     .Save();
/// </code>
/// </summary>
public sealed class XlsxExport
{
    private readonly string? _path;
    private readonly Stream? _stream;
    private readonly List<Action<XlsxWriter>> _sheetActions = new();

    public XlsxExport(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        _path = path;
    }

    public XlsxExport(Stream output)
    {
        _stream = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>Adds a sheet sourced from an IEnumerable&lt;T&gt;.</summary>
    public XlsxExport AddSheet<T>(string sheetName, IEnumerable<T> source, Action<XlsxExportConfig<T>>? configure = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        _sheetActions.Add(writer =>
        {
            var cfg = new XlsxExportConfig<T>();
            cfg.SheetName(sheetName);
            configure?.Invoke(cfg);
            ExportEngine.WriteEnumerableSheet(writer, source, cfg);
        });
        return this;
    }

    /// <summary>Adds a sheet sourced from a DataTable.</summary>
    public XlsxExport AddSheet(string sheetName, DataTable table, Action<DataTableExportConfig>? configure = null)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        _sheetActions.Add(writer =>
        {
            var cfg = new DataTableExportConfig();
            cfg.SheetName(sheetName);
            configure?.Invoke(cfg);
            ExportEngine.WriteDataTableSheet(writer, table, cfg);
        });
        return this;
    }

    public void Save()
    {
        if (_sheetActions.Count == 0)
            throw new InvalidOperationException("XlsxExport must have at least one sheet before Save.");

        using var writer = _path is not null
            ? XlsxWriter.Create(_path)
            : XlsxWriter.Create(_stream!);

        foreach (var action in _sheetActions)
            action(writer);

        writer.Save();
    }
}
