using System;
using System.Collections.Generic;
using System.Linq;
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
    public ParagraphFormat Format { get; private set; } = new();

    /// <summary>Restores old direct formatting in place (rejecting a tracked format change); the paragraph mark's formatting is kept.</summary>
    internal void ReplaceFormat(ParagraphFormat format)
    {
        var mark = Format.MarkFormat;
        Format.CopyAllFrom(format);
        Format.MarkFormat = mark;
    }

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

    private ListKind _list = ListKind.None;

    /// <summary>
    /// Direct list membership. Setting <see cref="ListKind.Bullet"/> or <see cref="ListKind.Number"/>
    /// makes the paragraph an item of the document's built-in default bullet or number list
    /// (and clears <see cref="ListInstance"/>); <see cref="ListKind.None"/> removes the
    /// paragraph's own list membership — numbering from its paragraph style still applies (set
    /// <see cref="NumberingSuppressed"/> to switch that off). With a <see cref="ListInstance"/>,
    /// reading returns Bullet when the item's level in that list's own definition is a bullet and
    /// Number otherwise (numStyleLink is not followed; use <see cref="Document.GetListLabel"/> for
    /// the exact label). Style numbering reads as None.
    /// </summary>
    public ListKind List
    {
        get => ListInstance is { } li
            ? (li.EffectiveLevel(Math.Clamp(ListLevel, 0, 8)).Format == NumberFormat.Bullet ? ListKind.Bullet : ListKind.Number)
            : _list;
        set
        {
            _list = value;
            _listInstance = null;
            if (value != ListKind.None) NumberingSuppressed = false;
        }
    }

    /// <summary>The built-in kind set through <see cref="List"/> (ignored while a <see cref="ListInstance"/> is set).</summary>
    internal ListKind LegacyListKind => _list;

    private ListInstance? _listInstance;

    /// <summary>
    /// The list this paragraph is an item of (w:numPr/w:numId), at <see cref="ListLevel"/> — a
    /// list from <see cref="Document.Numbering"/> (a list from elsewhere numbers nothing).
    /// Setting it replaces any built-in <see cref="List"/> kind; setting null leaves the
    /// paragraph without direct list membership.
    /// </summary>
    public ListInstance? ListInstance
    {
        get => _listInstance;
        set { _listInstance = value; _list = ListKind.None; }
    }

    /// <summary>0-based list indent level (0..8). Only used when the paragraph is a list item.</summary>
    public int ListLevel { get; set; }

    /// <summary>
    /// The list level for numbering that comes from the paragraph style (w:numPr with only
    /// w:ilvl), e.g. a Heading 1 paragraph shown at level 2 of the heading list. Null = the
    /// style's level. Ignored while the paragraph is itself a list item.
    /// </summary>
    public int? StyleListLevel { get; set; }

    /// <summary>
    /// Explicitly not numbered (w:numId="0"): switches off numbering the paragraph style would
    /// otherwise apply. Ignored while the paragraph is itself a list item.
    /// </summary>
    public bool NumberingSuppressed { get; set; }

    /// <summary>Makes the paragraph an item of <paramref name="list"/> at <paramref name="level"/> (0–8).</summary>
    public Paragraph SetList(ListInstance list, int level = 0)
    {
        if (level < 0 || level > 8) throw new ArgumentOutOfRangeException(nameof(level), "List level must be 0–8.");
        ListInstance = list ?? throw new ArgumentNullException(nameof(list));
        ListLevel = level;
        NumberingSuppressed = false;
        return this;
    }

    /// <summary>
    /// Ends a section here (w:pPr/w:sectPr): the content up to and including this paragraph is
    /// laid out with these settings. Only paragraphs directly in <see cref="Document.Blocks"/>
    /// end sections. See <see cref="Document.AddSectionBreak"/>.
    /// </summary>
    public Section? SectionProperties { get; set; }

    /// <summary>Loader: the w:sectPr read from w:pPr with its header/footer references, completed once header parts can be read.</summary>
    internal (Section Section, List<Internal.SectionXml.Reference> References)? LoadedSection { get; set; }

    /// <summary>Loader: the numId read from w:numPr, resolved against numbering.xml after loading.</summary>
    internal int? LoadedNumId { get; set; }

    /// <summary>Loader: whether w:numPr stated w:ilvl (styles often omit it).</summary>
    internal bool LoadedListLevelExplicit { get; set; }

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

    /// <summary>Appends a link to a bookmark in this document (rendered with the Hyperlink style).</summary>
    public Paragraph InternalLink(string text, string bookmarkName, TextFormat? format = null)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Text required.", nameof(text));
        Word.Bookmark.CheckName(bookmarkName);
        _runs.Add(new Run(text, format ?? TextFormat.None) { HyperlinkAnchor = bookmarkName });
        return this;
    }

    /// <summary>Appends the start of a bookmark (end it with <see cref="BookmarkEnd"/>, in this or a later paragraph).</summary>
    public Paragraph BookmarkStart(Bookmark bookmark)
    {
        if (bookmark is null) throw new ArgumentNullException(nameof(bookmark));
        _runs.Add(Run.BookmarkRun(bookmark, true));
        return this;
    }

    /// <summary>Appends the end of a bookmark.</summary>
    public Paragraph BookmarkEnd(Bookmark bookmark)
    {
        if (bookmark is null) throw new ArgumentNullException(nameof(bookmark));
        _runs.Add(Run.BookmarkRun(bookmark, false));
        return this;
    }

    /// <summary>Appends <paramref name="text"/> inside a new bookmark named <paramref name="name"/>.</summary>
    public Paragraph Bookmarked(string name, string text, TextFormat? format = null)
    {
        var b = new Bookmark(name);
        BookmarkStart(b);
        if (!string.IsNullOrEmpty(text)) Text(text, format);
        return BookmarkEnd(b);
    }

    /// <summary>
    /// Appends a field: begin, instruction, separate, the cached result (what shows until Word
    /// updates the field) and end — e.g. <c>Field("REF Total \h", "1,234")</c>. Set
    /// <paramref name="dirty"/> to have Word recalculate it when the document is opened.
    /// </summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public Paragraph Field(string instruction, string? cachedResult = null, TextFormat? format = null, bool dirty = false)
    {
        if (string.IsNullOrWhiteSpace(instruction)) throw new ArgumentException("Field instruction required.", nameof(instruction));
        var f = new Field(" " + instruction.Trim() + " ") { Dirty = dirty };
        return Field(f, cachedResult, format);
    }

    /// <summary>Appends a field from a <see cref="Word.Field"/> object with its cached result.</summary>
    public Paragraph Field(Field field, string? cachedResult = null, TextFormat? format = null)
    {
        if (field is null) throw new ArgumentNullException(nameof(field));
        _runs.Add(Run.FieldMarkRun(field, FieldMarkType.Begin, format));
        _runs.Add(Run.FieldMarkRun(field, FieldMarkType.Separate, format));
        if (!string.IsNullOrEmpty(cachedResult)) _runs.Add(new Run(cachedResult, format ?? TextFormat.None));
        _runs.Add(Run.FieldMarkRun(field, FieldMarkType.End, format));
        return this;
    }

    /// <summary>Appends one field marker (for fields whose result spans paragraphs, or built run by run).</summary>
    public Paragraph FieldMarker(Field field, FieldMarkType type, TextFormat? format = null)
    {
        if (field is null) throw new ArgumentNullException(nameof(field));
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        _runs.Add(Run.FieldMarkRun(field, type, format));
        return this;
    }

    /// <summary>
    /// Appends a DATE field (<c>DATE \@ "picture"</c>) with today's date as its cached result.
    /// The picture uses Word's codes: d, dd, ddd, dddd, M, MM, MMM, MMMM, yy, yyyy, h, hh, H, HH, mm, ss, AM/PM.
    /// </summary>
    public Paragraph Date(string picture = "d MMMM yyyy", TextFormat? format = null)
    {
        if (string.IsNullOrWhiteSpace(picture) || picture.Contains('"')) throw new ArgumentException("A date picture without quotes is required.", nameof(picture));
        return Field($"DATE \\@ \"{picture}\"", Internal.FieldFormat.FormatDate(DateTime.Now, picture), format);
    }

    /// <summary>Appends a REF field showing the text of bookmark <paramref name="bookmarkName"/> (with a link to it).</summary>
    public Paragraph Ref(string bookmarkName, string? cachedResult = null, TextFormat? format = null)
        => Field($"REF {Word.Bookmark.CheckName(bookmarkName)} \\h", cachedResult, format, dirty: cachedResult is null);

    /// <summary>Appends a PAGEREF field showing the page number of bookmark <paramref name="bookmarkName"/>.</summary>
    public Paragraph PageRef(string bookmarkName, string? cachedResult = null, TextFormat? format = null)
        => Field($"PAGEREF {Word.Bookmark.CheckName(bookmarkName)} \\h", cachedResult, format, dirty: cachedResult is null);

    /// <summary>Appends a SEQ field (figure / table numbering), e.g. <c>Seq("Figure", "1")</c> for "Figure 1".</summary>
    public Paragraph Seq(string identifier, string? cachedResult = null, TextFormat? format = null)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Any(char.IsWhiteSpace)) throw new ArgumentException("A SEQ identifier is one word.", nameof(identifier));
        return Field($"SEQ {identifier} \\* ARABIC", cachedResult, format, dirty: cachedResult is null);
    }

    /// <summary>Appends a reference to a footnote or endnote (superscript number in the text).</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public Paragraph NoteReference(Note note, TextFormat? format = null)
    {
        if (note is null) throw new ArgumentNullException(nameof(note));
        _runs.Add(Run.NoteReferenceRun(note, format, note.Kind == NoteKind.Footnote ? "FootnoteReference" : "EndnoteReference"));
        return this;
    }

    /// <summary>Appends a note reference whose mark is <paramref name="customMark"/> (e.g. "*") instead of a number.</summary>
    public Paragraph NoteReference(Note note, string customMark, TextFormat? format = null)
    {
        if (note is null) throw new ArgumentNullException(nameof(note));
        if (string.IsNullOrEmpty(customMark)) throw new ArgumentException("Custom mark required.", nameof(customMark));
        var style = note.Kind == NoteKind.Footnote ? "FootnoteReference" : "EndnoteReference";
        _runs.Add(Run.NoteReferenceRun(note, format, style, customMarkFollows: true));
        _runs.Add(new Run(customMark, format ?? TextFormat.None) { CharacterStyleId = style });
        return this;
    }

    /// <summary>Appends the note's own mark (the number at the start of a note's first paragraph).</summary>
    public Paragraph NoteMark(NoteKind kind = NoteKind.Footnote)
    {
        _runs.Add(Run.NoteMarkRun(kind == NoteKind.Footnote ? "FootnoteReference" : "EndnoteReference"));
        return this;
    }

    /// <summary>Adds a footnote with <paramref name="text"/> to <paramref name="document"/> and its reference here.</summary>
    public Paragraph Footnote(Document document, string text, TextFormat? noteFormat = null)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        return NoteReference(document.Footnotes.Add(text, noteFormat));
    }

    /// <summary>Adds an endnote with <paramref name="text"/> to <paramref name="document"/> and its reference here.</summary>
    public Paragraph Endnote(Document document, string text, TextFormat? noteFormat = null)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        return NoteReference(document.Endnotes.Add(text, noteFormat));
    }

    /// <summary>Appends a symbol character (w:sym) from a symbol font, e.g. <c>Symbol("Wingdings", '\uF0FC')</c> for a check mark.</summary>
    public Paragraph Symbol(string font, char code, TextFormat? format = null)
    {
        if (string.IsNullOrWhiteSpace(font)) throw new ArgumentException("Font required.", nameof(font));
        _runs.Add(Run.SymbolRun(new SymbolChar(font, code), format ?? TextFormat.None, null));
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

    /// <summary>
    /// Appends a positional tab (w:ptab): text after it starts at the left edge, the centre or the
    /// right edge of the margin (or indent) area, whatever the tab stops — e.g. a right-aligned
    /// page number in a footer. Leaders: none, dot, hyphen, underscore or middle dot.
    /// </summary>
    public Paragraph PositionalTab(PositionalTabAlignment alignment, PositionalTabBase relativeTo = PositionalTabBase.Margin,
        TabLeader leader = TabLeader.None, TextFormat? format = null)
    {
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (!Enum.IsDefined(relativeTo)) throw new ArgumentOutOfRangeException(nameof(relativeTo));
        if (!Enum.IsDefined(leader) || leader == TabLeader.Heavy)
            throw new ArgumentOutOfRangeException(nameof(leader), leader, "A positional tab's leader is None, Dot, Hyphen, Underscore or MiddleDot.");
        _runs.Add(Run.PositionalTabRun(new PositionalTab(alignment, relativeTo, leader), format ?? TextFormat.None));
        return this;
    }

    /// <summary>Appends a text box run (inline, or floating anchored to this paragraph).</summary>
    public Paragraph TextBox(TextBox textBox)
    {
        if (textBox is null) throw new ArgumentNullException(nameof(textBox));
        _runs.Add(new Run(string.Empty, TextFormat.None) { TextBox = textBox });
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

    /// <summary>All run text concatenated (fields contribute their placeholder value "1"; tracked deletions are left out).</summary>
    public string GetText()
    {
        var sb = new StringBuilder();
        foreach (var r in _runs) if (!r.IsDeleted) sb.Append(r.TextContent);
        return sb.ToString();
    }

    /// <summary>A tracked formatting change of this paragraph: the formatting before it (null = none).</summary>
    public ParagraphFormatChange? FormatChange { get; set; }

    private RevisionMark? _markRevision;

    /// <summary>
    /// A tracked insertion or deletion of the paragraph mark (null = not tracked). A deleted mark
    /// joins this paragraph with the next one when the change is accepted.
    /// </summary>
    public RevisionMark? MarkRevision
    {
        get => _markRevision;
        set
        {
            if (value is { Type: RevisionType.FormatChange }) throw new ArgumentException("A paragraph-mark revision is an insertion or a deletion.", nameof(value));
            _markRevision = value;
        }
    }

    /// <summary>A tracked formatting change of the paragraph mark (w:rPrChange in the mark's rPr): the mark's old formatting.</summary>
    public RunFormatChange? MarkFormatChange { get; set; }

    /// <summary>Appends text as a tracked insertion by <paramref name="author"/>.</summary>
    public Paragraph TrackedInsert(string text, string author, TextFormat? format = null, DateTime? date = null)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Text required.", nameof(text));
        _runs.Add(new Run(text, format ?? TextFormat.None) { Revision = RevisionMark.Insert(author, date) });
        return this;
    }

    /// <summary>Appends text as a tracked deletion by <paramref name="author"/> (struck through in Word until accepted).</summary>
    public Paragraph TrackedDelete(string text, string author, TextFormat? format = null, DateTime? date = null)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Text required.", nameof(text));
        _runs.Add(new Run(text, format ?? TextFormat.None) { Revision = RevisionMark.Delete(author, date) });
        return this;
    }

    /// <summary>Appends text whose formatting change from <paramref name="oldFormat"/> is tracked.</summary>
    public Paragraph TrackedFormat(string text, TextFormat format, TextFormat oldFormat, string author, DateTime? date = null)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Text required.", nameof(text));
        _runs.Add(new Run(text, format ?? TextFormat.None) { FormatChange = new RunFormatChange(RevisionMark.Format(author, date), oldFormat ?? TextFormat.None, null) });
        return this;
    }

    /// <summary>Appends the start of a commented range.</summary>
    public Paragraph CommentStart(Comment comment)
    {
        _runs.Add(Run.CommentRangeRun(comment ?? throw new ArgumentNullException(nameof(comment)), true));
        return this;
    }

    /// <summary>Appends the end of a commented range.</summary>
    public Paragraph CommentEnd(Comment comment)
    {
        _runs.Add(Run.CommentRangeRun(comment ?? throw new ArgumentNullException(nameof(comment)), false));
        return this;
    }

    /// <summary>Appends a comment's anchor (Word shows the balloon here; normally right after the range end).</summary>
    public Paragraph CommentReference(Comment comment)
    {
        _runs.Add(Run.CommentReferenceRun(comment ?? throw new ArgumentNullException(nameof(comment))));
        return this;
    }

    /// <summary>Appends <paramref name="text"/> commented with <paramref name="comment"/>: range start, text, range end and anchor.</summary>
    public Paragraph Commented(string text, Comment comment, TextFormat? format = null)
    {
        CommentStart(comment);
        if (!string.IsNullOrEmpty(text)) Text(text, format);
        CommentEnd(comment);
        return CommentReference(comment);
    }

    internal void AddRun(Run run) => _runs.Add(run);

    internal void InsertRun(int index, Run run) => _runs.Insert(index, run);

    internal void RemoveRunAt(int index) => _runs.RemoveAt(index);

    internal void PrependRuns(IEnumerable<Run> runs) => _runs.InsertRange(0, runs);

    internal void ReplaceRun(int index, Run run) => _runs[index] = run;
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

    /// <summary>Non-null when this run links inside the document: the target bookmark's name (w:hyperlink/@w:anchor).</summary>
    public string? HyperlinkAnchor { get; init; }

    /// <summary>The link's tooltip (w:hyperlink/@w:tooltip), or null.</summary>
    public string? HyperlinkTooltip { get; init; }

    /// <summary>True when this run is a link (external or internal).</summary>
    public bool IsLink => HyperlinkUrl is not null || HyperlinkAnchor is not null;

    /// <summary>Non-null when this run is one end of a bookmark (a zero-width marker; <see cref="TextContent"/> is empty).</summary>
    public BookmarkMark? Bookmark { get; private init; }

    /// <summary>Non-null when this run is a field marker (begin / separate / end; <see cref="TextContent"/> is empty).</summary>
    public FieldMark? FieldMark { get; private init; }

    /// <summary>Non-null when this run is a footnote or endnote reference (the number in the text; <see cref="TextContent"/> is empty).</summary>
    public Note? NoteReference { get; private init; }

    /// <summary>For a note reference: the mark is the run text that follows instead of a number (w:customMarkFollows).</summary>
    public bool CustomMarkFollows { get; private init; }

    /// <summary>True when this run is the note's own mark inside a footnote or endnote (w:footnoteRef / w:endnoteRef).</summary>
    public bool IsNoteMark { get; private init; }

    /// <summary>Non-null when this run is a symbol character (w:sym; <see cref="TextContent"/> holds the character code).</summary>
    public SymbolChar? Symbol { get; private init; }

    /// <summary>Non-null when this run is a text box (inline, or floating anchored to the paragraph).</summary>
    public TextBox? TextBox { get; init; }

    private readonly RevisionMark? _revision;

    /// <summary>A tracked insertion or deletion of this run (null = not tracked).</summary>
    public RevisionMark? Revision
    {
        get => _revision;
        init
        {
            if (value is { Type: RevisionType.FormatChange }) throw new ArgumentException("A run revision is an insertion or a deletion; use FormatChange for formatting.", nameof(value));
            _revision = value;
        }
    }

    /// <summary>
    /// For a deleted run that was itself a tracked insertion (one reviewer inserted, another deleted):
    /// the insertion. Rejecting all changes removes such a run.
    /// </summary>
    public RevisionMark? InsertedRevision { get; init; }

    /// <summary>A tracked formatting change of this run: the formatting before it (null = none).</summary>
    public RunFormatChange? FormatChange { get; init; }

    /// <summary>True when the run is tracked as deleted (it shows only in "markup" views; <see cref="Paragraph.GetText"/> leaves it out).</summary>
    public bool IsDeleted => Revision is { Type: RevisionType.Delete };

    /// <summary>Non-null when this run is one end of a commented range (a zero-width marker).</summary>
    public CommentRangeMark? CommentRange { get; private init; }

    /// <summary>Non-null when this run is a comment's anchor (w:commentReference).</summary>
    public Comment? CommentReference { get; private init; }

    /// <summary>True when this run is the comment's own mark inside a comment (w:annotationRef).</summary>
    public bool IsAnnotationMark { get; private init; }

    /// <summary>True for marker runs that take no space: bookmark ends, field markers and comment range ends.</summary>
    public bool IsMarker => Bookmark is not null || FieldMark is not null || CommentRange is not null;

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

    /// <summary>
    /// Non-null when this run is a positional tab (w:ptab): a tab to an absolute position measured
    /// from the margin or indent, ignoring the paragraph's tab stops (<see cref="TextContent"/> is "\t").
    /// </summary>
    public PositionalTab? PositionalTab { get; private init; }

    /// <summary>
    /// For a hyperlink run: whether the Hyperlink character style applies when no
    /// <see cref="CharacterStyleId"/> is set. True for links added in code; false for loaded
    /// links, which keep exactly the style the file gave them.
    /// </summary>
    internal bool HyperlinkStyleImplied { get; init; } = true;

    /// <summary>The character style that applies: <see cref="CharacterStyleId"/>, else "Hyperlink" for a link added in code.</summary>
    internal string? EffectiveCharacterStyleId
        => CharacterStyleId ?? (IsLink && HyperlinkStyleImplied ? "Hyperlink" : null);

    public Run(string text, TextFormat format)
    {
        TextContent = text ?? throw new ArgumentNullException(nameof(text));
        Format = format ?? TextFormat.None;
    }

    internal static Run Field(string instruction)
        => new("1", TextFormat.None) { FieldInstruction = instruction };

    internal static Run Field(string instruction, string result, TextFormat? format = null, string? characterStyleId = null)
        => new(string.IsNullOrEmpty(result) ? "1" : result, format ?? TextFormat.None) { FieldInstruction = instruction, CharacterStyleId = characterStyleId };

    internal static Run BreakRun(BreakType type, TextFormat format, string? characterStyleId = null)
        => new("\n", format) { Break = type, CharacterStyleId = characterStyleId };

    internal static Run TabRun(TextFormat format)
        => new("\t", format) { IsTab = true };

    internal static Run PositionalTabRun(PositionalTab tab, TextFormat format, string? characterStyleId = null)
        => new("\t", format) { PositionalTab = tab, CharacterStyleId = characterStyleId };

    /// <summary>A copy with the given format, style, anchor, revision and format change (everything else equal).</summary>
    internal Run Rebuild(TextFormat format, string? characterStyleId, string? hyperlinkAnchor, RevisionMark? revision, RunFormatChange? formatChange)
        => new(TextContent, format)
        {
            HyperlinkUrl = HyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = HyperlinkTooltip,
            HyperlinkStyleImplied = HyperlinkStyleImplied, FieldInstruction = FieldInstruction, Image = Image,
            CharacterStyleId = characterStyleId, Break = Break, IsTab = IsTab, PositionalTab = PositionalTab,
            Bookmark = Bookmark, FieldMark = FieldMark, NoteReference = NoteReference, CustomMarkFollows = CustomMarkFollows,
            IsNoteMark = IsNoteMark, Symbol = Symbol, TextBox = TextBox, Revision = revision, FormatChange = formatChange,
            CommentRange = CommentRange, CommentReference = CommentReference, IsAnnotationMark = IsAnnotationMark,
            InsertedRevision = revision is { Type: RevisionType.Delete } ? InsertedRevision : null,
        };

    /// <summary>A copy that is deleted text inside an insertion.</summary>
    internal Run WithInsertedDeletion(RevisionMark deletion, RevisionMark insertion)
    {
        var c = Rebuild(Format, CharacterStyleId, HyperlinkAnchor, deletion, FormatChange);
        return new Run(c.TextContent, c.Format)
        {
            HyperlinkUrl = c.HyperlinkUrl, HyperlinkAnchor = c.HyperlinkAnchor, HyperlinkTooltip = c.HyperlinkTooltip,
            HyperlinkStyleImplied = c.HyperlinkStyleImplied, FieldInstruction = c.FieldInstruction, Image = c.Image,
            CharacterStyleId = c.CharacterStyleId, Break = c.Break, IsTab = c.IsTab, PositionalTab = c.PositionalTab,
            Bookmark = c.Bookmark, FieldMark = c.FieldMark, NoteReference = c.NoteReference, CustomMarkFollows = c.CustomMarkFollows,
            IsNoteMark = c.IsNoteMark, Symbol = c.Symbol, TextBox = c.TextBox, Revision = deletion, FormatChange = c.FormatChange,
            CommentRange = c.CommentRange, CommentReference = c.CommentReference, IsAnnotationMark = c.IsAnnotationMark,
            InsertedRevision = insertion,
        };
    }

    /// <summary>A copy with a different character style and/or link anchor.</summary>
    internal Run CloneWith(string? characterStyleId, string? hyperlinkAnchor) => Rebuild(Format, characterStyleId, hyperlinkAnchor, Revision, FormatChange);

    /// <summary>A copy with a different revision and formatting change.</summary>
    internal Run WithRevision(RevisionMark? revision, RunFormatChange? formatChange) => Rebuild(Format, CharacterStyleId, HyperlinkAnchor, revision, formatChange);

    /// <summary>A copy with the old formatting restored (rejecting a format change).</summary>
    internal Run WithFormat(TextFormat format, string? characterStyleId) => Rebuild(format, characterStyleId, HyperlinkAnchor, Revision, null);

    internal static Run CommentRangeRun(Comment comment, bool isStart)
        => new("", TextFormat.None) { CommentRange = new CommentRangeMark(comment, isStart) };

    internal static Run CommentReferenceRun(Comment comment, TextFormat? format = null, string? characterStyleId = "CommentReference")
        => new("", format ?? TextFormat.None) { CommentReference = comment, CharacterStyleId = characterStyleId };

    internal static Run AnnotationMarkRun(string? characterStyleId = "CommentReference", TextFormat? format = null)
        => new("", format ?? TextFormat.None) { IsAnnotationMark = true, CharacterStyleId = characterStyleId };

    internal static Run BookmarkRun(Bookmark bookmark, bool isStart)
        => new("", TextFormat.None) { Bookmark = new BookmarkMark(bookmark, isStart) };

    internal static Run FieldMarkRun(Field field, FieldMarkType type, TextFormat? format = null, string? characterStyleId = null)
        => new("", format ?? TextFormat.None) { FieldMark = new FieldMark(field, type), CharacterStyleId = characterStyleId };

    internal static Run NoteReferenceRun(Note note, TextFormat? format, string? characterStyleId, bool customMarkFollows = false)
        => new("", format ?? TextFormat.None) { NoteReference = note, CharacterStyleId = characterStyleId, CustomMarkFollows = customMarkFollows };

    internal static Run NoteMarkRun(string? characterStyleId, TextFormat? format = null)
        => new("", format ?? TextFormat.None) { IsNoteMark = true, CharacterStyleId = characterStyleId };

    internal static Run SymbolRun(SymbolChar symbol, TextFormat format, string? characterStyleId)
        => new(symbol.Code.ToString(), format) { Symbol = symbol, CharacterStyleId = characterStyleId };
}

/// <summary>A positional tab (w:ptab): where the text after it starts.</summary>
/// <param name="Alignment">Left edge, centre or right edge of the <paramref name="RelativeTo"/> area.</param>
/// <param name="RelativeTo">The margins (default) or the paragraph's indents.</param>
/// <param name="Leader">Fill drawn up to the position (never <see cref="TabLeader.Heavy"/>).</param>
public sealed record PositionalTab(PositionalTabAlignment Alignment, PositionalTabBase RelativeTo = PositionalTabBase.Margin, TabLeader Leader = TabLeader.None);

/// <summary>Alignment of a positional tab (ST_PTabAlignment).</summary>
public enum PositionalTabAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>What a positional tab is measured from (ST_PTabRelativeTo).</summary>
public enum PositionalTabBase
{
    /// <summary>The page margins.</summary>
    Margin,
    /// <summary>The paragraph's indents.</summary>
    Indent,
}
