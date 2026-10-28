using System;
using System.Globalization;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>Options for <see cref="NumberFormatter"/>.</summary>
public sealed class NumberFormatOptions
{
    /// <summary>
    /// Culture that supplies what Excel takes from the system locale: the decimal separator
    /// (for '.'), the group separator and group sizes (for ','; e.g. en-IN groups 1,00,000),
    /// the date and time separators (for '/' and ':' in date formats), and month and day
    /// names. Default: <see cref="CultureInfo.InvariantCulture"/>, which matches Excel en-US.
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// True for workbooks using the 1904 date system (serial 0 = 1904-01-01). Default false:
    /// the 1900 system, including Excel's fictitious 29-Feb-1900 (serial 60).
    /// </summary>
    public bool Date1904 { get; init; }

    /// <summary>Shared default instance (invariant culture, 1900 date system).</summary>
    public static NumberFormatOptions Default { get; } = new();
}

/// <summary>
/// The result of formatting a value: the display text plus the presentation hints a format
/// code can carry.
/// </summary>
/// <param name="Text">The text Excel would display (fill characters from '*' are not expanded).</param>
/// <param name="Color">Colour from a [Red] / [Color10]-style code as 6-digit RGB hex, or null.</param>
/// <param name="FillCharacter">Repeat-fill character from a '*x' code (Excel repeats it to fill the cell), or null.</param>
/// <param name="IsDateTime">True if the value was rendered with a date/time section.</param>
public readonly record struct FormattedValue(string Text, string? Color, char? FillCharacter, bool IsDateTime);

/// <summary>
/// Renders a cell value with an Excel number format code, producing the text Excel displays:
/// <c>Format(45000, "dd/mm/yyyy")</c> → "15/03/2023"; <c>Format(1234.5, "#,##0.00")</c> →
/// "1,234.50"; <c>Format(-0.256, "0.0%;[Red](0.0%)")</c> → "(25.6%)".
///
/// Supports General; digit placeholders 0 # ?; decimal point; grouping and scaling commas;
/// percent; scientific (E+ / E-); fractions (# ?/?, ??/100); up to four sections
/// (positive;negative;zero;text) and [&gt;100]-style conditions; colours; literals in quotes,
/// backslash escapes, _x spacing and *x fill; [$€-409] currency tags; text sections with @;
/// dates and times (y, m, d, h, s, AM/PM, A/P, fractional seconds) and elapsed time
/// ([h], [mm], [ss]) in the 1900 or 1904 date system.
///
/// Locale-dependent parts follow <see cref="NumberFormatOptions.Culture"/>. Not supported:
/// [DBNum] / East Asian numerals, Buddhist and other non-Gregorian calendars, and the
/// system long-date/time tags [$-F800] / [$-F400] (rendered as ISO-style date / time).
/// Thread-safe; parsed format codes are cached.
/// </summary>
public static class NumberFormatter
{
    /// <summary>Formats <paramref name="value"/> with <paramref name="formatCode"/> and returns the display text.</summary>
    /// <param name="value">A number (any numeric type), DateTime, DateTimeOffset, TimeSpan, bool, string, or null.</param>
    /// <param name="formatCode">An Excel format code; null or empty means General.</param>
    /// <param name="options">Culture and date system; null for the defaults.</param>
    public static string Format(object? value, string? formatCode, NumberFormatOptions? options = null)
        => FormatDetailed(value, formatCode, options).Text;

    /// <summary>As <see cref="Format"/>, also returning colour, fill character and date flag.</summary>
    public static FormattedValue FormatDetailed(object? value, string? formatCode, NumberFormatOptions? options = null)
        => NumberFormatEngine.Format(value, formatCode, options ?? NumberFormatOptions.Default);

    /// <summary>
    /// Converts a <see cref="DateTime"/> to an Excel date serial in the given date system
    /// (1900 system: 1900-03-01 = 61; the fictitious 29-Feb-1900 is serial 60).
    /// </summary>
    public static double ToSerial(DateTime value, bool date1904 = false)
        => NumberFormatEngine.ToSerial(value, date1904);
}
