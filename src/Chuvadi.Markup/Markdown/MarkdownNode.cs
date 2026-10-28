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
        private List<MarkdownNode>? _children;

        /// <summary>The node kind.</summary>
        public abstract MarkdownNodeKind Kind { get; }

        /// <summary>True for block-level nodes.</summary>
        public abstract bool IsBlock { get; }

        /// <summary>Parent node, or null for the document or a detached node.</summary>
        public MarkdownNode? Parent { get; private set; }

        /// <summary>Where the node came from in the source text. Empty for nodes built in code.</summary>
        public SourceSpan Span { get; set; }

        /// <summary>Child nodes, in order.</summary>
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
            _children ??= new List<MarkdownNode>();
            _children.Insert(index, child);
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
            _children ??= new List<MarkdownNode>();
            _children.Add(child);
            child.Parent = this;
        }

        internal List<MarkdownNode> MutableChildren => _children ??= new List<MarkdownNode>();

        internal void SetChildren(List<MarkdownNode> children)
        {
            _children = children;
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
}
