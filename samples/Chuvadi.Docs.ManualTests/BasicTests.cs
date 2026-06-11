using System;
using System.IO;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.ManualTests;

public static class MinimalDocxTest
{
    public static void Run(string outDir)
    {
        var path = Path.Combine(outDir, "minimal.docx");

        Program.Check("Write one-paragraph document", () =>
        {
            var doc = new Document();
            doc.AddParagraph("Hello from Chuvadi.Docs.");
            doc.SaveTo(path);
            Program.AssertTrue(File.Exists(path), "File not created.");
        });

        Program.Check("Package has all mandatory parts", () =>
        {
            var parts = Program.ListParts(path);
            foreach (var required in new[]
            {
                "[Content_Types].xml", "_rels/.rels", "word/document.xml",
                "word/_rels/document.xml.rels", "word/styles.xml", "word/settings.xml",
                "docProps/core.xml", "docProps/app.xml",
            })
                Program.AssertTrue(parts.Contains(required), $"Missing part: {required}");
        });

        Program.Check("document.xml contains the text and sectPr", () =>
        {
            var xml = Program.ReadPartXml(path, "word/document.xml");
            Program.AssertContains(xml, "Hello from Chuvadi.Docs.", "body text");
            Program.AssertContains(xml, "sectPr", "section properties");
            Program.AssertContains(xml, "pgSz", "page size");
        });

        Program.Check("Content types declare the Word main part", () =>
        {
            var ct = Program.ReadPartXml(path, "[Content_Types].xml");
            Program.AssertContains(ct, "wordprocessingml.document.main+xml", "main content type");
        });

        Program.Check("No numbering part when no lists used", () =>
        {
            Program.AssertTrue(!Program.ListParts(path).Contains("word/numbering.xml"),
                "numbering.xml should be omitted for list-free documents");
        });
    }
}

public static class FormattingTest
{
    public static void Run(string outDir)
    {
        var path = Path.Combine(outDir, "formatting.docx");

        Program.Check("Write document exercising styles, runs, lists, links, breaks", () =>
        {
            var doc = new Document();
            doc.AddParagraph("Annual Report", ParagraphStyle.Title);
            doc.AddHeading("Overview", 1);
            doc.AddHeading("Financials", 2);
            doc.AddHeading("Detail", 3);
            doc.AddParagraph()
                .Text("Revenue grew ")
                .Text("18%", new TextFormat { Bold = true, ColorHex = "C00000" })
                .Text(" with ")
                .Text("strong", TextFormat.ItalicText)
                .Text(" margins, ")
                .Text("underlined", new TextFormat { Underline = true })
                .Text(" and ")
                .Text("struck", new TextFormat { Strikethrough = true })
                .Text(" plus ")
                .Text("highlighted", new TextFormat { Highlight = "yellow" })
                .Text(" and ")
                .Text("sized", new TextFormat { SizePt = 16, Font = "Arial" })
                .Text(".");
            doc.AddParagraph("Centered", ParagraphStyle.Normal).Alignment = ParagraphAlignment.Center;
            doc.AddParagraph("A wise quote.", ParagraphStyle.Quote);
            doc.AddBullet("First bullet");
            doc.AddBullet("Nested bullet", level: 1);
            doc.AddNumbered("Step one");
            doc.AddNumbered("Step two");
            doc.AddParagraph().Hyperlink("Chuvadi on GitHub", "https://github.com/arunshivab/chuvadi");
            doc.AddPageBreak();
            doc.AddParagraph("Second page.");
            doc.SaveTo(path);
        });

        Program.Check("document.xml carries run/paragraph markup", () =>
        {
            var xml = Program.ReadPartXml(path, "word/document.xml");
            foreach (var marker in new[]
            {
                "Heading1", "Heading2", "Heading3", "Title", "Quote",
                "w:b /", "w:i /", "w:u", "w:strike", "w:highlight", "w:color",
                "w:sz", "Arial", "w:jc", "numPr", "w:hyperlink", "w:br",
            })
                Program.AssertContains(xml.Replace("<w:b/>", "<w:b />").Replace("<w:i/>", "<w:i />"),
                    marker, $"marker {marker}");
        });

        Program.Check("numbering.xml present with bullet + decimal definitions", () =>
        {
            var xml = Program.ReadPartXml(path, "word/numbering.xml");
            Program.AssertContains(xml, "abstractNum", "abstract numbering");
            Program.AssertContains(xml, "bullet", "bullet format");
            Program.AssertContains(xml, "decimal", "decimal format");
        });

        Program.Check("Hyperlink has an external relationship", () =>
        {
            var rels = Program.ReadPartXml(path, "word/_rels/document.xml.rels");
            Program.AssertContains(rels, "https://github.com/arunshivab/chuvadi", "link target");
            Program.AssertContains(rels, "TargetMode=\"External\"", "external mode");
        });

        Program.Check("styles.xml declares the style set", () =>
        {
            var xml = Program.ReadPartXml(path, "word/styles.xml");
            foreach (var id in new[] { "Normal", "Title", "Heading1", "Heading2", "Heading3", "Quote", "ListParagraph", "Hyperlink" })
                Program.AssertContains(xml, $"w:styleId=\"{id}\"", $"style {id}");
        });
    }
}

public static class TablesTest
{
    public static void Run(string outDir)
    {
        var path = Path.Combine(outDir, "tables.docx");

        Program.Check("Write tables: borders, header row, shading, span, widths, adjacency", () =>
        {
            var doc = new Document();
            doc.AddHeading("Sales", 1);

            var t = doc.AddTable(3);
            t.ColumnWidthsPt = new double[] { 150, 80, 100 };
            t.HeaderRow("Item", "Qty", "Price").Shade("DDE7F5");
            t.AddRow("Widget", "4", "120.00");
            t.AddRow("Gadget", "2", "540.00");
            var totalRow = t.AddRow();
            totalRow.Cell(0).SetText("Total", TextFormat.BoldText);
            totalRow.Cell(0).ColumnSpan = 2;
            totalRow.Cell(2).SetText("1,560.00", TextFormat.BoldText);
            totalRow.Cell(2).ShadeHex = "FFF2CC";

            // Adjacent borderless table — exercises the auto-separator and trailing paragraph.
            var t2 = doc.AddTable(2);
            t2.Borders = false;
            t2.AddRow("Left", "Right");

            doc.SaveTo(path);
        });

        Program.Check("Table XML structure", () =>
        {
            var xml = Program.ReadPartXml(path, "word/document.xml");
            Program.AssertContains(xml, "w:tbl", "table element");
            Program.AssertContains(xml, "tblHeader", "repeating header row");
            Program.AssertContains(xml, "gridSpan", "column span");
            Program.AssertContains(xml, "w:shd", "cell shading");
            Program.AssertContains(xml, "DDE7F5", "header shade color");
            Program.AssertContains(xml, "tblBorders", "borders");
            Program.AssertContains(xml, "gridCol", "grid columns");
        });

        Program.Check("Adjacent tables separated; body doesn't end on a table", () =>
        {
            var xml = Program.ReadPartXml(path, "word/document.xml");
            var tblClose = xml.LastIndexOf("</w:tbl>", StringComparison.Ordinal);
            var after = xml.Substring(tblClose);
            Program.AssertContains(after, "<w:p", "paragraph after final table");
        });

        Program.Check("Reader returns the table grid", () =>
        {
            using var r = DocxReader.Open(path);
            string[][]? grid = null;
            foreach (var g in r.Tables()) { grid = g; break; }
            Program.AssertTrue(grid is not null, "no table found");
            Program.AssertTrue(grid![0][0] == "Item", $"header cell was '{grid[0][0]}'");
            Program.AssertTrue(grid[1][1] == "4", $"data cell was '{grid[1][1]}'");
        });
    }
}

public static class HeaderFooterPageTest
{
    public static void Run(string outDir)
    {
        var path = Path.Combine(outDir, "headerfooter.docx");

        Program.Check("Write landscape Letter doc with headers, footers, page numbers", () =>
        {
            var doc = new Document();
            doc.Page.Size = PageSize.Letter;
            doc.Page.Orientation = PageOrientation.Landscape;
            doc.Page.TopMarginPt = 36;
            doc.Header.SetText("Chuvadi Quarterly", TextFormat.BoldText);
            doc.Footer.Add(new Paragraph { Alignment = ParagraphAlignment.Right }.PageNumber(includeTotal: true));
            doc.FirstPageHeader.SetText("CONFIDENTIAL — Title Page");
            doc.AddParagraph("Body content.");
            doc.SaveTo(path);
        });

        Program.Check("Header/footer parts exist and are referenced", () =>
        {
            var parts = Program.ListParts(path);
            Program.AssertTrue(parts.Contains("word/header1.xml"), "header1 missing");
            Program.AssertTrue(parts.Contains("word/footer2.xml"), "footer2 missing");
            Program.AssertTrue(parts.Contains("word/header3.xml"), "first-page header missing");

            var docXml = Program.ReadPartXml(path, "word/document.xml");
            Program.AssertContains(docXml, "headerReference", "header reference");
            Program.AssertContains(docXml, "footerReference", "footer reference");
            Program.AssertContains(docXml, "w:titlePg", "different first page flag");
        });

        Program.Check("Footer carries PAGE/NUMPAGES fields", () =>
        {
            var xml = Program.ReadPartXml(path, "word/footer2.xml");
            Program.AssertContains(xml, " PAGE ", "PAGE field");
            Program.AssertContains(xml, " NUMPAGES ", "NUMPAGES field");
        });

        Program.Check("Landscape Letter page size with flipped dimensions", () =>
        {
            var xml = Program.ReadPartXml(path, "word/document.xml");
            Program.AssertContains(xml, "w:w=\"15840\"", "landscape width = portrait height");
            Program.AssertContains(xml, "w:h=\"12240\"", "landscape height = portrait width");
            Program.AssertContains(xml, "orient=\"landscape\"", "orientation attribute");
            Program.AssertContains(xml, "w:top=\"720\"", "36pt top margin in twips");
        });

        Program.Check("Content types declare header/footer parts", () =>
        {
            var ct = Program.ReadPartXml(path, "[Content_Types].xml");
            Program.AssertContains(ct, "header+xml", "header content type");
            Program.AssertContains(ct, "footer+xml", "footer content type");
        });
    }
}
