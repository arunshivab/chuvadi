using System;
using System.Collections.Generic;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Determines whether a number format code represents a date/time. Used by the reader to
/// decide whether a numeric cell should be returned as DateTime.
///
/// Excel's rule (mirrored here): a format code contains date-pattern characters
/// (y, m, d, h, s) outside quoted literals and bracketed expressions. We also short-circuit
/// for built-in date numFmtIds (14-22, 45-47).
/// </summary>
internal static class DateFormatDetector
{
    /// <summary>Built-in OOXML numFmtIds that always represent dates/times.</summary>
    private static readonly HashSet<int> BuiltInDateIds = new()
    {
        14, 15, 16, 17, 18, 19, 20, 21, 22, 45, 46, 47,
    };

    /// <summary>
    /// Tests whether the given (id, formatCode) pair represents a date/time format.
    /// id == 0 means "General" (no format) → not a date. formatCode == null means "use built-in"
    /// → look only at the id. Both supplied → look at id first, then format code.
    /// </summary>
    public static bool IsDateFormat(int numFmtId, string? formatCode)
    {
        if (BuiltInDateIds.Contains(numFmtId)) return true;

        // Built-in IDs not in our date set are definitely not dates.
        if (numFmtId < 164 && numFmtId != 0) return false;

        if (string.IsNullOrEmpty(formatCode)) return false;
        return ContainsDateChars(formatCode!);
    }

    /// <summary>
    /// Scans the format code for date-pattern characters (y, m, d, h, s), skipping content
    /// inside quoted literals ("..."), escaped chars (\x), and bracket expressions ([Red], [h]).
    /// Note: [h], [m], [s] in brackets ARE date-time markers (duration formats), so we honor
    /// those specifically. Other bracket content (colors, conditions) is ignored.
    /// </summary>
    private static bool ContainsDateChars(string code)
    {
        for (int i = 0; i < code.Length; i++)
        {
            var c = code[i];

            // Quoted literal — skip until matching quote.
            if (c == '"')
            {
                i++;
                while (i < code.Length && code[i] != '"') i++;
                continue;
            }

            // Escape — skip the next char.
            if (c == '\\')
            {
                i++;
                continue;
            }

            // Bracket — check if it's a duration marker, otherwise skip.
            if (c == '[')
            {
                int end = code.IndexOf(']', i + 1);
                if (end < 0) return false; // malformed
                // Check inside the bracket for h/m/s (duration markers).
                for (int j = i + 1; j < end; j++)
                {
                    var bc = char.ToLowerInvariant(code[j]);
                    if (bc == 'h' || bc == 'm' || bc == 's') return true;
                }
                i = end;
                continue;
            }

            var lc = char.ToLowerInvariant(c);
            if (lc == 'y' || lc == 'd') return true;

            // 'm' is ambiguous — it can mean "month" or "minute", but in either case it's a
            // date/time format. 'h' and 's' are unambiguous.
            if (lc == 'm' || lc == 'h' || lc == 's') return true;
        }
        return false;
    }
}
