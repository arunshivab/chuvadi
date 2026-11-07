using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Chuvadi.Markdown.Gate;
using Chuvadi.Markup.Markdown;

namespace Chuvadi.Markup.Tests.Gate
{
    /// <summary>
    /// The frozen clean-room gate (D-106, D-194): every case of Gate/Data/expected.jsonl.gz is run through every
    /// check it carries and compared byte for byte with the frozen output. One test per set. The comparison is the
    /// gate's own (GateCore.cs, Cases.cs and TraceCheck.cs are the gate files of the clean-room bundle; only
    /// TraceCheck gained an overload for this repository's folders). Rule-5 rules in Gate/Data/rule5-rules.txt
    /// apply as in the gate; an item still pending counts as a failure.
    /// </summary>
    public sealed class FrozenGateTests
    {
        /// <summary>SHA-256 of Gate/Data/expected.jsonl.gz. A refreeze (an approved rule-5 change only) changes this
        /// value in the same PR, together with the refreeze report.</summary>
        public const string ExpectedSha256 = "584E07B4E612637B0F5D0330D4D709FE061975053E50D0D0F12A42C55E591F8B";

        internal static string DataDir => Path.Combine(AppContext.BaseDirectory, "Gate", "Data");

        internal static string DataFile(string name) => Path.Combine(DataDir, name);

        private static readonly Lazy<List<GateCase>> s_cases = new(() => CaseFile.Read(DataFile("expected.jsonl.gz")).ToList());

        internal static List<GateCase> Cases => s_cases.Value;

        public static IEnumerable<object[]> Sets() =>
            new[] { "commonmark", "gfm-spec", "gfm-extensions", "documents", "fuzz", "limits", "labels", "urls", "pathological",
                    "contract", "diff-core", "diff-full", "slugs", "diff-domain" }.Select(s => new object[] { s });

        [Fact]
        public void FrozenDataIsTheReviewedFile()
        {
            using var fs = File.OpenRead(DataFile("expected.jsonl.gz"));
            Assert.Equal(ExpectedSha256, Convert.ToHexString(SHA256.HashData(fs)));
        }

        [Fact]
        public void EverySetIsCovered()
        {
            var inFile = Cases.Select(c => c.Set).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var tested = Sets().Select(s => (string)s[0]).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            Assert.Equal(inFile, tested);
        }

        [Theory]
        [MemberData(nameof(Sets))]
        public void SetMatchesFrozenOutput(string set)
        {
            var pending = Rule5.Load(DataFile("rule5-rules.txt"));
            var failures = new List<string>();
            int checks = 0, failed = 0;
            OnBigStack(() =>
            {
                foreach (var c in Cases.Where(x => x.Set == set))
                {
                    foreach (var kv in c.Expected)
                    {
                        checks++;
                        string check = kv.Key;
                        string actual = Safe(() => check.StartsWith("probe/", StringComparison.Ordinal)
                            ? GateCore.Probe(check.Substring(6)) : GateCore.Frozen(GateCore.Run(check, c.Input)));
                        if (actual == kv.Value || Rule5.Accepts(pending, check, kv.Value, actual)) continue;
                        failed++;
                        if (failures.Count < 20) failures.Add(c.Id + "  " + check + "\n" + FirstDifference(kv.Value, actual));
                    }
                }
            });
            Assert.True(checks > 0, "no cases in set " + set);
            Assert.True(failed == 0, set + ": " + failed + " of " + checks + " checks differ from the frozen output. First differences:\n" + string.Join("\n", failures));
        }

        [Fact]
        public void PublicApiIsUnchanged()
        {
            string expected = File.ReadAllText(DataFile("public-api.txt"), Encoding.UTF8);
            Assert.Equal(expected, GateCore.ApiListing(typeof(MarkdownParser).Assembly));
        }

        [Fact]
        public void SpecScoresAreTheBaseline()
        {
            var baseline = File.ReadAllLines(DataFile("baseline-scores.txt"), Encoding.UTF8);
            int cmPass = 0, cmTotal = 0, gfmPass = 0, gfmTotal = 0, extPass = 0, extTotal = 0;
            var gfmDiff = new List<string>();
            OnBigStack(() =>
            {
                foreach (var c in Cases)
                {
                    if (c.Set == "commonmark")
                    {
                        cmTotal++;
                        if (Safe(() => GateCore.Run("html/P-CM/H-Spec", c.Input)) == c.Meta["specHtml"]) cmPass++;
                    }
                    else if (c.Set == "gfm-spec")
                    {
                        gfmTotal++;
                        if (Safe(() => GateCore.Run("html/P-GFM/H-GfmSpec", c.Input)) == c.Meta["specHtml"]) gfmPass++;
                        else gfmDiff.Add(c.Meta["example"]);
                    }
                    else if (c.Set == "gfm-extensions" && c.Meta["specHtml"] != "<IGNORE>\n" && !c.Meta["section"].Contains("ootnote", StringComparison.Ordinal)
                        && !c.Input.Contains("[^", StringComparison.Ordinal))
                    {
                        extTotal++;
                        if (Safe(() => GateCore.Run("html/P-GFM/H-GfmSpec", c.Input)) == c.Meta["specHtml"]) extPass++;
                    }
                }
            });
            Assert.Equal(baseline[0], "commonmark: " + cmPass + " / " + cmTotal + " match the spec HTML (html/P-CM/H-Spec)");
            Assert.Equal(baseline[1], "gfm-spec: " + gfmPass + " / " + gfmTotal + " match the spec HTML (html/P-GFM/H-GfmSpec); differing examples: " + string.Join(",", gfmDiff));
            Assert.Equal(baseline[3], "gfm-extensions (footnotes excluded): " + extPass + " / " + extTotal + " match the spec HTML (html/P-GFM/H-GfmSpec)");
        }

        [Fact]
        public void TraceRecordMatchesTheCode()
        {
            string root = RepoRoot();
            var output = new StringWriter();
            var old = Console.Out;
            bool ok;
            try
            {
                Console.SetOut(output);
                ok = TraceCheck.Run(root, Path.Combine(root, "docs", "Chuvadi.Markup", "cleanroom", "trace"),
                    Path.Combine(root, "tests", "Chuvadi.Markup.Tests", "Conformance"));
            }
            finally { Console.SetOut(old); }
            Assert.True(ok, output.ToString());
        }

        /// <summary>
        /// Refreeze for an approved rule-5 change only. Does nothing unless CHUVADI_MARKUP_REFREEZE=1. It writes
        /// expected.jsonl.gz next to the source data file from the current build, and refreeze-report.txt listing every
        /// changed case and check, for the PR that carries the change (and the new ExpectedSha256).
        /// </summary>
        [Fact]
        public void RefreezeWhenAsked()
        {
            if (Environment.GetEnvironmentVariable("CHUVADI_MARKUP_REFREEZE") != "1") return;
            string target = Path.Combine(RepoRoot(), "tests", "Chuvadi.Markup.Tests", "Gate", "Data");
            var report = new StringBuilder();
            var changedKinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var fresh = new List<GateCase>();
            OnBigStack(() =>
            {
                foreach (var c in Cases)
                {
                    var n = new GateCase { Id = c.Id, Set = c.Set, Meta = c.Meta, Input = c.Input };
                    var changed = new List<string>();
                    foreach (var kv in c.Expected)
                    {
                        string v = Safe(() => kv.Key.StartsWith("probe/", StringComparison.Ordinal)
                            ? GateCore.Probe(kv.Key.Substring(6)) : GateCore.Frozen(GateCore.Run(kv.Key, c.Input)));
                        n.Expected[kv.Key] = v;
                        if (v != kv.Value)
                        {
                            changed.Add(kv.Key);
                            string kind = kv.Key.Split('/')[0];
                            changedKinds[kind] = changedKinds.GetValueOrDefault(kind) + 1;
                        }
                    }
                    if (changed.Count > 0) report.Append(c.Id).Append(" (").Append(changed.Count).Append(" checks): ").Append(string.Join(", ", changed)).Append('\n');
                    fresh.Add(n);
                }
            });
            CaseFile.Write(Path.Combine(target, "expected.jsonl.gz"), fresh);
            report.Insert(0, "Changed outputs by kind: " + string.Join(", ", changedKinds.Select(k => k.Key + " " + k.Value)) + "\n\n");
            File.WriteAllText(Path.Combine(target, "refreeze-report.txt"), report.ToString(), new UTF8Encoding(false));
        }

        // ---------- helpers ----------

        internal static void OnBigStack(Action a)
        {
            Exception? error = null;
            var t = new Thread(() => { try { a(); } catch (Exception ex) { error = ex; } }, 256 * 1024 * 1024);
            t.Start();
            t.Join();
            if (error is not null) throw new InvalidOperationException("gate run failed", error);
        }

        internal static string Safe(Func<string> f)
        {
            try { return f(); } catch (Exception ex) { return GateCore.DescribeException(ex); }
        }

        internal static string RepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !File.Exists(Path.Combine(d.FullName, "Chuvadi.slnx"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("Chuvadi.slnx not found above " + AppContext.BaseDirectory);
        }

        private static string FirstDifference(string expected, string actual)
        {
            var e = expected.Split('\n');
            var a = actual.Split('\n');
            int i = 0;
            while (i < e.Length && i < a.Length && e[i] == a[i]) i++;
            string el = i < e.Length ? e[i] : "<end>";
            string al = i < a.Length ? a[i] : "<end>";
            return "    line " + (i + 1).ToString(CultureInfo.InvariantCulture) + "\n    expected: " + Cut(el) + "\n    actual:   " + Cut(al);
        }

        private static string Cut(string s) => s.Length > 300 ? s.Substring(0, 300) + "..." : s;
    }

    /// <summary>The time budgets of the pathological inputs, as in the gate (best of 2 runs). Serial, so other tests
    /// cannot slow it down.</summary>
    [Collection(TimingCollection.Name)]
    public sealed class FrozenGateTimingTests
    {
        [Fact]
        public void PathologicalInputsStayWithinBudget()
        {
            var slow = new List<string>();
            FrozenGateTests.OnBigStack(() =>
            {
                foreach (var c in FrozenGateTests.Cases.Where(x => x.Set == "pathological"))
                {
                    double budget = double.Parse(c.Meta["budgetSeconds"], CultureInfo.InvariantCulture);
                    double best = double.MaxValue;
                    for (int r = 0; r < 2; r++)
                    {
                        GC.Collect();
                        var sw = Stopwatch.StartNew();
                        FrozenGateTests.Safe(() => GateCore.Run("html/P-GFM/H-GfmSpec", c.Input));
                        best = Math.Min(best, sw.Elapsed.TotalSeconds);
                    }
                    if (best >= budget) slow.Add(c.Meta["name"] + ": " + best.ToString("F3", CultureInfo.InvariantCulture) + " s (budget " + c.Meta["budgetSeconds"] + " s)");
                }
            });
            Assert.True(slow.Count == 0, "over budget:\n" + string.Join("\n", slow));
        }
    }

    /// <summary>The gate's rule-5 rules (Gate/Data/rule5-rules.txt), read and applied exactly as the gate does.</summary>
    internal static class Rule5
    {
        internal sealed record Rule(string Id, string CheckPrefix, string ExpectedContains, bool IgnoreActual, bool Decided);

        internal static List<Rule> Load(string path)
        {
            var list = new List<Rule>();
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var f = line.Split('|').Select(x => x.Trim()).ToArray();
                list.Add(new Rule(f[0], f[1], f[2], f[3] == "ignore-actual", f[4] == "decided"));
            }
            return list;
        }

        /// <summary>True when a decided rule accepts the difference. A pending rule never accepts.</summary>
        internal static bool Accepts(List<Rule> rules, string check, string expected, string actual)
        {
            var p = rules.FirstOrDefault(x => check.StartsWith(x.CheckPrefix, StringComparison.Ordinal) && (x.IgnoreActual
                ? expected.Contains(x.ExpectedContains, StringComparison.Ordinal) && StripActual(actual) == StripActual(expected)
                : expected.Contains(x.ExpectedContains, StringComparison.Ordinal) || actual.Contains(x.ExpectedContains, StringComparison.Ordinal)));
            return p is not null && p.Decided;
        }

        private static string StripActual(string s)
        {
            int i = s.IndexOf(" actual=", StringComparison.Ordinal);
            if (i < 0 || !s.StartsWith("!EXCEPTION", StringComparison.Ordinal)) return s;
            int j = s.IndexOf(' ', i + 1);
            string head = s.Substring(0, i) + (j < 0 ? "" : s.Substring(j));
            return System.Text.RegularExpressions.Regex.Replace(head, "reached [0-9]+", "reached N");
        }
    }
}
