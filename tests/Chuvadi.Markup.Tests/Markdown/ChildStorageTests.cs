using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chuvadi.Markup.Markdown;

namespace Chuvadi.Markup.Tests.Markdown
{
    /// <summary>
    /// D-330 and D-344: a node keeps its children in one <c>NodeChildren</c> object, which switches to chunked
    /// storage above <see cref="MarkdownNode.ChunkThreshold"/> children so no child array reaches the large object
    /// heap. These tests check that the storage changes nothing a caller can see: every operation is run on a node
    /// and on a plain <see cref="List{T}"/> side by side, and <see cref="MarkdownNode.Children"/> stays a live view.
    /// </summary>
    public class ChildStorageTests
    {
        private const int Threshold = MarkdownNode.ChunkThreshold;

        private static Paragraph[] MakeParagraphs(int count)
        {
            var result = new Paragraph[count];
            for (int i = 0; i < count; i++)
            {
                var p = new Paragraph();
                p.AppendChild(new TextInline("p" + i));
                result[i] = p;
            }
            return result;
        }

        private static bool IsChunked(MarkdownNode node) => node.Children is NodeChildren c && c.IsChunked;

        private static void AssertSame(IReadOnlyList<MarkdownNode> expected, MarkdownNode parent)
        {
            var actual = parent.Children;
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Same(expected[i], actual[i]);
                Assert.Same(parent, actual[i].Parent);
            }
            Assert.True(expected.SequenceEqual(actual));
        }

        [Fact]
        public void AppendSwitchesToChunkedStorageAfterTheThreshold()
        {
            var doc = new MarkdownDocument();
            var nodes = MakeParagraphs(Threshold + 1);
            for (int i = 0; i < Threshold; i++) doc.AppendChild(nodes[i]);
            Assert.False(IsChunked(doc));
            doc.AppendChild(nodes[Threshold]);
            Assert.True(IsChunked(doc));
            AssertSame(nodes, doc);
        }

        [Fact]
        public void ChildrenStaysALiveViewAcrossTheSwitch()
        {
            var doc = new MarkdownDocument();
            var nodes = MakeParagraphs(Threshold + 2);
            doc.AppendChild(nodes[0]);
            var view = doc.Children;
            for (int i = 1; i < nodes.Length; i++) doc.AppendChild(nodes[i]);
            Assert.True(IsChunked(doc));
            Assert.Same(view, doc.Children);
            Assert.Equal(nodes.Length, view.Count);
            Assert.Same(nodes[Threshold + 1], view[Threshold + 1]);

            doc.RemoveChild(nodes[5]);
            Assert.Equal(nodes.Length - 1, view.Count);
            Assert.Same(nodes[6], view[5]);
        }

        [Fact]
        public void ChildrenStaysALiveViewAfterMutableChildren()
        {
            var doc = new MarkdownDocument();
            var nodes = MakeParagraphs(Threshold + 10);
            foreach (var p in nodes) doc.AppendChild(p);
            var view = doc.Children;

            var list = doc.MutableChildren;
            Assert.IsType<List<MarkdownNode>>(list);
            Assert.True(nodes.SequenceEqual(list));
            Assert.False(IsChunked(doc));
            Assert.Same(view, doc.Children);

            var more = MakeParagraphs(10);
            foreach (var p in more) doc.AppendChildUnchecked(p);
            Assert.Equal(nodes.Length + more.Length, list.Count);
            Assert.Equal(nodes.Length + more.Length, view.Count);
            Assert.Same(more[9], view[view.Count - 1]);
            Assert.Same(list, doc.MutableChildren);
        }

        [Fact]
        public void ParserBuiltDocumentWithManyBlocksUsesChunkedStorage()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 20000; i++) sb.Append("Paragraph ").Append(i).Append("\n\n");
            var doc = MarkdownParser.Parse(sb.ToString());
            Assert.Equal(20000, doc.Children.Count);
            Assert.True(IsChunked(doc));
            for (int i = 0; i < 20000; i += 997)
                Assert.Equal("Paragraph " + i, doc.Children[i].ToPlainText());
        }

        [Fact]
        public void InsertAtStartMiddleAndEndMatchesList()
        {
            var doc = new MarkdownDocument();
            var expected = new List<MarkdownNode>();
            foreach (var p in MakeParagraphs(Threshold + 100)) { doc.AppendChild(p); expected.Add(p); }
            Assert.True(IsChunked(doc));

            var extra = MakeParagraphs(5);
            int[] positions = { 0, 4096, 4095, expected.Count / 2, expected.Count };
            for (int k = 0; k < extra.Length; k++)
            {
                int at = Math.Min(positions[k], expected.Count);
                doc.InsertChild(at, extra[k]);
                expected.Insert(at, extra[k]);
            }
            AssertSame(expected, doc);
        }

        [Fact]
        public void InsertAtTheThresholdSwitchesAndKeepsOrder()
        {
            var doc = new MarkdownDocument();
            var expected = new List<MarkdownNode>();
            foreach (var p in MakeParagraphs(Threshold)) { doc.AppendChild(p); expected.Add(p); }
            var middle = MakeParagraphs(1)[0];
            doc.InsertChild(10, middle);
            expected.Insert(10, middle);
            Assert.True(IsChunked(doc));
            AssertSame(expected, doc);
        }

        [Fact]
        public void RemoveMatchesList()
        {
            var doc = new MarkdownDocument();
            var expected = new List<MarkdownNode>();
            var nodes = MakeParagraphs(Threshold + 5000);
            foreach (var p in nodes) { doc.AppendChild(p); expected.Add(p); }

            foreach (int i in new[] { 0, 4096, 9000, nodes.Length - 1, 12345 })
            {
                Assert.True(doc.RemoveChild(nodes[i]));
                expected.Remove(nodes[i]);
                Assert.Null(nodes[i].Parent);
                Assert.False(doc.RemoveChild(nodes[i]));
            }
            nodes[7].Remove();
            expected.Remove(nodes[7]);
            AssertSame(expected, doc);

            var stranger = MakeParagraphs(1)[0];
            Assert.False(doc.RemoveChild(stranger));
        }

        [Fact]
        public void SmallNodesMatchList()
        {
            var doc = new MarkdownDocument();
            var expected = new List<MarkdownNode>();
            var nodes = MakeParagraphs(40);
            foreach (var p in nodes) { doc.AppendChild(p); expected.Add(p); }
            doc.InsertChild(0, MakeParagraphs(1)[0]);
            expected.Insert(0, doc.Children[0]);
            doc.InsertChild(17, MakeParagraphs(1)[0]);
            expected.Insert(17, doc.Children[17]);
            Assert.True(doc.RemoveChild(nodes[3]));
            expected.Remove(nodes[3]);
            Assert.False(IsChunked(doc));
            AssertSame(expected, doc);
        }

        [Fact]
        public void ListHandedOutBeforeTheThresholdIsNeverSwitched()
        {
            var doc = new MarkdownDocument();
            doc.AppendChild(MakeParagraphs(1)[0]);
            var list = doc.MutableChildren;
            foreach (var p in MakeParagraphs(Threshold + 10)) doc.AppendChildUnchecked(p);
            Assert.False(IsChunked(doc));
            Assert.Same(list, doc.MutableChildren);
            Assert.Equal(Threshold + 11, list.Count);
            Assert.Equal(Threshold + 11, doc.Children.Count);
        }

        [Fact]
        public void SetChildrenKeepsTheGivenList()
        {
            var doc = new MarkdownDocument();
            var given = new List<MarkdownNode>(MakeParagraphs(Threshold));
            doc.SetChildren(given);
            foreach (var p in MakeParagraphs(10)) doc.AppendChildUnchecked(p);
            Assert.False(IsChunked(doc));
            Assert.Same(given, doc.MutableChildren);
            Assert.Equal(Threshold + 10, given.Count);
            Assert.True(given.SequenceEqual(doc.Children));
        }

        [Fact]
        public void ExceptionsMatchList()
        {
            foreach (int count in new[] { 3, Threshold + 1 })
            {
                var doc = new MarkdownDocument();
                var reference = new List<MarkdownNode>();
                foreach (var p in MakeParagraphs(count)) { doc.AppendChild(p); reference.Add(p); }
                Assert.Equal(count > Threshold, IsChunked(doc));

                var listGet = Assert.Throws<ArgumentOutOfRangeException>(() => reference[reference.Count]);
                var nodeGet = Assert.Throws<ArgumentOutOfRangeException>(() => doc.Children[doc.Children.Count]);
                Assert.Equal(listGet.Message, nodeGet.Message);
                Assert.Equal(listGet.ParamName, nodeGet.ParamName);
                Assert.Throws<ArgumentOutOfRangeException>(() => doc.Children[-1]);

                var extra = MakeParagraphs(1)[0];
                var listInsert = Assert.Throws<ArgumentOutOfRangeException>(() => reference.Insert(reference.Count + 1, extra));
                var nodeInsert = Assert.Throws<ArgumentOutOfRangeException>(() => doc.InsertChild(doc.Children.Count + 1, extra));
                Assert.Equal(listInsert.Message, nodeInsert.Message);
                Assert.Equal(listInsert.ParamName, nodeInsert.ParamName);
                Assert.Null(extra.Parent);

                var listEnum = Assert.Throws<InvalidOperationException>(() => { foreach (var c in reference) reference.Remove(c); });
                var nodeEnum = Assert.Throws<InvalidOperationException>(() => { foreach (var c in doc.Children) doc.RemoveChild(c); });
                Assert.Equal(listEnum.Message, nodeEnum.Message);
            }
        }

        [Fact]
        public void InsertOutOfRangeAtTheThresholdLeavesTheNodeUnchanged()
        {
            var doc = new MarkdownDocument();
            var nodes = MakeParagraphs(Threshold);
            foreach (var p in nodes) doc.AppendChild(p);
            var extra = MakeParagraphs(1)[0];
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.InsertChild(Threshold + 1, extra));
            Assert.False(IsChunked(doc));
            AssertSame(nodes, doc);
        }

        [Fact]
        public void DescendantsAndPlainTextWalkAChunkedNode()
        {
            var doc = new MarkdownDocument();
            var nodes = MakeParagraphs(Threshold + 3000);
            foreach (var p in nodes) doc.AppendChild(p);
            Assert.Equal(nodes.Length * 2, doc.Descendants().Count());
            Assert.Equal(string.Concat(Enumerable.Range(0, nodes.Length).Select(i => "p" + i)), doc.ToPlainText());
        }

        [Fact]
        public void TwentyThousandBlocksRenderIdenticallyToPlainListStorage()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 20000; i++)
            {
                switch (i % 5)
                {
                    case 0: sb.Append("# Heading ").Append(i).Append("\n\n"); break;
                    case 1: sb.Append("Some *text* with `code` and [a link](http://x/").Append(i).Append(").\n\n"); break;
                    case 2: sb.Append("- item ").Append(i).Append("\n\n"); break;
                    case 3: sb.Append("> quote ").Append(i).Append("\n\n"); break;
                    default: sb.Append("    code ").Append(i).Append("\n\n"); break;
                }
            }
            string source = sb.ToString();

            var chunked = MarkdownParser.Parse(source);
            Assert.True(IsChunked(chunked));
            string html = MarkdownHtmlRenderer.Render(chunked);
            string markdown = MarkdownWriter.Write(chunked);

            // The same tree with plain List storage: MutableChildren moves the children into a List.
            var plain = MarkdownParser.Parse(source);
            Assert.IsType<List<MarkdownNode>>(plain.MutableChildren);
            Assert.False(IsChunked(plain));
            Assert.Equal(html, MarkdownHtmlRenderer.Render(plain));
            Assert.Equal(markdown, MarkdownWriter.Write(plain));

            // And the same as rendering the blocks in pieces below the threshold.
            var expected = new StringBuilder();
            const int piece = 4000;
            var all = new List<MarkdownNode>(plain.MutableChildren);
            for (int start = 0; start < all.Count; start += piece)
            {
                var part = new MarkdownDocument();
                part.SetChildren(all.GetRange(start, Math.Min(piece, all.Count - start)));
                expected.Append(MarkdownHtmlRenderer.Render(part));
            }
            Assert.Equal(expected.ToString(), html);
        }
    }
}
