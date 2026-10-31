using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using Chuvadi.Print;
using Chuvadi.Print.Windows;

namespace ChuvadiPdfPrinter.Agent
{
    /// <summary>
    /// End-to-end check on a real machine: prints a test page through the virtual printer, waits for the agent
    /// to save it, and checks the PDF and the audit trail. Prints PASS/FAIL per step; returns 0 when all pass.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class SelfTest
    {
        public static int Run(string configPath, string? printerOverride, int timeoutSeconds)
        {
            PrinterConfig config = PrinterConfig.Load(configPath);
            string printer = printerOverride ?? config.QueueName;
            string token = Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
            string title = "Chuvadi PDF self-test " + token;
            int failures = 0;

            void Step(bool ok, string what, string? detail = null)
            {
                Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what + (detail is null ? string.Empty : "  (" + detail + ")"));
                if (!ok) failures++;
            }

            Console.WriteLine("Chuvadi PDF self-test " + token + " on '" + printer + "'");
            Console.WriteLine("Config: " + configPath + (File.Exists(configPath) ? string.Empty : " (not found - defaults)"));

            bool queue = WindowsPrinters.Exists(printer);
            Step(queue, "printer '" + printer + "' is installed");
            bool agent = File.Exists(@"\\.\pipe\" + config.PipeName);
            Step(agent, "agent is running (pipe \\\\.\\pipe\\" + config.PipeName + ")",
                 agent ? null : "start it: Start-Service " + AgentHost.ServiceName);
            if (!queue || !agent) return Summary(failures);

            string expectedFolder = config.ResolveOutputFolder(Environment.UserDomainName + "\\" + Environment.UserName);
            DateTime started = DateTime.Now.AddSeconds(-2);

            var text = new StringBuilder()
                .AppendLine("CHUVADI PDF SELF-TEST")
                .AppendLine("TOKEN " + token)
                .AppendLine("PRINTER " + printer)
                .AppendLine("USER " + Environment.UserName)
                .AppendLine("MACHINE " + Environment.MachineName)
                .AppendLine("TIME " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            var target = new WindowsPrinterTarget(printer).WithMaxRasterDpi(150);
            PrintResult printed = new PrintJob(Encoding.UTF8.GetBytes(text.ToString()))
                .WithRasterizer(new TestPageRasterizer())
                .WithTitle(title)
                .WithColor(ColorMode.Grayscale)
                .PrintTo(target);
            Step(printed.Status == PrintJobStatus.Printed, "test page sent to the spooler",
                 printed.Status == PrintJobStatus.Printed ? "spooler job " + target.LastSpoolJobId : printed.Error);
            if (printed.Status != PrintJobStatus.Printed) return Summary(failures);

            Console.WriteLine("      waiting up to " + timeoutSeconds + " s for the PDF in " + expectedFolder + " ...");
            bool titleInName = config.FileNamePattern.Contains("{title}", StringComparison.OrdinalIgnoreCase);
            string? pdf = WaitForOutput(expectedFolder, token, started, titleInName, TimeSpan.FromSeconds(timeoutSeconds));
            Step(pdf is not null, "PDF saved by the agent", pdf ?? "nothing arrived; see " + config.ResolveDiagnosticLog());
            if (pdf is null) return Summary(failures);

            Step(PdfQuickInfo.LooksLikePdf(pdf), "file is a complete PDF", new FileInfo(pdf).Length + " bytes");
            int pages = PdfQuickInfo.CountPages(pdf);
            Step(pages is 0 or 1, "PDF has the one test page", pages == 0 ? "page count not visible (compressed); open it to check" : pages + " page(s)");

            string audit = config.ResolveAuditLog();
            bool audited = WaitFor(() => File.Exists(audit) && ReadShared(audit).Contains(token, StringComparison.Ordinal), TimeSpan.FromSeconds(10));
            Step(audited, "job recorded in the audit trail", audit);

            Console.WriteLine("Open the PDF to check it visually: " + pdf);
            return Summary(failures);
        }

        /// <summary>
        /// Finds the saved PDF: by the token in its name (the default pattern contains {title}); if the pattern has
        /// no {title}, the newest PDF created since the test started.
        /// </summary>
        private static string? WaitForOutput(string folder, string token, DateTime notBefore, bool titleInName, TimeSpan timeout)
        {
            string? found = null;
            WaitFor(() =>
            {
                if (!Directory.Exists(folder)) return false;
                var pdfs = Directory.EnumerateFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly);
                found = titleInName
                    ? pdfs.FirstOrDefault(f => Path.GetFileName(f).Contains(token, StringComparison.OrdinalIgnoreCase))
                    : pdfs.Where(f => File.GetCreationTime(f) >= notBefore).OrderByDescending(File.GetCreationTime).FirstOrDefault();
                return found is not null;
            }, timeout);
            return found;
        }

        private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < timeout)
            {
                try
                {
                    if (condition()) return true;
                }
                catch (IOException)
                {
                    // file still being written
                }
                catch (UnauthorizedAccessException)
                {
                }
                Thread.Sleep(500);
            }
            return false;
        }

        private static string ReadShared(string path) => Encoding.UTF8.GetString(ReadSharedBytes(path));

        private static byte[] ReadSharedBytes(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static int Summary(int failures)
        {
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "SELF-TEST PASSED" : "SELF-TEST FAILED (" + failures + " step(s))");
            return failures == 0 ? 0 : 1;
        }
    }
}
