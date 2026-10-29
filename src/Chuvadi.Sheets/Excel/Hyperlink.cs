using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// A hyperlink on a cell or range (&lt;hyperlink&gt; in the sheet's &lt;hyperlinks&gt;), since 1.7.0.
/// It points outside the workbook (<see cref="Target"/>: a URL, mailto: or file path, stored
/// in the sheet's relationships), inside it (<see cref="Location"/>: "Sheet2!A1" or a defined
/// name), or both (a file plus a place in it). All text is kept exactly as stored. Immutable.
/// </summary>
public sealed class Hyperlink
{
    /// <param name="reference">The cell or range it covers, e.g. "B3" or "B3:D3".</param>
    /// <param name="target">External target (URL, mailto:, file path), or null.</param>
    /// <param name="location">Place in the workbook (or in the target file), e.g. "'Sheet 2'!A1", or null.</param>
    /// <param name="tooltip">Screen tip, or null.</param>
    /// <param name="display">Display text Excel records for the link, or null.</param>
    public Hyperlink(string reference, string? target = null, string? location = null, string? tooltip = null, string? display = null)
        : this(Check(reference, target, location), target, location, tooltip, display, loaded: true) { }

    private static string Check(string reference, string? target, string? location)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("Reference required.", nameof(reference));
        if (string.IsNullOrEmpty(target) && string.IsNullOrEmpty(location))
            throw new ArgumentException("A hyperlink needs a target, a location, or both.");
        return reference;
    }

    /// <summary>Load path: as stored, without validation (a file can have a link whose target relationship is missing).</summary>
    internal Hyperlink(string reference, string? target, string? location, string? tooltip, string? display, bool loaded)
    {
        Reference = reference;
        Target = string.IsNullOrEmpty(target) ? null : target;
        Location = string.IsNullOrEmpty(location) ? null : location;
        Tooltip = tooltip;
        Display = display;
        ParseReference(reference, out int fr, out int fc, out int lr, out int lc);
        FirstRow = fr; FirstColumn = fc; LastRow = lr; LastColumn = lc;
    }

    /// <summary>The covered cell or range as stored, e.g. "B3" or "B3:D3".</summary>
    public string Reference { get; }

    /// <summary>The external target (URL, mailto:, file path) as stored, or null for a link within the workbook.</summary>
    public string? Target { get; }

    /// <summary>The place in the workbook (or in <see cref="Target"/>), e.g. "Sheet2!A1" or a defined name, or null.</summary>
    public string? Location { get; }

    /// <summary>The screen tip, or null.</summary>
    public string? Tooltip { get; }

    /// <summary>The display text Excel records (usually the cell text), or null.</summary>
    public string? Display { get; }

    /// <summary>True if the link points inside the workbook only (a <see cref="Location"/> and no <see cref="Target"/>).</summary>
    public bool IsInternal => Target is null;

    /// <summary>The covered range, 1-based; 0 when <see cref="Reference"/> is not an A1 cell or range.</summary>
    public int FirstRow { get; }
    public int FirstColumn { get; }
    public int LastRow { get; }
    public int LastColumn { get; }

    /// <summary>True if the 1-based cell lies in the covered range.</summary>
    public bool Contains(int row, int column)
        => FirstRow > 0 && row >= FirstRow && row <= LastRow && column >= FirstColumn && column <= LastColumn;

    private static void ParseReference(string reference, out int fr, out int fc, out int lr, out int lc)
    {
        fr = fc = lr = lc = 0;
        var parts = reference.Replace("$", "").Split(':');
        if (parts.Length is < 1 or > 2) return;
        if (!Cell(parts[0], out int r1, out int c1)) return;
        int r2 = r1, c2 = c1;
        if (parts.Length == 2 && !Cell(parts[1], out r2, out c2)) return;
        fr = Math.Min(r1, r2); lr = Math.Max(r1, r2);
        fc = Math.Min(c1, c2); lc = Math.Max(c1, c2);
    }

    private static bool Cell(string a1, out int row, out int col)
    {
        row = col = 0;
        int i = 0;
        while (i < a1.Length && char.IsAsciiLetter(a1[i]))
        {
            col = col * 26 + (char.ToUpperInvariant(a1[i]) - 'A' + 1);
            if (col > 16384) return false;
            i++;
        }
        if (i == 0 || i == a1.Length) return false;
        if (!int.TryParse(a1.AsSpan(i), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out row)) return false;
        return row is >= 1 and <= 1_048_576;
    }

    public override string ToString() => $"{Reference} → {Target}{(Location is null ? "" : "#" + Location)}";
}
