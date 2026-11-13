using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using Chuvadi.Print;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// Phase 4 part 3 checks on Windows (WINDOWS-VERIFICATION 11g to 11l): the job PrintTicket, the XPS sample printed
    /// through the Print Document Package API, and the WindowsQueue route with its raster fallback.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class Phase4QueueScenarios
    {
        /// <summary>The options a run takes: orientation, copies, duplex, who makes copies, and the status wait.</summary>
        public sealed class QueueOptions
        {
            public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;
            public int Copies { get; set; } = 1;
            public Duplex Duplex { get; set; } = Duplex.Simplex;
            public bool SoftwareCopies { get; set; }
            public TimeSpan? StatusTimeout { get; set; } = WindowsXpsPrinterTarget.DefaultStatusTimeout;

            /// <summary>Settings left to the driver's defaults (--xps-defaults).</summary>
            public PrinterDefault PrinterDefaults { get; set; }

            /// <summary>Print as a private job (--xps-private): the spooler sees "Chuvadi job", not the title.</summary>
            public bool Private { get; set; }

            public PrintSettings ToSettings() => new()
            {
                Paper = PaperSize.A4,
                Orientation = Orientation,
                Copies = Copies,
                Collate = true,
                Duplex = Duplex,
                Scale = ScaleMode.FitToPage,
                PrinterDefaults = PrinterDefaults,
                Private = Private
            };

            public override string ToString()
                => Orientation + ", " + Copies + (Copies == 1 ? " copy" : " copies") + ", " + Duplex
                   + (PrinterDefaults == PrinterDefault.None ? string.Empty : ", printer defaults: " + PrinterDefaults)
                   + (Private ? ", private" : string.Empty);

            /// <summary>Like <see cref="ToString"/>, saying who makes the copies on this queue when there is more than one.</summary>
            public string Describe(WindowsXpsPrinterTarget target)
            {
                if (Copies == 1) return ToString();
                PrintSettings settings = ToSettings();
                bool driver = PrintJob.TargetMakesCopies(settings, target.ReadCapabilities(settings).ToTargetCapabilities(null), target.CopyHandling);
                return Orientation + ", " + Copies + " copies" + (driver ? " (in the PrintTicket)" : " (repeated in the XPS)") + ", " + Duplex
                       + (PrinterDefaults == PrinterDefault.None ? string.Empty : ", printer defaults: " + PrinterDefaults)
                       + (Private ? ", private (job name \"" + PrintSettings.PrivateJobName + "\")" : string.Empty);
            }

            /// <summary>What the queue's driver reports as its defaults (paper, sides, colour, copies).</summary>
            public static string DriverDefaults(WindowsXpsPrinterTarget target)
            {
                WindowsPrinterCapabilities caps = target.ReadCapabilities(new PrintSettings());
                return "driver defaults: paper " + (caps.DefaultPaper?.ToString() ?? "not reported")
                       + ", " + (caps.DefaultDuplex?.ToString() ?? "sides not reported")
                       + ", " + (caps.DefaultColor?.ToString() ?? "colour not reported")
                       + ", " + (caps.DefaultCopies is int c ? c + (c == 1 ? " copy" : " copies") : "copies not reported");
            }
        }

        /// <summary>Writes the job PrintTicket the queue gets for these options; prints nothing.</summary>
        public static string WriteTicket(string queue, string path, QueueOptions options)
        {
            var target = new WindowsXpsPrinterTarget(queue) { CopyHandling = options.SoftwareCopies ? CopyHandling.Software : CopyHandling.Auto };
            byte[] ticket = target.CreatePrintTicket(options.ToSettings());
            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, ticket);
            string text = Encoding.UTF8.GetString(ticket);
            if (!text.Contains("PrintTicket", StringComparison.Ordinal))
                throw new InvalidOperationException("the ticket written to " + path + " has no PrintTicket element");
            return ticket.Length + " bytes written to " + Path.GetFullPath(path) + " for " + options.Describe(target)
                   + "; mentions: " + Mentions(text, "PageMediaSize", "PageOrientation", "JobCopiesAllDocuments", "JobDuplexAllDocumentsContiguously", "PageOutputColor")
                   + "; " + QueueOptions.DriverDefaults(target);
        }

        /// <summary>
        /// Prints the two-page XPS sample through the Print Document Package API. With <paramref name="portFile"/>, waits
        /// for the queue's port file and checks it is an XPS or PDF file (for queues whose port is a file).
        /// </summary>
        public static string PrintSample(string queue, string? portFile, QueueOptions options)
        {
            IReadOnlyList<VectorPage> pages = Phase4Scenarios.SamplePages(Phase4Scenarios.FindSystemFont());
            if (portFile is not null && File.Exists(portFile)) File.Delete(portFile);
            var target = new WindowsXpsPrinterTarget(queue)
            {
                CopyHandling = options.SoftwareCopies ? CopyHandling.Software : CopyHandling.Auto,
                StatusTimeout = options.StatusTimeout
            };
            if (!target.CanPrint(pages, options.ToSettings(), out string? reason)) throw new InvalidOperationException(reason);
            DateTime started = DateTime.UtcNow;
            target.PrintPages(pages, options.ToSettings(), "Chuvadi.Print XPS check", CancellationToken.None);
            string sent = target.LastSheetCount + " sheets, " + options.Describe(target) + ", "
                          + (target.LastStatusEventsConnected ? "status: " + (target.LastJobStatus?.ToString() ?? "none received") : "status events not connected")
                          + ", " + Math.Round((DateTime.UtcNow - started).TotalSeconds, 1) + " s";
            if (portFile is null) return sent;
            return sent + "; " + WaitForPortFile(portFile);
        }

        /// <summary>
        /// Routes two jobs through a router with the WindowsQueue route and a raster route that only records: the sample
        /// (expected WindowsQueue) and a page with a knockout group (expected a raster fallback, nothing printed).
        /// </summary>
        public static string Route(string queue, QueueOptions options)
        {
            IReadOnlyList<VectorPage> sample = Phase4Scenarios.SamplePages(Phase4Scenarios.FindSystemFont());
            var knockout = new VectorPage(595.28, 841.89);
            var group = new VectorGroup { Knockout = true };
            group.Children.Add(new VectorPath(VectorGeometry.FromRectangle(72, 72, 100, 100)) { Fill = new VectorSolidBrush(VectorColor.Black) });
            knockout.Elements.Add(group);

            var lines = new List<string>();
            lines.Add(RouteOne(queue, options, sample, "sample"));
            lines.Add(RouteOne(queue, options, new[] { knockout }, "knockout page"));
            return string.Join(" | ", lines);
        }

        private static string RouteOne(string queue, QueueOptions options, IReadOnlyList<VectorPage> pages, string label)
        {
            var target = new WindowsXpsPrinterTarget(queue, new FixedVectorSource(pages)) { StatusTimeout = options.StatusTimeout };
            var recorder = new RecordingTarget();
            var audit = new MemoryAuditSink();
            using var router = new PrintRouter(new WindowsQueueRoute(target), new RasterRoute(recorder));
            var sizes = new PageDimensions[pages.Count];
            for (int i = 0; i < pages.Count; i++) sizes[i] = new PageDimensions(pages[i].Width, pages[i].Height);
            var job = new PrintJob(FakeDocument.Create(sizes), options.ToSettings())
            {
                DocumentTitle = "Chuvadi.Print route check (" + label + ")",
                Options = new PrintJobOptions { Rasterizer = new FakeRasterizer(), AuditSink = audit }
            };
            PrintRouteResult result = router.Print(job);
            string route = audit.Events.Count == 1 ? audit.Events[0].Route ?? "?" : "?";
            return label + ": " + result.Result.Status + " via " + route
                   + (recorder.Pages.Count > 0 ? " (" + recorder.Pages.Count + " pages recorded, not printed)" : "")
                   + (result.Result.Error is null ? "" : " error: " + result.Result.Error);
        }

        private static string WaitForPortFile(string portFile)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            long lastLength = -1;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (File.Exists(portFile))
                    {
                        long length = new FileInfo(portFile).Length;
                        if (length > 0 && length == lastLength)
                        {
                            byte[] head = new byte[5];
                            using (var s = new FileStream(portFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                s.ReadExactly(head, 0, Math.Min(5, (int)length));
                            string kind = head[0] == (byte)'P' && head[1] == (byte)'K' ? "an XPS (zip) package"
                                : Encoding.ASCII.GetString(head, 0, 5) == "%PDF-" ? "a PDF" : "an unknown format";
                            return "port file " + portFile + " holds " + kind + " of " + length + " bytes";
                        }
                        lastLength = length;
                    }
                }
                catch (IOException)
                {
                    // the spooler is still writing the file
                }
                Thread.Sleep(500);
            }
            throw new TimeoutException("the queue wrote nothing to " + portFile + " within 60 seconds");
        }

        private static string Mentions(string text, params string[] names)
        {
            var found = new List<string>();
            foreach (string n in names) found.Add(n + (text.Contains(n, StringComparison.Ordinal) ? " yes" : " no"));
            return string.Join(", ", found);
        }

        /// <summary>Gives the same pages whatever document it is handed (stands in for the PDF converter of part d).</summary>
        private sealed class FixedVectorSource : IVectorPageSource
        {
            private readonly IReadOnlyList<VectorPage> _pages;

            public FixedVectorSource(IReadOnlyList<VectorPage> pages) => _pages = pages;

            public IVectorDocument Open(ReadOnlyMemory<byte> document) => new Document(_pages);

            private sealed class Document : IVectorDocument
            {
                private readonly IReadOnlyList<VectorPage> _pages;

                public Document(IReadOnlyList<VectorPage> pages) => _pages = pages;

                public int PageCount => _pages.Count;

                public PageDimensions GetPageSize(int pageNumber) => new(_pages[pageNumber - 1].Width, _pages[pageNumber - 1].Height);

                public VectorPage GetPage(int pageNumber, CancellationToken cancellationToken) => _pages[pageNumber - 1];

                public void Dispose() { }
            }
        }

        /// <summary>Reads the shared options from the command line.</summary>
        public static QueueOptions ParseOptions(Func<string, string?, string?> option)
        {
            var o = new QueueOptions();
            string? orientation = option("--xps-orientation", null);
            if (orientation is not null)
                o.Orientation = (PageOrientation)Enum.Parse(typeof(PageOrientation), orientation, ignoreCase: true);
            string? copies = option("--xps-copies", null);
            if (copies is not null) o.Copies = int.Parse(copies, System.Globalization.CultureInfo.InvariantCulture);
            string? duplex = option("--xps-duplex", null);
            if (duplex is not null) o.Duplex = (Duplex)Enum.Parse(typeof(Duplex), duplex, ignoreCase: true);
            o.SoftwareCopies = option("--xps-software-copies", string.Empty) is not null;
            string? defaults = option("--xps-defaults", null);
            if (defaults is not null) o.PrinterDefaults = (PrinterDefault)Enum.Parse(typeof(PrinterDefault), defaults, ignoreCase: true);
            o.Private = option("--xps-private", string.Empty) is not null;
            string? wait = option("--xps-status-seconds", null);
            if (wait is not null)
            {
                int seconds = int.Parse(wait, System.Globalization.CultureInfo.InvariantCulture);
                o.StatusTimeout = seconds < 0 ? null : TimeSpan.FromSeconds(seconds);
            }
            return o;
        }
    }
}
