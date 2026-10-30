using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Phase 7 review fixes for the template engine: insertion anchors, no re-reading of filled
/// data, data cycles, emptied containers, unique ids, linear time and cancellation, rows in
/// content controls, the scope stack, failed blocks, unknown run properties, value text that
/// is split, and the smaller items (one-cell rows, CheckTemplate, option checks).
/// </summary>
public static class TemplateFixesTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace WPn = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace W14n = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XNamespace Rels = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static void Run(string outDir)
    {
        Anchors(outDir);
        NoReRead(outDir);
        Cycles();
        EmptyContainers(outDir);
        UniqueIds(outDir);
        ScaleAndCancel(outDir);
        RowsInContentControls(outDir);
        ScopeStack(outDir);
        FailedBlocks(outDir);
        RunProperties(outDir);
        SmallItems(outDir);
        SecondReview(outDir);
    }

    // ---- helpers ---------------------------------------------------------------------

    private static string Save(string outDir, string name, Document d, Func<string, string>? editBody = null)
    {
        var tpl = Path.Combine(outDir, name + "-template.docx");
        d.SaveTo(tpl);
        if (editBody is not null) EditPart(tpl, "word/document.xml", editBody);
        return tpl;
    }

    private static string FillFile(string tpl, string name, TemplateData data, TemplateOptions? options = null)
    {
        var output = Path.Combine(Path.GetDirectoryName(tpl)!, name + "-filled.docx");
        DocxTemplate.Fill(tpl, output, data, options);
        return output;
    }

    private static void EditPart(string path, string part, Func<string, string> edit)
    {
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream();
        ms.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var e = zip.GetEntry(part)!;
            string xml;
            using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
            e.Delete();
            var n = zip.CreateEntry(part);
            using var w = new StreamWriter(n.Open(), new UTF8Encoding(false));
            w.Write(edit(xml));
        }
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static XElement Body(string path) => XElement.Parse(ReadPartXml(path, "word/document.xml")).Descendants(Wn + "body").Single();

    private static string Text(XElement e) => string.Concat(e.Descendants(Wn + "t").Select(t => t.Value));

    private static List<string> Paras(string path) => Body(path).Elements(Wn + "p").Select(Text).ToList();

    // ---- 1. anchors ------------------------------------------------------------------

    private static void Anchors(string outDir)
    {
        Check("inline repeat keeps item order and position, also when the last run of an item becomes an image", () =>
        {
            var d = new Document();
            d.AddParagraph("A {{#Items}}[{{Name}}]{{/Items}} Z");
            d.AddParagraph("{{#Items}}{{Photo}}{{/Items}}|end");
            var data = new TemplateData();
            foreach (var n in new[] { "a", "b", "c" })
                data.AddItem("Items").Set("Name", n).SetImage("Photo", new ImageSpec { Bytes = Png, ContentType = "image/png", WidthPt = 10, HeightPt = 10, AltText = n });
            var p = FillFile(Save(outDir, "fix-anchor", d), "fix-anchor", data);
            var paras = Paras(p);
            AssertTrue(paras[0] == "A [a][b][c] Z", paras[0]);
            var second = Body(p).Elements(Wn + "p").ElementAt(1);
            var alts = second.Descendants(WPn + "docPr").Select(e => (string?)e.Attribute("descr")).ToList();
            AssertTrue(alts.SequenceEqual(new[] { "a", "b", "c" }), string.Join(",", alts));
            AssertTrue(Text(second) == "|end", Text(second));
        });
    }

    // ---- 2. filled text is never read again -------------------------------------------

    private static void NoReRead(string outDir)
    {
        Check("values that look like placeholders or markers stay literal, at the top level and inside repeats", () =>
        {
            var d = new Document();
            d.AddParagraph("Top: {{Note}}");
            d.AddParagraph("{{#Items}}");
            d.AddParagraph("Item: {{Note}} / {{Other}}");
            d.AddParagraph("{{/Items}}");
            var data = new TemplateData().Set("Note", "{{Secret}}").Set("Secret", "LEAKED").Set("Other", "{{#Items}}x{{/Items}}");
            data.AddItem("Items").Set("Note", "{{Secret}} {{Other}}");
            var p = FillFile(Save(outDir, "fix-inject", d), "fix-inject", data);
            var paras = Paras(p);
            AssertTrue(paras[0] == "Top: {{Secret}}", paras[0]);
            AssertTrue(paras[1] == "Item: {{Secret}} {{Other}} / {{#Items}}x{{/Items}}", paras[1]);
            AssertTrue(!paras.Any(s => s.Contains("LEAKED")), "no second pass");
        });
    }

    // ---- 3. cycles -------------------------------------------------------------------

    private static void Cycles()
    {
        Check("template data cannot contain itself or an enclosing scope", () =>
        {
            var root = new TemplateData();
            AssertThrows<ArgumentException>(() => root.SetObject("Self", root), "self");
            var child = new TemplateData();
            root.SetObject("Child", child);
            AssertThrows<ArgumentException>(() => child.SetObject("Up", root), "ancestor object");
            var item = root.AddItem("Items");
            AssertThrows<ArgumentException>(() => item.SetItems("Loop", new[] { root }), "ancestor item");
            AssertThrows<ArgumentException>(() => item.SetItems("Loop", new[] { item }), "self item");
        });
    }

    // ---- 4. emptied containers ----------------------------------------------------------

    private static void EmptyContainers(string outDir)
    {
        Check("emptied cells, headers and bodies keep a paragraph; a table without rows is removed", () =>
        {
            var d = new Document();
            d.Header.SetText("{{?Show}}");
            d.Header.Add(new Paragraph("Header text"));
            d.Header.Add(new Paragraph("{{/Show}}"));
            var t = d.AddTable(2);
            t.AddRow("{{#Rows}}{{A}}", "{{B}}{{/Rows}}");
            var t2 = d.AddTable(1);
            var row = t2.AddRow("{{?Show}}");
            row.Cells[0].Paragraphs.Add(new Paragraph("cell text"));
            row.Cells[0].Paragraphs.Add(new Paragraph("{{/Show}}"));
            d.AddParagraph("{{?Show}}");
            d.AddParagraph("Body text");
            d.AddParagraph("{{/Show}}");
            var p = FillFile(Save(outDir, "fix-empty", d), "fix-empty", new TemplateData().SetFlag("Show", false));
            var body = Body(p);
            AssertTrue(body.Elements(Wn + "tbl").Count() == 1, "row-less table removed");
            var cell = body.Descendants(Wn + "tc").Single();
            AssertTrue(cell.Elements(Wn + "p").Any() && cell.Elements().Last().Name == Wn + "p", "cell keeps a final paragraph");
            AssertTrue(body.Elements().Last().Name == Wn + "sectPr", "sectPr stays last");
            using var z = ZipFile.OpenRead(p);
            var header = z.Entries.First(e => e.FullName.StartsWith("word/header"));
            string hx;
            using (var r = new StreamReader(header.Open())) hx = r.ReadToEnd();
            AssertTrue(XElement.Parse(hx).Elements(Wn + "p").Any(), "header keeps a paragraph");
            Document.Load(p);
        });
    }

    // ---- 5 + 12. unique ids ------------------------------------------------------------

    private static void UniqueIds(string outDir)
    {
        Check("an output filled again as a template gets new relationship and drawing ids", () =>
        {
            var d = new Document();
            d.AddParagraph("{{First}} {{Second}}");
            var tpl = Save(outDir, "fix-refill", d);
            var once = FillFile(tpl, "fix-refill-1", new TemplateData().SetImage("First", new ImageSpec { Bytes = Png, ContentType = "image/png", WidthPt = 5, HeightPt = 5 }));
            var twice = FillFile(once, "fix-refill-2", new TemplateData().SetImage("Second", new ImageSpec { Bytes = (byte[])Png.Clone(), ContentType = "image/png", WidthPt = 6, HeightPt = 6 }));
            var rels = XElement.Parse(ReadPartXml(twice, "word/_rels/document.xml.rels")).Elements(Rels + "Relationship").Select(r => (string)r.Attribute("Id")!).ToList();
            AssertTrue(rels.Distinct().Count() == rels.Count, string.Join(",", rels));
            var ids = Body(twice).Descendants(WPn + "docPr").Select(e => (string)e.Attribute("id")!).ToList();
            AssertTrue(ids.Count == 2 && ids.Distinct().Count() == 2, string.Join(",", ids));
            AssertTrue(Document.Load(twice).GetImagePlacements().Count == 2, "both pictures load");
        });
        Check("repeated content gets new drawing ids, renamed bookmarks and no duplicate paragraph ids", () =>
        {
            var d = new Document();
            d.AddParagraph("{{#Items}}");
            d.AddParagraph("Item {{Name}}").Image(new ImageSpec { Bytes = Png, ContentType = "image/png", WidthPt = 8, HeightPt = 8 });
            d.AddParagraph("{{/Items}}");
            var tpl = Save(outDir, "fix-ids", d, xml =>
            {
                var x = XElement.Parse(xml);
                var para = x.Descendants(Wn + "p").Single(e => Text(e).StartsWith("Item "));
                para.SetAttributeValue(W14n + "paraId", "1A2B3C4D");
                para.AddFirst(new XElement(Wn + "bookmarkStart", new XAttribute(Wn + "id", "7"), new XAttribute(Wn + "name", "ItemMark")));
                para.Add(new XElement(Wn + "bookmarkEnd", new XAttribute(Wn + "id", "7")));
                return x.ToString(SaveOptions.DisableFormatting);
            });
            AssertContains(ReadPartXml(tpl, "word/document.xml"), "ItemMark", "template edited");
            var data = new TemplateData();
            for (int i = 0; i < 3; i++) data.AddItem("Items").Set("Name", i.ToString());
            var p = FillFile(tpl, "fix-ids", data);
            var body = Body(p);
            var docPr = body.Descendants(WPn + "docPr").Select(e => (string)e.Attribute("id")!).ToList();
            AssertTrue(docPr.Count == 3 && docPr.Distinct().Count() == 3, string.Join(",", docPr));
            var names = body.Descendants(Wn + "bookmarkStart").Select(e => (string)e.Attribute(Wn + "name")!).ToList();
            var bIds = body.Descendants(Wn + "bookmarkStart").Select(e => (string)e.Attribute(Wn + "id")!).ToList();
            var eIds = body.Descendants(Wn + "bookmarkEnd").Select(e => (string)e.Attribute(Wn + "id")!).ToList();
            AssertTrue(names.Count == 3 && names.Distinct().Count() == 3 && names[0] == "ItemMark", string.Join(",", names));
            AssertTrue(bIds.Distinct().Count() == 3 && bIds.SequenceEqual(eIds), string.Join(",", bIds) + " / " + string.Join(",", eIds));
            var paraIds = body.Descendants().Select(e => (string?)e.Attribute(W14n + "paraId")).Where(v => v is not null).ToList();
            AssertTrue(paraIds.Distinct().Count() == paraIds.Count, "paraIds unique");
            Document.Load(p);
        });
    }

    // ---- 6. scale and cancellation --------------------------------------------------

    private static void ScaleAndCancel(string outDir)
    {
        Check("20,000 separate condition blocks in one body fill in linear time; cancellation stops a fill", () =>
        {
            var d = new Document();
            for (int i = 0; i < 20_000; i++) d.AddParagraph("{{?F}}x{{/F}}{{?G}}y{{/G}}");
            var tpl = Save(outDir, "fix-scale", d);
            var sw = Stopwatch.StartNew();
            var p = FillFile(tpl, "fix-scale", new TemplateData().SetFlag("F", true));
            sw.Stop();
            AssertTrue(sw.Elapsed.TotalSeconds < 10, $"took {sw.Elapsed.TotalSeconds:F1}s");
            var paras = Paras(p);
            AssertTrue(paras.Count == 20_000 && paras.All(s => s == "x"), "content");

            var d2 = new Document();
            for (int i = 0; i < 20_000; i++) d2.AddParagraph("{{?F}}a{{/F}} {{#L}}{{.}}{{/L}}");
            var tpl2 = Save(outDir, "fix-scale-paras", d2);
            var data = new TemplateData().SetFlag("F", true).SetItems("L", new[] { "1", "2" });
            sw.Restart();
            FillFile(tpl2, "fix-scale-paras", data);
            AssertTrue(sw.Elapsed.TotalSeconds < 10, $"took {sw.Elapsed.TotalSeconds:F1}s");

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            AssertThrows<OperationCanceledException>(() => DocxTemplate.Fill(tpl, Path.Combine(outDir, "fix-cancel.docx"), new TemplateData(), null, cts.Token), "cancelled");
        });
    }

    // ---- 7. rows inside content controls ---------------------------------------------

    private static void RowsInContentControls(string outDir)
    {
        Check("repeat markers in table rows wrapped in content controls repeat the rows", () =>
        {
            var d = new Document();
            var t = d.AddTable(2);
            t.AddRow("Name", "Value");
            t.AddRow("{{#Items}}{{Name}}", "{{Value}}{{/Items}}");
            var tpl = Save(outDir, "fix-sdt-rows", d, xml =>
            {
                var x = XElement.Parse(xml);
                var row = x.Descendants(Wn + "tr").ElementAt(1);
                var sdt = new XElement(Wn + "sdt", new XElement(Wn + "sdtPr"), new XElement(Wn + "sdtContent", new XElement(row)));
                row.ReplaceWith(sdt);
                return x.ToString(SaveOptions.DisableFormatting);
            });
            var data = new TemplateData();
            data.AddItem("Items").Set("Name", "a").Set("Value", "1");
            data.AddItem("Items").Set("Name", "b").Set("Value", "2");
            var p = FillFile(tpl, "fix-sdt-rows", data);
            var rows = Body(p).Descendants(Wn + "tr").Select(r => string.Join("|", r.Elements(Wn + "tc").Select(Text))).ToList();
            AssertTrue(rows.SequenceEqual(new[] { "Name|Value", "a|1", "b|2" }), string.Join(" / ", rows));
            AssertTrue(DocxTemplate.GetPlaceholders(tpl).Single(x => x.Name == "Items").Scope == TemplateBlockScope.TableRows, "scope");
        });
    }

    // ---- 8. scope stack --------------------------------------------------------------

    private static void ScopeStack(string outDir)
    {
        Check("lookups follow the template's nesting (a shared list sees each enclosing item); nested missing values follow the policy", () =>
        {
            var d = new Document();
            d.AddParagraph("{{#Customers}}");
            d.AddParagraph("{{#Orders}}{{Name}}:{{Item}}{{Nope}};{{/Orders}}");
            d.AddParagraph("{{/Customers}}");
            var shared = new List<TemplateData> { new TemplateData().Set("Item", "pen"), new TemplateData().Set("Item", "ink") };
            var data = new TemplateData();
            data.AddItem("Customers").Set("Name", "Asha").SetItems("Orders", shared);
            data.AddItem("Customers").Set("Name", "Ravi").SetItems("Orders", shared); // the items' data parent is now Ravi
            var p = FillFile(Save(outDir, "fix-scope", d), "fix-scope", data, new TemplateOptions { MissingValue = TemplateMissingValue.Empty });
            var paras = Paras(p);
            AssertTrue(paras.SequenceEqual(new[] { "Asha:pen;Asha:ink;", "Ravi:pen;Ravi:ink;" }), string.Join(" / ", paras));
            try
            {
                FillFile(Save(outDir, "fix-scope-strict", d), "fix-scope-strict", data, new TemplateOptions { Strict = true });
                AssertTrue(false, "no exception");
            }
            catch (DocxTemplateException ex)
            {
                AssertTrue(ex.Problems.Count(x => x.Contains("{{Nope}}")) == 1, string.Join(" / ", ex.Problems));
            }
        });
    }

    // ---- 9. failed blocks ----------------------------------------------------------------

    private static void FailedBlocks(string outDir)
    {
        Check("when a block's markers cannot form a block, the blocks inside it are still expanded", () =>
        {
            var d = new Document();
            var t = d.AddTable(1);
            t.AddRow("{{#Outer}} cell");
            d.AddParagraph("In: {{#L}}{{.}},{{/L}}");
            d.AddParagraph("{{/Outer}}");
            var tpl = Save(outDir, "fix-failed", d);
            var data = new TemplateData().SetItems("L", new[] { "a", "b" }).SetFlag("Outer", true);
            var p = FillFile(tpl, "fix-failed", data);
            AssertTrue(Paras(p)[0] == "In: a,b,", Paras(p)[0]);
            AssertTrue(DocxTemplate.CheckTemplate(tpl).Single().Contains("{{#Outer}}"), "CheckTemplate reports it");
            AssertThrows<DocxTemplateException>(() => FillFile(tpl, "fix-failed-strict", data, new TemplateOptions { Strict = true }), "strict");
        });
    }

    // ---- 10 + 11. run properties, split value text --------------------------------------

    private static void RunProperties(string outDir)
    {
        Check("a formatted value keeps run properties Docs does not model, in schema order", () =>
        {
            var d = new Document();
            d.AddParagraph().Text("{{V}}", new TextFormat { SizePt = 12 });
            var tpl = Save(outDir, "fix-rpr", d, xml => xml.Replace("<w:sz w:val=\"24\" />", "<w:webHidden /><w:sz w:val=\"24\" /><w:em w:val=\"dot\" />")
                                                          .Replace("<w:sz w:val=\"24\"/>", "<w:webHidden/><w:sz w:val=\"24\"/><w:em w:val=\"dot\"/>"));
            AssertContains(ReadPartXml(tpl, "word/document.xml"), "webHidden", "template edited");
            var p = FillFile(tpl, "fix-rpr", new TemplateData().Set("V", TemplateValue.Formatted("RED", new TextFormat { ColorHex = "FF0000", Bold = true })));
            var run = Body(p).Descendants(Wn + "r").Single(r => Text(r) == "RED");
            var names = run.Element(Wn + "rPr")!.Elements().Select(e => e.Name.LocalName).ToList();
            AssertTrue(names.Contains("webHidden") && names.Contains("em") && names.Contains("color") && names.Contains("b"), string.Join(",", names));
            int Pos(string n) => names.IndexOf(n);
            AssertTrue(Pos("b") < Pos("webHidden") && Pos("webHidden") < Pos("color") && Pos("color") < Pos("sz") && Pos("sz") < Pos("em"), string.Join(",", names));
        });
        Check("a value's line breaks survive when a picture placeholder before it splits the same text", () =>
        {
            var d = new Document();
            d.AddParagraph("{{Pic}} {{Addr}}");
            var p = FillFile(Save(outDir, "fix-split", d), "fix-split",
                new TemplateData().Set("Addr", "L1\nL2").SetImage("Pic", new ImageSpec { Bytes = Png, ContentType = "image/png", WidthPt = 5, HeightPt = 5 }));
            var para = Body(p).Elements(Wn + "p").First();
            AssertTrue(para.Descendants(Wn + "br").Count() == 1 && !Text(para).Contains('\n'), Text(para));
        });
    }

    // ---- low items ---------------------------------------------------------------------

    private static void SmallItems(string outDir)
    {
        Check("a one-cell row holding a whole block repeats as rows; empty names are not placeholders; From lets text win", () =>
        {
            var d = new Document();
            var t = d.AddTable(1);
            t.AddRow("{{#Items}}{{.}}{{/Items}}");
            d.AddParagraph("{{}} {{#}} {{Both}}");
            var tpl = Save(outDir, "fix-small", d);
            var data = new TemplateData().SetItems("Items", new[] { "a", "b", "c" }).Set("Both", "text");
            var p = FillFile(tpl, "fix-small", data);
            var rows = Body(p).Descendants(Wn + "tr").Select(Text).ToList();
            AssertTrue(rows.SequenceEqual(new[] { "a", "b", "c" }), string.Join("/", rows));
            AssertTrue(Paras(p)[0] == "{{}} {{#}} text", Paras(p)[0]);
            AssertTrue(DocxTemplate.CheckTemplate(tpl).Count == 0, "no problems");

            var from = TemplateData.From(new Dictionary<string, string> { ["Both"] = "text" },
                new Dictionary<string, ImageSpec> { ["Both"] = new ImageSpec { Bytes = Png, ContentType = "image/png", WidthPt = 1, HeightPt = 1 } });
            AssertTrue(from["Both"] is string, "text wins");
        });
        Check("options are validated; the generated-node budget stops runaway repeats", () =>
        {
            var d = new Document();
            d.AddParagraph("{{#L}}x{{/L}}");
            var tpl = Save(outDir, "fix-options", d);
            var data = new TemplateData().SetItems("L", Enumerable.Range(0, 100).Select(i => "v"));
            AssertThrows<ArgumentOutOfRangeException>(() => FillFile(tpl, "fix-options-a", data, new TemplateOptions { MaxBlockDepth = 0 }), "depth");
            AssertThrows<ArgumentOutOfRangeException>(() => FillFile(tpl, "fix-options-b", data, new TemplateOptions { MissingValue = (TemplateMissingValue)9 }), "enum");
            AssertThrows<DocxTemplateException>(() => FillFile(tpl, "fix-options-c", data, new TemplateOptions { MaxGeneratedElements = 50 }), "node budget");
            AssertThrows<ArgumentException>(() => new TemplateData().Set(" ", "x"), "empty key");
        });
    }

    // ---- second review round -------------------------------------------------------------

    private static void SecondReview(string outDir)
    {
        Check("an unterminated {{ followed by many spaces is scanned in linear time; absurd nesting is refused, not a crash", () =>
        {
            var d = new Document();
            d.AddParagraph("{{" + new string(' ', 20_000) + "x");
            var tpl = Save(outDir, "fix2-regex", d);
            var sw = Stopwatch.StartNew();
            FillFile(tpl, "fix2-regex", new TemplateData());
            DocxTemplate.CheckTemplate(tpl);
            AssertTrue(sw.Elapsed.TotalSeconds < 5, $"took {sw.Elapsed.TotalSeconds:F1}s");

            var deep = new Document();
            deep.AddParagraph(string.Concat(Enumerable.Repeat("{{?a}}", 300_000)) + string.Concat(Enumerable.Repeat("{{/a}}", 300_000)));
            var deepTpl = Save(outDir, "fix2-deep", deep);
            AssertTrue(DocxTemplate.CheckTemplate(deepTpl).Any(x => x.Contains("no partner")), "check reports, no crash");
            DocxTemplate.GetPlaceholders(deepTpl);
            AssertThrows<DocxTemplateException>(() => FillFile(deepTpl, "fix2-deep", new TemplateData().SetFlag("a", true)), "fill: deeper than MaxBlockDepth");

            // Stray opens never closed are left as text in a non-strict fill, however many.
            var stray = new Document();
            for (int i = 0; i < 70; i++) stray.AddParagraph($"note {{{{?x{i}}}}} stray");
            stray.AddParagraph("{{V}}");
            var sp = FillFile(Save(outDir, "fix2-stray", stray), "fix2-stray", new TemplateData().Set("V", "value"));
            AssertTrue(Paras(sp).Last() == "value" && Paras(sp)[0] == "note {{?x0}} stray", Paras(sp)[0]);
            var ok = new Document();
            ok.AddParagraph(string.Concat(Enumerable.Repeat("{{?a}}", 40)) + "x" + string.Concat(Enumerable.Repeat("{{/a}}", 40)));
            AssertTrue(DocxTemplate.GetPlaceholders(Save(outDir, "fix2-40", ok)).Count == 40, "40 levels list fine");
        });
        Check("blocks inside a one-cell table stay inside their repetition (order, conditions, no re-read of values)", () =>
        {
            var d = new Document();
            var t = d.AddTable(1);
            var row = t.AddRow("{{#L}}");
            row.Cells[0].Paragraphs.Add(new Paragraph("{{?X}}{{N}}{{/X}}"));
            row.Cells[0].Paragraphs.Add(new Paragraph("{{/L}}"));
            var data = new TemplateData().Set("Secret", "LEAK");
            data.AddItem("L").Set("N", "item0").SetFlag("X", true);
            data.AddItem("L").Set("N", "item1").SetFlag("X", false);
            data.AddItem("L").Set("N", "{{Secret}}").SetFlag("X", true);
            var p = FillFile(Save(outDir, "fix2-cell", d), "fix2-cell", data);
            var rows = Body(p).Descendants(Wn + "tr").ToList();
            AssertTrue(rows.Count == 1, $"{rows.Count} rows");
            var texts = rows[0].Descendants(Wn + "p").Select(Text).Where(x => x.Length > 0).ToList();
            AssertTrue(texts.SequenceEqual(new[] { "item0", "{{Secret}}" }), string.Join(" / ", texts));

            // Many items inside one cell stay linear.
            var big = new TemplateData();
            for (int i = 0; i < 16_000; i++) big.AddItem("L").Set("N", "n" + i).SetFlag("X", i % 2 == 0);
            var sw = Stopwatch.StartNew();
            FillFile(Save(outDir, "fix2-cell-big", d), "fix2-cell-big", big);
            AssertTrue(sw.Elapsed.TotalSeconds < 10, $"took {sw.Elapsed.TotalSeconds:F1}s");
        });
        Check("a one-cell row repeats only when the block is all it holds (repeated marker text, pictures)", () =>
        {
            var d = new Document();
            var t1 = d.AddTable(1);
            t1.AddRow("{{?U}}!{{/U}} name {{?U}}!{{/U}}");
            var t2 = d.AddTable(2);
            var r2 = t2.AddRow("{{?X}}caption{{/X}}", "");
            r2.Cells[1].Paragraphs[0].Image(new ImageSpec { Bytes = Png, ContentType = "image/png", WidthPt = 5, HeightPt = 5 });
            var p = FillFile(Save(outDir, "fix2-wholerow", d), "fix2-wholerow", new TemplateData().SetFlag("U", false).SetFlag("X", false));
            var body = Body(p);
            AssertTrue(body.Elements(Wn + "tbl").Count() == 2, "both tables kept");
            AssertTrue(Text(body.Elements(Wn + "tbl").First()) == " name ", Text(body.Elements(Wn + "tbl").First()));
            AssertTrue(body.Descendants(Wn + "drawing").Count() == 1, "picture kept");
        });
        Check("repeat markers in rows wrapped in separate content controls, or one wrapped and one plain, repeat the rows", () =>
        {
            var d = new Document();
            var t = d.AddTable(1);
            t.AddRow("A{{#L}}");
            t.AddRow("{{.}}");
            t.AddRow("B{{/L}}");
            var tpl = Save(outDir, "fix2-sdt-mixed", d, xml =>
            {
                var x = XElement.Parse(xml);
                var first = x.Descendants(Wn + "tr").First();
                first.ReplaceWith(new XElement(Wn + "sdt", new XElement(Wn + "sdtPr", new XElement(Wn + "id", new XAttribute(Wn + "val", "5"))), new XElement(Wn + "sdtContent", new XElement(first))));
                return x.ToString(SaveOptions.DisableFormatting);
            });
            var p = FillFile(tpl, "fix2-sdt-mixed", new TemplateData().SetItems("L", new[] { "1", "2" }));
            var rows = Body(p).Descendants(Wn + "tr").Select(Text).ToList();
            AssertTrue(rows.SequenceEqual(new[] { "A", "1", "B", "A", "2", "B" }), string.Join("/", rows));
            var sdtIds = Body(p).Descendants(Wn + "sdtPr").Elements(Wn + "id").Count();
            AssertTrue(sdtIds == 1, "a repeated content-control id is dropped in later copies");
        });
        Check("a marker paragraph holding a page break keeps it; invalid XML characters in values are reported", () =>
        {
            var d = new Document();
            d.AddParagraph("{{#L}}");
            d.AddParagraph("{{.}}");
            d.AddParagraph().Break(BreakType.Page).Text("{{/L}}");
            var tpl = Save(outDir, "fix2-break", d);
            var p = FillFile(tpl, "fix2-break", new TemplateData().SetItems("L", new[] { "a" }));
            AssertTrue(Body(p).Descendants(Wn + "br").Any(b => (string?)b.Attribute(Wn + "type") == "page"), "page break kept");
            try
            {
                FillFile(tpl, "fix2-badchar", new TemplateData().SetItems("L", new[] { "a\u000Bb" }));
                AssertTrue(false, "no exception");
            }
            catch (DocxTemplateException ex)
            {
                AssertTrue(ex.Problems.Single().Contains("U+000B"), ex.Problems.Single());
            }
            try
            {
                FillFile(tpl, "fix2-badsurrogate", new TemplateData().SetItems("L", new[] { "a\uD800b" }));
                AssertTrue(false, "no exception");
            }
            catch (DocxTemplateException ex)
            {
                AssertTrue(ex.Problems.Single().Contains("U+D800"), ex.Problems.Single());
            }
        });
    }
}
