using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Markup.Tests.Html
{
    /// <summary>
    /// Adversarial inputs: each must parse (and sanitize) within a time budget without stack
    /// overflow or excessive allocation. Budgets are generous for slow CI machines.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class HtmlPathologicalTests
    {
        private const double BudgetSeconds = 5;

        public static TheoryData<string> Cases()
        {
            var d = new TheoryData<string>();
            foreach (var name in new[]
            {
                "deep-divs", "deep-inline", "deep-tables", "deep-svg", "aaa-trap", "aaa-many-formatting",
                "unclosed-formatting", "many-attributes", "duplicate-attributes", "entity-flood", "ampersand-flood",
                "long-comment", "unclosed-comment", "long-attribute", "huge-table", "nested-lists", "foster-flood",
                "template-nesting", "select-flood", "noahs-ark", "cdata-flood", "mixed-ltlt", "deep-blockquote-styles",
                "css-deep", "css-many-rules",
            }) d.Add(name);
            return d;
        }

        internal static string Build(string name)
        {
            const int n = 50_000;
            var sb = new StringBuilder();
            switch (name)
            {
                case "deep-divs": return Rep("<div>", n) + "x" + Rep("</div>", n);
                case "deep-inline": return Rep("<span>", n) + "x";
                case "deep-tables": return Rep("<table><tr><td>", 5_000) + "x";
                case "deep-svg": return "<svg>" + Rep("<g>", n) + "x";
                case "aaa-trap": return Rep("<b><div>", 10_000) + Rep("</b>", 10_000);
                case "aaa-many-formatting": return Rep("<a><b><i><u><s><em>", 5_000) + "<p>x" + Rep("</em></s></u></i></b></a>", 5_000);
                case "unclosed-formatting": return Rep("<b>x<p>", n);
                case "many-attributes":
                    sb.Append("<p");
                    for (int i = 0; i < 100_000; i++) sb.Append(" a").Append(i).Append("=1");
                    return sb.Append('>').ToString();
                case "duplicate-attributes": return "<p" + Rep(" a=1", 200_000) + ">";
                case "entity-flood": return Rep("&amp;&lt;&CounterClockwiseContourIntegral;&#x1F600;&notin", n);
                case "ampersand-flood": return Rep("&", 500_000) + Rep("&a", 200_000);
                case "long-comment": return "<!--" + new string('-', 1_000_000) + "-->";
                case "unclosed-comment": return "<!--" + Rep("<!-- -- -", 100_000);
                case "long-attribute": return "<p title='" + new string('x', 2_000_000) + "'>";
                case "huge-table":
                    sb.Append("<table>");
                    for (int r = 0; r < 300; r++)
                    {
                        sb.Append("<tr>");
                        for (int c = 0; c < 300; c++) sb.Append("<td>x");
                    }
                    return sb.ToString();
                case "nested-lists": return Rep("<ul><li>", n);
                case "foster-flood": return "<table>" + Rep("x<b>y</b><p>", n);
                case "template-nesting": return Rep("<template>", n);
                case "select-flood": return Rep("<select><option>", n);
                case "noahs-ark": return Rep("<p>" + Rep("<b class=a>", 10), 5_000);
                case "cdata-flood": return "<svg>" + Rep("<![CDATA[x]]>", n);
                case "mixed-ltlt": return Rep("<<</a<!<?<", 200_000);
                case "deep-blockquote-styles": return Rep("<blockquote style='color:red;margin:0'>", 20_000) + "x";
                case "css-deep": return "<style>" + Rep("a{", 100_000) + "</style><p style='" + Rep("calc(", 100_000) + "'>x";
                case "css-many-rules": return "<style>" + Rep(".a .b > p:not(.c) { color: red; background: url(x) }", 50_000) + "</style>";
                default: throw new ArgumentException(name);
            }
        }

        private static string Rep(string s, int n) => string.Concat(Enumerable.Repeat(s, n));

        [Theory]
        [MemberData(nameof(Cases))]
        public void ParsesWithinBudget(string name)
        {
            string html = Build(name);
            var sw = Stopwatch.StartNew();
            try
            {
                var doc = HtmlParser.Parse(html);
                int depth = doc.Descendants().Max(x => x.Depth);
                Assert.True(depth <= new MarkupLimits().MaxNestingDepth + 2, "depth " + depth);
            }
            catch (MarkupLimitExceededException)
            {
                // a documented limit is an acceptable outcome
            }
            Assert.True(sw.Elapsed.TotalSeconds < BudgetSeconds, $"{name}: {sw.Elapsed.TotalSeconds:F2}s");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SanitizesWithinBudget(string name)
        {
            string html = Build(name);
            var sw = Stopwatch.StartNew();
            try
            {
                var r = HtmlSanitizer.Sanitize(html, SanitizerPolicy.Reader());
                Assert.NotNull(r.Html);
            }
            catch (MarkupLimitExceededException)
            {
            }
            Assert.True(sw.Elapsed.TotalSeconds < BudgetSeconds * 2, $"{name}: {sw.Elapsed.TotalSeconds:F2}s");
        }

        [Fact]
        public void StrictLimitsRejectHugeInputQuickly()
        {
            var o = new HtmlParserOptions { Limits = MarkupLimits.Strict() };
            var sw = Stopwatch.StartNew();
            Assert.Throws<MarkupLimitExceededException>(() => HtmlParser.Parse(new string('x', o.Limits.MaxInputChars + 1), o));
            Assert.True(sw.Elapsed.TotalSeconds < 1);
        }
    }
}
