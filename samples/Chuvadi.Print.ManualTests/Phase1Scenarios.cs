using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>Phase 1 checks on real printers and queues, run from Program options.</summary>
    public static class Phase1Scenarios
    {
        /// <summary>
        /// Sends a short text job with the RAW datatype to a Windows queue. When <paramref name="portFile"/> is given
        /// (the queue's local port is that file), checks that the file holds exactly the bytes sent.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static string WindowsRaw(string queue, string? portFile)
        {
            byte[] data = Encoding.ASCII.GetBytes("Chuvadi.Print RAW check " + Guid.NewGuid().ToString("N") + "\r\n\f");
            if (portFile is not null && File.Exists(portFile)) File.Delete(portFile);
            var target = new WindowsRawPrinterTarget(queue);
            target.Send(data, "Chuvadi.Print RAW check", CancellationToken.None);
            string sent = "spooler job " + target.LastSpoolJobId + ", " + target.LastBytesSent + " bytes";
            if (portFile is null) return sent;

            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (File.Exists(portFile))
                    {
                        byte[] written = File.ReadAllBytes(portFile);
                        if (written.Length >= data.Length)
                        {
                            if (!written.AsSpan().SequenceEqual(data))
                                throw new InvalidOperationException("the port file holds " + written.Length + " bytes that differ from the " + data.Length + " sent");
                            return sent + "; port file matches byte for byte";
                        }
                    }
                }
                catch (IOException)
                {
                    // the spooler is still writing the file
                }
                Thread.Sleep(250);
            }
            throw new TimeoutException("the queue wrote nothing to " + portFile + " within 30 seconds (" + sent + ")");
        }

        /// <summary>Sends a file, or a one-line text page, to a printer's raw port.</summary>
        public static string RawSocket(string uri, string? file)
        {
            byte[] data = file is null
                ? Encoding.ASCII.GetBytes("Chuvadi.Print raw port 9100 check\r\n\f")
                : File.ReadAllBytes(file);
            var target = RawSocketTarget.FromUri(uri);
            target.Send(data, "Chuvadi.Print raw check", CancellationToken.None);
            return target.LastBytesSent + " bytes sent to " + target.Name;
        }

        /// <summary>Shows which route the router would choose for this printer, for unknown and for embedded fonts. Prints nothing.</summary>
        public static string RouteDecision(string printerUri, bool acceptAnyCertificate)
        {
            var options = new IppClientOptions { AcceptAnyServerCertificate = acceptAnyCertificate };
            using PrintRouter router = IppRoutes.CreateRouter(printerUri, options);
            var page = PageDimensions.FromPaper(PaperSize.A4);
            PrintRouteDecision unknown = router.Choose(new PrintJob(FakeDocument.Uniform(1, page)).WithRasterizer(new FakeRasterizer()));
            PrintRouteDecision embedded = router.Choose(new PrintJob(FakeDocument.Uniform(1, page)).WithRasterizer(new FakeRasterizer()).WithFontsEmbedded());
            return "fonts unknown -> " + unknown.AuditText + Environment.NewLine + "      fonts embedded -> " + embedded.AuditText;
        }

        /// <summary>Reads an ipps:// printer's certificate and returns its subject, dates and SHA-256 fingerprint for pinning.</summary>
        public static string Certificate(string printerUri)
        {
            using X509Certificate2 cert = IppCertificates.GetServerCertificate(printerUri);
            return "subject " + cert.Subject + ", valid " + cert.NotBefore.ToString("yyyy-MM-dd") + " to " + cert.NotAfter.ToString("yyyy-MM-dd")
                   + Environment.NewLine + "      SHA-256 " + IppCertificates.Fingerprint(cert);
        }
    }
}
