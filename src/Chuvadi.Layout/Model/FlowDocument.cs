using System;
using System.Collections.Generic;

namespace Chuvadi.Layout.Model
{
    /// <summary>
    /// The flow-document IR: what a word-processing document looks like after style resolution. Importers
    /// (docx, txt, md) produce it; LiPi and other callers may build it directly. The engine treats it as
    /// read-only; do not modify a document while a <see cref="LaidOutDocument"/> uses it.
    /// </summary>
    public sealed class FlowDocument
    {
        public IList<FlowSection> Sections { get; } = new List<FlowSection>();

        /// <summary>Default tab stop interval in points (w:defaultTabStop; Word default 36 pt).</summary>
        public double DefaultTabStop { get; set; } = 36;

        /// <summary>Page count the producing application recorded (docProps/app.xml Pages), used as the initial estimate.</summary>
        public int? PageCountHint { get; set; }

        public string? Title { get; set; }

        /// <summary>
        /// Fonts that come with the document (a .docx's embedded fonts). Searched before every other source when
        /// <see cref="LayoutOptions.UseEmbeddedFonts"/> is on; null = none.
        /// </summary>
        public Fonts.IFontSource? EmbeddedFonts { get; set; }

        /// <summary>Number of blocks across all sections, counting table rows and nested content.</summary>
        public long CountBlocks()
        {
            long n = 0;
            foreach (FlowSection s in Sections)
            {
                n += FlowBlock.Count(s.Blocks);
            }
            return n;
        }
    }

    /// <summary>How a section starts.</summary>
    public enum SectionStart
    {
        NextPage,
        Continuous,
        EvenPage,
        OddPage,
        NextColumn,
    }

    /// <summary>Page size and margins of a section, in points.</summary>
    public sealed record PageGeometry
    {
        /// <summary>A4 portrait.</summary>
        public double Width { get; init; } = 595.3;

        public double Height { get; init; } = 841.9;

        public double MarginTop { get; init; } = 72;
        public double MarginBottom { get; init; } = 72;
        public double MarginLeft { get; init; } = 72;
        public double MarginRight { get; init; } = 72;

        /// <summary>Distance from the page top to the header's top (w:pgMar/@w:header).</summary>
        public double HeaderDistance { get; init; } = 35.4;

        /// <summary>Distance from the page bottom to the footer's bottom (w:pgMar/@w:footer).</summary>
        public double FooterDistance { get; init; } = 35.4;

        /// <summary>Gutter added to the left margin (or top, with <see cref="GutterAtTop"/>).</summary>
        public double Gutter { get; init; }

        public bool GutterAtTop { get; init; }

        /// <summary>Mirror margins: on even pages left and right margins swap.</summary>
        public bool MirrorMargins { get; init; }

        public static PageGeometry A4 { get; } = new PageGeometry();

        public static PageGeometry Letter { get; } = new PageGeometry { Width = 612, Height = 792 };

        /// <summary>Content-area width.</summary>
        public double ContentWidth => Width - MarginLeft - MarginRight - (GutterAtTop ? 0 : Gutter);

        public bool SameSizeAs(PageGeometry other) => Math.Abs(Width - other.Width) < 0.01 && Math.Abs(Height - other.Height) < 0.01;
    }

    /// <summary>One column of a section with unequal columns (w:col): its width and the gap after it, in points.</summary>
    public readonly record struct FlowColumn(double Width, double SpaceAfter);

    /// <summary>One section: page geometry plus content.</summary>
    public sealed class FlowSection
    {
        public PageGeometry Page { get; set; } = PageGeometry.A4;

        public SectionStart Start { get; set; } = SectionStart.NextPage;

        /// <summary>
        /// Number of equal text columns (w:cols/@w:num). Ignored when <see cref="ColumnWidths"/> is filled; the number
        /// of columns is then the number of entries.
        /// </summary>
        public int ColumnCount { get; set; } = 1;

        /// <summary>Gap between equal columns (w:cols/@w:space); 36 pt (720 twips) when the file does not state it.</summary>
        public double ColumnSpacing { get; set; } = 36;

        /// <summary>
        /// Unequal columns (w:cols/@w:equalWidth="0" with w:col), in order: each column's width and the gap after it.
        /// Empty = <see cref="ColumnCount"/> equal columns.
        /// </summary>
        public IList<FlowColumn> ColumnWidths { get; } = new List<FlowColumn>();

        /// <summary>A vertical line in the middle of each gap between columns (w:cols/@w:sep).</summary>
        public bool ColumnSeparator { get; set; }

        /// <summary>
        /// Balance the columns of the section's last region when the next section starts with a continuous break, so the
        /// columns end at about the same height (Word's behaviour; the file can turn it off with
        /// w:compat/w:doNotBalanceTextColumns).
        /// </summary>
        public bool BalanceColumns { get; set; } = true;

        public IList<FlowBlock> Blocks { get; } = new List<FlowBlock>();

        /// <summary>Headers and footers (null = inherit from the previous section, as in OOXML).</summary>
        public HeaderFooterSet? HeadersFooters { get; set; }

        /// <summary>First page number of the section (w:pgNumType/@w:start); null = continue.</summary>
        public int? PageNumberStart { get; set; }

        /// <summary>Page number format for PAGE fields in this section.</summary>
        public PageNumberFormat PageNumberFormat { get; set; } = PageNumberFormat.Decimal;

        /// <summary>A text watermark drawn behind the content of every page of this section; null = none. Not inherited.</summary>
        public FlowWatermark? Watermark { get; set; }
    }

    /// <summary>
    /// A text watermark ("DRAFT", "CONFIDENTIAL") as Word draws it: WordArt text stretched to a box centred on the
    /// margins, behind the text, horizontal or rotated 45 degrees (rising to the right).
    /// </summary>
    public sealed record FlowWatermark(string Text)
    {
        public string FontFamily { get; init; } = "Calibri";

        public Drawing.LayoutColor Color { get; init; } = new Drawing.LayoutColor(0xC0, 0xC0, 0xC0);

        /// <summary>0 (invisible) to 1 (solid). Word's "semitransparent" is 0.5.</summary>
        public double Opacity { get; init; } = 0.5;

        /// <summary>Rotated 45 degrees counter-clockwise (Word's diagonal layout); false = horizontal.</summary>
        public bool Diagonal { get; init; } = true;

        /// <summary>Box width in points; null = the content width.</summary>
        public double? Width { get; init; }

        /// <summary>Box height in points; null = about three characters per box height, at most half the content height.</summary>
        public double? Height { get; init; }
    }

    /// <summary>Formats for page numbers.</summary>
    public enum PageNumberFormat
    {
        Decimal,
        UpperRoman,
        LowerRoman,
        UpperLetter,
        LowerLetter,
    }

    /// <summary>
    /// Header and footer content of a section. The first-page parts apply when <see cref="DifferentFirstPage"/>;
    /// the even-page parts apply to even pages when <see cref="DifferentOddEven"/>.
    /// </summary>
    public sealed class HeaderFooterSet
    {
        public IList<FlowBlock>? DefaultHeader { get; set; }
        public IList<FlowBlock>? FirstPageHeader { get; set; }
        public IList<FlowBlock>? EvenPageHeader { get; set; }
        public IList<FlowBlock>? DefaultFooter { get; set; }
        public IList<FlowBlock>? FirstPageFooter { get; set; }
        public IList<FlowBlock>? EvenPageFooter { get; set; }
        public bool DifferentFirstPage { get; set; }
        public bool DifferentOddEven { get; set; }
    }

    /// <summary>A block-level element: paragraph or table.</summary>
    public abstract class FlowBlock
    {
        private protected FlowBlock()
        {
        }

        internal static long Count(IEnumerable<FlowBlock> blocks)
        {
            long n = 0;
            foreach (FlowBlock b in blocks)
            {
                n++;
                if (b is FlowTable t)
                {
                    foreach (FlowTableRow r in t.Rows)
                    {
                        n++;
                        foreach (FlowTableCell c in r.Cells)
                        {
                            n += Count(c.Blocks);
                        }
                    }
                }
            }
            return n;
        }
    }

    /// <summary>What follows a list label before the paragraph text.</summary>
    public enum ListLabelSuffix
    {
        Tab,
        Space,
        Nothing,
    }

    /// <summary>A resolved list label (bullet or number text) drawn at the start of the first line.</summary>
    public sealed record ListLabel(string Text, CharacterFormat Format, ListLabelSuffix Suffix = ListLabelSuffix.Tab);

    /// <summary>A paragraph.</summary>
    public sealed class FlowParagraph : FlowBlock
    {
        public ParagraphFormat Format { get; set; } = ParagraphFormat.Default;

        public IList<FlowInline> Inlines { get; } = new List<FlowInline>();

        /// <summary>Formatting of the paragraph mark: sets the height of an empty paragraph's line.</summary>
        public CharacterFormat MarkFormat { get; set; } = CharacterFormat.Default;

        public ListLabel? ListLabel { get; set; }

        /// <summary>Convenience: a paragraph with one text run.</summary>
        public static FlowParagraph Of(string text, CharacterFormat? format = null, ParagraphFormat? paragraph = null)
        {
            FlowParagraph p = new FlowParagraph
            {
                Format = paragraph ?? ParagraphFormat.Default,
                MarkFormat = format ?? CharacterFormat.Default,
            };
            p.Inlines.Add(new TextRun(text, format ?? CharacterFormat.Default));
            return p;
        }
    }

    /// <summary>Inline content of a paragraph.</summary>
    public abstract class FlowInline
    {
        private protected FlowInline(CharacterFormat format)
        {
            Format = format ?? throw new ArgumentNullException(nameof(format));
        }

        public CharacterFormat Format { get; }
    }

    /// <summary>Text. Tab (U+0009) and line feed (U+000A, a line break) characters inside are honoured.</summary>
    public sealed class TextRun : FlowInline
    {
        public TextRun(string text, CharacterFormat format) : base(format)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public string Text { get; }

        /// <summary>Hyperlink target for this run (external URI).</summary>
        public string? HyperlinkUri { get; init; }

        /// <summary>Hyperlink target for this run (internal bookmark).</summary>
        public string? HyperlinkAnchor { get; init; }
    }

    /// <summary>A tab character.</summary>
    public sealed class TabInline : FlowInline
    {
        public TabInline(CharacterFormat format) : base(format)
        {
        }

        /// <summary>
        /// A positional tab (Word's w:ptab): it advances to an absolute position in the margin or indent area and
        /// ignores tab stops. Null for an ordinary tab.
        /// </summary>
        public PositionalTab? Positional { get; init; }
    }

    /// <summary>
    /// Where a positional tab goes: left, center or right of the area between the margins
    /// (<paramref name="RelativeToIndent"/> false) or between the paragraph's indents (true), with a leader.
    /// </summary>
    public sealed record PositionalTab(TabAlignment Alignment, bool RelativeToIndent = false, TabLeader Leader = TabLeader.None);

    /// <summary>Kinds of break.</summary>
    public enum BreakKind
    {
        /// <summary>Line break (w:br, Shift+Enter).</summary>
        Line,

        /// <summary>Page break (w:br type="page").</summary>
        Page,

        /// <summary>Column break (w:br type="column"): the next column, or the next page after the last column.</summary>
        Column,
    }

    /// <summary>A break inside a paragraph.</summary>
    public sealed class BreakInline : FlowInline
    {
        public BreakInline(BreakKind kind, CharacterFormat format) : base(format)
        {
            Kind = kind;
        }

        public BreakKind Kind { get; }
    }

    /// <summary>Fields whose result Layout computes at layout time.</summary>
    public enum FieldKind
    {
        /// <summary>Current page number in the section's format.</summary>
        Page,

        /// <summary>Total pages in the document.</summary>
        NumPages,

        /// <summary>Pages in the current section.</summary>
        SectionPages,

        /// <summary>Current date (<see cref="LayoutOptions.Now"/>), formatted with <see cref="FieldInline.FormatString"/>.</summary>
        Date,

        /// <summary>Current time.</summary>
        Time,

        /// <summary>The document's file name (<see cref="LayoutOptions.DocumentName"/>).</summary>
        FileName,

        /// <summary>Any other field: the cached result text is used.</summary>
        Other,
    }

    /// <summary>A field. <see cref="CachedResult"/> is what the source application last displayed.</summary>
    public sealed class FieldInline : FlowInline
    {
        public FieldInline(FieldKind kind, CharacterFormat format, string? cachedResult = null) : base(format)
        {
            Kind = kind;
            CachedResult = cachedResult;
        }

        public FieldKind Kind { get; }

        public string? CachedResult { get; }

        /// <summary>.NET date/time format for <see cref="FieldKind.Date"/> and <see cref="FieldKind.Time"/>.</summary>
        public string? FormatString { get; init; }
    }

    /// <summary>Footnote or endnote.</summary>
    public enum FlowNoteKind
    {
        /// <summary>Laid out at the foot of the page that holds its reference.</summary>
        Footnote,

        /// <summary>Laid out at the end of the document.</summary>
        Endnote,
    }

    /// <summary>The content of a footnote or endnote.</summary>
    public sealed class FlowNote
    {
        public FlowNote(FlowNoteKind kind)
        {
            Kind = kind;
        }

        public FlowNoteKind Kind { get; }

        /// <summary>Paragraphs and tables of the note, in order.</summary>
        public IList<FlowBlock> Blocks { get; } = new List<FlowBlock>();
    }

    /// <summary>
    /// A footnote or endnote reference: draws <see cref="Label"/> in its format (usually superscript). A footnote's
    /// <see cref="Note"/> is laid out at the foot of the page where the reference lands, below a separator line.
    /// </summary>
    public sealed class NoteReferenceInline : FlowInline
    {
        public NoteReferenceInline(string label, FlowNote note, CharacterFormat format) : base(format)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Note = note ?? throw new ArgumentNullException(nameof(note));
        }

        /// <summary>The mark as shown ("1", "iv", "*"); empty when a custom mark follows as ordinary text.</summary>
        public string Label { get; }

        public FlowNote Note { get; }
    }

    /// <summary>Vertical alignment of a text box's content.</summary>
    public enum TextBoxVerticalAlignment
    {
        Top,
        Center,
        Bottom,
    }

    /// <summary>A text box: blocks laid out inside a frame, minus the insets.</summary>
    public sealed class FlowTextBox
    {
        /// <summary>Paragraphs and tables inside the box, in order.</summary>
        public IList<FlowBlock> Blocks { get; } = new List<FlowBlock>();

        public double InsetLeft { get; init; } = 7.2;

        public double InsetTop { get; init; } = 3.6;

        public double InsetRight { get; init; } = 7.2;

        public double InsetBottom { get; init; } = 3.6;

        public TextBoxVerticalAlignment VerticalAlignment { get; init; } = TextBoxVerticalAlignment.Top;

        /// <summary>Grow the box to fit its text (never shrink it below its height).</summary>
        public bool AutoFit { get; init; }

        /// <summary>Fill colour; null = none.</summary>
        public Drawing.LayoutColor? Fill { get; init; }

        /// <summary>Outline colour; null = none.</summary>
        public Drawing.LayoutColor? Line { get; init; }

        /// <summary>Outline width in points.</summary>
        public double LineWidth { get; init; } = 0.75;
    }

    /// <summary>A text box placed in the line like a large character (an inline text box).</summary>
    public sealed class TextBoxInline : FlowInline
    {
        public TextBoxInline(FlowTextBox box, double widthPt, double heightPt, CharacterFormat format) : base(format)
        {
            Box = box ?? throw new ArgumentNullException(nameof(box));
            Width = widthPt;
            Height = heightPt;
        }

        public FlowTextBox Box { get; }

        public double Width { get; }

        public double Height { get; }
    }

    /// <summary>An image placed in the line like a large character.</summary>
    public sealed class ImageInline : FlowInline
    {
        public ImageInline(Drawing.LayoutImage image, double widthPt, double heightPt, CharacterFormat format) : base(format)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
            Width = widthPt;
            Height = heightPt;
        }

        public Drawing.LayoutImage Image { get; }

        public double Width { get; }

        public double Height { get; }

        /// <summary>Alternative text.</summary>
        public string? Description { get; init; }

        /// <summary>Part of the image cut away before drawing; null = none.</summary>
        public ImageCrop? Crop { get; init; }
    }

    /// <summary>A table. Rows and cells are laid out in L5; see the tables section of DESIGN.md.</summary>
    public sealed class FlowTable : FlowBlock
    {
        public IList<FlowTableRow> Rows { get; } = new List<FlowTableRow>();

        /// <summary>Grid column widths in points (w:tblGrid).</summary>
        public IList<double> ColumnWidths { get; } = new List<double>();

        public TableFormat Format { get; set; } = new TableFormat();
    }

    /// <summary>Table-level formatting.</summary>
    public sealed record TableFormat
    {
        /// <summary>Indent of the table's left edge from the text margin.</summary>
        public double Indent { get; init; }

        public ParagraphAlignment Alignment { get; init; } = ParagraphAlignment.Left;

        /// <summary>Default cell margins.</summary>
        public double CellMarginLeft { get; init; } = 5.4;
        public double CellMarginRight { get; init; } = 5.4;
        public double CellMarginTop { get; init; }
        public double CellMarginBottom { get; init; }

        /// <summary>
        /// The table's preferred width as a percentage of the space it is laid out in (w:tblW type="pct"), for a table
        /// whose file gives no column widths. Null = not a percentage. With <see cref="GridFromLayoutWidth"/> the grid is
        /// worked out from this when the table is placed.
        /// </summary>
        public double? PreferredWidthPercent { get; init; }

        /// <summary>
        /// The file gives no column widths: the grid is shared equally from the width of the column the table is placed
        /// in (or <see cref="PreferredWidthPercent"/> of it) when the table is laid out, not from
        /// <see cref="FlowTable.ColumnWidths"/>.
        /// </summary>
        public bool GridFromLayoutWidth { get; init; }

        /// <summary>Spacing between cells (w:tblCellSpacing); 0 = collapsed borders.</summary>
        public double CellSpacing { get; init; }

        public BorderLine BorderTop { get; init; } = BorderLine.None;
        public BorderLine BorderBottom { get; init; } = BorderLine.None;
        public BorderLine BorderLeft { get; init; } = BorderLine.None;
        public BorderLine BorderRight { get; init; } = BorderLine.None;
        public BorderLine BorderInsideHorizontal { get; init; } = BorderLine.None;
        public BorderLine BorderInsideVertical { get; init; } = BorderLine.None;
    }

    /// <summary>How a row's height is interpreted.</summary>
    public enum RowHeightRule
    {
        Auto,
        AtLeast,
        Exact,
    }

    /// <summary>A table row.</summary>
    public sealed class FlowTableRow
    {
        public IList<FlowTableCell> Cells { get; } = new List<FlowTableCell>();

        public double Height { get; set; }

        public RowHeightRule HeightRule { get; set; } = RowHeightRule.Auto;

        /// <summary>Repeat this row at the top of each page the table continues on (w:tblHeader).</summary>
        public bool IsHeader { get; set; }

        /// <summary>The row must not split across pages (w:cantSplit).</summary>
        public bool CantSplit { get; set; }
    }

    /// <summary>Vertical merge state of a cell (w:vMerge).</summary>
    public enum VerticalMerge
    {
        None,
        Restart,
        Continue,
    }

    /// <summary>Vertical alignment of content within a cell.</summary>
    public enum CellVerticalAlignment
    {
        Top,
        Center,
        Bottom,
    }

    /// <summary>A table cell.</summary>
    public sealed class FlowTableCell
    {
        public IList<FlowBlock> Blocks { get; } = new List<FlowBlock>();

        /// <summary>Number of grid columns spanned (w:gridSpan).</summary>
        public int ColumnSpan { get; set; } = 1;

        public VerticalMerge VerticalMerge { get; set; }

        public CellVerticalAlignment VerticalAlignment { get; set; }

        public Drawing.LayoutColor? Background { get; set; }

        /// <summary>Cell borders; <see cref="BorderLine.None"/> defers to the table's borders.</summary>
        public BorderLine? BorderTop { get; set; }
        public BorderLine? BorderBottom { get; set; }
        public BorderLine? BorderLeft { get; set; }
        public BorderLine? BorderRight { get; set; }

        /// <summary>Cell margins overriding the table defaults.</summary>
        public double? MarginLeft { get; set; }
        public double? MarginRight { get; set; }
        public double? MarginTop { get; set; }
        public double? MarginBottom { get; set; }
    }
}
