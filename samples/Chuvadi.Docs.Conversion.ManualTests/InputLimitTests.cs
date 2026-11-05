using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Docs.Conversion;
using Chuvadi.Docs.Model;
using Chuvadi.Markup;
using static Chuvadi.Docs.Conversion.ManualTests.Program;

namespace Chuvadi.Docs.Conversion.ManualTests;

/// <summary>
/// Docs 2.0 (D-154 option c2, D-168): every importer (Markdown, HTML, text) reports an input-limit
/// breach as the one Docs type <see cref="DocxLimitExceededException"/> (an IOException, as Sheets'
/// XlsxLimitExceededException), with Markup's original error as InnerException where there is one. Every public import entry point is covered.
/// </summary>
public static class InputLimitTests
{
    public static void Run(string outDir)
    {
        Check("Markdown: every import entry point reports a limit breach as DocxLimitExceededException (inner MarkupLimitExceededException)", () =>
        {
            var o = new MarkdownImportOptions();
            o.Parser.Limits.MaxInputChars = 100;
            var md = "# Title\n\n" + new string('x', 500);
            var entries = new (string, Action)[]
            {
                ("Import(string)", () => MarkdownConversion.Import(md, o)),
                ("Import(string, target)", () => MarkdownConversion.Import(md, new Document(), o)),
                ("ImportDetailed(string)", () => MarkdownConversion.ImportDetailed(md, null, o)),
                ("Import(TextReader)", () => MarkdownConversion.Import(new StringReader(md), o)),
                ("ImportDetailed(TextReader)", () => MarkdownConversion.ImportDetailed(new StringReader(md), null, o)),
            };
            foreach (var (name, run) in entries) Limit(run, "Markdown." + name, "MaxInputChars", 100, inner: true);
        });
        Check("Markdown: a limit inside the document (node count) and inside raw HTML (the HTML policy) is DocxLimitExceededException too", () =>
        {
            var o = new MarkdownImportOptions();
            o.Parser.Limits.MaxNodes = 20;
            Limit(() => MarkdownConversion.Import(string.Concat(Enumerable.Repeat("para *a* **b**\n\n", 50)), o), "nodes", "MaxNodes", 20, inner: true);
            var raw = new MarkdownImportOptions();
            raw.Html.Policy.Limits.MaxInputChars = 50;
            Limit(() => MarkdownConversion.Import("text\n\n<div>" + new string('y', 500) + "</div>\n", raw), "raw HTML", "MaxInputChars", 50, inner: true);
        });
        Check("HTML: every import entry point reports a limit breach as DocxLimitExceededException (inner MarkupLimitExceededException)", () =>
        {
            var o = new HtmlImportOptions();
            o.Policy.Limits.MaxInputChars = 100;
            var html = "<p>" + new string('x', 500) + "</p>";
            var bytes = Encoding.UTF8.GetBytes(html);
            var entries = new (string, Action, long)[]
            {
                ("Import(string)", () => HtmlConversion.Import(html, o), 100),
                ("Import(string, target)", () => HtmlConversion.Import(html, new Document(), o), 100),
                ("ImportDetailed(string)", () => HtmlConversion.ImportDetailed(html, null, o), 100),
                ("Import(byte[])", () => HtmlConversion.Import(bytes, null, o), 100),
                ("ImportDetailed(byte[])", () => HtmlConversion.ImportDetailed(bytes, null, null, o), 100),
                ("Import(TextReader)", () => HtmlConversion.Import(new StringReader(html), o), 100),
                ("Import(Stream)", () => HtmlConversion.Import(new MemoryStream(new byte[100_000]), null, o), 400),
            };
            foreach (var (name, run, limit) in entries) Limit(run, "Html." + name, "MaxInputChars", limit, inner: true);
        });
        Check("HTML: the work budget and the node limit are DocxLimitExceededException too", () =>
        {
            Limit(() => HtmlConversion.Import(string.Concat(Enumerable.Repeat("<div><b>", 20000)) + "x"), "work budget", null, null, inner: true);
            var o = new HtmlImportOptions();
            o.Policy.Limits.MaxNodes = 20;
            Limit(() => HtmlConversion.Import(string.Concat(Enumerable.Repeat("<p>a</p>", 100)), o), "nodes", "MaxNodes", 20, inner: true);
        });
        Check("Text: every import entry point reports the size cap as DocxLimitExceededException (no inner: nothing else failed)", () =>
        {
            var o = new TextImportOptions { MaxInputChars = 10 };
            var text = new string('x', 100);
            var entries = new (string, Action)[]
            {
                ("Import(string)", () => TextConversion.Import(text, o)),
                ("Import(string, target)", () => TextConversion.Import(text, new Document(), o)),
                ("ImportDetailed(string)", () => TextConversion.ImportDetailed(text, null, o)),
                ("Import(TextReader)", () => TextConversion.Import(new StringReader(text), o)),
            };
            foreach (var (name, run) in entries) Limit(run, "Text." + name, "MaxInputChars", 10, inner: false);
        });
        Check("inputs within the limits still import; argument errors keep their types", () =>
        {
            AssertTrue(MarkdownConversion.Import("# ok").GetText().Contains("ok"), "markdown");
            AssertTrue(HtmlConversion.Import("<p>ok</p>").GetText().Contains("ok"), "html");
            AssertTrue(TextConversion.Import("ok").GetText().Contains("ok"), "text");
            AssertThrows<ArgumentNullException>(() => MarkdownConversion.Import((string)null!), "md null");
            AssertThrows<ArgumentNullException>(() => HtmlConversion.Import((string)null!), "html null");
            AssertThrows<ArgumentNullException>(() => TextConversion.Import((TextReader)null!), "text null");
        });
    }

    private static void Limit(Action run, string context, string? limitName, long? limit, bool inner)
    {
        Exception? ex = null;
        try { run(); } catch (Exception e) { ex = e; }
        if (ex is not DocxLimitExceededException le)
            throw new InvalidOperationException($"{context}: expected DocxLimitExceededException, got {(ex is null ? "nothing" : ex.GetType().Name + ": " + ex.Message)}.");
        if (limitName is not null) AssertTrue(le.LimitName == limitName, $"{context}: LimitName {le.LimitName}");
        if (limit is not null) AssertTrue(le.Limit == limit, $"{context}: Limit {le.Limit}");
        AssertTrue(le.Actual > le.Limit || limitName is null, $"{context}: Actual {le.Actual} vs Limit {le.Limit}");
        if (inner)
        {
            AssertTrue(le.InnerException is MarkupLimitExceededException m && m.LimitName == le.LimitName && m.Limit == le.Limit && m.Actual == le.Actual,
                $"{context}: inner {le.InnerException?.GetType().Name}");
        }
        else AssertTrue(le.InnerException is null, $"{context}: unexpected inner {le.InnerException?.GetType().Name}");
        // The family pattern (Chuvadi.Sheets' XlsxLimitExceededException): an IOException, not a DocxFormatException, same message style.
        AssertTrue(le is IOException, context + ": not an IOException");
        AssertTrue(le.Message.StartsWith($"{le.LimitName} exceeded ({le.Actual} > {le.Limit}): the ", StringComparison.Ordinal)
                   && le.Message.EndsWith(".", StringComparison.Ordinal), context + ": message " + le.Message);
    }
}
