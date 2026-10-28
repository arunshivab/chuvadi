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
    /// <summary>The first formula as stored, without '=' (a list in quotes, a range, a bound, or a custom formula), or null.</summary>
    public string? Formula1 { get; private init; }

    /// <summary>The second bound for Between/NotBetween, as stored, or null.</summary>
    public string? Formula2 { get; private init; }

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

    /// <summary>The kind of rule (&lt;dataValidation type&gt;).</summary>
    public DataValidationType Kind => Type switch
    {
        "whole" => DataValidationType.WholeNumber,
        "decimal" => DataValidationType.Decimal,
        "list" => DataValidationType.List,
        "date" => DataValidationType.Date,
        "time" => DataValidationType.Time,
        "textLength" => DataValidationType.TextLength,
        "custom" => DataValidationType.Custom,
        _ => DataValidationType.Any,
    };

    /// <summary>The comparison for number, date, time and text-length rules, or null (lists, custom, any).</summary>
    public ComparisonOp? Comparison => Operator switch
    {
        null => Kind is DataValidationType.List or DataValidationType.Custom or DataValidationType.Any ? null : ComparisonOp.Between,
        "between" => ComparisonOp.Between,
        "notBetween" => ComparisonOp.NotBetween,
        "equal" => ComparisonOp.Equal,
        "notEqual" => ComparisonOp.NotEqual,
        "lessThan" => ComparisonOp.LessThan,
        "lessThanOrEqual" => ComparisonOp.LessThanOrEqual,
        "greaterThan" => ComparisonOp.GreaterThan,
        "greaterThanOrEqual" => ComparisonOp.GreaterThanOrEqual,
        _ => null,
    };

    /// <summary>Error dialog style when a value is rejected (Stop blocks it; Warning and Information let the user keep it).</summary>
    public DataValidationErrorStyle ErrorStyle { get; set; }

    /// <summary>
    /// Show the error dialog for invalid input. Null (default) = shown when an error title or
    /// message is set, as before 1.6.0. A loaded rule carries the file's value.
    /// </summary>
    public bool? ShowErrorMessage { get; set; }

    /// <summary>
    /// Show the input prompt when the cell is selected. Null (default) = shown when a prompt
    /// title or message is set, as before 1.6.0. A loaded rule carries the file's value.
    /// </summary>
    public bool? ShowInputMessage { get; set; }

    /// <summary>Input method (IME) mode for East Asian input, as stored (e.g. "hiragana"), or null.</summary>
    public string? ImeMode { get; set; }

    private DataValidation() { }

    /// <summary>Load path: a rule exactly as stored.</summary>
    internal static DataValidation Loaded(string type, string? op, string? f1, string? f2) => new()
    {
        Type = type, Operator = op, Formula1 = f1, Formula2 = f2,
    };

    /// <summary>A rule whose formula must be true for the value to be accepted (relative to the range's top-left cell), e.g. "COUNTIF($A:$A,A2)=1".</summary>
    public static DataValidation Custom(string formula)
    {
        if (string.IsNullOrEmpty(formula)) throw new ArgumentException("Formula required.", nameof(formula));
        return new DataValidation { Type = "custom", Formula1 = formula };
    }

    /// <summary>Time of day between the bounds (inclusive; either may be null).</summary>
    public static DataValidation Time(TimeSpan? min = null, TimeSpan? max = null)
    {
        var (op, f1, f2) = ResolveBounds(
            min?.TotalDays.ToString("R", CultureInfo.InvariantCulture),
            max?.TotalDays.ToString("R", CultureInfo.InvariantCulture));
        return new DataValidation { Type = "time", Operator = op, Formula1 = f1, Formula2 = f2 };
    }

    /// <summary>A rule of any kind with an explicit comparison and formulas (bounds as Excel formulas, e.g. "10", "$B$1", "TODAY()").</summary>
    public static DataValidation Create(DataValidationType kind, ComparisonOp comparison, string formula1, string? formula2 = null)
    {
        if (string.IsNullOrEmpty(formula1)) throw new ArgumentException("Formula required.", nameof(formula1));
        if (comparison is ComparisonOp.Between or ComparisonOp.NotBetween && string.IsNullOrEmpty(formula2))
            throw new ArgumentException("Between and NotBetween need a second formula.", nameof(formula2));
        var type = kind switch
        {
            DataValidationType.WholeNumber => "whole",
            DataValidationType.Decimal => "decimal",
            DataValidationType.List => "list",
            DataValidationType.Date => "date",
            DataValidationType.Time => "time",
            DataValidationType.TextLength => "textLength",
            DataValidationType.Custom => "custom",
            _ => throw new ArgumentException("Use a kind other than Any.", nameof(kind)),
        };
        string? op = kind is DataValidationType.List or DataValidationType.Custom ? null : comparison switch
        {
            ComparisonOp.Between => "between",
            ComparisonOp.NotBetween => "notBetween",
            ComparisonOp.Equal => "equal",
            ComparisonOp.NotEqual => "notEqual",
            ComparisonOp.LessThan => "lessThan",
            ComparisonOp.LessThanOrEqual => "lessThanOrEqual",
            ComparisonOp.GreaterThan => "greaterThan",
            _ => "greaterThanOrEqual",
        };
        return new DataValidation { Type = type, Operator = op, Formula1 = formula1, Formula2 = formula2 };
    }

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

/// <summary>Data validation kinds (&lt;dataValidation type&gt;).</summary>
public enum DataValidationType { Any = 0, WholeNumber, Decimal, List, Date, Time, TextLength, Custom }

/// <summary>Error dialog style for rejected input (&lt;dataValidation errorStyle&gt;).</summary>
public enum DataValidationErrorStyle { Stop = 0, Warning, Information }
