namespace Chuvadi.Sheets.Internal;

/// <summary>
/// 2.2.0 (C3, C5): what Excel shows for a culture where it differs from .NET's own culture data, written in as fixed
/// text so the result does not change with the ICU version or the machine. Only cultures measured in Excel are here
/// (Excel for Office 2021 on Windows, 16 Nov 2026, with the Windows regional format set to each culture); every other
/// culture keeps .NET's data. Keyed by <see cref="System.Globalization.CultureInfo.Name"/>, so a culture with user
/// overrides (Windows regional settings) uses the same entry.
/// </summary>
internal static class ExcelLocaleData
{
    private static readonly string[] EnglishIndia =
        { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    private static readonly string[] GermanGermany =
        { "Jan", "Feb", "Mrz", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez" };

    // Excel's Tamil (India) "mmm" shows the full month names.
    private static readonly string[] TamilIndia =
    {
        "ஜனவரி", "பிப்ரவரி", "மார்ச்", "ஏப்ரல்", "மே", "ஜூன்",
        "ஜூலை", "ஆகஸ்ட்", "செப்டம்பர்", "அக்டோபர்", "நவம்பர்", "டிசம்பர்",
    };

    private static readonly string[] GermanDayMonthYear = { "dd\\.\\ mmm\\ yy", "dd\\.\\ mmm", "mmm\\ yy" };

    /// <summary>
    /// Excel's month abbreviations ("mmm") for <paramref name="cultureName"/>, January first, or null to use .NET's
    /// <c>AbbreviatedMonthNames</c>. en-IN: "Sep" (.NET "Sept"); de-DE: "Mrz" (.NET "Mär"); ta-IN: the full names
    /// (.NET abbreviates with a dot).
    /// </summary>
    public static string[]? AbbreviatedMonthNames(string cultureName) => cultureName switch
    {
        "en-IN" => EnglishIndia,
        "de-DE" => GermanGermany,
        "ta-IN" => TamilIndia,
        _ => null,
    };

    /// <summary>
    /// Excel's own codes for built-in 15, 16 and 17 in <paramref name="cultureName"/>, or null when Excel uses the
    /// day-month-year forms with dashes. de-DE: "TT. MMM JJ", "TT. MMM", "MMM JJ" in Excel's German spelling.
    /// </summary>
    public static string[]? DayMonthYearCodes(string cultureName) => cultureName switch
    {
        "de-DE" => GermanDayMonthYear,
        _ => null,
    };
}
