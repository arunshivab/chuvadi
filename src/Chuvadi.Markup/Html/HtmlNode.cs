using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Markup.Html
{
    /// <summary>DOM node type.</summary>
    public enum HtmlNodeType
    {
        /// <summary><see cref="HtmlDocument"/>.</summary>
        Document,
        /// <summary><see cref="HtmlDocumentFragment"/>.</summary>
        DocumentFragment,
        /// <summary><see cref="HtmlDocumentType"/>.</summary>
        DocumentType,
        /// <summary><see cref="HtmlElement"/>.</summary>
        Element,
        /// <summary><see cref="HtmlText"/>.</summary>
        Text,
        /// <summary><see cref="HtmlComment"/>.</summary>
        Comment,
    }

    /// <summary>
    /// Base DOM node. Children are a doubly linked list (DOM style): <see cref="FirstChild"/>,
    /// <see cref="NextSibling"/>, and so on; <see cref="ChildNodes"/> gives an indexed snapshot.
    /// All traversal helpers are iterative, so deep trees never threaten the stack.
    /// </summary>
    public abstract class HtmlNode
    {
        private HtmlNode[]? _childCache;
        private int _childCount;

        /// <summary>The node type.</summary>
        public abstract HtmlNodeType NodeType { get; }

        /// <summary>Parent node, or null.</summary>
        public HtmlNode? Parent { get; private set; }

        /// <summary>First child, or null.</summary>
        public HtmlNode? FirstChild { get; private set; }

        /// <summary>Last child, or null.</summary>
        public HtmlNode? LastChild { get; private set; }

        /// <summary>Next sibling, or null.</summary>
        public HtmlNode? NextSibling { get; private set; }

        /// <summary>Previous sibling, or null.</summary>
        public HtmlNode? PreviousSibling { get; private set; }

        /// <summary>Number of children.</summary>
        public int ChildCount => _childCount;

        /// <summary>Offset of the token that created this node in the parsed input, or -1.</summary>
        public int SourceOffset { get; internal set; } = -1;

        /// <summary>Children as an indexed list (a cached snapshot, rebuilt after changes).</summary>
        public IReadOnlyList<HtmlNode> ChildNodes
        {
            get
            {
                if (_childCache is not null) return _childCache;
                var arr = new HtmlNode[_childCount];
                int i = 0;
                for (var c = FirstChild; c is not null; c = c.NextSibling) arr[i++] = c;
                return _childCache = arr;
            }
        }

        /// <summary>Child elements only.</summary>
        public IEnumerable<HtmlElement> Children
        {
            get
            {
                for (var c = FirstChild; c is not null; c = c.NextSibling)
                    if (c is HtmlElement e) yield return e;
            }
        }

        /// <summary>True if this node type may have children.</summary>
        public virtual bool CanHaveChildren => false;

        /// <summary>Appends a child (detaching it from any previous parent).</summary>
        public HtmlNode AppendChild(HtmlNode child) => InsertBefore(child, null);

        /// <summary>Inserts a child before <paramref name="reference"/> (null appends).</summary>
        public HtmlNode InsertBefore(HtmlNode child, HtmlNode? reference)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (!CanHaveChildren) throw new InvalidOperationException(NodeType + " nodes cannot have children.");
            if (reference is not null && reference.Parent != this) throw new ArgumentException("The reference node is not a child of this node.", nameof(reference));
            if (child is HtmlDocument) throw new InvalidOperationException("A document cannot be inserted.");
            for (HtmlNode? p = this; p is not null; p = p.Parent)
                if (ReferenceEquals(p, child)) throw new InvalidOperationException("A node cannot be inserted into itself or its descendants.");
            if (child is HtmlDocumentFragment frag)
            {
                while (frag.FirstChild is { } fc)
                {
                    frag.RemoveChild(fc);
                    InsertBeforeCore(fc, reference);
                }
                return child;
            }
            child.Parent?.RemoveChild(child);
            InsertBeforeCore(child, reference);
            return child;
        }

        internal void InsertBeforeCore(HtmlNode child, HtmlNode? reference)
        {
            child.Parent = this;
            if (reference is null)
            {
                child.PreviousSibling = LastChild;
                child.NextSibling = null;
                if (LastChild is not null) LastChild.NextSibling = child; else FirstChild = child;
                LastChild = child;
            }
            else
            {
                child.NextSibling = reference;
                child.PreviousSibling = reference.PreviousSibling;
                if (reference.PreviousSibling is not null) reference.PreviousSibling.NextSibling = child; else FirstChild = child;
                reference.PreviousSibling = child;
            }
            _childCount++;
            _childCache = null;
        }

        /// <summary>Removes a child.</summary>
        public HtmlNode RemoveChild(HtmlNode child)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (child.Parent != this) throw new ArgumentException("Not a child of this node.", nameof(child));
            if (child.PreviousSibling is not null) child.PreviousSibling.NextSibling = child.NextSibling; else FirstChild = child.NextSibling;
            if (child.NextSibling is not null) child.NextSibling.PreviousSibling = child.PreviousSibling; else LastChild = child.PreviousSibling;
            child.Parent = child.PreviousSibling = child.NextSibling = null;
            _childCount--;
            _childCache = null;
            return child;
        }

        /// <summary>Detaches this node from its parent.</summary>
        public void Remove() => Parent?.RemoveChild(this);

        /// <summary>Replaces this node with another (or several via a fragment).</summary>
        public void ReplaceWith(HtmlNode replacement)
        {
            var parent = Parent ?? throw new InvalidOperationException("The node has no parent.");
            var next = NextSibling;
            parent.RemoveChild(this);
            parent.InsertBefore(replacement, next);
        }

        /// <summary>Moves all children of this node to just before this node, then removes it (unwrap).</summary>
        public void Unwrap()
        {
            var parent = Parent ?? throw new InvalidOperationException("The node has no parent.");
            while (FirstChild is { } c)
            {
                RemoveChild(c);
                parent.InsertBeforeCore(c, this);
            }
            parent.RemoveChild(this);
        }

        /// <summary>Removes all children.</summary>
        public void RemoveAllChildren()
        {
            while (FirstChild is { } c) RemoveChild(c);
        }

        /// <summary>All descendants in tree order (pre-order), without recursion. Template contents are not included.</summary>
        public IEnumerable<HtmlNode> Descendants()
        {
            var n = FirstChild;
            while (n is not null)
            {
                yield return n;
                if (n.FirstChild is not null) { n = n.FirstChild; continue; }
                while (n is not null && n != this && n.NextSibling is null) n = n.Parent;
                if (n is null || n == this) yield break;
                n = n.NextSibling;
            }
        }

        /// <summary>Descendant elements in tree order.</summary>
        public IEnumerable<HtmlElement> DescendantElements()
        {
            foreach (var n in Descendants())
                if (n is HtmlElement e) yield return e;
        }

        /// <summary>Concatenated text of all descendant text nodes.</summary>
        public virtual string TextContent
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var n in Descendants())
                    if (n is HtmlText t) sb.Append(t.Data);
                return sb.ToString();
            }
        }

        /// <summary>Depth of this node (the root has depth 0).</summary>
        public int Depth
        {
            get
            {
                int d = 0;
                for (var p = Parent; p is not null; p = p.Parent) d++;
                return d;
            }
        }

        /// <summary>Serializes this node's children as HTML (innerHTML).</summary>
        public string InnerHtml => HtmlSerializer.SerializeChildren(this);

        /// <summary>Serializes this node and its children as HTML (outerHTML).</summary>
        public string OuterHtml => HtmlSerializer.Serialize(this);

        /// <summary>Deep or shallow copy (detached).</summary>
        public abstract HtmlNode Clone(bool deep = true);

        internal void CloneChildrenInto(HtmlNode target)
        {
            // iterative deep copy
            var stack = new Stack<(HtmlNode src, HtmlNode dst)>();
            stack.Push((this, target));
            while (stack.Count > 0)
            {
                var (src, dst) = stack.Pop();
                for (var c = src.FirstChild; c is not null; c = c.NextSibling)
                {
                    var copy = c.Clone(false);
                    dst.InsertBeforeCore(copy, null);
                    if (c.FirstChild is not null) stack.Push((c, copy));
                    if (c is HtmlElement { Content: not null } te && copy is HtmlElement ce)
                    {
                        ce.Content = new HtmlDocumentFragment();
                        te.Content.CloneChildrenInto(ce.Content);
                    }
                }
            }
        }
    }

    /// <summary>A parsed or constructed document.</summary>
    public sealed class HtmlDocument : HtmlNode
    {
        internal readonly List<HtmlParseError> ErrorList = new List<HtmlParseError>();

        /// <inheritdoc />
        public override HtmlNodeType NodeType => HtmlNodeType.Document;

        /// <inheritdoc />
        public override bool CanHaveChildren => true;

        /// <summary>The document's quirks mode (set by the DOCTYPE).</summary>
        public HtmlQuirksMode QuirksMode { get; set; }

        /// <summary>Parse errors (up to <see cref="MarkupLimits.MaxRecordedErrors"/>), with line and column.</summary>
        public IReadOnlyList<HtmlParseError> ParseErrors => ErrorList;

        /// <summary>Total parse errors including unrecorded ones.</summary>
        public int ParseErrorCount { get; internal set; }

        /// <summary>The encoding used to decode byte input, or null for string input.</summary>
        public System.Text.Encoding? Encoding { get; internal set; }

        /// <summary>How the encoding was determined, for byte input.</summary>
        public HtmlEncodingSource EncodingSource { get; internal set; }

        /// <summary>The DOCTYPE node, if any.</summary>
        public HtmlDocumentType? DocumentType
        {
            get
            {
                for (var c = FirstChild; c is not null; c = c.NextSibling)
                    if (c is HtmlDocumentType d) return d;
                return null;
            }
        }

        /// <summary>The root element (normally <c>html</c>).</summary>
        public HtmlElement? DocumentElement
        {
            get
            {
                for (var c = FirstChild; c is not null; c = c.NextSibling)
                    if (c is HtmlElement e) return e;
                return null;
            }
        }

        /// <summary>The <c>head</c> element.</summary>
        public HtmlElement? Head => FindChild(DocumentElement, "head");

        /// <summary>The <c>body</c> (or <c>frameset</c>) element.</summary>
        public HtmlElement? Body => FindChild(DocumentElement, "body") ?? FindChild(DocumentElement, "frameset");

        /// <summary>The document title text (whitespace collapsed), or empty.</summary>
        public string Title
        {
            get
            {
                foreach (var e in DescendantElements())
                    if (e.LocalName == "title" && e.Namespace == HtmlNamespace.Html)
                        return string.Join(" ", e.TextContent.Split(new[] { ' ', '\t', '\n', '\f', '\r' }, StringSplitOptions.RemoveEmptyEntries));
                return string.Empty;
            }
        }

        private static HtmlElement? FindChild(HtmlElement? parent, string name)
        {
            if (parent is null) return null;
            foreach (var e in parent.Children)
                if (e.LocalName == name && e.Namespace == HtmlNamespace.Html) return e;
            return null;
        }

        /// <summary>Creates an HTML element (not inserted).</summary>
        public static HtmlElement CreateElement(string localName, HtmlNamespace ns = HtmlNamespace.Html) => new HtmlElement(localName, ns);

        /// <inheritdoc />
        public override HtmlNode Clone(bool deep = true)
        {
            var d = new HtmlDocument { QuirksMode = QuirksMode, Encoding = Encoding, EncodingSource = EncodingSource };
            if (deep) CloneChildrenInto(d);
            return d;
        }
    }

    /// <summary>Document quirks mode.</summary>
    public enum HtmlQuirksMode
    {
        /// <summary>Standards mode.</summary>
        NoQuirks,
        /// <summary>Quirks mode.</summary>
        Quirks,
        /// <summary>Limited-quirks mode.</summary>
        LimitedQuirks,
    }

    /// <summary>How the character encoding of byte input was determined.</summary>
    public enum HtmlEncodingSource
    {
        /// <summary>Input was a string.</summary>
        None,
        /// <summary>A byte order mark.</summary>
        ByteOrderMark,
        /// <summary>Supplied by the caller (for example a MIME charset).</summary>
        Caller,
        /// <summary>A <c>&lt;meta charset&gt;</c> or <c>http-equiv</c> declaration found by the prescan.</summary>
        MetaPrescan,
        /// <summary>The caller's fallback encoding (default windows-1252, as browsers use).</summary>
        Fallback,
    }

    /// <summary>A document fragment (result of fragment parsing, and template contents).</summary>
    public sealed class HtmlDocumentFragment : HtmlNode
    {
        /// <inheritdoc />
        public override HtmlNodeType NodeType => HtmlNodeType.DocumentFragment;

        /// <inheritdoc />
        public override bool CanHaveChildren => true;

        /// <summary>Parse errors when created by fragment parsing.</summary>
        public IReadOnlyList<HtmlParseError> ParseErrors { get; internal set; } = Array.Empty<HtmlParseError>();

        /// <inheritdoc />
        public override HtmlNode Clone(bool deep = true)
        {
            var f = new HtmlDocumentFragment();
            if (deep) CloneChildrenInto(f);
            return f;
        }
    }

    /// <summary>A DOCTYPE node.</summary>
    public sealed class HtmlDocumentType : HtmlNode
    {
        /// <summary>Creates a DOCTYPE.</summary>
        public HtmlDocumentType(string name, string publicId, string systemId)
        {
            Name = name;
            PublicId = publicId;
            SystemId = systemId;
        }

        /// <inheritdoc />
        public override HtmlNodeType NodeType => HtmlNodeType.DocumentType;

        /// <summary>Name (empty if missing).</summary>
        public string Name { get; set; }

        /// <summary>Public identifier (empty if missing).</summary>
        public string PublicId { get; set; }

        /// <summary>System identifier (empty if missing).</summary>
        public string SystemId { get; set; }

        /// <inheritdoc />
        public override string TextContent => string.Empty;

        /// <inheritdoc />
        public override HtmlNode Clone(bool deep = true) => new HtmlDocumentType(Name, PublicId, SystemId);
    }

    /// <summary>A text node.</summary>
    public sealed class HtmlText : HtmlNode
    {
        private readonly StringBuilder? _builder;
        private string? _data;

        /// <summary>Creates a text node.</summary>
        public HtmlText(string data) => _data = data ?? throw new ArgumentNullException(nameof(data));

        internal HtmlText(StringBuilder builder)
        {
            _builder = builder;
        }

        /// <inheritdoc />
        public override HtmlNodeType NodeType => HtmlNodeType.Text;

        /// <summary>The text.</summary>
        public string Data
        {
            get => _data ??= _builder!.ToString();
            set => _data = value ?? throw new ArgumentNullException(nameof(value));
        }

        internal void AppendInternal(string s)
        {
            if (_builder is not null && _data is null)
            {
                _builder.Append(s);
                return;
            }
            _data = Data + s;
        }

        internal bool IsBuilderBacked => _builder is not null && _data is null;

        /// <inheritdoc />
        public override string TextContent => Data;

        /// <inheritdoc />
        public override HtmlNode Clone(bool deep = true) => new HtmlText(Data);
    }

    /// <summary>A comment node.</summary>
    public sealed class HtmlComment : HtmlNode
    {
        /// <summary>Creates a comment.</summary>
        public HtmlComment(string data) => Data = data ?? throw new ArgumentNullException(nameof(data));

        /// <inheritdoc />
        public override HtmlNodeType NodeType => HtmlNodeType.Comment;

        /// <summary>The comment text.</summary>
        public string Data { get; set; }

        /// <inheritdoc />
        public override string TextContent => string.Empty;

        /// <inheritdoc />
        public override HtmlNode Clone(bool deep = true) => new HtmlComment(Data);
    }
}
