using System.Collections.Generic;
using System.Linq;
using Chuvadi.Markup.Markdown;

namespace Chuvadi.Markup.Tests.Markdown
{
    /// <summary>parse → MarkdownWriter → parse renders identical HTML for every spec example.</summary>
    public class RoundTripTests
    {
        public static IEnumerable<object[]> CommonMark() =>
            SpecFile.Load(SpecFile.ConformancePath("commonmark", "spec.txt")).Select(e => new object[] { e });

        public static IEnumerable<object[]> Gfm() =>
            SpecFile.Load(SpecFile.ConformancePath("gfm", "gfm-spec.txt"))
                .Concat(SpecFile.Load(SpecFile.ConformancePath("gfm", "extensions.txt")))
                .Where(e => e.Html != "<IGNORE>\n" && !e.Markdown.Contains("[^"))
                .Select(e => new object[] { e });

        private static readonly MarkdownHtmlOptions s_html = new MarkdownHtmlOptions
        {
            RawHtml = RawHtmlMode.Passthrough,
            TagFilter = false,
            UnsafeUrls = UnsafeUrlPolicy.Keep,
        };

        private static void Check(SpecExample e, MarkdownOptions po, MarkdownWriterOptions? wo = null)
        {
            var d1 = MarkdownParser.Parse(e.Markdown, po);
            string h1 = MarkdownHtmlRenderer.Render(d1, s_html);
            string md2 = MarkdownWriter.Write(d1, wo);
            string h2 = MarkdownHtmlRenderer.Render(MarkdownParser.Parse(md2, po), s_html);
            Assert.Equal(h1, h2);
        }

        [Theory]
        [MemberData(nameof(CommonMark))]
        public void CommonMarkExample(SpecExample e) => Check(e, MarkdownOptions.CommonMark());

        [Theory]
        [MemberData(nameof(CommonMark))]
        public void CommonMarkExample_NormalizedStyle(SpecExample e) =>
            Check(e, MarkdownOptions.CommonMark(), new MarkdownWriterOptions { PreserveSourceStyle = false, BackslashHardBreaks = false, SetextHeadings = true });

        [Theory]
        [MemberData(nameof(Gfm))]
        public void GfmExample(SpecExample e) => Check(e, MarkdownOptions.Gfm());

        [Fact]
        public void ReferenceLinksArePreservedWhenAsked()
        {
            var doc = MarkdownParser.Parse("See [the site][s] and [s].\n\n[s]: https://example.com \"Site\"\n");
            string md = MarkdownWriter.Write(doc, new MarkdownWriterOptions { PreserveReferenceLinks = true });
            Assert.Contains("[the site][s]", md);
            Assert.Contains("[s]: <https://example.com> \"Site\"", md);
            Assert.Equal(MarkdownHtmlRenderer.Render(doc), MarkdownConverter.ToHtml(md));
        }

        [Fact]
        public void BuiltTreeIsEscaped()
        {
            var doc = new MarkdownDocument();
            var p = new Paragraph();
            p.AppendChild(new TextInline("1. *not* a list # or [link] <b>"));
            doc.AppendChild(p);
            var h = new Heading { Level = 2 };
            h.AppendChild(new TextInline("Title ends with #"));
            doc.AppendChild(h);
            string md = MarkdownWriter.Write(doc);
            var back = MarkdownParser.Parse(md);
            Assert.IsType<Paragraph>(back.Children[0]);
            Assert.Equal("1. *not* a list # or [link] <b>", back.Children[0].ToPlainText());
            Assert.Equal("Title ends with #", back.Children[1].ToPlainText());
        }
    }
}
