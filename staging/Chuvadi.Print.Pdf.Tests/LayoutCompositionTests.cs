using Chuvadi.Pdf.Authoring;
using Chuvadi.Print.ManualTests;

namespace Chuvadi.Print.Pdf.Tests;

/// <summary>
/// A stand-in for Chuvadi.Layout (owned by the Layout chat): turns a small report model into PDF bytes using the
/// Chuvadi PDF authoring API, as Layout's ToPdf does for docx/xlsx. Only its output matters here: a PDF.
/// </summary>
internal static class StubLayout
{
    public sealed record Report(string Title, IReadOnlyList<(string Item, string Value)> Rows);

    public static byte[] ToPdf(Report report, int pages = 2)
    {
        PdfDocumentBuilder doc = PdfDocumentBuilder.Create().SetTitle(report.Title);
        for (int p = 1; p <= pages; p++)
        {
            PageBuilder page = doc.AddPage(PageSize.A4);
            page.DrawText(report.Title + " - page " + p, 56, 56, StandardFonts.HelveticaBold, 18, Colors.Black);
            double y = 100;
            foreach ((string item, string value) in report.Rows)
            {
                page.DrawText(item, 56, y, StandardFonts.Helvetica, 11, Colors.Black);
                page.DrawText(value, 400, y, StandardFonts.Helvetica, 11, Colors.Black);
                y += 18;
            }
            page.DrawRectangle(56, 40, 483, 4, fill: Colors.Black);
        }
        return doc.ToByteArray();
    }
}

/// <summary>
/// The composition a host application uses to print docx/xlsx reports: Chuvadi.Layout renders to PDF, Chuvadi.Print prints the
/// PDF (to a device through PdfPageRasterizer). No extra adapter.
/// </summary>
public class LayoutCompositionTests
{
    private static readonly StubLayout.Report Discharge = new("Discharge summary", new[]
    {
        ("Patient", "MRN 004211"), ("Ward", "Ward 3"), ("Consultant", "Dr. A. Example"), ("Follow-up", "14 days")
    });

    [Fact]
    public void LayoutPdfPrintsToADevice()
    {
        byte[] pdf = StubLayout.ToPdf(Discharge);
        var printer = new RecordingTarget();
        var audit = new MemoryAuditSink();
        PrintResult result = new PrintJob(pdf)
            .WithTitle("Discharge summary").WithUser("user1").WithTenant("tenant-a")
            .WithRasterizer(new PdfPageRasterizer())
            .WithCopies(2, collate: true).WithDuplex(Duplex.Vertical).WithColor(ColorMode.Monochrome)
            .WithAuditSink(audit)
            .PrintTo(printer);
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(4, printer.Pages.Count);
        Assert.All(printer.Pages, p => Assert.True(p.BlackPixels > 500, "text and rule were rendered"));
        Assert.Single(audit.Events);
    }

    [Fact]
    public void LayoutPdfTravelsThroughTheSpoolEnvelope()
    {
        // Web host: lay out, wrap with settings, send. Agent: load, render, print.
        byte[] spool = new PrintJob(StubLayout.ToPdf(Discharge)).WithCopies(3).WithTitle("Discharge summary").ToSpoolArray();
        var printer = new RecordingTarget();
        PrintResult result = PrintJob.LoadSpool(spool).WithRasterizer(new PdfPageRasterizer()).PrintTo(printer);
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(6, printer.Pages.Count);
    }

    [Fact]
    public void WritesLayoutPreviewForVisualCheck()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "layout-print-preview");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        // 300 dpi: the resolution a typical office printer renders at, so the preview shows real print sharpness.
        PrintResult result = new PrintJob(StubLayout.ToPdf(Discharge, pages: 1)).WithRasterizer(new PdfPageRasterizer())
            .PrintTo(new ImageFileTarget(folder).WithDpi(300).WithGuides());
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Single(Directory.GetFiles(folder, "*.png"));
    }
}
