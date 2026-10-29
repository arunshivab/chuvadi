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
/// Phase 6 — sections: multiple sections with their own page setup, columns, headers/footers
/// (default, first, even; linked or blank), page numbering, line numbers, page borders,
/// vertical alignment, document grid, custom page sizes, and text watermarks; loading of all
/// of it from Word-style files, page-number fields included.
/// </summary>
public static class SectionsTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Rn = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Vn = "urn:schemas-microsoft-com:vml";

    private static readonly string[] SectPrOrder =
    {
        "headerReference", "footerReference", "footnotePr", "endnotePr", "type", "pgSz", "pgMar", "paperSrc", "pgBorders", "lnNumType",
        "pgNumType", "cols", "formProt", "vAlign", "noEndnote", "titlePg", "textDirection", "bidi", "rtlGutter", "docGrid",
    };

    public static void Run(string outDir)
    {
        MultipleSections(outDir);
        EverySectionProperty(outDir);
        HeadersAndFooters(outDir);
        PageSizes(outDir);
        Watermarks(outDir);
        LoadWordStyle(outDir);
        Hostile(outDir);
        ReviewRegressions(outDir);
    }

    private static Document Report(out Section front, out Section body)
    {
        var doc = new Document();
        doc.Header.SetText("Front matter");
        doc.Footer.Add(new Paragraph { Alignment = ParagraphAlignment.Center }.PageNumber());
        front = doc.Section;
        front.PageNumbering.Format = NumberFormat.LowerRoman;
        front.PageNumbering.Start = 1;
        doc.AddParagraph("Preface");
        body = doc.AddSectionBreak(SectionStart.OddPage);
        body.Page.Orientation = PageOrientation.Landscape;
        body.PageNumbering.Format = NumberFormat.Decimal;
        body.PageNumbering.Start = 1;
        body.Header.SetText("Results");
        doc.AddParagraph("Wide table goes here");
        front = doc.Sections[0];
        return doc;
    }

    private static void MultipleSections(string outDir)
    {
        var path = Path.Combine(outDir, "sections-multi.docx");
        var doc = Report(out var front, out var body);
        doc.SaveTo(path);

        Check("AddSectionBreak: the ended section keeps layout and headers on the last paragraph; the new one starts linked", () =>
        {
            AssertTrue(doc.Sections.Count == 2 && ReferenceEquals(doc.Sections[1], doc.Section), "two sections");
            AssertTrue(front.Page.Orientation == PageOrientation.Portrait && front.PageNumbering.Format == NumberFormat.LowerRoman, "front layout copied");
            AssertTrue(front.Header[0].GetText() == "Front matter" && front.Footer.Count == 1 && doc.Footer.Count == 0, "headers moved");
            AssertTrue(((Paragraph)doc.Blocks[0]).SectionProperties == front && doc.Blocks.Count == 2, "preface paragraph holds the break");
            AssertTrue(body.Start == SectionStart.OddPage && body.Page.Orientation == PageOrientation.Landscape, "new section");
        });
        Check("multi-section document.xml: sectPr in the paragraph and the body, in schema order, with header references", () =>
        {
            var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            var sects = x.Descendants(Wn + "sectPr").ToList();
            AssertTrue(sects.Count == 2 && sects[0].Parent!.Name == Wn + "pPr", "paragraph sectPr");
            foreach (var s in sects) AssertOrder(s, SectPrOrder);
            AssertTrue(sects[0].Elements(Wn + "headerReference").Count() == 1 && sects[0].Elements(Wn + "footerReference").Count() == 1, "front refs");
            AssertTrue(sects[1].Elements(Wn + "headerReference").Count() == 1 && !sects[1].Elements(Wn + "footerReference").Any(), "body: own header, footer linked");
            AssertTrue(sects[1].Element(Wn + "type")!.Attribute(Wn + "val")!.Value == "oddPage", "type");
            AssertTrue(sects[0].Element(Wn + "pgNumType")!.Attribute(Wn + "fmt")!.Value == "lowerRoman"
                && sects[1].Element(Wn + "pgSz")!.Attribute(Wn + "orient")!.Value == "landscape", "numbering / orientation");
        });
        Check("multi-section round-trip: load → save reproduces document.xml and the header/footer parts exactly", () =>
        {
            var back = Document.Load(path);
            AssertTrue(back.Sections.Count == 2 && back.Sections[0].Header[0].GetText() == "Front matter" && back.Section.Footer.Count == 0
                && back.Section.Footer.LinkToPrevious, "model");
            var re = Path.Combine(outDir, "sections-multi-resaved.docx");
            back.SaveTo(re);
            foreach (var part in new[] { "word/document.xml", "word/header1.xml", "word/footer2.xml", "word/header3.xml", "word/settings.xml" })
                AssertTrue(ReadPartXml(path, part) == ReadPartXml(re, part), part + " differs");
        });
    }

    private static void EverySectionProperty(string outDir)
    {
        var path = Path.Combine(outDir, "sections-props.docx");
        var doc = new Document();
        var s = doc.Section;
        s.Columns.Count = 2;
        s.Columns.SpacingPt = 18;
        s.Columns.Separator = true;
        s.LineNumbering = new LineNumbering { CountBy = 5, DistancePt = 18, Restart = LineNumberRestart.EachSection };
        s.PageBorders = PageBorders.Box(new BorderSpec { Style = BorderStyle.Double, WidthPt = 1.5, ColorHex = "1F3864", SpacePt = 24 });
        s.PageBorders.OffsetFrom = PageBorderOffset.Page;
        s.PageBorders.Display = PageBorderDisplay.FirstPage;
        s.VerticalAlignment = SectionVerticalAlignment.Center;
        s.Grid = new DocumentGrid { Type = DocumentGridType.Lines, LinePitchPt = 18 };
        s.RightToLeft = true;
        s.PageNumbering.ChapterStyle = 1;
        s.PageNumbering.ChapterSeparator = ChapterSeparator.EnDash;
        s.Page.GutterPt = 18;
        s.Page.HeaderDistancePt = 20;
        s.Page.PaperSourceFirstPage = 1;
        doc.AddParagraph("Two columns");
        var narrow = doc.AddSectionBreak(SectionStart.Continuous);
        narrow.Columns.Count = null;
        narrow.Columns.Separator = null;
        narrow.Columns.SpacingPt = null;
        narrow.Columns.EqualWidth = false;
        narrow.Columns.Columns.Add(new ColumnDefinition(300, 24));
        narrow.Columns.Columns.Add(new ColumnDefinition(127.3, 0));
        narrow.TextDirection = CellTextDirection.TopToBottomRightToLeft;
        doc.MirrorMargins = true;
        doc.GutterAtTop = true;
        doc.AddParagraph("Unequal columns");
        doc.SaveTo(path);

        Check("every section property written in CT_SectPr order and read back", () =>
        {
            var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            var sects = x.Descendants(Wn + "sectPr").ToList();
            foreach (var sp in sects) AssertOrder(sp, SectPrOrder);
            var first = sects[0];
            AssertTrue(first.Element(Wn + "cols")!.Attribute(Wn + "num")!.Value == "2" && first.Element(Wn + "cols")!.Attribute(Wn + "sep")!.Value == "1", "cols");
            AssertTrue(first.Element(Wn + "pgBorders")!.Attribute(Wn + "offsetFrom")!.Value == "page" && first.Element(Wn + "pgBorders")!.Elements().Count() == 4, "borders");
            AssertTrue(first.Element(Wn + "lnNumType")!.Attribute(Wn + "countBy")!.Value == "5", "line numbers");
            AssertTrue(first.Element(Wn + "pgMar")!.Attribute(Wn + "gutter")!.Value == "360" && first.Element(Wn + "pgMar")!.Attribute(Wn + "header")!.Value == "400", "pgMar");
            var cols = sects[1].Element(Wn + "cols")!;
            AssertTrue(cols.Attribute(Wn + "equalWidth")!.Value == "0" && cols.Elements(Wn + "col").Count() == 2 && cols.Attribute(Wn + "num")!.Value == "2", "unequal cols");
            var st = ReadPartXml(path, "word/settings.xml");
            AssertContains(st, "<w:mirrorMargins />", "mirror margins");
            AssertContains(st, "<w:gutterAtTop />", "gutter at top");

            var back = Document.Load(path);
            var b0 = back.Sections[0];
            AssertTrue(b0.Columns.Count == 2 && b0.Columns.Separator == true && b0.LineNumbering!.Restart == LineNumberRestart.EachSection
                && b0.PageBorders!.Top!.Style == BorderStyle.Double && b0.VerticalAlignment == SectionVerticalAlignment.Center
                && b0.Grid!.Type == DocumentGridType.Lines && b0.RightToLeft == true && b0.PageNumbering.ChapterSeparator == ChapterSeparator.EnDash
                && b0.Page.PaperSourceFirstPage == 1, "section 1 model");
            AssertTrue(back.Section.Columns.Columns.Count == 2 && back.Section.Columns.Columns[1].WidthPt == 127.3
                && back.Section.TextDirection == CellTextDirection.TopToBottomRightToLeft && back.Section.Start == SectionStart.Continuous, "section 2 model");
            AssertTrue(back.MirrorMargins && back.GutterAtTop, "settings");
            var re = Path.Combine(outDir, "sections-props-resaved.docx");
            back.SaveTo(re);
            AssertTrue(ReadPartXml(path, "word/document.xml") == ReadPartXml(re, "word/document.xml"), "document.xml differs");
        });
    }

    private static void HeadersAndFooters(string outDir)
    {
        var path = Path.Combine(outDir, "sections-headers.docx");
        var doc = new Document();
        doc.Header.SetText("Odd page header");
        doc.Section.EvenPageHeader.SetText("Even page header");
        doc.Section.FirstPageFooter.SetText("Title page footer");
        var t = doc.Footer.AddTable(2);
        t.AddRow("Lab: Chuvadi", "Page");
        t.Rows[0].Cells[1].Paragraphs[0].PageNumber(includeTotal: true);
        var bullets = doc.Numbering.AddBulletList();
        doc.Header.Add(new Paragraph("listed in header").SetList(bullets));
        doc.AddParagraph("Body");
        var s2 = doc.AddSectionBreak();
        s2.Header.LinkToPrevious = false; // blank header in section 2
        doc.AddParagraph("Section 2 body");
        doc.SaveTo(path);

        Check("even/first/default headers and footers, a footer table with page fields, a blank unlinked header", () =>
        {
            var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            var sects = x.Descendants(Wn + "sectPr").ToList();
            var types = sects[0].Elements().Where(e => e.Name.LocalName.EndsWith("Reference")).Select(e => e.Name.LocalName[0] + ":" + e.Attribute(Wn + "type")!.Value);
            AssertTrue(string.Join(",", types) == "h:default,f:default,f:first,h:even", string.Join(",", types));
            AssertTrue(sects[0].Element(Wn + "titlePg") is not null, "titlePg automatic");
            AssertTrue(sects[1].Elements(Wn + "headerReference").Count() == 1, "blank header reference in section 2");
            AssertContains(ReadPartXml(path, "word/settings.xml"), "<w:evenAndOddHeaders />", "even/odd setting automatic");
            var rels = XElement.Parse(ReadPartXml(path, "word/_rels/document.xml.rels"));
            string Target(XElement reference) => rels.Elements().Single(r => r.Attribute("Id")!.Value == reference.Attribute(Rn + "id")!.Value).Attribute("Target")!.Value;
            var blank = XElement.Parse(ReadPartXml(path, "word/" + Target(sects[1].Element(Wn + "headerReference")!)));
            AssertTrue(blank.Elements(Wn + "p").Count() == 1 && !blank.Descendants(Wn + "t").Any(), "blank header part");
            var footer = XElement.Parse(ReadPartXml(path, "word/" + Target(sects[0].Elements(Wn + "footerReference").First())));
            AssertTrue(footer.Element(Wn + "tbl") is not null && footer.Elements().Last().Name == Wn + "p", "footer table + trailing paragraph");
            AssertTrue(footer.Descendants(Wn + "fldSimple").Count() == 2, "PAGE and NUMPAGES in the table");
            AssertContains(ReadPartXml(path, "word/numbering.xml"), "<w:abstractNum", "numbering for a list in a header");
            AssertTrue(doc.GetListLabel(doc.Sections[0].Header[1])?.Text == "•", "header list labelled");
        });
        Check("headers, footer tables and page fields load back and round-trip", () =>
        {
            var back = Document.Load(path);
            var s1 = back.Sections[0];
            AssertTrue(s1.EvenPageHeader[0].GetText() == "Even page header" && s1.FirstPageFooter[0].GetText() == "Title page footer", "slots");
            AssertTrue(s1.Footer.Tables.Count == 1 && back.DifferentOddAndEvenPages == true, "table + setting");
            var cell = s1.Footer.Tables[0].Rows[0].Cells[1].Paragraphs[0];
            AssertTrue(cell.Runs.Count(r => r.FieldInstruction is not null) == 2, "page fields are field runs again");
            AssertTrue(back.Section.Header.Count == 1 && back.Section.Header[0].GetText() == "" && !back.Section.Header.LinkToPrevious, "blank header kept as blank");
            var re = Path.Combine(outDir, "sections-headers-resaved.docx");
            back.SaveTo(re);
            AssertTrue(ReadPartXml(path, "word/document.xml") == ReadPartXml(re, "word/document.xml"), "document.xml differs");
            foreach (var n in Enumerable.Range(1, 6))
            {
                var name = PartNames(path).FirstOrDefault(e => e.EndsWith(n + ".xml") && (e.Contains("header") || e.Contains("footer")));
                if (name is not null) AssertTrue(ReadPartXml(path, name) == ReadPartXml(re, name), name + " differs");
            }
        });
    }

    private static void PageSizes(string outDir)
    {
        Check("page sizes: presets detected on load, custom sizes exact, landscape custom flips", () =>
        {
            var doc = new Document();
            doc.Page.Size = PageSize.A5;
            doc.AddParagraph("A5");
            var s2 = doc.AddSectionBreak();
            s2.Page.SetCustomSize(288, 432); // 4×6 in label
            s2.Page.Orientation = PageOrientation.Landscape;
            doc.AddParagraph("label");
            var s3 = doc.AddSectionBreak();
            s3.Page.Size = PageSize.B5;
            s3.Page.Orientation = PageOrientation.Portrait; // the new section starts as a copy of the landscape one
            doc.AddParagraph("B5");
            var path = Path.Combine(outDir, "sections-sizes.docx");
            doc.SaveTo(path);
            var sz = XElement.Parse(ReadPartXml(path, "word/document.xml")).Descendants(Wn + "pgSz")
                .Select(e => e.Attribute(Wn + "w")!.Value + "x" + e.Attribute(Wn + "h")!.Value).ToList();
            AssertTrue(string.Join(",", sz) == "8391x11906,8640x5760,10319x14571", string.Join(",", sz));
            var back = Document.Load(path);
            AssertTrue(back.Sections[0].Page.Size == PageSize.A5 && back.Sections[1].Page.Size == PageSize.Custom
                && back.Sections[1].Page.CustomWidthPt == 288 && back.Sections[1].Page.WidthPt == 432 && back.Section.Page.Size == PageSize.B5, "model");
            AssertThrows<ArgumentOutOfRangeException>(() => new PageSetup().SetCustomSize(0, 100), "zero width");
        });
    }

    private static void Watermarks(string outDir)
    {
        var path = Path.Combine(outDir, "sections-watermark.docx");
        var doc = new Document();
        doc.Section.Watermark = new TextWatermark("CONFIDENTIAL") { ColorHex = "FF0000" };
        doc.Section.FirstPageHeader.SetText("Cover");
        doc.AddParagraph("Watermarked text");
        doc.SaveTo(path);

        Check("text watermark: Word's VML WordArt shape in the default and first-page headers (a header part is created when empty)", () =>
        {
            var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            var refs = x.Descendants(Wn + "headerReference").ToList();
            AssertTrue(refs.Count == 2, "default + first header");
            var parts = PartNames(path).Where(n => n.Contains("header")).ToList();
            foreach (var part in parts)
            {
                var h = XElement.Parse(ReadPartXml(path, part));
                var shape = h.Descendants(Vn + "shape").Single();
                AssertTrue(shape.Attribute("id")!.Value.StartsWith("PowerPlusWaterMarkObject") && shape.Attribute("type")!.Value == "#_x0000_t136", "shape");
                AssertTrue(shape.Element(Vn + "textpath")!.Attribute("string")!.Value == "CONFIDENTIAL" && shape.Attribute("fillcolor")!.Value == "#FF0000", "text/colour");
                AssertContains(shape.Attribute("style")!.Value, "rotation:315", "diagonal");
                AssertTrue(h.Descendants(Vn + "shapetype").Single().Attribute("id")!.Value == "_x0000_t136", "shapetype");
            }
            var spids = parts.Select(pn => XElement.Parse(ReadPartXml(path, pn)).Descendants(Vn + "shape").Single().Attribute("id")!.Value).ToList();
            AssertTrue(spids.Distinct().Count() == spids.Count, "unique shape ids");
        });
        Check("text watermark loads back (text, colour, opacity, orientation, size) and round-trips exactly", () =>
        {
            var back = Document.Load(path);
            var wm = back.Section.Watermark!;
            AssertTrue(wm.Text == "CONFIDENTIAL" && wm.ColorHex == "FF0000" && wm.Opacity == 0.5 && wm.Diagonal && wm.Font == "Calibri" && wm.WidthPt > 0, "model");
            var re = Path.Combine(outDir, "sections-watermark-resaved.docx");
            back.SaveTo(re);
            foreach (var part in PartNames(path).Where(n => n.StartsWith("word/") && n.EndsWith(".xml")))
                AssertTrue(ReadPartXml(path, part) == ReadPartXml(re, part), part + " differs");
        });
    }

    private static void LoadWordStyle(string outDir)
    {
        Check("a Word-style file: three sections, shared and linked headers, complex PAGE/NUMPAGES fields, watermark — loads and re-saves", () =>
        {
            var hdr1 = "<w:p><w:pPr><w:pStyle w:val=\"Header\"/></w:pPr><w:r><w:rPr><w:noProof/></w:rPr><w:pict>" +
                       "<v:shape id=\"PowerPlusWaterMarkObject1\" o:spid=\"_x0000_s2049\" type=\"#_x0000_t136\" style=\"position:absolute;margin-left:0;margin-top:0;width:412.4pt;height:247.45pt;rotation:315;z-index:-251657216\" fillcolor=\"silver\" stroked=\"f\">" +
                       "<v:fill opacity=\".5\"/><v:textpath style=\"font-family:&quot;Calibri&quot;;font-size:1pt\" string=\"DRAFT\"/></v:shape></w:pict></w:r>" +
                       "<w:r><w:t>Chapter header</w:t></w:r></w:p>";
            var ftr = "<w:p><w:r><w:t xml:space=\"preserve\">Page </w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE   \\* MERGEFORMAT </w:instrText></w:r>" +
                      "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>2</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
                      "<w:r><w:t xml:space=\"preserve\"> of </w:t></w:r><w:fldSimple w:instr=\" NUMPAGES \"><w:r><w:t>9</w:t></w:r></w:fldSimple>" +
                      "<w:r><w:t xml:space=\"preserve\"> — </w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> DATE </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>28/10/2026</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
            var body =
                "<w:p><w:r><w:t>One</w:t></w:r></w:p>" +
                "<w:p><w:pPr><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH1\"/><w:footerReference w:type=\"default\" r:id=\"rIdF1\"/>" +
                "<w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>" +
                "<w:pgNumType w:fmt=\"lowerRoman\"/><w:cols w:space=\"708\"/><w:docGrid w:linePitch=\"360\"/></w:sectPr></w:pPr></w:p>" +
                "<w:p><w:r><w:t>Two</w:t></w:r></w:p>" +
                "<w:p><w:pPr><w:sectPr><w:type w:val=\"continuous\"/><w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>" +
                "<w:cols w:num=\"2\" w:space=\"708\"/><w:docGrid w:linePitch=\"360\"/></w:sectPr></w:pPr></w:p>" +
                "<w:p><w:r><w:t>Three</w:t></w:r></w:p>" +
                "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH1\"/><w:headerReference w:type=\"first\" r:id=\"rIdMissing\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
                "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/><w:pgNumType w:start=\"1\"/><w:cols w:space=\"708\"/><w:titlePg/><w:docGrid w:linePitch=\"360\"/></w:sectPr>";
            var path = WordFile(outDir, "sections-word.docx", body, new Dictionary<string, (string Id, string Xml, bool Header)>
            {
                ["header1.xml"] = ("rIdH1", hdr1, true),
                ["footer1.xml"] = ("rIdF1", ftr, false),
            });
            var doc = Document.Load(path);
            AssertTrue(doc.Sections.Count == 3, $"sections {doc.Sections.Count}");
            var (s1, s2, s3) = (doc.Sections[0], doc.Sections[1], doc.Section);
            AssertTrue(s1.Header[0].GetText() == "Chapter header" && s1.Watermark?.Text == "DRAFT" && s1.Watermark.ColorHex == "C0C0C0" && s1.Watermark.WidthPt == 412.4, "header + watermark");
            AssertTrue(s2.Header.Count == 0 && s2.Header.LinkToPrevious && s2.Start == SectionStart.Continuous && s2.Columns.Count == 2 && s2.Page.Orientation == PageOrientation.Landscape, "section 2 linked");
            AssertTrue(s3.Header[0].GetText() == "Chapter header" && s3.TitlePage == true && s3.PageNumbering.Start == 1 && s3.FirstPageHeader.Count == 0, "section 3: shared header, missing part ignored");
            AssertTrue(s1.PageNumbering.Format == NumberFormat.LowerRoman && s1.Grid!.LinePitchPt == 18 && s1.Columns.SpacingPt == 35.4, "section 1 layout");
            var runs = s1.Footer[0].Runs;
            var fields = runs.Where(r => r.FieldInstruction is not null).Select(r => r.FieldInstruction!.Trim().Split(' ')[0] + "=" + r.TextContent).ToList();
            AssertTrue(string.Join(",", fields) == "PAGE=2,NUMPAGES=9", string.Join(",", fields));
            AssertTrue(s1.Footer[0].GetText() == "Page 2 of 9 — 28/10/2026", s1.Footer[0].GetText());
            var outPath = Path.Combine(outDir, "sections-word-out.docx");
            doc.SaveTo(outPath);
            var again = Document.Load(outPath);
            AssertTrue(again.Sections.Count == 3 && again.Sections[0].Watermark?.Text == "DRAFT" && again.Section.Header[0].GetText() == "Chapter header", "re-saved");
            var x = XElement.Parse(ReadPartXml(outPath, "word/document.xml"));
            var sects = x.Descendants(Wn + "sectPr").ToList();
            AssertTrue(!sects[1].Elements(Wn + "headerReference").Any(), "linked header stays linked");
        });
    }

    private static void Hostile(string outDir)
    {
        Check("hostile sections: absurd sizes, too many references, 1000 columns, garbage values — load and save without throwing", () =>
        {
            var refs = string.Concat(Enumerable.Range(0, 20).Select(i => $"<w:headerReference w:type=\"default\" r:id=\"rId{i}\"/>"));
            var body = "<w:p><w:pPr><w:sectPr>" + refs + "<w:pgSz w:w=\"0\" w:h=\"999999999\"/><w:pgMar w:top=\"NaN\" w:left=\"1e308\"/>" +
                       "<w:cols w:num=\"1000\" w:space=\"-5\">" + string.Concat(Enumerable.Range(0, 100).Select(_ => "<w:col w:w=\"10\"/>")) + "</w:cols>" +
                       "<w:lnNumType w:countBy=\"999999\"/><w:pgNumType w:fmt=\"bogus\" w:chapStyle=\"77\"/><w:type w:val=\"sideways\"/></w:sectPr></w:pPr></w:p>" +
                       "<w:sectPr><w:pgSz w:w=\"-12\" w:h=\"abc\"/></w:sectPr>";
            var path = WordFile(outDir, "sections-hostile.docx", body, new Dictionary<string, (string Id, string Xml, bool Header)>());
            var doc = Document.Load(path);
            var s = doc.Sections[0];
            AssertTrue(s.Columns.Count == 45 && s.Columns.Columns.Count == 45 && s.LineNumbering!.CountBy == 100 && s.PageNumbering.Format is null && s.Start is null, "clamped");
            AssertTrue(s.Page.Size == PageSize.A4 && doc.Section.Page.Size == PageSize.A4, "bad sizes ignored");
            doc.SaveTo(new MemoryStream());
        });
    }

    private static string SectPr(string refs, string extra = "") =>
        "<w:sectPr>" + refs + "<w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>" + extra + "</w:sectPr>";

    private static void ReviewRegressions(string outDir)
    {
        Check("a header part shared by many sections is loaded once and written once (no blow-up)", () =>
        {
            var refs = "<w:headerReference w:type=\"default\" r:id=\"rIdH\"/><w:headerReference w:type=\"first\" r:id=\"rIdH\"/><w:headerReference w:type=\"even\" r:id=\"rIdH\"/>" +
                       "<w:footerReference w:type=\"default\" r:id=\"rIdH2\"/><w:footerReference w:type=\"first\" r:id=\"rIdH2\"/><w:footerReference w:type=\"even\" r:id=\"rIdH2\"/>";
            var body = new StringBuilder();
            for (int i = 0; i < 300; i++) body.Append("<w:p><w:pPr>" + SectPr(refs) + "</w:pPr></w:p>");
            body.Append(SectPr(refs));
            var big = string.Concat(Enumerable.Range(0, 5000).Select(i => $"<w:p><w:r><w:t>h{i}</w:t></w:r></w:p>"));
            var path = WordFile(outDir, "sections-shared-bomb.docx", body.ToString(), new Dictionary<string, (string Id, string Xml, bool Header)>
            {
                ["header1.xml"] = ("rIdH", big, true),
                ["footer1.xml"] = ("rIdH2", big, false),
            });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = Document.Load(path);
            AssertTrue(ReferenceEquals(doc.Sections[0].Header, doc.Sections[200].EvenPageHeader), "one object per part");
            var outPath = Path.Combine(outDir, "sections-shared-bomb-out.docx");
            doc.SaveTo(outPath);
            AssertTrue(PartNames(outPath).Count(n => n.StartsWith("word/header") || n.StartsWith("word/footer")) == 2, "two parts written");
            AssertTrue(sw.Elapsed.TotalSeconds < 20, $"took {sw.Elapsed}");
        });
        Check("even-page headers hidden in Word (no evenAndOddHeaders setting) stay hidden after load → save", () =>
        {
            var path = WordFile(outDir, "sections-even-hidden.docx",
                "<w:p><w:r><w:t>x</w:t></w:r></w:p>" + SectPr("<w:headerReference w:type=\"even\" r:id=\"rIdE\"/><w:headerReference w:type=\"default\" r:id=\"rIdD\"/>"),
                new Dictionary<string, (string Id, string Xml, bool Header)>
                {
                    ["header1.xml"] = ("rIdE", "<w:p><w:r><w:t>EVEN</w:t></w:r></w:p>", true),
                    ["header2.xml"] = ("rIdD", "<w:p><w:r><w:t>DEFAULT</w:t></w:r></w:p>", true),
                });
            var doc = Document.Load(path);
            AssertTrue(doc.DifferentOddAndEvenPages == false, "explicitly off");
            var outPath = Path.Combine(outDir, "sections-even-hidden-out.docx");
            doc.SaveTo(outPath);
            AssertTrue(!ReadPartXml(outPath, "word/settings.xml").Contains("evenAndOddHeaders"), "setting stays off");
        });
        Check("on a loaded plain document, adding a first-page header still turns on the different first page", () =>
        {
            var path = Path.Combine(outDir, "sections-plain.docx");
            var d = new Document();
            d.AddParagraph("x");
            d.SaveTo(path);
            var doc = Document.Load(path);
            doc.FirstPageHeader.SetText("Cover");
            AssertTrue(doc.DifferentFirstPage, "title page automatic");
        });
        Check("a watermark only in the default header is not spread to a blank first-page header; a later section's watermark keeps the inherited header text", () =>
        {
            var wmShape = "<w:p><w:r><w:pict><v:shape id=\"PowerPlusWaterMarkObject9\" type=\"#_x0000_t136\" style=\"width:400pt;height:200pt;rotation:315\" fillcolor=\"silver\"><v:textpath string=\"DRAFT\"/></v:shape></w:pict></w:r></w:p>";
            var path = WordFile(outDir, "sections-wm-default-only.docx",
                "<w:p><w:r><w:t>x</w:t></w:r></w:p>" + SectPr("<w:headerReference w:type=\"default\" r:id=\"rIdD\"/><w:headerReference w:type=\"first\" r:id=\"rIdF\"/>", "<w:titlePg/>"),
                new Dictionary<string, (string Id, string Xml, bool Header)>
                {
                    ["header1.xml"] = ("rIdD", wmShape, true),
                    ["header2.xml"] = ("rIdF", "<w:p/>", true),
                });
            var outPath = Path.Combine(outDir, "sections-wm-default-only-out.docx");
            Document.Load(path).SaveTo(outPath);
            var withShape = PartNames(outPath).Where(n => n.StartsWith("word/header")).Count(n => ReadPartXml(outPath, n).Contains("PowerPlusWaterMarkObject"));
            AssertTrue(withShape == 1, $"watermark in {withShape} headers");

            var d = new Document();
            d.Header.SetText("COMPANY HEADER");
            d.AddParagraph("one");
            var s2 = d.AddSectionBreak();
            s2.Watermark = new TextWatermark("DRAFT");
            d.AddParagraph("two");
            var p2 = Path.Combine(outDir, "sections-wm-inherit.docx");
            d.SaveTo(p2);
            var sect2 = XElement.Parse(ReadPartXml(p2, "word/document.xml")).Descendants(Wn + "sectPr").Last();
            var rels = XElement.Parse(ReadPartXml(p2, "word/_rels/document.xml.rels"));
            var id = sect2.Element(Wn + "headerReference")!.Attribute(Rn + "id")!.Value;
            var target = rels.Elements().Single(r => r.Attribute("Id")!.Value == id).Attribute("Target")!.Value;
            var h = ReadPartXml(p2, "word/" + target);
            AssertContains(h, "COMPANY HEADER", "inherited text kept");
            AssertContains(h, "PowerPlusWaterMarkObject", "watermark");
        });
        Check("page fields: formatting survives load → save; an unterminated PAGE field keeps its text", () =>
        {
            var d = new Document();
            d.AddParagraph("x");
            var path = WordFile(outDir, "sections-fields.docx",
                "<w:p><w:r><w:t>x</w:t></w:r></w:p>" + SectPr("<w:footerReference w:type=\"default\" r:id=\"rIdF\"/>"),
                new Dictionary<string, (string Id, string Xml, bool Header)>
                {
                    ["footer1.xml"] = ("rIdF",
                        "<w:p><w:fldSimple w:instr=\" PAGE \"><w:r><w:rPr><w:b/><w:color w:val=\"FF0000\"/></w:rPr><w:t>3</w:t></w:r></w:fldSimple></w:p>" +
                        "<w:p><w:r><w:t>C:</w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>3</w:t></w:r><w:r><w:t> of 9</w:t></w:r></w:p>", false),
                });
            var doc = Document.Load(path);
            var f = doc.Section.Footer;
            AssertTrue(f[0].Runs[0].Format.Bold && f[0].Runs[0].Format.ColorHex == "FF0000", "field formatting");
            AssertTrue(f[1].GetText() == "C:3 of 9", f[1].GetText());
            var outPath = Path.Combine(outDir, "sections-fields-out.docx");
            doc.SaveTo(outPath);
            var footer = PartNames(outPath).Single(n => n.StartsWith("word/footer"));
            AssertContains(ReadPartXml(outPath, footer), "<w:fldSimple w:instr=\" PAGE \"><w:r><w:rPr><w:b /><w:color w:val=\"FF0000\" /></w:rPr>", "formatted field written");
        });
        Check("HeaderFooterContent.Clear removes tables; RemoveTable; AddSectionBreak keeps the ended section's identity, title page, and does not copy a page-number restart", () =>
        {
            var d = new Document();
            d.Header.AddTable(1).AddRow("t");
            d.Header.Clear();
            AssertTrue(d.Header.Blocks.Count == 0, "cleared");
            var t = d.Footer.AddTable(1);
            AssertTrue(d.Footer.RemoveTable(t) && d.Footer.Blocks.Count == 0, "removed");
            var cover = d.Section;
            cover.FirstPageHeader.SetText("FIRST");
            cover.PageNumbering.Start = 1;
            d.AddParagraph("cover");
            var next = d.AddSectionBreak();
            AssertTrue(ReferenceEquals(d.Sections[0], cover) && ReferenceEquals(next, d.Section) && !ReferenceEquals(next, cover), "identity");
            AssertTrue(next.DifferentFirstPage && next.PageNumbering.Start is null && cover.FirstPageHeader[0].GetText() == "FIRST", "title page kept, restart not copied");
        });
        Check("malformed header part loads blank; paper code is cleared when the size changes; unequal columns imply equalWidth=0", () =>
        {
            var path = WordFile(outDir, "sections-bad-header.docx",
                "<w:p/>" + SectPr("<w:headerReference w:type=\"default\" r:id=\"rIdB\"/>").Replace("<w:pgSz ", "<w:pgSz w:code=\"9\" "),
                new Dictionary<string, (string Id, string Xml, bool Header)> { ["header1.xml"] = ("rIdB", "<w:p><w:r><w:t>unclosed</w:r></w:p>", true) });
            var doc = Document.Load(path);
            AssertTrue(doc.Section.Header.Count == 0 && doc.Section.Page.PaperCode == 9, "blank header, code read");
            doc.Page.Size = PageSize.Letter;
            AssertTrue(doc.Page.PaperCode is null, "code cleared");
            doc.Section.Columns.Columns.Add(new ColumnDefinition(200, 20));
            doc.Section.Columns.Columns.Add(new ColumnDefinition(200, 0));
            var outPath = Path.Combine(outDir, "sections-bad-header-out.docx");
            doc.SaveTo(outPath);
            AssertContains(ReadPartXml(outPath, "word/document.xml"), "w:equalWidth=\"0\"", "equalWidth");
        });
    }

    // ---- helpers ------------------------------------------------------------------------

    private static IEnumerable<string> PartNames(string path)
    {
        using var z = ZipFile.OpenRead(path);
        return z.Entries.Select(e => e.FullName).ToList();
    }

    /// <summary>A docx with the given body XML and header/footer parts (relationship ids as given).</summary>
    private static string WordFile(string outDir, string name, string bodyInner, Dictionary<string, (string Id, string Xml, bool Header)> parts)
    {
        var path = Path.Combine(outDir, name);
        var d = new Document();
        d.AddParagraph("base");
        d.SaveTo(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);
        void Edit(string entry, Func<string, string> change)
        {
            var e = zip.GetEntry(entry)!;
            string xml;
            using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
            e.Delete();
            using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            w.Write(change(xml));
        }
        const string ns = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                          "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\"";
        var rels = new StringBuilder();
        var types = new StringBuilder();
        foreach (var (file, (id, xml, header)) in parts)
        {
            using (var w = new StreamWriter(zip.CreateEntry("word/" + file).Open(), new UTF8Encoding(false)))
                w.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:{(header ? "hdr" : "ftr")} {ns}>{xml}</w:{(header ? "hdr" : "ftr")}>");
            rels.Append($"<Relationship Id=\"{id}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/{(header ? "header" : "footer")}\" Target=\"{file}\"/>");
            types.Append($"<Override PartName=\"/word/{file}\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.{(header ? "header" : "footer")}+xml\"/>");
        }
        Edit("word/_rels/document.xml.rels", x => x.Replace("</Relationships>", rels + "</Relationships>"));
        Edit("[Content_Types].xml", x => x.Replace("</Types>", types + "</Types>"));
        Edit("word/document.xml", x =>
        {
            int start = x.IndexOf("<w:body>", StringComparison.Ordinal) + "<w:body>".Length;
            int end = x.IndexOf("</w:body>", StringComparison.Ordinal);
            var head = x[..start].Replace("<w:document ", "<w:document xmlns:v=\"urn:schemas-microsoft-com:vml\" ");
            return head + bodyInner + x[end..];
        });
        return path;
    }

    private static void AssertOrder(XElement parent, string[] order)
    {
        var names = parent.Elements().Select(e => e.Name.LocalName).ToList();
        var idx = names.Select(n => Array.IndexOf(order, n)).ToList();
        AssertTrue(idx.All(i => i >= 0), $"unknown child in {parent.Name.LocalName}: [{string.Join(",", names)}]");
        for (int i = 1; i < idx.Count; i++)
            AssertTrue(idx[i] >= idx[i - 1], $"{parent.Name.LocalName} out of order: [{string.Join(",", names)}]");
    }
}
