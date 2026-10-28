using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Html;

namespace Chuvadi.Markup.Tests.Html
{
    public class HtmlApiTests
    {
        [Fact]
        public void ParsesDocumentStructure()
        {
            var doc = HtmlParser.Parse("<!DOCTYPE html><title> A  title </title><p class='a b' id=x>Hi<b>there");
            Assert.Equal(HtmlQuirksMode.NoQuirks, doc.QuirksMode);
            Assert.Equal("html", doc.DocumentType!.Name);
            Assert.Equal("A title", doc.Title);
            var p = doc.Body!.Children.Single();
            Assert.Equal("p", p.LocalName);
            Assert.Equal(new[] { "a", "b" }, p.ClassList);
            Assert.Equal("Hithere", p.TextContent);
            Assert.Equal("<p class=\"a b\" id=\"x\">Hi<b>there</b></p>", p.OuterHtml);
        }

        [Fact]
        public void QuirksModeFromDoctype()
        {
            Assert.Equal(HtmlQuirksMode.Quirks, HtmlParser.Parse("<p>x").QuirksMode);
            Assert.Equal(HtmlQuirksMode.LimitedQuirks, HtmlParser.Parse("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"x\">").QuirksMode);
        }

        [Fact]
        public void ParseErrorsHaveLineAndColumn()
        {
            var doc = HtmlParser.Parse("<!DOCTYPE html>\n<p>\0</p>");
            var err = doc.ParseErrors.First(e => e.Code == "unexpected-null-character");
            Assert.Equal(2, err.Line);
            Assert.Equal(4, err.Column);
            Assert.True(doc.ParseErrorCount >= 1);
        }

        [Fact]
        public void FragmentParsingUsesContext()
        {
            var frag = HtmlParser.ParseFragment("<td>cell</td>", "tr");
            Assert.Equal("td", ((HtmlElement)frag.FirstChild!).LocalName);
            var textarea = HtmlParser.ParseFragment("<b>not bold</b>", "textarea");
            Assert.IsType<HtmlText>(textarea.FirstChild);
        }

        [Fact]
        public void SerializerEscapesAttributesAndText()
        {
            var div = new HtmlElement("div");
            div.SetAttribute("title", "a\"b<c>& ");
            div.AppendChild(new HtmlText("1 < 2 & 3 > 2 "));
            Assert.Equal("<div title=\"a&quot;b&lt;c&gt;&amp;&nbsp;\">1 &lt; 2 &amp; 3 &gt; 2&nbsp;</div>", div.OuterHtml);
            var script = new HtmlElement("script");
            script.AppendChild(new HtmlText("if (a < b) x();"));
            Assert.Equal("<script>if (a < b) x();</script>", script.OuterHtml);
        }

        [Fact]
        public void SerializerKeepsLeadingNewlineInPre()
        {
            var doc = HtmlParser.Parse("<pre>\n\nx</pre>");
            var pre = doc.Body!.Children.Single();
            Assert.Equal("\nx", pre.TextContent);
            string html = pre.OuterHtml;
            Assert.Equal("<pre>\n\nx</pre>", html);
            Assert.Equal("\nx", HtmlParser.ParseFragment(html, "body").FirstChild!.TextContent);
        }

        [Fact]
        public void TemplateContentAndClone()
        {
            var doc = HtmlParser.Parse("<template id=t><p>in template</p></template>");
            var t = doc.Head!.Children.Single();
            Assert.Equal(0, t.ChildCount);
            Assert.Equal("in template", t.Content!.TextContent);
            var copy = (HtmlElement)t.Clone();
            Assert.Equal(t.OuterHtml, copy.OuterHtml);
            Assert.Equal("<template id=\"t\"><p>in template</p></template>", copy.OuterHtml);
        }

        [Fact]
        public void ForeignContentAdjustments()
        {
            var doc = HtmlParser.Parse("<svg viewbox='0 0 1 1'><foreignobject><p>x</p></foreignobject><use xlink:href='#a'/></svg><math definitionurl=u></math>");
            var svg = doc.Body!.Children.First();
            Assert.Equal(HtmlNamespace.Svg, svg.Namespace);
            Assert.Equal("0 0 1 1", svg.GetAttribute("viewBox"));
            Assert.Equal("foreignObject", svg.Children.First().LocalName);
            var use = svg.Children.Last();
            Assert.Equal(HtmlAttributeNamespace.XLink, use.Attributes[0].Namespace);
            Assert.Equal("xlink:href", use.Attributes[0].Name);
            Assert.Equal("u", doc.Body.Children.Last().GetAttribute("definitionURL"));
        }

        [Fact]
        public void DomEditing()
        {
            var frag = HtmlParser.ParseFragment("<p>a<b>b</b>c</p>", "body");
            var p = (HtmlElement)frag.FirstChild!;
            var b = p.Children.Single();
            b.Unwrap();
            Assert.Equal("<p>abc</p>", p.OuterHtml);
            var i = new HtmlElement("i");
            p.InsertBefore(i, p.FirstChild);
            i.AppendChild(new HtmlText("!"));
            Assert.Equal("<p><i>!</i>abc</p>", p.OuterHtml);
            Assert.Throws<InvalidOperationException>(() => i.AppendChild(p));
            Assert.Equal(4, p.ChildCount); // i + "a" + "b" + "c": Unwrap does not normalize text nodes
            Assert.Equal(4, p.ChildNodes.Count);
        }

        [Fact]
        public void ByteInputSniffsEncoding()
        {
            byte[] bom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("<p>é")).ToArray();
            var d1 = HtmlParser.Parse(bom);
            Assert.Equal(HtmlEncodingSource.ByteOrderMark, d1.EncodingSource);
            Assert.Equal("é", d1.Body!.TextContent);

            byte[] meta = Encoding.ASCII.GetBytes("<meta charset=\"windows-1251\"><p>").Concat(new byte[] { 0xCF, 0xF0, 0xE8 }).ToArray();
            var d2 = HtmlParser.Parse(meta);
            Assert.Equal(HtmlEncodingSource.MetaPrescan, d2.EncodingSource);
            Assert.Equal("При", d2.Body!.TextContent);

            byte[] http = Encoding.ASCII.GetBytes("<meta http-equiv=Content-Type content='text/html; charset=iso-8859-2'><p>").Concat(new byte[] { 0xB1 }).ToArray();
            Assert.Equal("ą", HtmlParser.Parse(http).Body!.TextContent);

            byte[] latin = new byte[] { (byte)'<', (byte)'p', (byte)'>', 0x93, 0x41, 0x94 };
            var d3 = HtmlParser.Parse(latin);
            Assert.Equal(HtmlEncodingSource.Fallback, d3.EncodingSource);
            Assert.Equal("“A”", d3.Body!.TextContent);

            var d4 = HtmlParser.Parse(Encoding.UTF8.GetBytes("<meta charset=latin1><p>é"), new HtmlParserOptions { Encoding = Encoding.UTF8 });
            Assert.Equal(HtmlEncodingSource.Caller, d4.EncodingSource);
            Assert.Equal("é", d4.Body!.TextContent);
        }

        [Fact]
        public void EncodingLabels()
        {
            Assert.Equal(1252, HtmlEncoding.GetEncoding("latin1")!.CodePage);
            Assert.Equal(1252, HtmlEncoding.GetEncoding(" US-ASCII ")!.CodePage);
            Assert.Equal(932, HtmlEncoding.GetEncoding("shift_jis")!.CodePage);
            Assert.Null(HtmlEncoding.GetEncoding("klingon"));
            Assert.Equal("utf-8", HtmlEncoding.ExtractCharsetFromContent("text/html; charset=utf-8"));
            Assert.Equal("x", HtmlEncoding.ExtractCharsetFromContent("charset=\"x\""));
            Assert.Null(HtmlEncoding.ExtractCharsetFromContent("text/html"));
        }

        [Fact]
        public void StreamInputRespectsLimit()
        {
            var o = new HtmlParserOptions();
            o.Limits.MaxInputChars = 100;
            Assert.Throws<MarkupLimitExceededException>(() => HtmlParser.Parse(new MemoryStream(new byte[1000]), o));
        }

        [Fact]
        public void NodeLimitThrows()
        {
            var o = new HtmlParserOptions();
            o.Limits.MaxNodes = 50;
            Assert.Throws<MarkupLimitExceededException>(() => HtmlParser.Parse(string.Concat(Enumerable.Repeat("<p>x", 100)), o));
        }

        [Fact]
        public void DepthLimitFlattensLikeChromium()
        {
            var o = new HtmlParserOptions();
            o.Limits.MaxNestingDepth = 20;
            var doc = HtmlParser.Parse(string.Concat(Enumerable.Repeat("<div>", 100)) + "deep", o);
            int max = doc.Descendants().Max(n => n.Depth);
            Assert.InRange(max, 19, 21);
            Assert.Contains("deep", doc.Body!.TextContent);
        }

        [Fact]
        public void AttributeLimits()
        {
            var o = new HtmlParserOptions();
            o.Limits.MaxAttributesPerElement = 3;
            o.Limits.MaxAttributeValueChars = 5;
            var doc = HtmlParser.Parse("<p a=1 b=2 c=3 d=4 e=123456789>", o);
            var p = doc.Body!.Children.Single();
            Assert.Equal(3, p.Attributes.Count);
            var doc2 = HtmlParser.Parse("<p a=123456789>", o);
            Assert.Equal("12345", doc2.Body!.Children.Single().GetAttribute("a"));
        }

        [Fact]
        public void CancellationIsHonoured()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => HtmlParser.Parse("<p>x", null, cts.Token));
        }

        [Fact]
        public void TokenizerIsUsableDirectly()
        {
            var tk = new HtmlTokenizer("<a href=x>t</a><!--c-->");
            var tokens = tk.Tokens().ToList();
            Assert.Equal(HtmlTokenKind.StartTag, tokens[0].Kind);
            Assert.Equal("x", tokens[0].GetAttribute("href"));
            Assert.Equal("t", tokens[1].Data);
            Assert.Equal(HtmlTokenKind.EndTag, tokens[2].Kind);
            Assert.Equal("c", tokens[3].Data);
            Assert.Equal(HtmlTokenKind.EndOfFile, tokens[4].Kind);
        }
    
        [Fact]
        public void SelectedContentMirrorsTheSelectedOption()
        {
            var doc = HtmlParser.Parse("<select><button><selectedcontent></selectedcontent></button><option>A<option selected>B<b>!</b><option>C</select>");
            var sc = doc.Descendants().OfType<HtmlElement>().Single(e => e.LocalName == "selectedcontent");
            Assert.Equal("B<b>!</b>", sc.InnerHtml);

            var multi = HtmlParser.Parse("<select multiple><button><selectedcontent></selectedcontent></button><option>A</select>");
            Assert.Equal(0, multi.Descendants().OfType<HtmlElement>().Single(e => e.LocalName == "selectedcontent").ChildCount);
        }

        [Fact]
        public void SelectedContentCloningCountsAgainstNodeLimit()
        {
            var o = new HtmlParserOptions();
            o.Limits.MaxNodes = 2_000;
            string html = "<select><button><selectedcontent></selectedcontent></button>" + string.Concat(Enumerable.Repeat("<option selected>" + string.Concat(Enumerable.Repeat("<i>x</i>", 100)), 20));
            Assert.Throws<MarkupLimitExceededException>(() => HtmlParser.Parse(html, o));
        }
    }
}
