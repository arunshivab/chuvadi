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
    /// <summary>
    /// Phase 1 verification groups: the route chooser (D-96, D-132), the raw 9100 target, CUPS queue listing, the
    /// saved-printer registry and the PWG back-side table.
    /// </summary>
    public static partial class VerificationGroups
    {
        private static Action[] P1Groups() => new Action[]
        {
            RouterFallsBackToRasterAndAuditsWhy,
            RouterSendsEmbeddedPdfAsIs,
            RawSocketDeliversBytesUnchanged,
            CupsQueuesAreListed,
            PrinterRegistryKeepsCapabilities,
            BackSideTransformFollowsPwgTable9
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

        public static void CupsQueuesAreListed()
        {
            using var server = new FakeIppPrinter();
            var queue = new IppAttributeGroup(IppTag.PrinterAttributes);
            queue.Add("printer-name", IppValue.Name("ward3"));
            queue.Add("printer-uri-supported", IppValue.Uri("ipp://localhost:631/printers/ward3"));
            queue.Add("printer-is-accepting-jobs", IppValue.Boolean(true));
            server.CupsQueues.Add(queue);
            var queues = CupsQueues.List(server.Uri);
            Require(queues.Count == 1 && queues[0].Name == "ward3", "one queue named ward3");
            Require(queues[0].Uri == "ipp://localhost:631/printers/ward3" && queues[0].IsAcceptingJobs, "queue URI and state");
        }

        public static void PrinterRegistryKeepsCapabilities()
        {
            using var printer = new FakeIppPrinter();
            var cache = new IppCapabilityCache();
            using (var client = new IppClient(printer.Uri)) cache.Get(client);
            var saved = new SavedPrinter { Name = "Ward 3", Connection = PrinterConnection.Ipp, Address = printer.Uri };
            Require(cache.StoreIn(saved), "capabilities stored");
            var registry = new PrinterRegistry();
            registry.Set(saved);
            var stream = new MemoryStream();
            registry.WriteTo(stream);
            stream.Position = 0;
            Require(PrinterRegistry.Read(stream).TryGet("ward 3", out SavedPrinter? back), "read back");
            var reloaded = new IppCapabilityCache();
            Require(reloaded.LoadFrom(back!), "capabilities loaded");
            Require(reloaded.TryGetCached(printer.Uri, out IppPrinterCapabilities? caps, out _) && caps!.PrinterName == "Chuvadi Fake IPP", "same printer");
        }

        public static void BackSideTransformFollowsPwgTable9()
        {
            // PWG 5102.4-2012 section 4.3.2.8, Table 9.
            Require(IppPrinterTarget.BackTransform(Duplex.Simplex, PwgSheetBack.Rotated) == (1, 1), "one-sided");
            Require(IppPrinterTarget.BackTransform(Duplex.Vertical, PwgSheetBack.Flipped) == (1, -1), "long edge, flipped");
            Require(IppPrinterTarget.BackTransform(Duplex.Vertical, PwgSheetBack.Rotated) == (-1, -1), "long edge, rotated");
            Require(IppPrinterTarget.BackTransform(Duplex.Horizontal, PwgSheetBack.Flipped) == (-1, 1), "short edge, flipped");
            Require(IppPrinterTarget.BackTransform(Duplex.Horizontal, PwgSheetBack.ManualTumble) == (-1, -1), "short edge, manual tumble");
            Require(IppPrinterTarget.BackTransform(Duplex.Horizontal, PwgSheetBack.Rotated) == (1, 1), "short edge, rotated");
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
