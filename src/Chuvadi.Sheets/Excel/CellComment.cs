using System;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Where a comment's box sits when shown (the VML &lt;x:Anchor&gt;): from a cell corner plus a
/// pixel offset to another. Columns and rows are 1-based (the file stores 0-based values);
/// offsets are pixels at 96 DPI from the cell's left or top edge.
/// </summary>
public readonly record struct CommentAnchor(int LeftColumn, int LeftOffset, int TopRow, int TopOffset,
                                            int RightColumn, int RightOffset, int BottomRow, int BottomOffset);

/// <summary>
/// A cell comment (a note: xl/commentsN.xml plus its VML shape), since 1.7.0. Text is kept as
/// stored, including the bold "Author:" run Excel puts at the start; <see cref="RichText"/>
/// has the runs when the comment is formatted. For Excel 365 threaded comments this is the
/// legacy note Excel writes alongside them. Immutable apart from the init properties.
/// </summary>
public sealed class CellComment
{
    /// <param name="reference">The cell, e.g. "B3".</param>
    /// <param name="author">The author (may be empty).</param>
    /// <param name="text">The comment text.</param>
    public CellComment(string reference, string author, string text)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("Reference required.", nameof(reference));
        var (row, col) = CellAddress.ParseA1(reference.Replace("$", ""));
        Reference = reference; Row = row; Column = col;
        Author = author ?? throw new ArgumentNullException(nameof(author));
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <param name="reference">The cell, e.g. "B3".</param>
    /// <param name="author">The author (may be empty).</param>
    /// <param name="text">The comment text as runs with fonts.</param>
    public CellComment(string reference, string author, RichText text)
        : this(reference, author, (text ?? throw new ArgumentNullException(nameof(text))).Text)
    {
        RichText = text;
    }

    /// <summary>The commented cell as stored, e.g. "B3".</summary>
    public string Reference { get; }

    /// <summary>The commented cell, 1-based.</summary>
    public int Row { get; }
    public int Column { get; }

    public string Author { get; }

    /// <summary>The plain text (runs joined; line breaks as '\n').</summary>
    public string Text { get; }

    /// <summary>The runs, when the comment text is stored as runs (Excel always writes runs); null for plain text.</summary>
    public RichText? RichText { get; private set; }

    /// <summary>The box position when shown, from the VML drawing; null when the file has none (Excel then places it next to the cell).</summary>
    public CommentAnchor? Anchor { get; init; }

    /// <summary>True if the comment is always shown (not only on hover).</summary>
    public bool Visible { get; init; }

    internal static CellComment Loaded(string reference, string author, string text, RichText? rich, CommentAnchor? anchor, bool visible)
        => new(reference, author, text) { RichText = rich, Anchor = anchor, Visible = visible };

    public override string ToString() => $"{Reference} ({Author}): {Text}";
}
