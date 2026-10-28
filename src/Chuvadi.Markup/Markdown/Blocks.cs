using System;
using System.Collections.Generic;

namespace Chuvadi.Markup.Markdown
{
    /// <summary>The root of a parsed Markdown document.</summary>
    public sealed class MarkdownDocument : MarkdownBlock
    {
        private readonly Dictionary<string, LinkReferenceDefinition> _definitions = new(StringComparer.Ordinal);
        private int[]? _lineStarts;

        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Document;

        /// <inheritdoc />
        public override bool CanHaveChildren => true;

        /// <summary>The source text the document was parsed from (empty for documents built in code).</summary>
        public string Source { get; internal set; } = string.Empty;

        /// <summary>Link reference definitions, keyed by normalized label (case-folded, whitespace collapsed).</summary>
        public IReadOnlyDictionary<string, LinkReferenceDefinition> LinkDefinitions => _definitions;

        internal Dictionary<string, LinkReferenceDefinition> MutableDefinitions => _definitions;

        /// <summary>Adds or replaces a definition (the first definition of a label wins when parsing).</summary>
        public void SetLinkDefinition(LinkReferenceDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            _definitions[LinkReferenceDefinition.NormalizeLabel(definition.Label)] = definition;
        }

        /// <summary>Converts a source offset into a one-based line and column.</summary>
        public SourcePosition GetPosition(int offset)
        {
            if (offset < 0 || offset > Source.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            var starts = _lineStarts ??= ComputeLineStarts(Source);
            int idx = Array.BinarySearch(starts, offset);
            if (idx < 0) idx = ~idx - 1;
            return new SourcePosition(idx + 1, offset - starts[idx] + 1);
        }

        private static int[] ComputeLineStarts(string s)
        {
            var list = new List<int> { 0 };
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\n') list.Add(i + 1);
                else if (s[i] == '\r')
                {
                    if (i + 1 < s.Length && s[i + 1] == '\n') i++;
                    list.Add(i + 1);
                }
            }
            return list.ToArray();
        }

        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitDocument(this);
    }

    /// <summary>A link reference definition (<c>[label]: destination "title"</c>).</summary>
    public sealed class LinkReferenceDefinition
    {
        /// <summary>Creates a definition.</summary>
        public LinkReferenceDefinition(string label, string destination, string? title)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            Title = title;
        }

        /// <summary>The label as written (without brackets).</summary>
        public string Label { get; }

        /// <summary>The destination with escapes and entities resolved.</summary>
        public string Destination { get; }

        /// <summary>The title, or null.</summary>
        public string? Title { get; }

        /// <summary>Where the definition came from.</summary>
        public SourceSpan Span { get; init; }

        /// <summary>CommonMark label normalization: trim, collapse internal whitespace, Unicode case fold.</summary>
        public static string NormalizeLabel(string label)
        {
            var sb = new System.Text.StringBuilder(label.Length);
            bool space = false;
            foreach (char c in label.Trim(' ', '\t', '\n', '\r', '\f'))
            {
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f') { space = true; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
            return Internal.CaseFolding.Fold(sb.ToString());
        }
    }

    /// <summary>A block quote (<c>&gt;</c>).</summary>
    public sealed class BlockQuote : MarkdownBlock
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.BlockQuote;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitBlockQuote(this);
    }

    /// <summary>Bullet or ordered list.</summary>
    public enum ListKind
    {
        /// <summary>Bullet list (<c>-</c>, <c>+</c>, <c>*</c>).</summary>
        Bullet,
        /// <summary>Ordered list (<c>1.</c>, <c>1)</c>).</summary>
        Ordered,
    }

    /// <summary>A list; its children are <see cref="ListItem"/>s.</summary>
    public sealed class ListBlock : MarkdownBlock
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.List;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>Bullet or ordered.</summary>
        public ListKind ListKind { get; set; }
        /// <summary>Bullet character for bullet lists ('-', '+' or '*').</summary>
        public char BulletChar { get; set; } = '-';
        /// <summary>Start number for ordered lists.</summary>
        public int Start { get; set; } = 1;
        /// <summary>Delimiter for ordered lists ('.' or ')').</summary>
        public char Delimiter { get; set; } = '.';
        /// <summary>True if the list is tight (paragraphs not wrapped in &lt;p&gt;).</summary>
        public bool IsTight { get; set; } = true;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitList(this);
    }

    /// <summary>State of a GFM task list item.</summary>
    public enum TaskState
    {
        /// <summary>Not a task item.</summary>
        None,
        /// <summary><c>[ ]</c>.</summary>
        Unchecked,
        /// <summary><c>[x]</c> or <c>[X]</c>.</summary>
        Checked,
    }

    /// <summary>A list item.</summary>
    public sealed class ListItem : MarkdownBlock
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.ListItem;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>GFM task state.</summary>
        public TaskState TaskState { get; set; }
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitListItem(this);
    }

    /// <summary>A fenced or indented code block.</summary>
    public sealed class CodeBlock : MarkdownBlock, ILiteralNode
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.CodeBlock;
        /// <summary>True for fenced blocks, false for indented.</summary>
        public bool IsFenced { get; set; }
        /// <summary>Fence character ('`' or '~') for fenced blocks.</summary>
        public char FenceChar { get; set; } = '`';
        /// <summary>Opening fence length for fenced blocks.</summary>
        public int FenceLength { get; set; } = 3;
        /// <summary>Info string with escapes and entities resolved (empty if none).</summary>
        public string Info { get; set; } = string.Empty;
        /// <summary>First word of <see cref="Info"/>, or empty.</summary>
        public string Language
        {
            get
            {
                int i = 0;
                while (i < Info.Length && Info[i] != ' ' && Info[i] != '\t') i++;
                return Info.Substring(0, i);
            }
        }
        /// <summary>The code, including the final newline of each line.</summary>
        public string Code { get; set; } = string.Empty;
        string ILiteralNode.Literal => Code;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitCodeBlock(this);
    }

    /// <summary>A raw HTML block (CommonMark start conditions 1 to 7).</summary>
    public sealed class HtmlBlock : MarkdownBlock
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.HtmlBlock;
        /// <summary>CommonMark HTML block type, 1 to 7 (0 for blocks built in code).</summary>
        public int BlockType { get; set; }
        /// <summary>The raw HTML, including the final newline of each line.</summary>
        public string Html { get; set; } = string.Empty;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitHtmlBlock(this);
    }

    /// <summary>A paragraph; children are inlines.</summary>
    public sealed class Paragraph : MarkdownBlock
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Paragraph;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitParagraph(this);
    }

    /// <summary>An ATX (<c>#</c>) or setext (underlined) heading; children are inlines.</summary>
    public sealed class Heading : MarkdownBlock
    {
        private int _level = 1;
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Heading;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>Level 1 to 6.</summary>
        public int Level
        {
            get => _level;
            set => _level = value is >= 1 and <= 6 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }
        /// <summary>True for setext headings.</summary>
        public bool IsSetext { get; set; }
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitHeading(this);
    }

    /// <summary>A thematic break (<c>---</c>, <c>***</c>, <c>___</c>).</summary>
    public sealed class ThematicBreak : MarkdownBlock
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.ThematicBreak;
        /// <summary>The marker character.</summary>
        public char Marker { get; set; } = '-';
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitThematicBreak(this);
    }

    /// <summary>Column alignment of a GFM table.</summary>
    public enum TableAlignment
    {
        /// <summary>No alignment given.</summary>
        None,
        /// <summary><c>:---</c>.</summary>
        Left,
        /// <summary><c>:---:</c>.</summary>
        Center,
        /// <summary><c>---:</c>.</summary>
        Right,
    }

    /// <summary>A GFM table; children are <see cref="TableRow"/>s, the first being the header row.</summary>
    public sealed class Table : MarkdownBlock
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Table;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>One alignment per column.</summary>
        public List<TableAlignment> Alignments { get; } = new List<TableAlignment>();
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitTable(this);
    }

    /// <summary>A table row; children are <see cref="TableCell"/>s.</summary>
    public sealed class TableRow : MarkdownBlock
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.TableRow;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>True for the header row.</summary>
        public bool IsHeader { get; set; }
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitTableRow(this);
    }

    /// <summary>A table cell; children are inlines.</summary>
    public sealed class TableCell : MarkdownBlock
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.TableCell;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>Alignment of the cell's column.</summary>
        public TableAlignment Alignment { get; set; }
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitTableCell(this);
    }
}
