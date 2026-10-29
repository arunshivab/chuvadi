using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Docs.Word;

/// <summary>
/// One paragraph: a sequence of runs (text spans with their own formatting) plus
/// paragraph-level properties (style, alignment, list membership, page-break-before).
///
/// Fluent usage:
/// <code>
/// doc.AddParagraph("Quarterly Report", ParagraphStyle.Title);
/// doc.AddParagraph()
///    .Text("Revenue grew ")
///    .Text("18%", TextFormat.BoldText)
///    .Text(" year over year.");
/// </code>
/// </summary>
public sealed class Paragraph
{
    private readonly List<Run> _runs = new();

    private ParagraphStyle _style = ParagraphStyle.Normal;
    private string? _customStyleId;

    /// <summary>
    /// The paragraph's built-in style. For a custom style (see <see cref="StyleId"/>) this reads
    /// <see cref="ParagraphStyle.Normal"/>. Setting it clears any custom style.
    /// </summary>
    public ParagraphStyle Style
    {
        get => _style;
        set { _style = value; _customStyleId = null; }
    }

    /// <summary>
    /// The paragraph style id (w:pStyle): a built-in ("Normal", "Heading1"…) or any style in
    /// <see cref="Document.Styles"/>. Setting a built-in id also sets <see cref="Style"/>.
    /// </summary>
    public string StyleId
    {
        get => _customStyleId ?? Internal.DocumentSerializer.StyleId(_style);
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Style id required.", nameof(value));
            if (Internal.DocumentSerializer.TryBuiltInStyle(value, out var builtIn)) { _style = builtIn; _customStyleId = null; }
            else { _style = ParagraphStyle.Normal; _customStyleId = value; }
        }
    }

    /// <summary>All paragraph-level formatting (spacing, indents, borders, tabs…). Owned by this paragraph.</summary>
    public ParagraphFormat Format { get; } = new();

    /// <summary>
    /// Horizontal alignment — a shortcut for <see cref="ParagraphFormat.Alignment"/>
    /// (Left = inherit; reading returns Left when unset).
    /// </summary>
    public ParagraphAlignment Alignment
    {
        get => Format.Alignment ?? ParagraphAlignment.Left;
        set => Format.Alignment = value == ParagraphAlignment.Left ? null : value;
    }

    /// <summary>Copies every set value of <paramref name="format"/> onto <see cref="Format"/> (see <see cref="ParagraphFormat.ApplyFrom"/>).</summary>
    public Paragraph ApplyFormat(ParagraphFormat format)
    {
        Format.ApplyFrom(format);
        return this;
    }

    /// <summary>Fluent access to <see cref="Format"/>: <c>doc.AddParagraph("x").Configure(f => f.SpaceAfterPt = 6)</c>.</summary>
    public Paragraph Configure(Action<ParagraphFormat> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        configure(Format);
        return this;
    }

    /// <summary>When set, this paragraph is a list item at <see cref="ListLevel"/>.</summary>
    public ListKind List { get; set; } = ListKind.None;

    /// <summary>0-based list indent level (0..8). Only used when <see cref="List"/> is set.</summary>
    public int ListLevel { get; set; }

    /// <summary>Emit a page break immediately before this paragraph.</summary>
    public bool PageBreakBefore { get; set; }

    public IReadOnlyList<Run> Runs => _runs;

    public Paragraph() { }

    public Paragraph(string text, TextFormat? format = null)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        _runs.Add(new Run(text, format ?? TextFormat.None));
    }

    /// <summary>
    /// Appends a text run. Line feeds (<c>\n</c>, <c>\r\n</c>, <c>\r</c>) in the text are
    /// written as line breaks and tab characters as tabs, so multi-line strings render as
    /// they read; the run's text itself is stored unchanged.
    /// </summary>
    public Paragraph Text(string text, TextFormat? format = null)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        _runs.Add(new Run(text, format ?? TextFormat.None));
        return this;
    }

    /// <summary>Appends a text run that uses the character style <paramref name="characterStyleId"/> (see <see cref="Document.Styles"/>), with optional direct formatting on top.</summary>
    public Paragraph StyledText(string text, string characterStyleId, TextFormat? format = null)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (string.IsNullOrWhiteSpace(characterStyleId)) throw new ArgumentException("Style id required.", nameof(characterStyleId));
        _runs.Add(new Run(text, format ?? TextFormat.None) { CharacterStyleId = characterStyleId });
        return this;
    }

    /// <summary>Appends an external hyperlink run (rendered with Word's Hyperlink style: blue + underlined).</summary>
    public Paragraph Hyperlink(string text, string url) => Hyperlink(text, url, TextFormat.None);

    /// <summary>Appends an external hyperlink run with extra formatting layered over the Hyperlink style.</summary>
    public Paragraph Hyperlink(string text, string url, TextFormat format)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Text required.", nameof(text));
        if (string.IsNullOrEmpty(url)) throw new ArgumentException("URL required.", nameof(url));
        _runs.Add(new Run(text, format ?? TextFormat.None) { HyperlinkUrl = url });
        return this;
    }

    /// <summary>Appends a break: line (default), page, column, or a text-wrapping break that clears floating objects.</summary>
    public Paragraph Break(BreakType type = BreakType.Line, TextFormat? format = null)
    {
        _runs.Add(Run.BreakRun(type, format ?? TextFormat.None));
        return this;
    }

    /// <summary>Appends a line break (Shift+Enter). Same as <see cref="Break(BreakType, TextFormat?)"/> with <see cref="BreakType.Line"/>.</summary>
    public Paragraph LineBreak() => Break(BreakType.Line);

    /// <summary>Appends a tab character, aligned to the paragraph's tab stops.</summary>
    public Paragraph Tab(TextFormat? format = null)
    {
        _runs.Add(Run.TabRun(format ?? TextFormat.None));
        return this;
    }

    /// <summary>Appends an image run (inline, or floating anchored to this paragraph).</summary>
    public Paragraph Image(ImageSpec image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        _runs.Add(new Run(string.Empty, TextFormat.None) { Image = image });
        return this;
    }

    /// <summary>Appends a "Page N" / "Page N of M" field sequence (meaningful in headers/footers).</summary>
    public Paragraph PageNumber(bool includeTotal = false)
    {
        _runs.Add(new Run("Page ", TextFormat.None));
        _runs.Add(Run.Field(" PAGE "));
        if (includeTotal)
        {
            _runs.Add(new Run(" of ", TextFormat.None));
            _runs.Add(Run.Field(" NUMPAGES "));
        }
        return this;
    }

    /// <summary>All run text concatenated (fields contribute their placeholder value "1").</summary>
    public string GetText()
    {
        var sb = new StringBuilder();
        foreach (var r in _runs) sb.Append(r.TextContent);
        return sb.ToString();
    }

    internal void AddRun(Run run) => _runs.Add(run);
}

/// <summary>
/// One run: a contiguous span of text sharing one <see cref="TextFormat"/>. May instead be
/// a simple field (PAGE / NUMPAGES), an image, an explicit break or tab, or carry a
/// hyperlink target.
/// </summary>
public sealed class Run
{
    public string TextContent { get; }
    public TextFormat Format { get; }

    /// <summary>Non-null when this run is an external hyperlink.</summary>
    public string? HyperlinkUrl { get; init; }

    /// <summary>Non-null when this run is a simple field; holds the field instruction
    /// (e.g. " PAGE ", " NUMPAGES ").</summary>
    public string? FieldInstruction { get; private init; }

    /// <summary>Non-null when this run is an image (inline or floating).</summary>
    public ImageSpec? Image { get; init; }

    /// <summary>Character style applied to the run (w:rStyle), below its direct <see cref="Format"/>. Null = none (hyperlinks use "Hyperlink").</summary>
    public string? CharacterStyleId { get; init; }

    /// <summary>Non-null when this run is an explicit break (<see cref="TextContent"/> is "\n").</summary>
    public BreakType? Break { get; private init; }

    /// <summary>True when this run is an explicit tab (<see cref="TextContent"/> is "\t").</summary>
    public bool IsTab { get; private init; }

    public Run(string text, TextFormat format)
    {
        TextContent = text ?? throw new ArgumentNullException(nameof(text));
        Format = format ?? TextFormat.None;
    }

    internal static Run Field(string instruction)
        => new("1", TextFormat.None) { FieldInstruction = instruction };

    internal static Run BreakRun(BreakType type, TextFormat format, string? characterStyleId = null)
        => new("\n", format) { Break = type, CharacterStyleId = characterStyleId };

    internal static Run TabRun(TextFormat format)
        => new("\t", format) { IsTab = true };
}
