using System;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Excel date serial ↔ DateTime in the 1900 or 1904 date system, and number-format codes for
/// display text.
/// </summary>
internal static class SerialDate
{
    private const double MaxSerial1900 = 2958465.99999999;   // 9999-12-31 23:59:59.999

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

    private static DateTime RoundToMs(DateTime dt)
        => new(dt.Ticks - dt.Ticks % TimeSpan.TicksPerMillisecond + (dt.Ticks % TimeSpan.TicksPerMillisecond >= TimeSpan.TicksPerMillisecond / 2 ? TimeSpan.TicksPerMillisecond : 0), dt.Kind);

    /// <summary>
    /// The format code to display a value with, for a numFmtId: the file's custom code or the
    /// ECMA built-in, else Excel en-US's code for the ids whose code depends on the UI language
    /// (5-8 currency, 41-44 accounting). Null means General.
    /// </summary>
    public static string? DisplayCode(CellStyleTable table, int numFmtId)
    {
        if (numFmtId <= 0) return null;
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
