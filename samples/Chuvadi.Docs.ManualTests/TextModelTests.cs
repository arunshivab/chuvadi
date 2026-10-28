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
/// Phase 1 — the run (character) model: every TextFormat property written in schema order,
/// read back by Document.Load, and round-tripped exactly; complex-script (Indic) font slots,
/// bold and size; line/page/column/text-wrapping breaks and tabs; formatted hyperlinks; and
/// multi-line template values.
/// </summary>
public static class TextModelTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>CT_RPr child order (subset Chuvadi writes), per ECMA-376 Part 1 §17.3.2.</summary>
    private static readonly string[] RprOrder =
    {
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike",
        "outline", "shadow", "emboss", "imprint", "noProof", "snapToGrid", "vanish", "webHidden",
        "color", "spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect",
        "bdr", "shd", "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout",
    };

    /// <summary>A format with every property set to a non-default value.</summary>
    public static TextFormat Everything => new()
    {
        Bold = true,
        Italic = true,
        Strikethrough = true,
        AsciiFont = "Arial",
        HighAnsiFont = "Arial Narrow",
        EastAsiaFont = "MS Mincho",
        ComplexScriptFont = "Nirmala UI",
        FontHint = FontHint.ComplexScript,
        AsciiThemeFont = ThemeFont.MinorAscii,
        HighAnsiThemeFont = ThemeFont.MinorHAnsi,
        EastAsiaThemeFont = ThemeFont.MinorEastAsia,
        ComplexScriptThemeFont = ThemeFont.MinorBidi,
        SizePt = 13,
        SizeComplexScriptPt = 12.5,
        BoldComplexScript = false,
        ItalicComplexScript = true,
        ComplexScript = true,
        RightToLeft = false,
        Language = "en-IN",
        EastAsiaLanguage = "ja-JP",
        ComplexScriptLanguage = "ta-IN",
        UnderlineStyle = UnderlineStyle.DotDash,
        UnderlineColorHex = "00B050",
        DoubleStrikethrough = false,
        Caps = true,
        SmallCaps = false,
        Hidden = false,
        Outline = true,
        Shadow = true,
        Emboss = false,
        Imprint = false,
        NoProof = true,
        VerticalPosition = VerticalTextPosition.Superscript,
        BaselineShiftPt = -1.5,
        CharacterSpacingPt = 0.75,
        HorizontalScalePercent = 90,
        KerningMinSizePt = 8,
        ColorHex = "1F3864",
        ThemeColor = ThemeColor.Accent1,
        ThemeColorTint = 0x99,
        ThemeColorShade = 0xBF,
        Highlight = "yellow",
        ShadingHex = "FFF2CC",
    };

    public static void Run(string outDir)
    {
        AllPropertiesWrittenInSchemaOrder(outDir);
        LoadReadsEveryProperty(outDir);
        ExactRoundTrip(outDir);
        ComplexScriptBehaviour(outDir);
        BreaksAndTabs(outDir);
        FormattedHyperlink(outDir);
        TemplateMultiLineValues(outDir);
        ReviewRegressions(outDir);
    }

    /// <summary>Regressions found in the independent review of Phase 1.</summary>
    private static void ReviewRegressions(string outDir)
    {
        var path = Path.Combine(outDir, "text-review.docx");
        var doc = new Document();
        doc.AddParagraph("base");
        doc.SaveTo(path);
        const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
        {
            var e = zip.GetEntry("word/document.xml")!;
            string xml;
            using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
            e.Delete();
            // 1) A Word-style text box: AlternateContent with Choice (DrawingML) AND Fallback (VML) copies.
            // 2) A moved paragraph: moveFrom (old place) + moveTo (new place).
            // 3) Explicit-off toggles.
            xml = xml.Replace("<w:body>", "<w:body>" +
                "<w:p><w:r><mc:AlternateContent xmlns:mc=\"" + Mc + "\">" +
                "<mc:Choice Requires=\"wps\"><w:drawing><wps:txbx xmlns:wps=\"" + Wps + "\"><w:txbxContent><w:p><w:r><w:t>BOX</w:t></w:r></w:p></w:txbxContent></wps:txbx></w:drawing></mc:Choice>" +
                "<mc:Fallback><w:pict><w:txbxContent><w:p><w:r><w:t>BOX</w:t></w:r></w:p></w:txbxContent></w:pict></mc:Fallback>" +
                "</mc:AlternateContent></w:r></w:p>" +
                "<w:p><w:moveFrom w:id=\"1\" w:author=\"a\" w:date=\"2026-10-28T00:00:00Z\"><w:r><w:t>MOVED</w:t></w:r></w:moveFrom></w:p>" +
                "<w:p><w:moveTo w:id=\"2\" w:author=\"a\" w:date=\"2026-10-28T00:00:00Z\"><w:r><w:t>MOVED</w:t></w:r></w:moveTo></w:p>" +
                "<w:p><w:r><w:rPr><w:b w:val=\"0\"/><w:i w:val=\"false\"/><w:strike w:val=\"off\"/><w:highlight w:val=\"none\"/></w:rPr><w:t>offs</w:t></w:r></w:p>");
            using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            w.Write(xml);
        }

        Check("text box written as AlternateContent is read once, not twice (loader and reader)", () =>
        {
            var p0 = ((Paragraph)Document.Load(path).Blocks[0]).GetText();
            AssertTrue(p0 == "BOX", $"loader text was '{p0}'");
            using var r = DocxReader.Open(path);
            var t = r.Paragraphs().First().Text;
            AssertTrue(t == "BOX", $"reader text was '{t}'");
        });
        Check("moved text appears only at its new place (moveFrom skipped)", () =>
        {
            var text = Document.Load(path).GetText();
            AssertTrue(text.Split("MOVED").Length - 1 == 1, $"MOVED appears {text.Split("MOVED").Length - 1} times");
            using var r = DocxReader.Open(path);
            var rt = r.ExtractText();
            AssertTrue(rt.Split("MOVED").Length - 1 == 1, "reader shows moved text twice");
        });
        Check("explicit-off b/i/strike and highlight=none survive load → save", () =>
        {
            var loaded = Document.Load(path);
            var f = loaded.Blocks.OfType<Paragraph>().First(p => p.GetText() == "offs").Runs[0].Format;
            AssertTrue(f.ExplicitlyOff == (RunToggles.Bold | RunToggles.Italic | RunToggles.Strikethrough), $"flags {f.ExplicitlyOff}");
            AssertTrue(f.Highlight == "none", "highlight none lost");
            var resaved = Path.Combine(outDir, "text-review-resaved.docx");
            loaded.SaveTo(resaved);
            var rPr = BodyXml(resaved).Descendants(Wn + "r").First(r => r.Value == "offs").Element(Wn + "rPr")!;
            foreach (var n in new[] { "b", "i", "strike" })
                AssertTrue(rPr.Element(Wn + n)?.Attribute(Wn + "val")?.Value == "0", $"{n} val=0 missing: {rPr}");
            AssertTrue(rPr.Element(Wn + "highlight")?.Attribute(Wn + "val")?.Value == "none", "highlight none not re-written");
        });
    }

    private static void AllPropertiesWrittenInSchemaOrder(string outDir)
    {
        var path = Path.Combine(outDir, "text-all.docx");
        var doc = new Document();
        doc.AddParagraph().Text("everything", Everything);
        doc.AddParagraph().Text("legacy", new TextFormat { Underline = true, Highlight = "green", SizePt = 9, Font = "Calibri" });
        doc.SaveTo(path);
        var body = BodyXml(path);

        Check("rPr children follow the CT_RPr schema order (incl. highlight before u)", () =>
        {
            foreach (var rPr in body.Descendants(Wn + "rPr"))
            {
                var names = rPr.Elements().Select(e => e.Name.LocalName).ToList();
                var idx = names.Select(n => Array.IndexOf(RprOrder, n)).ToList();
                AssertTrue(idx.All(i => i >= 0), $"unknown rPr child in [{string.Join(",", names)}]");
                for (int i = 1; i < idx.Count; i++)
                    AssertTrue(idx[i] > idx[i - 1], $"out of order: [{string.Join(",", names)}]");
            }
        });

        Check("every property emitted with the right attributes", () =>
        {
            var rPr = body.Descendants(Wn + "r").First().Element(Wn + "rPr")!;
            string A(string el, string attr) => rPr.Element(Wn + el)?.Attribute(Wn + attr)?.Value ?? "<missing>";
            AssertTrue(A("rFonts", "ascii") == "Arial" && A("rFonts", "hAnsi") == "Arial Narrow"
                && A("rFonts", "eastAsia") == "MS Mincho" && A("rFonts", "cs") == "Nirmala UI"
                && A("rFonts", "hint") == "cs" && A("rFonts", "cstheme") == "minorBidi", "rFonts");
            AssertTrue(rPr.Element(Wn + "b") is not null && A("bCs", "val") == "0", "b / bCs override");
            AssertTrue(rPr.Element(Wn + "i") is not null && rPr.Element(Wn + "iCs") is not null, "i / iCs");
            AssertTrue(A("sz", "val") == "26" && A("szCs", "val") == "25", "sz / szCs half-points");
            AssertTrue(A("u", "val") == "dotDash" && A("u", "color") == "00B050", "underline");
            AssertTrue(A("color", "val") == "1F3864" && A("color", "themeColor") == "accent1"
                && A("color", "themeTint") == "99" && A("color", "themeShade") == "BF", "color");
            AssertTrue(A("spacing", "val") == "15" && A("w", "val") == "90" && A("kern", "val") == "16"
                && A("position", "val") == "-3", "spacing / scale / kern / position");
            AssertTrue(A("shd", "fill") == "FFF2CC" && A("vertAlign", "val") == "superscript", "shd / vertAlign");
            AssertTrue(A("lang", "val") == "en-IN" && A("lang", "eastAsia") == "ja-JP" && A("lang", "bidi") == "ta-IN", "lang");
            AssertTrue(A("caps", "val") == "<missing>" && A("smallCaps", "val") == "0" && A("dstrike", "val") == "0", "tri-state toggles");
            AssertTrue(A("rtl", "val") == "0" && rPr.Element(Wn + "cs") is not null, "rtl / cs");
        });
    }

    private static void LoadReadsEveryProperty(string outDir)
    {
        var path = Path.Combine(outDir, "text-load.docx");
        var doc = new Document();
        doc.AddParagraph().Text("everything", Everything);
        doc.SaveTo(path);

        Check("Document.Load reads every run property back", () =>
        {
            var f = ((Paragraph)Document.Load(path).Blocks[0]).Runs[0].Format;
            var e = Everything;
            AssertTrue(f.Bold && f.Italic && f.Strikethrough && !f.Underline, "toggles");
            AssertTrue(f.AsciiFont == e.AsciiFont && f.HighAnsiFont == e.HighAnsiFont && f.EastAsiaFont == e.EastAsiaFont
                && f.ComplexScriptFont == e.ComplexScriptFont && f.FontHint == e.FontHint, "font slots");
            AssertTrue(f.AsciiThemeFont == e.AsciiThemeFont && f.HighAnsiThemeFont == e.HighAnsiThemeFont
                && f.EastAsiaThemeFont == e.EastAsiaThemeFont && f.ComplexScriptThemeFont == e.ComplexScriptThemeFont, "theme fonts");
            AssertTrue(f.SizePt == 13 && f.SizeComplexScriptPt == 12.5 && f.BoldComplexScript == false
                && f.ItalicComplexScript == true && f.ComplexScript == true && f.RightToLeft == false, "complex script");
            AssertTrue(!f.MirrorToComplexScript, "loaded formats do not mirror");
            AssertTrue(f.Language == "en-IN" && f.EastAsiaLanguage == "ja-JP" && f.ComplexScriptLanguage == "ta-IN", "languages");
            AssertTrue(f.UnderlineStyle == UnderlineStyle.DotDash && f.UnderlineColorHex == "00B050", "underline");
            AssertTrue(f.DoubleStrikethrough == false && f.Caps == true && f.SmallCaps == false && f.Hidden == false
                && f.Outline == true && f.Shadow == true && f.Emboss == false && f.Imprint == false && f.NoProof == true, "effects");
            AssertTrue(f.VerticalPosition == VerticalTextPosition.Superscript && f.BaselineShiftPt == -1.5
                && f.CharacterSpacingPt == 0.75 && f.HorizontalScalePercent == 90 && f.KerningMinSizePt == 8, "positioning");
            AssertTrue(f.ColorHex == "1F3864" && f.ThemeColor == ThemeColor.Accent1 && f.ThemeColorTint == 0x99
                && f.ThemeColorShade == 0xBF && f.Highlight == "yellow" && f.ShadingHex == "FFF2CC", "colors");
        });
    }

    private static void ExactRoundTrip(string outDir)
    {
        var a = Path.Combine(outDir, "text-rt-a.docx");
        var doc = new Document();
        doc.AddParagraph().Text("everything ", Everything).Text("plain").Tab().Text("after tab");
        doc.AddParagraph().Text("bold", TextFormat.BoldText).Break().Text("line 2").Break(BreakType.Column).Text("col");
        doc.AddParagraph().Text("H").Text("2", TextFormat.SubscriptText).Text("O and x").Text("2", TextFormat.SuperscriptText);
        doc.AddParagraph().Text("wrap").Break(BreakType.TextWrappingClearAll).Text("cleared").Break(BreakType.Page).Text("next page");
        doc.AddParagraph().Text("hyphen\u2011joined soft\u00ADhyphen");
        doc.SaveTo(a);

        Check("load → save reproduces document.xml exactly", () =>
        {
            var b = Path.Combine(outDir, "text-rt-b.docx");
            Document.Load(a).SaveTo(b);
            var xa = ReadPartXml(a, "word/document.xml");
            var xb = ReadPartXml(b, "word/document.xml");
            AssertTrue(xa == xb, "document.xml differs after round-trip:\n" + FirstDiff(xa, xb));
        });

        Check("b-only file (no bCs/szCs) round-trips without gaining complex-script properties", () =>
        {
            var legacy = Path.Combine(outDir, "text-legacy-b.docx");
            File.Copy(a, legacy, true);
            using (var fs = new FileStream(legacy, FileMode.Open, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                var e = zip.GetEntry("word/document.xml")!;
                string xml;
                using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
                e.Delete();
                xml = xml.Replace("<w:body>", "<w:body><w:p><w:r><w:rPr><w:b /><w:sz w:val=\"28\" /></w:rPr><w:t>legacy</w:t></w:r></w:p>");
                using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
                w.Write(xml);
            }
            var resaved = Path.Combine(outDir, "text-legacy-b-resaved.docx");
            var loaded = Document.Load(legacy);
            var f = ((Paragraph)loaded.Blocks[0]).Runs[0].Format;
            AssertTrue(f.Bold && f.BoldComplexScript is null && f.SizePt == 14 && f.SizeComplexScriptPt == 0, "loaded values");
            loaded.SaveTo(resaved);
            var first = BodyXml(resaved).Descendants(Wn + "rPr").First();
            AssertTrue(first.Element(Wn + "bCs") is null && first.Element(Wn + "szCs") is null,
                "re-save added bCs/szCs: " + first);
        });
    }

    private static void ComplexScriptBehaviour(string outDir)
    {
        var path = Path.Combine(outDir, "text-tamil.docx");
        var doc = new Document();
        var tamil = new TextFormat { FontForAllScripts = "Nirmala UI", SizePt = 14, Bold = true, ComplexScriptLanguage = "ta-IN" };
        doc.AddHeading("தமிழ் — Tamil", 1);
        doc.AddParagraph().Text("வணக்கம், இது ஒரு சோதனை அறிக்கை. ", tamil).Text("Latin in the same format.", tamil);
        doc.AddParagraph().Text("नमस्ते — Devanagari  ", new TextFormat { ComplexScriptFont = "Nirmala UI", ComplexScriptLanguage = "hi-IN" })
            .Text("നമസ്കാരം — Malayalam", new TextFormat { ComplexScriptFont = "Nirmala UI", ComplexScriptLanguage = "ml-IN" });
        doc.SaveTo(path);

        Check("FontForAllScripts fills all four rFonts slots; Bold/SizePt mirror to bCs/szCs", () =>
        {
            var rPr = BodyXml(path).Descendants(Wn + "p").ElementAt(1).Descendants(Wn + "rPr").First();
            var fonts = rPr.Element(Wn + "rFonts")!;
            foreach (var slot in new[] { "ascii", "hAnsi", "eastAsia", "cs" })
                AssertTrue(fonts.Attribute(Wn + slot)?.Value == "Nirmala UI", $"rFonts/@{slot}");
            AssertTrue(rPr.Element(Wn + "bCs") is not null, "bCs missing");
            AssertTrue(rPr.Element(Wn + "szCs")?.Attribute(Wn + "val")?.Value == "28", "szCs missing");
        });
        Check("docDefaults carry szCs so complex-script text defaults to 11pt like Latin", () =>
        {
            var styles = ReadPartXml(path, "word/styles.xml");
            AssertContains(styles, "<w:szCs w:val=\"22\" />", "docDefaults szCs");
        });
        Check("Font (Latin slots only, like Word's Font box) leaves the cs slot alone", () =>
        {
            var p = Path.Combine(outDir, "text-font-latin.docx");
            var d = new Document();
            d.AddParagraph().Text("x", new TextFormat { Font = "Georgia" });
            d.SaveTo(p);
            var fonts = BodyXml(p).Descendants(Wn + "rFonts").First();
            AssertTrue(fonts.Attribute(Wn + "ascii")?.Value == "Georgia" && fonts.Attribute(Wn + "hAnsi")?.Value == "Georgia"
                && fonts.Attribute(Wn + "cs") is null, "Font slots");
            AssertTrue(new TextFormat { Font = "Georgia" }.Font == "Georgia", "Font getter");
        });
        Check("Indic text survives reader and loader byte-for-byte", () =>
        {
            using var r = DocxReader.Open(path);
            AssertContains(r.ExtractText(), "வணக்கம், இது ஒரு சோதனை அறிக்கை.", "reader");
            AssertContains(Document.Load(path).GetText(), "നമസ്കാരം", "loader");
        });
    }

    private static void BreaksAndTabs(string outDir)
    {
        var path = Path.Combine(outDir, "text-breaks.docx");
        var doc = new Document();
        doc.AddParagraph("Line one\nLine two\tTabbed\r\nLine three\rLine four");
        doc.AddParagraph().Text("before").Break(BreakType.Page).Text("after page break");
        doc.AddParagraph().Text("col A").Break(BreakType.Column).Text("col B");
        doc.AddParagraph().Text("a").Break(BreakType.TextWrappingClearLeft).Text("b").Break(BreakType.TextWrappingClearRight).Text("c");
        doc.SaveTo(path);
        var paras = BodyXml(path).Elements(Wn + "body").Elements(Wn + "p").ToList();

        Check("\\n, \\r\\n and \\r become w:br; \\t becomes w:tab; no control chars left in w:t", () =>
        {
            var r = paras[0].Element(Wn + "r")!;
            var seq = string.Join(",", r.Elements().Where(e => e.Name != Wn + "rPr").Select(e => e.Name.LocalName));
            AssertTrue(seq == "t,br,t,tab,t,br,t,br,t", $"sequence was {seq}");
            AssertTrue(paras.SelectMany(p => p.Descendants(Wn + "t")).All(t => t.Value.IndexOfAny(new[] { '\n', '\r', '\t' }) < 0),
                "control character inside w:t");
        });
        Check("explicit page / column / text-wrapping breaks carry type and clear", () =>
        {
            var brs = paras.Skip(1).SelectMany(p => p.Descendants(Wn + "br")).Select(b =>
                (b.Attribute(Wn + "type")?.Value ?? "-") + "/" + (b.Attribute(Wn + "clear")?.Value ?? "-")).ToList();
            AssertTrue(string.Join(" ", brs) == "page/- column/- textWrapping/left textWrapping/right", string.Join(" ", brs));
        });
        Check("reader text: breaks → \\n, tabs → \\t; model GetText keeps the original string", () =>
        {
            using var rd = DocxReader.Open(path);
            var first = rd.Paragraphs().First().Text;
            AssertTrue(first == "Line one\nLine two\tTabbed\nLine three\nLine four", $"reader text was '{first.Replace("\n", "\\n").Replace("\t", "\\t")}'");
            AssertTrue(((Paragraph)doc.Blocks[0]).GetText() == "Line one\nLine two\tTabbed\r\nLine three\rLine four", "GetText");
        });
        Check("loader restores explicit break and tab runs with their kinds", () =>
        {
            var loaded = Document.Load(path);
            var p0 = (Paragraph)loaded.Blocks[0];
            AssertTrue(p0.Runs.Count(r => r.Break == BreakType.Line) == 3 && p0.Runs.Count(r => r.IsTab) == 1, "paragraph 0 runs");
            AssertTrue(((Paragraph)loaded.Blocks[1]).Runs.Any(r => r.Break == BreakType.Page), "mid-paragraph page break kept in place");
            AssertTrue(((Paragraph)loaded.Blocks[2]).Runs.Any(r => r.Break == BreakType.Column), "column break");
            var kinds = ((Paragraph)loaded.Blocks[3]).Runs.Where(r => r.Break is not null).Select(r => r.Break!.Value).ToList();
            AssertTrue(kinds.SequenceEqual(new[] { BreakType.TextWrappingClearLeft, BreakType.TextWrappingClearRight }), "wrapping breaks");
        });
        Check("AddPageBreak still loads as PageBreakBefore (unchanged contract)", () =>
        {
            var p = Path.Combine(outDir, "text-addpagebreak.docx");
            var d = new Document();
            d.AddParagraph("one");
            d.AddPageBreak();
            d.AddParagraph("two");
            d.SaveTo(p);
            var l = Document.Load(p);
            AssertTrue(((Paragraph)l.Blocks[1]).PageBreakBefore && ((Paragraph)l.Blocks[1]).Runs.Count == 0, "page-break paragraph");
        });
    }

    private static void FormattedHyperlink(string outDir)
    {
        var path = Path.Combine(outDir, "text-link.docx");
        var doc = new Document();
        doc.AddParagraph().Text("Visit ").Hyperlink("our site", "https://example.com/", new TextFormat { Bold = true, SizePt = 12 });
        doc.SaveTo(path);

        Check("formatted hyperlink: rStyle=Hyperlink first, then the extra formatting", () =>
        {
            var rPr = BodyXml(path).Descendants(Wn + "hyperlink").Single().Descendants(Wn + "rPr").Single();
            var names = rPr.Elements().Select(e => e.Name.LocalName).ToList();
            AssertTrue(names[0] == "rStyle" && rPr.Element(Wn + "rStyle")!.Attribute(Wn + "val")!.Value == "Hyperlink", "rStyle first");
            AssertTrue(names.Contains("b") && rPr.Element(Wn + "sz")!.Attribute(Wn + "val")!.Value == "24", "bold + size");
        });
    }

    private static void TemplateMultiLineValues(string outDir)
    {
        var template = Path.Combine(outDir, "text-tpl.docx");
        var doc = new Document();
        doc.AddParagraph().Text("Address: ").Text("{{Address}}", new TextFormat { Bold = true });
        doc.AddParagraph("Untouched line");
        doc.SaveTo(template);

        var output = Path.Combine(outDir, "text-tpl-out.docx");
        DocxTemplate.Fill(template, output, new Dictionary<string, string>
        {
            ["Address"] = "Apulki Medical Center\nBaner, Pune\tMH 411045",
        });

        Check("template: newlines / tabs in a filled value become w:br / w:tab inside the bold run", () =>
        {
            var run = BodyXml(output).Descendants(Wn + "r").First(r => r.Element(Wn + "rPr")?.Element(Wn + "b") is not null);
            var seq = string.Join(",", run.Elements().Where(e => e.Name != Wn + "rPr").Select(e => e.Name.LocalName));
            AssertTrue(seq == "t,br,t,tab,t", $"sequence was {seq}");
            using var r = DocxReader.Open(output);
            AssertContains(r.ExtractText(), "Address: Apulki Medical Center\nBaner, Pune\tMH 411045", "reader text");
            AssertContains(r.ExtractText(), "Untouched line", "untouched paragraph");
        });
    }

    // ---- Helpers -------------------------------------------------------------------

    private static XElement BodyXml(string path)
        => XElement.Parse(ReadPartXml(path, "word/document.xml"));

    private static string FirstDiff(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        int s = Math.Max(0, i - 80);
        return $"A: …{a.Substring(s, Math.Min(200, a.Length - s))}\nB: …{b.Substring(s, Math.Min(200, b.Length - s))}";
    }
}
