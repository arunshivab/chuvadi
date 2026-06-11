namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Excel's built-in named table styles. These render with predefined colors and patterns
/// when the file is opened — they don't add custom style definitions to the workbook,
/// they just reference one of the styles Excel ships with.
///
/// Names follow Excel's own convention: "Light" styles are subtle (light background, simple
/// borders), "Medium" styles have stronger colors and banded rows, "Dark" styles use dark
/// headers and high contrast. The numeric suffix selects the color theme (1=blue, 2=orange,
/// 3=gray, 4=gold, 5=blue, 6=green, etc. — the exact mapping varies by style category).
///
/// Use <see cref="None"/> for a table with no styling — Excel will still apply the structural
/// table behavior (filter dropdowns, structured references) but the visual rendering will be
/// plain.
/// </summary>
public enum TableStyle
{
    None,

    Light1, Light2, Light3, Light4, Light5, Light6, Light7,
    Light8, Light9, Light10, Light11, Light12, Light13, Light14,
    Light15, Light16, Light17, Light18, Light19, Light20, Light21,

    Medium1, Medium2, Medium3, Medium4, Medium5, Medium6, Medium7,
    Medium8, Medium9, Medium10, Medium11, Medium12, Medium13, Medium14,
    Medium15, Medium16, Medium17, Medium18, Medium19, Medium20, Medium21,
    Medium22, Medium23, Medium24, Medium25, Medium26, Medium27, Medium28,

    Dark1, Dark2, Dark3, Dark4, Dark5, Dark6,
    Dark7, Dark8, Dark9, Dark10, Dark11,
}

/// <summary>
/// Internal helper mapping <see cref="TableStyle"/> values to the OOXML style names Excel expects.
/// </summary>
internal static class TableStyleNames
{
    /// <summary>
    /// Returns the OOXML style name (e.g. "TableStyleMedium2") or null for <see cref="TableStyle.None"/>.
    /// </summary>
    public static string? ToOoxmlName(TableStyle style) => style switch
    {
        TableStyle.None => null,
        _ => "TableStyle" + style.ToString(),
    };
}
