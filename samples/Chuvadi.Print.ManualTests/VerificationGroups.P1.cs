using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>Phase 1 verification groups: the route chooser (D-96, D-132) and the raw 9100 target.</summary>
    public static partial class VerificationGroups
    {
        private static Action[] P1Groups() => new Action[]
        {
            RouterFallsBackToRasterAndAuditsWhy,
            RouterSendsEmbeddedPdfAsIs,
            RawSocketDeliversBytesUnchanged
        };

        public static void RouterFallsBackToRasterAndAuditsWhy()
        {
            using var printer = new FakeIppPrinter();
            using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
            var sink = new MemoryAuditSink();
            PrintRouteResult result = router.Print(new PrintJob(FakeDocument.Uniform(2, PageDimensions.FromPaper(PaperSize.A4)))
                .WithRasterizer(new FakeRasterizer()).WithAuditSink(sink));
            Require(result.Result.Status == PrintJobStatus.Printed, "printed: " + result.Result.Error);
            Require(result.Route == PrintRouteKind.Raster && result.IsFallback, "raster fallback");
            Require(printer.PrintJobs.Count == 1, "one job reached the printer");
            Require(sink.Events.Count == 1, "one audit event");
            Require(sink.Events[0].Route == "Raster (fallback: PdfAsIs: fonts not known to be embedded)", "route audited: " + sink.Events[0].Route);
        }

        public static void RouterSendsEmbeddedPdfAsIs()
        {
            using var printer = new FakeIppPrinter();
            using PrintRouter router = IppRoutes.CreateRouter(printer.Uri);
            var sink = new MemoryAuditSink();
            PrintJob job = new PrintJob(FakeDocument.Uniform(2, PageDimensions.FromPaper(PaperSize.A4)))
                .WithRasterizer(new FakeRasterizer()).WithFontsEmbedded().WithAuditSink(sink);
            PrintRouteResult result = router.Print(job);
            Require(result.Result.Status == PrintJobStatus.Printed, "printed: " + result.Result.Error);
            Require(result.Route == PrintRouteKind.PdfAsIs && !result.IsFallback, "PDF as-is");
            Require(SeqEqualBytes(printer.PrintJobs[0].Document, job.PdfBytes), "the PDF arrived unchanged");
            Require(sink.Events.Count == 1 && sink.Events[0].Route == "PdfAsIs", "route audited");
        }

        public static void RawSocketDeliversBytesUnchanged()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<byte[]> received = Task.Run(() =>
                {
                    using Socket socket = listener.AcceptSocket();
                    var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int read;
                    while ((read = socket.Receive(chunk)) > 0) buffer.Write(chunk, 0, read);
                    return buffer.ToArray();
                });
                var data = new byte[200_000];
                new Random(42).NextBytes(data);
                new RawSocketTarget("127.0.0.1", port).Send(data, "raw", CancellationToken.None);
                Require(received.Wait(TimeSpan.FromSeconds(20)), "the printer side finished reading");
                Require(SeqEqualBytes(received.Result, data), "bytes unchanged");
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
