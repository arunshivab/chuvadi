using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// Checks for 1.0.0-rc.4 on a real printer or CUPS server: AutoRotate on the as-is route (D-288, D-289), printer
    /// defaults (L3) and the merged printer list (P3).
    /// </summary>
    public static class Rc4Scenarios
    {
        /// <summary>
        /// Sends a PDF as it is, with AutoRotate set, to an IPP printer or CUPS queue, and says which orientation the
        /// ticket carried.
        /// </summary>
        public static string AutoRotatePdf(string printerUri, string? pdfPath, bool acceptAnyCertificate)
        {
            if (pdfPath is null) throw new ArgumentException("--autorotate-pdf needs --autorotate-file <file.pdf>.");
            var options = new IppClientOptions { AcceptAnyServerCertificate = acceptAnyCertificate };
            using var target = new IppDocumentTarget(printerUri, options);
            PrintResult result = new PrintJob(pdfPath).WithAutoRotate().WithTitle("Chuvadi.Print rc.4 auto-rotate").SubmitTo(target);
            if (result.Status != PrintJobStatus.Printed) throw new InvalidOperationException(result.Error);
            IppPrinterCapabilities caps = target.LastCapabilities ?? throw new InvalidOperationException("no capabilities were read");
            string sent = caps.Orientations.Contains(7) ? "orientation-requested 7 (none)" : "no orientation-requested";
            return "job " + target.LastJobId + ": " + sent + "; the printer lists orientations "
                   + (caps.Orientations.Count == 0 ? "(none)" : string.Join(" ", caps.Orientations))
                   + (target.LastApproximations.Count > 0 ? "; approx: " + string.Join("; ", target.LastApproximations) : string.Empty);
        }

        /// <summary>
        /// Prints two raster test pages (PWG Raster) to an IPP printer with paper, sides and colour left to the
        /// printer's defaults, and says which defaults it reported and what the audit recorded.
        /// </summary>
        public static string PrinterDefaults(string printerUri, bool acceptAnyCertificate)
        {
            var options = new IppClientOptions { AcceptAnyServerCertificate = acceptAnyCertificate };
            var sink = new MemoryAuditSink();
            using var target = new IppPrinterTarget(printerUri, options);
            PrintResult result = new PrintJob(FakeDocument.Uniform(2, PageDimensions.FromPaper(PaperSize.A4)))
                .WithRasterizer(new FakeRasterizer())
                .WithPrinterDefaults(PrinterDefault.Paper | PrinterDefault.Duplex | PrinterDefault.Color)
                .WithTitle("Chuvadi.Print rc.4 printer defaults").WithAuditSink(sink)
                .PrintTo(target);
            if (result.Status != PrintJobStatus.Printed) throw new InvalidOperationException(result.Error);
            IppPrinterCapabilities caps = target.LastCapabilities ?? throw new InvalidOperationException("no capabilities were read");
            return "job " + target.LastJobId + ", " + result.SheetsPrinted + " sheet(s); printer reports media-default "
                   + (caps.DefaultMedia ?? "-") + ", sides-default " + (caps.DefaultSides ?? "-") + ", print-color-mode-default "
                   + (caps.DefaultColorMode ?? "-") + ", copies-default " + (caps.DefaultCopies?.ToString() ?? "-")
                   + Environment.NewLine + "      audit: " + (sink.Events.Single().PrinterDefaults ?? "-");
        }

        /// <summary>
        /// The merged printer list: the local and configured CUPS servers, <paramref name="servers"/> (comma-separated,
        /// may be empty) and DNS-SD for <paramref name="seconds"/> seconds.
        /// </summary>
        public static string ListPrinters(string servers, string? seconds)
        {
            double s = 3;
            if (seconds is not null && !double.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s))
                throw new ArgumentException("--list-seconds takes a number of seconds: " + seconds);
            var options = new PrinterListingOptions { Discovery = new NetworkPrinterDiscoveryOptions { Duration = TimeSpan.FromSeconds(s) } };
            foreach (string server in servers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                options.CupsServers.Add(server);

            PrinterListingResult listing = PrinterListing.List(options);
            var sb = new StringBuilder();
            sb.Append(listing.Printers.Count).Append(" printer(s); configured CUPS server: ").Append(CupsQueues.ConfiguredServer() ?? "none")
              .Append("; servers asked: ").Append(listing.Servers.Count == 0 ? "none" : string.Join(", ", listing.Servers));
            foreach (ListedPrinter p in listing.Printers)
            {
                sb.Append(Environment.NewLine).Append("      ").Append(p.Name).Append(" | ").Append(p.Uri ?? "-")
                  .Append(" | uuid ").Append(p.Uuid ?? "-").Append(" | ").Append(p.MakeAndModel ?? "-");
                foreach (ListedQueue q in p.Queues)
                    sb.Append(Environment.NewLine).Append("        queue ").Append(q.Queue.Name).Append(" on ").Append(q.Server)
                      .Append(" | type ").Append(q.Queue.PrinterType is int t ? "0x" + t.ToString("X") : "-")
                      .Append(q.Queue.IsRemote ? " (remote)" : string.Empty)
                      .Append(" | device ").Append(q.Queue.DeviceUri ?? "-");
                foreach (DiscoveredPrinter d in p.Discovered)
                    sb.Append(Environment.NewLine).Append("        dns-sd ").Append(d.InstanceName).Append(" | ").Append(d.ServiceType)
                      .Append(" | ").Append(d.Uri ?? "-");
            }
            foreach (PrinterListingProblem problem in listing.Problems)
                sb.Append(Environment.NewLine).Append("      NOT LISTED ").Append(problem);
            return sb.ToString();
        }
    }
}
