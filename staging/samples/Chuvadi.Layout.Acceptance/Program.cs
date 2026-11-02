using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Chuvadi.Layout.Acceptance
{
    /// <summary>
    /// Usage:
    ///   Chuvadi.Layout.Acceptance &lt;corpus folder&gt; [--out &lt;folder&gt;] [--tolerance &lt;pt&gt;]
    ///       [--substitute-tolerance &lt;pt&gt;] [--system-fonts] [--fonts &lt;folder&gt;]... [--geometry] [--layout-only]
    ///       [--pixels [dpi]] [--pixel-threshold &lt;percent&gt;] [--images &lt;folder&gt;]
    ///   Chuvadi.Layout.Acceptance --dump-lines &lt;source&gt; &lt;reference.pdf&gt; [page]
    ///   Chuvadi.Layout.Acceptance --self-test [--out &lt;folder&gt;]
    /// The corpus run lays out every entry (see <see cref="Corpus"/>), compares it with its reference PDF and writes
    /// acceptance-report.md and acceptance-report.tsv to the output folder (default: &lt;corpus&gt;/_acceptance).
    /// Exit code 0 when every compared file passes tier A (with --geometry: also geometry); skipped files (formats
    /// whose importer has not landed) do not fail the run.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                return Run(args, Console.Out);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
        }

        internal static int Run(string[] args, TextWriter output)
        {
            if (args.Length >= 3 && args[0] == "--dump-lines")
            {
                // Diagnostics: the reference's and the layout's lines of one document, page by page.
                return DumpLines(args[1], args[2], args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1, output);
            }
            string? corpus = null, outFolder = null;
            double tolerance = 2.0, substitute = double.NaN;
            bool systemFonts = false, selfTest = false, geometry = false, layoutOnly = false;
            double pixelDpi = 0, pixelThreshold = 0.10;
            string? imageFolder = null;
            List<string> fontFolders = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--self-test":
                        selfTest = true;
                        break;
                    case "--out":
                        outFolder = Next(args, ref i);
                        break;
                    case "--tolerance":
                        tolerance = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--substitute-tolerance":
                        substitute = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--system-fonts":
                        systemFonts = true;
                        break;
                    case "--fonts":
                        fontFolders.Add(Next(args, ref i));
                        break;
                    case "--geometry":
                        geometry = true;
                        break;
                    case "--layout-only":
                        layoutOnly = true;
                        break;
                    case "--pixels":
                        pixelDpi = i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double dpi) ? double.Parse(Next(args, ref i), CultureInfo.InvariantCulture) : 96;
                        break;
                    case "--pixel-threshold":
                        pixelThreshold = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture) / 100;
                        break;
                    case "--images":
                        imageFolder = Next(args, ref i);
                        break;
                    default:
                        if (args[i].StartsWith("--", StringComparison.Ordinal) || corpus is not null)
                        {
                            throw new ArgumentException("Unknown argument: " + args[i]);
                        }
                        corpus = args[i];
                        break;
                }
            }
            if (selfTest)
            {
                int failures = SelfTest.Run(outFolder ?? Path.Combine(Path.GetTempPath(), "chuvadi-layout-acceptance"), output);
                output.WriteLine(failures == 0 ? "Self-test passed." : $"Self-test: {failures} failure(s).");
                return failures == 0 ? 0 : 1;
            }
            if (corpus is null || !Directory.Exists(corpus))
            {
                throw new ArgumentException("Give a corpus folder (or --self-test). See Program.cs for the options.");
            }
            RunSettings settings = new RunSettings(tolerance, substitute, systemFonts, fontFolders, pixelDpi, pixelThreshold, imageFolder);
            List<CorpusEntry> entries = Corpus.Discover(corpus, layoutOnly);
            List<EntryResult> results = new List<EntryResult>();
            int failed = 0;
            foreach (CorpusEntry e in entries)
            {
                EntryResult r = Runner.Run(e, settings);
                results.Add(r);
                string verdict = r.Status switch
                {
                    EntryStatus.Compared => r.Comparison!.TextEqual ? (r.Comparison.GeometryPass ? "PASS" : "TEXT") : "FAIL",
                    EntryStatus.LaidOut => "OK  ",
                    EntryStatus.Skipped => "SKIP",
                    _ => "ERR ",
                };
                output.WriteLine($"{verdict}  {r.Name}: {r.Message}");
                bool bad = r.Status == EntryStatus.Error
                    || (r.Status == EntryStatus.Compared && (!r.Comparison!.TextEqual || (geometry && !r.Comparison.GeometryPass)));
                if (bad)
                {
                    failed++;
                }
            }
            string folder = outFolder ?? Path.Combine(corpus, "_acceptance");
            Report.Write(folder, results, settings);
            output.WriteLine($"{entries.Count} file(s); {failed} failing. Report: {Path.GetFullPath(Path.Combine(folder, "acceptance-report.md"))}");
            return failed == 0 ? 0 : 1;
        }

        private static int DumpLines(string source, string reference, int page, TextWriter output)
        {
            using FileStream fs = File.OpenRead(source);
            using LaidOutDocument doc = DocumentLayout.Open(fs, LayoutSourceFormat.Auto, Runner.Options(RunSettings.Default));
            DocumentText l = LayoutText.Read(doc);
            DocumentText r = ReferencePdf.Read(File.ReadAllBytes(reference));
            PageText? rp = page - 1 < r.Pages.Count ? r.Pages[page - 1] : null;
            PageText? lp = page - 1 < l.Pages.Count ? l.Pages[page - 1] : null;
            int n = Math.Max(rp?.Lines.Count ?? 0, lp?.Lines.Count ?? 0);
            for (int i = 0; i < n; i++)
            {
                string a = rp is not null && i < rp.Lines.Count ? LayoutText.Describe(rp.Lines[i]) : "";
                string b = lp is not null && i < lp.Lines.Count ? LayoutText.Describe(lp.Lines[i]) : "";
                output.WriteLine($"R {a}\n  L {b}");
            }
            foreach (LayoutWarning w in doc.Warnings)
            {
                output.WriteLine("W " + w);
            }
            return 0;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException(args[i] + " needs a value.");
            }
            return args[++i];
        }
    }
}
