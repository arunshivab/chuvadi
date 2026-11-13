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
        ///   --xps-ticket "&lt;queue&gt;"         write the job PrintTicket the queue gets (with --xps-ticket-file &lt;file.xml&gt;); prints nothing
        ///   --xps-queue "&lt;queue&gt;"          print the XPS sample to a Windows queue through the Print Document Package API
        ///   --xps-queue-file &lt;path&gt;        with --xps-queue: the queue's port file; wait for it and say what it holds
        ///   --xps-route "&lt;queue&gt;"          route the sample and a knockout page through WindowsQueue with a recording raster fallback
        ///   --xps-orientation &lt;name&gt;      with the --xps-ticket/-queue/-route options: Portrait (default), Landscape, ReversePortrait, ReverseLandscape
        ///   --xps-copies &lt;n&gt;              copies (default 1); --xps-software-copies repeats the sheets in the XPS instead
        ///   --xps-duplex &lt;name&gt;           Simplex (default), Vertical (long edge) or Horizontal (short edge)
        ///   --xps-defaults &lt;list&gt;         settings left to the driver's defaults, e.g. Paper or "Paper, Copies" (PrinterDefault names)
        ///   --xps-private                    print as a private job: the spooler's document name is "Chuvadi job" (rc.5)
        ///   --autorotate-pdf ipp://...       send --autorotate-file &lt;file.pdf&gt; as it is with AutoRotate (rc.4, D-289); says which orientation was sent
        ///   --defaults-printer ipp://...     print 2 raster pages with paper, sides and colour left to the printer's defaults (rc.4, L3)
        ///   --list-printers [a,b]            the merged printer list: local and configured CUPS, servers a and b, and DNS-SD (rc.4, P3)
        ///   --list-seconds &lt;n&gt;            with --list-printers: how long to browse DNS-SD (default 3)
        ///   --xps-status-seconds &lt;n&gt;      how long to wait for the job status (default 120; -1 = do not wait)
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
            failed += Scenario(args, "--autorotate-pdf", "Auto-rotate PDF", uri =>
                Rc4Scenarios.AutoRotatePdf(uri, Option(args, "--autorotate-file", null), Array.IndexOf(args, "--ipp-insecure") >= 0));
            failed += Scenario(args, "--defaults-printer", "Printer defaults", uri =>
                Rc4Scenarios.PrinterDefaults(uri, Array.IndexOf(args, "--ipp-insecure") >= 0));
            if (Array.IndexOf(args, "--list-printers") >= 0)
                failed += Scenario(args, "--list-printers", "Printer list", servers => Rc4Scenarios.ListPrinters(servers, Option(args, "--list-seconds", null)), always: true);
            failed += Scenario(args, "--ipp-certificate", "IPP certificate", Phase1Scenarios.Certificate);
            failed += Scenario(args, "--raw-printer", "Raw printer", uri => Phase1Scenarios.RawSocket(uri, Option(args, "--raw-file", null)));
            failed += Scenario(args, "--windows-raw-printer", "Windows RAW", queue =>
                OperatingSystem.IsWindows()
                    ? Phase1Scenarios.WindowsRaw(queue, Option(args, "--windows-raw-file", null))
                    : throw new PlatformNotSupportedException("--windows-raw-printer needs Windows"));
            failed += Scenario(args, "--xps-sample", "XPS sample", file => Phase4Scenarios.WriteSample(file, Option(args, "--xps-font", null)));
            failed += Scenario(args, "--xps-ticket", "XPS PrintTicket", queue =>
                OperatingSystem.IsWindows()
                    ? Phase4QueueScenarios.WriteTicket(queue, Option(args, "--xps-ticket-file", null) ?? "chuvadi-ticket.xml",
                                                       Phase4QueueScenarios.ParseOptions((name, missing) => Option(args, name, missing)))
                    : throw new PlatformNotSupportedException("--xps-ticket needs Windows"));
            failed += Scenario(args, "--xps-queue", "XPS queue", queue =>
                OperatingSystem.IsWindows()
                    ? Phase4QueueScenarios.PrintSample(queue, Option(args, "--xps-queue-file", null),
                                                       Phase4QueueScenarios.ParseOptions((name, missing) => Option(args, name, missing)))
                    : throw new PlatformNotSupportedException("--xps-queue needs Windows"));
            failed += Scenario(args, "--xps-route", "XPS route", queue =>
                OperatingSystem.IsWindows()
                    ? Phase4QueueScenarios.Route(queue, Phase4QueueScenarios.ParseOptions((name, missing) => Option(args, name, missing)))
                    : throw new PlatformNotSupportedException("--xps-route needs Windows"));

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
