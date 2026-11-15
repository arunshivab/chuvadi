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
        string part, TableCellRef? cell, ListLabel? listLabel, Note? note, Comment? comment, TextBox? textBox, Paragraph? host)
    {
        Paragraph = paragraph;
        RunFormats = runFormats;
        ParagraphFormat = paragraphFormat;
        Part = part;
        Cell = cell;
        ListLabel = listLabel;
        Note = note;
        Comment = comment;
        TextBox = textBox;
        Host = host;
    }

    /// <summary>
    /// The paragraph with every run as <see cref="Document.Load(System.IO.Stream, string?)"/> reads it: text, direct
    /// formatting, hyperlinks, fields, bookmarks, symbols, pictures, text boxes and tracked changes
    /// (<see cref="Run.IsDeleted"/>). Footnote and endnote reference runs are included only with
    /// <see cref="DocxReaderOptions.IncludeNotes"/>, and comment reference runs only with
    /// <see cref="DocxReaderOptions.IncludeComments"/>: without them those parts are not read.
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

    /// <summary>
    /// The paragraph's list label ("1.", "a)", "•"), as <see cref="Document.GetListLabel(Paragraph)"/> gives it after
    /// <c>Load</c>, counted as the stream goes (2.4.0, D-387): one count for the body, one per header or footer part,
    /// one for all footnotes and one for all endnotes, as Word counts. Null for a paragraph that is not in a list, and
    /// for comment and text-box paragraphs (as <c>GetListLabel</c>).
    /// </summary>
    public ListLabel? ListLabel { get; }

    /// <summary>For a footnote or endnote paragraph (<see cref="DocxReaderOptions.IncludeNotes"/>), its note; null otherwise.</summary>
    public Note? Note { get; }

    /// <summary>For a comment paragraph (<see cref="DocxReaderOptions.IncludeComments"/>), its comment; null otherwise.</summary>
    public Comment? Comment { get; }

    /// <summary>
    /// For a text-box paragraph (<see cref="DocxReaderOptions.IncludeTextBoxes"/>), the text box; null otherwise.
    /// <see cref="Cell"/> is then the cell of a table inside the box, if any.
    /// </summary>
    public TextBox? TextBox { get; }

    /// <summary>For a text-box paragraph, the paragraph whose run holds the box (<see cref="Run.TextBox"/>); null otherwise.</summary>
    public Paragraph? Host { get; }

    /// <summary>The paragraph's text (<see cref="Paragraph.GetText()"/>).</summary>
    public string Text => Paragraph.GetText();
}
