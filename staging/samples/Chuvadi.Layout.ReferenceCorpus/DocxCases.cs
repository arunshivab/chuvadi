using System;
using System.Collections.Generic;
using Chuvadi.Docs.Model;

namespace Chuvadi.Layout.ReferenceCorpus
{
    /// <summary>One generated document: its file name, feature family and what it exercises.</summary>
    internal sealed record Case(string Name, string Family, string Description, Func<object> Build);

    /// <summary>The docx cases: one feature family each, built with Chuvadi.Docs.</summary>
    internal static class DocxCases
    {
        private static readonly TextFormat Tamil = new TextFormat { ComplexScriptFont = "LiPi Sans Tamil", ComplexScriptLanguage = "ta-IN" };

        private static Document A4()
        {
            Document d = new Document();
            d.Page.Size = PageSize.A4;
            return d;
        }

        private static void Body(Document d, int seed, int paragraphs, int sentences = 5)
        {
            for (int i = 0; i < paragraphs; i++)
            {
                d.AddParagraph(Sample.Paragraph(seed + i, sentences));
            }
        }

        public static IEnumerable<Case> All()
        {
            yield return new Case("d01-fonts-sizes", "fonts and sizes", "Calibri, Cambria, Arial, Times New Roman and Courier New at 8 to 28 pt", () =>
            {
                Document d = A4();
                foreach (string font in new[] { "Calibri", "Cambria", "Arial", "Times New Roman", "Courier New" })
                {
                    foreach (double size in new[] { 8.0, 10, 11, 12, 14, 18, 28 })
                    {
                        TextFormat tf = new TextFormat { Font = font, SizePt = size };
                        // The paragraph mark has the run's font and size, so the line height is the run's alone (d41 covers the mark).
                        d.AddParagraph().Text(font + " " + size + " pt: " + Sample.Sentence((int)size, 9), tf).Configure(f => f.MarkFormat = tf);
                    }
                }
                return d;
            });
            yield return new Case("d02-font-styles", "fonts and sizes", "bold, italic, underline styles, strike, caps, small caps, super/subscript, spacing, scaling", () =>
            {
                Document d = A4();
                d.AddParagraph().Text("Bold ", TextFormat.BoldText).Text("italic ", new TextFormat { Italic = true }).Text("bold italic ", new TextFormat { Bold = true, Italic = true })
                    .Text("underline ", new TextFormat { Underline = true }).Text("double ", new TextFormat { UnderlineStyle = UnderlineStyle.Double })
                    .Text("strike ", new TextFormat { Strikethrough = true }).Text("caps text ", new TextFormat { Caps = true }).Text("Small Caps ", new TextFormat { SmallCaps = true });
                d.AddParagraph().Text("H").Text("2", TextFormat.SubscriptText).Text("O and E = mc").Text("2", TextFormat.SuperscriptText).Text(" with x").Text("n+1", TextFormat.SuperscriptText);
                d.AddParagraph().Text("Expanded by 2 pt: " + Sample.Sentence(3, 6), new TextFormat { CharacterSpacingPt = 2 });
                d.AddParagraph().Text("Condensed by 1 pt: " + Sample.Sentence(4, 8), new TextFormat { CharacterSpacingPt = -1 });
                d.AddParagraph().Text("Scaled 150%: " + Sample.Sentence(5, 5), new TextFormat { HorizontalScalePercent = 150 });
                d.AddParagraph().Text("Coloured ", new TextFormat { ColorHex = "C00000" }).Text("highlighted", new TextFormat { Highlight = "yellow" }).Text(" shaded", new TextFormat { ShadingHex = "DDEBF7" });
                Body(d, 20, 3);
                return d;
            });
            yield return new Case("d03-tamil-latin-mix", "Tamil and Latin", "Tamil and Latin in one paragraph, bold Tamil, LiPi Sans Tamil for the complex-script slot", () =>
            {
                Document d = A4();
                d.Defaults.RunFormat = new TextFormat { ComplexScriptFont = "LiPi Sans Tamil" };
                d.AddHeading("\u0BA8\u0BCB\u0BAF\u0BBE\u0BB3\u0BBF \u0B85\u0BB1\u0BBF\u0B95\u0BCD\u0B95\u0BC8 \u2014 Patient report", 1);
                for (int i = 0; i < 6; i++)
                {
                    d.AddParagraph().Text(Sample.Tamil1 + " " + Sample.Sentence(i, 7) + " ", Tamil).Text(Sample.Tamil2, Tamil).Text(" " + Sample.Sentence(i + 9, 10));
                }
                d.AddParagraph().Text(Sample.Tamil3, new TextFormat { ComplexScriptFont = "LiPi Sans Tamil", Bold = true, BoldComplexScript = true });
                return d;
            });
            yield return new Case("d04-tamil-long", "Tamil and Latin", "long Tamil paragraphs that wrap and break across pages", () =>
            {
                Document d = A4();
                d.Defaults.RunFormat = new TextFormat { ComplexScriptFont = "LiPi Sans Tamil", SizeComplexScriptPt = 12 };
                for (int i = 0; i < 30; i++)
                {
                    d.AddParagraph().Text(Sample.Tamil1 + " " + Sample.Tamil3 + " " + Sample.Tamil2 + " " + Sample.Tamil1, Tamil);
                }
                return d;
            });
            yield return new Case("d05-paragraph-spacing", "paragraph spacing", "space before and after, contextual spacing", () =>
            {
                Document d = A4();
                double[] spaces = { 0, 3, 6, 12, 18, 24 };
                for (int i = 0; i < 18; i++)
                {
                    double before = spaces[i % 6], after = spaces[(i + 3) % 6];
                    d.AddParagraph(Sample.Paragraph(i, 2)).Configure(f => { f.SpaceBeforePt = before; f.SpaceAfterPt = after; });
                }
                for (int i = 0; i < 4; i++)
                {
                    d.AddParagraph(Sample.Sentence(i, 10)).Configure(f => { f.SpaceBeforePt = 12; f.SpaceAfterPt = 12; f.ContextualSpacing = true; });
                }
                return d;
            });
            yield return new Case("d06-line-spacing", "paragraph spacing", "single, 1.15, 1.5, double, exactly and at least line spacing", () =>
            {
                Document d = A4();
                LineSpacing[] ls = { LineSpacing.Single, LineSpacing.Multiple(1.15), LineSpacing.OneAndHalf, LineSpacing.Double, LineSpacing.Exactly(14), LineSpacing.AtLeast(20), LineSpacing.Exactly(10) };
                for (int i = 0; i < ls.Length; i++)
                {
                    LineSpacing s = ls[i];
                    d.AddParagraph(Sample.Paragraph(40 + i, 4)).Configure(f => { f.LineSpacing = s; f.SpaceAfterPt = 6; });
                }
                return d;
            });
            yield return new Case("d07-alignment", "paragraph spacing", "left, centre, right, justified and distributed paragraphs", () =>
            {
                Document d = A4();
                foreach (ParagraphAlignment a in new[] { ParagraphAlignment.Left, ParagraphAlignment.Center, ParagraphAlignment.Right, ParagraphAlignment.Justify, ParagraphAlignment.Left, ParagraphAlignment.Justify })
                {
                    ParagraphAlignment al = a;
                    d.AddParagraph(Sample.Paragraph((int)a + 50, 4)).Configure(f => { f.Alignment = al; f.SpaceAfterPt = 10; });
                }
                return d;
            });
            yield return new Case("d08-indents", "paragraph spacing", "left, right, first-line and hanging indents", () =>
            {
                Document d = A4();
                d.AddParagraph(Sample.Paragraph(60, 4)).Configure(f => f.IndentLeftPt = 36);
                d.AddParagraph(Sample.Paragraph(61, 4)).Configure(f => f.IndentRightPt = 72);
                d.AddParagraph(Sample.Paragraph(62, 4)).Configure(f => f.FirstLineIndentPt = 36);
                d.AddParagraph(Sample.Paragraph(63, 4)).Configure(f => { f.IndentLeftPt = 54; f.HangingIndentPt = 36; });
                d.AddParagraph(Sample.Paragraph(64, 4)).Configure(f => { f.IndentLeftPt = 90; f.IndentRightPt = 90; f.Alignment = ParagraphAlignment.Justify; });
                return d;
            });
            yield return new Case("d09-keep-with-next", "keep-with-next and widows", "headings kept with the next paragraph near a page end; kept-together paragraph", () =>
            {
                Document d = A4();
                for (int s = 0; s < 6; s++)
                {
                    d.AddHeading("Section " + (s + 1) + ": " + Sample.Sentence(s, 4), 1);
                    Body(d, 70 + (s * 5), 3 + (s % 3), 6);
                }
                d.AddParagraph(Sample.Paragraph(99, 12)).Configure(f => f.KeepLinesTogether = true);
                return d;
            });
            yield return new Case("d10-widow-orphan", "keep-with-next and widows", "widow and orphan control on and off at page ends", () =>
            {
                Document d = A4();
                for (int i = 0; i < 14; i++)
                {
                    bool control = i % 2 == 0;
                    d.AddParagraph(Sample.Paragraph(100 + i, 5 + (i % 4))).Configure(f => { f.WidowControl = control; f.SpaceAfterPt = 8; });
                }
                return d;
            });
            yield return new Case("d11-page-breaks", "keep-with-next and widows", "page break before, manual page breaks and a break inside a paragraph", () =>
            {
                Document d = A4();
                Body(d, 120, 2);
                d.AddPageBreak();
                Body(d, 123, 2);
                d.AddParagraph(Sample.Paragraph(126, 2)).Configure(f => f.StartOnNewPage = true);
                d.AddParagraph().Text("Before the break. ").Break(BreakType.Page).Text("After the break on a new page.");
                return d;
            });
            yield return new Case("d12-bullets", "lists", "bulleted list over three levels", () =>
            {
                Document d = A4();
                for (int i = 0; i < 12; i++)
                {
                    d.AddBullet(Sample.Sentence(130 + i, 6 + (i % 8)), i % 3);
                }
                return d;
            });
            yield return new Case("d13-numbered", "lists", "numbered lists: decimal, lower roman with brackets, restart, start at 10, multi-level", () =>
            {
                Document d = A4();
                for (int i = 0; i < 6; i++)
                {
                    d.AddNumbered(Sample.Sentence(140 + i, 8), i % 2);
                }
                ListInstance roman = d.Numbering.AddNumberedList(NumberFormat.LowerRoman, "({0})");
                for (int i = 0; i < 4; i++)
                {
                    d.AddListItem(Sample.Sentence(150 + i, 9), roman);
                }
                ListInstance again = d.Numbering.Restart(roman, startAt: 10);
                for (int i = 0; i < 3; i++)
                {
                    d.AddListItem(Sample.Sentence(160 + i, 7), again);
                }
                return d;
            });
            yield return new Case("d14-long-text", "paragraph spacing", "six pages of plain justified text (pagination stress)", () =>
            {
                Document d = A4();
                for (int i = 0; i < 40; i++)
                {
                    int seed = 200 + i;
                    d.AddParagraph(Sample.Paragraph(seed, 6)).Configure(f => { f.Alignment = ParagraphAlignment.Justify; f.SpaceAfterPt = 6; });
                }
                return d;
            });
            yield return new Case("d15-table-simple", "tables", "a grid table with fixed column widths and a shaded header", () =>
            {
                Document d = A4();
                DocTable t = d.AddTable(4);
                t.ColumnWidthsPt = new double[] { 160, 80, 80, 130 };
                t.Format.Borders = TableBorders.Grid(BorderSpec.Single(0.75, "7F7F7F"));
                t.HeaderRow("Service", "Qty", "Rate", "Amount").Shade("DDE7F5");
                for (int i = 0; i < 12; i++)
                {
                    t.AddRow(Sample.Sentence(300 + i, 3), (i + 1).ToString(), (150 * (i + 2)).ToString("N2"), (150 * (i + 2) * (i + 1)).ToString("N2"));
                }
                return d;
            });
            yield return new Case("d16-table-merges", "tables", "column spans and vertical merges, cell shading and vertical alignment", () =>
            {
                Document d = A4();
                DocTable t = d.AddTable(4);
                t.ColumnWidthsPt = new double[] { 120, 110, 110, 110 };
                t.Format.Borders = TableBorders.Grid(BorderSpec.Single(0.5, "000000"));
                TableRow h = t.HeaderRow("Group", "Test", "Result", "Range");
                for (int g = 0; g < 4; g++)
                {
                    for (int r = 0; r < 3; r++)
                    {
                        TableRow row = t.AddRow(r == 0 ? "Group " + (g + 1) : "", Sample.Sentence(g * 3 + r, 2), (10.5 + g + r).ToString("0.0"), "8.0\u201315.0");
                        row.Cell(0).VerticalMerge = r == 0 ? VerticalMerge.Restart : VerticalMerge.Continue;
                        row.Cell(0).VerticalAlignment = CellVerticalAlignment.Center;
                        if (r == 1)
                        {
                            row.Cell(2).Shading = Shading.Fill("FFC7CE");
                        }
                    }
                    TableRow note = t.AddRow();
                    note.Cell(0).SetText("Note");
                    note.Cell(1).SetText(Sample.Sentence(g + 40, 12));
                    note.Cell(1).ColumnSpan = 3;
                }
                return d;
            });
            yield return new Case("d17-table-repeat-header", "tables", "a long table over pages with a repeated header row and rows that cannot split", () =>
            {
                Document d = A4();
                DocTable t = d.AddTable(3);
                t.ColumnWidthsPt = new double[] { 60, 280, 110 };
                t.Format.Borders = TableBorders.Grid(BorderSpec.Single(0.5, "808080"));
                TableRow head = t.HeaderRow("No.", "Description", "Amount");
                head.Shade("1F3864");
                head.CantSplit = true;
                for (int i = 0; i < 70; i++)
                {
                    TableRow row = t.AddRow((i + 1).ToString(), Sample.Sentence(400 + i, 6 + (i % 10)), (1234.5 * (i + 1)).ToString("N2"));
                    row.CantSplit = i % 5 == 0;
                }
                return d;
            });
            yield return new Case("d18-table-widths", "tables", "tables with different column widths, a narrow table and cell margins", () =>
            {
                Document d = A4();
                double[][] widths = { new double[] { 100, 100, 100 }, new double[] { 50, 300, 100 }, new double[] { 200, 50, 50, 150 } };
                foreach (double[] w in widths)
                {
                    DocTable t = d.AddTable(w.Length);
                    t.ColumnWidthsPt = w;
                    t.Format.Borders = TableBorders.Grid(BorderSpec.Single(0.5, "404040"));
                    for (int r = 0; r < 4; r++)
                    {
                        string[] cells = new string[w.Length];
                        for (int c = 0; c < w.Length; c++)
                        {
                            cells[c] = Sample.Sentence((r * 7) + c, 3 + c + r);
                        }
                        t.AddRow(cells);
                    }
                    d.AddParagraph(Sample.Sentence(500, 10));
                }
                return d;
            });
            yield return new Case("d19-table-banded-style", "tables", "a table style with a header row, banded rows and horizontal borders", () =>
            {
                Document d = A4();
                var st = d.Styles.AddTableStyle("LabGrid", "Lab Grid");
                st.TableFormat.Borders = TableBorders.Horizontal(BorderSpec.Single(0.5, "BFBFBF"));
                st.Conditional(TableRegion.FirstRow).CellFormat.Shading = Shading.Fill("1F3864");
                st.Conditional(TableRegion.FirstRow).RunFormat = new TextFormat { Bold = true, ColorHex = "FFFFFF" };
                st.Conditional(TableRegion.OddRowBand).CellFormat.Shading = Shading.Fill("DEEAF6");
                DocTable t = d.AddTable(4);
                t.Format.StyleId = "LabGrid";
                t.ColumnWidthsPt = new double[] { 150, 90, 90, 120 };
                t.HeaderRow("Test", "Result", "Unit", "Reference");
                for (int i = 0; i < 16; i++)
                {
                    t.AddRow(Sample.Sentence(600 + i, 2), (4.2 + i).ToString("0.0"), "g/dL", "13.0\u201317.0");
                }
                return d;
            });
            yield return new Case("d20-table-nested", "tables", "a nested table inside a cell", () =>
            {
                Document d = A4();
                DocTable t = d.AddTable(2);
                t.ColumnWidthsPt = new double[] { 200, 250 };
                t.Format.Borders = TableBorders.Grid(BorderSpec.Single(0.75, "000000"));
                TableRow row = t.AddRow("Outer cell: " + Sample.Sentence(700, 12), "");
                DocTable inner = row.Cell(1).AddTable(2);
                inner.ColumnWidthsPt = new double[] { 110, 110 };
                inner.Format.Borders = TableBorders.Grid(BorderSpec.Single(0.5, "C00000"));
                for (int i = 0; i < 4; i++)
                {
                    inner.AddRow("Inner " + (i + 1), Sample.Sentence(710 + i, 3));
                }
                t.AddRow(Sample.Sentence(720, 8), Sample.Sentence(721, 8));
                return d;
            });
            yield return new Case("d21-sections-orientation", "sections and columns", "portrait, then a landscape section, then portrait", () =>
            {
                Document d = A4();
                Body(d, 800, 3);
                Section land = d.AddSectionBreak(SectionStart.NextPage);
                land.Page.Orientation = PageOrientation.Landscape;
                Body(d, 803, 3);
                Section port = d.AddSectionBreak(SectionStart.NextPage);
                port.Page.Orientation = PageOrientation.Portrait;
                Body(d, 806, 3);
                return d;
            });
            yield return new Case("d22-sections-columns", "sections and columns", "a two-column section with a separator, after a one-column section", () =>
            {
                Document d = A4();
                Body(d, 820, 2);
                Section cols = d.AddSectionBreak(SectionStart.Continuous);
                cols.Columns.Count = 2;
                cols.Columns.Separator = true;
                Body(d, 822, 6);
                return d;
            });
            yield return new Case("d23-sections-page-numbering", "sections and columns", "roman page numbers in front matter, decimal restarting at 1 after", () =>
            {
                Document d = A4();
                d.Section.PageNumbering.Format = NumberFormat.LowerRoman;
                d.Footer.Add(new Paragraph { Alignment = ParagraphAlignment.Center }.PageNumber());
                Body(d, 840, 8);
                Section main = d.AddSectionBreak(SectionStart.NextPage);
                main.PageNumbering.Format = NumberFormat.Decimal;
                main.PageNumbering.Start = 1;
                Body(d, 848, 12);
                return d;
            });
            yield return new Case("d24-header-footer", "headers and footers", "a header with text and a footer with page X of Y", () =>
            {
                Document d = A4();
                d.Header.SetText("Example Medical Center \u2014 Quarterly Report");
                d.Footer.Add(new Paragraph { Alignment = ParagraphAlignment.Right }.PageNumber(includeTotal: true));
                Body(d, 900, 16);
                return d;
            });
            yield return new Case("d25-header-first-page", "headers and footers", "a different first-page header and footer", () =>
            {
                Document d = A4();
                d.Section.FirstPageHeader.SetText("First page header (title page)");
                d.Section.FirstPageFooter.SetText("First page footer");
                d.Header.SetText("Running header");
                d.Footer.Add(new Paragraph("Page ").PageNumber());
                Body(d, 920, 14);
                return d;
            });
            yield return new Case("d26-header-odd-even", "headers and footers", "odd and even page headers", () =>
            {
                Document d = A4();
                d.Header.SetText("Odd page header");
                d.Section.EvenPageHeader.SetText("Even page header");
                d.DifferentOddAndEvenPages = true;
                d.Footer.Add(new Paragraph { Alignment = ParagraphAlignment.Center }.PageNumber());
                Body(d, 940, 18);
                return d;
            });
            yield return new Case("d27-header-table", "headers and footers", "a header that holds a two-column table", () =>
            {
                Document d = A4();
                DocTable ht = d.Header.AddTable(2);
                ht.ColumnWidthsPt = new double[] { 225, 225 };
                TableRow hr = ht.AddRow("Example Medical Center", "Report No. EMC/2026/0042");
                hr.Cell(1).Paragraphs[0].Alignment = ParagraphAlignment.Right;
                d.Footer.SetText("Confidential");
                Body(d, 960, 10);
                return d;
            });
            byte[] png = Sample.Png(240, 120, 30, 90, 200);
            byte[] small = Sample.Png(64, 64, 200, 60, 40);
            yield return new Case("d28-image-inline", "images", "inline pictures in text and on their own line", () =>
            {
                Document d = A4();
                d.AddParagraph(Sample.Paragraph(980, 2));
                d.AddImage(ImageSpec.Inline(png, "image/png", 240, 120));
                d.AddParagraph().Text("Logo ").Image(ImageSpec.Inline(small, "image/png", 24, 24)).Text(" in the middle of a line of text. " + Sample.Sentence(981, 10));
                d.AddParagraph(Sample.Paragraph(982, 3));
                return d;
            });
            yield return new Case("d29-image-sizes", "images", "inline pictures of several sizes, one wider than the text area", () =>
            {
                Document d = A4();
                foreach (double w in new[] { 60.0, 120, 300, 520 })
                {
                    d.AddParagraph(w + " pt wide:");
                    d.AddImage(ImageSpec.Inline(png, "image/png", w, w / 2));
                }
                return d;
            });
            yield return new Case("d30-image-float-square", "images", "a floating picture with square wrap on the right of the margin", () =>
            {
                Document d = A4();
                d.AddImage(ImageSpec.Float(png, "image/png", 160, 80, new FloatingPosition
                {
                    HorizontalAnchor = HorizontalAnchor.Margin, HAlign = HorizontalAlignment.Right,
                    VerticalAnchor = VerticalAnchor.Paragraph, VerticalOffsetPt = 0, Wrap = TextWrap.Square, DistanceFromTextPt = 9,
                }));
                Body(d, 1000, 6);
                return d;
            });
            yield return new Case("d31-image-float-behind", "images", "a picture behind text centred on the page, and one with top-and-bottom wrap", () =>
            {
                Document d = A4();
                d.AddImage(ImageSpec.Float(png, "image/png", 300, 150, new FloatingPosition
                {
                    HorizontalAnchor = HorizontalAnchor.Page, HAlign = HorizontalAlignment.Center,
                    VerticalAnchor = VerticalAnchor.Page, VAlign = VerticalAlignment.Center, Wrap = TextWrap.None, BehindText = true,
                }));
                Body(d, 1020, 3);
                d.AddImage(ImageSpec.Float(small, "image/png", 100, 100, new FloatingPosition
                {
                    HorizontalAnchor = HorizontalAnchor.Margin, HorizontalOffsetPt = 50, VerticalAnchor = VerticalAnchor.Paragraph, Wrap = TextWrap.TopAndBottom,
                }));
                Body(d, 1025, 4);
                return d;
            });
            yield return new Case("d32-page-a4-narrow", "page sizes and margins", "A4 with narrow 0.5 inch margins", () =>
            {
                Document d = A4();
                d.Page.TopMarginPt = d.Page.BottomMarginPt = d.Page.LeftMarginPt = d.Page.RightMarginPt = 36;
                Body(d, 1100, 14);
                return d;
            });
            yield return new Case("d33-page-letter-wide", "page sizes and margins", "US Letter with wide 1.5 inch side margins", () =>
            {
                Document d = new Document();
                d.Page.Size = PageSize.Letter;
                d.Page.LeftMarginPt = d.Page.RightMarginPt = 108;
                Body(d, 1120, 12);
                return d;
            });
            yield return new Case("d34-page-a5-legal", "page sizes and margins", "an A5 section followed by a Legal landscape section", () =>
            {
                Document d = new Document();
                d.Page.Size = PageSize.A5;
                Body(d, 1140, 5);
                Section legal = d.AddSectionBreak(SectionStart.NextPage);
                legal.Page.Size = PageSize.Legal;
                legal.Page.Orientation = PageOrientation.Landscape;
                Body(d, 1145, 5);
                return d;
            });
            yield return new Case("d35-tabs", "tabs and ptab", "left, centre, right and decimal tab stops with leaders; default tab stops", () =>
            {
                Document d = A4();
                for (int i = 0; i < 8; i++)
                {
                    d.AddParagraph().Text("Item " + (i + 1)).Tab().Text("centre " + i).Tab().Text((1234.5 * (i + 1) / 3).ToString("0.00")).Tab().Text("Rs " + (99 * (i + 3)) + ".00")
                        .Configure(f =>
                        {
                            f.TabStops.Add(new TabStop(150, TabAlignment.Center));
                            f.TabStops.Add(new TabStop(300, TabAlignment.Decimal, TabLeader.Dot));
                            f.TabStops.Add(new TabStop(450, TabAlignment.Right, TabLeader.Hyphen));
                        });
                }
                d.AddParagraph().Text("a").Tab().Text("b").Tab().Text("c").Tab().Text("d (default stops)");
                return d;
            });
            yield return new Case("d36-ptab", "tabs and ptab", "positional tabs in a header, footer and body", () =>
            {
                Document d = A4();
                d.Header.Add(new Paragraph("Left header").PositionalTab(PositionalTabAlignment.Center).Text("Centre header").PositionalTab(PositionalTabAlignment.Right).Text("Right header"));
                d.Footer.Add(new Paragraph("Confidential").PositionalTab(PositionalTabAlignment.Right).PageNumber());
                d.AddParagraph().Text("Body").PositionalTab(PositionalTabAlignment.Right, PositionalTabBase.Margin, TabLeader.Dot).Text("right");
                Body(d, 1200, 4);
                return d;
            });
            yield return new Case("d37-borders-shading", "paragraph spacing", "paragraph borders (box, bottom rule) and shading", () =>
            {
                Document d = A4();
                d.AddParagraph(Sample.Paragraph(1220, 3)).Configure(f => { f.Borders = ParagraphBorders.Box(new BorderSpec { WidthPt = 1.5, ColorHex = "C00000", SpacePt = 4 }); f.Shading = Shading.Fill("FDE9E7"); });
                d.AddParagraph(Sample.Paragraph(1221, 2));
                d.AddParagraph(Sample.Sentence(1222, 6)).Configure(f => f.Shading = Shading.Fill("D9D9D9"));
                d.AddParagraph(Sample.Paragraph(1223, 3)).Configure(f => f.Borders = ParagraphBorders.Box(new BorderSpec { WidthPt = 0.5, ColorHex = "000000", SpacePt = 1 }));
                return d;
            });
            yield return new Case("d38-hyperlinks", "fonts and sizes", "hyperlinks in body text", () =>
            {
                Document d = A4();
                for (int i = 0; i < 5; i++)
                {
                    d.AddParagraph().Text(Sample.Sentence(1240 + i, 8) + " See ").Hyperlink("example page " + (i + 1), "https://example.org/page" + (i + 1)).Text(" for details.");
                }
                return d;
            });
            yield return new Case("d39-watermark", "headers and footers", "a diagonal text watermark", () =>
            {
                Document d = A4();
                d.Section.Watermark = new TextWatermark("DRAFT");
                Body(d, 1260, 6);
                return d;
            });
            yield return new Case("d41-paragraph-mark-size", "fonts and sizes", "8 pt and 20 pt text whose paragraph marks keep the 11 pt default (Word sizes the last line by the mark too)", () =>
            {
                Document d = A4();
                foreach (double size in new[] { 8.0, 20, 8, 20 })
                {
                    d.AddParagraph().Text(Sample.Sentence((int)size + 3, 14), new TextFormat { SizePt = size });
                }
                d.AddParagraph();
                d.AddParagraph().Text("After an empty paragraph.");
                return d;
            });
            yield return new Case("d42-footnotes", "notes", "five footnotes at the foot of the page holding their references, then two endnotes after the text", () =>
            {
                Document d = A4();
                for (int i = 0; i < 9; i++)
                {
                    Paragraph p = d.AddParagraph().Text(Sample.Paragraph(1300 + i, 4));
                    if (i % 2 == 0)
                    {
                        p.Footnote(d, "Footnote " + (i / 2 + 1) + ": " + Sample.Sentence(1350 + i, 10 + (i * 2)));
                        p.Text(" " + Sample.Sentence(1360 + i, 6));
                    }
                }
                d.AddParagraph().Text("Two endnotes follow the text.").Endnote(d, "First endnote: " + Sample.Sentence(1370, 9)).Text(" And another.").Endnote(d, "Second endnote.");
                return d;
            });
            yield return new Case("d43-tracked-comments", "notes", "tracked insertions and deletions (laid out as accepted) and commented text (no balloons)", () =>
            {
                Document d = A4();
                Comment c = d.Comments.Add("Reviewer", "Please confirm the dose.", "RV", new DateTime(2026, 10, 1, 10, 0, 0));
                d.AddParagraph().Text("The planned dose is ").TrackedDelete("60 Gy in 30 fractions", "Reviewer", null, new DateTime(2026, 10, 1, 10, 0, 0))
                    .TrackedInsert("50 Gy in 25 fractions", "Reviewer", null, new DateTime(2026, 10, 1, 10, 0, 0)).Text(", delivered daily. ").Text(Sample.Paragraph(1400, 3));
                d.AddParagraph().Commented("This sentence carries a comment. ", c).Text(Sample.Paragraph(1401, 3));
                d.AddParagraph().Text(Sample.Paragraph(1402, 4)).TrackedDelete(" This whole sentence was deleted and the line breaks close up.", "Reviewer", null, new DateTime(2026, 10, 1, 10, 0, 0));
                d.AddParagraph().Text(Sample.Paragraph(1403, 4));
                return d;
            });
            yield return new Case("d44-fields", "notes", "bookmarks and complex fields with cached results: REF, PAGEREF, SEQ", () =>
            {
                Document d = A4();
                d.AddParagraph().Text("Total due: ").Bookmarked("Total", "1,234.50").Text(" INR.");
                d.AddParagraph().Text("Repeated with REF: ").Ref("Total", "1,234.50").Text("; on page ").PageRef("Total", "1").Text(".");
                for (int i = 1; i <= 3; i++)
                {
                    d.AddParagraph().Text("Table ").Seq("Table", i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Text(": " + Sample.Sentence(1450 + i, 8));
                }
                d.AddParagraph().Text(Sample.Paragraph(1460, 5));
                return d;
            });
            yield return new Case("d45-text-boxes", "images", "a floating text box with fill and outline (square wrap, right) and an inline text box", () =>
            {
                Document d = A4();
                TextBox box = new TextBox
                {
                    Placement = ImagePlacement.Floating,
                    Position = new FloatingPosition { HorizontalAnchor = HorizontalAnchor.Margin, HAlign = HorizontalAlignment.Right, VerticalAnchor = VerticalAnchor.Paragraph, VerticalOffsetPt = 0, Wrap = TextWrap.Square, DistanceLeftPt = 9, DistanceRightPt = 9 },
                    WidthPt = 170,
                    HeightPt = 90,
                    FillHex = "EAF1FB",
                    LineHex = "2F5496",
                    LineWidthPt = 1,
                };
                box.Paragraphs.Add(new Paragraph("Note box", TextFormat.BoldText));
                box.Paragraphs.Add(new Paragraph(Sample.Sentence(1500, 12)));
                d.AddParagraph().TextBox(box).Text(Sample.Paragraph(1501, 6));
                d.AddParagraph().Text(Sample.Paragraph(1502, 4));
                TextBox inline = new TextBox { Placement = ImagePlacement.Inline, WidthPt = 120, HeightPt = 36, LineHex = "000000" };
                inline.Paragraphs.Add(new Paragraph("Inline box"));
                d.AddParagraph().Text("Before ").TextBox(inline).Text(" after the inline box.");
                return d;
            });
            yield return new Case("d46-floating-table", "tables", "a floating table (w:tblpPr) 200 pt from the margin, in the text flow", () =>
            {
                Document d = A4();
                d.AddParagraph().Text(Sample.Paragraph(1550, 3));
                DocTable t = d.AddTable(2);
                t.ColumnWidthsPt = new double[] { 90, 90 };
                t.Format.Borders = TableBorders.Grid(BorderSpec.Single(0.5, "000000"));
                t.Format.FloatingPosition = new TableFloatingPosition { HorizontalAnchor = TableFloatAnchor.Margin, XPt = 200, VerticalAnchor = TableFloatAnchor.Text, YPt = 0 };
                for (int i = 0; i < 4; i++)
                {
                    t.AddRow("Row " + (i + 1), (100 * (i + 1)).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                d.AddParagraph().Text(Sample.Paragraph(1551, 4));
                return d;
            });
            yield return new Case("d47-symbols", "fonts and sizes", "symbol characters (w:sym) in a text font", () =>
            {
                Document d = A4();
                d.AddParagraph().Text("Copyright ").Symbol("Liberation Sans", '\u00A9').Text(" 2026; registered ").Symbol("Liberation Sans", '\u00AE').Text("; degree 37").Symbol("Liberation Sans", '\u00B0').Text("C.");
                d.AddParagraph().Text(Sample.Paragraph(1600, 3));
                return d;
            });
            yield return new Case("d48-embedded-font", "fonts and sizes", "text in DejaVu Serif, a font only the document carries (embedded, w:embedRegular)", () =>
            {
                Document d = A4();
                d.EmbedFont("DejaVu Serif", Sample.EmbeddedFont());
                TextFormat dv = new TextFormat { Font = "DejaVu Serif", SizePt = 12 };
                d.AddParagraph().Text("Embedded DejaVu Serif: " + Sample.Paragraph(1650, 4), dv).Configure(f => f.MarkFormat = dv);
                d.AddParagraph().Text("Calibri for comparison: " + Sample.Paragraph(1651, 2));
                return d;
            });
            yield return new Case("d40-report", "mixed", "a short report: title, headings, list, table, header and footer", () =>
            {
                Document d = A4();
                d.Header.SetText("Acme Pvt Ltd \u2014 Confidential");
                d.Footer.Add(new Paragraph { Alignment = ParagraphAlignment.Right }.PageNumber(includeTotal: true));
                d.AddParagraph("Quarterly Report", ParagraphStyle.Title);
                d.AddHeading("Financial Summary", 1);
                d.AddParagraph().Text("Revenue grew ").Text("18%", new TextFormat { Bold = true, ColorHex = "C00000" }).Text(" year over year. " + Sample.Paragraph(1280, 3));
                d.AddBullet("Strong margins in services");
                d.AddBullet("New regional expansion", 1);
                d.AddNumbered("Review contracts");
                d.AddNumbered("Approve budget");
                d.AddHeading("Details", 2);
                DocTable t = d.AddTable(3);
                t.ColumnWidthsPt = new double[] { 220, 90, 140 };
                t.Format.Borders = TableBorders.Grid(BorderSpec.Single(0.5, "7F7F7F"));
                t.HeaderRow("Item", "Qty", "Amount").Shade("DDE7F5");
                for (int i = 0; i < 8; i++)
                {
                    t.AddRow(Sample.Sentence(1290 + i, 3), (i + 2).ToString(), (480.0 * (i + 1)).ToString("N2"));
                }
                Body(d, 1300, 4);
                return d;
            });
        }
    }
}
