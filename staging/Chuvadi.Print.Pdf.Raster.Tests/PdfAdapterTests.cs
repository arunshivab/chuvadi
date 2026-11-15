using Chuvadi.Pdf.Documents;
using Chuvadi.Print.ManualTests;
using Box = Chuvadi.Print.Pdf.Raster.Tests.MiniPdf.Box;

namespace Chuvadi.Print.Pdf.Raster.Tests;

/// <summary>
/// The test page: 200 x 100 pt, a black 50 x 50 square in its top-left corner (PDF y 50..100).
/// At 72 dpi pixel (25,25) is black and (150,75) is white.
/// </summary>
public class PdfPageRasterizerTests
{
    internal static byte[] Square(int rotate = 0, Box? annotation = null) =>
        new MiniPdf().Add(200, 100, rotate, annotation, new Box(0, 50, 50, 50)).ToArray();

    internal static byte Gray(RasterPage page, int x, int y)
    {
        Span<byte> row = page.GetRow(y);
        return page.PixelFormat switch
        {
            RasterPixelFormat.Gray8 => row[x],
            RasterPixelFormat.Rgb24 => row[x * 3 + 1],
            _ => row[x * 4 + 1]
        };
    }

    private static RasterPage Render(byte[] pdf, RasterRequest request, int page = 1)
    {
        using IRasterDocument doc = new PdfPageRasterizer().Open(pdf);
        return doc.Render(page, request, CancellationToken.None);
    }

    [Fact]
    public void ReportsPageCountAndUprightSizes()
    {
        byte[] pdf = new MiniPdf().Add(595, 842).Add(200, 100, 90).Add(200, 100, 180).ToArray();
        using IRasterDocument doc = new PdfPageRasterizer().Open(pdf);
        Assert.Equal(3, doc.PageCount);
        Assert.Equal(new PageDimensions(595, 842), doc.GetPageSize(1));
        Assert.Equal(new PageDimensions(100, 200), doc.GetPageSize(2));
        Assert.Equal(new PageDimensions(200, 100), doc.GetPageSize(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetPageSize(4));
    }

    [Fact]
    public void RendersAtTheRequestedResolution()
    {
        using RasterPage page = Render(Square(), new RasterRequest { DpiX = 72, DpiY = 72, PixelFormat = RasterPixelFormat.Gray8 });
        Assert.Equal(200, page.PixelWidth);
        Assert.Equal(100, page.PixelHeight);
        Assert.True(Gray(page, 25, 25) < 64, "square is black");
        Assert.True(Gray(page, 150, 75) > 200, "rest is white");

        using RasterPage hi = Render(Square(), new RasterRequest { DpiX = 144, DpiY = 144, PixelFormat = RasterPixelFormat.Gray8 });
        Assert.Equal(400, hi.PixelWidth);
        Assert.True(Gray(hi, 50, 50) < 64);
    }

    [Fact]
    public void RequestedRotationTurnsClockwise()
    {
        using RasterPage page = Render(Square(), new RasterRequest { DpiX = 72, DpiY = 72, PixelFormat = RasterPixelFormat.Gray8, Rotation = PageRotation.Clockwise90 });
        Assert.Equal(100, page.PixelWidth);
        Assert.Equal(200, page.PixelHeight);
        Assert.True(Gray(page, 75, 25) < 64, "top-left square moved to the top-right");
        Assert.True(Gray(page, 25, 25) > 200);
    }

    [Fact]
    public void PageRotateIsApplied()
    {
        using RasterPage page = Render(Square(rotate: 90), new RasterRequest { DpiX = 72, DpiY = 72, PixelFormat = RasterPixelFormat.Gray8 });
        Assert.Equal(100, page.PixelWidth);
        Assert.Equal(200, page.PixelHeight);
        Assert.True(Gray(page, 75, 25) < 64, "renders upright, as viewers show it");
    }

    [Fact]
    public void AnisotropicResolutionIsResampled()
    {
        using RasterPage page = Render(Square(), new RasterRequest { DpiX = 72, DpiY = 144, PixelFormat = RasterPixelFormat.Gray8 });
        Assert.Equal(200, page.PixelWidth);
        Assert.Equal(200, page.PixelHeight);
        Assert.True(Gray(page, 25, 50) < 64);
        Assert.True(Gray(page, 25, 150) > 200);
    }

    [Theory]
    [InlineData(RasterPixelFormat.Bgra32, RasterPixelFormat.Bgra32)]
    [InlineData(RasterPixelFormat.Rgb24, RasterPixelFormat.Rgb24)]
    [InlineData(RasterPixelFormat.Gray8, RasterPixelFormat.Gray8)]
    [InlineData(RasterPixelFormat.BlackWhite1, RasterPixelFormat.Gray8)]
    public void DeliversTheRequestedFormat(RasterPixelFormat requested, RasterPixelFormat delivered)
    {
        using RasterPage page = Render(Square(), new RasterRequest { DpiX = 72, DpiY = 72, PixelFormat = requested });
        Assert.Equal(delivered, page.PixelFormat);
        Assert.True(Gray(page, 25, 25) < 64);
        Assert.True(Gray(page, 150, 75) > 200);
        if (delivered == RasterPixelFormat.Bgra32) Assert.Equal(255, page.GetRow(75)[150 * 4 + 3]);
    }

    [Fact]
    public void AnnotationsPrintOnlyWhenAsked()
    {
        // A Square annotation whose appearance fills x 150..190, y 10..40 (pixels 150..190 x 60..90).
        byte[] pdf = Square(annotation: new Box(150, 10, 40, 30));
        using IRasterDocument doc = new PdfPageRasterizer().Open(pdf);
        using (RasterPage with = doc.Render(1, new RasterRequest { DpiX = 72, DpiY = 72, PixelFormat = RasterPixelFormat.Gray8 }, CancellationToken.None))
            Assert.True(Gray(with, 170, 75) < 64, "annotation printed");
        using (RasterPage without = doc.Render(1, new RasterRequest { DpiX = 72, DpiY = 72, PixelFormat = RasterPixelFormat.Gray8, IncludeAnnotations = false }, CancellationToken.None))
        {
            Assert.True(Gray(without, 170, 75) > 200, "annotation left out");
            Assert.True(Gray(without, 25, 25) < 64, "page content still there");
        }
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void A_comment_prints_only_with_IncludeAnnotations_whatever_IncludeFormFields_says(bool annotations, bool formFields, bool printed)
    {
        // 0.2.1: with both off the renderer drew the comment anyway (its own AnnotationMode defaulted to View).
        byte[] pdf = Square(annotation: new Box(150, 10, 40, 30));
        using RasterPage page = Render(pdf, new RasterRequest
        {
            DpiX = 72, DpiY = 72, PixelFormat = RasterPixelFormat.Gray8,
            IncludeAnnotations = annotations, IncludeFormFields = formFields
        });
        Assert.Equal(printed, Gray(page, 170, 75) < 64);
        Assert.True(Gray(page, 25, 25) < 64, "page content still there");
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void A_form_field_prints_only_with_IncludeFormFields_whatever_IncludeAnnotations_says(bool annotations, bool formFields, bool printed)
    {
        // A text field whose appearance is a black bar at x 150..190, y 10..40 (pixels 150..190 x 60..90).
        const string ap = "0 g 0 0 40 30 re f";
        byte[] pdf = PageBoxAndFontTests.Raw(
            "/MediaBox [0 0 200 100] /Resources << >> /Annots [5 0 R]", "0 g 0 50 50 50 re f",
            "/AcroForm << /Fields [5 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /Rect [150 10 190 40] /F 4 /P 3 0 R /AP << /N 6 0 R >> >>",
            "<< /Type /XObject /Subtype /Form /BBox [0 0 40 30] /Resources << >> /Length " + ap.Length + " >>\nstream\n" + ap + "\nendstream");
        using RasterPage page = Render(pdf, new RasterRequest
        {
            DpiX = 72, DpiY = 72, PixelFormat = RasterPixelFormat.Gray8,
            IncludeAnnotations = annotations, IncludeFormFields = formFields
        });
        Assert.Equal(printed, Gray(page, 170, 75) < 64);
        Assert.True(Gray(page, 25, 25) < 64, "page content still there");
    }

    [Fact]
    public void HonoursCancellation()
    {
        using IRasterDocument doc = new PdfPageRasterizer().Open(Square());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => doc.Render(1, new RasterRequest(), cts.Token));
    }

    [Fact]
    public void PrintJobPrintsPdfPagesWithOneAuditEvent()
    {
        byte[] pdf = new MiniPdf().Add(200, 100, 0, null, new Box(0, 50, 50, 50)).Add(200, 100, 0, null, new Box(150, 0, 50, 50)).ToArray();
        var target = new RecordingTarget();
        var audit = new MemoryAuditSink();
        PrintResult result = new PrintJob(pdf).WithRasterizer(new PdfPageRasterizer()).WithCopies(2, collate: true)
            .WithAuditSink(audit).PrintTo(target);
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(4, target.Pages.Count);
        Assert.All(target.Pages, p => Assert.True(p.BlackPixels > 0));
        Assert.Single(audit.Events);
    }

    [Fact]
    public void WritesPreviewPngsForVisualCheck()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "print-pdf-preview");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        byte[] pdf = new MiniPdf().Add(200, 100, 0, new Box(150, 10, 40, 30), new Box(0, 50, 50, 50)).Add(200, 100, 90, null, new Box(0, 50, 50, 50)).ToArray();
        PrintResult result = new PrintJob(pdf).WithRasterizer(new PdfPageRasterizer()).WithPaper(PaperSize.A5)
            .WithAutoRotate().PrintTo(new ImageFileTarget(folder).WithDpi(100).WithGuides());
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(2, Directory.GetFiles(folder, "*.png").Length);
        Console.WriteLine("Preview PNGs: " + folder);

        // 0.2.1: the same job with annotations and form fields both off; page 1 shows the square without the annotation.
        string bothOff = Path.Combine(AppContext.BaseDirectory, "print-pdf-preview-both-off");
        if (Directory.Exists(bothOff)) Directory.Delete(bothOff, true);
        result = new PrintJob(pdf).WithRasterizer(new PdfPageRasterizer()).WithPaper(PaperSize.A5).WithAutoRotate()
            .WithAnnotations(false).WithFormFields(false).PrintTo(new ImageFileTarget(bothOff).WithDpi(100).WithGuides());
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(2, Directory.GetFiles(bothOff, "*.png").Length);
        Console.WriteLine("Preview PNGs, both off: " + bothOff);
    }
}
