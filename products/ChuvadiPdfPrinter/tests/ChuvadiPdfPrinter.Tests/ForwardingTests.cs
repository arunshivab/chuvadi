using System.Globalization;
using System.Text;
using Chuvadi.Print;
using Chuvadi.Print.ManualTests;
using Chuvadi.Print.Pdf;

namespace ChuvadiPdfPrinter.Tests;

/// <summary>Small valid PDFs: each page has a black square in its top-left corner.</summary>
internal static class RealPdf
{
    public static byte[] Create(params (double Width, double Height)[] pages)
    {
        var bodies = new SortedDictionary<int, string>();
        var kids = new List<int>();
        int next = 3;
        foreach (var (w, h) in pages)
        {
            int page = next++, content = next++;
            kids.Add(page);
            string c = "0 0 0 rg 0 " + N(h - 72) + " 72 72 re f\n";
            bodies[page] = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(w) + " " + N(h) + "] /Resources << >> /Contents " + content + " 0 R >>";
            bodies[content] = "<< /Length " + c.Length + " >>\nstream\n" + c + "endstream";
        }
        bodies[1] = "<< /Type /Catalog /Pages 2 0 R >>";
        bodies[2] = "<< /Type /Pages /Kids [" + string.Join(" ", kids.Select(k => k + " 0 R")) + "] /Count " + kids.Count + " >>";
        var sb = new StringBuilder("%PDF-1.7\n");
        var offsets = new int[next];
        foreach (var (id, body) in bodies)
        {
            offsets[id] = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append(id).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append("xref\n0 ").Append(next).Append("\n0000000000 65535 f \n");
        for (int i = 1; i < next; i++) sb.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(next).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

public class ForwardingTests
{
    private static readonly (double, double) A4 = (595.28, 841.89);

    private static (PrintResult Result, RecordingTarget Printer, MemoryAuditSink Audit) Forward(byte[] pdf, ForwardTarget target)
    {
        using var temp = new TempFolder();
        string path = Path.Combine(temp.Path, "job.pdf");
        File.WriteAllBytes(path, pdf);
        var printer = new RecordingTarget { Name = target.Printer, Capabilities = new PrintTargetCapabilities { DpiX = 72, DpiY = 72 } };
        var audit = new MemoryAuditSink();
        PrintResult result = new RasterForwarder(new PdfPageRasterizer(), _ => printer).Forward(path, Samples.Job(), target, audit);
        return (result, printer, audit);
    }

    [Fact]
    public void Raster_forwarding_renders_every_page_with_the_targets_settings()
    {
        var target = new ForwardTarget { Printer = "Ward-3 LaserJet", Copies = 2, Duplex = Duplex.Vertical, Color = ColorMode.Monochrome };
        var (result, printer, audit) = Forward(RealPdf.Create(A4, A4), target);
        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(2, result.PagesPrinted);
        Assert.Equal(4, printer.Pages.Count(p => !p.Impression.IsBlank));
        Assert.All(printer.Pages.Where(p => !p.Impression.IsBlank), p => Assert.True(p.BlackPixels > 1000, "the square was rendered"));
        PrintSettings s = printer.Context!.Settings;
        Assert.Equal((2, Duplex.Vertical, ColorMode.Monochrome, ScaleMode.ShrinkToFit), (s.Copies, s.Duplex, s.Color, s.Scale));
        Assert.Equal(PaperSize.A4, s.Paper);
        Assert.Equal(Margins.None, s.Margins);
        Assert.Equal(RasterPixelFormat.BlackWhite1, printer.Pages[0].Format);
        PrintAuditEvent e = Assert.Single(audit.Events);
        Assert.Equal(("Ward-3 LaserJet", PrintJobStatus.Printed, "Discharge summary.docx"), (e.PrinterName, e.Status, e.DocumentTitle));
    }

    [Fact]
    public void Paper_auto_follows_the_first_page_and_names_are_honoured()
    {
        Assert.Equal(PaperSize.Letter, Forward(RealPdf.Create((612, 792)), new ForwardTarget { Printer = "P" }).Printer.Context!.Settings.Paper);
        Assert.Equal(PaperSize.A3, Forward(RealPdf.Create((1190.55, 841.89)), new ForwardTarget { Printer = "P" }).Printer.Context!.Settings.Paper);
        Assert.Equal(PaperSize.A4, Forward(RealPdf.Create((300, 300)), new ForwardTarget { Printer = "P" }).Printer.Context!.Settings.Paper);
        Assert.Equal(PaperSize.A5, Forward(RealPdf.Create(A4), new ForwardTarget { Printer = "P", Paper = "a5" }).Printer.Context!.Settings.Paper);
    }

    [Fact]
    public void Landscape_pages_are_turned_to_fit_the_sheet()
    {
        var (_, printer, _) = Forward(RealPdf.Create((841.89, 595.28)), new ForwardTarget { Printer = "P", Paper = "A4" });
        Assert.Equal(PageRotation.Clockwise90, printer.Pages[0].Placement!.Rotation);
        Assert.Equal(1.0, printer.Pages[0].Placement!.ScaleFactor, 3);
    }

    [Fact]
    public void An_unreadable_pdf_fails_with_one_audit_event()
    {
        var (result, printer, audit) = Forward(Encoding.ASCII.GetBytes("not a pdf"), new ForwardTarget { Printer = "P" });
        Assert.Equal(PrintJobStatus.Failed, result.Status);
        Assert.Empty(printer.Pages);
        Assert.Equal(PrintJobStatus.Failed, Assert.Single(audit.Events).Status);
    }

    [Fact]
    public void The_agent_saves_then_forwards_through_the_raster_path()
    {
        using var temp = new TempFolder();
        PrinterConfig config = Samples.Config(temp.Path);
        config.ForwardTo.Add(new ForwardTarget { Printer = "Ward-3 LaserJet" });
        var printer = new RecordingTarget { Name = "Ward-3 LaserJet" };
        var factory = new RasterFactory(new RasterForwarder(new PdfPageRasterizer(), _ => printer));
        var audit = new MemoryAuditSink();
        CaptureResult result = Samples.Engine(config, audit, factory).CaptureFromStream(new MemoryStream(RealPdf.Create(A4, A4, A4)), Samples.Job());
        Assert.True(result.Succeeded);
        Assert.Equal(PrintJobStatus.Printed, Assert.Single(result.Forwards).Result.Status);
        Assert.Equal(3, printer.Pages.Count);
        Assert.Equal(2, audit.Events.Count); // the saved PDF + the forward
    }

    [Fact]
    public void Default_factory_picks_the_forwarder_for_the_platform()
    {
        IForwarder forwarder = new DefaultForwarderFactory().Create(new ForwardTarget { Kind = ForwardKind.WindowsPrinter, Printer = "Ward-3" });
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsForwarder>(forwarder);
            var off = new DefaultForwarderFactory { RasterizerFactory = () => null }.Create(new ForwardTarget { Printer = "Ward-3" });
            Assert.Contains("switched off", Assert.IsType<UnavailableForwarder>(off).Reason);
        }
        else
        {
            Assert.Contains("only be used on Windows", Assert.IsType<UnavailableForwarder>(forwarder).Reason);
        }
        Assert.IsType<IppForwarder>(new DefaultForwarderFactory().Create(new ForwardTarget { Kind = ForwardKind.Ipp, Printer = "ipp://p/ipp/print" }));
    }

    [Fact]
    public void Config_checks_paper_and_resolution()
    {
        var config = new PrinterConfig();
        config.ForwardTo.Add(new ForwardTarget { Printer = "P", Paper = "A9", MaxRasterDpi = 5000 });
        IReadOnlyList<string> problems = config.Validate();
        Assert.Contains(problems, p => p.Contains("unknown Paper 'A9'"));
        Assert.Contains(problems, p => p.Contains("MaxRasterDpi"));
        config.ForwardTo[0].Paper = "Letter";
        config.ForwardTo[0].MaxRasterDpi = 600;
        Assert.Empty(config.Validate());
    }

    private sealed class RasterFactory : IForwarderFactory
    {
        private readonly IForwarder _forwarder;
        public RasterFactory(IForwarder forwarder) => _forwarder = forwarder;
        public IForwarder Create(ForwardTarget target) => _forwarder;
    }
}
