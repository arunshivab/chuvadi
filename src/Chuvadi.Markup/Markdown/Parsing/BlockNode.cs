using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Markup.Markdown.Parsing
{
    internal enum BlockType
    {
        Document,
        BlockQuote,
        List,
        Item,
        CodeBlock,
        HtmlBlock,
        Paragraph,
        Heading,
        ThematicBreak,
        Table,
        TableRow,
    }

    /// <summary>Mutable block used while parsing; converted to the public AST at the end.</summary>
    internal sealed class BlockNode
    {
        public BlockNode(BlockType type, BlockNode? parent, int startOffset, int startLine)
        {
            Type = type;
            Parent = parent;
            StartOffset = startOffset;
            EndOffset = startOffset;
            StartLine = startLine;
        }

        public BlockType Type;
        public BlockNode? Parent;
        public readonly List<BlockNode> Children = new List<BlockNode>(0);
        public bool Open = true;
        public bool LastLineBlank;
        public int StartOffset;
        public int EndOffset;
        public int StartLine;
        public int Depth;

        // Text content (paragraph, heading, code, html). SourceMap holds (contentIndex, sourceOffset) pairs, one per line.
        public StringBuilder? Content;
        public List<int>? MapContent;
        public List<int>? MapSource;

        // List / item
        public ListKind ListKind;
        public char BulletChar;
        public int ListStart;
        public char ListDelimiter;
        public int MarkerOffset;
        public int Padding;
        public bool Tight;
        public TaskState TaskState;

        // Code block
        public bool Fenced;
        public char FenceChar;
        public int FenceLength;
        public int FenceOffset;
        public string Info = string.Empty;

        // Heading
        public int Level;
        public bool Setext;

        // HTML block
        public int HtmlType;

        // Thematic break
        public char Marker;

        // Table
        public bool TableVisited;
        public List<TableAlignment>? Alignments;
        public bool IsHeaderRow;
        public List<CellData>? Cells;
        public int TableRows;
        public int TableNonEmptyCells;

        // Parsed inline content
        public InlineNode? Inlines;

        public BlockNode? LastChild => Children.Count == 0 ? null : Children[Children.Count - 1];

        public void AddContentLine(string text, int sourceOffset)
        {
            Content ??= new StringBuilder();
            MapContent ??= new List<int>();
            MapSource ??= new List<int>();
            MapContent.Add(Content.Length);
            MapSource.Add(sourceOffset);
            Content.Append(text);
        }
    }

    internal sealed class CellData
    {
        public string Text = string.Empty;
        public int SourceOffset;
        public InlineNode? Inlines;
    }
}
