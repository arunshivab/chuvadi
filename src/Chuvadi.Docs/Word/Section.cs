using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>How a section starts relative to the previous one (w:type).</summary>
public enum SectionStart
{
    /// <summary>On a new page (Word's default).</summary>
    NextPage,
    /// <summary>On the same page (e.g. to switch the number of columns mid-page).</summary>
    Continuous,
    /// <summary>On the next even-numbered page.</summary>
    EvenPage,
    /// <summary>On the next odd-numbered page (chapters).</summary>
    OddPage,
    /// <summary>In the next column.</summary>
    NextColumn,
}

/// <summary>Vertical alignment of text on the page (w:vAlign).</summary>
public enum SectionVerticalAlignment { Top, Center, Justified, Bottom }

/// <summary>Separator between a chapter number and the page number (w:pgNumType/@w:chapSep).</summary>
public enum ChapterSeparator { Hyphen, Period, Colon, EmDash, EnDash }

/// <summary>When line numbers restart (w:lnNumType/@w:restart).</summary>
public enum LineNumberRestart { EachPage, EachSection, Continuous }

/// <summary>What page borders are measured from (w:pgBorders/@w:offsetFrom).</summary>
public enum PageBorderOffset { Text, Page }

/// <summary>Which pages show the page borders (w:pgBorders/@w:display).</summary>
public enum PageBorderDisplay { AllPages, FirstPage, NotFirstPage }

/// <summary>East Asian document grid (w:docGrid/@w:type).</summary>
public enum DocumentGridType { Default, Lines, LinesAndChars, SnapToChars }

/// <summary>One column of a section with unequal columns (w:col).</summary>
/// <param name="WidthPt">Column width.</param>
/// <param name="SpaceAfterPt">Space to the next column.</param>
public readonly record struct ColumnDefinition(double WidthPt, double SpaceAfterPt);

/// <summary>Text columns of a section (w:cols). Everything null = one column.</summary>
public sealed class SectionColumns
{
    /// <summary>Number of equal columns (w:num).</summary>
    public int? Count { get; set; }

    /// <summary>Space between equal columns (w:space). Word's default is 36pt.</summary>
    public double? SpacingPt { get; set; }

    /// <summary>A vertical line between columns (w:sep).</summary>
    public bool? Separator { get; set; }

    /// <summary>Equal column widths (w:equalWidth). Set false and fill <see cref="Columns"/> for unequal columns.</summary>
    public bool? EqualWidth { get; set; }

    /// <summary>Individual columns when <see cref="EqualWidth"/> is false.</summary>
    public List<ColumnDefinition> Columns { get; } = new();

    internal bool IsDefault => Count is null && SpacingPt is null && Separator is null && EqualWidth is null && Columns.Count == 0;

    internal SectionColumns Clone()
    {
        var c = new SectionColumns { Count = Count, SpacingPt = SpacingPt, Separator = Separator, EqualWidth = EqualWidth };
        c.Columns.AddRange(Columns);
        return c;
    }
}

/// <summary>Page numbering of a section (w:pgNumType): format, restart value and chapter numbering.</summary>
public sealed class PageNumbering
{
    /// <summary>Number format of page numbers (w:fmt), e.g. <see cref="NumberFormat.LowerRoman"/> for front matter.</summary>
    public NumberFormat? Format { get; set; }

    /// <summary>Restart page numbering at this value in this section (w:start). Null = continue.</summary>
    public int? Start { get; set; }

    /// <summary>Heading level (1–9) whose number prefixes page numbers (w:chapStyle).</summary>
    public int? ChapterStyle { get; set; }

    /// <summary>Separator between chapter and page number (w:chapSep).</summary>
    public ChapterSeparator? ChapterSeparator { get; set; }

    internal bool IsDefault => Format is null && Start is null && ChapterStyle is null && ChapterSeparator is null;

    internal PageNumbering Clone() => (PageNumbering)MemberwiseClone();
}

/// <summary>Line numbering in the margin (w:lnNumType).</summary>
public sealed class LineNumbering
{
    /// <summary>Show every n-th line number (w:countBy). Default 1.</summary>
    public int CountBy { get; set; } = 1;

    /// <summary>First line number (w:start, 0-based in the file: 0 shows 1).</summary>
    public int? Start { get; set; }

    /// <summary>Distance between the numbers and the text (w:distance).</summary>
    public double? DistancePt { get; set; }

    /// <summary>When numbering restarts (w:restart). Null = each page.</summary>
    public LineNumberRestart? Restart { get; set; }

    internal LineNumbering Clone() => (LineNumbering)MemberwiseClone();
}

/// <summary>Borders around the page (w:pgBorders).</summary>
public sealed class PageBorders
{
    public BorderSpec? Top { get; set; }
    public BorderSpec? Left { get; set; }
    public BorderSpec? Bottom { get; set; }
    public BorderSpec? Right { get; set; }

    /// <summary>Measure border spacing from the text or the page edge (w:offsetFrom). Null = text.</summary>
    public PageBorderOffset? OffsetFrom { get; set; }

    /// <summary>Which pages have borders (w:display). Null = all.</summary>
    public PageBorderDisplay? Display { get; set; }

    /// <summary>Draw the borders behind (false) or in front of (true, default) text (w:zOrder).</summary>
    public bool? InFront { get; set; }

    /// <summary>The same border on all four sides.</summary>
    public static PageBorders Box(BorderSpec line) => new() { Top = line, Left = line, Bottom = line, Right = line };

    internal PageBorders Clone() => (PageBorders)MemberwiseClone();
}

/// <summary>East Asian document grid (w:docGrid): lines per page and characters per line.</summary>
public sealed class DocumentGrid
{
    public DocumentGridType? Type { get; set; }

    /// <summary>Line pitch (w:linePitch). Word writes 18pt (360 twips) by default.</summary>
    public double? LinePitchPt { get; set; }

    /// <summary>Character pitch adjustment (w:charSpace), in Word's raw units.</summary>
    public int? CharacterSpace { get; set; }

    internal DocumentGrid Clone() => (DocumentGrid)MemberwiseClone();
}

/// <summary>
/// A text watermark ("DRAFT", "CONFIDENTIAL") behind the page text, written as Word writes
/// it: a WordArt shape in the section's headers.
/// </summary>
public sealed class TextWatermark
{
    public TextWatermark(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Watermark text required.", nameof(text));
        Text = text;
    }

    /// <summary>The watermark text.</summary>
    public string Text { get; set; }

    /// <summary>Font family. Default Calibri.</summary>
    public string Font { get; set; } = "Calibri";

    /// <summary>Fill colour (RRGGBB). Default C0C0C0 (silver).</summary>
    public string ColorHex { get; set; } = "C0C0C0";

    /// <summary>Opacity 0–1. Default 0.5 (Word's "semitransparent"); 1 = solid.</summary>
    public double Opacity { get; set; } = 0.5;

    /// <summary>Diagonal (default) or horizontal.</summary>
    public bool Diagonal { get; set; } = true;

    /// <summary>Shape width; null = sized from the text and the page's text width, as Word's "Auto".</summary>
    public double? WidthPt { get; set; }

    /// <summary>Shape height; null = from the width and the text length.</summary>
    public double? HeightPt { get; set; }

    internal TextWatermark Clone() => (TextWatermark)MemberwiseClone();
}

/// <summary>
/// One section of a document (w:sectPr): page setup, columns, headers and footers (default,
/// first page, even pages), page numbering, line numbering, page borders, vertical alignment,
/// text direction, document grid and a text watermark. The last section is
/// <see cref="Document.Section"/>; earlier ones end at a paragraph
/// (<see cref="Paragraph.SectionProperties"/>), created with <see cref="Document.AddSectionBreak"/>.
/// </summary>
public sealed class Section
{
    /// <summary>Page size, orientation and margins.</summary>
    public PageSetup Page { get; private set; } = new();

    /// <summary>How the section starts (w:type). Null = next page.</summary>
    public SectionStart? Start { get; set; }

    /// <summary>Text columns.</summary>
    public SectionColumns Columns { get; private set; } = new();

    /// <summary>Page numbering format, restart and chapter numbering.</summary>
    public PageNumbering PageNumbering { get; private set; } = new();

    /// <summary>Line numbers in the margin; null = none.</summary>
    public LineNumbering? LineNumbering { get; set; }

    /// <summary>Page borders; null = none.</summary>
    public PageBorders? PageBorders { get; set; }

    /// <summary>Vertical alignment of text on the page (w:vAlign). Null = top.</summary>
    public SectionVerticalAlignment? VerticalAlignment { get; set; }

    /// <summary>Text flow of the section (w:textDirection), e.g. vertical East Asian text.</summary>
    public CellTextDirection? TextDirection { get; set; }

    /// <summary>Right-to-left section layout (w:bidi): columns and page numbers run right to left.</summary>
    public bool? RightToLeft { get; set; }

    /// <summary>Gutter on the right (w:rtlGutter), for right-to-left binding.</summary>
    public bool? RightToLeftGutter { get; set; }

    /// <summary>Protect form fields in this section when forms protection is on (w:formProt).</summary>
    public bool? FormProtection { get; set; }

    /// <summary>Suppress endnotes in this section (w:noEndnote).</summary>
    public bool? NoEndnotes { get; set; }

    /// <summary>East Asian document grid; null = none.</summary>
    public DocumentGrid? Grid { get; set; }

    /// <summary>Default header (all pages, or pages after the first when the first page differs; odd pages when even pages differ).</summary>
    public HeaderFooterContent Header { get; private set; } = new();

    /// <summary>Default footer.</summary>
    public HeaderFooterContent Footer { get; private set; } = new();

    /// <summary>First-page header (used when <see cref="DifferentFirstPage"/>).</summary>
    public HeaderFooterContent FirstPageHeader { get; private set; } = new();

    /// <summary>First-page footer.</summary>
    public HeaderFooterContent FirstPageFooter { get; private set; } = new();

    /// <summary>Even-page header (used when <see cref="Document.DifferentOddAndEvenPages"/>).</summary>
    public HeaderFooterContent EvenPageHeader { get; private set; } = new();

    /// <summary>Even-page footer.</summary>
    public HeaderFooterContent EvenPageFooter { get; private set; } = new();

    /// <summary>
    /// Different first page (w:titlePg). Null = automatic: on when the first-page header or
    /// footer has content.
    /// </summary>
    public bool? TitlePage { get; set; }

    /// <summary>Whether the first page uses the first-page header/footer.</summary>
    public bool DifferentFirstPage => TitlePage ?? (FirstPageHeader.HasContent || FirstPageFooter.HasContent);

    private TextWatermark? _watermark;

    /// <summary>A text watermark on every page of the section (in its default, first-page and even-page headers); null = none.</summary>
    public TextWatermark? Watermark
    {
        get => _watermark;
        set { _watermark = value; WatermarkSlots = null; }
    }

    /// <summary>Loader: the header types ("default", "first", "even") that carried the watermark in the file; null = all that apply.</summary>
    internal HashSet<string>? WatermarkSlots { get; set; }

    /// <summary>footnotePr / endnotePr carried verbatim from a loaded file (modelled with notes).</summary>
    internal List<string> RawNoteProperties { get; } = new();

    /// <summary>
    /// A copy of the layout settings (page, columns, numbering, borders…) without headers,
    /// footers or watermark.
    /// </summary>
    public Section CloneLayout()
    {
        var c = new Section
        {
            Page = Page.Clone(), Start = Start, Columns = Columns.Clone(), PageNumbering = PageNumbering.Clone(),
            LineNumbering = LineNumbering?.Clone(), PageBorders = PageBorders?.Clone(), VerticalAlignment = VerticalAlignment,
            TextDirection = TextDirection, RightToLeft = RightToLeft, RightToLeftGutter = RightToLeftGutter,
            FormProtection = FormProtection, NoEndnotes = NoEndnotes, Grid = Grid?.Clone(), TitlePage = TitlePage,
        };
        c.RawNoteProperties.AddRange(RawNoteProperties);
        return c;
    }

    /// <summary>All six header/footer slots with their kind.</summary>
    internal IEnumerable<(HeaderFooterContent Content, bool IsHeader, string Type)> Slots()
    {
        yield return (Header, true, "default");
        yield return (Footer, false, "default");
        yield return (FirstPageHeader, true, "first");
        yield return (FirstPageFooter, false, "first");
        yield return (EvenPageHeader, true, "even");
        yield return (EvenPageFooter, false, "even");
    }

    /// <summary>Loader: shares one loaded header/footer part between the sections that reference it.</summary>
    internal void SetSlot(bool isHeader, string type, HeaderFooterContent content)
    {
        switch (isHeader, type)
        {
            case (true, "first"): FirstPageHeader = content; break;
            case (false, "first"): FirstPageFooter = content; break;
            case (true, "even"): EvenPageHeader = content; break;
            case (false, "even"): EvenPageFooter = content; break;
            case (true, _): Header = content; break;
            default: Footer = content; break;
        }
    }

    /// <summary>The slot for a kind and type.</summary>
    internal HeaderFooterContent Slot(bool isHeader, string type) => (isHeader, type) switch
    {
        (true, "first") => FirstPageHeader,
        (false, "first") => FirstPageFooter,
        (true, "even") => EvenPageHeader,
        (false, "even") => EvenPageFooter,
        (true, _) => Header,
        _ => Footer,
    };
}
