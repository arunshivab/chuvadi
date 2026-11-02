using System;
using System.Collections.Generic;

namespace Chuvadi.Layout.ManualTests
{
    public static class Program
    {
        /// <summary>
        /// Runs every verification group and returns 0 when all pass.
        /// Options:
        ///   --preview &lt;folder&gt;   also writes diagnostic SVG pages for visual verification.
        ///   --perf                  times large generated documents (open, first page, all pages, memory).
        ///   --font-report           prints how the Office core fonts resolve, with and without system fonts,
        ///                           and their Excel max-digit width and Word line height (run on Windows to compare
        ///                           real Calibri/Cambria/Arial with the bundled metric-compatible substitutes).
        /// </summary>
        public static int Main(string[] args)
        {
            int passed = 0, failed = 0;
            foreach (Action test in VerificationGroups.All())
            {
                try
                {
                    test();
                    Console.WriteLine("PASS  " + test.Method.Name);
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL  " + test.Method.Name + " -> " + ex.Message);
                    failed++;
                }
            }
            Console.WriteLine();
            Console.WriteLine("Total: " + (passed + failed) + "  Passed: " + passed + "  Failed: " + failed);

            if (HasFlag(args, "--perf"))
            {
                failed += PerfRun.Run(Console.Out);
            }

            if (HasFlag(args, "--font-report"))
            {
                FontReport.Write(Console.Out);
            }

            string? previewFolder = Value(args, "--preview", "layout-preview");
            if (previewFolder is not null)
            {
                try
                {
                    int files = PreviewScenarios.WriteAll(previewFolder);
                    Console.WriteLine("Preview: " + files + " file(s) written under " + System.IO.Path.GetFullPath(previewFolder));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL  Preview -> " + ex.Message);
                    failed++;
                }
            }

            return failed == 0 ? 0 : 1;
        }

        private static bool HasFlag(IReadOnlyList<string> args, string flag)
        {
            foreach (string a in args)
            {
                if (a == flag)
                {
                    return true;
                }
            }
            return false;
        }

        private static string? Value(IReadOnlyList<string> args, string flag, string fallback)
        {
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == flag)
                {
                    return i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : fallback;
                }
            }
            return null;
        }
    }
}
