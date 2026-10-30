using System;
using System.Globalization;
using System.Text;

namespace Chuvadi.Docs.Internal;

/// <summary>Formats cached field results the way Word would show them.</summary>
internal static class FieldFormat
{
    /// <summary>
    /// A date in Word's date-picture syntax (DATE \@): d, dd, ddd, dddd, M, MM, MMM, MMMM, yy,
    /// yyyy, h, hh, H, HH, m, mm, s, ss, AM/PM, am/pm; text in single quotes is literal.
    /// </summary>
    public static string FormatDate(DateTime value, string picture)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        int i = 0;
        while (i < picture.Length)
        {
            char c = picture[i];
            if (c == '\'')
            {
                int close = picture.IndexOf('\'', i + 1);
                if (close < 0) close = picture.Length;
                sb.Append(picture, i + 1, close - i - 1);
                i = close + 1;
                continue;
            }
            if (string.Compare(picture, i, "AM/PM", 0, 5, StringComparison.Ordinal) == 0) { sb.Append(value.Hour < 12 ? "AM" : "PM"); i += 5; continue; }
            if (string.Compare(picture, i, "am/pm", 0, 5, StringComparison.Ordinal) == 0) { sb.Append(value.Hour < 12 ? "am" : "pm"); i += 5; continue; }
            int run = 1;
            while (i + run < picture.Length && picture[i + run] == c) run++;
            switch (c)
            {
                case 'd':
                    sb.Append(run switch { 1 => value.Day.ToString(inv), 2 => value.Day.ToString("00", inv), 3 => value.ToString("ddd", inv), _ => value.ToString("dddd", inv) });
                    break;
                case 'M':
                    sb.Append(run switch { 1 => value.Month.ToString(inv), 2 => value.Month.ToString("00", inv), 3 => value.ToString("MMM", inv), _ => value.ToString("MMMM", inv) });
                    break;
                case 'y':
                    sb.Append(run <= 2 ? (value.Year % 100).ToString("00", inv) : value.Year.ToString(inv));
                    break;
                case 'h':
                {
                    int h12 = value.Hour % 12 == 0 ? 12 : value.Hour % 12;
                    sb.Append(run == 1 ? h12.ToString(inv) : h12.ToString("00", inv));
                    break;
                }
                case 'H':
                    sb.Append(run == 1 ? value.Hour.ToString(inv) : value.Hour.ToString("00", inv));
                    break;
                case 'm':
                    sb.Append(run == 1 ? value.Minute.ToString(inv) : value.Minute.ToString("00", inv));
                    break;
                case 's':
                    sb.Append(run == 1 ? value.Second.ToString(inv) : value.Second.ToString("00", inv));
                    break;
                default:
                    sb.Append(c, run);
                    break;
            }
            i += run;
        }
        return sb.ToString();
    }
}
