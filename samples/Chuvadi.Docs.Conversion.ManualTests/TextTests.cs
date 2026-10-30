using System;
using System.IO;
using System.Linq;
using System.Threading;
using Chuvadi.Docs.Conversion;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.Conversion.ManualTests.Program;

namespace Chuvadi.Docs.Conversion.ManualTests;

public static class TextTests
{
    public static void Run(string outDir)
    {
        Check("text import: one paragraph per line, CRLF/LF/CR, BOM, final newline, tabs and form feeds", () =>
        {
            var doc = TextConversion.Import("\ufeffFirst\tline\r\nSecond\rThird\n\fFourth\n");
            var paras = doc.Blocks.OfType<Paragraph>().ToList();
            AssertEqual(4, paras.Count, "paragraphs");
            AssertEqual("First", paras[0].Runs[0].TextContent, "BOM removed");
            AssertTrue(paras[0].Runs[1].IsTab && paras[0].GetText() == "First\tline", paras[0].GetText());
            AssertEqual("Third", paras[2].GetText(), "lone CR");
            AssertTrue(paras[3].Runs[0].Break == BreakType.Page && paras[3].GetText().EndsWith("Fourth", StringComparison.Ordinal), "form feed");
            doc.SaveTo(Path.Combine(outDir, "text-import.docx"));
        });

        Check("text import: blank-line paragraphs (line breaks or re-flowed), style, format, control characters", () =>
        {
            var src = "a\nb\n\n\n c \nd\u0001e";
            var kept = TextConversion.Import(src, new TextImportOptions { ParagraphMode = TextParagraphMode.BlankLineSeparated, StyleId = "Quote", Format = TextFormat.ItalicText });
            var p = kept.Blocks.OfType<Paragraph>().ToList();
            AssertEqual(2, p.Count, "two paragraphs");
            AssertTrue(p[0].Runs.Any(r => r.Break == BreakType.Line) && p[0].StyleId == "Quote" && p[0].Runs[0].Format.Italic, "breaks, style, format");
            AssertEqual(" c \nde", p[1].GetText(), "control character removed");
            var flowed = TextConversion.Import(src, new TextImportOptions { ParagraphMode = TextParagraphMode.BlankLineSeparatedReflow, RemoveControlCharacters = false });
            AssertEqual("c d�e", flowed.Blocks.OfType<Paragraph>().ElementAt(1).GetText(), "re-flowed");
            AssertThrows<InvalidDataException>(() => TextConversion.Import(new string('x', 100), new TextImportOptions { MaxInputChars = 10 }), "size cap");
            var reader = TextConversion.Import(new StringReader("r1\nr2"));
            AssertEqual(2, reader.Blocks.Count, "reader overload");
        });

        Check("text export: list labels, tables, notes, page breaks, tracked changes, headers", () =>
        {
            var d = new Document();
            d.Header.Add(new Paragraph("HEAD"));
            d.AddHeading("Title", 1);
            d.AddNumbered("one");
            d.AddNumbered("two");
            d.AddNumbered("sub", 1);
            d.AddBullet("dot");
            var t = d.AddTable(2);
            t.HeaderRow("H1", "H2");
            t.AddRow("a", "b");
            d.AddParagraph("Note here").Footnote(d, "the note");
            var page = d.AddParagraph("Next page");
            page.PageBreakBefore = true;
            d.AddParagraph("Kept ").TrackedDelete("gone", "R").TrackedInsert("new", "R");
            var text = TextConversion.Export(d);
            AssertContains(text, "Title\n1. one\n2. two\n  1. sub\n", "numbered with Word labels");
            AssertContains(text, "H1\tH2\na\tb\n", "table");
            AssertContains(text, "Note here[1]\n", "note reference");
            AssertContains(text, "\fNext page\n", "page break");
            AssertContains(text, "Kept new\n", "final view");
            AssertContains(text, "----\n[1] the note\n", "notes at the end");
            AssertNotContains(text, "HEAD", "headers off by default");
            var original = TextConversion.Export(d, new TextExportOptions { TrackedChanges = TrackedChangesView.Original, IncludeHeadersFooters = true, NewLine = "\r\n", ListLabels = false, PageBreak = null, IncludeNotes = false });
            AssertContains(original, "Kept gone\r\n", "original view");
            AssertTrue(original.StartsWith("HEAD\r\n", StringComparison.Ordinal), "header first");
            AssertContains(original, "\r\none\r\n", "no labels");
            AssertNotContains(original, "the note", "notes off");
            File.WriteAllText(Path.Combine(outDir, "text-export.txt"), text);
        });

        Check("text export: cancellation is honoured", () =>
        {
            var d = new Document();
            for (int i = 0; i < 5000; i++) d.AddParagraph("p" + i);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            AssertThrows<OperationCanceledException>(() => TextConversion.Export(d, null, cts.Token), "cancelled");
        });
    }
}
