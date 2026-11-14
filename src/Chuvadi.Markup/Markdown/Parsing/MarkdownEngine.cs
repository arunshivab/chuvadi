using System.Collections.Generic;
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
        // TRACE R-MD-0275
        // safety net: after a parse every span (nodes and link reference definitions) lies inside the input; a span
        // that would not is clamped to it and counted here. It never fires when the span rules are right (D-309).
        [System.ThreadStatic] internal static int SpanClampsOnThisThread;

        /// <summary>The span clamped to an input of <paramref name="length"/> chars; counts each clamp.</summary>
        internal static SourceSpan ClampSpan(SourceSpan span, int length)
        {
            if (span.End <= length) return span;
            SpanClampsOnThisThread++;
            int start = span.Start < length ? span.Start : length;
            return new SourceSpan(start, length - start);
        }

        private static void ClampTree(MarkdownNode root, int length, ref CancellationPoll poll)
        {
            if (root.Span.End > length) root.Span = ClampSpan(root.Span, length);
            // one frame per level (node and next child index), so the stack grows with depth, not with width
            var stack = new System.Collections.Generic.Stack<(MarkdownNode Node, int Next)>();
            if (root.CanHaveChildren) stack.Push((root, 0));
            while (stack.Count > 0)
            {
                poll.Tick();
                var (node, next) = stack.Pop();
                // TRACE R-MD-0280
                var kids = node.Children;
                if (next >= kids.Count) continue;
                stack.Push((node, next + 1));
                var child = kids[next];
                if (child.Span.End > length) child.Span = ClampSpan(child.Span, length);
                if (child.CanHaveChildren) stack.Push((child, 0));
            }
        }

        // TRACE R-MD-0101
        internal static void Parse(string markdown, MarkdownOptions options, MarkdownDocument document, CancellationToken cancellationToken)
        {
            var blocks = new BlockParser(markdown, options, document, cancellationToken);
            blocks.Run();

            var poll = new CancellationPoll(cancellationToken);
            var work = new WorkBudget(options.Limits, markdown.Length, cancellationToken);
            var list = blocks.InlineBlocks;
            for (int ib = 0; ib < list.Count; ib++)
            {
                // TRACE R-MD-0279
                // each entry is released once used, so its content dies with its block's inline parse
                var (block, content) = list[ib];
                list[ib] = default;
                poll.Check();
                var parser = new InlineParser(content, document, work, options.Limits.MaxNestingDepth, options.Strikethrough, options.Autolinks);
                parser.ParseInto(block);
                InlineTree.FlattenDeepEmphasis(block, options.Limits.MaxNestingDepth);
                InlineTree.MergeAdjacentText(block);
                if (options.Autolinks) EmailAutolinks.Apply(block, work);
                InlineTree.CountNodes(block, blocks.Nodes, poll);
            }
            list.Clear();
            ClampTree(document, markdown.Length, ref poll);
        }
    }

    // TRACE R-MD-0279
    /// <summary>
    /// An append-only list kept in chunks of 1,024 entries, so that no array of it is large enough for the large
    /// object heap. A large array is collected only with generation 2, and while it waits it keeps whatever it points
    /// to alive (issue #94, D-303); small chunks die young with the parse. Entries are released after use.
    /// </summary>
    internal sealed class ChunkedList<T>
    {
        private const int ChunkSize = 1024;
        private readonly System.Collections.Generic.List<T[]> _chunks = new();
        private int _count;

        public int Count => _count;

        // TRACE R-MD-0281
        // the first chunk starts at 4 entries and doubles up to ChunkSize, so a small document allocates a buffer
        // that grows with what it holds (D-368); later chunks are ChunkSize, below the large-object size
        public void Add(T item)
        {
            if (_count < ChunkSize)
            {
                if (_chunks.Count == 0) _chunks.Add(new T[4]);
                var first = _chunks[0];
                if (_count == first.Length)
                {
                    System.Array.Resize(ref first, first.Length * 2);
                    _chunks[0] = first;
                }
            }
            else if ((_count & (ChunkSize - 1)) == 0) _chunks.Add(new T[ChunkSize]);
            _chunks[_count >> 10][_count & (ChunkSize - 1)] = item;
            _count++;
        }

        public T this[int index]
        {
            get => _chunks[index >> 10][index & (ChunkSize - 1)];
            set => _chunks[index >> 10][index & (ChunkSize - 1)] = value;
        }

        public void Clear()
        {
            _chunks.Clear();
            _count = 0;
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
                // TRACE R-MD-0280
                // read through Children; take the mutable list only when two text nodes are neighbours
                var view = node.Children;
                bool merge = false;
                for (int q = 0; q + 1 < view.Count; q++)
                    if (view[q] is TextInline && view[q + 1] is TextInline) { merge = true; break; }
                if (!merge)
                {
                    for (int q = 0; q < view.Count; q++)
                        if (view[q].CanHaveChildren) stack.Push(view[q]);
                    continue;
                }
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
                // TRACE R-MD-0280
                IReadOnlyList<MarkdownNode> kids = node.Children;
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
                            var inner = c.Children;
                            for (int k = inner.Count - 1; k >= 0; k--) pending.Push(inner[k]);
                            c.MutableChildren.Clear();
                        }
                        else flat.Add(c);
                    }
                    node.SetChildren(flat);
                    kids = flat;
                }
                for (int q = 0; q < kids.Count; q++)
                    if (kids[q].CanHaveChildren) stack.Push((kids[q], childDepth));
            }
        }

        private static bool HasEmphasis(IReadOnlyList<MarkdownNode> kids)
        {
            for (int q = 0; q < kids.Count; q++)
                if (kids[q] is Emphasis || kids[q] is Strong || kids[q] is Strikethrough) return true;
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
