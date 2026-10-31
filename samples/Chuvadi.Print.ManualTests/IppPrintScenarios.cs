using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// IPP checks. <see cref="WritePreview"/> decodes what an in-process IPP printer received and writes it as
    /// PNG (what a real printer would print). <see cref="PrintToPrinter"/> sends the proof set to a real IPP
    /// Everywhere printer as PWG Raster and, given a PDF, passes that PDF through.
    /// </summary>
    public static class IppPrintScenarios
    {
        private static readonly PageDimensions A4 = PageDimensions.FromPaper(PaperSize.A4);

        private static List<(string Name, PrintJob Job)> ProofSet() => new()
        {
            ("01-align-top-left", new PrintJob(FakeDocument.Create(PageDimensions.FromPaper(PaperSize.A6))).WithScale(ScaleMode.ActualSize).WithAlignment(ContentAlignment.TopLeft)),
            ("02-landscape", new PrintJob(FakeDocument.Create(A4.Swap())).WithOrientation(PageOrientation.Landscape)),
            ("03-reverse-portrait", new PrintJob(FakeDocument.Create(A4)).WithOrientation(PageOrientation.ReversePortrait)),
            ("04-grayscale", new PrintJob(FakeDocument.Create(A4)).WithColor(ColorMode.Grayscale)),
            ("05-mono", new PrintJob(FakeDocument.Create(A4)).WithColor(ColorMode.Monochrome).WithMonochrome(MonochromeConversion.ErrorDiffusion)),
            ("06-duplex-3pages", new PrintJob(FakeDocument.Uniform(3, A4)).WithDuplex(Duplex.Vertical))
        };

        /// <summary>Prints the proof set to an in-process IPP printer and writes every received PWG page as PNG.</summary>
        public static int WritePreview(string folder)
        {
            Directory.CreateDirectory(folder);
            int files = 0;
            foreach (PwgSheetBack back in new[] { PwgSheetBack.Normal, PwgSheetBack.Flipped })
            {
                using var printer = new FakeIppPrinter(g => g.Set("pwg-raster-document-sheet-back",
                    IppValue.Keyword(back == PwgSheetBack.Flipped ? "flipped" : "normal")));
                foreach ((string name, PrintJob job) in ProofSet())
                {
                    if (back == PwgSheetBack.Flipped && !name.Contains("duplex")) continue;
                    using var target = new IppPrinterTarget(printer.Uri).WithMaxRasterDpi(72);
                    PrintResult result = job.WithRasterizer(new FakeRasterizer()).PrintTo(target);
                    if (result.Status != PrintJobStatus.Printed) throw new InvalidOperationException(name + ": " + result.Error);
                    byte[] document = printer.PrintJobs[printer.PrintJobs.Count - 1].Document;
                    var pages = new PwgRasterReader(new MemoryStream(document)).ReadAll();
                    for (int i = 0; i < pages.Count; i++)
                    {
                        string file = Path.Combine(folder, name + (back == PwgSheetBack.Flipped ? "-sheetback-flipped" : string.Empty)
                                                           + "-p" + (i + 1) + ".png");
                        WritePng(file, pages[i].Header, pages[i].Pixels);
                        files++;
                    }
                }
            }
            return files;
        }

        /// <summary>Sends the proof set (PWG Raster) and optionally a PDF (passthrough) to a real IPP printer.</summary>
        public static int PrintToPrinter(string printerUri, string? pdfPath, bool acceptAnyCertificate)
        {
            var options = new IppClientOptions { AcceptAnyServerCertificate = acceptAnyCertificate };
            using (var probe = new IppDocumentTarget(printerUri, options))
            {
                IppPrinterCapabilities caps = probe.QueryCapabilities();
                Console.WriteLine("Printer: " + caps.MakeAndModel + " (" + caps.PrinterName + "), state " + caps.State
                                  + ", formats " + string.Join(" ", caps.DocumentFormats));
                Console.WriteLine("  sides " + string.Join(" ", caps.Sides) + "; colour " + string.Join(" ", caps.ColorModes)
                                  + "; PWG " + string.Join(" ", caps.PwgRasterTypes) + " @ " + string.Join(" ", caps.PwgRasterResolutions)
                                  + "; sheet-back " + caps.PwgRasterSheetBack);
            }

            int sent = 0;
            foreach ((string name, PrintJob job) in ProofSet())
            {
                using var target = new IppPrinterTarget(printerUri, options);
                PrintResult result = job.WithRasterizer(new FakeRasterizer()).WithTitle("Chuvadi.Print IPP " + name).PrintTo(target);
                Console.WriteLine((result.Status == PrintJobStatus.Printed ? "SENT  " : "FAIL  ") + name + "  job " + target.LastJobId
                                  + (target.LastApproximations.Count > 0 ? "  approx: " + string.Join("; ", target.LastApproximations) : string.Empty)
                                  + (result.Error is null ? string.Empty : "  -> " + result.Error));
                if (result.Status != PrintJobStatus.Printed) throw new InvalidOperationException(name + ": " + result.Error);
                sent++;
            }

            if (pdfPath is not null)
            {
                using var target = new IppDocumentTarget(printerUri, options);
                PrintResult result = new PrintJob(pdfPath).SubmitTo(target);
                Console.WriteLine((result.Status == PrintJobStatus.Printed ? "SENT  " : "FAIL  ") + "PDF " + Path.GetFileName(pdfPath)
                                  + "  job " + target.LastJobId + (result.Error is null ? string.Empty : "  -> " + result.Error));
                if (result.Status != PrintJobStatus.Printed) throw new InvalidOperationException("PDF: " + result.Error);
                sent++;
            }
            return sent;
        }

        private static void WritePng(string path, PwgPageHeader header, byte[] pixels)
        {
            bool rgb = header.ColorSpace == PwgColorSpace.SRgb8;
            var encoder = new PngEncoder(header.Width, header.Height, rgb ? PngEncoder.ColorTypeRgb : PngEncoder.ColorTypeGray, header.DpiX, header.DpiY);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            encoder.Write(stream, (y, row) =>
            {
                ReadOnlySpan<byte> src = pixels.AsSpan(y * header.BytesPerLine, header.BytesPerLine);
                if (header.ColorSpace != PwgColorSpace.Black1) { src.CopyTo(row); return; }
                for (int x = 0; x < header.Width; x++) row[x] = (src[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;
            });
        }
    }
}
