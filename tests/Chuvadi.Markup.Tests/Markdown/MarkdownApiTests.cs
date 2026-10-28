using System;
using System.IO;
using System.Linq;
using System.Threading;
using Chuvadi.Markup.Markdown;

namespace Chuvadi.Markup.Tests.Markdown
{
    public class MarkdownApiTests
    {
        [Fact]
        public void AstShapeAndSpans()
        {
            const string src = "# Title\n\nSome *em* and [link](http://x \"t\").\n\n- [x] done\n- [ ] todo\n";
            var doc = MarkdownParser.Parse(src);
            var h = Assert.IsType<Heading>(doc.Children[0]);
            Assert.Equal(1, h.Level);
            Assert.Equal("Title", h.ToPlainText());
            Assert.Equal(0, h.Span.Start);
            Assert.Equal(new SourcePosition(1, 1), doc.GetPosition(h.Span.Start));

            var p = Assert.IsType<Paragraph>(doc.Children[1]);
            var em = p.Children.OfType<Emphasis>().Single();
            Assert.Equal("*em*", src.Substring(em.Span.Start, em.Span.Length));
            var link = p.Children.OfType<Link>().Single();
            Assert.Equal("http://x", link.Destination);
            Assert.Equal("t", link.Title);
            Assert.Equal(LinkStyle.Inline, link.Style);
            Assert.Equal("[link](http://x \"t\")", src.Substring(link.Span.Start, link.Span.Length));

            var list = Assert.IsType<ListBlock>(doc.Children[2]);
            Assert.True(list.IsTight);
            Assert.Equal(TaskState.Checked, ((ListItem)list.Children[0]).TaskState);
            Assert.Equal(TaskState.Unchecked, ((ListItem)list.Children[1]).TaskState);
            Assert.Equal("done", list.Children[0].ToPlainText());
            Assert.Equal(new SourcePosition(5, 1), doc.GetPosition(list.Span.Start));
        }

        [Fact]
        public void ReferenceDefinitionsAreExposed()
        {
            var doc = MarkdownParser.Parse("[Foo Bar]: /url 'title'\n\n[foo bar]\n");
            var def = doc.LinkDefinitions["foo bar"];
            Assert.Equal("/url", def.Destination);
            Assert.Equal("title", def.Title);
            var link = doc.Descendants().OfType<Link>().Single();
            Assert.Equal(LinkStyle.ReferenceShortcut, link.Style);
            Assert.Equal("foo bar", link.Label);
        }

        [Fact]
        public void ExtensionsCanBeDisabled()
        {
            const string src = "| a |\n| - |\n| b |\n\n~~x~~ www.example.com\n\n- [ ] t\n";
            string gfm = MarkdownConverter.ToHtml(src);
            Assert.Contains("<table>", gfm);
            Assert.Contains("<del>x</del>", gfm);
            Assert.Contains("href=\"http://www.example.com\"", gfm);
            Assert.Contains("type=\"checkbox\"", gfm);
            string cm = MarkdownConverter.ToHtml(src, MarkdownOptions.CommonMark());
            Assert.DoesNotContain("<table>", cm);
            Assert.DoesNotContain("<del>", cm);
            Assert.DoesNotContain("<a ", cm);
            Assert.DoesNotContain("checkbox", cm);
        }

        [Fact]
        public void RawHtmlModes()
        {
            const string src = "<div onclick=\"x()\">hi</div>\n\ntext <b>bold</b>\n";
            string esc = MarkdownConverter.ToHtml(src);
            Assert.DoesNotContain("<div", esc);
            Assert.Contains("&lt;div onclick=&quot;x()&quot;&gt;hi&lt;/div&gt;", esc);
            Assert.Contains("text &lt;b&gt;bold&lt;/b&gt;", esc);
            string strip = MarkdownConverter.ToHtml(src, null, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Strip });
            Assert.Equal("<p>text bold</p>\n", strip);
            string pass = MarkdownConverter.ToHtml(src, null, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough });
            Assert.Contains("<div onclick=\"x()\">hi</div>", pass);
        }

        [Fact]
        public void TagFilterNeutralisesScriptInPassthrough()
        {
            string html = MarkdownConverter.ToHtml("<script>alert(1)</script>\n", null, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough });
            Assert.Equal("&lt;script>alert(1)&lt;/script>\n", html);
        }

        [Theory]
        [InlineData("[a](javascript:alert(1))", "<p><a href=\"\">a</a></p>\n")]
        [InlineData("[a](JaVaScRiPt:alert(1))", "<p><a href=\"\">a</a></p>\n")]
        [InlineData("[a](vbscript:x)", "<p><a href=\"\">a</a></p>\n")]
        [InlineData("[a](file:///etc/passwd)", "<p><a href=\"\">a</a></p>\n")]
        [InlineData("[a](data:text/html,x)", "<p><a href=\"\">a</a></p>\n")]
        [InlineData("![a](data:image/png;base64,AAAA)", "<p><img src=\"data:image/png;base64,AAAA\" alt=\"a\" /></p>\n")]
        [InlineData("[a](https://ok.example/?q=1&r=2)", "<p><a href=\"https://ok.example/?q=1&amp;r=2\">a</a></p>\n")]
        public void UnsafeUrlsRemovedByDefault(string md, string expected) => Assert.Equal(expected, MarkdownConverter.ToHtml(md));

        [Fact]
        public void RendererOptions()
        {
            var doc = MarkdownParser.Parse("# Hello World\n\n## Hello World\n\nline one\nline two\n\n- [x] a\n\n| a | b |\n|:--|--:|\n| 1 | 2 |\n\n---\n\n```cs\nx\n```\n");
            var html = MarkdownHtmlRenderer.Render(doc, new MarkdownHtmlOptions
            {
                HeadingIds = true,
                HeadingIdPrefix = "md-",
                SoftBreak = SoftBreakStyle.LineBreak,
                TaskLists = TaskListStyle.Glyph,
                TableAlignment = TableAlignmentStyle.StyleAttribute,
                XhtmlVoidElements = false,
                CodeLanguageClassPrefix = "lang-",
                SourcePositions = true,
            });
            Assert.Contains("<h1 id=\"md-hello-world\" data-sourcepos=\"1:1-1:13\">", html);
            Assert.Contains("<h2 id=\"md-hello-world-1\"", html);
            Assert.Contains("line one<br>\nline two", html);
            Assert.Contains("☑", html);
            Assert.Contains("style=\"text-align: left\"", html);
            Assert.Contains("<hr data-sourcepos", html);
            Assert.DoesNotContain(" />", html);
            Assert.Contains("class=\"lang-cs\"", html);
        }

        [Fact]
        public void InputLimitThrows()
        {
            var o = new MarkdownOptions();
            o.Limits.MaxInputChars = 10;
            var ex = Assert.Throws<MarkupLimitExceededException>(() => MarkdownParser.Parse(new string('a', 11), o));
            Assert.Equal(nameof(MarkupLimits.MaxInputChars), ex.LimitName);
            Assert.Equal(10, ex.Limit);
        }

        [Fact]
        public void NodeLimitThrows()
        {
            var o = new MarkdownOptions();
            o.Limits.MaxNodes = 100;
            var ex = Assert.Throws<MarkupLimitExceededException>(() => MarkdownParser.Parse(string.Concat(Enumerable.Repeat("*a* ", 100)), o));
            Assert.Equal(nameof(MarkupLimits.MaxNodes), ex.LimitName);
        }

        [Fact]
        public void OutputLimitThrows()
        {
            var doc = MarkdownParser.Parse(new string('a', 1000));
            Assert.Throws<MarkupLimitExceededException>(() =>
                MarkdownHtmlRenderer.Render(doc, new MarkdownHtmlOptions { Limits = new MarkupLimits { MaxOutputChars = 100 } }));
        }

        [Fact]
        public void TextReaderLimitIsEnforcedWhileReading()
        {
            var o = new MarkdownOptions();
            o.Limits.MaxInputChars = 1000;
            Assert.Throws<MarkupLimitExceededException>(() => MarkdownParser.Parse(new StringReader(new string('x', 100_000)), o));
        }

        [Fact]
        public void CancellationIsHonoured()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => MarkdownParser.Parse("x", null, cts.Token));
            string big = string.Concat(Enumerable.Repeat("*a **b** c* ", 200_000));
            using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));
            SpinWait.SpinUntil(() => cts2.IsCancellationRequested, 10_000);
            Assert.ThrowsAny<OperationCanceledException>(() => MarkdownParser.Parse(big, null, cts2.Token));
        }

        [Fact]
        public void TreeEditing()
        {
            var doc = MarkdownParser.Parse("a\n\nb\n");
            var second = doc.Children[1];
            second.Remove();
            Assert.Single(doc.Children);
            Assert.Null(second.Parent);
            var q = new BlockQuote();
            q.AppendChild(second);
            doc.AppendChild(q);
            Assert.Equal("<p>a</p>\n<blockquote>\n<p>b</p>\n</blockquote>\n", MarkdownHtmlRenderer.Render(doc));
            Assert.Throws<InvalidOperationException>(() => doc.AppendChild(second));
            Assert.Throws<InvalidOperationException>(() => new TextInline("x").AppendChild(new TextInline("y")));
        }

        [Fact]
        public void VisitorVisitsEverything()
        {
            var doc = MarkdownParser.Parse("# h\n\n*a* `b` [c](d)\n");
            var v = new CountingVisitor();
            doc.Accept(v);
            Assert.Equal(doc.Descendants().Count() + 1, v.Count);
        }

        private sealed class CountingVisitor : MarkdownVisitor
        {
            public int Count;
            public override void VisitDocument(MarkdownDocument node) { Count++; base.VisitDocument(node); }
            public override void VisitHeading(Heading node) { Count++; base.VisitHeading(node); }
            public override void VisitParagraph(Paragraph node) { Count++; base.VisitParagraph(node); }
            public override void VisitText(TextInline node) => Count++;
            public override void VisitEmphasis(Emphasis node) { Count++; base.VisitEmphasis(node); }
            public override void VisitCodeSpan(CodeSpan node) => Count++;
            public override void VisitLink(Link node) { Count++; base.VisitLink(node); }
            public override void VisitSoftBreak(SoftBreak node) => Count++;
        }
    }
}
