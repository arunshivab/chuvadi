using Chuvadi.Docs.Model;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Docs;
using Chuvadi.Layout.Tests;
using Chuvadi.Layout.Model;
using LayoutBreak = Chuvadi.Layout.Model.BreakKind;
using LParagraphAlignment = Chuvadi.Layout.Model.ParagraphAlignment;
using LSectionStart = Chuvadi.Layout.Model.SectionStart;
using LUnderline = Chuvadi.Layout.Model.UnderlineStyle;
using LVerticalMerge = Chuvadi.Layout.Model.VerticalMerge;

namespace Chuvadi.Layout.Docs.Tests;

/// <summary>Documents written with Chuvadi.Docs, read back through the docx importer (L8).</summary>
public class DocsImportTests
{
    internal static LayoutOptions Options(string culture = "en-IN")
    {
        LayoutOptions o = Flow.Options();
        o.Culture = new System.Globalization.CultureInfo(culture);
        return o;
    }

    internal static byte[] Save(Document doc)
    {
        using MemoryStream ms = new MemoryStream();
        doc.SaveTo(ms);
        return ms.ToArray();
    }

    /// <summary>Saves and imports, so the importer sees exactly what a loaded file gives.</summary>
    internal static FlowDocument Import(Document doc, DocsImportOptions? o = null, List<LayoutWarning>? warnings = null, LayoutOptions? lo = null)
        => DocsImporter.Import(new MemoryStream(Save(doc)), o, lo ?? Options(), warnings is null ? null : warnings.Add);

    private static IEnumerable<FlowParagraph> Paragraphs(IEnumerable<FlowBlock> blocks)
    {
        foreach (FlowBlock b in blocks)
        {
            if (b is FlowParagraph p)
            {
                yield return p;
            }
            else if (b is FlowTable t)
            {
                foreach (FlowTableRow r in t.Rows)
                {
                    foreach (FlowTableCell c in r.Cells)
                    {
                        foreach (FlowParagraph inner in Paragraphs(c.Blocks))
                        {
                            yield return inner;
                        }
                    }
                }
            }
        }
    }

    private static string Text(FlowParagraph p) => string.Concat(p.Inlines.OfType<TextRun>().Select(r => r.Text));

    [Fact]
    public void Runs_take_their_effective_formatting()
    {
        Document doc = new Document();
        doc.AddHeading("Title text", 1);
        doc.AddParagraph().Text("plain ").Text("bold", TextFormat.BoldText).Text(" red", new TextFormat { ColorHex = "C00000", SizePt = 14 })
            .Text(" wavy", new TextFormat { UnderlineStyle = Chuvadi.Docs.Model.UnderlineStyle.Wave, Caps = true, Highlight = "yellow" })
            .Hyperlink("link", "https://example.org");
        FlowDocument flow = Import(doc);
        List<FlowParagraph> ps = Paragraphs(flow.Sections[0].Blocks).ToList();

        // Heading 1 comes from the style: Word's heading font and accent colour, larger and not black.
        TextRun heading = ps[0].Inlines.OfType<TextRun>().Single();
        Assert.Equal("Title text", heading.Text);
        Assert.True(heading.Format.FontSize > 11);
        Assert.NotEqual(LayoutColor.Black, heading.Format.Color);

        List<TextRun> runs = ps[1].Inlines.OfType<TextRun>().ToList();
        Assert.False(runs[0].Format.Bold);
        Assert.True(runs[1].Format.Bold);
        Assert.Equal(LayoutColor.FromHex("C00000"), runs[2].Format.Color);
        Assert.Equal(14, runs[2].Format.FontSize);
        Assert.Equal(LUnderline.Wave, runs[3].Format.Underline);
        Assert.Equal(TextCapitals.AllCaps, runs[3].Format.Capitals);
        Assert.Equal(new LayoutColor(0xFF, 0xFF, 0x00), runs[3].Format.Background);
        Assert.NotEqual(LUnderline.None, runs[4].Format.Underline); // the Hyperlink character style
        // Built and loaded hyperlinks both keep their target (loaded ones since Chuvadi.Docs 1.5.0).
        TextRun built = Paragraphs(DocsImporter.Import(doc, null, Options()).Sections[0].Blocks).ElementAt(1).Inlines.OfType<TextRun>().Last();
        Assert.Equal("https://example.org", built.HyperlinkUri);
        Assert.Equal("https://example.org", runs[4].HyperlinkUri);
        Assert.Equal("Calibri", runs[0].Format.FontFamily);
    }

    [Fact]
    public void Theme_colours_tints_and_fonts_resolve()
    {
        Document doc = new Document();
        doc.AddParagraph().Text("accent", new TextFormat { ThemeColor = Chuvadi.Docs.Model.ThemeColor.Accent1 })
            .Text("tinted", new TextFormat { ThemeColor = Chuvadi.Docs.Model.ThemeColor.Accent1, ThemeColorTint = 0x99 })
            .Text("theme font", new TextFormat { AsciiThemeFont = ThemeFont.MajorAscii });
        List<TextRun> runs = Paragraphs(Import(doc).Sections[0].Blocks).Single().Inlines.OfType<TextRun>().ToList();
        Assert.Equal(LayoutColor.FromHex("4472C4"), runs[0].Format.Color); // Office theme accent 1
        LayoutColor tinted = runs[1].Format.Color;
        Assert.True(tinted.R > 0x44 && tinted.G > 0x72 && tinted.B > 0xC4, "a tint lightens");
        Assert.Equal("Calibri Light", runs[2].Format.FontFamily);
    }

    [Fact]
    public void Paragraph_formatting_lists_and_tabs()
    {
        Document doc = new Document();
        doc.AddParagraph("centred").Configure(f =>
        {
            f.Alignment = Chuvadi.Docs.Model.ParagraphAlignment.Center;
            f.SpaceBeforePt = 6;
            f.SpaceAfterPt = 12;
            f.LineSpacing = LineSpacing.Exactly(18);
            f.IndentLeftPt = 36;
            f.HangingIndentPt = 18;
            f.KeepWithNext = true;
            f.TabStops.Add(new Chuvadi.Docs.Model.TabStop(200, Chuvadi.Docs.Model.TabAlignment.Right, Chuvadi.Docs.Model.TabLeader.Dot));
            f.Borders = new ParagraphBorders { Bottom = BorderSpec.Single(1.5, "FF0000") };
            f.Shading = Shading.Fill("EEEEEE");
        });
        doc.AddBullet("bullet");
        doc.AddNumbered("first");
        doc.AddNumbered("second");
        List<FlowParagraph> ps = Paragraphs(Import(doc).Sections[0].Blocks).ToList();
        Model.ParagraphFormat f = ps[0].Format;
        Assert.Equal(LParagraphAlignment.Center, f.Alignment);
        Assert.Equal(6, f.SpaceBefore);
        Assert.Equal(12, f.SpaceAfter);
        Assert.Equal(Model.LineSpacingRule.Exact, f.LineSpacingRule);
        Assert.Equal(18, f.LineSpacing);
        Assert.Equal(36, f.LeftIndent);
        Assert.Equal(-18, f.FirstLineIndent);
        Assert.True(f.KeepWithNext);
        Assert.Equal(new Model.TabStop(200, Model.TabAlignment.Right, Model.TabLeader.Dot), Assert.Single(f.TabStops));
        Assert.Equal(1.5, f.BorderBottom.Width);
        Assert.Equal(LayoutColor.FromHex("FF0000"), f.BorderBottom.Color);
        Assert.Equal(LayoutColor.FromHex("EEEEEE"), f.Background);

        Assert.Equal("\u2022", ps[1].ListLabel!.Text);
        Assert.Equal("1.", ps[2].ListLabel!.Text);
        Assert.Equal("2.", ps[3].ListLabel!.Text);
        Assert.True(ps[2].Format.LeftIndent > 0, "list items take their level's indent");
    }

    [Fact]
    public void Breaks_tabs_and_page_fields()
    {
        Document doc = new Document();
        doc.AddParagraph().Text("a").Tab().Text("b").LineBreak().Text("c").Break(Chuvadi.Docs.Model.BreakType.Page).Text("d");
        doc.Footer.Add(new Chuvadi.Docs.Model.Paragraph().PageNumber(includeTotal: true));
        FlowDocument flow = Import(doc);
        FlowParagraph p = Paragraphs(flow.Sections[0].Blocks).First();
        // A loaded document carries tabs and line breaks as characters in the run text (the engine honours both).
        Assert.Equal("a\tb\ncd", Text(p));
        Assert.Contains(p.Inlines, i => i is BreakInline { Kind: LayoutBreak.Page });
        FlowParagraph builtParagraph = Paragraphs(DocsImporter.Import(doc, null, Options()).Sections[0].Blocks).First();
        Assert.Contains(builtParagraph.Inlines, i => i is TabInline);
        Assert.Contains(builtParagraph.Inlines, i => i is BreakInline { Kind: LayoutBreak.Line });
        FlowParagraph footer = Paragraphs(flow.Sections[0].HeadersFooters!.DefaultFooter!).Single();
        Assert.Contains(footer.Inlines, i => i is FieldInline { Kind: FieldKind.Page });
        Assert.Contains(footer.Inlines, i => i is FieldInline { Kind: FieldKind.NumPages });

        using LaidOutDocument laid = DocumentLayout.Open(flow, Options());
        Assert.Equal(2, laid.ResolvePageCount());
        Assert.Contains("Page 2 of 2", laid.GetPage(1).DisplayList.OfType<GlyphRunOp>().Aggregate(string.Empty, (s, r) => s + r.Text));
    }

    [Fact]
    public void Sections_page_setup_and_linked_headers()
    {
        Document doc = new Document();
        doc.Page.Size = PageSize.Letter;
        doc.Page.LeftMarginPt = 50;
        doc.Header.SetText("Shared header");
        doc.AddParagraph("first section");
        Chuvadi.Docs.Model.Section second = doc.AddSectionBreak(Chuvadi.Docs.Model.SectionStart.OddPage);
        second.Page.Orientation = PageOrientation.Landscape;
        second.PageNumbering.Format = NumberFormat.LowerRoman;
        second.PageNumbering.Start = 1;
        doc.AddParagraph("second section");
        Chuvadi.Docs.Model.Section third = doc.AddSectionBreak();
        third.Header.LinkToPrevious = false; // blank
        doc.AddParagraph("third section");

        FlowDocument flow = Import(doc);
        Assert.Equal(3, flow.Sections.Count);
        Assert.Equal(612, flow.Sections[0].Page.Width, 1);
        Assert.Equal(50, flow.Sections[0].Page.MarginLeft);
        Assert.Equal(792, flow.Sections[1].Page.Width, 1); // landscape
        Assert.Equal(LSectionStart.OddPage, flow.Sections[1].Start);
        Assert.Equal(PageNumberFormat.LowerRoman, flow.Sections[1].PageNumberFormat);
        Assert.Equal(1, flow.Sections[1].PageNumberStart);
        Assert.Equal("first section", Text(Paragraphs(flow.Sections[0].Blocks).Single()));
        Assert.Equal("third section", Text(Paragraphs(flow.Sections[2].Blocks).Single()));

        IList<FlowBlock> h1 = flow.Sections[0].HeadersFooters!.DefaultHeader!;
        Assert.Equal("Shared header", Text(Paragraphs(h1).Single()));
        Assert.Same(h1, flow.Sections[1].HeadersFooters!.DefaultHeader); // linked to previous
        Assert.All(Paragraphs(flow.Sections[2].HeadersFooters!.DefaultHeader!), p => Assert.Equal(string.Empty, Text(p))); // not linked: blank
    }

    [Fact]
    public void Tables_carry_grid_spans_merges_and_resolved_borders()
    {
        Document doc = new Document();
        DocTable t = doc.AddTable(3);
        t.ColumnWidthsPt = new double[] { 100, 150, 200 };
        t.HeaderRow("H1", "H2", "H3").Shade("DDE7F5");
        TableRow r1 = t.AddRow("a", "b", "c");
        r1.Cell(0).ColumnSpan = 2;
        TableRow r2 = t.AddRow("d", "e", "f");
        r2.Cell(2).VerticalMerge = Chuvadi.Docs.Model.VerticalMerge.Restart;
        TableRow r3 = t.AddRow("g", "h", "i");
        r3.Cell(2).VerticalMerge = Chuvadi.Docs.Model.VerticalMerge.Continue;
        r3.HeightPt = 40;
        r3.HeightRule = Chuvadi.Docs.Model.RowHeightRule.Exact;
        r3.CantSplit = true;
        DocTable nested = r3.Cell(0).AddTable(1);
        nested.AddRow("inner");

        FlowTable ft = Assert.IsType<FlowTable>(Import(doc).Sections[0].Blocks.Single(b => b is FlowTable));
        Assert.Equal(new[] { 100.0, 150, 200 }, ft.ColumnWidths);
        Assert.True(ft.Rows[0].IsHeader);
        Assert.Equal(LayoutColor.FromHex("DDE7F5"), ft.Rows[0].Cells[0].Background);
        Assert.Equal(2, ft.Rows[1].Cells.Count);
        Assert.Equal(2, ft.Rows[1].Cells[0].ColumnSpan);
        Assert.Equal(LVerticalMerge.Restart, ft.Rows[2].Cells[2].VerticalMerge);
        Assert.Equal(LVerticalMerge.Continue, ft.Rows[3].Cells[2].VerticalMerge);
        Assert.Equal(40, ft.Rows[3].Height);
        Assert.Equal(Model.RowHeightRule.Exact, ft.Rows[3].HeightRule);
        Assert.True(ft.Rows[3].CantSplit);
        Assert.True(ft.Rows[0].Cells[0].BorderTop!.Value.IsVisible, "the default grid is resolved onto each cell");
        FlowTable inner = Assert.IsType<FlowTable>(ft.Rows[3].Cells[0].Blocks.Single(b => b is FlowTable));
        Assert.Equal("inner", Text(Paragraphs(inner.Rows[0].Cells[0].Blocks).Single()));
    }

    [Fact]
    public void Automatic_text_colour_turns_white_on_dark_shading()
    {
        Document doc = new Document();
        DocTable t = doc.AddTable(2);
        t.HeaderRow("dark", "light");
        t.Rows[0].Cell(0).ShadeHex = "1F3A60";
        t.Rows[0].Cell(1).ShadeHex = "DDE7F5";
        doc.AddParagraph("explicit").Configure(f => f.Shading = Shading.Fill("000000"));
        doc.AddParagraph().Text("red stays red", new TextFormat { ColorHex = "FF0000" }).Configure(f => f.Shading = Shading.Fill("000000"));
        FlowDocument flow = Import(doc);
        FlowTable ft = flow.Sections[0].Blocks.OfType<FlowTable>().Single();
        Assert.Equal(LayoutColor.White, Paragraphs(ft.Rows[0].Cells[0].Blocks).Single().Inlines.OfType<TextRun>().Single().Format.Color);
        Assert.Equal(LayoutColor.Black, Paragraphs(ft.Rows[0].Cells[1].Blocks).Single().Inlines.OfType<TextRun>().Single().Format.Color);
        List<FlowParagraph> body = flow.Sections[0].Blocks.OfType<FlowParagraph>().ToList();
        Assert.Equal(LayoutColor.White, body[^2].Inlines.OfType<TextRun>().Single().Format.Color);
        Assert.Equal(LayoutColor.FromHex("FF0000"), body[^1].Inlines.OfType<TextRun>().Single().Format.Color);
    }

    [Fact]
    public void Watermarks_are_drawn_behind_the_text()
    {
        Document doc = new Document();
        doc.Section.Watermark = new TextWatermark("DRAFT") { ColorHex = "FF0000", Opacity = 0.5 };
        doc.AddParagraph("Body text over the watermark.");
        FlowDocument flow = Import(doc);
        FlowWatermark wm = flow.Sections[0].Watermark!;
        Assert.Equal("DRAFT", wm.Text);
        Assert.True(wm.Diagonal);
        using LaidOutDocument laid = DocumentLayout.Open(flow, Options());
        LayoutDisplayList list = laid.GetPage(0).DisplayList;
        int firstWatermark = list.ToList().FindIndex(op => op is PathOp { Fill: LayoutColor c } && c.R == 255 && c.G == 0 && c.A == 128);
        int firstText = list.ToList().FindIndex(op => op is GlyphRunOp);
        Assert.True(firstWatermark >= 0, "watermark letters are filled paths");
        Assert.True(firstWatermark < firstText, "the watermark is painted first");
        // Rotated 45 degrees: the letters' points run from lower left to upper right.
        List<PathOp> letters = list.OfType<PathOp>().Where(op => op.Fill is LayoutColor c && c.R == 255 && c.G == 0).ToList();
        Assert.Equal(5, letters.Count);
        double Y(PathOp p) => p.Segments.Where(s => s.Kind != PathSegmentKind.Close).Average(s => s.Y);
        Assert.True(Y(letters[0]) > Y(letters[^1]) + 50, "D is lower on the page than T");

        FlowDocument none = Import(doc, new DocsImportOptions { IncludeWatermarks = false });
        Assert.Null(none.Sections[0].Watermark);
    }

    private static readonly byte[] Png1x1 = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Built_images_are_inline_or_floating()
    {
        Document doc = new Document();
        doc.AddParagraph("before").Image(ImageSpec.Inline(Png1x1, "image/png", 72, 36));
        doc.AddParagraph("anchor").Image(ImageSpec.Float(Png1x1, "image/png", 100, 50, new FloatingPosition
        {
            HorizontalAnchor = Chuvadi.Docs.Model.HorizontalAnchor.Page,
            HAlign = Chuvadi.Docs.Model.HorizontalAlignment.Center,
            VerticalAnchor = Chuvadi.Docs.Model.VerticalAnchor.Paragraph,
            VerticalOffsetPt = 10,
            Wrap = TextWrap.TopAndBottom,
        }));
        FlowDocument flow = DocsImporter.Import(doc, null, Options());
        List<FlowParagraph> ps = Paragraphs(flow.Sections[0].Blocks).ToList();
        ImageInline inline = Assert.Single(ps[0].Inlines.OfType<ImageInline>());
        Assert.Equal(72, inline.Width);
        AnchoredImageInline floating = Assert.Single(ps[1].Inlines.OfType<AnchoredImageInline>());
        Assert.Equal(Model.HorizontalAnchor.Page, floating.HorizontalRelativeTo);
        Assert.Equal(FloatAlignment.Center, floating.HorizontalAlignment);
        Assert.Equal(FloatWrap.TopAndBottom, floating.Wrap);
        Assert.Equal(10, floating.OffsetY);
    }

    [Fact]
    public void Floating_picture_geometry_survives_a_round_trip()
    {
        Document doc = new Document();
        ImageSpec img = ImageSpec.Float(Png1x1, "image/png", 100, 50, new FloatingPosition
        {
            HorizontalAnchor = Chuvadi.Docs.Model.HorizontalAnchor.Margin,
            HorizontalOffsetPercent = 50,
            HorizontalOffsetPt = 7,
            VerticalAnchor = Chuvadi.Docs.Model.VerticalAnchor.Paragraph,
            VerticalOffsetPt = 12,
            Wrap = TextWrap.Tight,
            WrapSide = Chuvadi.Docs.Model.WrapSide.Left,
            DistanceLeftPt = 4,
            DistanceRightPt = 6,
        });
        img.Crop = new Chuvadi.Docs.Model.ImageCrop(0.1, 0, 0.2, 0);
        img.RelativeWidth = new RelativeSize(25, RelativeSizeFrame.Margin);
        doc.AddParagraph("anchor").Image(img);
        FlowDocument flow = Import(doc);
        AnchoredImageInline a = Paragraphs(flow.Sections[0].Blocks).Single().Inlines.OfType<AnchoredImageInline>().Single();
        double content = flow.Sections[0].Page.ContentWidth;
        Assert.Equal(content * 0.25, a.Width, 1);              // relative size wins over the 100 pt
        Assert.Equal(content * 0.5, a.OffsetX, 1);             // percentage wins over the points
        Assert.Equal(12, a.OffsetY, 1);
        Assert.Equal(FloatWrap.Tight, a.Wrap);
        Assert.Equal(FloatWrapSide.Left, a.WrapSide);
        Assert.Equal(4, a.DistanceLeft, 1);
        Assert.Equal(6, a.DistanceRight, 1);
        Assert.Equal(new Model.ImageCrop(0.1, 0, 0.2, 0), a.Crop);
    }

    [Fact]
    public void Loaded_images_are_runs_in_their_paragraphs()
    {
        Document doc = new Document();
        doc.AddParagraph("Intro text");
        doc.AddParagraph().Image(ImageSpec.Inline(Png1x1, "image/png", 72, 36));   // an image-only paragraph
        doc.AddParagraph().Text("before ").Image(ImageSpec.Inline(Png1x1, "image/png", 10, 10)).Text(" after"); // mid-paragraph
        doc.AddParagraph("Caption follows");
        DocTable t = doc.AddTable(2);
        t.AddRow("left", "right");
        t.Rows[0].Cell(1).AddImage(ImageSpec.Inline(Png1x1, "image/png", 20, 20));
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        FlowDocument flow = Import(doc, null, warnings);
        List<FlowParagraph> ps = Paragraphs(flow.Sections[0].Blocks).ToList();
        Assert.Empty(ps[0].Inlines.OfType<ImageInline>());
        Assert.Equal(72, Assert.Single(ps[1].Inlines.OfType<ImageInline>()).Width);
        // Exactly where the run is: between the two text runs.
        Assert.Collection(ps[2].Inlines,
            i => Assert.Equal("before ", Assert.IsType<TextRun>(i).Text),
            i => Assert.IsType<ImageInline>(i),
            i => Assert.Equal(" after", Assert.IsType<TextRun>(i).Text));
        FlowTable ft = flow.Sections[0].Blocks.OfType<FlowTable>().Single();
        Assert.Empty(Paragraphs(ft.Rows[0].Cells[0].Blocks).SelectMany(p => p.Inlines.OfType<ImageInline>()));
        Assert.Single(Paragraphs(ft.Rows[0].Cells[1].Blocks).SelectMany(p => p.Inlines.OfType<ImageInline>()));
        Assert.Empty(warnings);

        FlowDocument noImages = Import(doc, new DocsImportOptions { IncludeImages = false });
        Assert.Empty(Paragraphs(noImages.Sections[0].Blocks).SelectMany(p => p.Inlines.OfType<ImageInline>()));
    }

    [Fact]
    public void Hidden_text_is_skipped_unless_asked_for()
    {
        Document doc = new Document();
        doc.AddParagraph().Text("shown ").Text("secret", new TextFormat { Hidden = true });
        TextRun secret = Paragraphs(Import(doc).Sections[0].Blocks).Single().Inlines.OfType<TextRun>().Last();
        Assert.True(secret.Format.Hidden);
        TextRun printed = Paragraphs(Import(doc, new DocsImportOptions { IncludeHiddenText = true }).Sections[0].Blocks).Single().Inlines.OfType<TextRun>().Last();
        Assert.False(printed.Format.Hidden);
    }

    [Fact]
    public void Positional_tabs_go_to_the_centre_and_right_edge_whatever_the_tab_stops()
    {
        Document doc = new Document();
        doc.AddParagraph().Text("Left").PositionalTab(PositionalTabAlignment.Center).Text("Mid")
            .PositionalTab(PositionalTabAlignment.Right, PositionalTabBase.Margin, Chuvadi.Docs.Model.TabLeader.Dot).Text("Right");
        FlowDocument flow = Import(doc);
        List<TabInline> tabs = Paragraphs(flow.Sections[0].Blocks).First().Inlines.OfType<TabInline>().ToList();
        Assert.Equal(2, tabs.Count);
        Assert.Equal(new Model.PositionalTab(Model.TabAlignment.Center, false, Model.TabLeader.None), tabs[0].Positional);
        Assert.Equal(new Model.PositionalTab(Model.TabAlignment.Right, false, Model.TabLeader.Dot), tabs[1].Positional);

        using LaidOutDocument laid = DocumentLayout.Open(flow, Options());
        LaidOutPage page = laid.GetPage(0);
        PageGeometry g = flow.Sections[0].Page;
        List<GlyphRunOp> runs = page.DisplayList.OfType<GlyphRunOp>().ToList();
        GlyphRunOp mid = runs.Single(r => r.Text.Contains("Mid"));
        GlyphRunOp right = runs.Single(r => r.Text.Contains("Right"));
        double centre = g.MarginLeft + (g.ContentWidth / 2);
        Assert.InRange(mid.X + (mid.Width / 2), centre - 1, centre + 1);
        Assert.InRange(right.X + right.Width, g.Width - g.MarginRight - 1, g.Width - g.MarginRight + 0.5);
        Assert.Contains(runs, r => r.Text.Trim('.').Length == 0 && r.Text.Contains('.')); // the dot leader
    }

    [Fact]
    public void Positional_tabs_in_a_word_made_header()
    {
        // POI ThreeColHeadFoot.docx: "First header column" ptab-centre "Mid header" ptab-right "Right header!".
        using FileStream fs = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "docx", "ThreeColHeadFoot.docx"));
        using LaidOutDocument laid = DocsLayout.Open(fs, null, Options());
        LaidOutPage page = laid.GetPage(0);
        LayoutBox header = page.Boxes.First(b => b.Kind == LayoutBoxKind.Header);
        List<GlyphRunOp> inHeader = page.DisplayList.OfType<GlyphRunOp>().Where(r => r.Y <= header.Rect.Bottom).ToList();
        GlyphRunOp right = inHeader.Single(r => r.Text.Contains("Right header"));
        GlyphRunOp mid = inHeader.Single(r => r.Text.Contains("Mid header"));
        Assert.True(right.X + right.Width > page.WidthPt - 100, "the right column ends at the right margin");
        Assert.InRange(mid.X + (mid.Width / 2), (page.WidthPt / 2) - 40, (page.WidthPt / 2) + 40);
    }

    [Theory]
    [InlineData("en-US", 612, 792)]
    [InlineData("en-IN", 595.3, 841.9)]
    public void A_section_without_a_page_size_follows_the_culture_D30(string culture, double width, double height)
    {
        Document doc = new Document();
        doc.AddParagraph("No paper size stated");
        doc.Section.Page.SizeStated = false;
        byte[] bytes = Save(doc);
        using LaidOutDocument laid = DocsLayout.Open(new MemoryStream(bytes), null, Options(culture));
        Assert.Equal(width, laid.GetPage(0).WidthPt, 1);
        Assert.Equal(height, laid.GetPage(0).HeightPt, 1);

        // Landscape with no stated size (in memory: Chuvadi.Docs writes a w:pgSz for any landscape section).
        doc.Section.Page.Orientation = PageOrientation.Landscape;
        doc.Section.Page.SizeStated = false;
        PageGeometry landscape = DocsImporter.Import(doc, null, Options(culture)).Sections[0].Page;
        Assert.Equal(height, landscape.Width, 1);
        Assert.Equal(width, landscape.Height, 1);

        // A stated size is kept whatever the culture.
        Document a4 = new Document();
        a4.AddParagraph("A4 stated");
        using LaidOutDocument stated = DocsLayout.Open(new MemoryStream(Save(a4)), null, Options("en-US"));
        Assert.Equal(595.3, stated.GetPage(0).WidthPt, 0);
    }

    [Fact]
    public void The_document_default_tab_stop_is_used()
    {
        Document doc = new Document { DefaultTabStopPt = 72 };
        doc.AddParagraph().Text("a").Tab().Text("b");
        FlowDocument flow = Import(doc);
        Assert.Equal(72, flow.DefaultTabStop);
        using LaidOutDocument laid = DocumentLayout.Open(flow, Options());
        GlyphRunOp b = laid.GetPage(0).DisplayList.OfType<GlyphRunOp>().Single(r => r.Text == "b" || r.Text.EndsWith("b"));
        double x = b.X + b.Glyphs[b.Text.IndexOf('b')].X;
        Assert.Equal(flow.Sections[0].Page.MarginLeft + 72, x, 0.5);

        Assert.Equal(36, Import(new Document()).DefaultTabStop);
    }

    [Fact]
    public void A_workbook_renamed_docx_is_rejected_by_the_document_reader()
    {
        // A workbook written with Chuvadi.Sheets (a test-only reference of this project).
        Chuvadi.Sheets.Model.Workbook wb = new Chuvadi.Sheets.Model.Workbook();
        wb.AddSheet("Charges").Cell("A1").Value = "Schedule of Charges";
        using MemoryStream saved = new MemoryStream();
        wb.SaveTo(saved);
        byte[] xlsx = saved.ToArray();
        LayoutFormatException ex = Assert.Throws<LayoutFormatException>(() => DocsLayout.Open(new MemoryStream(xlsx), null, Options()));
        Assert.IsType<DocxFormatException>(ex.InnerException);
    }

    [Fact]
    public void Corrupt_and_limited_input_fail_cleanly()
    {
        byte[] notDocx = System.Text.Encoding.ASCII.GetBytes("PK\u0003\u0004word/document.xml then garbage");
        Assert.Throws<LayoutFormatException>(() => DocsLayout.Open(new MemoryStream(notDocx), null, Options()));

        Document doc = new Document();
        DocTable outer = doc.AddTable(1);
        DocTable current = outer;
        for (int i = 0; i < 6; i++)
        {
            current.AddRow("level " + i);
            current = current.Rows[0].Cell(0).AddTable(1);
        }
        current.AddRow("deepest");
        LayoutOptions tight = Options();
        tight.Limits = new LayoutLimits { MaxNestingDepth = 3 };
        Assert.Throws<LayoutLimitExceededException>(() => DocsLayout.Open(new MemoryStream(Save(doc)), null, tight).ResolvePageCount());
    }
}

/// <summary>Word-made documents from the Apache POI corpus (Fixtures/docx, Apache-2.0).</summary>
public class DocsWordFixtureTests
{
    private static LaidOutDocument Open(string file, List<LayoutWarning>? warnings = null)
    {
        using FileStream fs = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "docx", file));
        return DocsLayout.Open(fs, null, DocsImportTests.Options());
    }

    [Theory]
    [InlineData("SampleDoc.docx")]
    [InlineData("TestDocument.docx")]
    [InlineData("Headers.docx")]
    [InlineData("DiffFirstPageHeadFoot.docx")]
    [InlineData("ComplexNumberedLists.docx")]
    [InlineData("ThreeColHeadFoot.docx")]
    [InlineData("headerPic.docx")]
    [InlineData("issue_51265_3.docx")]
    public void Word_documents_lay_out_to_the_page_count_word_recorded(string file)
    {
        using LaidOutDocument doc = Open(file);
        int pages = doc.ResolvePageCount();
        int recorded = RecordedPages(file);
        Assert.Equal(recorded, pages);
        foreach (LaidOutPage page in doc.GetPages())
        {
            Assert.NotEmpty(page.DisplayList);
        }
    }

    [Fact]
    public void Words_recorded_page_count_is_the_first_estimate()
    {
        using LaidOutDocument doc = Open("Headers.docx");
        Assert.False(doc.IsPageCountFinal);
        Assert.Equal(3, doc.PageCount);
        Assert.Equal(3, doc.ResolvePageCount());
        Assert.True(doc.IsPageCountFinal);
    }

    [Fact]
    public void Pictures_in_headers_and_body_are_placed_at_their_runs()
    {
        // Chuvadi.Docs 6b keeps loaded pictures as image runs: three in the first page's header, four in the body.
        using LaidOutDocument doc = Open("issue_51265_3.docx");
        Assert.Equal(1, doc.ResolvePageCount());
        LaidOutPage page = doc.GetPage(0);
        List<ImageOp> images = page.DisplayList.OfType<ImageOp>().ToList();
        Assert.Equal(7, images.Count);
        LayoutBox header = page.Boxes.First(b => b.Kind == LayoutBoxKind.Header);
        Assert.Equal(3, images.Count(i => i.Destination.Bottom <= header.Rect.Bottom + 0.5));
        // Only the Info notes of metric-compatible substitutions (Calibri → Carlito; D-116 Q6), nothing that changes the output.
        Assert.All(doc.Warnings, w => Assert.Equal(LayoutWarningCodes.MetricFontSubstitution, w.Code));
    }

    [Fact]
    public void Loaded_hyperlinks_carry_their_targets()
    {
        // Chuvadi.Docs 1.5.0 sets Run.HyperlinkUrl from the relationship: the PDF and SVG get live links.
        using LaidOutDocument doc = Open("TestDocument.docx");
        List<LinkOp> links = doc.GetPages().SelectMany(p => p.DisplayList.OfType<LinkOp>()).ToList();
        Assert.NotEmpty(links);
        Assert.All(links, l => Assert.False(string.IsNullOrEmpty(l.Uri ?? l.Anchor)));
    }

    /// <summary>The page count Word stored when it last saved the file (docProps/app.xml).</summary>
    private static int RecordedPages(string file)
    {
        using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "docx", file));
        using StreamReader r = new StreamReader(zip.GetEntry("docProps/app.xml")!.Open());
        string xml = r.ReadToEnd();
        int i = xml.IndexOf("<Pages>", StringComparison.Ordinal) + 7;
        return int.Parse(xml.AsSpan(i, xml.IndexOf("</Pages>", i, StringComparison.Ordinal) - i), System.Globalization.CultureInfo.InvariantCulture);
    }
}
