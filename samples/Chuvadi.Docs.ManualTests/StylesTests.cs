using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Phase 3 — styles: built-in set, custom paragraph/character styles, document defaults,
/// styles.xml round-trip through Document.Load, and EFFECTIVE formatting resolution
/// (basedOn chains, toggle XOR between paragraph and character styles, direct formatting
/// wins, tab/border accumulation), robust against cycles and missing styles.
/// </summary>
public static class StylesTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly string[] StyleChildOrder =
    {
        "name", "aliases", "basedOn", "next", "link", "autoRedefine", "hidden", "uiPriority",
        "semiHidden", "unhideWhenUsed", "qFormat", "locked", "personal", "personalCompose",
        "personalReply", "rsid", "pPr", "rPr", "tblPr", "trPr", "tcPr", "tblStylePr",
    };

    public static void Run(string outDir)
    {
        BuiltIns(outDir);
        CustomStylesWritten(outDir);
        RoundTrip(outDir);
        EffectiveRunFormat();
        EffectiveParagraphFormat();
        RobustChains(outDir);
        CustomDefaults(outDir);
        ReviewRegressions(outDir);
    }

    /// <summary>Regressions from the independent review of the styles phase.</summary>
    private static void ReviewRegressions(string outDir)
    {
        var basePath = Path.Combine(outDir, "styles-rev-base.docx");
        var bd = new Document();
        bd.AddParagraph("base");
        bd.SaveTo(basePath);

        Check("duplicate style ids: first wins, linear time (20,000 duplicates load quickly)", () =>
        {
            var path = Path.Combine(outDir, "styles-dupes.docx");
            File.Copy(basePath, path, true);
            var sb = new StringBuilder();
            sb.Append("<w:styles xmlns:w=\"" + Wn.NamespaceName + "\"><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Dup\"><w:name w:val=\"First\"/></w:style>");
            for (int i = 0; i < 20_000; i++) sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Dup\"><w:name w:val=\"Later\"/></w:style>");
            sb.Append("</w:styles>");
            ReplacePart(path, "word/styles.xml", sb.ToString());
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var loaded = Document.Load(path);
            sw.Stop();
            AssertTrue(loaded.Styles["Dup"].Name == "First" && loaded.Styles.Count == 2, "first definition wins");
            AssertTrue(sw.ElapsedMilliseconds < 3_000, $"took {sw.ElapsedMilliseconds} ms");
        });

        Check("loaded file lacking built-ins: referenced Heading/Hyperlink/ListParagraph styles are written on save", () =>
        {
            var path = Path.Combine(outDir, "styles-minimal.docx");
            File.Copy(basePath, path, true);
            ReplacePart(path, "word/styles.xml",
                "<w:styles xmlns:w=\"" + Wn.NamespaceName + "\"><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style></w:styles>");
            var loaded = Document.Load(path);
            loaded.AddHeading("Added later", 2);
            loaded.AddParagraph().Hyperlink("link", "https://example.com/");
            loaded.AddBullet("item");
            var re = Path.Combine(outDir, "styles-minimal-resaved.docx");
            loaded.SaveTo(re);
            var styles = XElement.Parse(ReadPartXml(re, "word/styles.xml"));
            var ids = styles.Elements(Wn + "style").Select(e => e.Attribute(Wn + "styleId")!.Value).ToList();
            foreach (var id in new[] { "Heading2", "Hyperlink", "ListParagraph" })
                AssertTrue(ids.Contains(id), $"{id} not written");
            AssertTrue(ids.Count(i => i == "Normal") == 1, "Normal duplicated");
            AssertTrue(!loaded.Styles.Contains("Heading2"), "the document's own collection is not changed by saving");
        });

        Check("theme part (theme fonts/colours) is carried through load → save byte-for-byte", () =>
        {
            var path = Path.Combine(outDir, "styles-theme.docx");
            File.Copy(basePath, path, true);
            var theme = "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office Theme\"><a:themeElements><a:fontScheme name=\"Office\"><a:majorFont><a:latin typeface=\"Cambria\"/></a:majorFont><a:minorFont><a:latin typeface=\"Calibri\"/></a:minorFont></a:fontScheme></a:themeElements></a:theme>";
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                using (var w = new StreamWriter(zip.CreateEntry("word/theme/theme1.xml").Open(), new UTF8Encoding(false))) w.Write(theme);
                Edit(zip, "word/_rels/document.xml.rels", x => x.Replace("</Relationships>",
                    "<Relationship Id=\"rIdT\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"theme/theme1.xml\"/></Relationships>"));
                Edit(zip, "[Content_Types].xml", x => x.Replace("</Types>",
                    "<Override PartName=\"/word/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/></Types>"));
            }
            var loaded = Document.Load(path);
            AssertTrue(loaded.ThemeXml is not null && Encoding.UTF8.GetString(loaded.ThemeXml) == theme, "theme loaded");
            var re = Path.Combine(outDir, "styles-theme-resaved.docx");
            loaded.SaveTo(re);
            AssertTrue(ReadPartXml(re, "word/theme/theme1.xml") == theme, "theme written back");
            AssertContains(ReadPartXml(re, "word/_rels/document.xml.rels"), "relationships/theme", "theme relationship");
        });

        Check("table-style tblPr/tblStylePr survive load → save; a style numPr pointing at no list is dropped (Word ignores it too)", () =>
        {
            var path = Path.Combine(outDir, "styles-table-num.docx");
            File.Copy(basePath, path, true);
            ReplacePart(path, "word/styles.xml",
                "<w:styles xmlns:w=\"" + Wn.NamespaceName + "\">" +
                "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
                "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/>" +
                "<w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:left w:w=\"108\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>" +
                "<w:style w:type=\"table\" w:styleId=\"Grid\"><w:name w:val=\"Grid\"/><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/></w:tblBorders></w:tblPr>" +
                "<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/></w:rPr></w:tblStylePr></w:style>" +
                "<w:style w:type=\"paragraph\" w:styleId=\"ListBullet\"><w:name w:val=\"List Bullet\"/><w:basedOn w:val=\"Normal\"/>" +
                "<w:pPr><w:numPr><w:numId w:val=\"3\"/></w:numPr><w:contextualSpacing/></w:pPr></w:style>" +
                "</w:styles>");
            var re = Path.Combine(outDir, "styles-table-num-resaved.docx");
            Document.Load(path).SaveTo(re);
            var st = XElement.Parse(ReadPartXml(re, "word/styles.xml"));
            XElement Style(string id) => st.Elements(Wn + "style").Single(e => e.Attribute(Wn + "styleId")!.Value == id);
            AssertTrue(Style("TableNormal").Descendants(Wn + "tblCellMar").Any(), "TableNormal tblCellMar lost");
            AssertTrue(Style("Grid").Element(Wn + "tblStylePr")?.Attribute(Wn + "type")?.Value == "firstRow", "tblStylePr lost");
            var lb = Style("ListBullet").Element(Wn + "pPr")!;
            AssertTrue(lb.Element(Wn + "numPr") is null && lb.Element(Wn + "contextualSpacing") is not null, "dangling numPr dropped, rest kept");
            // Linked style numbering (with a real numbering part) is covered by the Lists group.
        });

        Check("resolver: a direct first-line indent replaces an inherited hanging indent (and spacing vs auto-spacing)", () =>
        {
            var d = new Document();
            var st = d.Styles.AddParagraphStyle("Hang");
            st.ParagraphFormat.HangingIndentPt = 18;
            st.ParagraphFormat.SpaceBeforeAuto = true;
            var p = d.AddParagraph("x");
            p.StyleId = "Hang";
            p.Format.FirstLineIndentPt = 12;
            p.Format.SpaceBeforePt = 4;
            var f = d.GetEffectiveParagraphFormat(p);
            AssertTrue(f.FirstLineIndentPt == 12 && f.HangingIndentPt is null, $"firstLine {f.FirstLineIndentPt} hanging {f.HangingIndentPt}");
            AssertTrue(f.SpaceBeforePt == 4 && f.SpaceBeforeAuto is null, "spacing replaces auto-spacing");
        });

        Check("explicit color auto overrides an inherited colour, and survives load → save", () =>
        {
            var d = new Document();
            var red = d.Styles.AddParagraphStyle("Red");
            red.RunFormat = new TextFormat { ColorHex = "FF0000" };
            var p = d.AddParagraph().Text("auto", new TextFormat { ColorHex = "auto" });
            p.StyleId = "Red";
            AssertTrue(d.GetEffectiveRunFormat(p, p.Runs[0]).ColorHex == "auto", "auto should win");
            var path = Path.Combine(outDir, "styles-auto.docx");
            d.SaveTo(path);
            var lp = Document.Load(path).Blocks.OfType<Paragraph>().Single(x => x.GetText() == "auto");
            AssertTrue(lp.Runs[0].Format.ColorHex == "auto", "auto lost on load");
        });

        Check("a pStyle naming a character style falls back to the default paragraph style", () =>
        {
            var d = new Document();
            d.Styles["Normal"].RunFormat = new TextFormat { SizePt = 13 };
            var p = new Paragraph("x") { StyleId = "Hyperlink" };
            AssertTrue(d.GetEffectiveRunFormat(p, p.Runs[0]).SizePt == 13, "fallback to Normal");
        });
    }

    private static void ReplacePart(string path, string part, string content)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);
        zip.GetEntry(part)?.Delete();
        using var w = new StreamWriter(zip.CreateEntry(part).Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static void Edit(ZipArchive zip, string part, Func<string, string> edit)
    {
        var e = zip.GetEntry(part)!;
        string text;
        using (var r = new StreamReader(e.Open())) text = r.ReadToEnd();
        e.Delete();
        using var w = new StreamWriter(zip.CreateEntry(part).Open(), new UTF8Encoding(false));
        w.Write(edit(text));
    }

    private static Document Sample()
    {
        var doc = new Document();
        var note = doc.Styles.AddParagraphStyle("ClinicalNote", "Clinical Note");
        note.ParagraphFormat.SpaceAfterPt = 4;
        note.ParagraphFormat.IndentLeftPt = 18;
        note.RunFormat = new TextFormat { Italic = true, ColorHex = "404040" };
        note.Aliases = "Note";
        var drug = doc.Styles.AddCharacterStyle("DrugName", "Drug Name");
        drug.RunFormat = new TextFormat { Bold = true, ColorHex = "1F3864" };
        doc.AddHeading("Medication plan", 1);
        doc.AddParagraph("Patient tolerated the procedure well.").StyleId = "ClinicalNote";
        doc.AddParagraph().Text("Give ").StyledText("Clopidogrel", "DrugName").Text(" 75 mg daily.");
        return doc;
    }

    private static void BuiltIns(string outDir)
    {
        var path = Path.Combine(outDir, "styles-builtin.docx");
        var doc = new Document();
        doc.AddHeading("H", 1);
        doc.SaveTo(path);

        Check("new documents carry the built-in styles (Normal default, Title, Heading 1–9, Quote, List Paragraph, Hyperlink)", () =>
        {
            var ids = doc.Styles.Select(s => s.StyleId).ToList();
            foreach (var id in new[] { "Normal", "Title", "Quote", "ListParagraph", "Hyperlink" }.Concat(Enumerable.Range(1, 9).Select(i => $"Heading{i}")))
                AssertTrue(ids.Contains(id), $"missing {id}");
            AssertTrue(doc.Styles.GetDefault(StyleType.Paragraph)?.StyleId == "Normal", "Normal is default");
            AssertTrue(doc.Styles["Hyperlink"].Type == StyleType.Character, "Hyperlink is a character style");
        });
        Check("built-in look unchanged; headings now based on Normal, next Normal, keep with next / lines together", () =>
        {
            var styles = XElement.Parse(ReadPartXml(path, "word/styles.xml"));
            var h1 = styles.Elements(Wn + "style").Single(s => s.Attribute(Wn + "styleId")!.Value == "Heading1");
            string V(string el) => h1.Descendants(Wn + el).First().Attribute(Wn + "val")?.Value ?? "<present>";
            AssertTrue(V("basedOn") == "Normal" && V("next") == "Normal", "basedOn / next");
            AssertTrue(V("sz") == "32" && V("color") == "2E74B5" && h1.Descendants(Wn + "b").Any(), "Heading1 look (16pt bold blue)");
            AssertTrue(h1.Descendants(Wn + "keepNext").Any() && h1.Descendants(Wn + "keepLines").Any(), "keep with next / lines");
            AssertTrue(h1.Descendants(Wn + "bCs").Any() && V("szCs") == "32", "headings bold/sized for complex scripts too");
            foreach (var st in styles.Elements(Wn + "style"))
            {
                var names = st.Elements().Select(e => e.Name.LocalName).ToList();
                var idx = names.Select(n => Array.IndexOf(StyleChildOrder, n)).ToList();
                AssertTrue(idx.All(i => i >= 0) && idx.Zip(idx.Skip(1), (a, b) => a < b).All(x => x),
                    $"CT_Style order in {st.Attribute(Wn + "styleId")!.Value}: [{string.Join(",", names)}]");
            }
        });
    }

    private static void CustomStylesWritten(string outDir)
    {
        var path = Path.Combine(outDir, "styles-custom.docx");
        Sample().SaveTo(path);

        Check("custom paragraph + character styles: definitions in styles.xml, pStyle / rStyle in the body", () =>
        {
            var styles = XElement.Parse(ReadPartXml(path, "word/styles.xml"));
            var note = styles.Elements(Wn + "style").Single(s => s.Attribute(Wn + "styleId")!.Value == "ClinicalNote");
            AssertTrue(note.Attribute(Wn + "type")!.Value == "paragraph" && note.Attribute(Wn + "customStyle")!.Value == "1", "note type/custom");
            AssertTrue(note.Element(Wn + "name")!.Attribute(Wn + "val")!.Value == "Clinical Note"
                && note.Element(Wn + "aliases")!.Attribute(Wn + "val")!.Value == "Note"
                && note.Element(Wn + "basedOn")!.Attribute(Wn + "val")!.Value == "Normal"
                && note.Element(Wn + "next")!.Attribute(Wn + "val")!.Value == "ClinicalNote", "note links");
            AssertTrue(note.Element(Wn + "pPr")!.Element(Wn + "ind")!.Attribute(Wn + "left")!.Value == "360", "note pPr");
            AssertTrue(note.Element(Wn + "rPr")!.Element(Wn + "i") is not null, "note rPr");
            var drug = styles.Elements(Wn + "style").Single(s => s.Attribute(Wn + "styleId")!.Value == "DrugName");
            AssertTrue(drug.Attribute(Wn + "type")!.Value == "character" && drug.Element(Wn + "pPr") is null, "drug is character-only");
            var body = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            AssertTrue(body.Descendants(Wn + "pStyle").Any(e => e.Attribute(Wn + "val")!.Value == "ClinicalNote"), "pStyle in body");
            var rStyle = body.Descendants(Wn + "rStyle").Single(e => e.Attribute(Wn + "val")!.Value == "DrugName");
            AssertTrue(rStyle.Parent!.Elements().First() == rStyle, "rStyle is the first rPr child");
        });
        Check("StyleId: built-in ids map to the enum; custom ids read back as Normal enum", () =>
        {
            var p = new Paragraph("x") { StyleId = "Heading3" };
            AssertTrue(p.Style == ParagraphStyle.Heading3, "built-in id → enum");
            p.StyleId = "ClinicalNote";
            AssertTrue(p.Style == ParagraphStyle.Normal && p.StyleId == "ClinicalNote", "custom id");
            p.Style = ParagraphStyle.Quote;
            AssertTrue(p.StyleId == "Quote", "setting the enum clears the custom id");
            AssertThrows<ArgumentException>(() => doc_AddDuplicate(), "duplicate style id rejected");
        });
    }

    private static void doc_AddDuplicate()
    {
        var d = new Document();
        d.Styles.AddParagraphStyle("Heading1");
    }

    private static void RoundTrip(string outDir)
    {
        var a = Path.Combine(outDir, "styles-rt-a.docx");
        var doc = Sample();
        doc.Defaults.ParagraphFormat.SpaceAfterPt = 8;
        doc.Defaults.ParagraphFormat.LineSpacing = LineSpacing.Multiple(1.08);
        var hidden = doc.Styles.Add(new StyleDefinition("Internal", StyleType.Paragraph)
        {
            Name = "Internal", BasedOn = "Normal", Hidden = true, SemiHidden = true, UnhideWhenUsed = true,
            Locked = true, AutoRedefine = true, UiPriority = 7, Link = "InternalChar", IsCustom = true,
        });
        doc.SaveTo(a);

        Check("Document.Load reads styles and defaults back; load → save reproduces styles.xml AND document.xml exactly", () =>
        {
            var loaded = Document.Load(a);
            AssertTrue(loaded.Styles.Count == doc.Styles.Count, $"style count {loaded.Styles.Count} vs {doc.Styles.Count}");
            var note = loaded.Styles["ClinicalNote"];
            AssertTrue(note.IsCustom && note.BasedOn == "Normal" && note.ParagraphFormat.IndentLeftPt == 18 && note.RunFormat!.Italic, "custom style loaded");
            var intl = loaded.Styles["Internal"];
            AssertTrue(intl.Hidden && intl.SemiHidden && intl.UnhideWhenUsed && intl.Locked && intl.AutoRedefine
                && intl.UiPriority == 7 && intl.Link == "InternalChar", "flags loaded");
            AssertTrue(loaded.Defaults.ParagraphFormat.SpaceAfterPt == 8 && loaded.Defaults.RunFormat!.AsciiFont == "Calibri", "defaults loaded");
            var p = (Paragraph)loaded.Blocks[1];
            AssertTrue(p.StyleId == "ClinicalNote", "custom pStyle preserved on load");
            AssertTrue(((Paragraph)loaded.Blocks[2]).Runs[1].CharacterStyleId == "DrugName", "rStyle preserved on load");
            var b = Path.Combine(outDir, "styles-rt-b.docx");
            loaded.SaveTo(b);
            AssertTrue(ReadPartXml(a, "word/styles.xml") == ReadPartXml(b, "word/styles.xml"), "styles.xml differs");
            AssertTrue(ReadPartXml(a, "word/document.xml") == ReadPartXml(b, "word/document.xml"), "document.xml differs");
        });
    }

    private static void EffectiveRunFormat()
    {
        var doc = new Document();
        var baseStyle = doc.Styles.AddParagraphStyle("Base");
        baseStyle.RunFormat = new TextFormat { SizePt = 14, Font = "Georgia" };
        var derived = doc.Styles.AddParagraphStyle("Derived", basedOn: "Base");
        derived.RunFormat = new TextFormat { ColorHex = "C00000" };
        var emph = doc.Styles.AddCharacterStyle("Emph");
        emph.RunFormat = new TextFormat { Bold = true, Italic = true, SizePt = 9 };

        var h1 = doc.AddHeading("x", 1);
        var inHeading = new Run("a", TextFormat.None) { CharacterStyleId = "Emph" };
        var direct = new Run("b", TextFormat.BoldText) { CharacterStyleId = "Emph" };
        var plain = new Run("c", TextFormat.None);
        var d = doc.AddParagraph("y");
        d.StyleId = "Derived";

        Check("effective: defaults → paragraph style chain (basedOn, root first) → direct", () =>
        {
            var f = doc.GetEffectiveRunFormat(d, d.Runs[0]);
            AssertTrue(f.SizePt == 14 && f.Font == "Georgia" && f.ColorHex == "C00000", $"chain: {f.SizePt} {f.Font} {f.ColorHex}");
            AssertTrue(f.ComplexScriptFont == "Calibri" && f.SizeComplexScriptPt == 14, "cs font from defaults; cs size mirrored from the style");
            var n = doc.GetEffectiveRunFormat(new Paragraph("z"), new Run("z", TextFormat.None));
            AssertTrue(n.Font == "Calibri" && n.SizePt == 11 && !n.Bold, "Normal = document defaults");
        });
        Check("effective: toggles set by paragraph AND character style XOR (bold heading + bold char style = not bold)", () =>
        {
            var f = doc.GetEffectiveRunFormat(h1, inHeading);
            AssertTrue(!f.Bold && f.ExplicitlyOff.HasFlag(RunToggles.Bold), "bold XOR bold should be off");
            AssertTrue(f.Italic, "italic only from the character style → on");
            AssertTrue(f.SizePt == 9 && f.ColorHex == "2E74B5", "non-toggles: character style wins size; colour from heading");
            AssertTrue(f.BoldComplexScript == false, "complex-script bold XORs too");
        });
        Check("effective: direct formatting overrides the toggle absolutely", () =>
        {
            AssertTrue(doc.GetEffectiveRunFormat(h1, direct).Bold, "direct bold wins");
            AssertTrue(doc.GetEffectiveRunFormat(h1, plain).Bold, "heading bold applies to plain runs");
        });
        Check("effective: hyperlink runs resolve through the Hyperlink character style", () =>
        {
            var p = new Paragraph().Hyperlink("site", "https://example.com/");
            var f = doc.GetEffectiveRunFormat(p, p.Runs[0]);
            AssertTrue(f.ColorHex == "0563C1" && f.Underline, "hyperlink colour/underline");
        });
    }

    private static void EffectiveParagraphFormat()
    {
        var doc = new Document();
        doc.Defaults.ParagraphFormat.SpaceAfterPt = 8;
        var s = doc.Styles.AddParagraphStyle("Tabbed");
        s.ParagraphFormat.TabStops.Add(new TabStop(100));
        s.ParagraphFormat.TabStops.Add(new TabStop(200, TabAlignment.Right));
        s.ParagraphFormat.Borders = new ParagraphBorders { Top = BorderSpec.Single(1), Bottom = BorderSpec.Single(1) };
        s.ParagraphFormat.Alignment = ParagraphAlignment.Center;
        var p = doc.AddParagraph("x");
        p.StyleId = "Tabbed";
        p.Format.TabStops.Add(new TabStop(100, TabAlignment.Clear));
        p.Format.TabStops.Add(new TabStop(300, TabAlignment.Decimal));
        p.Format.Borders = new ParagraphBorders { Bottom = BorderSpec.None };
        p.Format.SpaceAfterPt = 2;

        Check("effective paragraph: defaults → style → direct; tabs accumulate with Clear; borders merge per side", () =>
        {
            var f = doc.GetEffectiveParagraphFormat(p);
            AssertTrue(f.SpaceAfterPt == 2 && f.Alignment == ParagraphAlignment.Center, "override order");
            var tabs = string.Join(",", f.TabStops.Select(t => $"{t.PositionPt}{t.Alignment.ToString()[0]}"));
            AssertTrue(tabs == "200R,300D", $"tabs {tabs}");
            AssertTrue(f.Borders!.Top!.Style == BorderStyle.Single && f.Borders.Bottom!.Style == BorderStyle.None, "per-side borders");
            AssertTrue(doc.GetEffectiveParagraphFormat(new Paragraph("n")).SpaceAfterPt == 8, "defaults alone");
            f.TabStops.Clear();
            AssertTrue(p.Format.TabStops.Count == 2, "result is independent");
        });
    }

    private static void RobustChains(string outDir)
    {
        var doc = new Document();
        doc.Styles.Add(new StyleDefinition("A", StyleType.Paragraph) { BasedOn = "B", RunFormat = new TextFormat { SizePt = 20 } });
        doc.Styles.Add(new StyleDefinition("B", StyleType.Paragraph) { BasedOn = "A", RunFormat = new TextFormat { ColorHex = "FF0000" } });
        var p = new Paragraph("x") { StyleId = "A" };
        var missing = new Paragraph("y") { StyleId = "NoSuchStyle" };

        Check("basedOn cycle and missing styles: no hang, sensible fallback", () =>
        {
            var f = doc.GetEffectiveRunFormat(p, p.Runs[0]);
            AssertTrue(f.SizePt == 20 && f.ColorHex == "FF0000", "cycle resolved once each");
            var m = doc.GetEffectiveRunFormat(missing, missing.Runs[0]);
            AssertTrue(m.SizePt == 11, "missing style falls back to Normal");
        });

        var path = Path.Combine(outDir, "styles-hostile.docx");
        doc.AddParagraph(p);
        doc.SaveTo(path);
        Check("loading a styles.xml with a basedOn cycle completes and resolves", () =>
        {
            var loaded = Document.Load(path);
            var lp = (Paragraph)loaded.Blocks[0];
            AssertTrue(loaded.GetEffectiveRunFormat(lp, lp.Runs[0]).SizePt == 20, "resolved after load");
        });
    }

    private static void CustomDefaults(string outDir)
    {
        var path = Path.Combine(outDir, "styles-defaults.docx");
        var doc = new Document();
        doc.Defaults.RunFormat = new TextFormat { FontForAllScripts = "Nirmala UI", SizePt = 10.5, Language = "en-IN", ComplexScriptLanguage = "ta-IN" };
        doc.AddParagraph("வணக்கம்");
        doc.SaveTo(path);

        Check("custom document defaults are written to docDefaults and drive effective formatting", () =>
        {
            var styles = XElement.Parse(ReadPartXml(path, "word/styles.xml"));
            var rPr = styles.Element(Wn + "docDefaults")!.Element(Wn + "rPrDefault")!.Element(Wn + "rPr")!;
            AssertTrue(rPr.Element(Wn + "rFonts")!.Attribute(Wn + "cs")!.Value == "Nirmala UI", "cs font default");
            AssertTrue(rPr.Element(Wn + "sz")!.Attribute(Wn + "val")!.Value == "21" && rPr.Element(Wn + "szCs")!.Attribute(Wn + "val")!.Value == "21", "size");
            AssertTrue(rPr.Element(Wn + "lang")!.Attribute(Wn + "bidi")!.Value == "ta-IN", "lang");
            var loaded = Document.Load(path);
            var lp = (Paragraph)loaded.Blocks[0];
            var f = loaded.GetEffectiveRunFormat(lp, lp.Runs[0]);
            AssertTrue(f.ComplexScriptFont == "Nirmala UI" && f.SizeComplexScriptPt == 10.5, "effective after load");
        });
    }
}
