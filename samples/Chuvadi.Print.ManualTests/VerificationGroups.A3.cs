using System;
using System.IO;
using Chuvadi.Print;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>Thrown by a verification group that does not apply on this machine; reported as SKIP.</summary>
    public sealed class VerificationSkippedException : Exception
    {
        public VerificationSkippedException(string reason) : base(reason) { }
    }

    /// <summary>A3 verification groups: Windows printer discovery and printing through the GDI spooler.</summary>
    public static partial class VerificationGroups
    {
        public const string MicrosoftPdfPrinter = "Microsoft Print to PDF";

        /// <summary>
        /// True on a GitHub Actions runner. Its Windows image accepts spooler jobs but does not write the
        /// "Microsoft Print to PDF" output file, so the file check is only enforced on real Windows PCs
        /// (WINDOWS-VERIFICATION.md). Everything Chuvadi.Print does (StartDoc, pages, EndDoc, job id, audit)
        /// is still checked there.
        /// </summary>
        public static bool OnHostedCi =>
            string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

        private static Action[] A3Groups() => new Action[]
        {
            WindowsPrintersAreDiscoverable,
            WindowsPrintToPdfWritesFile
        };

        public static void WindowsPrintersAreDiscoverable()
        {
            if (!OperatingSystem.IsWindows()) throw new VerificationSkippedException("Windows only");
            var printers = WindowsPrinters.List();
            foreach (PrinterInfo p in printers) Require(!string.IsNullOrEmpty(p.Name), "printer has a name");
            string? defaultName = WindowsPrinters.DefaultPrinterName;
            if (defaultName is not null)
                Require(WindowsPrinters.Exists(defaultName), "default printer can be opened: " + defaultName);
        }

        public static void WindowsPrintToPdfWritesFile()
        {
            if (!OperatingSystem.IsWindows()) throw new VerificationSkippedException("Windows only");
            if (!WindowsPrinters.Exists(MicrosoftPdfPrinter)) throw new VerificationSkippedException(MicrosoftPdfPrinter + " not installed");

            string path = Path.Combine(Path.GetTempPath(), "chuvadi-print-a3-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                var target = new WindowsPrinterTarget(MicrosoftPdfPrinter).WithOutputFile(path).WithMaxRasterDpi(150);
                PrintResult result = new PrintJob(FakeDocument.Uniform(3, PageDimensions.FromPaper(PaperSize.A4)))
                    .WithRasterizer(new FakeRasterizer())
                    .WithDuplex(Duplex.Vertical)
                    .WithTitle("Chuvadi.Print A3 verification")
                    .PrintTo(target);
                Require(result.Status == PrintJobStatus.Printed, "printed: " + result.Error);
                Require(target.LastSpoolJobId > 0, "spooler job id assigned");
                if (!WaitForFile(path))
                {
                    if (OnHostedCi)
                        throw new VerificationSkippedException("spooler job " + target.LastSpoolJobId +
                            " accepted; this CI runner's spooler wrote no PDF (checked on real PCs instead)");
                    Require(false, "PDF written by the spooler: " + path);
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>Waits for an asynchronously spooled PDF to be complete.</summary>
        public static bool WaitForFile(string path, int timeoutMs = 60_000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        byte[] bytes = File.ReadAllBytes(path);
                        if (bytes.Length > 8 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F'
                            && System.Text.Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 1024), Math.Min(1024, bytes.Length)).Contains("%%EOF"))
                            return true;
                    }
                }
                catch (IOException)
                {
                    // still being written by the spooler
                }
                System.Threading.Thread.Sleep(250);
            }
            return false;
        }
    }
}
