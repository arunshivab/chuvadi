using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// The font of a rich-text run (&lt;rPr&gt; in a shared or inline string), exactly as stored.
/// Every property is null when the run does not state it; a run with no &lt;rPr&gt; at all
/// has no <see cref="RichTextRun.Font"/>. What an unstated property shows as is the
/// renderer's choice (Excel uses the cell's font for a run without &lt;rPr&gt;).
/// Immutable once built; create one with an object initializer.
/// </summary>
public sealed class RichTextFont : IEquatable<RichTextFont>
{
    /// <summary>Face name (&lt;rFont val&gt;), e.g. "Calibri".</summary>
    public string? Name { get; init; }

    /// <summary>Size in points (&lt;sz val&gt;).</summary>
    public double? Size { get; init; }

    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Strikethrough { get; init; }

    /// <summary>Underline (&lt;u val&gt;); <see cref="UnderlineStyle.None"/> when stated as none.</summary>
    public UnderlineStyle? Underline { get; init; }

    /// <summary>Superscript / subscript (&lt;vertAlign val&gt;).</summary>
    public FontVerticalAlign? VerticalAlign { get; init; }

    /// <summary>
    /// Text colour as stored (RGB, theme slot with tint, or indexed palette). Resolve it to
    /// RGB with <see cref="CellStyleTable.ResolveColor"/> of the workbook's style table, or
    /// read <see cref="RichTextRun.ResolvedColor"/> on a loaded run.
    /// </summary>
    public ColorRef? Color { get; init; }

    /// <summary>Font family class (&lt;family val&gt;: 1 Roman, 2 Swiss, 3 Modern, 4 Script, 5 Decorative).</summary>
    public int? Family { get; init; }

    /// <summary>Character set (&lt;charset val&gt;).</summary>
    public int? Charset { get; init; }

    /// <summary>Theme font scheme (&lt;scheme val&gt;).</summary>
    public FontScheme? Scheme { get; init; }

    public bool? Outline { get; init; }
    public bool? Shadow { get; init; }
    public bool? Condense { get; init; }
    public bool? Extend { get; init; }

    public bool Equals(RichTextFont? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Size == other.Size && Bold == other.Bold && Italic == other.Italic
            && Strikethrough == other.Strikethrough && Underline == other.Underline && VerticalAlign == other.VerticalAlign
            && Family == other.Family && Charset == other.Charset && Scheme == other.Scheme
            && Outline == other.Outline && Shadow == other.Shadow && Condense == other.Condense && Extend == other.Extend
            && ColorEquals(Color, other.Color);
    }

    private static bool ColorEquals(ColorRef? a, ColorRef? b)
        => a is null ? b is null
         : b is not null && a.Kind == b.Kind && a.Rgb == b.Rgb && a.Theme == b.Theme && a.Indexed == b.Indexed && a.Tint == b.Tint;

    public override bool Equals(object? obj) => Equals(obj as RichTextFont);

    public override int GetHashCode() => HashCode.Combine(Name, Size, Bold, Italic, Underline, VerticalAlign, Color?.ToString());

    public override string ToString()
    {
        var parts = new List<string>();
        if (Name is not null) parts.Add(Name);
        if (Size is double s) parts.Add(s.ToString(System.Globalization.CultureInfo.InvariantCulture) + "pt");
        if (Bold == true) parts.Add("bold");
        if (Italic == true) parts.Add("italic");
        if (Underline is { } u && u != UnderlineStyle.None) parts.Add("underline:" + u);
        if (Strikethrough == true) parts.Add("strike");
        if (VerticalAlign is { } va && va != FontVerticalAlign.Baseline) parts.Add(va.ToString());
        if (Color is not null) parts.Add(Color.ToString());
        return string.Join(" ", parts);
    }
}

/// <summary>One run of a rich-text cell: a piece of text and its font.</summary>
public sealed class RichTextRun
{
    /// <param name="text">The run's text, exactly as stored (may be empty, may contain line breaks).</param>
    /// <param name="font">The run's font, or null for a run without &lt;rPr&gt; (it shows in the cell's font).</param>
    public RichTextRun(string text, RichTextFont? font = null)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Font = font;
    }

    public string Text { get; }

    /// <summary>The run's font as stored, or null when the run has no &lt;rPr&gt;.</summary>
    public RichTextFont? Font { get; }

    /// <summary>
    /// For a run read from a file: <see cref="RichTextFont.Color"/> resolved to 6-digit RGB hex
    /// with the workbook's theme and palette; null when the run states no colour (or for runs
    /// built in code, which have no workbook to resolve against).
    /// </summary>
    public string? ResolvedColor { get; internal set; }

    public override string ToString() => Font is null ? Text : $"[{Font}] {Text}";
}

/// <summary>
/// Rich text: a cell string made of runs with their own fonts (since 1.7.0). Read from shared
/// strings (&lt;si&gt; with &lt;r&gt; runs) and inline strings; phonetic guides (&lt;rPh&gt;)
/// are not part of it. The cell's value stays the plain <see cref="Text"/>, so code that reads
/// strings is unaffected. Write it by passing a RichText as a cell value to the streaming
/// writer, or by setting <see cref="Cell.RichText"/>. Immutable.
/// </summary>
public sealed class RichText : IEquatable<RichText>
{
    private readonly RichTextRun[] _runs;

    public RichText(IEnumerable<RichTextRun> runs)
    {
        if (runs is null) throw new ArgumentNullException(nameof(runs));
        _runs = runs.ToArray();
        foreach (var r in _runs) if (r is null) throw new ArgumentException("Runs cannot contain null.", nameof(runs));
        if (_runs.Length == 0) throw new ArgumentException("Rich text needs at least one run.", nameof(runs));
        var sb = new StringBuilder();
        foreach (var r in _runs) sb.Append(r.Text);
        Text = sb.ToString();
    }

    public RichText(params RichTextRun[] runs) : this((IEnumerable<RichTextRun>)runs) { }

    /// <summary>The runs, in order.</summary>
    public IReadOnlyList<RichTextRun> Runs => _runs;

    /// <summary>The plain text: all runs' text joined (the cell's value).</summary>
    public string Text { get; }

    public bool Equals(RichText? other)
    {
        if (other is null || other._runs.Length != _runs.Length) return false;
        for (int i = 0; i < _runs.Length; i++)
        {
            var a = _runs[i]; var b = other._runs[i];
            if (a.Text != b.Text) return false;
            if (a.Font is null ? b.Font is not null : !a.Font.Equals(b.Font)) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as RichText);

    public override int GetHashCode() => HashCode.Combine(Text, _runs.Length);

    public override string ToString() => Text;
}
