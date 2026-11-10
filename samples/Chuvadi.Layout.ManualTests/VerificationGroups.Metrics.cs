using System;
using System.Collections.Generic;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        /// <summary>
        /// D-287 (font comparison, option 2): --compare-metrics names the characters that differ, and a pair with the same
        /// vertical metrics and at most a few differing characters is "metric-compatible except" them. Checked on the
        /// verdict itself, because the installed fonts exist only on Windows (Arun's measurements: Times New Roman differs
        /// from Liberation Serif only at U+20AA, Arial from Liberation Sans only at U+20BF).
        /// </summary>
        private static void M1_MetricVerdictNamesTheDifferingCharacters()
        {
            List<(int, double, double)> none = new();
            Check(MetricCompare.Verdict(380, none, 0, vertical: true) == "metric-compatible", "no difference");
            Check(MetricCompare.Verdict(0, none, 0, vertical: true) == "not metric-compatible", "nothing compared");

            List<(int, double, double)> rounding = new() { (0x41, 667, 666.2) };
            Check(MetricCompare.Verdict(380, rounding, 0.8, vertical: true) == "metric-compatible (rounding)", "rounding only");

            List<(int, double, double)> sheqel = new() { (0x2019, 333, 332.4), (0x20AA, 870, 701.5) };
            string v = MetricCompare.Verdict(382, sheqel, 168.5, vertical: true);
            Check(v == "metric-compatible except U+20AA", "one real difference, the rounding one not named: " + v);
            Check(MetricCompare.Verdict(382, sheqel, 168.5, vertical: false) == "not metric-compatible", "vertical metrics differ");

            List<(int, double, double)> many = new();
            for (int cp = 0x41; cp < 0x41 + MetricCompare.MaxExceptions + 1; cp++)
            {
                many.Add((cp, 600, 500));
            }
            Check(MetricCompare.Verdict(300, many, 100, vertical: true) == "not metric-compatible", "more than " + MetricCompare.MaxExceptions + " differences");
            many.RemoveAt(0);
            Check(MetricCompare.Verdict(300, many, 100, vertical: true).StartsWith("metric-compatible except U+0042, ", StringComparison.Ordinal), "exactly the limit");

            string d = MetricCompare.Describe(sheqel);
            Check(d == "U+2019 333 / 332.4; U+20AA 870 / 701.5", "listing: " + d);
            List<(int, double, double)> long45 = new();
            for (int i = 0; i < 45; i++)
            {
                long45.Add((0x100 + i, 500, 400));
            }
            Check(MetricCompare.Describe(long45).EndsWith("; and 5 more", StringComparison.Ordinal), "long listing is cut at 40");
        }
    }
}
