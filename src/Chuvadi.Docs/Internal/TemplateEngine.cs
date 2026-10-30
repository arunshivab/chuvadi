using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Expands one template part (an XmlDocument of document.xml, a header, a footer, notes) with
/// <see cref="TemplateData"/>: normalises placeholders split across runs, expands repeat and
/// condition blocks (inline, paragraph/table blocks, table rows; nested, with scopes), and
/// fills values (text, formatted runs, images). Only the touched runs change; everything else
/// in the part is left as it was.
///
/// Expanded content is never read again: each repetition is expanded and filled on its own,
/// with the whole scope stack, and is then marked done, so text that came from data is never
/// taken for a placeholder. Nodes to remove are only marked while expanding and are removed
/// in one pass per parent at the end (XmlNode removal walks the sibling list).
/// </summary>
internal sealed class TemplateEngine
{
    private const string W = DocumentSerializer.W;
    private const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string WP = DocumentSerializer.WP;
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";

    // Linear: no nested quantifiers over whitespace (kind and name are trimmed in code).
    private static readonly Regex TagPattern = new(@"\{\{([^{}]*)\}\}", RegexOptions.CultureInvariant);

    /// <summary>Kind and name of a tag's inner text: "#Items" → ('#', "Items"); " Name " → ('\0', "Name").</summary>
    private static (char Kind, string Name) Parse(string inner)
    {
        var t = inner.Trim();
        if (t.Length > 0 && t[0] is '#' or '/' or '?' or '^') return (t[0], t[1..].Trim());
        return ('\0', t);
    }

    private readonly XmlDocument _xml;
    private readonly string _part;
    private readonly TemplateOptions _options;
    private readonly Func<ImageSpec, XmlElement>? _imageRun;
    private readonly Func<int>? _nextDrawingId;
    private readonly Func<int>? _nextBookmarkId;
    private readonly CancellationToken _ct;
    private long _generated;
    private long _generatedNodes;
    private readonly HashSet<string> _problemSet = new(StringComparer.Ordinal);

    /// <summary>Content already expanded and filled (skipped by every later collection).</summary>
    private readonly HashSet<XmlNode> _done = new();

    /// <summary>Nodes to remove at the end (skipped by every later collection).</summary>
    private readonly HashSet<XmlNode> _dead = new();

    public List<string> Problems { get; } = new();

    /// <summary>w:t nodes whose text came from a value (their \n and \t become w:br / w:tab).</summary>
    public HashSet<XmlElement> ChangedText { get; } = new();

    public TemplateEngine(XmlDocument xml, string part, TemplateOptions options, Func<ImageSpec, XmlElement>? imageRun,
        Func<int>? nextDrawingId = null, Func<int>? nextBookmarkId = null, CancellationToken cancellationToken = default)
    {
        _xml = xml;
        _part = part;
        _options = options;
        _imageRun = imageRun;
        _nextDrawingId = nextDrawingId;
        _nextBookmarkId = nextBookmarkId;
        _ct = cancellationToken;
    }

    internal sealed class Tag
    {
        public char Kind;          // '\0' value, '#', '?', '^', '/'
        public string Name = "";
        public XmlElement T = null!;
        public int Start;
        public int Length;
        public int Order;          // document order within one Collect()
        public string Text => T.InnerText.Substring(Start, Length);
    }

    internal sealed class Block
    {
        public Tag Open = null!;
        public Tag Close = null!;
        public List<Block> Children = new();
    }

    /// <summary>The template scopes in force: the innermost item first, then the enclosing ones.</summary>
    internal sealed class Scope
    {
        private const int MaxParentWalk = 10_000;

        public Scope(TemplateData data, Scope? outer) { Data = data; Outer = outer; }

        public TemplateData Data { get; }
        public Scope? Outer { get; }

        /// <summary>The frames' own keys innermost to outermost, then the innermost data's parents.</summary>
        public bool TryResolve(string path, out object? value)
        {
            for (var f = this; f is not null; f = f.Outer)
                if (f.Data.TryResolveOwn(path, out value)) return true;
            int guard = 0;
            for (var d = Data.Parent; d is not null && guard++ < MaxParentWalk; d = d.Parent)
                if (d.TryResolveOwn(path, out value)) return true;
            value = null;
            return false;
        }
    }

    private enum Shape { None, Inline, Rows, Blocks }

    // =====================================================================================
    // Entry points
    // =====================================================================================

    public void Run(TemplateData data)
    {
        if (_xml.DocumentElement is null) return;
        Normalize(_xml.DocumentElement);
        var root = _xml.DocumentElement;
        Expand(() => new List<XmlNode> { root }, new Scope(data, null), 0);
        Finish();
    }

    /// <summary>Placeholders of a part in document order (the caller passes a throwaway document).</summary>
    public List<TemplatePlaceholder> List()
    {
        var result = new List<TemplatePlaceholder>();
        if (_xml.DocumentElement is null) return result;
        Normalize(_xml.DocumentElement);
        var tags = Collect(new List<XmlNode> { _xml.DocumentElement });
        var (blocks, unpaired) = Pair(tags);
        var opens = new Dictionary<Tag, Block>();
        var closes = new Dictionary<Tag, Block>();
        void Index(List<Block> level)
        {
            foreach (var b in level) { opens[b.Open] = b; closes[b.Close] = b; Index(b.Children); }
        }
        Index(blocks);
        // One forward walk with a stack of the open blocks (the pairs are properly nested).
        var path = new List<string>();
        foreach (var t in tags)
        {
            if (t.Kind == '\0')
                result.Add(new TemplatePlaceholder(t.Name, TemplatePlaceholderKind.Value, path.ToArray(), _part, TemplateBlockScope.None));
            else if (opens.TryGetValue(t, out var b))
            {
                var kind = b.Open.Kind switch { '#' => TemplatePlaceholderKind.Repeat, '?' => TemplatePlaceholderKind.Condition, _ => TemplatePlaceholderKind.InvertedCondition };
                result.Add(new TemplatePlaceholder(b.Open.Name, kind, path.ToArray(), _part, ToScope(Classify(b, null, out _, out _))));
                path.Add(b.Open.Name);
            }
            else if (closes.ContainsKey(t) && path.Count > 0)
                path.RemoveAt(path.Count - 1);
        }
        foreach (var u in unpaired)
            AddProblem($"{_part}: {u.Text} has no partner");
        return result;
    }

    /// <summary>Problems a fill would meet regardless of the data: unpaired markers and blocks that cannot form.</summary>
    public List<string> Check()
    {
        if (_xml.DocumentElement is null) return Problems;
        Normalize(_xml.DocumentElement);
        var tags = Collect(new List<XmlNode> { _xml.DocumentElement });
        var (blocks, unpaired) = Pair(tags);
        foreach (var u in unpaired.OrderBy(u => u.Order))
            AddProblem($"{_part}: {u.Text} has no partner");
        void Walk(List<Block> level)
        {
            foreach (var b in level)
            {
                if (Classify(b, null, out _, out _) == Shape.None) AddProblem(NotABlock(b));
                Walk(b.Children);
            }
        }
        Walk(blocks);
        return Problems;
    }

    private void AddProblem(string problem)
    {
        if (_problemSet.Add(problem)) Problems.Add(problem);
    }

    private string NotABlock(Block b)
        => $"{_part}: {b.Open.Text} … {b.Close.Text} do not form a block (inline in one paragraph, marker paragraphs, or table rows)";

    // =====================================================================================
    // Normalisation: a placeholder split across runs is moved into its first text node
    // =====================================================================================

    private static void Normalize(XmlNode root)
    {
        foreach (var p in Descendants(root).Where(IsParagraph).ToList())
        {
            var texts = Descendants(p).Where(n => IsW(n, "t") && NearestParagraph(n) == p).Cast<XmlElement>().ToList();
            if (texts.Count < 2) continue;
            var sb = new StringBuilder();
            var starts = new int[texts.Count];
            for (int i = 0; i < texts.Count; i++) { starts[i] = sb.Length; sb.Append(texts[i].InnerText); }
            var combined = sb.ToString();
            if (combined.IndexOf("{{", StringComparison.Ordinal) < 0) continue;
            var matches = TagPattern.Matches(combined).Cast<Match>().ToList();
            for (int m = matches.Count - 1; m >= 0; m--)
            {
                var match = matches[m];
                int a = Locate(starts, match.Index), b = Locate(starts, match.Index + match.Length - 1);
                if (a == b) continue;
                var first = texts[a];
                var head = first.InnerText[..(match.Index - starts[a])];
                first.InnerText = head + match.Value;
                Preserve(first);
                for (int i = a + 1; i < b; i++) texts[i].InnerText = "";
                var last = texts[b];
                last.InnerText = last.InnerText[(match.Index + match.Length - starts[b])..];
                Preserve(last);
            }
        }
    }

    private static int Locate(int[] starts, int pos)
    {
        int i = Array.BinarySearch(starts, pos);
        if (i >= 0)
        {
            while (i + 1 < starts.Length && starts[i + 1] == pos) i++; // skip empty nodes
            return i;
        }
        return ~i - 1;
    }

    // =====================================================================================
    // Tags and blocks
    // =====================================================================================

    /// <summary>Tags in document order under <paramref name="roots"/>, skipping done and removed content.</summary>
    private List<Tag> Collect(List<XmlNode> roots)
    {
        var tags = new List<Tag>();
        var stack = new Stack<XmlNode>();
        var buffer = new List<XmlNode>();
        foreach (var root in roots)
        {
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (_done.Contains(n) || _dead.Contains(n)) continue;
                if (IsW(n, "t"))
                {
                    var t = (XmlElement)n;
                    var text = t.InnerText;
                    if (text.IndexOf("{{", StringComparison.Ordinal) < 0) continue;
                    foreach (Match m in TagPattern.Matches(text))
                    {
                        var (kind, name) = Parse(m.Groups[1].Value);
                        if (name.Length == 0) continue; // {{}} and {{#}} are not placeholders
                        tags.Add(new Tag { Kind = kind, Name = name, T = t, Start = m.Index, Length = m.Length, Order = tags.Count });
                    }
                    continue;
                }
                buffer.Clear();
                for (var c = n.FirstChild; c is not null; c = c.NextSibling) buffer.Add(c);
                for (int i = buffer.Count - 1; i >= 0; i--) stack.Push(buffer[i]);
            }
        }
        return tags;
    }

    /// <summary>Pairs block markers (a close matches the nearest open of the same name).</summary>
    private (List<Block> TopLevel, List<Tag> Unpaired) Pair(List<Tag> tags)
    {
        // Nesting comes from text, not XML depth: cap the pending opens so every tree walk stays
        // shallow (at least 64 levels; a fill fails beyond MaxBlockDepth anyway).
        int maxOpen = Math.Clamp(_options.MaxBlockDepth, 64, 1024) + 1;
        var top = new List<Block>();
        var unpaired = new List<Tag>();
        var stack = new List<Block>();
        foreach (var t in tags)
        {
            if (t.Kind is '#' or '?' or '^')
            {
                if (stack.Count >= maxOpen)
                {
                    // Too many opens pending (deep nesting, or stray markers): the oldest can no longer
                    // pair; it is left as text and its finished children become top-level blocks.
                    unpaired.Add(stack[0].Open);
                    top.AddRange(stack[0].Children);
                    stack.RemoveAt(0);
                }
                stack.Add(new Block { Open = t });
            }
            else if (t.Kind == '/')
            {
                int i = stack.FindLastIndex(b => b.Open.Name == t.Name);
                if (i < 0) { unpaired.Add(t); continue; }
                for (int j = stack.Count - 1; j > i; j--)
                {
                    // An open with no close: its finished children belong to the enclosing block.
                    unpaired.Add(stack[j].Open);
                    stack[j - 1].Children.AddRange(stack[j].Children);
                    stack.RemoveAt(j);
                }
                var block = stack[i];
                block.Close = t;
                stack.RemoveAt(i);
                if (stack.Count == 0) top.Add(block); else stack[^1].Children.Add(block);
            }
        }
        // Opens never closed: their finished children are top-level blocks.
        foreach (var b in stack)
        {
            unpaired.Add(b.Open);
            top.AddRange(b.Children);
        }
        top.Sort((x, y) => x.Open.Order.CompareTo(y.Open.Order));
        // Children promoted from an unclosed open may follow later siblings: sort every level (iteratively).
        var work = new Stack<Block>(top);
        while (work.Count > 0)
        {
            var b = work.Pop();
            b.Children.Sort((x, y) => x.Open.Order.CompareTo(y.Open.Order));
            foreach (var c in b.Children) work.Push(c);
        }
        return (top, unpaired);
    }

    // =====================================================================================
    // Block shapes (decided without changing anything)
    // =====================================================================================

    /// <summary>
    /// How a block's markers expand. Inline: <paramref name="a"/>/<paramref name="b"/> are the marker
    /// runs. Rows: the first and last table rows (or row-level content controls). Blocks: the marker
    /// holders (paragraphs or block-level content controls). <paramref name="inside"/> (null = the
    /// whole part) keeps every node that would be cloned or removed inside the content being expanded.
    /// </summary>
    private Shape Classify(Block block, Func<XmlNode, bool>? inside, out XmlNode? a, out XmlNode? b)
    {
        a = b = null;
        var pO = NearestParagraph(block.Open.T);
        var pC = NearestParagraph(block.Close.T);
        if (pO is null || pC is null) return Shape.None;

        if (pO == pC)
        {
            // A table row whose content is exactly the block (e.g. a one-cell row) repeats as a row.
            if (RowOf(pO) is { } r0 && (inside is null || inside(r0)) && WholeRow(pO, block) is { } row) { a = b = row; return Shape.Rows; }
            var runO = block.Open.T.ParentNode;
            var runC = block.Close.T.ParentNode;
            if (runO is null || runC is null || !IsW(runO, "r") || !IsW(runC, "r") || runO.ParentNode is null || runO.ParentNode != runC.ParentNode)
                return Shape.None;
            if (inside is not null && !(inside(runO) && inside(runC))) return Shape.None;
            a = runO; b = runC;
            return Shape.Inline;
        }

        var lca = Lca(pO, pC);
        if (lca is null || IsW(lca, "p") || IsW(lca, "r")) return Shape.None;
        if (IsW(lca, "tr"))
        {
            if (inside is not null && !inside(lca)) return Shape.None;
            a = b = lca;
            return Shape.Rows;
        }
        var hO = ChildOn(lca, pO);
        var hC = ChildOn(lca, pC);
        if (hO is null || hC is null || hO == hC) return Shape.None;
        if (inside is not null && !(inside(hO) && inside(hC))) return Shape.None;
        if (IsW(lca, "tbl"))
        {
            // Children of a table on the way to a paragraph are rows, or content controls / custom XML holding rows.
            if (IsRowLike(hO) && IsRowLike(hC)) { a = hO; b = hC; return Shape.Rows; }
            return Shape.None;
        }
        if (IsW(hO, "tr") && IsW(hC, "tr")) { a = hO; b = hC; return Shape.Rows; } // both inside one row-level control
        if (IsBlockHolder(hO) && IsBlockHolder(hC)) { a = hO; b = hC; return Shape.Blocks; }
        return Shape.None;
    }

    private static bool IsRowLike(XmlNode n) => IsW(n, "tr") || IsW(n, "sdt") || IsW(n, "customXml");

    private static bool IsBlockHolder(XmlNode n) => IsW(n, "p") || IsW(n, "sdt") || IsW(n, "customXml");

    /// <summary>
    /// The row when the block is all its row holds: the open marker starts and the close marker
    /// ends the paragraph's content, and the row's other paragraphs are empty. Null otherwise.
    /// </summary>
    private static XmlNode? RowOf(XmlNode paragraph)
    {
        for (var n = paragraph.ParentNode; n is not null; n = n.ParentNode)
        {
            if (IsW(n, "tr")) return n;
            if (IsW(n, "tbl") || IsW(n, "body")) return null;
        }
        return null;
    }

    private XmlNode? WholeRow(XmlNode paragraph, Block block)
    {
        XmlNode? row = null;
        for (var n = paragraph.ParentNode; n is not null; n = n.ParentNode)
        {
            if (IsW(n, "tr")) { row = n; break; }
            if (IsW(n, "tbl") || IsW(n, "body")) return null;
        }
        if (row is null) return null;

        // The paragraph: nothing but whitespace before the open marker and after the close marker.
        int state = 0; // 0 before the open marker, 1 inside the block, 2 after the close marker
        var stack = new Stack<XmlNode>();
        stack.Push(paragraph);
        var buffer = new List<XmlNode>();
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (_dead.Contains(n)) continue;
            if (n == block.Open.T)
            {
                if (state != 0 || n.InnerText[..block.Open.Start].Trim().Length > 0) return null;
                state = 1;
            }
            if (n == block.Close.T)
            {
                if (state != 1 || n.InnerText[(block.Close.Start + block.Close.Length)..].Trim().Length > 0) return null;
                state = 2;
                continue;
            }
            if (n != block.Open.T && state != 1 && IsContent(n)) return null;
            if (IsW(n, "t")) continue;
            buffer.Clear();
            for (var c = n.FirstChild; c is not null; c = c.NextSibling) buffer.Add(c);
            for (int i = buffer.Count - 1; i >= 0; i--) stack.Push(buffer[i]);
        }
        if (state != 2) return null;

        // The rest of the row: empty (stops at the first content found).
        stack.Push(row);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n == paragraph || _dead.Contains(n)) continue;
            if (IsContent(n)) return null;
            for (var c = n.FirstChild; c is not null; c = c.NextSibling) stack.Push(c);
        }
        return row;
    }

    /// <summary>Content worth keeping: non-blank text, pictures, objects, symbols, breaks, notes and fields.</summary>
    private static bool IsContent(XmlNode n)
    {
        if (n.NodeType != XmlNodeType.Element || n.NamespaceURI != W) return false;
        switch (n.LocalName)
        {
            case "t": return n.InnerText.Trim().Length > 0;
            case "sectPr": case "tbl": case "drawing": case "pict": case "object": case "fldSimple": case "fldChar":
            case "sym": case "ptab": case "footnoteReference": case "endnoteReference": case "commentReference":
                return true;
            case "br":
                return ((XmlElement)n).GetAttribute("type", W) is "page" or "column";
            default: return false;
        }
    }

    private static TemplateBlockScope ToScope(Shape s) => s switch
    {
        Shape.Inline => TemplateBlockScope.Inline,
        Shape.Rows => TemplateBlockScope.TableRows,
        Shape.Blocks => TemplateBlockScope.Blocks,
        _ => TemplateBlockScope.None,
    };

    // =====================================================================================
    // Expansion
    // =====================================================================================

    private void Expand(Func<List<XmlNode>> roots, Scope scope, int depth)
    {
        _ct.ThrowIfCancellationRequested();
        if (depth > _options.MaxBlockDepth)
            throw new DocxTemplateException(new[] { $"{_part}: blocks nested deeper than {_options.MaxBlockDepth}" });

        // One collection; blocks are expanded last to first, so expanding a block never moves
        // the nodes or offsets of the markers before it.
        var rootList = roots();
        var tags = Collect(rootList);
        var (blocks, unpaired) = Pair(tags);
        if (_options.Strict)
            foreach (var u in unpaired) AddProblem($"{_part}: {u.Text} has no partner");
        // Everything a block clones or removes must lie inside the content being expanded.
        var rootSet = new HashSet<XmlNode>(rootList);
        bool Inside(XmlNode n)
        {
            for (var x = n; x is not null; x = x.ParentNode)
                if (rootSet.Contains(x)) return true;
            return false;
        }
        Func<XmlNode, bool>? inside = depth == 0 ? null : Inside;
        for (int i = blocks.Count - 1; i >= 0; i--)
            ApplyTree(blocks[i], scope, depth, inside);
        FillValues(roots(), scope);
    }

    /// <summary>Expands a block; when its markers cannot form one, its inner blocks are still expanded.</summary>
    private void ApplyTree(Block block, Scope scope, int depth, Func<XmlNode, bool>? inside)
    {
        if (Apply(block, scope, depth, inside)) return;
        if (_options.Strict) AddProblem(NotABlock(block));
        for (int i = block.Children.Count - 1; i >= 0; i--)
            ApplyTree(block.Children[i], scope, depth, inside);
    }

    /// <summary>Expands one block in place. False when its markers cannot form a block (left untouched).</summary>
    private bool Apply(Block block, Scope scope, int depth, Func<XmlNode, bool>? inside)
    {
        var shape = Classify(block, inside, out var a, out var b);
        if (shape == Shape.None) return false;
        var scopes = Iterations(block.Open, scope);

        List<XmlNode> fragment;
        XmlNode anchor;
        switch (shape)
        {
            case Shape.Inline:
            {
                // Isolate the markers into their own runs (close first: the open's node keeps its offset).
                var cRun = Isolate(block.Close)!;
                var oRun = Isolate(block.Open)!;
                fragment = Between(oRun, cRun);
                anchor = oRun;
                Kill(oRun);
                Kill(cRun);
                break;
            }
            case Shape.Rows:
            {
                RemoveTag(block.Close);
                RemoveTag(block.Open);
                fragment = a == b ? new List<XmlNode>() : Between(a!, b!);
                fragment.Insert(0, a!);
                if (b != a) fragment.Add(b!);
                anchor = b!;
                break;
            }
            default:
            {
                RemoveTag(block.Close);
                RemoveTag(block.Open);
                fragment = Between(a!, b!);
                anchor = fragment.Count > 0 ? fragment[^1] : a!;
                if (!HasContent(a!)) Kill(a!);
                if (!HasContent(b!)) Kill(b!);
                break;
            }
        }
        foreach (var n in fragment) Kill(n);

        if (scopes.Count > 0 && fragment.Count > 0)
        {
            _generated += scopes.Count;
            if (_generated > _options.MaxGeneratedItems)
                throw new DocxTemplateException(new[] { $"{_part}: more than {_options.MaxGeneratedItems:N0} repeated items" });
            long size = 0;
            foreach (var n in fragment) size += CountNodes(n);
            _generatedNodes += size * scopes.Count;
            if (_generatedNodes > _options.MaxGeneratedElements)
                throw new DocxTemplateException(new[] { $"{_part}: more than {_options.MaxGeneratedElements:N0} generated XML nodes" });
        }
        else return true;

        // Clones go after the anchor (InsertAfter is O(1); InsertBefore walks the sibling list).
        // The anchor and the stop node stay in place (removals are deferred), so each repetition
        // is exactly the nodes between them.
        var parent = anchor.ParentNode!;
        var stop = anchor.NextSibling;
        int iteration = 0;
        foreach (var itemScope in scopes)
        {
            _ct.ThrowIfCancellationRequested();
            var before = anchor;
            foreach (var n in fragment)
            {
                var c = n.CloneNode(deep: true);
                parent.InsertAfter(c, anchor);
                anchor = c;
            }
            if (iteration > 0) Renumber(before, stop);
            var from = before;
            Expand(() => Range(from, stop), itemScope, depth + 1);
            // Walk to the node before the stop (expansion may have replaced or split the last clone), marking it all done.
            var last = before;
            for (var n = before.NextSibling; n is not null && n != stop; n = n.NextSibling)
            {
                _done.Add(n);
                last = n;
            }
            anchor = last;
            iteration++;
        }
        return true;
    }

    private static List<XmlNode> Range(XmlNode before, XmlNode? stop)
    {
        var list = new List<XmlNode>();
        for (var n = before.NextSibling; n is not null && n != stop; n = n.NextSibling) list.Add(n);
        return list;
    }

    private void Kill(XmlNode n) => _dead.Add(n);

    private static long CountNodes(XmlNode root)
    {
        long count = 0;
        var stack = new Stack<XmlNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            count++;
            for (var c = n.FirstChild; c is not null; c = c.NextSibling) stack.Push(c);
        }
        return count;
    }

    /// <summary>The scopes a block expands with: one per item, once, or none.</summary>
    private static List<Scope> Iterations(Tag open, Scope scope)
    {
        scope.TryResolve(open.Name, out var value);
        bool truthy = Truthy(value);
        switch (open.Kind)
        {
            case '#':
                if (value is List<TemplateData> items)
                {
                    var list = new List<Scope>(items.Count);
                    foreach (var item in items) list.Add(new Scope(item, scope));
                    return list;
                }
                if (value is TemplateData obj) return new List<Scope> { new Scope(obj, scope) };
                return truthy ? new List<Scope> { scope } : new List<Scope>();
            case '?':
                return truthy ? new List<Scope> { scope } : new List<Scope>();
            default:
                return truthy ? new List<Scope>() : new List<Scope> { scope };
        }
    }

    private static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        TemplateValue tv => tv.ToString().Length > 0,
        List<TemplateData> l => l.Count > 0,
        _ => true,
    };

    // =====================================================================================
    // Repeated content: ids that must stay unique
    // =====================================================================================

    /// <summary>
    /// In a second or later repetition: new drawing ids (wp:docPr), bookmarks renamed with new ids
    /// (a bookmark whose other end is outside the repetition is dropped), and Word 2010 paragraph
    /// ids and content-control ids removed (Word regenerates them).
    /// </summary>
    private void Renumber(XmlNode before, XmlNode? stop)
    {
        var starts = new List<XmlElement>();
        var ends = new List<XmlElement>();
        var sdtIds = new List<XmlElement>();
        foreach (var root in Range(before, stop))
        {
            var stack = new Stack<XmlNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n is XmlElement e)
                {
                    if (e.NamespaceURI == WP && e.LocalName == "docPr" && _nextDrawingId is not null)
                        e.SetAttribute("id", _nextDrawingId().ToString(System.Globalization.CultureInfo.InvariantCulture));
                    else if (IsW(e, "bookmarkStart")) starts.Add(e);
                    else if (IsW(e, "id") && e.ParentNode is { } sdtPr && IsW(sdtPr, "sdtPr")) sdtIds.Add(e);
                    else if (IsW(e, "bookmarkEnd")) ends.Add(e);
                    if (e.HasAttribute("paraId", W14)) e.RemoveAttribute("paraId", W14);
                    if (e.HasAttribute("textId", W14)) e.RemoveAttribute("textId", W14);
                }
                for (var c = n.FirstChild; c is not null; c = c.NextSibling) stack.Push(c);
            }
        }
        // Content-control ids are optional; a repeated one would clash, so later copies drop it.
        foreach (var id in sdtIds) id.ParentNode?.RemoveChild(id);
        if (starts.Count == 0 && ends.Count == 0) return;
        var endById = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
        foreach (var e in ends) endById[e.GetAttribute("id", W)] = e;
        var paired = new HashSet<XmlElement>();
        foreach (var s in starts)
        {
            var id = s.GetAttribute("id", W);
            if (!endById.TryGetValue(id, out var end) || paired.Contains(end) || _nextBookmarkId is null)
            {
                s.ParentNode?.RemoveChild(s);
                continue;
            }
            paired.Add(end);
            var newId = _nextBookmarkId().ToString(System.Globalization.CultureInfo.InvariantCulture);
            s.SetAttribute("id", W, newId);
            end.SetAttribute("id", W, newId);
            var name = s.GetAttribute("name", W);
            if (name.Length > 0)
            {
                var suffix = "_" + newId;
                s.SetAttribute("name", W, (name.Length + suffix.Length > 40 ? name[..Math.Max(1, 40 - suffix.Length)] : name) + suffix);
            }
        }
        foreach (var e in ends)
            if (!paired.Contains(e)) e.ParentNode?.RemoveChild(e);
    }

    // =====================================================================================
    // Values
    // =====================================================================================

    private void FillValues(List<XmlNode> roots, Scope scope)
    {
        var tags = Collect(roots).Where(t => t.Kind == '\0').ToList();
        // Right to left within a text node keeps earlier offsets valid.
        for (int i = tags.Count - 1; i >= 0; i--)
        {
            if ((i & 255) == 0) _ct.ThrowIfCancellationRequested();
            var t = tags[i];
            if (t.T.ParentNode is null) continue;
            if (!scope.TryResolve(t.Name, out var value))
            {
                // The lookup saw every enclosing scope: the value is missing everywhere.
                if (_options.Strict) AddProblem($"{_part}: no value for {t.Text}");
                else if (_options.MissingValue == TemplateMissingValue.Empty) ReplaceText(t, "");
                continue;
            }
            switch (value)
            {
                case null: ReplaceText(t, ""); break;
                case string s: ReplaceText(t, s); break;
                case bool b: ReplaceText(t, b ? "true" : "false"); break;
                case TemplateValue tv: ReplaceFormatted(t, tv); break;
                case ImageSpec img:
                    if (_imageRun is null) { ReplaceText(t, ""); break; }
                    var run = Isolate(t);
                    if (run?.ParentNode is null) break;
                    run.ParentNode.ReplaceChild(_imageRun(img), run);
                    break;
                default:
                    if (_options.Strict) AddProblem($"{_part}: {t.Text} is a list or object, not a value");
                    break;
            }
        }
    }

    private bool ValidXml(Tag t, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool ok;
            if (char.IsHighSurrogate(c)) { ok = i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]); if (ok) { i++; continue; } }
            else ok = !char.IsLowSurrogate(c) && (c is '\t' or '\n' or '\r' || c >= ' ' && c != '\uFFFE' && c != '\uFFFF');
            if (ok) continue;
            AddProblem($"{_part}: the value for {t.Text} contains a character XML cannot hold (U+{(int)c:X4})");
            return false;
        }
        return true;
    }

    private void ReplaceText(Tag t, string value)
    {
        if (!ValidXml(t, value)) return;
        var text = t.T.InnerText;
        t.T.InnerText = text[..t.Start] + value + text[(t.Start + t.Length)..];
        Preserve(t.T);
        ChangedText.Add(t.T);
    }

    private void ReplaceFormatted(Tag t, TemplateValue value)
    {
        foreach (var (text, _) in value.Runs) if (!ValidXml(t, text)) return;
        var run = Isolate(t);
        if (run?.ParentNode is null) return;
        var parent = run.ParentNode;
        var rPr = run.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => IsW(n, "rPr")) as XmlElement;
        foreach (var (text, format) in value.Runs)
        {
            var clone = (XmlElement)run.CloneNode(deep: false);
            var newRPr = MergeRPr(rPr, format);
            if (newRPr is not null) clone.AppendChild(newRPr);
            var tEl = _xml.CreateElement(run.Prefix, "t", W);
            tEl.InnerText = text;
            Preserve(tEl);
            clone.AppendChild(tEl);
            parent.InsertBefore(clone, run);
            ChangedText.Add(tEl);
        }
        parent.RemoveChild(run);
    }

    /// <summary>CT_RPr child order (ECMA-376 §17.3.2.28).</summary>
    private static readonly string[] RPrOrder =
    {
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow",
        "emboss", "imprint", "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern",
        "position", "sz", "szCs", "highlight", "u", "effect", "bdr", "shd", "fitText", "vertAlign", "rtl", "cs",
        "em", "lang", "eastAsianLayout", "specVanish", "oMath",
    };

    private static int RPrRank(XmlNode n)
    {
        if (n.NodeType != XmlNodeType.Element) return 1500;
        if (n.NamespaceURI != W) return 1000;              // w14 and other extensions after the known ones
        if (n.LocalName == "rPrChange") return 2000;      // always last
        int i = Array.IndexOf(RPrOrder, n.LocalName);
        return i >= 0 ? i : 999;
    }

    /// <summary>
    /// The placeholder's rPr with the value's formatting laid over it: elements the value sets
    /// replace the original ones (rFonts and lang merge attribute by attribute); everything else
    /// in the original (unknown elements, w14 effects, rPrChange) is kept, in schema order.
    /// </summary>
    private XmlElement? MergeRPr(XmlElement? original, TextFormat format)
    {
        var overlay = format.IsDefault ? null : BuildRPr(format);
        if (original is null) return overlay;
        var merged = (XmlElement)original.CloneNode(deep: true);
        if (overlay is null) return merged;
        foreach (var add in overlay.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().ToList())
        {
            var existing = merged.ChildNodes.Cast<XmlNode>().OfType<XmlElement>()
                .FirstOrDefault(e => e.LocalName == add.LocalName && e.NamespaceURI == add.NamespaceURI);
            if (existing is not null && add.LocalName is "rFonts" or "lang")
            {
                foreach (XmlAttribute attr in add.Attributes) existing.SetAttribute(attr.LocalName, attr.NamespaceURI, attr.Value);
                continue;
            }
            if (existing is not null) merged.RemoveChild(existing);
            merged.AppendChild(add);
        }
        var children = merged.ChildNodes.Cast<XmlNode>().ToList();
        var sorted = children.OrderBy(RPrRank).ToList(); // stable
        if (!children.SequenceEqual(sorted))
        {
            var attrs = merged.Attributes.Cast<XmlAttribute>().ToList();
            merged.RemoveAll();
            foreach (var a in attrs) merged.Attributes.Append(a);
            foreach (var c in sorted) merged.AppendChild(c);
        }
        return merged;
    }

    private XmlElement? BuildRPr(TextFormat format)
    {
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment }))
        {
            w.WriteStartElement("w", "root", W);
            RunProperties.Write(w, format, null);
            w.WriteEndElement();
        }
        var frag = new XmlDocument();
        frag.LoadXml(sb.ToString());
        var el = frag.DocumentElement!.FirstChild;
        return el is null ? null : (XmlElement)_xml.ImportNode(el, deep: true);
    }

    // =====================================================================================
    // Run surgery
    // =====================================================================================

    /// <summary>Splits the run holding <paramref name="t"/>'s tag so the tag has a run of its own; returns that run.</summary>
    private XmlElement? Isolate(Tag t)
    {
        var run = t.T.ParentNode as XmlElement;
        if (run is null || !IsW(run, "r")) return null;
        var parent = run.ParentNode;
        if (parent is null) return null;
        int index = IndexOf(run, t.T);
        bool changed = ChangedText.Contains(t.T);
        var text = t.T.InnerText;
        var prefix = text[..t.Start];
        var tagText = text.Substring(t.Start, t.Length);
        var suffix = text[(t.Start + t.Length)..];

        var mid = (XmlElement)run.CloneNode(deep: true);
        var after = (XmlElement)run.CloneNode(deep: true);
        // before (the original run): children up to the tag node, which keeps the prefix
        for (int i = run.ChildNodes.Count - 1; i > index; i--) run.RemoveChild(run.ChildNodes[i]!);
        t.T.InnerText = prefix;
        Preserve(t.T);
        // mid: rPr + the tag
        for (int i = mid.ChildNodes.Count - 1; i >= 0; i--)
            if (i != index && !IsW(mid.ChildNodes[i]!, "rPr")) mid.RemoveChild(mid.ChildNodes[i]!);
        var midT = (XmlElement)mid.ChildNodes.Cast<XmlNode>().First(n => IsW(n, "t"));
        midT.InnerText = tagText;
        Preserve(midT);
        // after: rPr + suffix + children after the tag node
        for (int i = index - 1; i >= 0; i--)
            if (!IsW(after.ChildNodes[i]!, "rPr")) after.RemoveChild(after.ChildNodes[i]!);
        var afterT = (XmlElement)after.ChildNodes.Cast<XmlNode>().First(n => IsW(n, "t"));
        afterT.InnerText = suffix;
        Preserve(afterT);
        if (changed)
        {
            // Text that came from a value keeps being treated as such in its pieces.
            ChangedText.Add(midT);
            ChangedText.Add(afterT);
        }

        parent.InsertAfter(after, run);
        parent.InsertAfter(mid, run);
        if (!RunHasContent(after)) parent.RemoveChild(after);
        if (!RunHasContent(run)) parent.RemoveChild(run);
        t.T = midT;
        t.Start = 0;
        return mid;
    }

    private static int IndexOf(XmlNode parent, XmlNode child)
    {
        int i = 0;
        foreach (XmlNode c in parent.ChildNodes) { if (c == child) return i; i++; }
        return -1;
    }

    private static bool RunHasContent(XmlElement run)
    {
        foreach (XmlNode c in run.ChildNodes)
        {
            if (c.NodeType != XmlNodeType.Element || IsW(c, "rPr")) continue;
            if (IsW(c, "t") && c.InnerText.Length == 0) continue;
            return true;
        }
        return false;
    }

    private static void RemoveTag(Tag t)
    {
        var text = t.T.InnerText;
        t.T.InnerText = text[..t.Start] + text[(t.Start + t.Length)..];
        Preserve(t.T);
    }

    /// <summary>True when a marker holder still has something worth keeping after its marker was removed.</summary>
    private bool HasContent(XmlNode holder)
    {
        var stack = new Stack<XmlNode>();
        stack.Push(holder);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (_dead.Contains(n)) continue;
            if (IsContent(n)) return true;
            for (var c = n.FirstChild; c is not null; c = c.NextSibling) stack.Push(c);
        }
        return false;
    }


    // =====================================================================================
    // Finishing: deferred removals, then containers that must not be empty
    // =====================================================================================

    private static readonly HashSet<string> BlockContainers = new(StringComparer.Ordinal)
    {
        "tc", "hdr", "ftr", "footnote", "endnote", "txbxContent", "comment", "body",
    };

    private void Finish()
    {
        if (_dead.Count == 0) return;
        var parents = new List<XmlNode>();
        var seen = new HashSet<XmlNode>();
        foreach (var n in _dead)
            if (n.ParentNode is { } p && seen.Add(p)) parents.Add(p);
        // One pass per parent: keep the live children, in order.
        foreach (var p in parents)
        {
            var keep = new List<XmlNode>();
            for (var c = p.FirstChild; c is not null; c = c.NextSibling)
                if (!_dead.Contains(c)) keep.Add(c);
            var attrs = p.Attributes?.Cast<XmlAttribute>().ToList();
            p.RemoveAll();
            if (attrs is not null) foreach (var a in attrs) p.Attributes!.Append(a);
            foreach (var c in keep) p.AppendChild(c);
        }
        // Tables left without rows go; containers left without a paragraph get an empty one.
        var check = new Queue<XmlNode>(parents);
        while (check.Count > 0)
        {
            var p = check.Dequeue();
            if (p.ParentNode is null && p != _xml.DocumentElement) continue;
            var table = IsW(p, "tbl") ? p : RowLevelTableOf(p);
            if (table is not null)
            {
                if (!HasRow(table) && table.ParentNode is { } holder)
                {
                    holder.RemoveChild(table);
                    check.Enqueue(holder);
                }
                continue;
            }
            var container = p;
            if (IsW(p, "sdtContent") && p.ParentNode is { } sdt && IsW(sdt, "sdt"))
            {
                if (HasBlock(p)) continue;
                container = sdt.ParentNode ?? p;
                if (container != p && !IsW(container, "p") && !IsW(container, "r") && sdt.ParentNode is not null)
                {
                    // An emptied block-level content control: give it a paragraph.
                    p.AppendChild(NewParagraph(p));
                    continue;
                }
            }
            if (container.NodeType != XmlNodeType.Element || container.NamespaceURI != W || !BlockContainers.Contains(container.LocalName)) continue;
            if (IsW(container, "tc"))
            {
                // A cell needs a block, and its last block must be a paragraph.
                XmlNode? lastBlock = null;
                for (var c = container.FirstChild; c is not null; c = c.NextSibling)
                    if (IsBlock(c)) lastBlock = c;
                if (lastBlock is null || !IsW(lastBlock, "p")) container.AppendChild(NewParagraph(container));
            }
            else if (!HasBlock(container))
            {
                var para = NewParagraph(container);
                var sectPr = container.ChildNodes.Cast<XmlNode>().LastOrDefault(c => IsW(c, "sectPr"));
                if (sectPr is not null) container.InsertBefore(para, sectPr); else container.AppendChild(para);
            }
        }
    }

    private static XmlNode? RowLevelTableOf(XmlNode n)
    {
        // An sdt/customXml content holding rows directly inside a table.
        for (var c = n; c is not null; c = c.ParentNode)
        {
            if (IsW(c, "tbl")) return c;
            if (!(IsW(c, "sdtContent") || IsW(c, "sdt") || IsW(c, "customXml"))) return null;
        }
        return null;
    }

    private static bool HasRow(XmlNode table)
    {
        var stack = new Stack<XmlNode>();
        for (var c = table.FirstChild; c is not null; c = c.NextSibling) stack.Push(c);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (IsW(n, "tr")) return true;
            if (IsW(n, "sdt") || IsW(n, "sdtContent") || IsW(n, "customXml"))
                for (var c = n.FirstChild; c is not null; c = c.NextSibling) stack.Push(c);
        }
        return false;
    }

    private static bool IsBlock(XmlNode n) => IsW(n, "p") || IsW(n, "tbl") || IsW(n, "sdt") || IsW(n, "customXml") || IsW(n, "altChunk");

    private static bool HasBlock(XmlNode container)
    {
        for (var c = container.FirstChild; c is not null; c = c.NextSibling)
            if (IsBlock(c)) return true;
        return false;
    }

    private XmlElement NewParagraph(XmlNode context)
        => _xml.CreateElement(context.GetPrefixOfNamespace(W) is { Length: > 0 } prefix ? prefix : "w", "p", W);

    // =====================================================================================
    // Tree helpers
    // =====================================================================================

    private static XmlNode? Lca(XmlNode a, XmlNode b)
    {
        var set = new HashSet<XmlNode>();
        for (var n = a; n is not null; n = n.ParentNode) set.Add(n);
        for (var n = b; n is not null; n = n.ParentNode) if (set.Contains(n)) return n;
        return null;
    }

    /// <summary>The child of <paramref name="ancestor"/> on the path to <paramref name="node"/>.</summary>
    private static XmlNode? ChildOn(XmlNode? ancestor, XmlNode node)
    {
        if (ancestor is null) return null;
        for (var n = node; n is not null; n = n.ParentNode)
            if (n.ParentNode == ancestor) return n;
        return null;
    }

    private static List<XmlNode> Between(XmlNode a, XmlNode b)
    {
        var list = new List<XmlNode>();
        for (var n = a.NextSibling; n is not null && n != b; n = n.NextSibling) list.Add(n);
        return list;
    }

    private static XmlNode? NearestParagraph(XmlNode n)
    {
        for (var c = n.ParentNode; c is not null; c = c.ParentNode) if (IsParagraph(c)) return c;
        return null;
    }

    private static bool IsParagraph(XmlNode n) => IsW(n, "p");

    private static bool IsW(XmlNode n, string local) => n.NodeType == XmlNodeType.Element && n.LocalName == local && n.NamespaceURI == W;

    private static IEnumerable<XmlNode> Self(XmlNode n) { yield return n; }

    /// <summary>Descendants in document order, iteratively (no recursion depth limit).</summary>
    private static IEnumerable<XmlNode> Descendants(XmlNode root)
    {
        // XmlNode.PreviousSibling walks the whole sibling list, so children are gathered forwards.
        var stack = new Stack<XmlNode>();
        var buffer = new List<XmlNode>();
        void PushChildren(XmlNode n)
        {
            buffer.Clear();
            for (var c = n.FirstChild; c is not null; c = c.NextSibling) buffer.Add(c);
            for (int i = buffer.Count - 1; i >= 0; i--) stack.Push(buffer[i]);
        }
        PushChildren(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            PushChildren(n);
        }
    }

    private static void Preserve(XmlElement t) => t.SetAttribute("space", XmlNs, "preserve");
}
