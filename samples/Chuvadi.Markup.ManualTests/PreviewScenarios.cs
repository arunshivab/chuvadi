using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Markdown;
using Chuvadi.Markup.Sanitization;
using Chuvadi.Markup.Text;

namespace Chuvadi.Markup.ManualTests
{
    /// <summary>
    /// Writes HTML pages for visual verification in a browser (Edge / WebView2). Each page wraps the
    /// library output in a host page with a Content-Security-Policy that blocks all network access,
    /// as Reader and Anjal do, so anything that still tried to load would show as a CSP error in the
    /// browser's developer tools console.
    /// </summary>
    public static class PreviewScenarios
    {
        private const string Csp = "default-src 'none'; img-src data:; media-src data:; font-src data:; style-src 'unsafe-inline'";

        public static int WriteAll(string folder)
        {
            Directory.CreateDirectory(folder);
            var pages = new List<(string File, string Title, string Body)>();

            pages.Add(("markdown-gfm.html", "Markdown (GFM) README", MarkdownConverter.ToHtml(Samples.ReadmeMarkdown)));
            pages.Add(("markdown-raw-html-sanitized.html", "Markdown with raw HTML, Reader-sanitized",
                HtmlSanitizer.Sanitize(MarkdownConverter.ToHtml(Samples.ReadmeMarkdown, null, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough, HeadingIds = true }), SanitizerPolicy.Reader()).Html));
            pages.Add(("markdown-roundtrip.html", "MarkdownWriter output of the README",
                "<pre>" + HtmlSerializer.EscapeText(MarkdownWriter.Write(MarkdownParser.Parse(Samples.ReadmeMarkdown, MarkdownOptions.Gfm()))) + "</pre>"));

            var anjal = HtmlSanitizer.Sanitize(Samples.NewsletterHtml, SanitizerPolicy.Anjal());
            pages.Add(("email-anjal.html", "Newsletter, Anjal policy (remote images blocked)", anjal.Html + Report(anjal)));

            var withCid = SanitizerPolicy.Anjal();
            withCid.ResourceResolver = new DelegateResourceResolver(r => r.Url.StartsWith("cid:", StringComparison.Ordinal) ? Samples.PngDataUri : null);
            var cid = HtmlSanitizer.Sanitize(Samples.NewsletterHtml, withCid);
            pages.Add(("email-anjal-cid.html", "Newsletter, Anjal policy, cid: parts resolved", cid.Html + Report(cid)));

            var reader = HtmlSanitizer.Sanitize(Samples.NewsletterHtml, SanitizerPolicy.Reader());
            pages.Add(("email-reader.html", "Newsletter, Reader policy", reader.Html + Report(reader)));

            pages.Add(("email-original-escaped.html", "Newsletter source (escaped, for comparison)", "<pre>" + HtmlSerializer.EscapeText(Samples.NewsletterHtml) + "</pre>"));
            pages.Add(("text-prewrap.html", "Plain-text reply, pre-wrap with quotes and inert links", PlainText.ToHtml(Samples.PlainTextReply, new TextToHtmlOptions { Links = LinkHandling.Inert })));
            pages.Add(("text-paragraphs.html", "Plain-text reply, paragraph layout", PlainText.ToHtml(Samples.PlainTextReply, new TextToHtmlOptions { Layout = TextLayout.Paragraphs })));
            pages.Add(("text-flowed.html", "format=flowed text", PlainText.ToHtml(Samples.FormatFlowedText, new TextToHtmlOptions { FormatFlowed = true })));
            pages.Add(("windows-1252.html", "windows-1252 mail decoded", HtmlSanitizer.Sanitize(Samples.Windows1252Mail()).Html));

            foreach (var (file, title, body) in pages)
                Write(Path.Combine(folder, file), title, body);

            var index = new StringBuilder("<ul>");
            foreach (var (file, title, _) in pages)
                index.Append("<li><a href=\"").Append(file).Append("\">").Append(HtmlSerializer.EscapeText(title)).Append("</a></li>");
            index.Append("</ul><p>Each page carries the CSP <code>").Append(HtmlSerializer.EscapeText(Csp)).Append("</code>. Open the developer tools console: there must be no CSP violation on any page.</p>");
            Write(Path.Combine(folder, "index.html"), "Chuvadi.Markup preview", index.ToString(), allowNavigation: true);
            return pages.Count + 1;
        }

        private static string Report(SanitizeResult r)
        {
            var sb = new StringBuilder("<hr><section class=\"report\"><h3>Sanitizer report</h3><p>Stable: ")
                .Append(r.IsStable).Append(", passes: ").Append(r.Passes).Append(", actions: ").Append(r.ActionCount)
                .Append(", title: ").Append(HtmlSerializer.EscapeText(r.Title)).Append("</p><p>Blocked resources:</p><ul>");
            foreach (var b in r.BlockedResources)
                sb.Append("<li>").Append(HtmlSerializer.EscapeText(b.Kind + " " + b.Element + (b.Attribute is null ? "" : "@" + b.Attribute) + " " + b.Url)).Append("</li>");
            sb.Append("</ul><p>Actions (first 50):</p><ul>");
            foreach (var a in r.Actions.Take(50))
                sb.Append("<li>").Append(HtmlSerializer.EscapeText(a.Kind + " " + a.Element + (a.Attribute is null ? "" : "@" + a.Attribute) + (a.Detail is null ? "" : " " + a.Detail))).Append("</li>");
            return sb.Append("</ul></section>").ToString();
        }

        private static void Write(string path, string title, string body, bool allowNavigation = false)
        {
            string html =
                "<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\n" +
                "<meta http-equiv=\"Content-Security-Policy\" content=\"" + Csp + "\">\n" +
                "<title>" + HtmlSerializer.EscapeText(title) + "</title>\n" +
                "<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;max-width:900px} .host-banner{background:#eef;border:1px solid #99c;padding:6px 10px;margin-bottom:16px;font-size:13px}" +
                " .report{font-size:12px;color:#444} table{border-collapse:collapse} th,td{border:1px solid #ccc;padding:4px} pre{background:#f6f6f6;padding:8px;overflow:auto}" +
                " [data-chuvadi-href]{color:#06c;text-decoration:underline;cursor:pointer}</style>\n" +
                "</head><body>\n<div class=\"host-banner\">" + HtmlSerializer.EscapeText(title) + (allowNavigation ? "" : " &mdash; <a href=\"index.html\">index</a>") + "</div>\n" +
                body + "\n</body></html>\n";
            File.WriteAllText(path, html, new UTF8Encoding(false));
        }
    }
}
