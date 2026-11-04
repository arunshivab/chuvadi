using System;
using System.IO;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Markdown;
using Chuvadi.Markup.Sanitization;
using Chuvadi.Markup.Text;

namespace Chuvadi.Markup.Tests
{
    /// <summary>
    /// MaxOutputChars is exact (D-127 R5-03, option 1 for MarkdownWriter): output stops at exactly the limit,
    /// a TextWriter has received exactly that many chars, and Actual is the limit plus one.
    /// </summary>
    public class OutputLimitTests
    {
        private const string Markdown = "# Title\n\nSome *text* with [a link](http://example.com \"t\") and `code`.\n\n- one\n- two\n\n> quote\n\n```cs\nfenced\n```\n";
        private const string Html = "<p>Some <b>bold</b> text with <a href=\"http://example.com\">a link</a> &amp; more.</p><ul><li>one</li><li>two</li></ul>";

        private static void Exact(MarkupLimitExceededException ex, long limit)
        {
            Assert.Equal(nameof(MarkupLimits.MaxOutputChars), ex.LimitName);
            Assert.Equal(limit, ex.Limit);
            Assert.Equal(limit + 1, ex.Actual);
            Assert.Equal("Markup limit 'MaxOutputChars' exceeded: limit " + limit + ", reached " + (limit + 1) + ".", ex.Message);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(40)]
        [InlineData(99)]
        public void MarkdownRenderer_StringAndWriter(int limit)
        {
            var doc = MarkdownParser.Parse(Markdown);
            string full = MarkdownHtmlRenderer.Render(doc);
            Assert.True(full.Length > limit);
            var opt = new MarkdownHtmlOptions { Limits = new MarkupLimits { MaxOutputChars = limit } };
            Exact(Assert.Throws<MarkupLimitExceededException>(() => MarkdownHtmlRenderer.Render(doc, opt)), limit);
            var sw = new StringWriter();
            Exact(Assert.Throws<MarkupLimitExceededException>(() => MarkdownHtmlRenderer.Render(doc, sw, opt)), limit);
            Assert.Equal(full.Substring(0, limit), sw.ToString());
        }

        [Fact]
        public void MarkdownRenderer_ExactlyAtTheLimitSucceeds()
        {
            var doc = MarkdownParser.Parse(Markdown);
            string full = MarkdownHtmlRenderer.Render(doc);
            var opt = new MarkdownHtmlOptions { Limits = new MarkupLimits { MaxOutputChars = full.Length } };
            Assert.Equal(full, MarkdownHtmlRenderer.Render(doc, opt));
            var sw = new StringWriter();
            MarkdownHtmlRenderer.Render(doc, sw, opt);
            Assert.Equal(full, sw.ToString());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(13)]
        [InlineData(60)]
        public void MarkdownWriter_StringAndWriter(int limit)
        {
            var doc = MarkdownParser.Parse(Markdown);
            string full = MarkdownWriter.Write(doc);
            Assert.True(full.Length > limit);
            var opt = new MarkdownWriterOptions { Limits = new MarkupLimits { MaxOutputChars = limit } };
            Exact(Assert.Throws<MarkupLimitExceededException>(() => MarkdownWriter.Write(doc, opt)), limit);
            var sw = new StringWriter();
            Exact(Assert.Throws<MarkupLimitExceededException>(() => MarkdownWriter.Write(doc, sw, opt)), limit);
            Assert.Equal(full.Substring(0, limit), sw.ToString());
        }

        [Fact]
        public void MarkdownWriter_ExactlyAtTheLimitSucceeds()
        {
            var doc = MarkdownParser.Parse(Markdown);
            string full = MarkdownWriter.Write(doc);
            var opt = new MarkdownWriterOptions { Limits = new MarkupLimits { MaxOutputChars = full.Length } };
            Assert.Equal(full, MarkdownWriter.Write(doc, opt));
            var sw = new StringWriter();
            MarkdownWriter.Write(doc, sw, opt);
            Assert.Equal(full, sw.ToString());
        }

        [Fact]
        public void MarkdownWriter_ArgumentChecksUnchanged()
        {
            var doc = MarkdownParser.Parse("a");
            Assert.Equal("writer", Assert.Throws<ArgumentNullException>(() => MarkdownWriter.Write(null!, (TextWriter)null!)).ParamName);
            Assert.Equal("node", Assert.Throws<ArgumentNullException>(() => MarkdownWriter.Write(null!, new StringWriter())).ParamName);
            Assert.Equal("node", Assert.Throws<ArgumentNullException>(() => MarkdownWriter.Write(null!)).ParamName);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(57)]
        public void HtmlSerializer_StringAndWriter(int limit)
        {
            var doc = HtmlParser.Parse(Html);
            string full = HtmlSerializer.Serialize(doc);
            Assert.True(full.Length > limit);
            var opt = new HtmlSerializerOptions { Limits = new MarkupLimits { MaxOutputChars = limit } };
            Exact(Assert.Throws<MarkupLimitExceededException>(() => HtmlSerializer.Serialize(doc, opt)), limit);
            Exact(Assert.Throws<MarkupLimitExceededException>(() => HtmlSerializer.SerializeChildren(doc, opt)), limit);
            var sw = new StringWriter();
            Exact(Assert.Throws<MarkupLimitExceededException>(() => HtmlSerializer.Serialize(doc, sw, opt)), limit);
            Assert.Equal(full.Substring(0, limit), sw.ToString());
        }

        [Fact]
        public void Sanitizer_ThrowsExactly()
        {
            var policy = new SanitizerPolicy();
            policy.Limits.MaxOutputChars = 20;
            Exact(Assert.Throws<MarkupLimitExceededException>(() => HtmlSanitizer.Sanitize(Html, policy)), 20);
        }

        [Fact]
        public void PlainText_ThrowsExactly()
        {
            var opt = new TextToHtmlOptions { Limits = new MarkupLimits { MaxOutputChars = 30 } };
            Exact(Assert.Throws<MarkupLimitExceededException>(() => PlainText.ToHtml("line one\nline two http://example.com\n", opt)), 30);
        }

        [Fact]
        public void BelowTheLimitOutputIsUnchanged()
        {
            var doc = MarkdownParser.Parse(Markdown);
            Assert.Equal(MarkdownHtmlRenderer.Render(doc), MarkdownHtmlRenderer.Render(doc, new MarkdownHtmlOptions { Limits = new MarkupLimits { MaxOutputChars = 100_000 } }));
            Assert.Equal(MarkdownWriter.Write(doc), MarkdownWriter.Write(doc, new MarkdownWriterOptions { Limits = new MarkupLimits { MaxOutputChars = 100_000 } }));
        }
    }
}
