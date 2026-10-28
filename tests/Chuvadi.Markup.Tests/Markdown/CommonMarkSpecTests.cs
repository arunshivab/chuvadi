using System.Collections.Generic;
using System.Linq;
using Chuvadi.Markup.Markdown;

namespace Chuvadi.Markup.Tests.Markdown
{
    /// <summary>Every example of the CommonMark 0.31.2 spec, compared byte for byte with the reference output.</summary>
    public class CommonMarkSpecTests
    {
        private static readonly List<SpecExample> s_examples = SpecFile.Load(SpecFile.ConformancePath("commonmark", "spec.txt"));

        public static IEnumerable<object[]> Examples() => s_examples.Select(e => new object[] { e });

        internal static MarkdownHtmlOptions SpecHtml() => new MarkdownHtmlOptions
        {
            RawHtml = RawHtmlMode.Passthrough,
            TagFilter = false,
            UnsafeUrls = UnsafeUrlPolicy.Keep,
        };

        [Fact]
        public void SpecHas652Examples() => Assert.Equal(652, s_examples.Count);

        [Theory]
        [MemberData(nameof(Examples))]
        public void Example(SpecExample example)
        {
            string html = MarkdownConverter.ToHtml(example.Markdown, MarkdownOptions.CommonMark(), SpecHtml());
            Assert.Equal(example.Html, html);
        }
    }
}
