using System;

namespace Chuvadi.Markup.ManualTests
{
    public static class Program
    {
        /// <summary>
        /// Runs every verification group. With <c>--preview &lt;folder&gt;</c>, also writes HTML pages
        /// (Markdown, sanitized mail under both policies, plain text) for visual verification in a browser.
        /// </summary>
        public static int Main(string[] args)
        {
            int passed = 0, failed = 0;
            foreach (var test in VerificationGroups.All())
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

            string? previewFolder = PreviewFolder(args);
            if (previewFolder is not null)
            {
                try
                {
                    int files = PreviewScenarios.WriteAll(previewFolder);
                    Console.WriteLine("Preview: " + files + " HTML files written under " + System.IO.Path.GetFullPath(previewFolder));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL  Preview -> " + ex.Message);
                    failed++;
                }
            }

            return failed == 0 ? 0 : 1;
        }

        private static string? PreviewFolder(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--preview")
                    return i + 1 < args.Length ? args[i + 1] : "markup-preview";
            }
            return null;
        }
    }
}
