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
/// Phase 2 — the paragraph model: every ParagraphFormat property written in CT_PPr order,
/// read back by Document.Load and round-tripped exactly; Heading 4–9; full run shading;
/// decorative (art) borders preserved; Word's start/end indent attributes; ApplyFormat/Clone.
/// </summary>
public static class ParagraphModelTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>CT_PPrBase + CT_PPr child order, ECMA-376 Part 1 §17.3.1.26.</summary>
    private static readonly string[] PprOrder =
    {
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr",
        "suppressLineNumbers", "pBdr", "shd", "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap",
        "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd",
        "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap", "jc",
        "textDirection", "textAlignment", "textboxTightWrap", "outlineLvl", "divId", "cnfStyle", "rPr",
        "sectPr", "pPrChange",
    };

    /// <summary>A format with every property set.</summary>
    public static ParagraphFormat Everything()
    {
        var f = new ParagraphFormat
        {
            Alignment = ParagraphAlignment.Distribute,
            KeepWithNext = true,
            KeepLinesTogether = true,
            StartOnNewPage = false,
            WidowControl = false,
            SuppressLineNumbers = true,
            SuppressAutoHyphens = true,
            SpaceBeforePt = 12,
            SpaceAfterPt = 6,
            SpaceBeforeAuto = false,
            SpaceAfterAuto = false,
            LineSpacing = LineSpacing.Multiple(1.15),
            ContextualSpacing = true,
            IndentLeftPt = 36,
            IndentRightPt = 18,
            HangingIndentPt = 9,
            MirrorIndents = false,
            Borders = new ParagraphBorders
            {
                Top = BorderSpec.Single(1.5, "1F3864"),
                Left = new BorderSpec { Style = BorderStyle.Double, WidthPt = 0.75, ColorHex = "C00000", SpacePt = 4 },
                Bottom = new BorderSpec { Style = BorderStyle.Dashed, WidthPt = 1, ThemeColor = ThemeColor.Accent2, Shadow = true },
                Right = BorderSpec.None,
                Between = BorderSpec.Single(0.25),
                Bar = new BorderSpec { Style = BorderStyle.Thick, WidthPt = 3, Frame = true },
            },
            Shading = Shading.WithPattern(ShadingPattern.Percent20, "000000", "DEEAF6"),
            OutlineLevel = 9,
            RightToLeft = false,
            FontAlignment = FontAlignment.Center,
            SnapToGrid = false,
            AdjustRightIndent = false,
            Kinsoku = true,
            WordWrap = false,
            OverflowPunctuation = true,
            TopLinePunctuation = false,
            AutoSpaceDE = false,
            AutoSpaceDN = true,
            MarkFormat = new TextFormat { SizePt = 8, Font = "Arial" },
        };
        f.TabStops.Add(new TabStop(360, TabAlignment.Right, TabLeader.Dot));
        f.TabStops.Add(new TabStop(72, TabAlignment.Left));
        f.TabStops.Add(new TabStop(216, TabAlignment.Decimal, TabLeader.MiddleDot));
        f.TabStops.Add(new TabStop(144, TabAlignment.Center, TabLeader.Hyphen));
        return f;
    }

    public static void Run(string outDir)
    {
        WrittenInSchemaOrder(outDir);
        LoadReadsEverything(outDir);
        ExactRoundTrip(outDir);
        HeadingsFourToNine(outDir);
        RunShadingAndArtBorders(outDir);
        WordAuthoredVariants(outDir);
        FormatCopySemantics();
        HostileValues(outDir);
        SaveToStreamLeavesOpen();
        ReaderIgnoresTabStopDefinitions(outDir);
    }

    private static void ReaderIgnoresTabStopDefinitions(string outDir)
    {
        Check("DocxReader text: tab-stop definitions (pPr/w:tabs) are not tab characters; pPrChange style ignored", () =>
        {
            var path = Path.Combine(outDir, "para-reader-tabs.docx");
            var d = new Document();
            d.AddParagraph().Text("Item").Tab().Text("₹ 10").Configure(f =>
            {
                f.TabStops.Add(new TabStop(200, TabAlignment.Right, TabLeader.Dot));
                f.TabStops.Add(new TabStop(300));
            });
            d.AddHeading("Styled", 2);
            d.SaveTo(path);
            EditDocumentXml(path, xml => xml.Replace("<w:pStyle w:val=\"Heading2\" />",
                "<w:pStyle w:val=\"Heading2\" /><w:pPrChange w:id=\"1\" w:author=\"a\" w:date=\"2026-10-28T00:00:00Z\"><w:pPr><w:pStyle w:val=\"Heading9\" /></w:pPr></w:pPrChange>"));
            using var r = DocxReader.Open(path);
            var ps = r.Paragraphs().ToList();
            AssertTrue(ps[0].Text == "Item\t₹ 10", $"text was '{ps[0].Text.Replace("\t", "\\t")}'");
            AssertTrue(ps[1].StyleId == "Heading2", $"style was {ps[1].StyleId}");
        });
    }

    private static void SaveToStreamLeavesOpen()
    {
        Check("Document.SaveTo(Stream) leaves the caller's stream open and positioned at the end", () =>
        {
            var d = new Document();
            d.AddParagraph("stream");
            var ms = new MemoryStream();
            d.SaveTo(ms);
            AssertTrue(ms.CanRead && ms.Position == ms.Length && ms.Length > 0, "stream closed or empty");
            ms.Position = 0;
            using var r = DocxReader.Open(ms);
            AssertContains(r.ExtractText(), "stream", "read back from the same stream");
        });
    }

    /// <summary>Regressions from the independent review: odd/hostile values never crash load or produce invalid output.</summary>
    private static void HostileValues(string outDir)
    {
        var path = Path.Combine(outDir, "para-hostile.docx");
        var doc = new Document();
        doc.AddParagraph("base");
        doc.SaveTo(path);
        EditDocumentXml(path, xml => xml.Replace("<w:body>", "<w:body>" +
            "<w:p><w:pPr><w:spacing w:line=\"1e309\" /></w:pPr><w:r><w:t>inf</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:spacing w:line=\"5e-324\" /></w:pPr><w:r><w:t>tiny</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:spacing w:line=\"Infinity\" w:lineRule=\"exact\" /></w:pPr><w:r><w:t>exact-inf</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pBdr><w:top w:val=\"single\" w:sz=\"-8\" w:space=\"1e40\" /></w:pBdr><w:ind w:left=\"1e309\" /><w:textAlignment w:val=\"7\" /><w:outlineLvl w:val=\"-3\" /></w:pPr><w:r><w:t>odd</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:textAlignment w:val=\"top,bottom\" /></w:pPr><w:r><w:t>a</w:t><w:rPr><w:b /></w:rPr><w:t>b</w:t></w:r></w:p>"));

        Check("hostile line spacing / borders / indents / textAlignment / outlineLvl: load never throws, output stays schema-valid", () =>
        {
            var loaded = Document.Load(path);
            var ps = loaded.Blocks.Cast<Paragraph>().ToList();
            AssertTrue(ps[0].Format.LineSpacing is null && ps[1].Format.LineSpacing is null && ps[2].Format.LineSpacing is null, "bad line spacing ignored");
            var odd = ps[3].Format;
            AssertTrue(odd.Borders!.Top!.WidthPt == 0 && odd.Borders.Top.SpacePt == 0, $"border clamped: {odd.Borders.Top.WidthPt}/{odd.Borders.Top.SpacePt}");
            AssertTrue(odd.IndentLeftPt is null && odd.FontAlignment is null && odd.OutlineLevel is null, "ind / textAlignment / outlineLvl ignored");
            AssertTrue(ps[4].Format.FontAlignment is null, "comma list rejected");
            var re = Path.Combine(outDir, "para-hostile-resaved.docx");
            loaded.SaveTo(re);
            var xml = ReadPartXml(re, "word/document.xml");
            AssertTrue(!xml.Contains("2147483647") && !xml.Contains("w:sz=\"-") && !xml.Contains("textAlignment w:val=\"7\"")
                && !xml.Contains("outlineLvl"), "invalid values leaked into output");
        });
        Check("text before a (misplaced) rPr keeps its own formatting", () =>
        {
            var runs = Document.Load(path).Blocks.Cast<Paragraph>().ElementAt(4).Runs; // the inserted paragraphs precede "base"
            AssertTrue(runs.Count == 2 && runs[0].TextContent == "a" && !runs[0].Format.Bold && runs[1].TextContent == "b" && runs[1].Format.Bold,
                $"runs: {string.Join("|", runs.Select(r => r.TextContent + (r.Format.Bold ? "*" : "")))}");
        });
        Check("invalid values set through the API fail clearly at save, not with IndexOutOfRange", () =>
        {
            var d = new Document();
            d.AddParagraph("x").Configure(f => f.Shading = new Shading { Pattern = (ShadingPattern)99 });
            AssertThrows<ArgumentOutOfRangeException>(() => d.SaveTo(new MemoryStream()), "undefined pattern");
            var d2 = new Document();
            d2.AddParagraph("x").Configure(f => f.Borders = new ParagraphBorders { Top = new BorderSpec { RawStyleToken = "x\" y=\"1" } });
            AssertThrows<ArgumentException>(() => d2.SaveTo(new MemoryStream()), "raw token injection");
            var d3 = new Document();
            d3.AddParagraph("x").Configure(f => f.SpaceAfterPt = double.NaN);
            AssertThrows<ArgumentOutOfRangeException>(() => d3.SaveTo(new MemoryStream()), "NaN measurement");
        });
    }

    private static void WrittenInSchemaOrder(string outDir)
    {
        var path = Path.Combine(outDir, "para-all.docx");
        var doc = new Document();
        doc.AddParagraph("everything").ApplyFormat(Everything());
        doc.AddParagraph("first line").Configure(f => { f.FirstLineIndentPt = 18; f.LineSpacing = LineSpacing.Exactly(14); });
        doc.AddParagraph("at least").Configure(f => f.LineSpacing = LineSpacing.AtLeast(16));
        var li = doc.AddBullet("list item with format");
        li.Format.SpaceAfterPt = 3;
        li.Format.KeepWithNext = true;
        doc.SaveTo(path);
        var body = Body(path);

        Check("pPr children follow the CT_PPr schema order (incl. numPr between widowControl and pBdr)", () =>
        {
            foreach (var pPr in body.Descendants(Wn + "pPr"))
            {
                var names = pPr.Elements().Select(e => e.Name.LocalName).ToList();
                var idx = names.Select(n => Array.IndexOf(PprOrder, n)).ToList();
                AssertTrue(idx.All(i => i >= 0), $"unknown pPr child in [{string.Join(",", names)}]");
                for (int i = 1; i < idx.Count; i++)
                    AssertTrue(idx[i] > idx[i - 1], $"out of order: [{string.Join(",", names)}]");
            }
        });

        Check("every paragraph property emitted with the right attributes and units", () =>
        {
            var pPr = body.Descendants(Wn + "p").First().Element(Wn + "pPr")!;
            string A(string el, string attr) => pPr.Element(Wn + el)?.Attribute(Wn + attr)?.Value ?? "<missing>";
            AssertTrue(A("jc", "val") == "distribute", "jc");
            AssertTrue(A("spacing", "before") == "240" && A("spacing", "after") == "120"
                && A("spacing", "line") == "276" && A("spacing", "lineRule") == "auto"
                && A("spacing", "beforeAutospacing") == "0", "spacing twips / 240ths");
            AssertTrue(A("ind", "left") == "720" && A("ind", "right") == "360" && A("ind", "hanging") == "180"
                && A("ind", "firstLine") == "<missing>", "ind (hanging wins over firstLine)");
            AssertTrue(A("shd", "val") == "pct20" && A("shd", "color") == "000000" && A("shd", "fill") == "DEEAF6", "shd");
            AssertTrue(A("outlineLvl", "val") == "9" && A("textAlignment", "val") == "center", "outline / textAlignment");
            AssertTrue(A("keepNext", "val") == "<missing>" && A("widowControl", "val") == "0" && A("pageBreakBefore", "val") == "0", "toggles");
            var bdr = pPr.Element(Wn + "pBdr")!;
            var sides = string.Join(",", bdr.Elements().Select(e => e.Name.LocalName));
            AssertTrue(sides == "top,left,bottom,right,between,bar", $"pBdr order {sides}");
            string B(string side, string attr) => bdr.Element(Wn + side)?.Attribute(Wn + attr)?.Value ?? "<missing>";
            AssertTrue(B("top", "val") == "single" && B("top", "sz") == "12" && B("top", "color") == "1F3864", "top border eighths");
            AssertTrue(B("left", "val") == "double" && B("left", "space") == "4" && B("left", "sz") == "6", "left border");
            AssertTrue(B("bottom", "themeColor") == "accent2" && B("bottom", "shadow") == "1", "bottom border");
            AssertTrue(B("right", "val") == "none" && B("bar", "frame") == "1" && B("bar", "sz") == "24", "right / bar");
            var tabs = pPr.Element(Wn + "tabs")!.Elements().Select(t =>
                $"{t.Attribute(Wn + "val")!.Value}@{t.Attribute(Wn + "pos")!.Value}:{t.Attribute(Wn + "leader")?.Value ?? "-"}");
            AssertTrue(string.Join(" ", tabs) == "left@1440:- center@2880:hyphen decimal@4320:middleDot right@7200:dot",
                "tabs sorted by position: " + string.Join(" ", tabs));
            var mark = pPr.Element(Wn + "rPr")!;
            AssertTrue(mark.Element(Wn + "sz")?.Attribute(Wn + "val")?.Value == "16", "paragraph mark rPr");
        });

        Check("line spacing rules: exact and atLeast in twips; firstLine without hanging", () =>
        {
            var ps = body.Descendants(Wn + "p").ToList();
            var p1 = ps[1].Element(Wn + "pPr")!;
            AssertTrue(p1.Element(Wn + "spacing")!.Attribute(Wn + "lineRule")!.Value == "exact"
                && p1.Element(Wn + "spacing")!.Attribute(Wn + "line")!.Value == "280", "exact 14pt = 280");
            AssertTrue(p1.Element(Wn + "ind")!.Attribute(Wn + "firstLine")!.Value == "360", "firstLine 18pt");
            var p2 = ps[2].Element(Wn + "pPr")!;
            AssertTrue(p2.Element(Wn + "spacing")!.Attribute(Wn + "lineRule")!.Value == "atLeast", "atLeast");
        });
    }

    private static void LoadReadsEverything(string outDir)
    {
        var path = Path.Combine(outDir, "para-load.docx");
        var doc = new Document();
        doc.AddParagraph("everything").ApplyFormat(Everything());
        doc.SaveTo(path);

        Check("Document.Load reads every paragraph property back", () =>
        {
            var f = ((Paragraph)Document.Load(path).Blocks[0]).Format;
            var e = Everything();
            AssertTrue(f.Alignment == e.Alignment && f.KeepWithNext == true && f.KeepLinesTogether == true
                && f.StartOnNewPage == false && f.WidowControl == false && f.SuppressLineNumbers == true
                && f.SuppressAutoHyphens == true, "pagination");
            AssertTrue(f.SpaceBeforePt == 12 && f.SpaceAfterPt == 6 && f.SpaceBeforeAuto == false && f.SpaceAfterAuto == false
                && Equals(f.LineSpacing, LineSpacing.Multiple(1.15)) && f.ContextualSpacing == true, "spacing");
            AssertTrue(f.IndentLeftPt == 36 && f.IndentRightPt == 18 && f.HangingIndentPt == 9 && f.FirstLineIndentPt is null
                && f.MirrorIndents == false, "indents");
            AssertTrue(f.Borders!.Top == e.Borders!.Top && f.Borders.Left == e.Borders.Left && f.Borders.Bottom == e.Borders.Bottom
                && f.Borders.Right == e.Borders.Right && f.Borders.Between == e.Borders.Between && f.Borders.Bar == e.Borders.Bar, "borders");
            AssertTrue(f.Shading == e.Shading, "shading");
            AssertTrue(f.TabStops.OrderBy(t => t.PositionPt).SequenceEqual(e.TabStops.OrderBy(t => t.PositionPt)), "tab stops");
            AssertTrue(f.OutlineLevel == 9 && f.RightToLeft == false && f.FontAlignment == FontAlignment.Center
                && f.SnapToGrid == false && f.AdjustRightIndent == false, "structure");
            AssertTrue(f.Kinsoku == true && f.WordWrap == false && f.OverflowPunctuation == true && f.TopLinePunctuation == false
                && f.AutoSpaceDE == false && f.AutoSpaceDN == true, "East Asian typography");
            AssertTrue(f.MarkFormat!.SizePt == 8 && f.MarkFormat.Font == "Arial", "mark format");
        });
    }

    private static void ExactRoundTrip(string outDir)
    {
        var a = Path.Combine(outDir, "para-rt-a.docx");
        var doc = new Document();
        doc.AddParagraph("everything").ApplyFormat(Everything());
        doc.AddHeading("Heading 6", 6).Configure(f => f.KeepWithNext = false);
        doc.AddParagraph("centred").Alignment = ParagraphAlignment.Center;
        doc.AddParagraph("Item\t₹1,250.00").Configure(f => f.TabStops.Add(new TabStop(400, TabAlignment.Decimal, TabLeader.Dot)));
        doc.AddNumbered("numbered").Configure(f => f.SpaceAfterPt = 0);
        doc.AddParagraph("rtl").Configure(f => { f.RightToLeft = true; f.Alignment = ParagraphAlignment.HighKashida; });
        doc.SaveTo(a);

        Check("load → save reproduces document.xml exactly (paragraph properties)", () =>
        {
            var b = Path.Combine(outDir, "para-rt-b.docx");
            Document.Load(a).SaveTo(b);
            var xa = ReadPartXml(a, "word/document.xml");
            var xb = ReadPartXml(b, "word/document.xml");
            AssertTrue(xa == xb, "document.xml differs after round-trip");
        });
    }

    private static void HeadingsFourToNine(string outDir)
    {
        var path = Path.Combine(outDir, "para-headings.docx");
        var doc = new Document();
        for (int level = 1; level <= 9; level++) doc.AddHeading($"Heading level {level}", level);
        doc.SaveTo(path);

        Check("Heading 1–9: styles defined with outline levels 0–8; reader and loader see them", () =>
        {
            var styles = XElement.Parse(ReadPartXml(path, "word/styles.xml"));
            for (int level = 1; level <= 9; level++)
            {
                var st = styles.Elements(Wn + "style").FirstOrDefault(s => s.Attribute(Wn + "styleId")?.Value == $"Heading{level}");
                AssertTrue(st is not null, $"Heading{level} style missing");
                var ol = st!.Descendants(Wn + "outlineLvl").Single().Attribute(Wn + "val")!.Value;
                AssertTrue(ol == (level - 1).ToString(), $"Heading{level} outline level {ol}");
            }
            using var r = DocxReader.Open(path);
            AssertTrue(r.Paragraphs().Select(p => p.StyleId).SequenceEqual(Enumerable.Range(1, 9).Select(i => $"Heading{i}")), "reader style ids");
            var loaded = Document.Load(path).Blocks.Cast<Paragraph>().Select(p => p.Style).ToList();
            AssertTrue(loaded[8] == ParagraphStyle.Heading9 && loaded[3] == ParagraphStyle.Heading4, "loader styles");
        });
    }

    private static void RunShadingAndArtBorders(string outDir)
    {
        var path = Path.Combine(outDir, "para-shading.docx");
        var doc = new Document();
        doc.AddParagraph().Text("patterned run", new TextFormat { Shading = Shading.WithPattern(ShadingPattern.DiagonalStripe, "FF0000", "FFFF00") })
            .Text(" plain fill", new TextFormat { ShadingHex = "E2EFDA" });
        doc.SaveTo(path);

        Check("run shading: full pattern round-trips as Shading; plain fill stays the ShadingHex shortcut", () =>
        {
            var runs = ((Paragraph)Document.Load(path).Blocks[0]).Runs;
            AssertTrue(runs[0].Format.Shading == Shading.WithPattern(ShadingPattern.DiagonalStripe, "FF0000", "FFFF00")
                && runs[0].Format.ShadingHex is null, "patterned run");
            AssertTrue(runs[1].Format.ShadingHex == "E2EFDA" && runs[1].Format.Shading is null, "plain fill");
        });

        var art = Path.Combine(outDir, "para-art-border.docx");
        File.Copy(path, art, true);
        EditDocumentXml(art, xml => xml.Replace("<w:body>",
            "<w:body><w:p><w:pPr><w:pBdr><w:top w:val=\"apples\" w:sz=\"20\" w:space=\"1\" w:color=\"auto\" /></w:pBdr></w:pPr><w:r><w:t>art</w:t></w:r></w:p>"));
        Check("decorative art border (outside BorderStyle) is preserved verbatim through load → save", () =>
        {
            var loaded = Document.Load(art);
            var top = ((Paragraph)loaded.Blocks[0]).Format.Borders!.Top!;
            AssertTrue(top.RawStyleToken == "apples" && top.WidthPt == 2.5, $"raw {top.RawStyleToken} width {top.WidthPt}");
            var re = Path.Combine(outDir, "para-art-border-resaved.docx");
            loaded.SaveTo(re);
            AssertContains(ReadPartXml(re, "word/document.xml"), "w:val=\"apples\"", "art border token");
        });
    }

    private static void WordAuthoredVariants(string outDir)
    {
        var path = Path.Combine(outDir, "para-word-variants.docx");
        var doc = new Document();
        doc.AddParagraph("base");
        doc.SaveTo(path);
        EditDocumentXml(path, xml => xml.Replace("<w:body>",
            "<w:body><w:p><w:pPr><w:pBdr><w:start w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\" /></w:pBdr>" +
            "<w:tabs><w:tab w:val=\"end\" w:pos=\"9000\" /></w:tabs>" +
            "<w:ind w:start=\"720\" w:end=\"360\" />" +
            "<w:jc w:val=\"end\" />" +
            "<w:pPrChange w:id=\"1\" w:author=\"a\" w:date=\"2026-10-28T00:00:00Z\"><w:pPr><w:jc w:val=\"center\" /><w:ind w:start=\"0\" /></w:pPr></w:pPrChange>" +
            "</w:pPr><w:r><w:t>word</w:t></w:r></w:p>"));

        Check("Word's start/end attributes map to left/right; pPrChange history does not override", () =>
        {
            var f = ((Paragraph)Document.Load(path).Blocks[0]).Format;
            AssertTrue(f.IndentLeftPt == 36 && f.IndentRightPt == 18, $"ind {f.IndentLeftPt}/{f.IndentRightPt}");
            AssertTrue(f.Alignment == ParagraphAlignment.Right, $"jc end → Right, got {f.Alignment}");
            AssertTrue(f.Borders?.Left is not null, "pBdr start → Left");
            AssertTrue(f.TabStops.Single().Alignment == TabAlignment.Right && f.TabStops[0].PositionPt == 450, "tab end → Right");
        });
    }

    private static void FormatCopySemantics()
    {
        Check("ApplyFormat copies only set values; Clone is deep (borders, tab stops)", () =>
        {
            var p = new Paragraph("x");
            p.Format.SpaceAfterPt = 4;
            p.Format.Alignment = ParagraphAlignment.Center;
            var template = new ParagraphFormat { SpaceBeforePt = 10, Borders = ParagraphBorders.Box(BorderSpec.Single()) };
            template.TabStops.Add(new TabStop(100));
            p.ApplyFormat(template);
            AssertTrue(p.Format.SpaceAfterPt == 4 && p.Format.SpaceBeforePt == 10 && p.Alignment == ParagraphAlignment.Center, "merge");
            template.Borders.Top = BorderSpec.None;
            template.TabStops.Clear();
            AssertTrue(p.Format.Borders!.Top!.Style == BorderStyle.Single && p.Format.TabStops.Count == 1, "applied copy is independent");
            var clone = p.Format.Clone();
            clone.TabStops.Add(new TabStop(200));
            clone.Borders!.Bottom = BorderSpec.None;
            AssertTrue(p.Format.TabStops.Count == 1 && p.Format.Borders.Bottom!.Style == BorderStyle.Single, "clone is independent");
            p.Alignment = ParagraphAlignment.Left;
            AssertTrue(p.Format.Alignment is null, "Alignment = Left means inherit (nothing written)");
        });
    }

    // ---- Helpers -------------------------------------------------------------------

    private static XElement Body(string path) => XElement.Parse(ReadPartXml(path, "word/document.xml"));

    private static void EditDocumentXml(string path, Func<string, string> edit)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);
        var e = zip.GetEntry("word/document.xml")!;
        string xml;
        using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
        e.Delete();
        using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
        w.Write(edit(xml));
    }
}
