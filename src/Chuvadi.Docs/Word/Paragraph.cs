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

    public ParagraphStyle Style { get; set; } = ParagraphStyle.Normal;
    public ParagraphAlignment Alignment { get; set; } = ParagraphAlignment.Left;

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

    /// <summary>Appends a text run.</summary>
    public Paragraph Text(string text, TextFormat? format = null)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        _runs.Add(new Run(text, format ?? TextFormat.None));
        return this;
    }

    /// <summary>Appends an external hyperlink run (rendered blue + underlined).</summary>
    public Paragraph Hyperlink(string text, string url)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Text required.", nameof(text));
        if (string.IsNullOrEmpty(url)) throw new ArgumentException("URL required.", nameof(url));
        _runs.Add(new Run(text, TextFormat.None) { HyperlinkUrl = url });
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
/// a simple field (PAGE / NUMPAGES) or carry a hyperlink target.
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

    public Run(string text, TextFormat format)
    {
        TextContent = text ?? throw new ArgumentNullException(nameof(text));
        Format = format ?? TextFormat.None;
    }

    internal static Run Field(string instruction)
        => new("1", TextFormat.None) { FieldInstruction = instruction };
}
