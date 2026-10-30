using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Chuvadi.Docs.Word;

/// <summary>
/// A bookmark: a named range of the document (w:bookmarkStart … w:bookmarkEnd). Its two ends
/// are marker runs (<see cref="Run.Bookmark"/>) that may sit in different paragraphs. Internal
/// links (<see cref="Paragraph.InternalLink(string, string, TextFormat?)"/>) and REF / PAGEREF
/// fields refer to it by <see cref="Name"/>.
/// </summary>
public sealed class Bookmark
{
    private string _name;

    /// <summary>Creates a bookmark (add its ends with <see cref="Paragraph.BookmarkStart"/> / <see cref="Paragraph.BookmarkEnd"/>).</summary>
    public Bookmark(string name)
    {
        _name = CheckName(name);
    }

    /// <summary>
    /// The name (at most 40 characters, no spaces). Word hides bookmarks whose name starts with
    /// "_" (e.g. "_Toc…", "_Ref…", "_GoBack").
    /// </summary>
    public string Name
    {
        get => _name;
        set => _name = CheckName(value);
    }

    /// <summary>First table column of a column bookmark (w:colFirst), or null.</summary>
    public int? ColumnFirst { get; set; }

    /// <summary>Last table column of a column bookmark (w:colLast), or null.</summary>
    public int? ColumnLast { get; set; }

    /// <summary>True for a hidden bookmark (name starting with "_").</summary>
    public bool IsHidden => _name.StartsWith('_');

    internal static string CheckName(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Bookmark name required.", nameof(name));
        if (name.Length > 40) throw new ArgumentException("A bookmark name has at most 40 characters (Word's limit).", nameof(name));
        foreach (var c in name)
            if (char.IsWhiteSpace(c) || char.IsControl(c)) throw new ArgumentException("A bookmark name cannot contain spaces or control characters.", nameof(name));
        return name;
    }

    /// <summary>Renames without checking Word's rules (Append keeps loaded names as they were).</summary>
    internal void SetNameUnchecked(string name) => _name = name;

    /// <summary>Loader: a name as found in a file (kept even when Word's rules are broken).</summary>
    internal static Bookmark Loaded(string name)
    {
        var b = new Bookmark("x");
        b._name = name;
        return b;
    }

    public override string ToString() => _name;
}

/// <summary>One end of a bookmark, as a marker run.</summary>
/// <param name="Bookmark">The bookmark.</param>
/// <param name="IsStart">True for w:bookmarkStart, false for w:bookmarkEnd.</param>
public sealed record BookmarkMark(Bookmark Bookmark, bool IsStart);

/// <summary>
/// A field (DATE, REF, PAGEREF, TOC, SEQ, MERGEFIELD …): its instruction and flags. In a
/// paragraph a field is three marker runs — <see cref="FieldMarkType.Begin"/>,
/// <see cref="FieldMarkType.Separate"/> and <see cref="FieldMarkType.End"/> — with the cached
/// result as ordinary runs between Separate and End (a result may span paragraphs, e.g. a
/// table of contents). PAGE, NUMPAGES and SECTIONPAGES are single field runs
/// (<see cref="Run.FieldInstruction"/>) instead.
/// </summary>
public sealed class Field
{
    private string _instruction;
    private List<object>? _parts;

    /// <summary>Creates a field with an instruction, e.g. <c>DATE \@ "d MMMM yyyy"</c> or <c>REF Total \h</c>.</summary>
    public Field(string instruction)
    {
        _instruction = instruction ?? throw new ArgumentNullException(nameof(instruction));
    }

    /// <summary>
    /// The field instruction (w:instrText), spaces included as written. When the instruction
    /// contains nested fields (e.g. <c>IF { MERGEFIELD Sex } = "F" "Ms" "Mr"</c>) this reads them
    /// as their cached results; <see cref="InstructionParts"/> keeps the nested fields. Setting it
    /// replaces any nested fields with plain text.
    /// </summary>
    public string Instruction
    {
        get
        {
            if (_parts is null) return _instruction;
            var sb = new System.Text.StringBuilder();
            foreach (var part in _parts) sb.Append(part is Field f ? f.CachedResult ?? "" : (string)part);
            return sb.ToString();
        }
        set
        {
            _instruction = value ?? throw new ArgumentNullException(nameof(value));
            _parts = null;
        }
    }

    /// <summary>
    /// The instruction in order: text (string) and nested fields (<see cref="Field"/>, with their
    /// own <see cref="CachedResult"/>), e.g. ["IF ", {MERGEFIELD Sex}, " = \"F\" …"]. A single
    /// string when there are no nested fields.
    /// </summary>
    public IReadOnlyList<object> InstructionParts => _parts ?? new List<object> { _instruction };

    /// <summary>For a field nested in another field's instruction: its cached result text.</summary>
    public string? CachedResult { get; set; }

    /// <summary>Sets the instruction as parts (strings and nested fields).</summary>
    public void SetInstructionParts(IEnumerable<object> parts)
    {
        if (parts is null) throw new ArgumentNullException(nameof(parts));
        var list = new List<object>();
        foreach (var p in parts)
        {
            if (p is not (string or Field)) throw new ArgumentException("Instruction parts are strings or nested fields.", nameof(parts));
            if (ReferenceEquals(p, this)) throw new ArgumentException("A field cannot contain itself.", nameof(parts));
            list.Add(p);
        }
        _parts = list.Exists(p => p is Field) ? list : null;
        if (_parts is null) _instruction = string.Concat(list.ConvertAll(p => (string)p));
    }

    /// <summary>Legacy form-field and field data (w:ffData / w:fldData under the begin marker), carried verbatim.</summary>
    internal List<string> RawData { get; } = new();

    /// <summary>The field type: the instruction's first word in upper case (e.g. "TOC", "REF"); empty when none.</summary>
    public string FieldType
    {
        get
        {
            var t = Instruction.TrimStart();
            int end = 0;
            while (end < t.Length && !char.IsWhiteSpace(t[end]) && t[end] != '\\') end++;
            return t[..end].ToUpperInvariant();
        }
    }

    /// <summary>
    /// The first argument after the field type (quotes removed), e.g. the bookmark of REF / PAGEREF,
    /// the identifier of SEQ, the field name of MERGEFIELD; null when there is none.
    /// </summary>
    public string? Argument
    {
        get
        {
            var t = Instruction.TrimStart();
            int i = 0;
            while (i < t.Length && !char.IsWhiteSpace(t[i]) && t[i] != '\\') i++;
            while (i < t.Length && char.IsWhiteSpace(t[i])) i++;
            if (i >= t.Length || t[i] == '\\') return null;
            if (t[i] == '"')
            {
                int close = t.IndexOf('"', i + 1);
                return close < 0 ? t[(i + 1)..] : t.Substring(i + 1, close - i - 1);
            }
            int end = i;
            while (end < t.Length && !char.IsWhiteSpace(t[end])) end++;
            return t[i..end];
        }
    }

    /// <summary>Word recalculates the field when the document is opened (w:dirty).</summary>
    public bool Dirty { get; set; }

    /// <summary>The field is locked against updates (w:fldLock).</summary>
    public bool Locked { get; set; }

    /// <summary>Written as a simple field (w:fldSimple) — only when Begin and End are in one paragraph and there is no Separate.</summary>
    internal bool Simple { get; set; }

    public override string ToString() => Instruction;
}

/// <summary>Which marker of a field a run is.</summary>
public enum FieldMarkType
{
    /// <summary>w:fldChar begin (followed by the instruction).</summary>
    Begin,
    /// <summary>w:fldChar separate: the cached result follows.</summary>
    Separate,
    /// <summary>w:fldChar end.</summary>
    End,
}

/// <summary>A field marker run.</summary>
/// <param name="Field">The field.</param>
/// <param name="Type">Begin, separate or end.</param>
public sealed record FieldMark(Field Field, FieldMarkType Type);

/// <summary>A symbol character (w:sym): a character code in a symbol font such as Wingdings.</summary>
/// <param name="Font">The symbol font.</param>
/// <param name="Code">The character code (often in the private-use range F000–F0FF).</param>
public sealed record SymbolChar(string Font, char Code);

/// <summary>Footnote or endnote.</summary>
public enum NoteKind
{
    Footnote,
    Endnote,
}

/// <summary>
/// A footnote or endnote: its content (paragraphs and tables). The body refers to it with a
/// note reference run (<see cref="Run.NoteReference"/>); its first paragraph normally starts with
/// a note mark run (<see cref="Run.IsNoteMark"/>) that shows the note's number.
/// </summary>
public sealed class Note
{
    private readonly List<(Paragraph? After, DocTable Table)> _tables = new();

    internal Note(NoteKind kind) { Kind = kind; }

    internal bool RemoveTableInternal(DocTable t) => _tables.RemoveAll(e => ReferenceEquals(e.Table, t)) > 0;

    /// <summary>Footnote or endnote.</summary>
    public NoteKind Kind { get; }

    /// <summary>The note's paragraphs.</summary>
    public List<Paragraph> Paragraphs { get; } = new();

    /// <summary>Adds a table after the current last paragraph.</summary>
    public DocTable AddTable(int columns)
    {
        var t = new DocTable(columns);
        _tables.Add((Paragraphs.Count > 0 ? Paragraphs[^1] : null, t));
        return t;
    }

    /// <summary>Paragraphs and tables in content order.</summary>
    public IReadOnlyList<object> Blocks
    {
        get
        {
            var list = new List<object>(Paragraphs.Count + _tables.Count);
            if (_tables.Count == 0) { foreach (var p in Paragraphs) list.Add(p); return list; }
            var after = new Dictionary<Paragraph, List<DocTable>>(ReferenceEqualityComparer.Instance);
            foreach (var (a, t) in _tables)
            {
                if (a is null) { list.Add(t); continue; }
                if (!after.TryGetValue(a, out var l)) after[a] = l = new List<DocTable>();
                l.Add(t);
            }
            foreach (var p in Paragraphs)
            {
                list.Add(p);
                if (after.Remove(p, out var ts)) list.AddRange(ts);
            }
            foreach (var ts in after.Values) list.AddRange(ts);
            return list;
        }
    }

    /// <summary>All text (paragraphs joined with newlines).</summary>
    public string GetText() => string.Join("\n", Paragraphs.Select(p => p.GetText()));

    internal void AppendTable(DocTable t) => _tables.Add((Paragraphs.Count > 0 ? Paragraphs[^1] : null, t));

    /// <summary>The id the note had in the file it was loaded from (ids are reassigned on save).</summary>
    internal int? LoadedId { get; set; }
}

/// <summary>The footnotes or the endnotes of a document, in the order they are written.</summary>
public sealed class NoteCollection : IReadOnlyList<Note>
{
    private readonly List<Note> _notes = new();
    private readonly Document _owner;

    internal NoteCollection(Document owner, NoteKind kind)
    {
        _owner = owner;
        Kind = kind;
    }

    /// <summary>Footnotes or endnotes.</summary>
    public NoteKind Kind { get; }

    public int Count => _notes.Count;
    public Note this[int index] => _notes[index];
    public IEnumerator<Note> GetEnumerator() => _notes.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Creates a note holding one paragraph: the note mark, a space and <paramref name="text"/>, in
    /// the "footnote text" / "endnote text" style. Refer to it with <see cref="Paragraph.NoteReference(Note, TextFormat?)"/>
    /// (or use <see cref="Paragraph.Footnote(Document, string, TextFormat?)"/>, which does both).
    /// </summary>
    public Note Add(string text, TextFormat? format = null)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        _owner.EnsureNoteStyles(Kind);
        var note = new Note(Kind);
        var p = new Paragraph { StyleId = Kind == NoteKind.Footnote ? "FootnoteText" : "EndnoteText" };
        p.AddRun(Run.NoteMarkRun(Kind == NoteKind.Footnote ? "FootnoteReference" : "EndnoteReference"));
        p.Text(" " + text, format);
        note.Paragraphs.Add(p);
        _notes.Add(note);
        return note;
    }

    /// <summary>Creates an empty note (fill <see cref="Note.Paragraphs"/> yourself; start it with <see cref="Paragraph.NoteMark"/>).</summary>
    public Note AddEmpty()
    {
        _owner.EnsureNoteStyles(Kind);
        var note = new Note(Kind);
        _notes.Add(note);
        return note;
    }

    /// <summary>Removes a note. References to it are no longer written.</summary>
    public bool Remove(Note note) => _notes.Remove(note);

    /// <summary>True when the note belongs to this collection.</summary>
    public bool Contains(Note note) => _notes.Contains(note);

    internal void AddLoaded(Note note) => _notes.Add(note);
    internal void AddMoved(Note note) => _notes.Add(note);
}

/// <summary>Where notes are placed (w:pos).</summary>
public enum NotePosition
{
    /// <summary>Bottom of the page (footnote default).</summary>
    PageBottom,
    /// <summary>Directly beneath the text on the page.</summary>
    BeneathText,
    /// <summary>End of the section.</summary>
    SectionEnd,
    /// <summary>End of the document (endnote default).</summary>
    DocumentEnd,
}

/// <summary>When note numbering restarts (w:numRestart).</summary>
public enum NoteRestart
{
    /// <summary>Numbers continue through the document (default).</summary>
    Continuous,
    /// <summary>Restart at each section.</summary>
    EachSection,
    /// <summary>Restart on each page (footnotes only; needs pagination).</summary>
    EachPage,
}

/// <summary>
/// Footnote or endnote numbering and placement (w:footnotePr / w:endnotePr), for the whole
/// document (<see cref="Document.FootnoteProperties"/>) or one section
/// (<see cref="Section.FootnoteProperties"/>, which wins where set). Null members are not stated.
/// </summary>
public sealed class NoteProperties
{
    /// <summary>Where the notes are placed.</summary>
    public NotePosition? Position { get; set; }

    /// <summary>Number format (footnotes default to decimal, endnotes to lower-case roman).</summary>
    public NumberFormat? NumberFormat { get; set; }

    private int? _startAt;

    /// <summary>First number (0 or more).</summary>
    public int? StartAt
    {
        get => _startAt;
        set
        {
            if (value is < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "The first note number cannot be negative.");
            _startAt = value;
        }
    }

    /// <summary>A number-format token Docs does not model (e.g. "custom" with its w:format), carried as loaded.</summary>
    internal string? RawFormatToken { get; set; }
    internal string? RawFormatCustom { get; set; }

    /// <summary>When numbering restarts.</summary>
    public NoteRestart? Restart { get; set; }

    /// <summary>True when nothing is stated.</summary>
    public bool IsEmpty => Position is null && NumberFormat is null && StartAt is null && Restart is null && RawFormatToken is null;

    /// <summary>A copy.</summary>
    public NoteProperties Clone() => (NoteProperties)MemberwiseClone();
}

/// <summary>A note reference in the body with the number Word shows for it.</summary>
/// <param name="Paragraph">The paragraph holding the reference.</param>
/// <param name="RunIndex">The reference run's index in <see cref="Paragraph.Runs"/>.</param>
/// <param name="Note">The note.</param>
/// <param name="Number">The note's number (counting continues or restarts per section as stated).</param>
/// <param name="Label">The mark as shown: the number in the note's format, or the custom mark text.</param>
/// <param name="Section">The section the reference is in.</param>
/// <param name="RestartsEachPage">True when numbering restarts on each page: a paginator must renumber per page.</param>
public sealed record NoteReferenceInfo(Paragraph Paragraph, int RunIndex, Note Note, int Number, string Label, Section Section,
    bool RestartsEachPage);

/// <summary>Document properties (docProps/core.xml, app.xml and custom.xml).</summary>
public sealed class DocumentProperties
{
    /// <summary>Title (dc:title).</summary>
    public string? Title { get; set; }
    /// <summary>Subject (dc:subject).</summary>
    public string? Subject { get; set; }
    /// <summary>Author (dc:creator). Null = "Chuvadi.Docs" is written.</summary>
    public string? Author { get; set; }
    /// <summary>Keywords (cp:keywords).</summary>
    public string? Keywords { get; set; }
    /// <summary>Comments (dc:description).</summary>
    public string? Description { get; set; }
    /// <summary>Category (cp:category).</summary>
    public string? Category { get; set; }
    /// <summary>Status (cp:contentStatus).</summary>
    public string? ContentStatus { get; set; }
    /// <summary>Last modified by (cp:lastModifiedBy).</summary>
    public string? LastModifiedBy { get; set; }
    /// <summary>Revision number (cp:revision).</summary>
    public string? Revision { get; set; }
    /// <summary>Language (dc:language).</summary>
    public string? Language { get; set; }
    /// <summary>Identifier (dc:identifier).</summary>
    public string? Identifier { get; set; }
    /// <summary>Version (cp:version).</summary>
    public string? Version { get; set; }
    /// <summary>Created (dcterms:created). Null = the time of saving.</summary>
    public DateTime? Created { get; set; }
    /// <summary>Modified (dcterms:modified). Null = the time of saving; a loaded value left unchanged is also replaced by the time of saving.</summary>
    public DateTime? Modified { get; set; }

    /// <summary>The modified date as loaded (to tell whether the caller changed it).</summary>
    internal DateTime? LoadedModified { get; set; }
    /// <summary>Last printed (cp:lastPrinted).</summary>
    public DateTime? LastPrinted { get; set; }

    /// <summary>Company (app.xml).</summary>
    public string? Company { get; set; }
    /// <summary>Manager (app.xml).</summary>
    public string? Manager { get; set; }
    /// <summary>Template the document was based on (app.xml), e.g. "Normal.dotm".</summary>
    public string? Template { get; set; }
    /// <summary>Hyperlink base (app.xml).</summary>
    public string? HyperlinkBase { get; set; }

    /// <summary>
    /// Custom properties (custom.xml) by name: string, bool, int, uint, long, double, float, decimal
    /// or DateTime values (others are written as text; the first of names differing only in case
    /// wins). An Unspecified DateTime is taken as UTC. Order is kept.
    /// </summary>
    public IList<KeyValuePair<string, object>> Custom { get; } = new List<KeyValuePair<string, object>>();

    /// <summary>Sets (or adds) a custom property.</summary>
    public DocumentProperties SetCustom(string name, object value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Property name required.", nameof(name));
        if (value is not (string or bool or int or uint or long or double or float or decimal or DateTime))
            throw new ArgumentException("Custom property values are string, bool, int, uint, long, double, float, decimal or DateTime.", nameof(value));
        for (int i = 0; i < Custom.Count; i++)
            if (string.Equals(Custom[i].Key, name, StringComparison.OrdinalIgnoreCase)) { Custom[i] = new(name, value); return this; }
        Custom.Add(new(name, value));
        return this;
    }

    /// <summary>A custom property's value, or null.</summary>
    public object? GetCustom(string name)
    {
        foreach (var kv in Custom) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }
}

/// <summary>Options for <see cref="Document.Append(Document, AppendOptions?)"/>.</summary>
public sealed class AppendOptions
{
    /// <summary>
    /// When set, the appended content starts a new section of this kind (e.g. next page);
    /// null (default) continues in the current section.
    /// </summary>
    public SectionStart? StartSection { get; init; }

    /// <summary>
    /// With <see cref="StartSection"/>: the new section takes the source's page layout, headers
    /// and footers (true), or the destination's (false, default).
    /// </summary>
    public bool KeepSourceLayout { get; init; }

    /// <summary>
    /// Style ids present in both documents: the destination's style wins (false, default — Word's
    /// "use destination styles"), or the source's is kept under a new id such as "Heading1_2" (true).
    /// </summary>
    public bool KeepSourceStyles { get; init; }
}
