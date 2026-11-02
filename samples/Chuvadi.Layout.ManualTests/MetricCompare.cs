using System;
using System.Collections.Generic;
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
            log.WriteLine("installed | substitute | codepoints compared | width differences | largest difference | vertical metrics | verdict");
            int compared = 0;
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
                (int n, int diffs, double max, int worstCp) = Widths(a, b);
                bool vertical = a.HheaMetrics.LineHeight * b.UnitsPerEm == b.HheaMetrics.LineHeight * a.UnitsPerEm
                    && a.WindowsMetrics.Ascender * b.UnitsPerEm == b.WindowsMetrics.Ascender * a.UnitsPerEm
                    && a.WindowsMetrics.Descender * b.UnitsPerEm == b.WindowsMetrics.Descender * a.UnitsPerEm;
                string verdict = n > 0 && diffs == 0 && vertical ? "metric-compatible" : max <= 1 && vertical ? "metric-compatible (rounding)" : "not metric-compatible";
                log.WriteLine($"{installed} | {substitute} | {n} | {diffs} | {max:0.#}{(worstCp >= 0 ? $" (U+{worstCp:X4})" : string.Empty)} | {(vertical ? "same" : "differ")} | {verdict}");
                compared++;
            }
            log.WriteLine(compared + " pair(s) compared. Nothing was written.");
            return 0;
        }

        private static byte[] SubstituteBytes(string file)
            => file.StartsWith("LiPi-", StringComparison.Ordinal) ? Chuvadi.Fonts.FontPack.ReadAllBytes(file) : BundledFontSource.ReadResource(file);

        /// <summary>Codepoints both faces map (BMP), how many advances differ, and the largest difference in 1/1000 em.</summary>
        private static (int Count, int Diffs, double Max, int WorstCp) Widths(FontFace a, FontFace b)
        {
            int count = 0, diffs = 0, worst = -1;
            double max = 0;
            for (int cp = 0x20; cp <= 0xFFFF; cp++)
            {
                if (cp is >= 0xD800 and <= 0xDFFF)
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
                    diffs++;
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
