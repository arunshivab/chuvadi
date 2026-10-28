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
        bool was = page.Loading;
        page.Loading = true;
        try { ApplyCore(page, names, workbookIndex); }
        finally { page.Loading = was; }
    }

    private static void ApplyCore(PageSetup page, IReadOnlyList<DefinedNameInfo> names, int workbookIndex)
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

    /// <summary>True for the two built-in names that <see cref="PageSetup"/> owns.</summary>
    public static bool IsPrintName(string name)
        => string.Equals(name, "_xlnm.Print_Area", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "_xlnm.Print_Titles", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normalises a print-area range ("$a$1:d20" → "A1:D20"; "a:c"; "1:5"; "B2"), or null when
    /// it is not a plain A1 range. A single cell stays a single cell.
    /// </summary>
    public static string? NormalizeArea(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return null;
        var r = range.Trim().Replace("$", "").ToUpperInvariant();
        var ends = r.Split(':');
        if (ends.Length == 1) return IsCell(ends[0]) ? ends[0] : null;
        if (ends.Length != 2) return null;
        if (IsCell(ends[0]) && IsCell(ends[1])) return r;
        if (ColumnNumber(ends[0]) is int c1 && ColumnNumber(ends[1]) is int c2 && c1 <= 16384 && c2 <= 16384) return r;
        if (int.TryParse(ends[0], NumberStyles.None, CultureInfo.InvariantCulture, out var r1)
            && int.TryParse(ends[1], NumberStyles.None, CultureInfo.InvariantCulture, out var r2)
            && r1 is >= 1 and <= 1048576 && r2 is >= 1 and <= 1048576) return r;
        return null;
    }

    private static bool IsCell(string t) => FormulaText.TryCellRef(t, out _, out _, out _, out _);

    /// <summary>The _xlnm.Print_Area formula for the given sheet: 'Sheet'!$A$1:$D$20,'Sheet'!$F$1:$G$5.</summary>
    public static string AreaFormula(string sheetName, IReadOnlyList<string> areas)
    {
        var sb = new StringBuilder();
        var prefix = QuoteSheet(sheetName) + "!";
        foreach (var a in areas)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(prefix);
            var ends = a.Split(':');
            for (int i = 0; i < ends.Length; i++)
            {
                if (i > 0) sb.Append(':');
                sb.Append(Absolute(ends[i]));
            }
        }
        return sb.ToString();
    }

    /// <summary>The _xlnm.Print_Titles formula: rows first, then columns ('S'!$1:$2,'S'!$A:$B).</summary>
    public static string? TitlesFormula(string sheetName, (int First, int Last)? rows, (int First, int Last)? cols)
    {
        if (rows is null && cols is null) return null;
        var prefix = QuoteSheet(sheetName) + "!";
        var parts = new List<string>(2);
        if (rows is (int r1, int r2))
            parts.Add(prefix + "$" + r1.ToString(CultureInfo.InvariantCulture) + ":$" + r2.ToString(CultureInfo.InvariantCulture));
        if (cols is (int c1, int c2))
            parts.Add(prefix + "$" + CellAddress.ColumnLetters(c1) + ":$" + CellAddress.ColumnLetters(c2));
        return string.Join(",", parts);
    }

    /// <summary>"A1" → "$A$1"; "A" → "$A"; "5" → "$5".</summary>
    private static string Absolute(string part)
    {
        int i = 0;
        while (i < part.Length && char.IsLetter(part[i])) i++;
        if (i == 0 || i == part.Length) return "$" + part;
        return "$" + part.Substring(0, i) + "$" + part.Substring(i);
    }

    /// <summary>Quotes a sheet name for a formula when it needs it ('My Sheet', 'O''Brien', '2024').</summary>
    public static string QuoteSheet(string name)
    {
        bool plain = name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_')
            && !FormulaText.TryCellRef(name, out _, out _, out _, out _)
            && !IsR1C1Like(name);
        if (plain)
            foreach (var ch in name)
                if (!(char.IsLetterOrDigit(ch) || ch is '_' or '.')) { plain = false; break; }
        return plain ? name : "'" + name.Replace("'", "''") + "'";
    }

    private static bool IsR1C1Like(string n)
    {
        var u = n.ToUpperInvariant();
        if (u is "R" or "C") return true;
        if (u.Length > 1 && (u[0] == 'R' || u[0] == 'C') && char.IsDigit(u[1])) return true;
        return false;
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
