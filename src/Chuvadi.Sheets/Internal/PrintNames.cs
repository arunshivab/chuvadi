using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Maps the built-in defined names _xlnm.Print_Area and _xlnm.Print_Titles (scoped to a sheet
/// by localSheetId) onto that sheet's <see cref="PageSetup"/>.
/// </summary>
internal static class PrintNames
{
    public static void Apply(PageSetup page, IReadOnlyList<DefinedNameInfo> names, int workbookIndex)
    {
        foreach (var n in names)
        {
            if (n.LocalSheetIndex != workbookIndex) continue;
            if (string.Equals(n.Name, "_xlnm.Print_Area", StringComparison.OrdinalIgnoreCase))
            {
                page.PrintAreaFormula = n.Formula;
                var areas = new List<string>();
                foreach (var part in SplitRefs(n.Formula))
                {
                    var r = StripSheet(part).Replace("$", "");
                    if (r.Length > 0 && r[0] != '#') areas.Add(r);
                }
                page.PrintAreas = areas;
            }
            else if (string.Equals(n.Name, "_xlnm.Print_Titles", StringComparison.OrdinalIgnoreCase))
            {
                page.PrintTitlesFormula = n.Formula;
                foreach (var part in SplitRefs(n.Formula))
                {
                    var r = StripSheet(part).Replace("$", "");
                    var ends = r.Split(':');
                    if (ends.Length != 2) continue;
                    if (int.TryParse(ends[0], NumberStyles.None, CultureInfo.InvariantCulture, out var r1)
                        && int.TryParse(ends[1], NumberStyles.None, CultureInfo.InvariantCulture, out var r2))
                        page.PrintTitleRows = (Math.Min(r1, r2), Math.Max(r1, r2));
                    else if (ColumnNumber(ends[0]) is int c1 && ColumnNumber(ends[1]) is int c2)
                        page.PrintTitleColumns = (Math.Min(c1, c2), Math.Max(c1, c2));
                }
            }
        }
    }

    /// <summary>Splits "A,B" on commas outside quotes (sheet names may contain commas).</summary>
    private static IEnumerable<string> SplitRefs(string formula)
    {
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (var ch in formula)
        {
            if (ch == '\'') quoted = !quoted;
            if (ch == ',' && !quoted) { yield return sb.ToString().Trim(); sb.Clear(); continue; }
            sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString().Trim();
    }

    private static string StripSheet(string reference)
    {
        int bang = reference.LastIndexOf('!');
        return bang >= 0 ? reference.Substring(bang + 1) : reference;
    }

    private static int? ColumnNumber(string letters)
    {
        if (letters.Length == 0 || letters.Length > 3) return null;
        int c = 0;
        foreach (var ch in letters)
        {
            if (!char.IsLetter(ch)) return null;
            c = c * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return c;
    }
}
