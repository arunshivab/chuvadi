using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Customizes how a property is exported when using <c>IEnumerable&lt;T&gt;.ToXlsx(...)</c>.
/// Applying this attribute is optional; properties without it are still exported with their
/// declared name and default ordering.
///
/// <code>
/// public class Patient
/// {
///     [Column("Patient ID", Order = 1, Width = 12)]
///     public string Id { get; set; }
///
///     [Column("Date of Birth", Order = 2, Format = "yyyy-mm-dd")]
///     public DateTime Dob { get; set; }
/// }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ColumnAttribute : Attribute
{
    /// <summary>The column header text. If null/empty, the property name is used.</summary>
    public string? Header { get; }

    public ColumnAttribute() { Header = null; }
    public ColumnAttribute(string header) { Header = header; }

    /// <summary>
    /// Ordering hint. Columns are sorted ascending by Order, then by declaration order.
    /// Unspecified columns use <see cref="DefaultOrder"/> (effectively "last in declaration").
    /// </summary>
    public int Order { get; set; } = DefaultOrder;

    /// <summary>Column width in Excel column-width units. 0 or negative = use default (auto).</summary>
    public double Width { get; set; } = 0;

    /// <summary>Number format code applied to every cell in this column (e.g. "yyyy-mm-dd", "0.00").</summary>
    public string? Format { get; set; }

    /// <summary>The default Order value when not explicitly set.</summary>
    public const int DefaultOrder = int.MaxValue / 2;
}

/// <summary>
/// Excludes a property from xlsx export. Useful for internal/computed properties that
/// shouldn't appear in the output file.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ColumnIgnoreAttribute : Attribute { }

/// <summary>
/// Applies a per-data-row style to every cell of a column. Set named properties on the
/// attribute to declare the style; this is a compact alternative to building a full
/// <see cref="CellStyle"/> when only a few properties matter.
///
/// <code>
/// [ColumnStyle(Bold = true, Background = "#FFEB3B")]
/// public string Status { get; set; }
/// </code>
///
/// For richer styling (fonts, borders, alignment), use the fluent API's
/// <c>cfg.ColumnStyle(...)</c> overload instead.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ColumnStyleAttribute : Attribute
{
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public string? Format { get; set; }

    /// <summary>Converts the attribute properties into a <see cref="CellStyle"/>.</summary>
    public CellStyle ToCellStyle()
    {
        var b = new CellStyleBuilder();
        if (Bold) b.Bold();
        if (Italic) b.Italic();
        if (Underline) b.Underline();
        if (Foreground is not null) b.Foreground(Foreground);
        if (Background is not null) b.Background(Background);
        if (Format is not null) b.Format(Format);
        return b.Build();
    }
}
