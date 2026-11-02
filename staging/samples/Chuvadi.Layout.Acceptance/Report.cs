using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Chuvadi.Layout.Acceptance
{
    /// <summary>Writes the corpus report as Markdown (for people) and TSV (for diffs between runs).</summary>
    internal static class Report
    {
        public static string Markdown(IReadOnlyList<EntryResult> results, RunSettings settings, DateTimeOffset when)
        {
            StringBuilder sb = new StringBuilder();
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<EntryResult> compared = results.Where(r => r.Status == EntryStatus.Compared).ToList();
            int textPass = compared.Count(r => r.Comparison!.TextEqual);
            int geoPass = compared.Count(r => r.Comparison!.GeometryPass);
            sb.AppendLine("# Chuvadi.Layout acceptance report");
            sb.AppendLine();
            sb.AppendLine(string.Create(ci, $"Run {when:yyyy-MM-dd HH:mm zzz}. Tolerance {settings.Tolerance:0.##} pt; with substituted fonts {(double.IsNaN(settings.SubstituteTolerance) ? "1% of the page" : settings.SubstituteTolerance.ToString("0.##", ci) + " pt")}. System fonts {(settings.SystemFonts ? "on" : "off")}."));
            sb.AppendLine();
            sb.AppendLine(string.Create(ci, $"- Files: {results.Count}; compared {compared.Count}; laid out without a reference {results.Count(r => r.Status == EntryStatus.LaidOut)} ({results.Where(r => r.Status == EntryStatus.LaidOut).Sum(r => r.Pages)} pages); skipped {results.Count(r => r.Status == EntryStatus.Skipped)}; errors {results.Count(r => r.Status == EntryStatus.Error)}"));
            if (compared.Count > 0)
            {
                sb.AppendLine(string.Create(ci, $"- Tier A (page count, page breaks and per-page text identical): {textPass} of {compared.Count} ({100.0 * textPass / compared.Count:0}%)"));
                sb.AppendLine(string.Create(ci, $"- Geometry (every line within tolerance): {geoPass} of {compared.Count} ({100.0 * geoPass / compared.Count:0}%)"));
            }
            sb.AppendLine();
            if (settings.PixelDpi > 0 && compared.Count > 0)
            {
                int pixelPass = compared.Count(r => !(r.WorstPixelScore > settings.PixelThreshold));
                sb.AppendLine(string.Create(ci, $"- Pixels at {settings.PixelDpi:0} dpi (every page within {100 * settings.PixelThreshold:0.#}% of its content): {pixelPass} of {compared.Count}"));
                sb.AppendLine();
            }
            sb.AppendLine("| File | Printer | Pages (ref / layout) | Tier A | Lines matched | Geometry | Max \u0394 baseline / left (pt) | Pixels (worst page) | Fonts | Layout ms (open / resolve / pages) | Notes |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (EntryResult r in results)
            {
                if (r.Comparison is FileComparison c)
                {
                    string pixels = double.IsNaN(r.WorstPixelScore) ? "" : string.Create(ci, $"{100 * r.WorstPixelScore:0.#}%");
                    sb.AppendLine(string.Create(ci,
                        $"| {Esc(r.Name)} | {Esc(r.Entry.Printer)} | {c.ReferencePages} / {c.LayoutPages} | {(c.TextEqual ? "pass" : "FAIL")} | {100 * c.LineMatchRatio:0}% | {(c.GeometryPass ? "pass" : "FAIL")} | {c.MaxBaselineDelta:0.##} / {c.MaxLeftDelta:0.##} | {pixels} | {(r.Substituted ? "substituted" : "exact")} | {r.OpenMs:0} / {r.ResolveMs:0} / {r.AllPagesMs:0} | {Esc(r.Message)} |"));
                }
                else
                {
                    string fonts = r.Status == EntryStatus.LaidOut ? (r.Substituted ? "substituted" : "exact") : string.Empty;
                    string ms = r.Status == EntryStatus.LaidOut ? string.Create(ci, $"{r.OpenMs:0} / {r.ResolveMs:0} / {r.AllPagesMs:0}") : string.Empty;
                    string pages = r.Status == EntryStatus.LaidOut ? string.Create(ci, $"\u2013 / {r.Pages}") : string.Empty;
                    sb.AppendLine(string.Create(ci, $"| {Esc(r.Name)} | {Esc(r.Entry.Printer)} | {pages} | {r.Status.ToString().ToLowerInvariant()} | | | | | {fonts} | {ms} | {Esc(r.Message)} |"));
                }
            }
            foreach (EntryResult r in results)
            {
                if (r.Comparison is not FileComparison c || c.GeometryPass)
                {
                    continue;
                }
                sb.AppendLine();
                sb.AppendLine("## " + Esc(r.Name));
                sb.AppendLine();
                foreach (PageComparison p in c.Pages)
                {
                    if (p.TextEqual && p.SizeEqual && p.LinesOverTolerance == 0 && p.MatchedLines == p.ReferenceLines)
                    {
                        continue;
                    }
                    sb.Append(string.Create(ci, $"- Page {p.Page + 1}: "));
                    List<string> notes = new List<string>();
                    if (!p.SizeEqual)
                    {
                        notes.Add("page size differs");
                    }
                    if (!p.TextEqual)
                    {
                        notes.Add("text " + (p.TextDifference ?? "differs"));
                    }
                    notes.Add(string.Create(ci, $"lines matched {p.MatchedLines} of {p.ReferenceLines} (layout has {p.LayoutLines}), {p.LinesOverTolerance} over tolerance"));
                    if (p.WorstLine is not null)
                    {
                        notes.Add("largest offset " + p.WorstLine);
                    }
                    sb.AppendLine(Esc(string.Join("; ", notes)));
                }
            }
            return sb.ToString();
        }

        public static string Tsv(IReadOnlyList<EntryResult> results)
        {
            StringBuilder sb = new StringBuilder();
            CultureInfo ci = CultureInfo.InvariantCulture;
            sb.AppendLine("file\tstatus\tprinter\tref_pages\tlayout_pages\ttier_a\tlines_matched\tgeometry\tmax_dy\tmax_dx\tpixels_worst\tpixels_pages\tfonts\topen_ms\tresolve_ms\tpages_ms\twarnings\tmessage");
            foreach (EntryResult r in results)
            {
                FileComparison? c = r.Comparison;
                sb.AppendLine(string.Create(ci,
                    $"{r.Name}\t{r.Status}\t{r.Entry.Printer}\t{c?.ReferencePages}\t{(c is null ? (r.Pages > 0 ? r.Pages.ToString(ci) : "") : c.LayoutPages.ToString(ci))}\t{(c is null ? "" : c.TextEqual ? "pass" : "fail")}\t{(c is null ? "" : (100 * c.LineMatchRatio).ToString("0.#", ci))}\t{(c is null ? "" : c.GeometryPass ? "pass" : "fail")}\t{c?.MaxBaselineDelta:0.###}\t{c?.MaxLeftDelta:0.###}\t{(double.IsNaN(r.WorstPixelScore) ? "" : (100 * r.WorstPixelScore).ToString("0.##", ci))}\t{(r.PixelScores is null ? "" : string.Join(",", r.PixelScores.Select(p => (100 * p).ToString("0.#", ci))))}\t{(r.Substituted ? "substituted" : "exact")}\t{r.OpenMs:0.0}\t{r.ResolveMs:0.0}\t{r.AllPagesMs:0.0}\t{r.Warnings}\t{r.Message.Replace('\t', ' ')}"));
            }
            return sb.ToString();
        }

        public static void Write(string folder, IReadOnlyList<EntryResult> results, RunSettings settings)
        {
            Directory.CreateDirectory(folder);
            UTF8Encoding utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(folder, "acceptance-report.md"), Markdown(results, settings, DateTimeOffset.Now), utf8);
            File.WriteAllText(Path.Combine(folder, "acceptance-report.tsv"), Tsv(results), utf8);
        }

        private static string Esc(string s) => s.Replace("|", "\\|").Replace("\n", " ");
    }
}
