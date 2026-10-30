using Chuvadi.Pdf.Documents;
using Chuvadi.Print.ManualTests;
using Box = Chuvadi.Print.Pdf.Tests.MiniPdf.Box;

namespace Chuvadi.Print.Pdf.Tests;

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
    }
}

/// <summary>Print to PDF: output pages are rendered back with PdfPageRasterizer and checked.</summary>
public class PdfFileTargetTests
{
    private static RasterPage RenderPage(byte[] pdf, int page)
    {
        using IRasterDocument doc = new PdfPageRasterizer().Open(pdf);
        return doc.Render(page, new RasterRequest { DpiX = 72, DpiY = 72, PixelFormat = RasterPixelFormat.Gray8 }, CancellationToken.None);
    }

    private static (PrintResult Result, byte[] Pdf, PdfFileTarget Target) Print(PrintJob job, Action<PdfFileTarget>? configure = null)
    {
        var output = new MemoryStream();
        var target = new PdfFileTarget(output);
        configure?.Invoke(target);
        PrintResult result = job.SubmitTo(target);
        return (result, output.ToArray(), target);
    }

    private static PrintJob Job(byte[] pdf) =>
        new PrintJob(pdf).WithPaper(PaperSize.A4).WithMargins(Margins.None).WithScale(ScaleMode.ActualSize)
            .WithAlignment(ContentAlignment.TopLeft);

    [Fact]
    public void PlacesThePageAsVectorsAtActualSize()
    {
        var (result, pdf, target) = Print(Job(PdfPageRasterizerTests.Square()));
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf));
        Assert.Equal(1, doc.PageCount);
        Assert.Equal(PaperSize.A4.WidthPoints, doc.Pages[0].Width, 2);
        Assert.Equal(PaperSize.A4.HeightPoints, doc.Pages[0].Height, 2);
        Assert.Equal(1, target.LastPageCount);
        using RasterPage page = RenderPage(pdf, 1);
        Assert.True(PdfPageRasterizerTests.Gray(page, 25, 25) < 64, "square at the sheet's top-left");
        Assert.True(PdfPageRasterizerTests.Gray(page, 75, 25) > 200);
        Assert.True(PdfPageRasterizerTests.Gray(page, 25, 75) > 200);
    }

    [Fact]
    public void AutoRotateTurnsLandscapePagesClockwise()
    {
        var (_, pdf, _) = Print(Job(PdfPageRasterizerTests.Square()).WithAutoRotate());
        using RasterPage page = RenderPage(pdf, 1);
        Assert.True(PdfPageRasterizerTests.Gray(page, 75, 25) < 64, "square now at the top-right of a 100 x 200 box");
        Assert.True(PdfPageRasterizerTests.Gray(page, 25, 25) > 200);
    }

    [Fact]
    public void PageRotateIsHonoured()
    {
        var (_, pdf, _) = Print(Job(PdfPageRasterizerTests.Square(rotate: 90)));
        using RasterPage page = RenderPage(pdf, 1);
        Assert.True(PdfPageRasterizerTests.Gray(page, 75, 25) < 64, "the page as displayed: 100 x 200, square top-right");
        Assert.True(PdfPageRasterizerTests.Gray(page, 25, 25) > 200);
    }

    [Fact]
    public void ReverseOrientationTurnsHalfway()
    {
        var (_, pdf, _) = Print(Job(PdfPageRasterizerTests.Square()).WithOrientation(PageOrientation.ReversePortrait));
        using RasterPage page = RenderPage(pdf, 1);
        Assert.True(PdfPageRasterizerTests.Gray(page, 175, 75) < 64, "square at the bottom-right of the page box");
        Assert.True(PdfPageRasterizerTests.Gray(page, 25, 25) > 200);
    }

    [Fact]
    public void FitToPageScalesAndCentres()
    {
        var (_, pdf, _) = Print(Job(PdfPageRasterizerTests.Square()).WithScale(ScaleMode.FitToPage).WithAlignment(ContentAlignment.Center));
        double scale = PaperSize.A4.WidthPoints / 200;
        double top = (PaperSize.A4.HeightPoints - 100 * scale) / 2;
        using RasterPage page = RenderPage(pdf, 1);
        Assert.True(PdfPageRasterizerTests.Gray(page, (int)(25 * scale), (int)(top + 25 * scale)) < 64);
        Assert.True(PdfPageRasterizerTests.Gray(page, (int)(150 * scale), (int)(top + 75 * scale)) > 200);
        Assert.True(PdfPageRasterizerTests.Gray(page, 20, (int)(top / 2)) > 200, "nothing above the content");
    }

    [Fact]
    public void ClipsToThePrintableArea()
    {
        byte[] full = new MiniPdf().Add(200, 100, 0, null, new Box(0, 0, 200, 100)).ToArray();
        PrintJob job = Job(full).WithMargins(Margins.Uniform(20)).WithCustomScale(400);
        var (_, clipped, _) = Print(job);
        using (RasterPage page = RenderPage(clipped, 1))
        {
            Assert.True(PdfPageRasterizerTests.Gray(page, 300, 100) < 64, "inside the area");
            Assert.True(PdfPageRasterizerTests.Gray(page, (int)PaperSize.A4.WidthPoints - 10, 100) > 200, "right margin stays clear");
            Assert.True(PdfPageRasterizerTests.Gray(page, 100, 10) > 200, "top margin stays clear");
        }
        var (_, unclipped, _) = Print(job, t => t.WithClipping(false));
        using (RasterPage page = RenderPage(unclipped, 1))
            Assert.True(PdfPageRasterizerTests.Gray(page, (int)PaperSize.A4.WidthPoints - 10, 100) < 64, "without clipping the content runs to the edge");
    }

    [Fact]
    public void CopiesCollationAndDuplexBlankBacksFollowThePlan()
    {
        byte[] three = new MiniPdf().Add(200, 100, 0, null, new Box(0, 50, 50, 50))
                                    .Add(200, 100, 0, null, new Box(150, 50, 50, 50))
                                    .Add(200, 100, 0, null, new Box(0, 0, 50, 50)).ToArray();
        PrintJob job = Job(three).WithCopies(2, collate: true).WithDuplex(Duplex.Vertical);
        var (result, pdf, target) = Print(job);
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(target.LastPlan!.Impressions.Count, target.LastPageCount);
        // Copy 1: p1 | p2, p3 | blank back (so copy 2 starts on a fresh sheet); copy 2: p1 | p2, p3.
        // The plan adds no back after the last sheet, so 7 sides.
        Assert.Equal(7, target.LastPageCount);
        using (PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf))) Assert.Equal(7, doc.PageCount);
        using (RasterPage blank = RenderPage(pdf, 4))
            Assert.True(PdfPageRasterizerTests.Gray(blank, 25, 25) > 200 && PdfPageRasterizerTests.Gray(blank, 175, 25) > 200, "side 4 is the blank back");
        using (RasterPage second = RenderPage(pdf, 6))
            Assert.True(PdfPageRasterizerTests.Gray(second, 175, 25) < 64, "copy 2 side 2 is source page 2");
        Assert.Contains(target.LastApproximations, a => a.StartsWith("Duplex"));

        var (_, noBacks, t2) = Print(job, t => t.WithBlankBacks(false));
        Assert.Equal(6, t2.LastPageCount);
        using (PdfDocument doc = PdfDocument.Open(new MemoryStream(noBacks))) Assert.Equal(6, doc.PageCount);
    }

    [Fact]
    public void PageSelectionPicksPages()
    {
        byte[] three = new MiniPdf().Add(200, 100).Add(200, 100).Add(200, 100, 0, null, new Box(0, 50, 50, 50)).ToArray();
        var (_, pdf, _) = Print(Job(three).WithPages(PageSelection.Range("3")));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf));
        Assert.Equal(1, doc.PageCount);
        using RasterPage page = RenderPage(pdf, 1);
        Assert.True(PdfPageRasterizerTests.Gray(page, 25, 25) < 64);
    }

    [Fact]
    public void AnnotationsAreFlattenedOnRequest()
    {
        byte[] annotated = PdfPageRasterizerTests.Square(annotation: new Box(150, 10, 40, 30));
        var (_, with, _) = Print(Job(annotated));
        using (RasterPage page = RenderPage(with, 1)) Assert.True(PdfPageRasterizerTests.Gray(page, 170, 75) < 64);
        var (_, without, _) = Print(Job(annotated), t => t.WithAnnotations(false));
        using (RasterPage page = RenderPage(without, 1)) Assert.True(PdfPageRasterizerTests.Gray(page, 170, 75) > 200);
    }

    [Fact]
    public void WritesFilesAtomicallyAndAuditsOnce()
    {
        string folder = Path.Combine(Path.GetTempPath(), "chuvadi-print-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(folder, "out", "summary.pdf");
            var audit = new MemoryAuditSink();
            PrintResult result = Job(PdfPageRasterizerTests.Square()).WithColor(ColorMode.Monochrome).WithAuditSink(audit)
                .SubmitTo(new PdfFileTarget(path));
            Assert.Equal(PrintJobStatus.Printed, result.Status);
            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
            Assert.Single(audit.Events);
            Assert.Equal("PDF file summary.pdf", audit.Events[0].PrinterName);

            var target = new PdfFileTarget(path);
            Job(PdfPageRasterizerTests.Square()).WithColor(ColorMode.Monochrome).SubmitTo(target);
            Assert.Contains(target.LastApproximations, a => a.StartsWith("Color"));
            Assert.Equal(path, target.LastOutputPath);

            string cancelledPath = Path.Combine(folder, "cancelled.pdf");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            PrintResult cancelled = Job(PdfPageRasterizerTests.Square()).SubmitTo(new PdfFileTarget(cancelledPath), cts.Token);
            Assert.Equal(PrintJobStatus.Cancelled, cancelled.Status);
            Assert.False(File.Exists(cancelledPath));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
