using System;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Excel date serial ↔ DateTime in the 1900 or 1904 date system, and number-format codes for
/// display text.
/// </summary>
internal static class SerialDate
{
    private const double MaxSerial1900 = 2958465.99999999;   // 9999-12-31 23:59:59.999

    private static readonly DateTime OleBase = new(1899, 12, 30);
    private static readonly DateTime Excel1900Day0 = new(1899, 12, 31);   // serial 0 = "0-Jan-1900"
    private static readonly DateTime FirstAfterFakeLeapDay = new(1900, 3, 1);

    /// <summary>
    /// The serial Excel stores for a DateTime in the 1900 date system, the inverse of
    /// <see cref="ToDateTime"/> (2.0.5, SH-11): dates on or after 1-Mar-1900 are the OLE Automation
    /// date; 31-Dec-1899 to 28-Feb-1900 count from 31-Dec-1899, because Excel's 1900 system includes
    /// the fictitious 29-Feb-1900 (1-Jan-1900 is serial 1, not 2 as ToOADate gives); a DateTime on
    /// 30-Dec-1899 is a time-only value (serial 0 to 1), which the reader produces for serials below
    /// 1. Earlier dates keep ToOADate (Excel shows any negative serial as "####" anyway).
    /// Written files are always in the 1900 system.
    /// </summary>
    public static double FromDateTime(DateTime value)
    {
        if (value >= FirstAfterFakeLeapDay) return value.ToOADate();
        if (value >= Excel1900Day0) return (value - Excel1900Day0).TotalDays;
        if (value >= OleBase) return (value - OleBase).TotalDays;
        return value.ToOADate();
    }

    /// <summary>
    /// <see cref="FromDateTime(DateTime)"/> in the given date system (the formula evaluator works in the
    /// workbook's own system).
    /// </summary>
    public static double FromDateTime(DateTime value, bool date1904)
        => date1904 ? value.ToOADate() - 1462 : FromDateTime(value);

    /// <summary>
    /// Converts a serial to a DateTime, or null if it cannot be represented: negative serials,
    /// serials beyond 9999-12-31, and serial 60 in the 1900 system (Excel's fictitious
    /// 29-Feb-1900). In the 1900 system serials 1..59 (Jan–Feb 1900) are converted exactly,
    /// unlike DateTime.FromOADate which is a day off there.
    /// </summary>
    public static DateTime? ToDateTime(double serial, bool date1904)
    {
        if (double.IsNaN(serial) || serial < 0) return null;
        try
        {
            if (date1904)
            {
                var dt = new DateTime(1904, 1, 1).AddDays(serial);
                return RoundToMs(dt);
            }
            if (serial >= 61) return serial > MaxSerial1900 ? null : DateTime.FromOADate(serial);
            if (serial >= 60 && serial < 61) return null;
            if (serial < 1) return RoundToMs(new DateTime(1899, 12, 30).AddDays(serial));   // time-only values
            return RoundToMs(new DateTime(1899, 12, 31).AddDays(serial));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// The built-in date ids whose display follows the culture: 14 (short date) and 22 (short date and time) since
    /// 2.1.0 (SH-9 C2); 15, 16 and 17 (day-month-year forms) since 2.2.0 (C3).
    /// </summary>
    public static bool IsRegionalDateId(int numFmtId) => numFmtId is >= 14 and <= 17 or 22;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> RegionalCodes = new(StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> RegionalCodesByCulture = new(StringComparer.Ordinal);

    /// <summary>
    /// The Excel code for built-in 14 to 17 or 22 under <paramref name="culture"/>, as Excel shows them on a PC with
    /// that culture's regional settings (measured in Excel, 16 Nov 2026: en-US, en-IN, de-DE, ta-IN):
    /// <list type="bullet">
    /// <item>14: the short date pattern as an Excel code (2.1.0).</item>
    /// <item>22: the same, a space, and "h:mm", or "hh:mm" when the short time pattern has a two-digit hour (2.2.0;
    /// 24-hour, no AM/PM, as Excel shows it in every region).</item>
    /// <item>15, 16, 17: "d-mmm-yy", "d-mmm", "mmm-yy", with "dd" when the short date has a two-digit day; a culture
    /// whose Excel has its own forms uses those (<see cref="ExcelLocaleData.DayMonthYearCodes"/>, de-DE).</item>
    /// </list>
    /// The invariant culture keeps the 2.1.0 codes ("d-mmm-yy", "d-mmm", "mmm-yy", short date and "h:mm"). Cached by
    /// culture name and patterns, so a culture with user overrides (Windows regional settings) gets its own code.
    /// </summary>
    public static string RegionalDateCode(int numFmtId, System.Globalization.CultureInfo culture)
    {
        culture ??= System.Globalization.CultureInfo.InvariantCulture;
        var dtf = culture.DateTimeFormat;
        string shortDate = dtf.ShortDatePattern;
        if (numFmtId == 14) return RegionalCodes.GetOrAdd(shortDate, ExcelCodeFromDotNetDatePattern);
        bool invariant = culture.Name.Length == 0;
        string key = numFmtId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0001" + culture.Name + "\u0001" + shortDate
                     + "\u0001" + (numFmtId == 22 ? dtf.ShortTimePattern : string.Empty);
        return RegionalCodesByCulture.GetOrAdd(key, _ =>
        {
            if (numFmtId == 22)
            {
                string date = RegionalCodes.GetOrAdd(shortDate, ExcelCodeFromDotNetDatePattern);
                string hour = invariant ? "h" : (HasTwoDigitHour(dtf.ShortTimePattern) ? "hh" : "h");
                return date + " " + hour + ":mm";
            }
            if (invariant) return numFmtId switch { 15 => "d-mmm-yy", 16 => "d-mmm", _ => "mmm-yy" };
            var own = ExcelLocaleData.DayMonthYearCodes(culture.Name);
            if (own is not null) return own[numFmtId - 15];
            string day = HasTwoDigitDay(shortDate) ? "dd" : "d";
            return numFmtId switch { 15 => day + "-mmm-yy", 16 => day + "-mmm", _ => "mmm-yy" };
        });
    }

    /// <summary>True when a .NET date pattern shows the day of the month with two digits ("dd", not "ddd"/"dddd").</summary>
    internal static bool HasTwoDigitDay(string pattern) => FirstRun(pattern, c => c == 'd', maxRun: 2) == 2;

    /// <summary>True when a .NET time pattern shows the hour with two digits ("HH" or "hh").</summary>
    internal static bool HasTwoDigitHour(string pattern) => FirstRun(pattern, c => c is 'h' or 'H', maxRun: int.MaxValue) >= 2;

    /// <summary>
    /// The length of the first run of a pattern letter outside quotes and escapes whose length is at most
    /// <paramref name="maxRun"/> (longer runs, such as "dddd", are skipped); 0 when there is none.
    /// </summary>
    private static int FirstRun(string pattern, Func<char, bool> letter, int maxRun)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c is '\'' or '"')
            {
                int end = pattern.IndexOf(c, i + 1);
                i = end < 0 ? pattern.Length : end;
                continue;
            }
            if (c == '\\') { i++; continue; }
            if (!letter(c)) continue;
            int run = 1;
            while (i + run < pattern.Length && pattern[i + run] == c) run++;
            if (run <= maxRun) return run;
            i += run - 1;
        }
        return 0;
    }

    /// <summary>
    /// A .NET date pattern (d, dd, ddd, dddd, M…MMMM, y…yyyy, '/', quoted literals) as an Excel format code.
    /// "/" stays "/", which displays the culture's date separator; any other character is written escaped.
    /// </summary>
    internal static string ExcelCodeFromDotNetDatePattern(string pattern)
    {
        var sb = new System.Text.StringBuilder(pattern.Length + 8);
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            int run = 1;
            while (i + run < pattern.Length && pattern[i + run] == c && c is 'd' or 'M' or 'y' or 'g') run++;
            switch (c)
            {
                case 'd': sb.Append('d', Math.Min(run, 4)); i += run - 1; break;
                case 'M': sb.Append('m', Math.Min(run, 4)); i += run - 1; break;
                case 'y': sb.Append(run <= 2 ? "yy" : "yyyy"); i += run - 1; break;
                case 'g': i += run - 1; break;   // era: not shown in the Gregorian calendar
                case '/': sb.Append('/'); break;
                case '\\':
                    if (i + 1 < pattern.Length) { sb.Append('\\').Append(pattern[++i]); }
                    break;
                case '\'':
                case '"':
                    int end = pattern.IndexOf(c, i + 1);
                    if (end < 0) end = pattern.Length;
                    for (int k = i + 1; k < end; k++) sb.Append('\\').Append(pattern[k]);
                    i = end;
                    break;
                default: sb.Append('\\').Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static DateTime RoundToMs(DateTime dt)
        => new(dt.Ticks - dt.Ticks % TimeSpan.TicksPerMillisecond + (dt.Ticks % TimeSpan.TicksPerMillisecond >= TimeSpan.TicksPerMillisecond / 2 ? TimeSpan.TicksPerMillisecond : 0), dt.Kind);

    /// <summary>
    /// The format code to display a value with, for a numFmtId: the file's custom code or the
    /// ECMA built-in, else Excel en-US's code for the ids whose code depends on the UI language
    /// (5-8 currency, 41-44 accounting). Null means General.
    /// </summary>
    public static string? DisplayCode(CellStyleTable table, int numFmtId, System.Globalization.CultureInfo culture)
    {
        if (numFmtId <= 0) return null;
        // 2.1.0 (SH-9 C2): built-in 14 and 22 show the system's short date (22: plus the time), not their ECMA
        // codes. 2.2.0 (C3): 15 to 17 and 22's hour follow the culture as well (RegionalDateCode). A code the file
        // itself declares for the id still wins.
        if (IsRegionalDateId(numFmtId) && !table.CustomNumberFormats.ContainsKey(numFmtId))
            return RegionalDateCode(numFmtId, culture);
        var code = table.GetNumberFormatCode(numFmtId);
        if (code is not null) return code;
        return numFmtId switch
        {
            5 => "\"$\"#,##0_);(\"$\"#,##0)",
            6 => "\"$\"#,##0_);[Red](\"$\"#,##0)",
            7 => "\"$\"#,##0.00_);(\"$\"#,##0.00)",
            8 => "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)",
            41 => "_(* #,##0_);_(* \\(#,##0\\);_(* \"-\"_);_(@_)",
            42 => "_(\"$\"* #,##0_);_(\"$\"* \\(#,##0\\);_(\"$\"* \"-\"_);_(@_)",
            43 => "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)",
            44 => "_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)",
            _ => null,   // East Asian locale ids (27-36, 50-81): General
        };
    }
}
