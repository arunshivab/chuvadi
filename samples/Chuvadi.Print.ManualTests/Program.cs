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
        ///   --ipp-insecure                 with --ipp-printer: accept the printer's self-signed TLS certificate (ipps)
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

            return failed == 0 ? 0 : 1;
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
