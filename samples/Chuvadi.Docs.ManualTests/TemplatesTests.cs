using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Phase 7 — template blocks: repeating table rows, paragraph blocks and inline runs,
/// conditions, nested repeats with scopes, formatted values, per-item images, strict mode,
/// placeholder listing, stream overloads and limits.
/// </summary>
public static class TemplatesTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Run(string outDir)
    {
        SplitPlaceholders(outDir);
        TableRows(outDir);
        ParagraphBlocksAndNesting(outDir);
        InlineAndConditions(outDir);
        FormattedValuesAndImages(outDir);
        StrictListingStreams(outDir);
    }

    private static string Fill(string outDir, string name, Document template, TemplateData data, TemplateOptions? options = null)
    {
        var tpl = Path.Combine(outDir, name + "-template.docx");
        template.SaveTo(tpl);
        var output = Path.Combine(outDir, name + "-filled.docx");
        DocxTemplate.Fill(tpl, output, data, options);
        return output;
    }

    private static List<string> ParagraphTexts(string path)
    {
        var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
        return x.Descendants(Wn + "body").Single().Elements(Wn + "p").Select(p => string.Concat(p.Descendants(Wn + "t").Select(t => t.Value))).ToList();
    }

    private static List<List<string>> TableTexts(string path, int table = 0)
    {
        var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
        var t = x.Descendants(Wn + "tbl").ElementAt(table);
        return t.Elements(Wn + "tr").Select(r => r.Elements(Wn + "tc").Select(c => string.Concat(c.Descendants(Wn + "t").Select(tt => tt.Value))).ToList()).ToList();
    }

    private static void SplitPlaceholders(string outDir)
    {
        Check("a placeholder split across runs is filled and the neighbouring runs keep their formatting", () =>
        {
            var doc = new Document();
            doc.AddParagraph().Text("Dear {{Pat", TextFormat.None).Text("ient}}, ", new TextFormat { Italic = true }).Text("welcome", TextFormat.BoldText);
            var path = Fill(outDir, "tpl-split", doc, new TemplateData().Set("Patient", "R. Kumar"));
            var p = XElement.Parse(ReadPartXml(path, "word/document.xml")).Descendants(Wn + "p").First();
            var runs = p.Elements(Wn + "r").Select(r => (Text: string.Concat(r.Descendants(Wn + "t").Select(t => t.Value)), Bold: r.Descendants(Wn + "b").Any(), Italic: r.Descendants(Wn + "i").Any())).Where(r => r.Text.Length > 0).ToList();
            AssertTrue(string.Concat(runs.Select(r => r.Text)) == "Dear R. Kumar, welcome", string.Concat(runs.Select(r => r.Text)));
            AssertTrue(runs.Single(r => r.Text == ", ").Italic && runs.Single(r => r.Text == "welcome").Bold, "neighbour formatting kept");
        });
    }

    private static void TableRows(string outDir)
    {
        var doc = new Document();
        doc.AddParagraph("Report for {{Patient}}");
        var t = doc.AddTable(3);
        t.HeaderRow("Test", "Result", "Unit");
        t.AddRow("{{#Tests}}{{Name}}", "{{Result}}", "{{Unit}} ({{Patient}}){{/Tests}}");
        t.AddRow("Total", "{{Count}}", "");
        var data = new TemplateData().Set("Patient", "RK").Set("Count", "3");
        data.AddItem("Tests").Set("Name", "Hb").Set("Result", "13.2").Set("Unit", "g/dL");
        data.AddItem("Tests").Set("Name", "RBC").Set("Result", "4.8").Set("Unit", "M/µL");
        data.AddItem("Tests").Set("Name", "WBC").Set("Result", "7.1").Set("Unit", "K/µL");
        var path = Fill(outDir, "tpl-rows", doc, data);

        Check("table-row repeat: one row per item, header and total rows untouched, outer values resolve inside items", () =>
        {
            var rows = TableTexts(path);
            AssertTrue(rows.Count == 5, $"rows {rows.Count}");
            AssertTrue(string.Join("|", rows[1]) == "Hb|13.2|g/dL (RK)" && string.Join("|", rows[3]) == "WBC|7.1|K/µL (RK)", string.Join("|", rows[1]));
            AssertTrue(rows[0][0] == "Test" && string.Join("|", rows[4]) == "Total|3|", "fixed rows");
        });
        Check("multi-row repeat: markers in different rows repeat the row group; an empty list removes the rows", () =>
        {
            var d2 = new Document();
            var t2 = d2.AddTable(2);
            t2.AddRow("{{#People}}Name", "{{Name}}");
            t2.AddRow("Phone", "{{Phone}}{{/People}}");
            t2.AddRow("end", "");
            var data2 = new TemplateData();
            data2.AddItem("People").Set("Name", "A").Set("Phone", "1");
            data2.AddItem("People").Set("Name", "B").Set("Phone", "2");
            var p2 = Fill(outDir, "tpl-rowgroup", d2, data2);
            var rows = TableTexts(p2).Select(r => string.Join("|", r)).ToList();
            AssertTrue(string.Join(";", rows) == "Name|A;Phone|1;Name|B;Phone|2;end|", string.Join(";", rows));
            var p3 = Fill(outDir, "tpl-rowgroup-empty", d2, new TemplateData());
            AssertTrue(TableTexts(p3).Count == 1, "only the end row");
        });
    }

    private static void ParagraphBlocksAndNesting(string outDir)
    {
        var doc = new Document();
        doc.AddParagraph("Intro");
        doc.AddParagraph("{{#Groups}}");
        doc.AddHeading("{{Title}}", 2);
        var t = doc.AddTable(2);
        t.HeaderRow("Test", "Value");
        t.AddRow("{{#Tests}}{{Name}}", "{{Value}} {{Title}}{{/Tests}}");
        doc.AddParagraph("{{?Note}}Note: {{Note}}{{/Note}}");
        doc.AddParagraph("{{/Groups}}");
        doc.AddParagraph("Signed: {{Doctor}}");
        var data = new TemplateData().Set("Doctor", "Dr. S");
        var g1 = data.AddItem("Groups").Set("Title", "Haematology").Set("Note", "Fasting sample");
        g1.AddItem("Tests").Set("Name", "Hb").Set("Value", "13");
        g1.AddItem("Tests").Set("Name", "WBC").Set("Value", "7");
        var g2 = data.AddItem("Groups").Set("Title", "Biochemistry");
        g2.AddItem("Tests").Set("Name", "Glucose").Set("Value", "92");
        var path = Fill(outDir, "tpl-blocks", doc, data);

        Check("paragraph block repeat with a nested table-row repeat: headings and tables per group, marker paragraphs removed, scopes nest", () =>
        {
            var texts = ParagraphTexts(path);
            AssertTrue(texts.First() == "Intro" && texts.Last() == "Signed: Dr. S", "outside the block");
            AssertTrue(!texts.Any(s => s.Contains("{{")), "no markers left: " + string.Join("|", texts));
            AssertTrue(texts.Count(s => s == "Haematology") == 1 && texts.Count(s => s == "Biochemistry") == 1, "headings: " + string.Join("|", texts));
            AssertTrue(texts.Contains("Note: Fasting sample") && texts.Count(s => s.StartsWith("Note")) == 1, "condition per group");
            var t1 = TableTexts(path, 0).Select(r => string.Join("|", r)).ToList();
            var t2 = TableTexts(path, 1).Select(r => string.Join("|", r)).ToList();
            AssertTrue(string.Join(";", t1) == "Test|Value;Hb|13 Haematology;WBC|7 Haematology", string.Join(";", t1));
            AssertTrue(string.Join(";", t2) == "Test|Value;Glucose|92 Biochemistry", string.Join(";", t2));
        });
        Check("a marker paragraph with other text keeps that text; untouched parts are byte-identical", () =>
        {
            var d = new Document();
            d.AddParagraph("Items: {{#Items}}");
            d.AddParagraph("- {{.}}");
            d.AddParagraph("{{/Items}} (end)");
            var p = Fill(outDir, "tpl-marker-text", d, new TemplateData().SetItems("Items", new[] { "a", "b" }));
            AssertTrue(string.Join("|", ParagraphTexts(p)) == "Items: |- a|- b| (end)", string.Join("|", ParagraphTexts(p)));
            var tpl = Path.Combine(outDir, "tpl-marker-text-template.docx");
            AssertTrue(ReadPartXml(tpl, "word/styles.xml") == ReadPartXml(p, "word/styles.xml"), "styles.xml untouched");
        });
    }

    private static void InlineAndConditions(string outDir)
    {
        Check("inline repeat and inline conditions inside one paragraph", () =>
        {
            var d = new Document();
            d.AddParagraph().Text("Tags: {{#Tags}}[{{.}}]{{/Tags}}").Text(" — {{?Urgent}}URGENT{{/Urgent}}{{^Urgent}}routine{{/Urgent}}", TextFormat.BoldText);
            var p1 = Fill(outDir, "tpl-inline", d, new TemplateData().SetItems("Tags", new[] { "x", "y", "z" }).SetFlag("Urgent", true));
            AssertTrue(ParagraphTexts(p1)[0] == "Tags: [x][y][z] — URGENT", ParagraphTexts(p1)[0]);
            var p2 = Fill(outDir, "tpl-inline2", d, new TemplateData().SetFlag("Urgent", false));
            AssertTrue(ParagraphTexts(p2)[0] == "Tags:  — routine", ParagraphTexts(p2)[0]);
            var bold = XElement.Parse(ReadPartXml(p1, "word/document.xml")).Descendants(Wn + "r")
                .Where(r => r.Descendants(Wn + "t").Any(t => t.Value == "URGENT")).Single().Descendants(Wn + "b").Any();
            AssertTrue(bold, "kept bold");
        });
        Check("conditions as paragraph blocks; truthiness of strings, lists, missing keys and objects ({{#Obj}} enters its scope)", () =>
        {
            var d = new Document();
            d.AddParagraph("{{?Allergies}}");
            d.AddParagraph("Allergies: {{Allergies}}");
            d.AddParagraph("{{/Allergies}}");
            d.AddParagraph("{{^Items}}");
            d.AddParagraph("No items");
            d.AddParagraph("{{/Items}}");
            d.AddParagraph("{{#Customer}}");
            d.AddParagraph("{{Name}} of {{City}} / {{Customer.Name}}");
            d.AddParagraph("{{/Customer}}");
            var data = new TemplateData().Set("Allergies", "").SetObject("Customer", new TemplateData().Set("Name", "Acme").Set("City", "Chennai"));
            var p = Fill(outDir, "tpl-conditions", d, data);
            AssertTrue(string.Join("|", ParagraphTexts(p)) == "No items|Acme of Chennai / Acme", string.Join("|", ParagraphTexts(p)));
        });
    }

    private static void FormattedValuesAndImages(string outDir)
    {
        Check("formatted values layer over the placeholder's formatting; line breaks in values become w:br", () =>
        {
            var d = new Document();
            d.AddParagraph().Text("Result: {{Flag}}", new TextFormat { SizePt = 14, Font = "Arial" });
            d.AddParagraph().Text("{{Address}}");
            var v = new TemplateValue().Add("HIGH", new TextFormat { Bold = true, ColorHex = "C00000" }).Add(" (re-test)");
            var p = Fill(outDir, "tpl-formatted", d, new TemplateData().Set("Flag", v).Set("Address", "12 Main St\nChennai"));
            var x = XElement.Parse(ReadPartXml(p, "word/document.xml"));
            var high = x.Descendants(Wn + "r").Single(r => r.Descendants(Wn + "t").Any(t => t.Value == "HIGH"));
            AssertTrue(high.Descendants(Wn + "b").Any() && high.Descendants(Wn + "color").Single().Attribute(Wn + "val")!.Value == "C00000", "value formatting");
            AssertTrue(high.Descendants(Wn + "sz").Single().Attribute(Wn + "val")!.Value == "28" && high.Descendants(Wn + "rFonts").Single().Attribute(Wn + "ascii")!.Value == "Arial", "placeholder formatting kept");
            var retest = x.Descendants(Wn + "r").Single(r => r.Descendants(Wn + "t").Any(t => t.Value == " (re-test)"));
            AssertTrue(!retest.Descendants(Wn + "b").Any() && retest.Descendants(Wn + "sz").Any(), "second run: placeholder formatting only");
            AssertTrue(x.Descendants(Wn + "br").Count() == 1, "line break");
        });
        Check("per-item images in repeated rows: one media part per distinct image, unique drawing ids", () =>
        {
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
            var d = new Document();
            var t = d.AddTable(2);
            t.AddRow("{{#Items}}{{Name}}", "{{Photo}}{{/Items}}");
            var data = new TemplateData();
            for (int i = 0; i < 3; i++)
                data.AddItem("Items").Set("Name", "n" + i).SetImage("Photo", new ImageSpec { Bytes = png, ContentType = "image/png", WidthPt = 10 + i, HeightPt = 10 });
            var p = Fill(outDir, "tpl-images", d, data);
            using var z = ZipFile.OpenRead(p);
            AssertTrue(z.Entries.Count(e => e.FullName.StartsWith("word/media/")) == 1, "the same bytes are stored once");
            var x = XElement.Parse(ReadPartXml(p, "word/document.xml"));
            AssertTrue(x.Descendants(Wn + "drawing").Count() == 3, "three drawings");
            var docPrIds = x.Descendants().Where(e => e.Name.LocalName == "docPr").Select(e => e.Attribute("id")!.Value).ToList();
            AssertTrue(docPrIds.Distinct().Count() == 3, "unique drawing ids");
            Document.Load(p); // loads
        });
    }

    private static void StrictListingStreams(string outDir)
    {
        var d = new Document();
        d.Header.SetText("{{Clinic}}");
        d.AddParagraph("{{Patient}} {{Missing}}");
        d.AddParagraph("{{#Unclosed}}");
        var t = d.AddTable(1);
        t.AddRow("{{#Rows}}{{Cell}}{{/Rows}}");
        var tpl = Path.Combine(outDir, "tpl-strict-template.docx");
        d.SaveTo(tpl);

        Check("strict mode reports every missing value and unpaired marker; non-strict leaves them or empties values", () =>
        {
            try
            {
                DocxTemplate.Fill(tpl, Path.Combine(outDir, "tpl-strict-out.docx"), new TemplateData().Set("Patient", "RK").Set("Clinic", "C"), new TemplateOptions { Strict = true });
                AssertTrue(false, "no exception");
            }
            catch (DocxTemplateException ex)
            {
                AssertTrue(ex.Problems.Any(p => p.Contains("{{Missing}}")) && ex.Problems.Any(p => p.Contains("{{#Unclosed}}")), string.Join(" / ", ex.Problems));
                AssertTrue(!File.Exists(Path.Combine(outDir, "tpl-strict-out.docx")), "no output written");
            }
            var leave = Path.Combine(outDir, "tpl-leave.docx");
            DocxTemplate.Fill(tpl, leave, new TemplateData().Set("Patient", "RK"));
            AssertContains(ReadPartXml(leave, "word/document.xml"), "{{Missing}}", "left as-is");
            var empty = Path.Combine(outDir, "tpl-empty.docx");
            DocxTemplate.Fill(tpl, empty, new TemplateData().Set("Patient", "RK"), new TemplateOptions { MissingValue = TemplateMissingValue.Empty });
            AssertTrue(!ReadPartXml(empty, "word/document.xml").Contains("{{Missing}}"), "emptied");
        });
        Check("GetPlaceholders lists values, blocks, their nesting, scope and part (headers included)", () =>
        {
            var list = DocxTemplate.GetPlaceholders(tpl);
            AssertTrue(list.Any(p => p.Name == "Clinic" && p.Part.StartsWith("word/header")), "header value");
            var rows = list.Single(p => p.Name == "Rows");
            AssertTrue(rows.Kind == TemplatePlaceholderKind.Repeat && rows.Scope == TemplateBlockScope.TableRows, $"{rows.Kind} {rows.Scope}");
            var cell = list.Single(p => p.Name == "Cell");
            AssertTrue(cell.Path.SequenceEqual(new[] { "Rows" }) && cell.Kind == TemplatePlaceholderKind.Value, "nested path");
            AssertTrue(list.Any(p => p.Name == "Missing" && p.Path.Count == 0), "top-level value");
        });
        Check("stream overload: non-seekable template, output stream left open; limits stop runaway templates", () =>
        {
            var bytes = File.ReadAllBytes(tpl);
            using var output = new MemoryStream();
            DocxTemplate.Fill(new NonSeekable(bytes), output, new TemplateData().Set("Patient", "RK"));
            AssertTrue(output.CanWrite && output.Length > 0, "written, still open");
            output.Position = 0;
            AssertTrue(Document.Load(output).Blocks.OfType<Paragraph>().First().GetText().StartsWith("RK"), "readable");

            var many = new TemplateData();
            for (int i = 0; i < 50; i++) many.AddItem("Rows").Set("Cell", "c");
            AssertThrows<DocxTemplateException>(() => DocxTemplate.Fill(new MemoryStream(bytes), new MemoryStream(), many, new TemplateOptions { MaxGeneratedItems = 10 }), "item limit");
        });
    }

    private sealed class NonSeekable : Stream
    {
        private readonly MemoryStream _inner;
        public NonSeekable(byte[] b) => _inner = new MemoryStream(b);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
