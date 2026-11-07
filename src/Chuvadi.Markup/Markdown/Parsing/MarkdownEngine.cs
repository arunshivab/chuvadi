using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>
    /// Entry point used by <see cref="MarkdownParser"/>: builds the block structure of the document, then
    /// parses the inline content of its paragraphs and headings.
    /// </summary>
    internal static class MarkdownEngine
    {
        // TRACE R-MD-0101
        internal static void Parse(string markdown, MarkdownOptions options, MarkdownDocument document, CancellationToken cancellationToken)
        {
            var blocks = new BlockParser(markdown, options, document, cancellationToken);
            blocks.Run();

            var poll = new CancellationPoll(cancellationToken);
            var work = new WorkBudget(options.Limits, markdown.Length);
            foreach (var (block, content) in blocks.InlineBlocks)
            {
                poll.Check();
                var parser = new InlineParser(content, document, work, options.Limits.MaxNestingDepth, options.Strikethrough, options.Autolinks);
                parser.ParseInto(block);
                InlineTree.FlattenDeepEmphasis(block, options.Limits.MaxNestingDepth);
                InlineTree.MergeAdjacentText(block);
                if (options.Autolinks) EmailAutolinks.Apply(block, work);
                InlineTree.CountNodes(block, blocks.Nodes, poll);
            }
        }
    }

    /// <summary>Tidying and counting of the inline nodes of one block.</summary>
    internal static class InlineTree
    {
        // TRACE R-MD-0159
        /// <summary>Joins neighbouring text nodes into one, with a span from the first to the last.</summary>
        public static void MergeAdjacentText(MarkdownBlock block)
        {
            var stack = new System.Collections.Generic.Stack<MarkdownNode>();
            stack.Push(block);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var kids = node.MutableChildren;
                int w = 0;
                int r = 0;
                while (r < kids.Count)
                {
                    var child = kids[r];
                    if (child is TextInline first && r + 1 < kids.Count && kids[r + 1] is TextInline)
                    {
                        var sb = new System.Text.StringBuilder(first.Text);
                        int start = first.Span.Start;
                        int end = first.Span.End;
                        int k = r + 1;
                        while (k < kids.Count && kids[k] is TextInline more)
                        {
                            sb.Append(more.Text);
                            if (more.Span.End > end) end = more.Span.End;
                            k++;
                        }
                        first.Text = sb.ToString();
                        first.Span = new SourceSpan(start, end - start);
                        kids[w++] = first;
                        r = k;
                        continue;
                    }
                    kids[w++] = child;
                    if (child.CanHaveChildren) stack.Push(child);
                    r++;
                }
                if (w < kids.Count) kids.RemoveRange(w, kids.Count - w);
            }
        }

        // TRACE R-MD-0161
        /// <summary>
        /// Emphasis and strong emphasis are kept only while their depth (the document being 0) stays below
        /// MaxNestingDepth; deeper ones are replaced by their content, so the text stays and the formatting goes.
        /// </summary>
        public static void FlattenDeepEmphasis(MarkdownBlock block, int maxDepth)
        {
            int blockDepth = 0;
            for (MarkdownNode? p = block; p is not null && p is not MarkdownDocument; p = p.Parent) blockDepth++;
            var stack = new System.Collections.Generic.Stack<(MarkdownNode Node, int Depth)>();
            stack.Push((block, blockDepth));
            while (stack.Count > 0)
            {
                var (node, depth) = stack.Pop();
                int childDepth = depth + 1;
                var kids = node.MutableChildren;
                if (childDepth >= maxDepth && HasEmphasis(kids))
                {
                    var flat = new System.Collections.Generic.List<MarkdownNode>(kids.Count);
                    var pending = new System.Collections.Generic.Stack<MarkdownNode>();
                    for (int k = kids.Count - 1; k >= 0; k--) pending.Push(kids[k]);
                    while (pending.Count > 0)
                    {
                        var c = pending.Pop();
                        // TRACE R-MD-0239
                        if (c is Emphasis || c is Strong || c is Strikethrough)
                        {
                            var inner = c.MutableChildren;
                            for (int k = inner.Count - 1; k >= 0; k--) pending.Push(inner[k]);
                            inner.Clear();
                        }
                        else flat.Add(c);
                    }
                    node.SetChildren(flat);
                    kids = flat;
                }
                foreach (var child in kids)
                    if (child.CanHaveChildren) stack.Push((child, childDepth));
            }
        }

        private static bool HasEmphasis(System.Collections.Generic.List<MarkdownNode> kids)
        {
            foreach (var k in kids)
                if (k is Emphasis || k is Strong || k is Strikethrough) return true;
            return false;
        }

        // TRACE R-MD-0160
        /// <summary>Counts the inline nodes of a block against MaxNodes.</summary>
        public static void CountNodes(MarkdownBlock block, NodeBudget budget, CancellationPoll poll)
        {
            var stack = new System.Collections.Generic.Stack<MarkdownNode>();
            foreach (var child in block.Children) stack.Push(child);
            while (stack.Count > 0)
            {
                poll.Tick();
                var node = stack.Pop();
                budget.Take();
                foreach (var child in node.Children) stack.Push(child);
            }
        }
    }
}
