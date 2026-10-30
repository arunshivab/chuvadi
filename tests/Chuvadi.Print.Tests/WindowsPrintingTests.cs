using System.Text;
using Chuvadi.Print;
using Chuvadi.Print.ManualTests;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.Tests;

/// <summary>
/// Real spooler tests. They run only on Windows and return early elsewhere; the printing tests also
/// return early when "Microsoft Print to PDF" is not installed (it is inbox on Windows 10/11).
/// </summary>
public class WindowsPrintingTests
{
    private const string PdfPrinter = "Microsoft Print to PDF";

    [Fact]
    public void Printers_can_be_listed_and_default_read()
    {
        if (!OperatingSystem.IsWindows()) return;
        IReadOnlyList<PrinterInfo> printers = WindowsPrinters.List();
        foreach (PrinterInfo p in printers) Assert.False(string.IsNullOrEmpty(p.Name));
        string? defaultName = WindowsPrinters.DefaultPrinterName;
        if (defaultName is not null) Assert.Contains(printers, p => p.IsDefault);
    }

    [Fact]
    public void Unknown_printer_is_reported()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.False(WindowsPrinters.Exists("No Such Printer " + Guid.NewGuid()));
        bool threw = false;
        try { WindowsPrinters.Get("No Such Printer " + Guid.NewGuid()); }
        catch (PrinterNotFoundException) { threw = true; }
        Assert.True(threw, "PrinterNotFoundException expected");
    }

    [Fact]
    public void Pdf_printer_reports_capabilities()
    {
        if (!OperatingSystem.IsWindows() || !WindowsPrinters.Exists(PdfPrinter)) return;
        WindowsPrinterCapabilities caps = WindowsPrinters.GetCapabilities(PdfPrinter, new PrintSettings { Paper = PaperSize.A4 });
        Assert.NotEmpty(caps.Papers);
        Assert.Contains(caps.Papers, p => Math.Abs(p.Size.WidthMillimetres - 210) < 1 && Math.Abs(p.Size.HeightMillimetres - 297) < 1);
        Assert.True(caps.DeviceResolution.X > 0);
        Assert.True(caps.Geometry.SheetMatches(PageDimensions.FromPaper(PaperSize.A4)), "device sheet " + caps.DeviceSheet);
    }

    [Fact]
    public void Job_prints_to_pdf_file_through_the_spooler()
    {
        if (!OperatingSystem.IsWindows() || !WindowsPrinters.Exists(PdfPrinter)) return;
        string path = Path.Combine(Path.GetTempPath(), "chuvadi-a3-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var sink = new MemoryAuditSink();
            var target = new WindowsPrinterTarget(PdfPrinter).WithOutputFile(path).WithMaxRasterDpi(150);
            PrintResult result = new PrintJob(FakeDocument.Uniform(2, PageDimensions.FromPaper(PaperSize.A4)))
                .WithRasterizer(new FakeRasterizer())
                .WithColor(ColorMode.Grayscale)
                .WithTitle("Chuvadi A3 test")
                .WithAuditSink(sink)
                .PrintTo(target);

            Assert.True(result.Status == PrintJobStatus.Printed, result.Error);
            Assert.Equal(2, result.ImpressionsPrinted);
            Assert.True(target.LastSpoolJobId > 0);
            Assert.Single(sink.Events);
            Assert.Equal(PdfPrinter, sink.Events[0].PrinterName);
            Assert.True(WaitForPdf(path), "PDF not written: " + path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>The spooler writes the file asynchronously after EndDoc; wait until it is complete.</summary>
    internal static bool WaitForPdf(string path, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes.Length > 8 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-"
                        && Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 1024), Math.Min(1024, bytes.Length)).Contains("%%EOF"))
                        return true;
                }
            }
            catch (IOException)
            {
                // still being written
            }
            Thread.Sleep(250);
        }
        return false;
    }
}
