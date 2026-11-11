using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Model;

/// <summary>
/// One-liner xlsx export for the common cases. These are thin convenience wrappers over
/// <see cref="XlsxWriter"/>; for multi-sheet workbooks or full control, use <see cref="XlsxExport"/>
/// or <see cref="XlsxWriter"/> directly.
/// </summary>
public static class XlsxExtensions
{
    // Since 2.1.0 every export also takes XlsxWriterOptions (encryption, the temp folder or memory, inline
    // strings). The older overloads that take a configure lambda have OverloadResolutionPriority(1), so a call
    // passing a literal null (ToXlsx(path, null)) binds to them, as before 2.1.0, and stays unambiguous.

    // ---- IEnumerable<T> → xlsx -----------------------------------------------------

    /// <summary>Exports the sequence to a single-sheet xlsx file using reflection on T's public properties.</summary>
    public static void ToXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this IEnumerable<T> source, string path)
        => ToXlsx(source, path, configure: null);

    /// <summary>Exports the sequence with a configuration lambda. Configuration overrides any attributes on T.</summary>
    [OverloadResolutionPriority(1)]
    public static void ToXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this IEnumerable<T> source, string path, Action<XlsxExportConfig<T>>? configure)
        => ToXlsx(source, path, options: null, configure);

    /// <summary>
    /// Exports the sequence to a single-sheet xlsx file with writer options (since 2.1.0): encryption, the temp
    /// folder or memory, inline strings and the default date formats. Null options are the defaults.
    /// </summary>
    public static void ToXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this IEnumerable<T> source, string path, XlsxWriterOptions? options, Action<XlsxExportConfig<T>>? configure = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));

        var cfg = new XlsxExportConfig<T>();
        configure?.Invoke(cfg);

        using var writer = XlsxWriter.Create(path, options);
        ExportEngine.WriteEnumerableSheet(writer, source, cfg);
        writer.Save();
    }

    /// <summary>Exports the sequence to a stream. The stream is not closed by this method.</summary>
    [OverloadResolutionPriority(1)]
    public static void ToXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this IEnumerable<T> source, Stream output, Action<XlsxExportConfig<T>>? configure = null)
        => ToXlsx(source, output, options: null, configure);

    /// <summary>
    /// Exports the sequence to a stream with writer options (since 2.1.0). The stream is not closed by this
    /// method. Null options are the defaults.
    /// </summary>
    public static void ToXlsx<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this IEnumerable<T> source, Stream output, XlsxWriterOptions? options, Action<XlsxExportConfig<T>>? configure = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (output is null) throw new ArgumentNullException(nameof(output));

        var cfg = new XlsxExportConfig<T>();
        configure?.Invoke(cfg);

        using var writer = XlsxWriter.Create(output, options);
        ExportEngine.WriteEnumerableSheet(writer, source, cfg);
        writer.Save();
    }

    // ---- DataTable → xlsx ----------------------------------------------------------

    /// <summary>Exports the DataTable to a single-sheet xlsx file. The sheet name defaults to the table's name (or "Sheet1").</summary>
    public static void ToXlsx(this DataTable table, string path)
        => ToXlsx(table, path, configure: null);

    /// <summary>Exports the DataTable with a configuration lambda.</summary>
    [OverloadResolutionPriority(1)]
    public static void ToXlsx(this DataTable table, string path, Action<DataTableExportConfig>? configure)
        => ToXlsx(table, path, options: null, configure);

    /// <summary>
    /// Exports the DataTable to a single-sheet xlsx file with writer options (since 2.1.0). Null options are
    /// the defaults.
    /// </summary>
    public static void ToXlsx(this DataTable table, string path, XlsxWriterOptions? options, Action<DataTableExportConfig>? configure = null)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));

        var cfg = NewConfig(table, configure);
        using var writer = XlsxWriter.Create(path, options);
        ExportEngine.WriteDataTableSheet(writer, table, cfg);
        writer.Save();
    }

    /// <summary>Exports the DataTable to a stream (since 2.1.0). The stream is not closed by this method.</summary>
    public static void ToXlsx(this DataTable table, Stream output)
        => ToXlsx(table, output, configure: null);

    /// <summary>Exports the DataTable to a stream with a configuration lambda (since 2.1.0). The stream is not closed.</summary>
    [OverloadResolutionPriority(1)]
    public static void ToXlsx(this DataTable table, Stream output, Action<DataTableExportConfig>? configure)
        => ToXlsx(table, output, options: null, configure);

    /// <summary>
    /// Exports the DataTable to a stream with writer options (since 2.1.0). The stream is not closed. Null
    /// options are the defaults.
    /// </summary>
    public static void ToXlsx(this DataTable table, Stream output, XlsxWriterOptions? options, Action<DataTableExportConfig>? configure = null)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (output is null) throw new ArgumentNullException(nameof(output));

        var cfg = NewConfig(table, configure);
        using var writer = XlsxWriter.Create(output, options);
        ExportEngine.WriteDataTableSheet(writer, table, cfg);
        writer.Save();
    }

    private static DataTableExportConfig NewConfig(DataTable table, Action<DataTableExportConfig>? configure)
    {
        var cfg = new DataTableExportConfig();
        if (!string.IsNullOrEmpty(table.TableName)) cfg.SheetName(table.TableName);
        configure?.Invoke(cfg);
        return cfg;
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
    private readonly XlsxWriterOptions? _options;
    private readonly List<Action<XlsxWriter>> _sheetActions = new();

    /// <summary>An export that saves to the given xlsx file path.</summary>
    public XlsxExport(string path) : this(path, options: null) { }

    /// <summary>An export that saves to the given stream (not closed by Save).</summary>
    public XlsxExport(Stream output) : this(output, options: null) { }

    /// <summary>
    /// An export that saves to the given xlsx file path with writer options (since 2.1.0): encryption, the
    /// temp folder or memory, inline strings and the default date formats. Null options are the defaults.
    /// </summary>
    public XlsxExport(string path, XlsxWriterOptions? options)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        _path = path;
        _options = options;
    }

    /// <summary>
    /// An export that saves to the given stream with writer options (since 2.1.0). The stream is not closed by
    /// Save. Null options are the defaults.
    /// </summary>
    public XlsxExport(Stream output, XlsxWriterOptions? options)
    {
        _stream = output ?? throw new ArgumentNullException(nameof(output));
        _options = options;
    }

    /// <summary>Adds a sheet sourced from an IEnumerable&lt;T&gt;.</summary>
    public XlsxExport AddSheet<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string sheetName, IEnumerable<T> source, Action<XlsxExportConfig<T>>? configure = null)
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
            ? XlsxWriter.Create(_path, _options)
            : XlsxWriter.Create(_stream!, _options);

        foreach (var action in _sheetActions)
            action(writer);

        writer.Save();
    }
}
