using System;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Conversion;
using Chuvadi.Docs.Word;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Sanitization;
using static Chuvadi.Docs.Conversion.ManualTests.Program;

namespace Chuvadi.Docs.Conversion.ManualTests;

public static class HtmlExportTests
{
    public static void Run(string outDir)
    {
        Check("html export: full page with headings, formatting, lists, quote, table, notes, picture", () =>
        {
            var d = MarkdownExportTests.Sample();
            d.Properties.Title = "Report <1>";
            d.Properties.Language = "en-IN";
            d.AddParagraph().Text("red ", new TextFormat { ColorHex = "C00000" }).Text("big", new TextFormat { SizePt = 20 }).Text(" marked", new TextFormat { Highlight = "yellow" });
            var right = d.AddParagraph("right");
            right.Alignment = ParagraphAlignment.Right;
            var html = HtmlConversion.Export(d);
            File.WriteAllText(Path.Combine(outDir, "html-export.html"), html);
            AssertTrue(html.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal), "doctype");
            AssertContains(html, "<html lang=\"en-IN\">", "lang");
            AssertContains(html, "<title>Report &lt;1&gt;</title>", "escaped title");
            AssertContains(html, "<h1>Report *2026*</h1>", "h1");
            AssertContains(html, "<strong>bold</strong>", "strong");
            AssertContains(html, "<em>italic</em>", "em");
            AssertContains(html, "<code>code</code>", "code");
            AssertContains(html, "<a href=\"https://example.com\">site</a>", "link");
            AssertContains(html, "<ul>", "bullets");
            AssertContains(html, "<ol>", "numbers");
            AssertContains(html, "<blockquote>", "quote");
            AssertContains(html, "<th", "header cells");
            AssertContains(html, "a|b", "cell text");
            AssertContains(html, "<sub>2</sub>", "sub");
            AssertContains(html, "<sup><a id=\"fnref-1\" href=\"#fn-1\">1</a></sup>", "note ref");
            AssertContains(html, "<div id=\"fn-1\">", "note");
            AssertContains(html, "<img src=\"data:image/png;base64,", "picture");
            AssertContains(html, "color:#C00000", "colour");
            AssertContains(html, "font-size:20pt", "size");
            AssertContains(html, "background-color:#FFFF00", "highlight");
            AssertContains(html, "<p style=\"text-align:right\">right</p>", "alignment");
            var parsed = HtmlParser.Parse(html);
            AssertEqual(0, parsed.ParseErrorCount, "well-formed for the HTML parser");
        });

        Check("html export: semantic mode, labels mode, fragment wrapper, max blocks, unsafe links dropped", () =>
        {
            var d = new Document();
            d.AddNumbered("one");
            d.AddNumbered("sub", 1);
            d.AddParagraph().Hyperlink("bad", "javascript:alert(1)").Text(" ").Hyperlink("data", "data:text/html,<script>x</script>").Text(" ").Hyperlink("mail", "mailto:a@b.c");
            for (int i = 0; i < 10; i++) d.AddParagraph("p" + i);
            var labels = HtmlConversion.Export(d, new HtmlExportOptions { FullDocument = false, Lists = HtmlListRendering.Labels, Formatting = HtmlFormatting.Semantic, WrapperElement = "article", WrapperClass = "doc x", MaxBlocks = 5 });
            AssertTrue(labels.StartsWith("<article class=\"doc x\">", StringComparison.Ordinal), labels);
            AssertContains(labels, "<p>1. one</p>", "label as text");
            AssertContains(labels, "<p>1. sub</p>", "nested label");
            AssertNotContains(labels, "javascript", "javascript link dropped");
            AssertNotContains(labels, "<script", "data link dropped");
            AssertContains(labels, "href=\"mailto:a@b.c\"", "mailto kept");
            AssertContains(labels, "<p>…</p>", "cut marker");
            AssertNotContains(labels, "style=", "semantic: no inline styles");
            AssertNotContains(labels, "p9", "cut after max blocks");
        });

        Check("html preview: self-contained and the Anjal sanitizer removes nothing from it", () =>
        {
            var d = MarkdownExportTests.Sample();
            d.AddParagraph().Hyperlink("evil", "javascript:alert(1)");
            d.AddParagraph().Text("Tamil: வணக்கம்", new TextFormat { Font = "Nirmala UI" });
            var preview = HtmlConversion.ExportPreview(d);
            File.WriteAllText(Path.Combine(outDir, "html-preview.html"),
                "<!DOCTYPE html><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\">" + preview);
            AssertTrue(preview.StartsWith("<div class=\"chuvadi-docx\">", StringComparison.Ordinal), "wrapper");
            AssertNotContains(preview, "<html", "fragment");
            AssertNotContains(preview, "<style", "no style sheet");
            AssertNotContains(preview, "javascript", "no unsafe link");
            var r = HtmlSanitizer.Sanitize(preview, SanitizerPolicy.Anjal());
            var removals = r.Actions.Where(a => a.Kind is SanitizerActionKind.RemovedElement or SanitizerActionKind.UnwrappedElement
                or SanitizerActionKind.RemovedAttribute or SanitizerActionKind.RemovedCssDeclaration or SanitizerActionKind.BlockedResource)
                // Making a link inert removes its href (recorded together with NeutralizedLink): expected for mail.
                .Where(a => !(a.Kind == SanitizerActionKind.RemovedAttribute && a.Element == "a" && a.Attribute == "href")).ToList();
            AssertTrue(r.Actions.Count(a => a.Kind == SanitizerActionKind.NeutralizedLink) >= 1, "links made inert");
            AssertTrue(removals.Count == 0, "sanitizer removed: " + string.Join(", ", removals.Select(a => a.Kind + " " + a.Element + " " + a.Attribute + " " + a.Detail)));
            AssertTrue(r.BlockedResources.Count == 0 && r.IsStable, "nothing blocked");
            AssertContains(r.Html, "data:image/png;base64,", "picture survives the sanitizer");
        });

        Check("html export: pictures the browser cannot show, size cap and resolver", () =>
        {
            var d = new Document();
            var emf = ImageSpec.Inline(new byte[] { 1, 0, 0, 0 }, "image/x-emf", 20, 10);
            emf.AltText = "chart";
            d.AddParagraph().Image(emf);
            d.AddParagraph().Image(ImageSpec.FromBytes(Png(), "image/png"));
            var html = HtmlConversion.Export(d, new HtmlExportOptions { FullDocument = false });
            AssertContains(html, "[chart]", "EMF becomes its alt text");
            AssertEqual(1, CountOf(html, "<img "), "one picture");
            var capped = HtmlConversion.Export(d, new HtmlExportOptions { FullDocument = false, MaxImageBytes = 10 });
            AssertEqual(0, CountOf(capped, "<img "), "size cap");
            var resolved = HtmlConversion.Export(d, new HtmlExportOptions { FullDocument = false, ImageUrlResolver = i => i.Index == 2 ? "images/p2.png" : "javascript:x" });
            AssertContains(resolved, "src=\"images/p2.png\"", "resolver URL");
            AssertNotContains(resolved, "javascript", "unsafe resolver URL ignored");
        });
    }

    private static int CountOf(string s, string needle)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
