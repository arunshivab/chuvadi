using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Print.Pdf.Raster.Tests;

/// <summary>
/// D-235 L1.3a (print the crop box) and L2 (PdfFontInspector). The PDFs are written by hand (<see cref="Raw"/>) so
/// the boxes, fonts and fields are exactly as stated; the embedded-font and encrypted cases use the Chuvadi.Pdf builder,
/// which embeds real TrueType programs (LiPi Sans).
/// </summary>
public class PageBoxAndFontTests
{
    // ------------------------------------------------------------------ hand-built PDFs

    /// <summary>A one-page PDF: page dictionary entries, content, and extra objects numbered from 5 (catalog extra too).</summary>
    internal static byte[] Raw(string pageEntries, string content, string catalogExtra = "", params string[] extra)
    {
        var bodies = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R " + catalogExtra + " >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R " + pageEntries + " /Contents 4 0 R >>",
            "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "\nendstream",
        };
        bodies.AddRange(extra);
        var output = new MemoryStream();
        void Write(string s) { byte[] b = Encoding.ASCII.GetBytes(s); output.Write(b, 0, b.Length); }
        Write("%PDF-1.7\n");
        var offsets = new long[bodies.Count + 1];
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets[i + 1] = output.Position;
            Write((i + 1) + " 0 obj\n" + bodies[i] + "\nendobj\n");
        }
        long xref = output.Position;
        Write("xref\n0 " + (bodies.Count + 1) + "\n0000000000 65535 f \n");
        for (int i = 1; i <= bodies.Count; i++) Write(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Size " + (bodies.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        return output.ToArray();
    }

    /// <summary>
    /// 200 x 100 pt media box. Black square A at x 0..50, y 50..100 (outside the crop box); black square B at
    /// x 100..150, y 0..50 (inside). Crop box x 50..150, y 0..80: 100 x 80 pt.
    /// </summary>
    private static byte[] Cropped(int rotate = 0, string? crop = "[50 0 150 80]")
        => Raw("/MediaBox [0 0 200 100]" + (crop is null ? "" : " /CropBox " + crop) + (rotate != 0 ? " /Rotate " + rotate : "") + " /Resources << >>",
               "0 0 0 rg 0 50 50 50 re f 100 0 50 50 re f");

    private static RasterPage Render(byte[] pdf, PdfPageBox box = PdfPageBox.CropBox, PageRotation rotation = PageRotation.None, double dpiX = 72, double dpiY = 72)
    {
        using IRasterDocument doc = new PdfPageRasterizer { PageBox = box }.Open(pdf);
        return doc.Render(1, new RasterRequest { DpiX = dpiX, DpiY = dpiY, PixelFormat = RasterPixelFormat.Gray8, Rotation = rotation }, CancellationToken.None);
    }

    private static bool Black(RasterPage page, int x, int y) => PdfPageRasterizerTests.Gray(page, x, y) < 64;

    /// <summary>The bounding box of the black pixels: left, top, right, bottom (inclusive).</summary>
    private static (int L, int T, int R, int B) Ink(RasterPage page)
    {
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (int y = 0; y < page.PixelHeight; y++)
            for (int x = 0; x < page.PixelWidth; x++)
                if (Black(page, x, y)) { l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x); b = Math.Max(b, y); }
        return (l, t, r, b);
    }

    private static void InkAt(RasterPage page, int l, int t, int r, int b)
    {
        (int L, int T, int R, int B) ink = Ink(page);
        Assert.True(Math.Abs(ink.L - l) <= 1 && Math.Abs(ink.T - t) <= 1 && Math.Abs(ink.R - r) <= 1 && Math.Abs(ink.B - b) <= 1,
                    $"black pixels at {ink}, expected about ({l}, {t}, {r}, {b})");
    }
    private static bool White(RasterPage page, int x, int y) => PdfPageRasterizerTests.Gray(page, x, y) > 200;

    // ------------------------------------------------------------------ page boxes

    [Fact]
    public void The_crop_box_is_printed_by_default()
    {
        Assert.Equal(PdfPageBox.CropBox, new PdfPageRasterizer().PageBox);
        using IRasterDocument doc = new PdfPageRasterizer().Open(Cropped());
        Assert.Equal(new PageDimensions(100, 80), doc.GetPageSize(1));

        using RasterPage page = Render(Cropped());
        Assert.Equal((100, 80), (page.PixelWidth, page.PixelHeight));
        // Square B: crop x 50..100, from the top 30..80. Square A is cut away (it would be at the top left).
        Assert.True(Black(page, 75, 55), "square B is inside the crop box");
        Assert.True(White(page, 25, 10), "square A is outside the crop box");
        Assert.True(White(page, 25, 55));
        Assert.True(White(page, 75, 10));
        InkAt(page, 50, 30, 99, 79);
    }

    [Fact]
    public void The_media_box_can_still_be_printed()
    {
        using IRasterDocument doc = new PdfPageRasterizer { PageBox = PdfPageBox.MediaBox }.Open(Cropped());
        Assert.Equal(new PageDimensions(200, 100), doc.GetPageSize(1));
        using RasterPage page = Render(Cropped(), PdfPageBox.MediaBox);
        Assert.Equal((200, 100), (page.PixelWidth, page.PixelHeight));
        Assert.True(Black(page, 25, 25), "square A");
        Assert.True(Black(page, 125, 75), "square B");
        Assert.Equal(PdfPageRasterizerTests.Gray(page, 25, 25), PdfPageRasterizerTests.Gray(page, 125, 75));
    }

    [Fact]
    public void Without_a_crop_box_the_media_box_is_printed()
    {
        using RasterPage page = Render(Cropped(crop: null));
        Assert.Equal((200, 100), (page.PixelWidth, page.PixelHeight));
        Assert.True(Black(page, 25, 25));
    }

    [Fact]
    public void A_crop_box_outside_the_media_box_is_clipped_to_it_and_a_disjoint_one_is_ignored()
    {
        using (IRasterDocument doc = new PdfPageRasterizer().Open(Cropped(crop: "[150 -20 300 60]")))
            Assert.Equal(new PageDimensions(50, 60), doc.GetPageSize(1));
        using (IRasterDocument doc = new PdfPageRasterizer().Open(Cropped(crop: "[300 300 400 400]")))
            Assert.Equal(new PageDimensions(200, 100), doc.GetPageSize(1));
        using (IRasterDocument doc = new PdfPageRasterizer().Open(Cropped(crop: "[150 80 50 0]")))
            Assert.Equal(new PageDimensions(100, 80), doc.GetPageSize(1)); // corners in either order
    }

    [Fact]
    public void Page_rotate_turns_the_cropped_page()
    {
        using IRasterDocument doc = new PdfPageRasterizer().Open(Cropped(90));
        Assert.Equal(new PageDimensions(80, 100), doc.GetPageSize(1));
        using RasterPage page = Render(Cropped(90));
        Assert.Equal((80, 100), (page.PixelWidth, page.PixelHeight));
        // Square B (bottom right of the upright crop) turns clockwise to the bottom left.
        Assert.True(Black(page, 25, 75), "square B, bottom left");
        Assert.True(White(page, 60, 25));
        Assert.True(White(page, 60, 75));
        InkAt(page, 0, 50, 49, 99);
    }

    [Theory]
    [InlineData(PageRotation.Rotate180, 0, 0, 49, 49)]     // bottom right -> top left
    [InlineData(PageRotation.Clockwise270, 30, 0, 79, 49)] // bottom right -> top right of the 80 x 100 page
    [InlineData(PageRotation.Clockwise90, 0, 50, 49, 99)]  // bottom right -> bottom left
    public void Requested_rotation_turns_the_cropped_page(PageRotation rotation, int l, int t, int r, int b)
    {
        using RasterPage page = Render(Cropped(), rotation: rotation);
        InkAt(page, l, t, r, b);
        Assert.Equal(rotation == PageRotation.Rotate180 ? (100, 80) : (80, 100), (page.PixelWidth, page.PixelHeight));
    }

    [Theory]
    [InlineData(90, 0, 50, 49, 99)]
    [InlineData(180, 0, 0, 49, 49)]
    [InlineData(270, 30, 0, 79, 49)]
    [InlineData(-90, 30, 0, 79, 49)]
    [InlineData(450, 0, 50, 49, 99)]
    public void Every_page_rotate_value_turns_the_cropped_page(int rotate, int l, int t, int r, int b)
    {
        using RasterPage page = Render(Cropped(rotate));
        InkAt(page, l, t, r, b);
    }

    [Fact]
    public void Rotate_270_on_the_page_and_90_requested_cancel_out()
    {
        using RasterPage page = Render(Cropped(270), rotation: PageRotation.Clockwise90);
        Assert.Equal((100, 80), (page.PixelWidth, page.PixelHeight));
        Assert.True(Black(page, 75, 55));
        Assert.True(White(page, 25, 10));
        InkAt(page, 50, 30, 99, 79);
    }

    [Fact]
    public void Anisotropic_resolution_keeps_the_crop()
    {
        using RasterPage page = Render(Cropped(), dpiX: 144, dpiY: 72);
        Assert.Equal((200, 80), (page.PixelWidth, page.PixelHeight));
        Assert.True(Black(page, 150, 55));
        Assert.True(White(page, 50, 10));
        InkAt(page, 100, 30, 199, 79);
    }

    [Fact]
    public void Trim_and_bleed_boxes_default_to_the_crop_box_and_are_used_when_present()
    {
        byte[] pdf = Raw("/MediaBox [0 0 200 100] /CropBox [50 0 150 80] /TrimBox [100 0 150 50] /Resources << >>",
                         "0 0 0 rg 0 50 50 50 re f 100 0 50 50 re f");
        using (IRasterDocument doc = new PdfPageRasterizer { PageBox = PdfPageBox.TrimBox }.Open(pdf))
            Assert.Equal(new PageDimensions(50, 50), doc.GetPageSize(1));
        using (IRasterDocument doc = new PdfPageRasterizer { PageBox = PdfPageBox.BleedBox }.Open(pdf))
            Assert.Equal(new PageDimensions(100, 80), doc.GetPageSize(1));
        using RasterPage trim = Render(pdf, PdfPageBox.TrimBox);
        Assert.True(Black(trim, 25, 25), "the trim box is square B");
        InkAt(trim, 0, 0, 49, 49);
    }

    [Fact]
    public void An_unknown_page_box_value_is_refused()
        => Assert.Throws<InvalidOperationException>(() => new PdfPageRasterizer { PageBox = (PdfPageBox)9 }.Open(Cropped()));

    // ------------------------------------------------------------------ font inspector

    private static byte[] Built(Action<PdfDocumentBuilder, PageBuilder> draw, EncryptionOptions? encryption = null)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().UseLipiFonts();
        PageBuilder page = builder.AddPage(PageSize.A4);
        draw(builder, page);
        return builder.ToByteArray(encryption);
    }

    [Fact]
    public void Embedded_fonts_only_can_go_as_is()
    {
        byte[] pdf = Built((_, p) => p.DrawText("Tamil தமிழ் and Latin", 72, 72, "Lipi", 12, new Chuvadi.Pdf.Authoring.Color(0, 0, 0)));
        var inspector = new PdfFontInspector();
        Assert.Equal(FontEmbedding.AllEmbedded, inspector.Inspect(pdf));
        Assert.NotNull(inspector.LastReport);
        Assert.Empty(inspector.LastReport!.NotEmbedded);
        Assert.Null(inspector.LastError);
    }

    [Fact]
    public void A_page_without_fonts_can_go_as_is()
        => Assert.Equal(FontEmbedding.AllEmbedded, new PdfFontInspector().Inspect(Cropped()));

    [Fact]
    public void A_standard_14_font_that_is_not_embedded_is_reported_with_its_page()
    {
        byte[] pdf = Raw("/MediaBox [0 0 200 100] /Resources << /Font << /F1 5 0 R >> >>",
                         "BT /F1 12 Tf 10 50 Td (Hello) Tj ET", "",
                         "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var inspector = new PdfFontInspector();
        Assert.Equal(FontEmbedding.NotAllEmbedded, inspector.Inspect(pdf));
        FontUse use = Assert.Single(inspector.LastReport!.NotEmbedded);
        Assert.Equal((1, "Helvetica", "F1"), (use.PageNumber, use.BaseFont, use.ResourceName));
    }

    [Fact]
    public void Fields_that_need_appearances_keep_an_embedded_pdf_off_the_as_is_routes()
    {
        byte[] pdf = Raw("/MediaBox [0 0 400 300] /Resources << >> /Annots [5 0 R]", "0 g 40 200 320 1 re f",
                         "/AcroForm << /Fields [5 0 R] /NeedAppearances true >>",
                         "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (Arun) /Rect [50 150 350 180] /F 4 /P 3 0 R >>");
        var inspector = new PdfFontInspector();
        Assert.Equal(FontEmbedding.NeedsGeneratedAppearances, inspector.Inspect(pdf));
        Assert.True(inspector.LastReport!.NeedsGeneratedAppearances);
        Assert.Equal(FontEmbeddingVerdict.AllEmbedded, inspector.LastReport.Verdict);
    }

    [Fact]
    public void A_comment_without_an_appearance_needs_one_generated()
        => Assert.Equal(FontEmbedding.NeedsGeneratedAppearances, new PdfFontInspector().Inspect(
               Raw("/MediaBox [0 0 400 300] /Resources << >> /Annots [5 0 R]", "", "",
                   "<< /Type /Annot /Subtype /Square /Rect [100 100 300 200] /C [1 0 0] /F 4 >>")));

    [Fact]
    public void Not_embedded_wins_over_missing_appearances()
        => Assert.Equal(FontEmbedding.NotAllEmbedded, PdfFontInspector.Map(FontEmbeddingVerdict.NotAllEmbedded, true));

    [Fact]
    public void Encrypted_pdfs_need_the_password()
    {
        byte[] pdf = Built((_, p) => p.DrawText("Secret", 72, 72, "Lipi", 12, new Chuvadi.Pdf.Authoring.Color(0, 0, 0)), EncryptionOptions.Aes256("open-sesame"));
        var inspector = new PdfFontInspector();
        Assert.Equal(FontEmbedding.Unknown, inspector.Inspect(pdf));
        Assert.NotNull(inspector.LastError);
        Assert.Null(inspector.LastReport);

        inspector.Password = "wrong";
        Assert.Equal(FontEmbedding.Unknown, inspector.Inspect(pdf));

        inspector.Password = "open-sesame";
        Assert.Equal(FontEmbedding.AllEmbedded, inspector.Inspect(pdf));
        Assert.Null(inspector.LastError);
    }

    [Fact]
    public void Unreadable_input_is_unknown_not_an_exception()
    {
        var inspector = new PdfFontInspector();
        Assert.Equal(FontEmbedding.Unknown, inspector.Inspect(ReadOnlyMemory<byte>.Empty));
        Assert.Equal(FontEmbedding.Unknown, inspector.Inspect(Encoding.ASCII.GetBytes("not a pdf")));
        Assert.NotNull(inspector.LastError);
    }

    [Fact]
    public void The_router_sends_an_embedded_pdf_as_is_once_the_inspector_says_so()
    {
        byte[] embedded = Built((_, p) => p.DrawText("Embedded", 72, 72, "Lipi", 12, new Chuvadi.Pdf.Authoring.Color(0, 0, 0)));
        using var router = new PrintRouter(new RecordingRoute(PrintRouteKind.PdfAsIs, needsAllEmbedded: true), new RecordingRoute(PrintRouteKind.Raster, needsAllEmbedded: false))
        {
            FontInspector = new PdfFontInspector()
        };
        Assert.Equal(PrintRouteKind.PdfAsIs, router.Print(new PrintJob(embedded)).Route);

        byte[] notEmbedded = Raw("/MediaBox [0 0 200 100] /Resources << /Font << /F1 5 0 R >> >>", "BT /F1 12 Tf 10 50 Td (Hi) Tj ET", "",
                                 "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        PrintRouteResult fallback = router.Print(new PrintJob(notEmbedded));
        Assert.Equal(PrintRouteKind.Raster, fallback.Route);
        Assert.Equal(FontEmbedding.NotAllEmbedded, fallback.Decision.Fonts);
    }

    /// <summary>A route that prints nothing; the as-is kind is usable only when every font is embedded.</summary>
    private sealed class RecordingRoute : IPrintRoute
    {
        private readonly bool _needsAllEmbedded;

        public RecordingRoute(PrintRouteKind kind, bool needsAllEmbedded)
        {
            Kind = kind;
            _needsAllEmbedded = needsAllEmbedded;
        }

        public PrintRouteKind Kind { get; }
        public string Name => Kind.ToString();

        public PrintRouteCheck Check(PrintRouteContext context, CancellationToken cancellationToken)
            => !_needsAllEmbedded || context.Fonts == FontEmbedding.AllEmbedded ? PrintRouteCheck.Usable : PrintRouteCheck.Skip("fonts");

        public PrintResult Print(PrintJob job, CancellationToken cancellationToken) => PrintResult.Printed(job.JobId, 1);
    }
}
