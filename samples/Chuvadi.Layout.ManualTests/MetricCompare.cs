using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.ManualTests
{
    /// <summary>
    /// --compare-metrics [folder]: compares installed fonts (default C:\Windows\Fonts; read only) with the substitutes
    /// Layout uses for them, per codepoint: advance widths (in 1/1000 em) and vertical metrics. Run on Windows to decide
    /// whether a mapping can be marked metric-compatible (D-116 A5: Georgia → Gelasio, Segoe UI → Selawik). The Office
    /// core fonts are listed too, as a check of the method: their substitutes are known to be metric-compatible.
    /// </summary>
    internal static class MetricCompare
    {
        private static readonly (string Installed, string Substitute)[] Pairs =
        {
            ("georgia.ttf", "Gelasio-Regular.ttf"), ("georgiab.ttf", "Gelasio-Bold.ttf"),
            ("georgiai.ttf", "Gelasio-Italic.ttf"), ("georgiaz.ttf", "Gelasio-BoldItalic.ttf"),
            ("segoeui.ttf", "selawk.ttf"), ("segoeuib.ttf", "selawkb.ttf"), ("segoeuil.ttf", "selawkl.ttf"),
            ("segoeuisl.ttf", "selawksl.ttf"), ("seguisb.ttf", "selawksb.ttf"),
            ("calibri.ttf", "Carlito-Regular.ttf"), ("cambria.ttc", "Caladea-Regular.ttf"),
            ("arial.ttf", "LiberationSans-Regular.ttf"), ("times.ttf", "LiberationSerif-Regular.ttf"),
            ("cour.ttf", "LiberationMono-Regular.ttf"),
            ("NotoSansTamil-Regular.ttf", "LiPi-Sans-Tamil-Regular.ttf"), ("NotoSansDevanagari-Regular.ttf", "LiPi-Sans-Devanagari-Regular.ttf"),
        };

        public static int Write(TextWriter log, string folder)
        {
            log.WriteLine();
            log.WriteLine("Metric comparison: installed font (" + folder + ") vs the substitute Layout uses (advance widths per 1/1000 em)");
            log.WriteLine("The verdict counts the characters documents use (Basic Latin, Latin-1, Latin Extended-A, General Punctuation,");
            log.WriteLine("currency symbols); differences elsewhere in the BMP are listed but do not decide it (T2). Only characters both fonts");
            log.WriteLine("have are compared, and invisible formatting characters (Unicode category Cf, such as U+200C or U+202A) are skipped (D-255).");
            log.WriteLine("With the same vertical metrics and at most " + MaxExceptions + " document characters wider or narrower by more than 1/1000 em, the");
            log.WriteLine("verdict is \"metric-compatible except\" those characters. Every differing document character is listed after the table (D-287).");
            log.WriteLine("installed | substitute | codepoints compared | width differences | largest difference | vertical metrics | other BMP differences | verdict");
            int compared = 0;
            List<(string Pair, List<(int Cp, double A, double B)> Diffs)> details = new();
            foreach ((string installed, string substitute) in Pairs)
            {
                string path = Path.Combine(folder, installed);
                if (!File.Exists(path))
                {
                    log.WriteLine($"{installed} | {substitute} | not installed | | | |");
                    continue;
                }
                FontFace a = FontFace.Load(File.ReadAllBytes(path), 0, FontOrigin.System, installed);
                FontFace b = FontFace.Load(SubstituteBytes(substitute), 0, FontOrigin.Bundled, substitute);
                (int n, List<(int Cp, double A, double B)> differing, double max, int worstCp) = Widths(a, b, DocumentCharacters);
                (_, List<(int Cp, double A, double B)> other, _, _) = Widths(a, b, cp => !DocumentCharacters(cp));
                int diffs = differing.Count, otherDiffs = other.Count;
                bool vertical = a.HheaMetrics.LineHeight * b.UnitsPerEm == b.HheaMetrics.LineHeight * a.UnitsPerEm
                    && a.WindowsMetrics.Ascender * b.UnitsPerEm == b.WindowsMetrics.Ascender * a.UnitsPerEm
                    && a.WindowsMetrics.Descender * b.UnitsPerEm == b.WindowsMetrics.Descender * a.UnitsPerEm;
                string verdict = Verdict(n, differing, max, vertical);
                if (diffs > 0)
                {
                    details.Add((installed + " / " + substitute, differing));
                }
                log.WriteLine($"{installed} | {substitute} | {n} | {diffs} | {max:0.#}{(worstCp >= 0 ? $" (U+{worstCp:X4})" : string.Empty)} | {(vertical ? "same" : "differ")} | {otherDiffs} | {verdict}");
                compared++;
            }
            if (details.Count > 0)
            {
                log.WriteLine();
                log.WriteLine("Document characters whose widths differ (installed / substitute, 1/1000 em):");
                foreach ((string pair, List<(int Cp, double A, double B)> list) in details)
                {
                    log.WriteLine(pair + ": " + Describe(list));
                }
            }
            log.WriteLine(compared + " pair(s) compared. Nothing was written.");
            return 0;
        }

        /// <summary>At most this many characters may differ by more than 1/1000 em for "metric-compatible except" (D-287).</summary>
        internal const int MaxExceptions = 5;

        /// <summary>
        /// The verdict for one pair. "metric-compatible" when no compared character differs; "(rounding)" when none differs
        /// by more than 1/1000 em; "metric-compatible except U+…" when the vertical metrics are the same and at most
        /// <see cref="MaxExceptions"/> characters differ by more (each named); otherwise "not metric-compatible".
        /// </summary>
        internal static string Verdict(int compared, IReadOnlyList<(int Cp, double A, double B)> differing, double max, bool vertical)
        {
            if (compared == 0 || !vertical)
            {
                return "not metric-compatible";
            }
            if (differing.Count == 0)
            {
                return "metric-compatible";
            }
            if (max <= 1)
            {
                return "metric-compatible (rounding)";
            }
            List<string> beyond = new();
            foreach ((int cp, double a, double b) in differing)
            {
                if (Math.Abs(a - b) > 1)
                {
                    beyond.Add("U+" + cp.ToString("X4", CultureInfo.InvariantCulture));
                }
            }
            return beyond.Count <= MaxExceptions ? "metric-compatible except " + string.Join(", ", beyond) : "not metric-compatible";
        }

        /// <summary>"U+20AA 870 / 701.5" for each character, in code point order; at most 40, then how many more.</summary>
        internal static string Describe(IReadOnlyList<(int Cp, double A, double B)> list)
        {
            const int shown = 40;
            List<string> parts = new();
            for (int i = 0; i < list.Count && i < shown; i++)
            {
                (int cp, double a, double b) = list[i];
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"U+{cp:X4} {a:0.#} / {b:0.#}"));
            }
            string text = string.Join("; ", parts);
            return list.Count > shown ? text + "; and " + (list.Count - shown) + " more" : text;
        }

        private static byte[] SubstituteBytes(string file)
            => file.StartsWith("LiPi-", StringComparison.Ordinal) ? Chuvadi.Fonts.FontPack.ReadAllBytes(file) : SubstituteFontPack.ReadAllBytes(file);

        /// <summary>The characters ordinary documents use: Basic Latin, Latin-1, Latin Extended-A, General Punctuation, currency.</summary>
        private static bool DocumentCharacters(int cp)
            => cp is (>= 0x20 and <= 0x7E) or (>= 0xA0 and <= 0x17F) or (>= 0x2000 and <= 0x206F) or (>= 0x20A0 and <= 0x20CF);

        /// <summary>
        /// Codepoints both faces map (BMP, within <paramref name="include"/>), the characters whose advances differ (by more
        /// than half a unit, with both widths), and the largest
        /// difference in 1/1000 em. A character only one face maps is not compared, and nor is an invisible formatting
        /// character (category Cf: joiners, direction marks), whose width a font may set to anything (D-255).
        /// </summary>
        private static (int Count, List<(int Cp, double A, double B)> Diffs, double Max, int WorstCp) Widths(FontFace a, FontFace b, Func<int, bool> include)
        {
            int count = 0, worst = -1;
            List<(int Cp, double A, double B)> diffs = new();
            double max = 0;
            for (int cp = 0x20; cp <= 0xFFFF; cp++)
            {
                if (cp is >= 0xD800 and <= 0xDFFF || !include(cp) || CharUnicodeInfo.GetUnicodeCategory(cp) == UnicodeCategory.Format)
                {
                    continue;
                }
                int ga = a.GetGlyphId(cp);
                int gb = b.GetGlyphId(cp);
                if (ga == 0 || gb == 0)
                {
                    continue;
                }
                count++;
                double wa = a.GetAdvance(ga) * 1000.0 / a.UnitsPerEm;
                double wb = b.GetAdvance(gb) * 1000.0 / b.UnitsPerEm;
                double d = Math.Abs(wa - wb);
                if (d > 0.5)
                {
                    diffs.Add((cp, wa, wb));
                }
                if (d > max)
                {
                    max = d;
                    worst = cp;
                }
            }
            return (count, diffs, max, worst);
        }
    }
}
