using System.Text;

namespace Chuvadi.Markup.Markdown.Parsing
{
    internal enum InlineType
    {
        Text,
        Code,
        Emphasis,
        Strong,
        Strikethrough,
        Link,
        Image,
        Html,
        HardBreak,
        SoftBreak,
        Container, // root holder for a paragraph's inlines
    }

    /// <summary>Doubly linked inline node used during inline parsing (O(1) splicing for emphasis and links).</summary>
    internal sealed class InlineNode
    {
        public InlineNode(InlineType type) => Type = type;

        public InlineType Type;
        public InlineNode? Parent, Prev, Next, First, Last;
        public string Literal = string.Empty;
        // Length Literal will be cut to at the end of emphasis processing (-1: not shortened).
        public int PendingLength = -1;
        public int Start, End; // subject offsets

        // links / images
        public string Destination = string.Empty;
        public string? Title;
        public LinkStyle Style;
        public string? Label;
        public bool IsEmail;

        // emphasis / code / breaks
        public char Delimiter;
        public int DelimiterLength;
        public bool IsBackslash;

        public void Append(InlineNode child)
        {
            child.Parent = this;
            child.Next = null;
            child.Prev = Last;
            if (Last is not null) Last.Next = child; else First = child;
            Last = child;
        }

        public void InsertAfter(InlineNode node)
        {
            node.Parent = Parent;
            node.Prev = this;
            node.Next = Next;
            if (Next is not null) Next.Prev = node; else if (Parent is not null) Parent.Last = node;
            Next = node;
        }

        public void Unlink()
        {
            if (Prev is not null) Prev.Next = Next; else if (Parent is not null) Parent.First = Next;
            if (Next is not null) Next.Prev = Prev; else if (Parent is not null) Parent.Last = Prev;
            Parent = Prev = Next = null;
        }
    }
}
