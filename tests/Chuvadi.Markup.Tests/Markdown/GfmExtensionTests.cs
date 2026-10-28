using System.Collections.Generic;
using System.Linq;
using Chuvadi.Markup.Markdown;

namespace Chuvadi.Markup.Tests.Markdown
{
    /// <summary>
    /// cmark-gfm's extension tests (tables, strikethrough, autolinks, tag filter, task lists) and the
    /// extension-tagged examples of the GFM spec. Footnotes are out of scope and skipped.
    /// </summary>
    public class GfmExtensionTests
    {
        private static readonly List<SpecExample> s_ext = SpecFile.Load(SpecFile.ConformancePath("gfm", "extensions.txt"));
        private static readonly List<SpecExample> s_gfm = SpecFile.Load(SpecFile.ConformancePath("gfm", "gfm-spec.txt"));

        private static bool InScope(SpecExample e) =>
            e.Html != "<IGNORE>\n" && !e.Section.Contains("ootnote", System.StringComparison.Ordinal)
            && !e.Markdown.Contains("[^", System.StringComparison.Ordinal);

        public static IEnumerable<object[]> ExtensionExamples() => s_ext.Where(InScope).Select(e => new object[] { e });

        public static IEnumerable<object[]> GfmTaggedExamples() =>
            s_gfm.Where(e => e.Tag is "table" or "strikethrough" or "autolink" or "tagfilter").Select(e => new object[] { e });

        private static MarkdownHtmlOptions Html() => new MarkdownHtmlOptions
        {
            RawHtml = RawHtmlMode.Passthrough,
            TagFilter = true,
            UnsafeUrls = UnsafeUrlPolicy.Keep,
        };

        [Theory]
        [MemberData(nameof(ExtensionExamples))]
        public void Extension(SpecExample example)
        {
            Assert.Equal(example.Html, MarkdownConverter.ToHtml(example.Markdown, MarkdownOptions.Gfm(), Html()));
        }

        [Theory]
        [MemberData(nameof(GfmTaggedExamples))]
        public void GfmSpec(SpecExample example)
        {
            Assert.Equal(example.Html, MarkdownConverter.ToHtml(example.Markdown, MarkdownOptions.Gfm(), Html()));
        }
    }
}
