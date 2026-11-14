using System;
using System.Collections.Generic;

namespace Chuvadi.Markup.Markdown
{
    /// <summary>The kind of a <see cref="MarkdownNode"/>; one value per concrete node class.</summary>
    public enum MarkdownNodeKind
    {
        /// <summary><see cref="MarkdownDocument"/>.</summary>
        Document,
        /// <summary><see cref="BlockQuote"/>.</summary>
        BlockQuote,
        /// <summary><see cref="ListBlock"/>.</summary>
        List,
        /// <summary><see cref="ListItem"/>.</summary>
        ListItem,
        /// <summary><see cref="CodeBlock"/>.</summary>
        CodeBlock,
        /// <summary><see cref="HtmlBlock"/>.</summary>
        HtmlBlock,
        /// <summary><see cref="Paragraph"/>.</summary>
        Paragraph,
        /// <summary><see cref="Heading"/>.</summary>
        Heading,
        /// <summary><see cref="ThematicBreak"/>.</summary>
        ThematicBreak,
        /// <summary><see cref="Table"/>.</summary>
        Table,
        /// <summary><see cref="TableRow"/>.</summary>
        TableRow,
        /// <summary><see cref="TableCell"/>.</summary>
        TableCell,
        /// <summary><see cref="TextInline"/>.</summary>
        Text,
        /// <summary><see cref="CodeSpan"/>.</summary>
        CodeSpan,
        /// <summary><see cref="Emphasis"/>.</summary>
        Emphasis,
        /// <summary><see cref="Strong"/>.</summary>
        Strong,
        /// <summary><see cref="Strikethrough"/>.</summary>
        Strikethrough,
        /// <summary><see cref="Link"/>.</summary>
        Link,
        /// <summary><see cref="Image"/>.</summary>
        Image,
        /// <summary><see cref="HtmlInline"/>.</summary>
        HtmlInline,
        /// <summary><see cref="HardBreak"/>.</summary>
        HardBreak,
        /// <summary><see cref="SoftBreak"/>.</summary>
        SoftBreak,
    }

    /// <summary>
    /// Base class of every Markdown AST node. Container blocks hold blocks; paragraphs, headings,
    /// table cells, emphasis, links and images hold inlines; other nodes are leaves.
    /// The tree is mutable so callers (for example Chuvadi.Docs export) can build or edit documents
    /// and hand them to <see cref="MarkdownHtmlRenderer"/> or <see cref="MarkdownWriter"/>.
    /// </summary>
    public abstract class MarkdownNode
    {
        // Child storage (D-330, D-344): one NodeChildren object for the life of the node once it has a child, so that
        // Children stays a live view. Above ChunkThreshold children it stores them in chunks, so no child array reaches
        // the large object heap.
        internal const int ChunkThreshold = NodeChildren.ChunkThreshold;

        private NodeChildren? _children;

        /// <summary>The node kind.</summary>
        public abstract MarkdownNodeKind Kind { get; }

        /// <summary>True for block-level nodes.</summary>
        public abstract bool IsBlock { get; }

        /// <summary>Parent node, or null for the document or a detached node.</summary>
        public MarkdownNode? Parent { get; private set; }

        /// <summary>Where the node came from in the source text. Empty for nodes built in code.</summary>
        public SourceSpan Span { get; set; }

        /// <summary>Child nodes, in order.</summary>
        /// <remarks>
        /// Once the node has a child, the returned list is live: it reflects later changes to this node. Its
        /// concrete type is not part of the contract.
        /// </remarks>
        public IReadOnlyList<MarkdownNode> Children => (IReadOnlyList<MarkdownNode>?)_children ?? Array.Empty<MarkdownNode>();

        /// <summary>True if this node may hold children.</summary>
        public virtual bool CanHaveChildren => false;

        /// <summary>Appends a child. Throws if the node cannot hold children or the child already has a parent.</summary>
        public void AppendChild(MarkdownNode child) => InsertChild(Children.Count, child);

        /// <summary>Inserts a child at an index.</summary>
        public void InsertChild(int index, MarkdownNode child)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (!CanHaveChildren)
                throw new InvalidOperationException(Kind + " nodes cannot have children.");
            if (child.Parent is not null)
                throw new InvalidOperationException("The node already has a parent; remove it first.");
            if (child is MarkdownDocument)
                throw new InvalidOperationException("A document cannot be a child.");
            for (var p = this; p is not null; p = p.Parent)
                if (ReferenceEquals(p, child))
                    throw new InvalidOperationException("A node cannot be its own descendant.");
            (_children ??= new NodeChildren()).Insert(index, child);
            child.Parent = this;
        }

        /// <summary>Removes a child; returns false if it was not a child of this node.</summary>
        public bool RemoveChild(MarkdownNode child)
        {
            if (_children is null || !ReferenceEquals(child.Parent, this)) return false;
            if (!_children.Remove(child)) return false;
            child.Parent = null;
            return true;
        }

        /// <summary>Detaches this node from its parent.</summary>
        public void Remove() => Parent?.RemoveChild(this);

        internal void AppendChildUnchecked(MarkdownNode child)
        {
            (_children ??= new NodeChildren()).Add(child);
            child.Parent = this;
        }

        /// <summary>
        /// The children as a mutable list, for the parser's own passes. From then on the node keeps its children in
        /// that list (never in chunks), and <see cref="Children"/> reads through to it.
        /// </summary>
        internal List<MarkdownNode> MutableChildren => (_children ??= new NodeChildren()).AsList();

        internal void SetChildren(List<MarkdownNode> children)
        {
            (_children ??= new NodeChildren()).UseList(children);
            foreach (var c in children) c.Parent = this;
        }

        /// <summary>All descendants in document order (pre-order), without recursion.</summary>
        public IEnumerable<MarkdownNode> Descendants()
        {
            var stack = new Stack<(MarkdownNode node, int index)>();
            stack.Push((this, 0));
            while (stack.Count > 0)
            {
                var (node, index) = stack.Pop();
                var kids = node.Children;
                if (index < kids.Count)
                {
                    stack.Push((node, index + 1));
                    var child = kids[index];
                    yield return child;
                    stack.Push((child, 0));
                }
            }
        }

        /// <summary>Concatenated literal text of this node and its descendants (code, text, alt text; breaks as spaces/newlines).</summary>
        public string ToPlainText()
        {
            var sb = new System.Text.StringBuilder();
            if (this is ILiteralNode lit0) sb.Append(lit0.Literal);
            foreach (var d in Descendants())
            {
                switch (d)
                {
                    case SoftBreak: sb.Append(' '); break;
                    case HardBreak: sb.Append('\n'); break;
                    case HtmlInline: break;
                    case ILiteralNode lit: sb.Append(lit.Literal); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Calls the visitor method for this node's kind.</summary>
        public abstract void Accept(MarkdownVisitor visitor);
    }

    /// <summary>A node whose content is literal text.</summary>
    public interface ILiteralNode
    {
        /// <summary>The literal text.</summary>
        string Literal { get; }
    }

    /// <summary>Base class for block nodes.</summary>
    public abstract class MarkdownBlock : MarkdownNode
    {
        /// <inheritdoc />
        public sealed override bool IsBlock => true;
    }

    /// <summary>Base class for inline nodes.</summary>
    public abstract class MarkdownInline : MarkdownNode
    {
        /// <inheritdoc />
        public sealed override bool IsBlock => false;
    }

    /// <summary>
    /// The children of one <see cref="MarkdownNode"/>; <see cref="MarkdownNode.Children"/> returns this object, so it
    /// stays a live view. It stores the children in one of three ways:
    /// - one array, doubled as needed, up to <see cref="ChunkThreshold"/> entries (64 KiB);
    /// - past that, fixed chunks of 4,096 entries (32 KiB), so no array reaches the large object heap (D-330);
    /// - a <see cref="List{T}"/>, once the parser has asked for one (<see cref="MarkdownNode.MutableChildren"/>) or
    ///   given one (<see cref="MarkdownNode.SetChildren"/>); every read then goes through to that list.
    /// It behaves as <see cref="List{T}"/> does for the operations <see cref="MarkdownNode"/> uses, including the
    /// exception types, parameter names and messages, and the check that the list is not changed during enumeration.
    /// </summary>
    internal sealed class NodeChildren : IReadOnlyList<MarkdownNode>
    {
        internal const int ChunkThreshold = 8192;
        private const int Shift = 12;
        private const int ChunkSize = 1 << Shift;
        private const int Mask = ChunkSize - 1;

        // A MarkdownNode[] (one array), a MarkdownNode[][] (chunks) or a List<MarkdownNode>.
        private object _store = new MarkdownNode[4];
        private int _count;
        private int _version;

        /// <summary>True when the children are held in chunks (more than <see cref="ChunkThreshold"/> of them).</summary>
        internal bool IsChunked => _store is MarkdownNode[][];

        public int Count => _store is List<MarkdownNode> list ? list.Count : _count;

        public MarkdownNode this[int index]
        {
            get
            {
                switch (_store)
                {
                    case MarkdownNode[] one:
                        if ((uint)index >= (uint)_count) ThrowIndex();
                        return one[index];
                    case MarkdownNode[][] chunks:
                        if ((uint)index >= (uint)_count) ThrowIndex();
                        return chunks[index >> Shift][index & Mask];
                    default:
                        return ((List<MarkdownNode>)_store)[index];
                }
            }
        }

        private static void ThrowIndex() =>
            throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");

        public void Add(MarkdownNode node)
        {
            switch (_store)
            {
                case MarkdownNode[] one:
                    if (_count == one.Length)
                    {
                        if (_count >= ChunkThreshold) { ToChunks(one); AddToChunks(node); return; }
                        Array.Resize(ref one, one.Length * 2);
                        _store = one;
                    }
                    one[_count++] = node;
                    _version++;
                    return;
                case MarkdownNode[][]:
                    AddToChunks(node);
                    return;
                default:
                    ((List<MarkdownNode>)_store).Add(node);
                    return;
            }
        }

        public void Insert(int index, MarkdownNode node)
        {
            if (_store is List<MarkdownNode> list) { list.Insert(index, node); return; }
            if ((uint)index > (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index), "Index must be within the bounds of the List.");
            Add(node);
            for (int i = _count - 1; i > index; i--) Set(i, Get(i - 1));
            Set(index, node);
        }

        public bool Remove(MarkdownNode node)
        {
            if (_store is List<MarkdownNode> list) return list.Remove(node);
            int at = -1;
            for (int i = 0; i < _count; i++)
            {
                if (ReferenceEquals(Get(i), node)) { at = i; break; }
            }
            if (at < 0) return false;
            for (int i = at; i < _count - 1; i++) Set(i, Get(i + 1));
            _count--;
            Set(_count, null);
            _version++;
            return true;
        }

        /// <summary>Switches to a <see cref="List{T}"/> holding the children (once) and returns it.</summary>
        internal List<MarkdownNode> AsList()
        {
            if (_store is List<MarkdownNode> list) return list;
            list = new List<MarkdownNode>(_count);
            for (int i = 0; i < _count; i++) list.Add(Get(i)!);
            UseList(list);
            return list;
        }

        /// <summary>From now on the children are the given list.</summary>
        internal void UseList(List<MarkdownNode> list)
        {
            _store = list;
            _count = 0;
            _version++;
        }

        public IEnumerator<MarkdownNode> GetEnumerator()
        {
            if (_store is List<MarkdownNode> list) return list.GetEnumerator();
            return Enumerate();
        }

        private IEnumerator<MarkdownNode> Enumerate()
        {
            int version = _version;
            for (int i = 0; ; i++)
            {
                if (version != _version)
                    throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                if (i >= _count) yield break;
                yield return Get(i)!;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private void ToChunks(MarkdownNode[] one)
        {
            var chunks = new MarkdownNode?[]?[Math.Max(4, (_count >> Shift) * 2 + 2)];
            for (int c = 0; c * ChunkSize < _count; c++)
            {
                var chunk = new MarkdownNode?[ChunkSize];
                Array.Copy(one, c * ChunkSize, chunk, 0, Math.Min(ChunkSize, _count - c * ChunkSize));
                chunks[c] = chunk;
            }
            _store = chunks;
        }

        private void AddToChunks(MarkdownNode node)
        {
            var chunks = (MarkdownNode?[]?[])_store;
            int c = _count >> Shift;
            if (c == chunks.Length) { Array.Resize(ref chunks, chunks.Length * 2); _store = chunks; }
            (chunks[c] ??= new MarkdownNode?[ChunkSize])[_count & Mask] = node;
            _count++;
            _version++;
        }

        private MarkdownNode? Get(int i) => _store is MarkdownNode[] one ? one[i] : ((MarkdownNode?[]?[])_store)[i >> Shift]![i & Mask];

        private void Set(int i, MarkdownNode? node)
        {
            if (_store is MarkdownNode[] one) one[i] = node!;
            else ((MarkdownNode?[]?[])_store)[i >> Shift]![i & Mask] = node;
        }
    }
}
