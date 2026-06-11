using System;
using System.Globalization;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Validation rule applied to a cell range. Use the static factory methods (<c>List</c>,
/// <c>WholeNumber</c>, <c>Decimal</c>, <c>Date</c>, <c>TextLength</c>) to construct one.
///
/// <code>
/// sheet.AddDataValidation("E2:E1000", DataValidation.List("Pending", "Active", "Closed"));
/// sheet.AddDataValidation("F2:F1000", DataValidation.WholeNumber(min: 0, max: 120));
/// </code>
/// </summary>
public sealed class DataValidation
{
    internal string Type { get; private init; } = "any";
    internal string? Operator { get; private init; }
    internal string? Formula1 { get; private init; }
    internal string? Formula2 { get; private init; }

    /// <summary>If true, blank cells are allowed regardless of the rule. Default: true.</summary>
    public bool AllowBlank { get; set; } = true;

    /// <summary>If true, a dropdown arrow is shown in the cell (only relevant for List). Default: true.</summary>
    public bool ShowDropdown { get; set; } = true;

    /// <summary>If non-null, the title of the error dialog shown when a user types an invalid value.</summary>
    public string? ErrorTitle { get; set; }

    /// <summary>If non-null, the body text of the error dialog.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>If non-null, the title of an info popup shown when the cell is selected.</summary>
    public string? PromptTitle { get; set; }

    /// <summary>If non-null, the body text of the info popup shown on cell selection.</summary>
    public string? PromptMessage { get; set; }

    private DataValidation() { }

    /// <summary>
    /// Dropdown list of fixed string values. Excel limits the inline-list form to a total of
    /// 255 characters including separators; for longer lists, point the formula at a named
    /// range or sheet range and use the <see cref="ListFromRange"/> factory instead.
    /// </summary>
    public static DataValidation List(params string[] values)
    {
        if (values is null || values.Length == 0)
            throw new ArgumentException("At least one value required.", nameof(values));
        // OOXML format: a quoted, comma-separated list.
        var joined = string.Join(",", values);
        if (joined.Length > 255)
            throw new ArgumentException(
                $"Inline list too long ({joined.Length} chars); use ListFromRange for long lists.",
                nameof(values));
        return new DataValidation
        {
            Type = "list",
            Formula1 = "\"" + joined.Replace("\"", "\"\"") + "\"",
        };
    }

    /// <summary>Dropdown list whose options come from a sheet range or named range, e.g. "Lookups!$A$1:$A$10".</summary>
    public static DataValidation ListFromRange(string rangeOrName)
    {
        if (string.IsNullOrEmpty(rangeOrName)) throw new ArgumentException("Range or name required.", nameof(rangeOrName));
        return new DataValidation
        {
            Type = "list",
            Formula1 = rangeOrName,
        };
    }

    public static DataValidation WholeNumber(int? min = null, int? max = null)
        => MakeNumeric("whole", min, max);

    public static DataValidation DecimalNumber(double? min = null, double? max = null)
        => MakeNumeric("decimal", min, max);

    public static DataValidation Date(DateTime? min = null, DateTime? max = null)
    {
        var (op, f1, f2) = ResolveBounds(
            min?.ToOADate().ToString("R", CultureInfo.InvariantCulture),
            max?.ToOADate().ToString("R", CultureInfo.InvariantCulture));
        return new DataValidation { Type = "date", Operator = op, Formula1 = f1, Formula2 = f2 };
    }

    public static DataValidation TextLength(int? min = null, int? max = null)
        => MakeNumeric("textLength", min, max);

    private static DataValidation MakeNumeric(string type, double? min, double? max)
    {
        var (op, f1, f2) = ResolveBounds(
            min?.ToString("R", CultureInfo.InvariantCulture),
            max?.ToString("R", CultureInfo.InvariantCulture));
        return new DataValidation { Type = type, Operator = op, Formula1 = f1, Formula2 = f2 };
    }

    private static DataValidation MakeNumeric(string type, int? min, int? max)
    {
        var (op, f1, f2) = ResolveBounds(
            min?.ToString(CultureInfo.InvariantCulture),
            max?.ToString(CultureInfo.InvariantCulture));
        return new DataValidation { Type = type, Operator = op, Formula1 = f1, Formula2 = f2 };
    }

    private static (string Op, string? F1, string? F2) ResolveBounds(string? minStr, string? maxStr)
    {
        if (minStr is not null && maxStr is not null) return ("between", minStr, maxStr);
        if (minStr is not null) return ("greaterThanOrEqual", minStr, null);
        if (maxStr is not null) return ("lessThanOrEqual", maxStr, null);
        return ("between", "0", "0");  // shouldn't be reachable with sensible input
    }
}
