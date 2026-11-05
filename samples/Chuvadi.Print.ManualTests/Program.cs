using System;

namespace Chuvadi.Print.ManualTests
{
    public static class Program
    {
        /// <summary>
        /// Runs every verification group (groups that do not apply on this machine report SKIP).
        /// Options:
        ///   --preview &lt;folder&gt;            also write PNG sheet images for visual verification
        ///   --windows-printer "&lt;name&gt;"     also print the proof set to that Windows printer
        ///   --windows-output &lt;folder&gt;     with --windows-printer: write each job to a file in this folder
        ///   --ipp-printer &lt;uri&gt;          also send the proof set to an IPP Everywhere printer (PWG Raster)
        ///   --ipp-pdf &lt;file&gt;             with --ipp-printer: also pass this PDF through unchanged
        ///   --ipp-insecure                 with --ipp-printer or --route-printer: accept the printer's self-signed TLS certificate (ipps)
        ///   --route-printer &lt;uri&gt;        show which route an IPP printer gets for unknown and for embedded fonts (prints nothing)
        ///   --ipp-certificate &lt;uri&gt;      show an ipps:// printer's certificate and SHA-256 fingerprint, for pinning
        ///   --discover [seconds]           list the printers that answer on the local network (DNS-SD, default 3 seconds)
        ///   --cups-server &lt;uri&gt;          list the queues of a CUPS server (for example ipp://localhost:631/)
        ///   --raw-printer socket://host[:port]  send a one-line text page (or --raw-file) to a printer's raw port
        ///   --raw-file &lt;file&gt;            with --raw-printer: send this file unchanged
        ///   --windows-raw-printer "&lt;queue&gt;"  send a short text job with the RAW datatype to a Windows queue
        ///   --windows-raw-file &lt;path&gt;     with --windows-raw-printer: the queue's port file; check it holds exactly the bytes sent
        ///   --xps-sample &lt;file.xps&gt;      write the two-page XPS sample for viewing in an XPS viewer
        ///   --xps-font &lt;file.ttf&gt;        with --xps-sample: the TrueType font for its text (default Arial on Windows)
        /// </summary>
        public static int Main(string[] args)
        {
            int passed = 0, failed = 0, skipped = 0;
            foreach (var test in VerificationGroups.All())
            {
                try
                {
                    test();
                    Console.WriteLine("PASS  " + test.Method.Name);
                    passed++;
                }
                catch (VerificationSkippedException skip)
                {
                    Console.WriteLine("SKIP  " + test.Method.Name + " (" + skip.Message + ")");
                    skipped++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL  " + test.Method.Name + " -> " + ex.Message);
                    failed++;
                }
            }
            Console.WriteLine();
            Console.WriteLine("Total: " + (passed + failed + skipped) + "  Passed: " + passed + "  Failed: " + failed + "  Skipped: " + skipped);

            string? previewFolder = Option(args, "--preview", "print-preview");
            if (previewFolder is not null)
            {
                try
                {
                    int files = PreviewScenarios.WriteAll(previewFolder);
                    files += IppPrintScenarios.WritePreview(System.IO.Path.Combine(previewFolder, "07-ipp-raster"));
                    Console.WriteLine("Preview: " + files + " PNG files written under " + System.IO.Path.GetFullPath(previewFolder));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL  Preview -> " + ex.Message);
                    failed++;
                }
            }

            string? printer = Option(args, "--windows-printer", null);
            if (printer is not null)
            {
                if (!OperatingSystem.IsWindows())
                {
                    Console.WriteLine("FAIL  --windows-printer needs Windows");
                    failed++;
                }
                else
                {
                    try
                    {
                        int jobs = WindowsPrintScenarios.PrintAll(printer, Option(args, "--windows-output", null));
                        Console.WriteLine("Windows printer: " + jobs + " jobs sent to '" + printer + "'");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("FAIL  Windows printer -> " + ex.Message);
                        failed++;
                    }
                }
            }

            string? ippPrinter = Option(args, "--ipp-printer", null);
            if (ippPrinter is not null)
            {
                try
                {
                    int jobs = IppPrintScenarios.PrintToPrinter(ippPrinter, Option(args, "--ipp-pdf", null), Array.IndexOf(args, "--ipp-insecure") >= 0);
                    Console.WriteLine("IPP printer: " + jobs + " jobs sent to " + ippPrinter);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL  IPP printer -> " + ex.Message);
                    failed++;
                }
            }

            failed += Scenario(args, "--route-printer", "Route", uri => Phase1Scenarios.RouteDecision(uri, Array.IndexOf(args, "--ipp-insecure") >= 0));
            if (Array.IndexOf(args, "--discover") >= 0)
                failed += Scenario(args, "--discover", "Discovery", _ => Phase1Scenarios.Discover(Option(args, "--discover", null)), always: true);
            failed += Scenario(args, "--cups-server", "CUPS", Phase1Scenarios.Cups);
            failed += Scenario(args, "--ipp-certificate", "IPP certificate", Phase1Scenarios.Certificate);
            failed += Scenario(args, "--raw-printer", "Raw printer", uri => Phase1Scenarios.RawSocket(uri, Option(args, "--raw-file", null)));
            failed += Scenario(args, "--windows-raw-printer", "Windows RAW", queue =>
                OperatingSystem.IsWindows()
                    ? Phase1Scenarios.WindowsRaw(queue, Option(args, "--windows-raw-file", null))
                    : throw new PlatformNotSupportedException("--windows-raw-printer needs Windows"));
            failed += Scenario(args, "--xps-sample", "XPS sample", file => Phase4Scenarios.WriteSample(file, Option(args, "--xps-font", null)));

            return failed == 0 ? 0 : 1;
        }

        /// <summary>Runs one option-driven check; returns 1 when it failed.</summary>
        private static int Scenario(string[] args, string option, string label, Func<string, string> run, bool always = false)
        {
            string? value = Option(args, option, always ? string.Empty : null);
            if (value is null) return 0;
            try
            {
                Console.WriteLine("PASS  " + label + ": " + run(value));
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + label + " -> " + ex.Message);
                return 1;
            }
        }

        private static string? Option(string[] args, string name, string? valueWhenMissing)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name)
                    return i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : valueWhenMissing;
            }
            return null;
        }
    }
}
