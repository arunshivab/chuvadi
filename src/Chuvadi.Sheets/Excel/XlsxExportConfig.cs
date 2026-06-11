using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Fluent configuration shared by both <see cref="XlsxExportConfig{T}"/> and
/// <see cref="DataTableExportConfig"/>. Holds settings that are independent of how columns
/// are sourced (from a type's properties vs from a <c>DataTable</c>'s schema).
/// </summary>
public abstract class ExportConfigBase
{
    internal string _sheetName = "Sheet1";
    internal CellStyle? _headerStyle;
    internal bool _autoFilter = false;
    internal bool _freezeHeaderRow = false;
    internal bool _asTable = false;
    internal string? _tableName;
    internal TableStyle _tableStyle = TableStyle.Medium2;
    internal string? _pageHeader;
    internal string? _pageFooter;

    /// <summary>Per-column-name overrides (works for both T-property names and DataTable column names).</summary>
    internal Dictionary<string, ColumnOverride> _columnOverrides = new(StringComparer.Ordinal);
    internal HashSet<string> _ignoredColumns = new(StringComparer.Ordinal);

    internal sealed class ColumnOverride
    {
        public string? Header;
        public double Width;
        public string? Format;
        public CellStyle? Style;
    }

    protected void SetSheetName(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Sheet name required.", nameof(name));
        _sheetName = name;
    }

    protected void SetHeaderStyle(CellStyle style) => _headerStyle = style;
    protected void SetAutoFilter(bool on) => _autoFilter = on;
    protected void SetFreezeHeaderRow(bool on) => _freezeHeaderRow = on;
    protected void SetPageHeaderFooter(string? header, string? footer)
    {
        _pageHeader = header;
        _pageFooter = footer;
    }

    protected void SetAsTable(string? name, TableStyle style)
    {
        _asTable = true;
        _tableName = name;
        _tableStyle = style;
    }

    protected void OverrideColumn(string propertyOrColumnName, string? header, double width, string? format, CellStyle? style)
    {
        if (!_columnOverrides.TryGetValue(propertyOrColumnName, out var ov))
        {
            ov = new ColumnOverride();
            _columnOverrides[propertyOrColumnName] = ov;
        }
        if (header is not null) ov.Header = header;
        if (width > 0) ov.Width = width;
        if (format is not null) ov.Format = format;
        if (style is not null) ov.Style = style;
    }

    protected void IgnoreColumn(string propertyOrColumnName) => _ignoredColumns.Add(propertyOrColumnName);
}

/// <summary>
/// Fluent configuration for exporting an <see cref="IEnumerable{T}"/> to xlsx. Used inside
/// the lambda of <c>list.ToXlsx(path, cfg =&gt; cfg. ...)</c>.
/// </summary>
public sealed class XlsxExportConfig<T> : ExportConfigBase
{
    /// <summary>The name of the worksheet in the output file. Default: "Sheet1".</summary>
    public XlsxExportConfig<T> SheetName(string name) { SetSheetName(name); return this; }

    /// <summary>Style applied to the header row.</summary>
    public XlsxExportConfig<T> HeaderStyle(CellStyle style) { SetHeaderStyle(style); return this; }

    /// <summary>Enables Excel's autofilter on the header row.</summary>
    public XlsxExportConfig<T> AutoFilter(bool on = true) { SetAutoFilter(on); return this; }

    /// <summary>Freezes the header row so it stays visible while scrolling.</summary>
    public XlsxExportConfig<T> FreezeHeaderRow(bool on = true) { SetFreezeHeaderRow(on); return this; }

    /// <summary>
    /// Wraps the data range in an Excel structured table with the given name and style.
    /// </summary>
    public XlsxExportConfig<T> AsTable(string name, TableStyle style = TableStyle.Medium2)
    { SetAsTable(name, style); return this; }

    /// <summary>
    /// Sets the printed page header and/or footer (Excel header/footer codes: &amp;L/&amp;C/&amp;R
    /// sections, &amp;P page number, &amp;N page count, &amp;D date; plain text is centered).
    /// </summary>
    public XlsxExportConfig<T> PageHeaderFooter(string? header, string? footer = null)
    { SetPageHeaderFooter(header, footer); return this; }

    // ---- Strongly-typed column overrides (preferred for IEnumerable<T>) ------------

    /// <summary>Overrides settings for the column derived from the given property expression.</summary>
    public XlsxExportConfig<T> Column<TProp>(
        Expression<Func<T, TProp>> selector,
        string? header = null,
        double width = 0,
        string? format = null,
        CellStyle? style = null)
    {
        var name = GetPropertyName(selector);
        OverrideColumn(name, header, width, format, style);
        return this;
    }

    /// <summary>Excludes a property from the export.</summary>
    public XlsxExportConfig<T> Ignore<TProp>(Expression<Func<T, TProp>> selector)
    {
        IgnoreColumn(GetPropertyName(selector));
        return this;
    }

    /// <summary>Applies a per-data-row style to every cell of a column.</summary>
    public XlsxExportConfig<T> ColumnStyle<TProp>(Expression<Func<T, TProp>> selector, CellStyle style)
    {
        OverrideColumn(GetPropertyName(selector), header: null, width: 0, format: null, style: style);
        return this;
    }

    private static string GetPropertyName<TProp>(Expression<Func<T, TProp>> selector)
    {
        Expression body = selector.Body;
        // Strip implicit conversions (e.g. value type -> object).
        while (body is UnaryExpression ue && ue.NodeType == ExpressionType.Convert)
            body = ue.Operand;
        if (body is MemberExpression me && me.Member is PropertyInfo)
            return me.Member.Name;
        throw new ArgumentException(
            "Selector must be a simple property expression like 'x => x.PropertyName'.", nameof(selector));
    }
}

/// <summary>
/// Fluent configuration for exporting a <see cref="System.Data.DataTable"/> to xlsx.
/// </summary>
public sealed class DataTableExportConfig : ExportConfigBase
{
    public DataTableExportConfig SheetName(string name) { SetSheetName(name); return this; }
    public DataTableExportConfig HeaderStyle(CellStyle style) { SetHeaderStyle(style); return this; }
    public DataTableExportConfig AutoFilter(bool on = true) { SetAutoFilter(on); return this; }
    public DataTableExportConfig FreezeHeaderRow(bool on = true) { SetFreezeHeaderRow(on); return this; }

    public DataTableExportConfig AsTable(string name, TableStyle style = TableStyle.Medium2)
    { SetAsTable(name, style); return this; }

    /// <summary>
    /// Sets the printed page header and/or footer (Excel header/footer codes: &amp;L/&amp;C/&amp;R
    /// sections, &amp;P page number, &amp;N page count, &amp;D date; plain text is centered).
    /// </summary>
    public DataTableExportConfig PageHeaderFooter(string? header, string? footer = null)
    { SetPageHeaderFooter(header, footer); return this; }

    /// <summary>Overrides settings for the column with the given <c>DataColumn.ColumnName</c>.</summary>
    public DataTableExportConfig Column(
        string columnName,
        string? header = null,
        double width = 0,
        string? format = null,
        CellStyle? style = null)
    {
        if (string.IsNullOrEmpty(columnName)) throw new ArgumentException("Column name required.", nameof(columnName));
        OverrideColumn(columnName, header, width, format, style);
        return this;
    }

    public DataTableExportConfig Ignore(string columnName)
    {
        if (string.IsNullOrEmpty(columnName)) throw new ArgumentException("Column name required.", nameof(columnName));
        IgnoreColumn(columnName);
        return this;
    }

    public DataTableExportConfig ColumnStyle(string columnName, CellStyle style)
    {
        if (string.IsNullOrEmpty(columnName)) throw new ArgumentException("Column name required.", nameof(columnName));
        OverrideColumn(columnName, header: null, width: 0, format: null, style: style);
        return this;
    }

    public DataTableExportConfig ColumnFormat(string columnName, string format)
    {
        if (string.IsNullOrEmpty(columnName)) throw new ArgumentException("Column name required.", nameof(columnName));
        OverrideColumn(columnName, header: null, width: 0, format: format, style: null);
        return this;
    }
}
