using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Chuvadi.Print;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// Prints a short proof set to a real Windows printer for visual verification: alignment, scale,
    /// orientation (red square = the page's own top-left corner), colour and mono conversion, and a
    /// 3-page duplex job with 2 collated copies. With an output folder, each job is written to a file
    /// instead (for "Microsoft Print to PDF" this gives one PDF per scenario).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class WindowsPrintScenarios
    {
        public static int PrintAll(string printerName, string? outputFolder)
        {
            if (outputFolder is not null) Directory.CreateDirectory(outputFolder);
            PageDimensions a4 = PageDimensions.FromPaper(PaperSize.A4);
            PageDimensions a6 = PageDimensions.FromPaper(PaperSize.A6);

            var jobs = new List<(string Name, PrintJob Job)>
            {
                ("01-align-top-left", new PrintJob(FakeDocument.Create(a6)).WithScale(ScaleMode.ActualSize).WithAlignment(ContentAlignment.TopLeft)),
                ("02-align-center", new PrintJob(FakeDocument.Create(a6)).WithScale(ScaleMode.ActualSize).WithAlignment(ContentAlignment.Center)),
                ("03-align-bottom-right", new PrintJob(FakeDocument.Create(a6)).WithScale(ScaleMode.ActualSize).WithAlignment(ContentAlignment.BottomRight)),
                ("04-fit-letter-on-a4", new PrintJob(FakeDocument.Create(PageDimensions.FromPaper(PaperSize.Letter))).WithScale(ScaleMode.FitToPage)),
                ("05-landscape", new PrintJob(FakeDocument.Create(a4.Swap())).WithOrientation(PageOrientation.Landscape)),
                ("06-reverse-portrait", new PrintJob(FakeDocument.Create(a4)).WithOrientation(PageOrientation.ReversePortrait)),
                ("07-autorotate-landscape-page", new PrintJob(FakeDocument.Create(a4.Swap())).WithAutoRotate()),
                ("08-colour", new PrintJob(FakeDocument.Create(a4)).WithColor(ColorMode.Color)),
                ("09-grayscale", new PrintJob(FakeDocument.Create(a4)).WithColor(ColorMode.Grayscale)),
                ("10-mono-error-diffusion", new PrintJob(FakeDocument.Create(a4)).WithColor(ColorMode.Monochrome).WithMonochrome(MonochromeConversion.ErrorDiffusion)),
                ("11-duplex-3pages-2copies", new PrintJob(FakeDocument.Uniform(3, a4)).WithDuplex(Duplex.Vertical).WithCopies(2, collate: true))
            };

            int printed = 0;
            foreach ((string name, PrintJob job) in jobs)
            {
                var target = new WindowsPrinterTarget(printerName);
                if (outputFolder is not null) target.OutputFileName = Path.GetFullPath(Path.Combine(outputFolder, name + ".pdf"));
                PrintResult result = job.WithRasterizer(new FakeRasterizer()).WithTitle("Chuvadi.Print " + name).PrintTo(target);
                Console.WriteLine((result.Status == PrintJobStatus.Printed ? "SENT  " : "FAIL  ") + name
                                  + "  job " + target.LastSpoolJobId + "  sheet " + target.LastDeviceSheet
                                  + (result.Error is null ? string.Empty : "  -> " + result.Error));
                if (result.Status != PrintJobStatus.Printed)
                    throw new InvalidOperationException("Printing '" + name + "' failed: " + result.Error);
                printed++;
            }
            return printed;
        }
    }
}
