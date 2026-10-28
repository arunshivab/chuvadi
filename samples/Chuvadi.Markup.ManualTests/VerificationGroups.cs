using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Markdown;
using Chuvadi.Markup.Sanitization;
using Chuvadi.Markup.Text;

namespace Chuvadi.Markup.ManualTests
{
    /// <summary>
    /// End-to-end scenarios for the library's consumers (Reader, Anjal Mail, Layout, Docs). Each method
    /// is self-contained and throws on failure. The console Program runs them all; the xUnit Tests
    /// project drives each as a [Fact].
    /// </summary>
    public static class VerificationGroups
    {
        public static IReadOnlyList<Action> All() => new Action[]
        {
            MarkdownReadmeRendersAsGfm,
            MarkdownCommonMarkProfileHasNoExtensions,
            MarkdownRawHtmlModes,
            MarkdownUnsafeUrlsAreRemoved,
            MarkdownAstIsNavigable,
            MarkdownWriterRoundTrips,
            MarkdownThroughReaderSanitizerIsSafe,
            NewsletterSanitizesForAnjal,
            NewsletterSanitizesForReader,
            CidImagesResolveThroughHost,
            Windows1252MailDecodes,
            SanitizedOutputIsStable,
            PlainTextReplyBecomesSafeHtml,
            FormatFlowedJoinsLines,
            HtmlDomEditAndSerialize,
            HostileInputStaysBounded,
            CancellationStopsWork,
        };

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Verification failed: " + message);
        }

        private static void Contains(string haystack, string needle, string what) =>
            Require(haystack.Contains(needle, StringComparison.Ordinal), what + ": expected to contain \"" + needle + "\"");

        private static void Lacks(string haystack, string needle, string what) =>
            Require(!haystack.Contains(needle, StringComparison.OrdinalIgnoreCase), what + ": must not contain \"" + needle + "\"");

        public static void MarkdownReadmeRendersAsGfm()
        {
            string html = MarkdownConverter.ToHtml(Samples.ReadmeMarkdown);
            Contains(html, "<h1>Chuvadi Reader</h1>", "heading");
            Contains(html, "<strong>fast</strong>", "strong");
            Contains(html, "<del>unsafe</del>", "strikethrough");
            Contains(html, "<a href=\"http://www.example.org\">www.example.org</a>", "extended autolink");
            Contains(html, "<input type=\"checkbox\" checked=\"\" disabled=\"\" /> Markdown", "checked task");
            Contains(html, "<input type=\"checkbox\" disabled=\"\" /> PDF", "open task");
            Contains(html, "<th align=\"center\">Status</th>", "table alignment");
            Contains(html, "<td align=\"right\">652/652</td>", "table cell");
            Contains(html, "<pre><code class=\"language-csharp\">", "fenced code info");
            Contains(html, "<a href=\"https://example.com/ref\" title=\"Ref title\">reference</a>", "reference link");
            Contains(html, "line one<br />\nline two<br />\nline three".Replace("line one", "Line one"), "hard breaks");
            Contains(html, "&lt;span onclick=&quot;alert(1)&quot;&gt;inline html&lt;/span&gt;", "default raw HTML escaping");
            Lacks(html, "<span", "no raw tag");
            Lacks(html, "javascript:", "unsafe URL");
        }

        public static void MarkdownCommonMarkProfileHasNoExtensions()
        {
            string html = MarkdownConverter.ToHtml("~~x~~ www.example.org\n\n| a |\n|---|\n| b |", MarkdownOptions.CommonMark());
            Lacks(html, "<del>", "strikethrough off");
            Lacks(html, "<table>", "tables off");
            Lacks(html, "<a ", "autolinks off");
        }

        public static void MarkdownRawHtmlModes()
        {
            const string md = "a <b class=x>bold</b>\n\n<div>block</div>\n";
            string esc = MarkdownConverter.ToHtml(md, null, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Escape });
            Contains(esc, "a &lt;b class=x&gt;bold&lt;/b&gt;", "escape inline");
            Contains(esc, "<p>&lt;div&gt;block&lt;/div&gt;</p>", "escape block");
            string strip = MarkdownConverter.ToHtml(md, null, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Strip });
            Lacks(strip, "<b", "strip");
            Lacks(strip, "&lt;b", "strip leaves no escaped tag");
            string pass = MarkdownConverter.ToHtml(md, null, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough });
            Contains(pass, "<b class=x>bold</b>", "passthrough");
            Contains(pass, "<div>block</div>", "passthrough block");
        }

        public static void MarkdownUnsafeUrlsAreRemoved()
        {
            string html = MarkdownConverter.ToHtml("[a](javascript:x) [b](VBScript:x) ![c](data:text/html,x) ![d](data:image/png;base64,AA) [e](file:///etc/passwd)");
            Lacks(html, "javascript", "javascript");
            Lacks(html, "vbscript", "vbscript");
            Lacks(html, "text/html", "data html");
            Contains(html, "data:image/png;base64,AA", "data image kept");
        }

        public static void MarkdownAstIsNavigable()
        {
            var doc = MarkdownParser.Parse(Samples.ReadmeMarkdown, MarkdownOptions.Gfm());
            var headings = doc.Descendants().OfType<Heading>().ToList();
            Require(headings.Count == 2 && headings[0].Level == 1 && headings[1].Level == 2, "two headings");
            Require(headings[1].ToPlainText() == "Features", "heading text");
            var table = doc.Descendants().OfType<Table>().Single();
            Require(table.Alignments.Count == 3, "three table columns");
            var tasks = doc.Descendants().OfType<ListItem>().Where(i => i.TaskState != TaskState.None).ToList();
            Require(tasks.Count == 3 && tasks.Count(t => t.TaskState == TaskState.Checked) == 2, "task states");
            Require(doc.LinkDefinitions.ContainsKey(LinkReferenceDefinition.NormalizeLabel("REF")), "link definitions are normalized");
            var pos = doc.GetPosition(headings[1].Span.Start);
            Require(pos.Line == 5 && pos.Column == 1, "source position of second heading: " + pos);
        }

        public static void MarkdownWriterRoundTrips()
        {
            var doc = MarkdownParser.Parse(Samples.ReadmeMarkdown, MarkdownOptions.Gfm());
            string md = MarkdownWriter.Write(doc);
            var opts = new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough };
            string a = MarkdownHtmlRenderer.Render(doc, opts);
            string b = MarkdownHtmlRenderer.Render(MarkdownParser.Parse(md, MarkdownOptions.Gfm()), opts);
            Require(a == b, "Markdown written from the AST renders identically");
        }

        public static void MarkdownThroughReaderSanitizerIsSafe()
        {
            string html = MarkdownConverter.ToHtml(Samples.ReadmeMarkdown, null, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough });
            var r = HtmlSanitizer.Sanitize(html, SanitizerPolicy.Reader());
            Lacks(r.Html, "onclick", "event handler");
            Contains(r.Html, "<span>inline html</span>", "allowed raw span");
            Contains(r.Html, "<table>", "table");
            Require(r.IsStable, "stable");
        }

        public static void NewsletterSanitizesForAnjal()
        {
            var r = HtmlSanitizer.Sanitize(Samples.NewsletterHtml, SanitizerPolicy.Anjal());
            string h = r.Html;
            Require(r.Title == "October deals", "title extracted");
            foreach (var bad in new[] { "<script", "onload", "<form", "<input", "<iframe", "javascript:", "tracker.example", "cdn.example", "fonts.example", "@import", "behavior", "position: fixed", " href=" })
                Lacks(h, bad, "Anjal output");
            Contains(h, "<div class=\"chuvadi-content\"", "scope wrapper");
            Contains(h, ".chuvadi-content #user-content-wrapper", "scoped id selector");
            Contains(h, "@media only screen and (max-width: 620px)", "media query kept");
            Contains(h, "data-chuvadi-href=\"https://shop.example/deals?utm_source=mail&amp;id=42\"", "inert link keeps destination");
            Contains(h, "data-chuvadi-href=\"mailto:help@shop.example\"", "inert mailto");
            Contains(h, "background-color: #f4f4f4", "body bgcolor transferred");
            Contains(h, "<h1>October deals</h1>", "content kept");
            var blocked = r.BlockedResources.Select(b => b.Url).ToList();
            foreach (var u in new[] { "https://cdn.example/product.jpg", "cid:logo@shop.example", "https://tracker.example/open.gif?u=42", "https://tracker.example/bg.png" })
                Require(blocked.Contains(u), "blocked resource reported: " + u);
            Require(r.IsStable, "stable");
        }

        public static void NewsletterSanitizesForReader()
        {
            var r = HtmlSanitizer.Sanitize(Samples.NewsletterHtml, SanitizerPolicy.Reader());
            foreach (var bad in new[] { "<script", "onload", "<form", "<iframe", "javascript:", "tracker.example", "cdn.example" })
                Lacks(r.Html, bad, "Reader output");
            Contains(r.Html, "href=\"https://shop.example/deals?utm_source=mail&amp;id=42\"", "links kept for host interception");
            Contains(r.Html, "rel=\"noopener noreferrer\"", "rel added");
            Contains(r.Html, "<input type=\"password\" name=\"pw\">", "inert form control kept (form removed)");
        }

        public static void CidImagesResolveThroughHost()
        {
            var p = SanitizerPolicy.Anjal();
            var requests = new List<ResourceRequest>();
            p.ResourceResolver = new DelegateResourceResolver(req =>
            {
                requests.Add(req);
                return req.Url == "cid:logo@shop.example" ? Samples.PngDataUri : null;
            });
            var r = HtmlSanitizer.Sanitize(Samples.NewsletterHtml, p);
            Contains(r.Html, "src=\"" + Samples.PngDataUri + "\" alt=\"Shop logo\"", "cid image inlined");
            Require(requests.Any(q => q.Url == "cid:logo@shop.example" && q.Kind == ResourceKind.Image && q.ElementName == "img" && q.AttributeName == "src"), "resolver request details");
            Require(r.BlockedResources.All(b => b.Url != "cid:logo@shop.example"), "resolved cid not reported blocked");
        }

        public static void Windows1252MailDecodes()
        {
            var doc = HtmlParser.Parse(Samples.Windows1252Mail());
            Require(doc.EncodingSource == HtmlEncodingSource.MetaPrescan, "encoding from meta prescan");
            Require(doc.Body!.TextContent == "“Café” €5", "windows-1252 text: " + doc.Body.TextContent);
            var r = HtmlSanitizer.Sanitize(Samples.Windows1252Mail());
            Contains(r.Html, "“Café” €5", "sanitized from bytes");
        }

        public static void SanitizedOutputIsStable()
        {
            foreach (var policy in new[] { SanitizerPolicy.Anjal(), SanitizerPolicy.Reader() })
            {
                var r = HtmlSanitizer.Sanitize(Samples.NewsletterHtml, policy);
                var reparsed = HtmlParser.ParseFragment(r.Html, "body");
                Require(HtmlSerializer.SerializeChildren(reparsed) == r.Html, "output serializes back to itself after re-parsing");
            }
        }

        public static void PlainTextReplyBecomesSafeHtml()
        {
            string html = PlainText.ToHtml(Samples.PlainTextReply, new TextToHtmlOptions { Links = LinkHandling.Inert });
            Contains(html, "&lt;not a tag&gt; &amp; ampersand", "escaped");
            Contains(html, "data-chuvadi-href=\"https://ci.example/build/123\"", "inert url link");
            Contains(html, "data-chuvadi-href=\"http://www.example.org/notes\"", "inert www link");
            Contains(html, "data-chuvadi-href=\"mailto:ops@example.com\"", "inert email link");
            Contains(html, "<blockquote>Can you check the release?<blockquote>Earlier: it failed on Windows.</blockquote>Thanks.</blockquote>", "nested quotes");
            Lacks(html, " href=", "no live links");
            string pre = PlainText.ToHtml(Samples.PlainTextReply, new TextToHtmlOptions { Layout = TextLayout.Preformatted, QuoteLevels = false, Autolink = false });
            Contains(pre, "<pre", "preformatted layout");
            Contains(pre, "&gt; Can you check", "quote markers kept as text");
        }

        public static void FormatFlowedJoinsLines()
        {
            string html = PlainText.ToHtml(Samples.FormatFlowedText, new TextToHtmlOptions { FormatFlowed = true, WrapperClass = null });
            Contains(html, "wrapped by the sender's mail client and should be shown as one flowing line in the reader. It ends here.", "flowed paragraph");
            Contains(html, "<blockquote>Quoted flowed text also joins up.</blockquote>", "flowed quote");
        }

        public static void HtmlDomEditAndSerialize()
        {
            var doc = HtmlParser.Parse("<!DOCTYPE html><p>Hello <b>world</b><table><tr><td>cell</table>");
            var p = doc.Body!.Children.First();
            var b = p.Children.Single();
            var strong = new HtmlElement("strong");
            b.ReplaceWith(strong);
            foreach (var c in b.ChildNodes.ToList()) strong.AppendChild(c);
            Require(p.OuterHtml == "<p>Hello <strong>world</strong></p>", "edited: " + p.OuterHtml);
            Require(doc.Body.InnerHtml.Contains("<tbody><tr><td>cell</td></tr></tbody>"), "implied tbody");
            var frag = HtmlParser.ParseFragment("<li>a<li>b", "ul");
            Require(frag.ChildNodes.Count == 2, "fragment li auto-close");
        }

        public static void HostileInputStaysBounded()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string deep = string.Concat(Enumerable.Repeat("<div><b><table><tr><td>", 20_000));
            var r = HtmlSanitizer.Sanitize(deep, SanitizerPolicy.Reader());
            Require(r.Html.Length > 0, "output produced");
            string md = new string('>', 100_000) + string.Concat(Enumerable.Repeat("*a **b ", 50_000));
            MarkdownConverter.ToHtml(md);
            var strict = SanitizerPolicy.Anjal();
            bool threw = false;
            try { HtmlSanitizer.Sanitize(new string('x', strict.Limits.MaxInputChars + 1), strict); }
            catch (MarkupLimitExceededException ex) { threw = ex.LimitName == nameof(MarkupLimits.MaxInputChars); }
            Require(threw, "input size limit enforced");
            Require(sw.Elapsed.TotalSeconds < 20, "hostile inputs processed in " + sw.Elapsed.TotalSeconds.ToString("F1") + "s");
        }

        public static void CancellationStopsWork()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            int cancelled = 0;
            try { HtmlSanitizer.Sanitize(Samples.NewsletterHtml, null, cts.Token); } catch (OperationCanceledException) { cancelled++; }
            try { MarkdownConverter.ToHtml(Samples.ReadmeMarkdown, null, null, cts.Token); } catch (OperationCanceledException) { cancelled++; }
            try { PlainText.ToHtml(Samples.PlainTextReply, null, cts.Token); } catch (OperationCanceledException) { cancelled++; }
            try { HtmlParser.Parse(Samples.NewsletterHtml, null, cts.Token); } catch (OperationCanceledException) { cancelled++; }
            Require(cancelled == 4, "every entry point honours a cancelled token (" + cancelled + "/4)");
        }
    }
}
