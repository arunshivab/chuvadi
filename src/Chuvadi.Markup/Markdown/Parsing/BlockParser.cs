using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>
    /// Phase 1 of parsing: reads the input line by line and builds the block structure
    /// (CommonMark "Appendix: A parsing strategy", "Phase 1: block structure").
    /// </summary>
    internal sealed partial class BlockParser
    {
        private readonly string _src;
        private readonly MarkdownDocument _document;
        private readonly MarkdownOptions _options;
        private readonly LineCursor _line = new LineCursor();
        private readonly OpenBlock _root;
        private readonly NodeBudget _nodes;
        private readonly DefinitionBudget _definitions;
        private readonly int _maxDepth;
        private readonly CancellationToken _cancel;

        /// <summary>Raw inline content of every kept paragraph and heading, for the inline phase.</summary>
        public List<(MarkdownBlock Block, InlineContent Content)> InlineBlocks { get; } = new();

        public BlockParser(string source, MarkdownOptions options, MarkdownDocument document, CancellationToken cancel)
        {
            _src = source;
            _options = options;
            _document = document;
            _cancel = cancel;
            _nodes = new NodeBudget(options.Limits.MaxNodes);
            _definitions = new DefinitionBudget(options.Limits.MaxLinkReferenceDefinitions);
            _maxDepth = options.Limits.MaxNestingDepth;
            _root = new OpenBlock(BlockKind.Document, document, null);
        }

        public NodeBudget Nodes => _nodes;

        public void Run()
        {
            var poll = new CancellationPoll(_cancel);
            int start = 0;
            int lineNumber = 0;
            // TRACE R-MD-0047
            while (start < _src.Length)
            {
                poll.Tick();
                _line.Load(_src, start, lineNumber);
                ProcessLine();
                start = _line.NextLineStart;
                lineNumber++;
            }
            // TRACE R-MD-0048
            CloseBelow(_root);
        }

        private static OpenBlock Deepest(OpenBlock b)
        {
            while (b.OpenChild is not null) b = b.OpenChild;
            return b;
        }

        // ---------------------------------------------------------------- one line

        /// <summary>The last block the current line continued: new blocks of the line open below it.</summary>
        private OpenBlock _lineStart = null!;

        private enum Continuation { Matched, NotMatched, LineConsumed }

        private void ProcessLine()
        {
            // step 1: walk the open blocks and consume their continuation markers
            OpenBlock lastMatched = _root;
            OpenBlock? firstUnmatched = null;
            for (var b = _root.OpenChild; b is not null; b = b.OpenChild)
            {
                var result = Continue(b);
                if (result == Continuation.LineConsumed)
                {
                    FinishNonBlankLine(b);
                    return;
                }
                if (result == Continuation.NotMatched)
                {
                    firstUnmatched = b;
                    break;
                }
                lastMatched = b;
            }

            var deepestOpenBlock = Deepest(_root);
            var container = lastMatched;
            _lineStart = lastMatched;
            bool opened = false;

            // step 2: look for new block starts
            // TRACE R-MD-0049
            if (!IsLiteral(lastMatched.Kind))
            {
                while (true)
                {
                    bool paragraphContext = !opened && deepestOpenBlock.Kind == BlockKind.Paragraph;
                    int indent = _line.Indent;
                    bool blank = _line.RestIsBlank;
                    if (blank) break;
                    int first = _line.FirstNonSpace;

                    // TRACE R-MD-0050
                    if (indent >= 4)
                    {
                        if (paragraphContext) break;
                        _line.SkipColumns(4);
                        container = Open(BlockKind.IndentedCode, container, ref opened, firstUnmatched, _line.Pos);
                        container.Literal = new StringBuilder();
                        break;
                    }

                    char c = _src[first];
                    if (c == '>' && HasRoom(container, BlockKind.BlockQuote, 1))
                    {
                        // TRACE R-MD-0051
                        _line.SkipSpacesThenAdvance(1);
                        if (_line.At(_line.Pos) == ' ' || _line.At(_line.Pos) == '\t') _line.SkipColumns(1);
                        container = Open(BlockKind.BlockQuote, container, ref opened, firstUnmatched, first);
                        container.End = _line.LineEnd;
                        continue;
                    }

                    if (c == '#' && TryAtxHeading(first, ref container, ref opened, firstUnmatched)) return;

                    if ((c == '`' || c == '~') && TryFenceOpening(first, indent, ref container, ref opened, firstUnmatched)) return;

                    if (c == '<')
                    {
                        // TRACE R-MD-0105
                        bool type7Allowed = opened || container.Kind != BlockKind.Paragraph;
                        int type = HtmlSyntax.BlockStart(_src, first, _line.LineEnd, mayBeType7: type7Allowed);
                        if (type > 0)
                        {
                            container = Open(BlockKind.HtmlBlock, container, ref opened, firstUnmatched, first);
                            container.HtmlType = type;
                            container.Literal = new StringBuilder();
                            break;
                        }
                    }

                    // TRACE R-MD-0052
                    if (!opened && container.Kind == BlockKind.Paragraph && (c == '=' || c == '-') && IsSetextUnderline(first))
                    {
                        if (TurnIntoSetextHeading(container, c == '=' ? 1 : 2)) return;
                    }

                    // TRACE R-MD-0192
                    if (_options.Tables && !opened && container.Kind == BlockKind.Paragraph && (c == '|' || c == ':' || c == '-' || c == '\v' || c == '\f')
                        && !StartsListItemWithContent(first) && TryTable(container)) return;

                    if ((c == '*' || c == '-' || c == '_') && IsThematicBreak(first))
                    {
                        // TRACE R-MD-0053
                        container = Open(BlockKind.ThematicBreak, container, ref opened, firstUnmatched, first);
                        ((ThematicBreak)container.Node).Marker = c;
                        container.End = _line.LineEnd;
                        TouchAncestors(container, _line.LineEnd);
                        ClearBlankFlags(container);
                        return;
                    }

                    // TRACE R-MD-0102
                    bool interruptsParagraph = !opened && container.Kind == BlockKind.Paragraph;
                    if (TryListItem(first, interruptsParagraph, ref container, ref opened, firstUnmatched)) continue;

                    break;
                }
            }

            // step 3: the rest of the line
            bool restBlank = _line.RestIsBlank;
            // TRACE R-MD-0054
            if (!opened && firstUnmatched is not null && deepestOpenBlock.Kind == BlockKind.Paragraph && !restBlank)
            {
                AddParagraphLine(deepestOpenBlock, lazy: true);
                // TRACE R-MD-0267
                // a lazy line extends the unmatched blocks around its paragraph only once the paragraph is known to
                // stay (it is not all link reference definitions): see FinishParagraph (D-232, N3)
                ClearBlankFlags(deepestOpenBlock);
                deepestOpenBlock.HasLazyEnd = true;
                // the blocks this line did continue (the last matched one and those around it) do take in its end
                for (var m = lastMatched; m.Kind != BlockKind.Document; m = m.Parent!)
                {
                    if (_line.LineEnd > m.End) m.End = _line.LineEnd;
                    if (m.Kind == BlockKind.List) m.LazyLineEnd = _line.LineEnd;
                }
                return;
            }

            if (!opened && firstUnmatched is not null)
            {
                CloseFrom(firstUnmatched);
                // closed once: opening a block below must not close them again
                firstUnmatched = null;
            }

            if (restBlank)
            {
                // TRACE R-MD-0055
                // TRACE R-MD-0270
                // a blank line inside an open fenced code block is part of the code and loosens no list
                // ("- ~~~\n\n-" is tight; D-232, C2)
                if (!opened && container.Kind != BlockKind.FencedCode)
                    for (var b = container; b is not null && b.Kind != BlockKind.BlockQuote; b = b.Parent) b.EndsWithBlank = true;
                // TRACE R-MD-0265
                // a block opened on this line (with nothing after its marker) still extends the blocks around it to the
                // end of the line: "*\n\t>" gives the item the span 0+4 (D-232, C1)
                else TouchAncestors(container, _line.LineEnd);
                if (container.Kind == BlockKind.IndentedCode || container.Kind == BlockKind.FencedCode || container.Kind == BlockKind.HtmlBlock)
                {
                    AddLiteralLine(container, blank: true);
                    // TRACE R-MD-0104
                    TouchAncestors(container, _line.LineEnd);
                }
                return;
            }

            switch (container.Kind)
            {
                case BlockKind.Paragraph:
                    AddParagraphLine(container);
                    break;
                case BlockKind.IndentedCode:
                case BlockKind.FencedCode:
                    AddLiteralLine(container, blank: false);
                    break;
                case BlockKind.HtmlBlock:
                    AddLiteralLine(container, blank: false);
                    // TRACE R-MD-0056
                    if (container.HtmlType <= 5 && HtmlSyntax.BlockEnds(container.HtmlType, _src, _line.Pos, _line.LineEnd))
                    {
                        FinishNonBlankLine(container);
                        Close(container, displaced: false);
                        return;
                    }
                    break;
                case BlockKind.Table when TryAddBodyRow(container):
                    break;
                default:
                    // TRACE R-MD-0057
                    container = Open(BlockKind.Paragraph, container, ref opened, firstUnmatched, _line.FirstNonSpace);
                    container.Lines = new List<ContentLine>();
                    AddParagraphLine(container);
                    break;
            }
            FinishNonBlankLine(container);
        }

        private static bool IsLiteral(BlockKind kind)
            => kind == BlockKind.IndentedCode || kind == BlockKind.FencedCode || kind == BlockKind.HtmlBlock;

        /// <summary>After a non-blank line reached <paramref name="target"/>: update container ends and blank flags.</summary>
        private void FinishNonBlankLine(OpenBlock target)
        {
            TouchAncestors(target, _line.LineEnd);
            ClearBlankFlags(target);
        }

        // TRACE R-MD-0058
        private static void ClearBlankFlags(OpenBlock target)
        {
            for (var b = target; b is not null; b = b.Parent) b.EndsWithBlank = false;
        }

        // TRACE R-MD-0059
        private void TouchAncestors(OpenBlock target, int end)
        {
            for (var b = target.Parent; b is not null && b.Kind != BlockKind.Document; b = b.Parent)
                if (end > b.End) b.End = end;
        }

        // ---------------------------------------------------------------- continuation (step 1)

        private Continuation Continue(OpenBlock b)
        {
            switch (b.Kind)
            {
                case BlockKind.BlockQuote:
                    {
                        // TRACE R-MD-0060
                        if (_line.Indent > 3) return Continuation.NotMatched;
                        int first = _line.FirstNonSpace;
                        if (_line.At(first) != '>') return Continuation.NotMatched;
                        _line.SkipSpacesThenAdvance(1);
                        if (_line.At(_line.Pos) == ' ' || _line.At(_line.Pos) == '\t') _line.SkipColumns(1);
                        b.End = _line.LineEnd;
                        TouchAncestors(b, _line.LineEnd);
                        return Continuation.Matched;
                    }
                case BlockKind.List:
                    // TRACE R-MD-0061
                    return Continuation.Matched;
                case BlockKind.ListItem:
                    {
                        if (_line.RestIsBlank)
                        {
                            // TRACE R-MD-0062
                            if (b.ChildCount == 0) return Continuation.NotMatched;
                            int skip = _line.Indent;
                            _line.SkipColumns(skip < b.ItemContentIndent ? skip : b.ItemContentIndent);
                            return Continuation.Matched;
                        }
                        // TRACE R-MD-0063
                        if (_line.Indent >= b.ItemContentIndent)
                        {
                            _line.SkipColumns(b.ItemContentIndent);
                            return Continuation.Matched;
                        }
                        return Continuation.NotMatched;
                    }
                case BlockKind.Paragraph:
                    // TRACE R-MD-0064
                    return _line.RestIsBlank ? Continuation.NotMatched : Continuation.Matched;
                case BlockKind.IndentedCode:
                    {
                        // TRACE R-MD-0065
                        int indent = _line.Indent;
                        if (indent >= 4)
                        {
                            _line.SkipColumns(4);
                            return Continuation.Matched;
                        }
                        if (_line.RestIsBlank)
                        {
                            _line.SkipColumns(indent);
                            return Continuation.Matched;
                        }
                        return Continuation.NotMatched;
                    }
                case BlockKind.FencedCode:
                    {
                        int indent = _line.Indent;
                        // TRACE R-MD-0066
                        if (indent <= 3 && IsClosingFence(b, _line.FirstNonSpace))
                        {
                            b.End = _line.LineEnd;
                            Close(b, displaced: false);
                            return Continuation.LineConsumed;
                        }
                        // TRACE R-MD-0067
                        _line.SkipColumns(indent < b.FenceIndent ? indent : b.FenceIndent);
                        return Continuation.Matched;
                    }
                case BlockKind.HtmlBlock:
                    // TRACE R-MD-0068
                    if (b.HtmlType >= 6 && _line.RestIsBlank) return Continuation.NotMatched;
                    return Continuation.Matched;
                case BlockKind.ThematicBreak:
                    // TRACE R-MD-0069
                    return Continuation.Matched;
                case BlockKind.Table:
                    // TRACE R-MD-0193
                    if (_line.RestIsBlank) return Continuation.NotMatched;
                    // TRACE R-MD-0261
                    // a row of only pipes (and spaces, tabs, U+000B, U+000C) ends the table (D-232, N9)
                    return OnlyPipes(_line.FirstNonSpace, _line.LineEnd) ? Continuation.NotMatched : Continuation.Matched;
                default:
                    return Continuation.NotMatched;
            }
        }

        // ---------------------------------------------------------------- opening and closing

        // TRACE R-MD-0070
        private static bool CanContain(BlockKind parent, BlockKind child) => parent switch
        {
            BlockKind.Document or BlockKind.BlockQuote or BlockKind.ListItem => child != BlockKind.ListItem,
            BlockKind.List => child == BlockKind.ListItem,
            _ => false,
        };

        /// <summary>
        /// Opens a new block of <paramref name="kind"/> as a child of <paramref name="container"/> (or of the
        /// nearest ancestor that can hold it). On the first block opened for a line, the open blocks that the
        /// line did not continue are closed first.
        /// </summary>
        private OpenBlock Open(BlockKind kind, OpenBlock container, ref bool opened, OpenBlock? firstUnmatched, int start)
        {
            if (!opened)
            {
                if (firstUnmatched is not null) CloseFrom(firstUnmatched);
                opened = true;
            }
            // TRACE R-MD-0071
            while (!CanContain(container.Kind, kind))
            {
                var parent = container.Parent!;
                Close(container, displaced: true);
                container = parent;
            }
            // TRACE R-MD-0072
            if (container.ChildCount > 0 && container.EndsWithBlank)
            {
                if (container.Kind == BlockKind.ListItem) ((ListBlock)container.Parent!.Node).IsTight = false;
                else if (container.Kind == BlockKind.List) ((ListBlock)container.Node).IsTight = false;
            }
            var node = NewNode(kind);
            var block = new OpenBlock(kind, node, container) { Start = start, End = _line.LineEnd };
            block.FirstInItem = container.Kind == BlockKind.ListItem && container.ChildCount == 0;
            container.Node.AppendChildUnchecked(node);
            container.ChildCount++;
            container.OpenChild = block;
            return block;
        }

        private static MarkdownBlock NewNode(BlockKind kind) => kind switch
        {
            BlockKind.BlockQuote => new BlockQuote(),
            BlockKind.List => new ListBlock(),
            BlockKind.ListItem => new ListItem(),
            BlockKind.Paragraph => new Paragraph(),
            BlockKind.AtxHeading or BlockKind.SetextHeading => new Heading(),
            BlockKind.ThematicBreak => new ThematicBreak(),
            BlockKind.IndentedCode or BlockKind.FencedCode => new CodeBlock(),
            BlockKind.HtmlBlock => new HtmlBlock(),
            BlockKind.Table => new Table(),
            _ => throw new System.InvalidOperationException(),
        };

        /// <summary>Closes <paramref name="from"/> and every open block below it.</summary>
        private void CloseFrom(OpenBlock from)
        {
            CloseBelow(from);
            Close(from, displaced: false);
        }

        // TRACE R-MD-0242
        /// <summary>
        /// Closes every open block below <paramref name="b"/>, deepest first, walking up once from the deepest
        /// open block so that closing a chain of n blocks takes n steps.
        /// </summary>
        private void CloseBelow(OpenBlock b)
        {
            var x = Deepest(b);
            while (!ReferenceEquals(x, b))
            {
                var up = x.Parent!;
                Close(x, displaced: false);
                x = up;
            }
        }

        /// <summary>
        /// Closes a block whose children are already closed. A block that the current line continued but
        /// that has to give way to a new block (<paramref name="displaced"/>) ends at the end of this line.
        /// </summary>
        private void Close(OpenBlock b, bool displaced)
        {
            if (b.OpenChild is not null) CloseBelow(b);
            // TRACE R-MD-0073
            if (displaced) b.End = _line.LineEnd;
            var parent = b.Parent!;
            parent.OpenChild = null;
            bool keep = true;
            switch (b.Kind)
            {
                case BlockKind.Paragraph:
                    keep = FinishParagraph(b);
                    break;
                case BlockKind.IndentedCode:
                    {
                        // TRACE R-MD-0074
                        var code = (CodeBlock)b.Node;
                        code.IsFenced = false;
                        code.Code = CharacterReferences.Safe(b.Literal!.ToString(0, b.LiteralKeep));
                        break;
                    }
                case BlockKind.FencedCode:
                    ((CodeBlock)b.Node).Code = CharacterReferences.Safe(b.Literal!.ToString());
                    break;
                case BlockKind.HtmlBlock:
                    {
                        var html = (HtmlBlock)b.Node;
                        html.BlockType = b.HtmlType;
                        html.Html = CharacterReferences.Safe(b.Literal!.ToString());
                        break;
                    }
                case BlockKind.AtxHeading:
                case BlockKind.SetextHeading:
                    // TRACE R-MD-0100
                    InlineBlocks.Add((b.Node, BuildContent(b.Lines!)));
                    break;
                case BlockKind.List:
                    {
                        // TRACE R-MD-0075
                        var kids = b.Node.Children;
                        if (kids.Count > 0)
                        {
                            b.Start = kids[0].Span.Start;
                            if (!displaced)
                            {
                                int last = kids[kids.Count - 1].Span.End;
                                // TRACE R-MD-0267
                                // a lazy line the list was continued on counts even when its item does not take it in
                                b.End = b.LazyLineEnd > last ? b.LazyLineEnd : last;
                            }
                        }
                        break;
                    }
            }
            if (!keep)
            {
                parent.Node.RemoveChild(b.Node);
                parent.ChildCount--;
                return;
            }
            b.Node.Span = new SourceSpan(b.Start, b.End > b.Start ? b.End - b.Start : 0);
            _nodes.Take();
        }

        // TRACE R-MD-0076
        /// <summary>
        /// Whether <paramref name="levels"/> new container levels of <paramref name="kind"/> fit below the block
        /// that would hold them: a container is opened only while its depth (the document is 0) stays below
        /// <see cref="MarkupLimits.MaxNestingDepth"/>, so its content can still sit at that depth.
        /// </summary>
        private bool HasRoom(OpenBlock container, BlockKind kind, int levels)
        {
            var parent = container;
            while (!CanContain(parent.Kind, kind)) parent = parent.Parent!;
            // TRACE R-MD-0238
            // a list or list item: its first new level must stay below the limit, counted from the deeper of its
            // parent and the last block this line continued (so a paragraph it would interrupt counts)
            if (kind != BlockKind.BlockQuote)
                return (parent.Depth > _lineStart.Depth ? parent.Depth : _lineStart.Depth) + 1 < _maxDepth;
            return parent.Depth + levels < _maxDepth;
        }
    }
}
