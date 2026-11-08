using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>The kinds of block the line-by-line phase works with.</summary>
    internal enum BlockKind
    {
        Document,
        BlockQuote,
        List,
        ListItem,
        Paragraph,
        AtxHeading,
        SetextHeading,
        ThematicBreak,
        IndentedCode,
        FencedCode,
        HtmlBlock,
        Table,
    }

    /// <summary>
    /// One source line of a paragraph or heading after its leading spaces and tabs were removed: content
    /// [Start, End), the offset of the next line, how many columns of indentation were removed, and where the
    /// line break before this line ends (BreakEnd), and where the line began after container markers (RawStart).
    /// </summary>
    internal readonly record struct ContentLine(int Start, int End, int Next, int Indent, int BreakEnd, int RawStart, int Shift = 0);

    /// <summary>
    /// The raw inline content of a paragraph or heading: its lines joined with '\n', and the source offset
    /// of each line so that any position in the joined text maps back to the source.
    /// </summary>
    internal sealed class InlineContent
    {
        public InlineContent(string text, int[] textStarts, int[] sourceStarts, int[] breakEnds)
        {
            Text = text;
            TextStarts = textStarts;
            SourceStarts = sourceStarts;
            BreakEnds = breakEnds;
        }

        /// <summary>For each line, the source offset where a line break before it ends.</summary>
        public int[] BreakEnds { get; }

        /// <summary>Index of the line that starts at text position <paramref name="textIndex"/> or contains it.</summary>
        public int LineOf(int textIndex)
        {
            int lo = 0, hi = TextStarts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (TextStarts[mid] <= textIndex) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        public string Text { get; }

        /// <summary>Index in <see cref="Text"/> where each line begins.</summary>
        public int[] TextStarts { get; }

        /// <summary>Source offset of each line's first char.</summary>
        public int[] SourceStarts { get; }

        // TRACE R-MD-0046
        /// <summary>Source offset of a position in <see cref="Text"/>.</summary>
        public int ToSource(int textIndex)
        {
            int lo = 0, hi = TextStarts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (TextStarts[mid] <= textIndex) lo = mid;
                else hi = mid - 1;
            }
            return SourceStarts[lo] + (textIndex - TextStarts[lo]);
        }
    }

    /// <summary>A block that is still being built, with what the line phase needs to know about it.</summary>
    internal sealed class OpenBlock
    {
        public OpenBlock(BlockKind kind, MarkdownBlock node, OpenBlock? parent)
        {
            Kind = kind;
            Node = node;
            Parent = parent;
            Depth = parent is null ? 0 : parent.Depth + 1;
        }

        public BlockKind Kind { get; set; }

        public MarkdownBlock Node { get; set; }

        public OpenBlock? Parent { get; }

        /// <summary>The open last child, or null.</summary>
        public OpenBlock? OpenChild { get; set; }

        /// <summary>Number of block levels above this one (the document is 0).</summary>
        public int Depth { get; }

        public int Start { get; set; }

        public int End { get; set; }

        /// <summary>True while the last line that reached this block was blank.</summary>
        public bool EndsWithBlank { get; set; }

        /// <summary>Number of child blocks added so far.</summary>
        public int ChildCount { get; set; }

        // list item
        public int ItemContentIndent { get; set; }

        // fenced code
        public char FenceChar { get; set; }
        public int FenceLength { get; set; }
        public int FenceIndent { get; set; }

        // HTML block
        public int HtmlType { get; set; }

        // table
        public int TableColumns { get; set; }

        /// <summary>
        /// For a paragraph: the index of its line that was a delimiter row refused for the line before it; that line
        /// may not be a table header itself (-1 for none).
        /// </summary>
        public int RefusedDelimiterLine { get; set; } = -1;

        /// <summary>For a paragraph: its last line so far was a lazy continuation line, not yet passed to its ancestors.</summary>
        public bool HasLazyEnd { get; set; }

        /// <summary>For a list: the end of the last lazy line it was continued on (-1 for none).</summary>
        public int LazyLineEnd { get; set; } = -1;

        // paragraph that is the first block of a list item (task list markers)
        public bool FirstInItem { get; set; }

        // paragraph and heading text, one entry per line
        public List<ContentLine>? Lines { get; set; }

        // code and HTML text
        public StringBuilder? Literal { get; set; }

        /// <summary>Length of <see cref="Literal"/> after its last non-blank line (indented code trims after it).</summary>
        public int LiteralKeep { get; set; }
    }
}
