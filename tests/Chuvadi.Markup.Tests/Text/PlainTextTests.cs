using Chuvadi.Markup.Sanitization;
using Chuvadi.Markup.Text;

namespace Chuvadi.Markup.Tests.Text
{
    public class PlainTextTests
    {
        [Fact]
        public void EscapesAndPreservesWhitespace()
        {
            string html = PlainText.ToHtml("a <b> & \"c\"\n  indented\ttab", new TextToHtmlOptions { Autolink = false });
            Assert.Equal("<div class=\"chuvadi-text\" style=\"white-space: pre-wrap; tab-size: 8\">a &lt;b&gt; &amp; &quot;c&quot;\n  indented\ttab</div>", html);
        }

        [Fact]
        public void Autolinks()
        {
            string html = PlainText.ToHtml("see https://x.example/a_(b). www.y.example, z@w.example!", new TextToHtmlOptions { LinksOpenInNewWindow = false, WrapperClass = null, Layout = TextLayout.Preformatted });
            Assert.Equal("<pre style=\"tab-size: 8\">see <a href=\"https://x.example/a_(b)\">https://x.example/a_(b)</a>. <a href=\"http://www.y.example\">www.y.example</a>, <a href=\"mailto:z@w.example\">z@w.example</a>!</pre>", html);
        }

        [Fact]
        public void InertLinksForMail()
        {
            string html = PlainText.ToHtml("https://x.example", new TextToHtmlOptions { Links = LinkHandling.Inert, WrapperClass = null });
            Assert.Contains("<a data-chuvadi-href=\"https://x.example\" title=\"https://x.example\">https://x.example</a>", html);
            Assert.DoesNotContain("href=\"https", html.Replace("data-chuvadi-href", ""));
        }

        [Fact]
        public void QuoteLevelsAndFlowed()
        {
            string html = PlainText.ToHtml("top\n> one\n>> two\n> back\nend", new TextToHtmlOptions { WrapperClass = null });
            Assert.Contains("top<blockquote>one<blockquote>two</blockquote>back</blockquote>end", html);
            string flowed = PlainText.ToHtml("soft \nline\n> q \n> r\n-- \nsig", new TextToHtmlOptions { FormatFlowed = true, WrapperClass = null });
            Assert.Contains("soft line<blockquote>q r</blockquote>-- \nsig", flowed);
            string delsp = PlainText.ToHtml("soft \nline", new TextToHtmlOptions { FormatFlowed = true, DelSp = true, WrapperClass = null });
            Assert.Contains(">softline<", delsp);
        }

        [Fact]
        public void ParagraphLayout()
        {
            string html = PlainText.ToHtml("one\ntwo   three\n\n\nfour", new TextToHtmlOptions { Layout = TextLayout.Paragraphs, WrapperClass = null });
            Assert.Equal("<div><p>one<br>two &nbsp;&nbsp;three</p><p>four</p></div>", html);
        }

        [Fact]
        public void LongInputIsLinear()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            PlainText.ToHtml(string.Concat(System.Linq.Enumerable.Repeat("a@b c www http:/ x.example@ ", 50_000)));
            Assert.True(sw.Elapsed.TotalSeconds < 3);
        }
    }
}
