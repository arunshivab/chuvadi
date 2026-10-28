using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Markup.Markdown;

namespace Chuvadi.Markup.Tests.Markdown
{
    /// <summary>
    /// The cmark pathological inputs (quadratic and exponential traps). Each must finish quickly and
    /// produce the reference output, or the documented degraded output where a depth limit applies.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class PathologicalTests
    {
        private const double BudgetSeconds = 3.0;

        private static string Render(string input)
        {
            GC.Collect(); // keep earlier tests' garbage out of this test's time budget
            var sw = Stopwatch.StartNew();
            string html = MarkdownConverter.ToHtml(input, MarkdownOptions.Gfm(), new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough, UnsafeUrls = UnsafeUrlPolicy.Keep });
            sw.Stop();
            Assert.True(sw.Elapsed.TotalSeconds < BudgetSeconds, "took " + sw.Elapsed.TotalSeconds.ToString("F2") + " s");
            return html;
        }

        private static string Rep(string s, int n) => string.Concat(Enumerable.Repeat(s, n));

        private static void Matches(string pattern, string html) => Assert.Matches(new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(30)), html);

        [Fact]
        public void NestedStrongEmph_DegradesPastDepthButKeepsText()
        {
            string html = Render(Rep("*a **a ", 65000) + "b" + Rep(" a** a*", 65000));
            Matches("^<p>(<em>a <strong>a ){100}", html);
            Assert.Contains("b", html);
            Assert.True(html.Length > 65000 * 4);
        }

        // Security review F2/F5: contiguous same-character delimiter runs were quadratic (each match copied the run).
        [Theory]
        [InlineData('*')]
        [InlineData('_')]
        [InlineData('~')]
        public void ContiguousDelimiterRuns_MillionEachSide_NoToken(char c)
        {
            string html = Render(new string(c, 1_000_000) + "a" + new string(c, 1_000_000));
            Assert.Contains("a", html, StringComparison.Ordinal);
        }

        [Fact]
        public void ContiguousMixedDelimiterRuns() => Render(new string('*', 300_000) + new string('_', 300_000) + "a" + new string('_', 300_000) + new string('*', 300_000));

        [Fact]
        public void UnderscoreRunsWithMod3Lengths() => Render(Rep("_a__b___c", 200_000));

        [Fact]
        public void InlineWorkBudgetThrowsTheLimitException()
        {
            var o = MarkdownOptions.Gfm();
            o.Limits.MaxParserWorkPerChar = 1;
            var ex = Assert.Throws<MarkupLimitExceededException>(() => MarkdownParser.Parse(Rep("*a_ ", 500_000), o));
            Assert.Equal(nameof(MarkupLimits.MaxParserWorkPerChar), ex.LimitName);
        }

        [Fact]
        public void CancellationIsResponsiveInsideDelimiterProcessing()
        {
            string input = new string('*', 3_000_000) + "a" + new string('*', 3_000_000);
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var sw = Stopwatch.StartNew();
            try { MarkdownParser.Parse(input, null, cts.Token); }
            catch (OperationCanceledException) { }
            Assert.True(sw.Elapsed.TotalSeconds < 2, "took " + sw.Elapsed.TotalSeconds.ToString("F2") + " s");
        }

        [Fact] public void ManyEmphClosers() => Matches("(a[_] ){64999}a_", Render(Rep("a_ ", 65000)));
        [Fact] public void ManyEmphOpeners() => Matches("(_a ){64999}_a", Render(Rep("_a ", 65000)));
        [Fact] public void ManyLinkClosers() => Matches(@"(a\]){65000}", Render(Rep("a]", 65000)));
        [Fact] public void ManyLinkOpeners() => Matches(@"(\[a){65000}", Render(Rep("[a", 65000)));
        [Fact] public void MismatchedOpenersAndClosers() => Matches("([*]a[_] ){49999}[*]a_", Render(Rep("*a_ ", 50000)));
        [Fact] public void OpenersAndClosersMultipleOf3() => Matches("a[*][*]b(c[*] ){49999}c[*]", Render("a**b" + Rep("c* ", 50000)));
        [Fact] public void LinkOpenersAndEmphClosers() => Matches(@"(\[ a_){50000}", Render(Rep("[ a_", 50000)));
        [Fact] public void BracketParenPattern() => Matches(@"(\[ \(\]\(){80000}", Render(Rep("[ (](", 80000)));
        [Fact] public void ImageEmptyLinkPattern() => Matches("(!\\[<a href=\"\"></a>){160000}", Render(Rep("![[]()", 160000)));
        [Fact] public void HardLinkEmphCase() => Matches("\\*\\*x <a href=\"d\">a<em>b\\*\\*c</em></a>", Render("**x [a*b**c*](d)"));
        [Fact] public void NestedBrackets() => Matches(@"\[{50000}a\]{50000}", Render(Rep("[", 50000) + "a" + Rep("]", 50000)));

        [Fact]
        public void NestedBlockQuotes_StopAtDepthLimit()
        {
            string html = Render(Rep("> ", 50000) + "a");
            int depth = Regex.Matches(html, "<blockquote>").Count;
            Assert.InRange(depth, 500, 512);
            Assert.Contains("&gt; &gt;", html);
        }

        [Fact]
        public void DeeplyNestedLists_WithinLimit()
        {
            string input = string.Concat(Enumerable.Range(0, 200).Select(x => new string(' ', 2 * x) + "* a\n"));
            Matches("<ul>\n(<li>a\n<ul>\n){199}<li>a</li>\n</ul>\n(</li>\n</ul>\n){199}", Render(input));
        }

        [Fact]
        public void DeeplyNestedLists_PastLimit_Degrades()
        {
            string input = string.Concat(Enumerable.Range(0, 1000).Select(x => new string(' ', 2 * x) + "* a\n"));
            string html = Render(input);
            Assert.InRange(Regex.Matches(html, "<ul>").Count, 200, 256);
        }

        [Fact] public void NulInInput() => Matches("abc�de�", Render("abc\0de\0"));

        [Fact]
        public void Backticks() => Matches("^<p>[e`]*</p>\n$", Render(string.Concat(Enumerable.Range(1, 4999).Select(x => "e" + new string('`', x)))));

        [Fact] public void UnclosedLinksA() => Matches(@"(\[a\]\(&lt;b){30000}", Render(Rep("[a](<b", 30000)));
        [Fact] public void UnclosedLinksB() => Matches(@"(\[a\]\(b){30000}", Render(Rep("[a](b", 30000)));
        [Fact] public void UnclosedComment() => Matches(@"\&lt;\/(\&lt;!--){300000}", Render("</" + Rep("<!--", 300000)));

        [Fact]
        public void Tables() => Matches("^<p>aaa</p>\n<table>\n<thead>\n<tr>\n<th>bbb</th>\n</tr>\n</thead>\n<tbody>\n(<tr>\n<td>aaa</td>\n</tr>\n<tr>\n<td>bbb</td>\n</tr>\n<tr>\n<td>-\x0b</td>\n</tr>\n){29999}</tbody>\n</table>\n$",
            Render(Rep("aaa\rbbb\n-\v\n", 30000)));

        [Fact]
        public void ManyReferences()
        {
            var sb = new StringBuilder();
            for (int i = 1; i < 5000 * 16; i++) sb.Append('[').Append(i).Append("]: u\n");
            sb.Append(Rep("[0] ", 5000));
            Matches(@"(\[0\] ){4999}", Render(sb.ToString()));
        }

        [Fact]
        public void ManyTableColumns_Bounded()
        {
            string header = Rep("|a", 5000) + "|\n" + Rep("|-", 5000) + "|\n";
            string html = Render(header + Rep(Rep("|x", 10) + "|\n", 2000));
            Assert.DoesNotContain("<table>", html); // 5000 columns exceeds MaxTableColumns (1000)
        }

        [Fact]
        public void TableCellsCapped_RowsDegradeToParagraph()
        {
            var options = MarkdownOptions.Gfm();
            options.Limits.MaxTableCells = 100;
            string input = "|a|b|\n|-|-|\n" + Rep("|x|y|\n", 200);
            var doc = MarkdownParser.Parse(input, options);
            var table = Assert.IsType<Table>(doc.Children[0]);
            Assert.True(table.Children.Count <= 50);
        }

        [Fact]
        public void LongEmailAndUrlRuns()
        {
            Render(Rep("a@b.", 50000) + " " + Rep("www.a", 50000) + " " + Rep("http://x.", 30000));
        }
    }
}
