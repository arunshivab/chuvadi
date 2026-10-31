using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>A4 verification groups: IPP encoding, PWG Raster, and both IPP paths end to end against an in-process printer.</summary>
    public static partial class VerificationGroups
    {
        private static Action[] A4Groups() => new Action[]
        {
            IppMessagesRoundTrip,
            PwgRasterRoundTrips,
            IppRasterJobReachesPrinter,
            IppPdfJobPassesThrough
        };

        public static void IppMessagesRoundTrip()
        {
            IppMessage request = IppMessage.CreateRequest(IppOperation.PrintJob, 42, "ipp://printer/ipp/print", "arun");
            request.AddGroup(IppTag.JobAttributes)
                .Add("copies", IppValue.Integer(2))
                .Add("media-col", IppValue.Collection(new IppCollection()
                    .Add("media-size", IppValue.Collection(new IppCollection()
                        .Add("x-dimension", IppValue.Integer(21000)).Add("y-dimension", IppValue.Integer(29700))))
                    .Add("media-source", IppValue.Keyword("tray-1"))))
                .Add("page-ranges", IppValue.Range(1, 2), IppValue.Range(4, 4));
            byte[] bytes = request.ToArray();
            IppMessage back = IppMessage.Parse(bytes);
            Require(SeqEqualBytes(back.ToArray(), bytes), "re-encoding is byte-identical");
            Require(back.Operation == IppOperation.PrintJob && back.RequestId == 42, "header");
            Require(back.Group(IppTag.JobAttributes)!["page-ranges"]!.Values.Count == 2, "1setOf");
        }

        public static void PwgRasterRoundTrips()
        {
            var header = new PwgPageHeader { ColorSpace = PwgColorSpace.SGray8, Width = 64, Height = 32, PageWidthPoints = 46, PageHeightPoints = 23 };
            var pixels = new byte[64 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((i / 64) * 8 + (i % 64 < 32 ? 0 : 7));
            var ms = new MemoryStream();
            var writer = new PwgRasterWriter(ms);
            writer.BeginPage(header);
            for (int y = 0; y < 32; y++) writer.WriteLine(pixels.AsSpan(y * 64, 64));
            writer.EndPage();
            ms.Position = 0;
            var page = new PwgRasterReader(ms).ReadPage();
            Require(page is not null && SeqEqualBytes(page.Value.Pixels, pixels), "pixels survive compression");
        }

        public static void IppRasterJobReachesPrinter()
        {
            using var printer = new FakeIppPrinter();
            using var target = new IppPrinterTarget(printer.Uri).WithMaxRasterDpi(150);
            PrintResult result = new PrintJob(FakeDocument.Uniform(2, PageDimensions.FromPaper(PaperSize.A4)))
                .WithRasterizer(new FakeRasterizer()).WithDuplex(Duplex.Vertical).PrintTo(target);
            Require(result.Status == PrintJobStatus.Printed, "printed: " + result.Error);
            Require(printer.PrintJobs.Count == 1, "one Print-Job");
            var pages = new PwgRasterReader(new MemoryStream(printer.PrintJobs[0].Document)).ReadAll();
            Require(pages.Count == 2 && pages[0].Header.ColorSpace == PwgColorSpace.SRgb8, "two colour pages");
            Require(pages[0].Header.Width == 1240 && pages[0].Header.Height == 1754, "A4 at 150 dpi");
        }

        public static void IppPdfJobPassesThrough()
        {
            using var printer = new FakeIppPrinter();
            using var target = new IppDocumentTarget(printer.Uri);
            byte[] pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n");
            var sink = new MemoryAuditSink();
            PrintResult result = new PrintJob(pdf).WithCopies(3).WithDuplex(Duplex.Horizontal).WithAuditSink(sink).SubmitTo(target);
            Require(result.Status == PrintJobStatus.Printed, "submitted: " + result.Error);
            Require(SeqEqualBytes(printer.PrintJobs[0].Document, pdf), "document bytes unchanged");
            IppAttributeGroup job = printer.PrintJobs[0].Request.Group(IppTag.JobAttributes)!;
            Require(job["copies"]!.Value.AsInteger() == 3, "copies");
            Require(job["sides"]!.Value.AsString() == "two-sided-short-edge", "sides");
            Require(sink.Events.Count == 1, "one audit event");
        }
    }
}
