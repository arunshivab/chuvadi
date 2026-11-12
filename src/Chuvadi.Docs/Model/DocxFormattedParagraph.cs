using System.Collections.Generic;

namespace Chuvadi.Docs.Model;

/// <summary>
/// One paragraph from <see cref="DocxReader.FormattedParagraphs()"/>: the paragraph as <see cref="Document.Load(System.IO.Stream, string?)"/>
/// builds it, with the effective formatting of each run and of the paragraph already resolved (Docs 2.2.0, D-336).
/// <para>
/// <see cref="RunFormats"/>[i] belongs to <see cref="Paragraph"/>.<see cref="Paragraph.Runs"/>[i]: document defaults,
/// then the table style (for a paragraph in a table cell), the paragraph style chain, the character style chain and
/// the run's direct formatting, exactly as <see cref="Document.GetEffectiveRunFormat(Paragraph, Run, TableCellRef)"/>
/// resolves it. Theme font and colour references stay references; <see cref="DocxReader.GetThemeFontName(ThemeFont)"/>
/// and <see cref="DocxReader.GetThemeColorHex(ThemeColor, byte?, byte?)"/> turn them into names and colours.
/// </para>
/// <para>
/// The objects are a read snapshot: the paragraph belongs to no document, and changing it changes nothing in the
/// file. They are not shared with later items, so a caller may keep or drop each one.
/// </para>
/// </summary>
public sealed class DocxFormattedParagraph
{
    internal DocxFormattedParagraph(Paragraph paragraph, IReadOnlyList<TextFormat> runFormats, ParagraphFormat paragraphFormat,
        string part, TableCellRef? cell)
    {
        Paragraph = paragraph;
        RunFormats = runFormats;
        ParagraphFormat = paragraphFormat;
        Part = part;
        Cell = cell;
    }

    /// <summary>
    /// The paragraph with every run as <see cref="Document.Load(System.IO.Stream, string?)"/> reads it: text, direct
    /// formatting, hyperlinks, fields, bookmarks, symbols, pictures, text boxes and tracked changes
    /// (<see cref="Run.IsDeleted"/>). Footnote, endnote and comment references are not included: those parts are not read.
    /// </summary>
    public Paragraph Paragraph { get; }

    /// <summary>The effective formatting of each run, in the order of <see cref="Paragraph.Runs"/> (one entry per run).</summary>
    public IReadOnlyList<TextFormat> RunFormats { get; }

    /// <summary>The paragraph's effective formatting (defaults, table style, list level and style chain, then direct), as <see cref="Document.GetEffectiveParagraphFormat(Paragraph)"/>.</summary>
    public ParagraphFormat ParagraphFormat { get; }

    /// <summary>The part the paragraph comes from, for example <c>/word/document.xml</c> or <c>/word/header1.xml</c>.</summary>
    public string Part { get; }

    /// <summary>
    /// For a paragraph inside a table cell, the cell: the innermost table (with its rows, read as one unit), the 0-based
    /// row and the 0-based grid column of the cell's first column. Null for a paragraph outside tables.
    /// </summary>
    public TableCellRef? Cell { get; }

    /// <summary>The paragraph's text (<see cref="Paragraph.GetText()"/>).</summary>
    public string Text => Paragraph.GetText();
}
