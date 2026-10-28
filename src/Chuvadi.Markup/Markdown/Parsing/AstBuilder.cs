using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>Turns the parser's internal block tree into the public AST, running inline parsing on leaf text.</summary>
    internal static class AstBuilder
    {
        private static readonly char[] s_trim = { ' ', '\t', '\n', '\r', '\f' }; // cmark_isspace (no VT)

        public static void Build(BlockNode root, MarkdownDocument doc, InlineParser inline, MarkdownOptions options, CancellationPoll poll)
        {
            int maxDepth = options.Limits.MaxNestingDepth;
            var stack = new Stack<(BlockNode block, MarkdownNode target)>();
            for (int i = root.Children.Count - 1; i >= 0; i--) stack.Push((root.Children[i], doc));
            var pending = new List<(BlockNode block, MarkdownNode target)>();

            while (stack.Count > 0)
            {
                poll.Tick();
                var (b, target) = stack.Pop();
                MarkdownNode? node = Convert(b, inline, options, maxDepth);
                if (node is null) continue;
                target.AppendChildUnchecked(node);
                if (b.Type == BlockType.Table)
                {
                    BuildTable(b, (Table)node, inline, maxDepth);
                    continue;
                }
                if (b.Type == BlockType.Item && node is ListItem li && options.TaskLists)
                    DetectTask(b, li);
                for (int i = b.Children.Count - 1; i >= 0; i--) stack.Push((b.Children[i], node));
            }
            FixContainerSpans(doc);
        }

        private static void DetectTask(BlockNode item, ListItem li)
        {
            if (item.Children.Count == 0) return;
            var first = item.Children[0];
            if (first.Type != BlockType.Paragraph || first.StartLine != item.StartLine || first.Content is null) return;
            var c = first.Content;
            if (c.Length < 4 || c[0] != '[' || c[2] != ']' || (c[3] != ' ' && c[3] != '\t')) return;
            char mark = c[1];
            if (mark == ' ') li.TaskState = TaskState.Unchecked;
            else if (mark == 'x' || mark == 'X') li.TaskState = TaskState.Checked;
            else return;
            int strip = 3;
            while (strip < c.Length && (c[strip] == ' ' || c[strip] == '\t')) strip++;
            c.Remove(0, strip);
            if (first.MapContent is { Count: > 0 })
            {
                first.MapSource![0] += strip;
                for (int i = 1; i < first.MapContent.Count; i++) first.MapContent[i] -= strip;
            }
            first.StartOffset += strip;
        }

        private static MarkdownNode? Convert(BlockNode b, InlineParser inline, MarkdownOptions options, int maxDepth)
        {
            MarkdownNode node;
            switch (b.Type)
            {
                case BlockType.BlockQuote:
                    node = new BlockQuote();
                    break;
                case BlockType.List:
                    node = new ListBlock
                    {
                        ListKind = b.ListKind,
                        BulletChar = b.ListKind == ListKind.Bullet ? b.BulletChar : '-',
                        Start = b.ListKind == ListKind.Ordered ? b.ListStart : 1,
                        Delimiter = b.ListKind == ListKind.Ordered ? b.ListDelimiter : '.',
                        IsTight = b.Tight,
                    };
                    break;
                case BlockType.Item:
                    node = new ListItem();
                    break;
                case BlockType.CodeBlock:
                    node = new CodeBlock
                    {
                        IsFenced = b.Fenced,
                        FenceChar = b.Fenced ? b.FenceChar : '`',
                        FenceLength = b.Fenced ? b.FenceLength : 3,
                        Info = b.Info,
                        Code = b.Content?.ToString() ?? string.Empty,
                    };
                    break;
                case BlockType.HtmlBlock:
                    node = new HtmlBlock { BlockType = b.HtmlType, Html = b.Content?.ToString() ?? string.Empty };
                    break;
                case BlockType.ThematicBreak:
                    node = new ThematicBreak { Marker = b.Marker };
                    break;
                case BlockType.Paragraph:
                {
                    string content = b.Content?.ToString().TrimEnd(s_trim) ?? string.Empty;
                    if (content.Length == 0 && b.Parent?.Type == BlockType.Item) return null;
                    var p = new Paragraph();
                    AddInlines(p, inline.Parse(content), b, maxDepth - b.Depth);
                    node = p;
                    break;
                }
                case BlockType.Heading:
                {
                    string content = b.Content?.ToString() ?? string.Empty;
                    int lead = 0;
                    while (lead < content.Length && Array.IndexOf(s_trim, content[lead]) >= 0) lead++;
                    content = content.Substring(lead).TrimEnd(s_trim);
                    var h = new Heading { Level = b.Level, IsSetext = b.Setext };
                    AddInlines(h, inline.Parse(content), b, maxDepth - b.Depth, lead);
                    node = h;
                    break;
                }
                case BlockType.Table:
                {
                    var t = new Table();
                    t.Alignments.AddRange(b.Alignments!);
                    node = t;
                    break;
                }
                default:
                    return null;
            }
            node.Span = new SourceSpan(b.StartOffset, Math.Max(0, b.EndOffset - b.StartOffset));
            return node;
        }

        private static void BuildTable(BlockNode table, Table t, InlineParser inline, int maxDepth)
        {
            foreach (var rowBlock in table.Children)
            {
                var row = new TableRow { IsHeader = rowBlock.IsHeaderRow };
                row.Span = new SourceSpan(rowBlock.StartOffset, Math.Max(0, rowBlock.EndOffset - rowBlock.StartOffset));
                t.AppendChildUnchecked(row);
                for (int i = 0; i < rowBlock.Cells!.Count; i++)
                {
                    var cd = rowBlock.Cells[i];
                    var cell = new TableCell { Alignment = i < t.Alignments.Count ? t.Alignments[i] : TableAlignment.None };
                    cell.Span = new SourceSpan(cd.SourceOffset, cd.Text.Length);
                    row.AppendChildUnchecked(cell);
                    var map = new BlockNode(BlockType.Paragraph, null, cd.SourceOffset, 0);
                    map.AddContentLine(cd.Text, cd.SourceOffset);
                    AddInlines(cell, inline.Parse(cd.Text), map, maxDepth - table.Depth - 2);
                }
            }
        }

        private static void FixContainerSpans(MarkdownDocument doc)
        {
            // Container ends extend to their last descendant (post-order, iterative).
            var order = new List<MarkdownNode>();
            foreach (var n in doc.Descendants())
                if (n.IsBlock && n.Children.Count > 0) order.Add(n);
            for (int i = order.Count - 1; i >= 0; i--)
            {
                var n = order[i];
                var last = n.Children[n.Children.Count - 1];
                if (last.Span.End > n.Span.End && last.Span.Length >= 0)
                    n.Span = new SourceSpan(n.Span.Start, last.Span.End - n.Span.Start);
            }
        }

        /// <summary>Converts the linked inline tree to public nodes, merging adjacent text and unwrapping nesting past the depth budget.</summary>
        private static void AddInlines(MarkdownNode target, InlineNode root, BlockNode mapBlock, int depthBudget, int subjectShift = 0)
        {
            if (depthBudget < 1) depthBudget = 1;
            var stack = new Stack<(InlineNode? next, MarkdownNode parent, int depth)>();
            stack.Push((root.First, target, 1));
            while (stack.Count > 0)
            {
                var (cur, parent, depth) = stack.Pop();
                if (cur is null) continue;
                stack.Push((cur.Next, parent, depth));

                int s = BlockParser.MapToSource(mapBlock, cur.Start + subjectShift);
                int e = BlockParser.MapToSource(mapBlock, Math.Max(cur.Start, cur.End) + subjectShift);
                var span = new SourceSpan(s, Math.Max(0, e - s));
                MarkdownNode? node = null;
                switch (cur.Type)
                {
                    case InlineType.Text:
                        if (cur.Literal.Length == 0) break;
                        node = new TextInline(cur.Literal);
                        break;
                    case InlineType.Code:
                        node = new CodeSpan(cur.Literal) { DelimiterLength = cur.DelimiterLength };
                        break;
                    case InlineType.Html:
                        node = new HtmlInline(cur.Literal);
                        break;
                    case InlineType.SoftBreak:
                        node = new SoftBreak();
                        break;
                    case InlineType.HardBreak:
                        node = new HardBreak { IsBackslash = cur.IsBackslash };
                        break;
                    case InlineType.Emphasis:
                        node = new Emphasis { Delimiter = cur.Delimiter };
                        break;
                    case InlineType.Strong:
                        node = new Strong { Delimiter = cur.Delimiter };
                        break;
                    case InlineType.Strikethrough:
                        node = new Strikethrough { DelimiterLength = cur.DelimiterLength };
                        break;
                    case InlineType.Link:
                        node = new Link { Destination = cur.Destination, Title = cur.Title, Style = cur.Style, Label = cur.Label, IsEmail = cur.IsEmail };
                        break;
                    case InlineType.Image:
                        node = new Image { Destination = cur.Destination, Title = cur.Title, Style = cur.Style, Label = cur.Label };
                        break;
                }
                if (node is null)
                {
                    continue;
                }
                bool container = cur.First is not null && node.CanHaveChildren;
                if (container && depth >= depthBudget && (cur.Type == InlineType.Emphasis || cur.Type == InlineType.Strong || cur.Type == InlineType.Strikethrough))
                {
                    // too deep: unwrap the formatting, keep the content
                    stack.Push((cur.First, parent, depth));
                    continue;
                }
                node.Span = span;
                parent.AppendChildUnchecked(node);
                if (container) stack.Push((cur.First, node, depth + 1));
            }
            MergeTextRuns(target);
        }

        /// <summary>Merges adjacent text nodes under every inline container (linear, iterative).</summary>
        private static void MergeTextRuns(MarkdownNode target)
        {
            var stack = new Stack<MarkdownNode>();
            stack.Push(target);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                var kids = n.MutableChildren;
                bool needs = false;
                for (int i = 1; i < kids.Count; i++)
                    if (kids[i] is TextInline && kids[i - 1] is TextInline) { needs = true; break; }
                if (needs)
                {
                    var merged = new List<MarkdownNode>(kids.Count);
                    StringBuilder? sb = null;
                    TextInline? run = null;
                    int runEnd = 0;
                    foreach (var k in kids)
                    {
                        if (k is TextInline t)
                        {
                            if (run is null)
                            {
                                run = t;
                                runEnd = t.Span.End;
                                sb = null;
                                merged.Add(t);
                            }
                            else
                            {
                                sb ??= new StringBuilder(run.Text);
                                sb.Append(t.Text);
                                runEnd = Math.Max(runEnd, t.Span.End);
                            }
                            continue;
                        }
                        Flush();
                        merged.Add(k);
                    }
                    Flush();
                    n.SetChildren(merged);

                    void Flush()
                    {
                        if (run is not null && sb is not null)
                        {
                            run.Text = sb.ToString();
                            run.Span = new SourceSpan(run.Span.Start, Math.Max(0, runEnd - run.Span.Start));
                        }
                        run = null;
                        sb = null;
                    }
                }
                foreach (var k in n.Children)
                    if (!k.IsBlock && k.Children.Count > 0) stack.Push(k);
            }
        }
    }
}
