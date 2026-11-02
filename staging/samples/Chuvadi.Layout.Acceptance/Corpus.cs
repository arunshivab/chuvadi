using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.Acceptance
{
    /// <summary>A source document and the reference PDF it is compared with.</summary>
    internal sealed record CorpusEntry(string Source, string Reference, string Printer, string Notes);

    /// <summary>
    /// Finds corpus entries. With a manifest.tsv in the folder, its rows are used: source, reference PDF, printer,
    /// notes (tab-separated; '#' starts a comment; paths relative to the folder). Without one, every .docx, .xlsx,
    /// .txt, .md, .html and .htm file with a .pdf of the same name is an entry, and its printer is "(not stated)". With
    /// <c>layoutOnly</c>, every source file is an entry; those without a reference are laid out but not compared.
    /// </summary>
    internal static class Corpus
    {
        public const string ManifestName = "manifest.tsv";
        public const string NotStated = "(not stated)";
        private static readonly string[] SourceExtensions = { ".docx", ".xlsx", ".txt", ".md", ".html", ".htm" };

        public static List<CorpusEntry> Discover(string folder, bool layoutOnly = false)
        {
            string manifest = Path.Combine(folder, ManifestName);
            if (File.Exists(manifest) && !layoutOnly)
            {
                return ReadManifest(folder, manifest);
            }
            List<CorpusEntry> entries = new List<CorpusEntry>();
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (!SourceExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                {
                    continue;
                }
                string pdf = Path.ChangeExtension(file, ".pdf");
                if (File.Exists(pdf))
                {
                    entries.Add(new CorpusEntry(file, pdf, NotStated, string.Empty));
                }
                else if (layoutOnly)
                {
                    entries.Add(new CorpusEntry(file, string.Empty, NotStated, string.Empty));
                }
            }
            return entries;
        }

        private static List<CorpusEntry> ReadManifest(string folder, string manifest)
        {
            List<CorpusEntry> entries = new List<CorpusEntry>();
            foreach (string raw in File.ReadAllLines(manifest, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }
                string[] f = raw.Split('\t');
                if (f.Length < 2 || string.Equals(f[0].Trim(), "source", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string printer = f.Length > 2 && f[2].Trim().Length > 0 ? f[2].Trim() : NotStated;
                string notes = f.Length > 3 ? f[3].Trim() : string.Empty;
                entries.Add(new CorpusEntry(Path.Combine(folder, f[0].Trim()), Path.Combine(folder, f[1].Trim()), printer, notes));
            }
            return entries;
        }
    }

    internal enum EntryStatus
    {
        Compared,

        /// <summary>Laid out completely, with no reference to compare with (--layout-only).</summary>
        LaidOut,
        Skipped,
        Error,
    }

    /// <summary>Result of one corpus entry.</summary>
    internal sealed record EntryResult(
        CorpusEntry Entry,
        EntryStatus Status,
        string Message,
        FileComparison? Comparison,
        bool Substituted,
        double OpenMs,
        double ResolveMs,
        double AllPagesMs,
        int Warnings,
        int Pages = 0,
        IReadOnlyList<double>? PixelScores = null)
    {
        /// <summary>The worst page's pixel score (0 to 1), or NaN when pixels were not compared.</summary>
        public double WorstPixelScore
        {
            get
            {
                if (PixelScores is null || PixelScores.Count == 0)
                {
                    return double.NaN;
                }
                double m = 0;
                foreach (double s in PixelScores)
                {
                    m = Math.Max(m, s);
                }
                return m;
            }
        }

        public string Name => Path.GetFileName(Entry.Source);
    }

    /// <summary>Settings for a corpus run.</summary>
    internal sealed record RunSettings(double Tolerance, double SubstituteTolerance, bool SystemFonts, IReadOnlyList<string> FontFolders,
        double PixelDpi = 0, double PixelThreshold = 0.10, string? ImageFolder = null)
    {
        public static RunSettings Default { get; } = new RunSettings(2.0, double.NaN, false, Array.Empty<string>());

        /// <summary>Tolerance when a font was substituted: the given value, or 1% of the larger page dimension.</summary>
        public double SubstituteFor(DocumentText reference)
        {
            if (!double.IsNaN(SubstituteTolerance))
            {
                return SubstituteTolerance;
            }
            double page = reference.Pages.Count > 0 ? Math.Max(reference.Pages[0].Width, reference.Pages[0].Height) : 842;
            return Math.Max(Tolerance, page * 0.01);
        }
    }

    internal static class Runner
    {
        public static LayoutOptions Options(RunSettings settings)
        {
            LayoutOptions o = new LayoutOptions { AllowSystemFonts = settings.SystemFonts };
            if (settings.FontFolders.Count > 0)
            {
                o.FontSources.Add(new DirectoryFontSource(settings.FontFolders));
            }
            o.FontSources.Add(BundledFontSource.Instance);
            // Complex scripts (Tamil and the other Indic scripts) are shaped, as apps do with PdfShaper.
            o.Shaper = new Chuvadi.Layout.Pdf.PdfShaper();
            return o;
        }

        public static EntryResult Run(CorpusEntry entry, RunSettings settings)
        {
            if (!File.Exists(entry.Source))
            {
                return new EntryResult(entry, EntryStatus.Error, "source file not found", null, false, 0, 0, 0, 0);
            }
            bool hasReference = entry.Reference.Length > 0;
            if (hasReference && !File.Exists(entry.Reference))
            {
                return new EntryResult(entry, EntryStatus.Error, "reference PDF not found", null, false, 0, 0, 0, 0);
            }
            // The source is read into memory before the clock starts: on Windows, Microsoft Defender scans an encrypted
            // docx/xlsx on open by trying the default password, which can take seconds before any library code runs (D-44).
            byte[] source;
            try
            {
                source = File.ReadAllBytes(entry.Source);
            }
            catch (IOException ex)
            {
                return new EntryResult(entry, EntryStatus.Error, ex.GetType().Name + ": " + ex.Message, null, false, 0, 0, 0, 0);
            }
            Stopwatch sw = Stopwatch.StartNew();
            LaidOutDocument doc;
            try
            {
                using MemoryStream fs = new MemoryStream(source, writable: false);
                LayoutOptions options = Options(settings);
                options.DocumentName = Path.GetFileName(entry.Source);
                doc = Path.GetExtension(entry.Source).ToLowerInvariant() switch
                {
                    ".md" => Chuvadi.Layout.Markup.MarkdownLayout.Open(fs, null, options),
                    ".html" or ".htm" => Chuvadi.Layout.Markup.HtmlLayout.Open(fs, null, options),
                    _ => DocumentLayout.Open(fs, LayoutSourceFormat.Auto, options),
                };
            }
            catch (Exception ex) when (ex is LayoutException or IOException or InvalidDataException)
            {
                return new EntryResult(entry, EntryStatus.Error, ex.GetType().Name + ": " + ex.Message, null, false, 0, 0, 0, 0);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return new EntryResult(entry, EntryStatus.Error, "CRASH while opening: " + ex.GetType().Name + ": " + ex.Message, null, false, 0, 0, 0, 0);
            }
            using (doc)
            {
                double openMs = sw.Elapsed.TotalMilliseconds;
                double resolveMs, allMs;
                DocumentText layout;
                try
                {
                    sw.Restart();
                    doc.ResolvePageCount();
                    resolveMs = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    layout = LayoutText.Read(doc);
                    allMs = sw.Elapsed.TotalMilliseconds;
                }
                catch (LayoutException ex)
                {
                    return new EntryResult(entry, EntryStatus.Error, ex.GetType().Name + ": " + ex.Message, null, false, openMs, 0, 0, doc.Warnings.Count);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return new EntryResult(entry, EntryStatus.Error, "CRASH while laying out: " + ex.GetType().Name + ": " + ex.Message, null, false, openMs, 0, 0, doc.Warnings.Count);
                }
                bool substituted = doc.FontSubstitutions.Any(s => s.Kind != FontMatchKind.Exact);
                if (!hasReference)
                {
                    string note = string.Create(CultureInfo.InvariantCulture, $"{layout.Pages.Count} page(s), {doc.Warnings.Count} warning(s)");
                    return new EntryResult(entry, EntryStatus.LaidOut, note, null, substituted, openMs, resolveMs, allMs, doc.Warnings.Count, layout.Pages.Count);
                }
                DocumentText reference;
                try
                {
                    reference = ReferencePdf.Read(File.ReadAllBytes(entry.Reference));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return new EntryResult(entry, EntryStatus.Error, "reference PDF could not be read: " + ex.Message, null, false, openMs, resolveMs, allMs, doc.Warnings.Count);
                }
                double tolerance = substituted ? settings.SubstituteFor(reference) : settings.Tolerance;
                FileComparison cmp = Comparer.Compare(reference, layout, tolerance);
                string message = Summary(cmp, entry);
                List<double>? scores = null;
                if (settings.PixelDpi > 0)
                {
                    try
                    {
                        scores = PixelScores(entry, doc, settings);
                        double worst = 0;
                        int worstPage = 0;
                        for (int i = 0; i < scores.Count; i++)
                        {
                            if (scores[i] > worst)
                            {
                                worst = scores[i];
                                worstPage = i + 1;
                            }
                        }
                        if (worst > settings.PixelThreshold)
                        {
                            message = (message == "ok" ? string.Empty : message + "; ")
                                + string.Create(CultureInfo.InvariantCulture, $"pixels differ {100 * worst:0.#}% on page {worstPage}");
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        message += "; pixel comparison failed: " + ex.Message;
                    }
                }
                return new EntryResult(entry, EntryStatus.Compared, message, cmp, substituted, openMs, resolveMs, allMs, doc.Warnings.Count, layout.Pages.Count, scores);
            }
        }

        /// <summary>Per-page pixel scores for the pages both documents have; writes the worst page's images when asked.</summary>
        private static List<double> PixelScores(CorpusEntry entry, LaidOutDocument doc, RunSettings settings)
        {
            int pages = doc.ResolvePageCount();
            List<Pixels.GreyImage> reference = Pixels.RenderPdf(File.ReadAllBytes(entry.Reference), settings.PixelDpi, pages);
            List<double> scores = new List<double>();
            for (int i = 0; i < Math.Min(pages, reference.Count); i++)
            {
                Pixels.GreyImage layout = Pixels.RenderLayout(doc.GetPage(i), settings.PixelDpi);
                double score = Pixels.Score(reference[i], layout);
                scores.Add(score);
                if (settings.ImageFolder is string folder && score > settings.PixelThreshold)
                {
                    Directory.CreateDirectory(folder);
                    string stem = Path.GetFileNameWithoutExtension(entry.Source) + "-p" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    Pixels.WritePgm(reference[i], Path.Combine(folder, stem + "-reference.pgm"));
                    Pixels.WritePgm(layout, Path.Combine(folder, stem + "-layout.pgm"));
                }
            }
            return scores;
        }

        private static string Summary(FileComparison c, CorpusEntry e)
        {
            List<string> parts = new List<string>();
            if (!c.PageCountEqual)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{c.LayoutPages} pages, reference {c.ReferencePages}"));
            }
            if (c.FirstTextDifferencePage > 0)
            {
                parts.Add("text differs from page " + c.FirstTextDifferencePage.ToString(CultureInfo.InvariantCulture));
            }
            else if (!c.GeometryPass)
            {
                int over = 0, missing = 0;
                foreach (PageComparison p in c.Pages)
                {
                    over += p.LinesOverTolerance;
                    missing += p.ReferenceLines - p.MatchedLines;
                }
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{over} line(s) beyond {c.Tolerance:0.##} pt, {missing} unmatched"));
            }
            if (e.Printer == Corpus.NotStated && Path.GetExtension(e.Source).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("printer not stated (Excel pagination depends on the printer driver)");
            }
            return parts.Count == 0 ? "ok" : string.Join("; ", parts);
        }
    }
}
