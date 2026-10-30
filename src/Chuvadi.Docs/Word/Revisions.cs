using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.Docs.Word;

/// <summary>Kind of a tracked change.</summary>
public enum RevisionType
{
    /// <summary>Inserted content (w:ins, and w:moveTo when moved).</summary>
    Insert,
    /// <summary>Deleted content (w:del, and w:moveFrom when moved).</summary>
    Delete,
    /// <summary>Changed formatting (w:rPrChange / w:pPrChange): the old formatting is kept.</summary>
    FormatChange,
}

/// <summary>
/// Who made a tracked change and when. One object may be shared by the runs of one change.
/// <see cref="IsMove"/> marks a moved range (it is written as a plain insertion / deletion).
/// </summary>
public sealed class RevisionMark
{
    public RevisionMark(RevisionType type, string author, DateTime? date = null)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        Type = type;
        Author = string.IsNullOrEmpty(author) ? throw new ArgumentException("Author required.", nameof(author)) : author;
        Date = date;
    }

    /// <summary>Insert, delete or format change.</summary>
    public RevisionType Type { get; }

    /// <summary>The reviewer (w:author).</summary>
    public string Author { get; set; }

    /// <summary>When (w:date); null = not stated.</summary>
    public DateTime? Date { get; set; }

    /// <summary>The content was moved (w:moveFrom / w:moveTo); written as a deletion / insertion.</summary>
    public bool IsMove { get; set; }

    /// <summary>An insertion by <paramref name="author"/>, dated now unless given.</summary>
    public static RevisionMark Insert(string author, DateTime? date = null) => new(RevisionType.Insert, author, date ?? DateTime.UtcNow);

    /// <summary>A deletion by <paramref name="author"/>, dated now unless given.</summary>
    public static RevisionMark Delete(string author, DateTime? date = null) => new(RevisionType.Delete, author, date ?? DateTime.UtcNow);

    /// <summary>A formatting change by <paramref name="author"/>, dated now unless given.</summary>
    public static RevisionMark Format(string author, DateTime? date = null) => new(RevisionType.FormatChange, author, date ?? DateTime.UtcNow);

    private RevisionMark(RevisionType type, DateTime? date) { Type = type; Author = ""; Date = date; }

    /// <summary>Loader: the author exactly as found (possibly empty).</summary>
    internal static RevisionMark Loaded(RevisionType type, string? author, DateTime? date)
        => new(type, date) { Author = author ?? "" };
}

/// <summary>A tracked formatting change of a run: the formatting before the change.</summary>
/// <param name="Mark">Who and when (type <see cref="RevisionType.FormatChange"/>).</param>
/// <param name="OldFormat">The run's direct formatting before the change.</param>
/// <param name="OldCharacterStyleId">The run's character style before the change, or null.</param>
public sealed record RunFormatChange(RevisionMark Mark, TextFormat OldFormat, string? OldCharacterStyleId);

/// <summary>A tracked formatting change of a paragraph: its formatting before the change.</summary>
/// <param name="Mark">Who and when (type <see cref="RevisionType.FormatChange"/>).</param>
/// <param name="OldFormat">The paragraph's direct formatting before the change.</param>
/// <param name="OldStyleId">The paragraph style before the change, or null when it was not stated.</param>
public sealed record ParagraphFormatChange(RevisionMark Mark, ParagraphFormat OldFormat, string? OldStyleId)
{
    /// <summary>The old w:numPr (numId, ilvl) as loaded, written back (not restored by reject).</summary>
    internal (int? NumId, int? Level)? OldNumbering { get; init; }

    /// <summary>The list the old numId named, linked after numbering.xml is read (ids may change on append).</summary>
    internal ListInstance? OldList { get; set; }
}

/// <summary>
/// A comment: author, initials, date, its content (paragraphs and tables), whether it is resolved,
/// and its replies. The commented range is marked in the text by <see cref="Run.CommentRange"/>
/// marker runs and the comment's anchor by a reference run (<see cref="Run.CommentReference"/>).
/// </summary>
public sealed class Comment
{
    private readonly List<(Paragraph? After, DocTable Table)> _tables = new();

    internal Comment(string author) { Author = author; }

    internal bool RemoveTableInternal(DocTable t) => _tables.RemoveAll(e => ReferenceEquals(e.Table, t)) > 0;

    /// <summary>The reviewer (w:author).</summary>
    public string Author { get; set; }

    /// <summary>The reviewer's initials (w:initials).</summary>
    public string? Initials { get; set; }

    /// <summary>When (w:date).</summary>
    public DateTime? Date { get; set; }

    /// <summary>Marked as resolved / done (Word 2013+, commentsExtended).</summary>
    public bool Done { get; set; }

    /// <summary>The comment this one replies to, or null for a top-level comment.</summary>
    public Comment? Parent { get; internal set; }

    /// <summary>The comment's paragraphs.</summary>
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

    internal int? LoadedId { get; set; }
    internal string? LoadedParaId { get; set; }
}

/// <summary>One end of a commented range, as a marker run.</summary>
/// <param name="Comment">The comment.</param>
/// <param name="IsStart">True for w:commentRangeStart, false for w:commentRangeEnd.</param>
public sealed record CommentRangeMark(Comment Comment, bool IsStart);

/// <summary>The comments of a document, in the order they are written (replies follow their comments).</summary>
public sealed class CommentCollection : IReadOnlyList<Comment>
{
    private readonly List<Comment> _comments = new();
    private readonly Document _owner;

    internal CommentCollection(Document owner) { _owner = owner; }

    public int Count => _comments.Count;
    public Comment this[int index] => _comments[index];
    public IEnumerator<Comment> GetEnumerator() => _comments.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Creates a comment holding one paragraph of <paramref name="text"/> (the comment mark first,
    /// "annotation text" style). Mark its range with <see cref="Paragraph.CommentStart"/> /
    /// <see cref="Paragraph.CommentEnd"/> and anchor it with <see cref="Paragraph.CommentReference"/>
    /// — or use <see cref="Paragraph.Commented"/>, which does all three.
    /// </summary>
    public Comment Add(string author, string text, string? initials = null, DateTime? date = null)
    {
        if (string.IsNullOrEmpty(author)) throw new ArgumentException("Author required.", nameof(author));
        if (text is null) throw new ArgumentNullException(nameof(text));
        _owner.EnsureCommentStyles();
        var c = new Comment(author) { Initials = initials ?? Initial(author), Date = date ?? DateTime.UtcNow };
        var p = new Paragraph { StyleId = "CommentText" };
        p.AddRun(Run.AnnotationMarkRun());
        p.Text(text);
        c.Paragraphs.Add(p);
        _comments.Add(c);
        return c;
    }

    /// <summary>Adds a reply to <paramref name="parent"/> (Word shows it threaded under the comment; it shares the comment's range).</summary>
    public Comment Reply(Comment parent, string author, string text, string? initials = null, DateTime? date = null)
    {
        if (parent is null) throw new ArgumentNullException(nameof(parent));
        if (!_comments.Contains(parent)) throw new ArgumentException("The comment is not in this document.", nameof(parent));
        var root = parent;
        int guard = 0;
        while (root.Parent is not null && guard++ < 1000) root = root.Parent; // Word threads replies under the top comment
        var reply = Add(author, text, initials, date);
        reply.Parent = root;
        return reply;
    }

    internal void Clear() => _comments.Clear();

    /// <summary>Removes a comment (and its replies). Its range markers and reference are no longer written.</summary>
    public bool Remove(Comment comment)
    {
        if (!_comments.Remove(comment)) return false;
        _comments.RemoveAll(c => ReferenceEquals(c.Parent, comment));
        return true;
    }

    /// <summary>True when the comment belongs to this collection.</summary>
    public bool Contains(Comment comment) => _comments.Contains(comment);

    /// <summary>The replies to <paramref name="comment"/>, in order.</summary>
    public IReadOnlyList<Comment> RepliesTo(Comment comment) => _comments.Where(c => ReferenceEquals(c.Parent, comment)).ToList();

    private static string Initial(string author)
    {
        var parts = author.Split(new[] { ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(3).Select(w => char.ToUpperInvariant(w[0])));
    }

    internal void AddLoaded(Comment c) => _comments.Add(c);
    internal void AddMoved(Comment c) => _comments.Add(c);
}
