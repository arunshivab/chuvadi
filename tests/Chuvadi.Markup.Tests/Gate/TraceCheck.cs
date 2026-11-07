using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Chuvadi.Markdown.Gate
{
    /// <summary>
    /// Checks the trace log against the author's code:
    /// - every "// TRACE R-MD-nnnn" tag in an author file has an entry in trace/rules.md, and every entry's
    ///   location names a file that exists and carries that tag;
    /// - an entry's Status, if present, is "superseded by R-MD-nnnn" naming another entry of trace/rules.md
    ///   (the bare word "superseded" is still accepted, with a warning); a superseded entry needs no TRACE tag;
    /// - every entry has a source type (spec, experiment, general) and a citation that resolves:
    ///   a spec citation names a heading that exists in the bundled spec, with example numbers in range;
    ///   an experiment names a trace/experiments/E-nnnn.md file and a rule-5 case in trace/rule5-cases.md
    ///   (or "spec-covered" with a spec citation);
    /// - every author file carries at least one tag;
    /// - no author file mentions another Markdown implementation;
    /// - trace/sources.md lists only permitted sources.
    /// </summary>
    public static class TraceCheck
    {
        /// <summary>The files the clean-room author writes. Everything else in src/ is the given library.</summary>
        public static IEnumerable<string> AuthorFiles(string root)
        {
            string lib = Path.Combine(root, "src", "Chuvadi.Markup");
            foreach (var f in Directory.EnumerateFiles(Path.Combine(lib, "Markdown", "Parsing"), "*.cs", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                yield return f;
            yield return Path.Combine(lib, "Markdown", "MarkdownHtmlRenderer.cs");
        }

        private static readonly Regex s_tag = new(@"//\s*TRACE\s+(R-MD-\d{4})", RegexOptions.CultureInvariant);
        private static readonly Regex s_entry = new(@"^#{2,4}\s+(R-MD-\d{4})\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex s_status = new(@"^superseded(?:\s+by\s+(R-MD-\d{4}))?$", RegexOptions.CultureInvariant);
        private static readonly Regex s_r5 = new(@"^#{2,4}\s+(R5-\d{4})\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex s_spec = new(@"^(CommonMark 0\.31\.2|GFM 0\.29|GFM extensions)\s+""([^""]+)""(?:\s*,\s*examples?\s+(\d+)(?:\s*-\s*(\d+))?)?", RegexOptions.CultureInvariant);

        // Names of other implementations. A mention in the author's code or sources is a finding.
        private static readonly string[] s_forbiddenWords =
        {
            "cmark", "houdini", "commonmark.js", "commonmark-java", "markdown-it", "markdig", "md4c", "pulldown",
            "micromark", "marked.js", "remark-parse", "flexmark", "goldmark", "commonmarker", "showdown", "parsedown",
        };

        private static readonly string[] s_allowedHosts =
        {
            "spec.commonmark.org", "github.github.com/gfm", "html.spec.whatwg.org", "www.unicode.org", "unicode.org",
            "learn.microsoft.com", "www.rfc-editor.org", "datatracker.ietf.org", "url.spec.whatwg.org",
        };

        public static bool Run(string root) => Run(root, Path.Combine(root, "trace"), Path.Combine(root, "specs"));

        /// <summary>The same check with the trace and spec folders given separately (the monorepo keeps the trace in
        /// docs/Chuvadi.Markup/cleanroom and the specs in tests/Chuvadi.Markup.Tests/Conformance).</summary>
        public static bool Run(string root, string trace, string specs)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            // spec headings and example counts
            var heads = new Dictionary<string, (HashSet<string> Headings, int Examples)>(StringComparer.Ordinal)
            {
                ["CommonMark 0.31.2"] = Spec(Path.Combine(specs, "commonmark", "spec.txt")),
                ["GFM 0.29"] = Spec(Path.Combine(specs, "gfm", "gfm-spec.txt")),
                ["GFM extensions"] = Spec(Path.Combine(specs, "gfm", "extensions.txt")),
            };

            // tags in code
            var tags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            int files = 0, stubFiles = 0;
            foreach (var f in AuthorFiles(root))
            {
                if (!File.Exists(f)) { errors.Add("missing author file " + Rel(root, f)); continue; }
                files++;
                string text = File.ReadAllText(f, Encoding.UTF8);
                if (text.Contains("CLEAN-ROOM STUB", StringComparison.Ordinal)) { stubFiles++; continue; }
                var found = s_tag.Matches(text).Select(m => m.Groups[1].Value).ToList();
                if (found.Count == 0) errors.Add(Rel(root, f) + ": no TRACE tag");
                foreach (var id in found)
                {
                    if (!tags.TryGetValue(id, out var where)) tags[id] = where = new List<string>();
                    where.Add(Rel(root, f));
                }
                string lower = text.ToLowerInvariant();
                foreach (var w in s_forbiddenWords)
                    if (lower.Contains(w, StringComparison.Ordinal)) errors.Add(Rel(root, f) + ": mentions \"" + w + "\"");
            }

            // rule-5 cases
            var r5 = new HashSet<string>(StringComparer.Ordinal);
            string r5File = Path.Combine(trace, "rule5-cases.md");
            if (File.Exists(r5File))
            {
                bool fence = false;
                foreach (var line in File.ReadAllLines(r5File, Encoding.UTF8))
                {
                    if (line.StartsWith("```", StringComparison.Ordinal)) { fence = !fence; continue; }
                    if (fence) continue;
                    var m = s_r5.Match(line);
                    if (m.Success && !r5.Add(m.Groups[1].Value)) errors.Add("rule5-cases.md: duplicate " + m.Groups[1].Value);
                }
            }

            // rules
            var entries = ReadEntries(Path.Combine(trace, "rules.md"), errors);
            foreach (var (id, fields) in entries)
            {
                string loc = fields.GetValueOrDefault("Location", "");
                string source = fields.GetValueOrDefault("Source", "");
                string cite = fields.GetValueOrDefault("Citation", "");
                if (fields.GetValueOrDefault("Rule", "").Length == 0) errors.Add(id + ": no Rule line");
                string file = loc.Split(',')[0].Trim();
                if (file.Length == 0) errors.Add(id + ": no Location");
                bool superseded = false;
                string status = fields.GetValueOrDefault("Status", "");
                if (status.Length > 0)
                {
                    var sm = s_status.Match(status);
                    if (!sm.Success) warnings.Add(id + ": Status \"" + status + "\" not recognised (the documented form is \"superseded by R-MD-nnnn\"); entry treated as current");
                    else
                    {
                        superseded = true;
                        if (!sm.Groups[1].Success) warnings.Add(id + ": superseded without naming the new entry (write \"superseded by R-MD-nnnn\")");
                        else if (sm.Groups[1].Value == id) errors.Add(id + ": superseded by itself");
                        else if (!entries.ContainsKey(sm.Groups[1].Value)) errors.Add(id + ": superseded by " + sm.Groups[1].Value + ", which has no entry in trace/rules.md");
                        if (tags.ContainsKey(id)) warnings.Add(id + ": superseded but still tagged in " + string.Join(", ", tags[id].Distinct()));
                    }
                }
                if (file.Length > 0 && !superseded)
                {
                    string path = Path.Combine(root, "src", "Chuvadi.Markup", file.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path)) errors.Add(id + ": location " + file + " does not exist");
                    else if (!tags.TryGetValue(id, out var w) || !w.Any(x => x.EndsWith(file.Replace('\\', '/'), StringComparison.Ordinal)))
                        errors.Add(id + ": no TRACE tag in " + file);
                }
                switch (source)
                {
                    case "spec":
                        foreach (var c in cite.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
                            CheckSpecCitation(id, c, heads, errors);
                        break;
                    case "experiment":
                        {
                            var e = Regex.Match(fields.GetValueOrDefault("Experiment", ""), @"E-\d{4}", RegexOptions.CultureInvariant);
                            if (!e.Success || !File.Exists(Path.Combine(trace, "experiments", e.Value + ".md"))) errors.Add(id + ": experiment record missing (trace/experiments/E-nnnn.md)");
                            string rc = fields.GetValueOrDefault("Rule5", "");
                            if (rc == "spec-covered")
                            {
                                if (cite.Length == 0) errors.Add(id + ": spec-covered experiment needs a spec Citation");
                                else foreach (var c in cite.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0)) CheckSpecCitation(id, c, heads, errors);
                            }
                            else if (!r5.Contains(rc)) errors.Add(id + ": experiment rule needs \"Rule5: R5-nnnn\" (a case in rule5-cases.md) or \"Rule5: spec-covered\"");
                            break;
                        }
                    case "general":
                        if (cite.Length == 0) warnings.Add(id + ": general technique without a one-line description in Citation");
                        break;
                    default:
                        errors.Add(id + ": Source must be spec, experiment or general");
                        break;
                }
            }
            foreach (var (id, where) in tags)
                if (!entries.ContainsKey(id)) errors.Add(id + ": tagged in " + string.Join(", ", where.Distinct()) + " but has no entry in trace/rules.md");

            // sources log
            string sources = Path.Combine(trace, "sources.md");
            if (!File.Exists(sources)) errors.Add("trace/sources.md missing");
            else
            {
                foreach (var line in File.ReadAllLines(sources, Encoding.UTF8))
                {
                    string lower = line.ToLowerInvariant();
                    foreach (Match m in Regex.Matches(lower, @"https?://[^\s)>\]]+", RegexOptions.CultureInvariant))
                    {
                        string url = m.Value;
                        if (s_forbiddenWords.Any(w => url.Contains(w, StringComparison.Ordinal))) errors.Add("sources.md: forbidden source " + url);
                        else if (!s_allowedHosts.Any(h => url.Contains("://" + h, StringComparison.Ordinal))) warnings.Add("sources.md: source outside the permitted list (needs the coordinator's OK): " + url);
                    }
                }
            }

            Console.WriteLine("TRACE: " + files + " author files (" + stubFiles + " still stubs), " + tags.Count + " tagged rules, " + entries.Count + " entries, " + r5.Count + " rule-5 cases.");
            foreach (var w in warnings) Console.WriteLine("  warning: " + w);
            foreach (var e in errors) Console.WriteLine("  ERROR: " + e);
            Console.WriteLine(errors.Count == 0 ? "TRACE: PASSED" : "TRACE: NOT PASSED (" + errors.Count + " errors)");
            return errors.Count == 0;
        }

        private static void CheckSpecCitation(string id, string c, Dictionary<string, (HashSet<string> Headings, int Examples)> heads, List<string> errors)
        {
            var m = s_spec.Match(c);
            if (!m.Success) { errors.Add(id + ": citation not in the form  CommonMark 0.31.2 \"Heading\"[, examples N-M]  (or GFM 0.29 / GFM extensions): " + c); return; }
            var (h, n) = heads[m.Groups[1].Value];
            if (!h.Contains(m.Groups[2].Value)) errors.Add(id + ": no heading \"" + m.Groups[2].Value + "\" in " + m.Groups[1].Value);
            foreach (var g in new[] { m.Groups[3], m.Groups[4] })
                if (g.Success && (int.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture) < 1 || int.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture) > n))
                    errors.Add(id + ": example " + g.Value + " out of range for " + m.Groups[1].Value + " (1-" + n + ")");
        }

        private static (HashSet<string>, int) Spec(string path) => (SpecFile.Headings(path), SpecFile.Load(path).Count);

        private static Dictionary<string, Dictionary<string, string>> ReadEntries(string path, List<string> errors)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (!File.Exists(path)) { errors.Add("trace/rules.md missing"); return result; }
            Dictionary<string, string>? cur = null;
            bool fence = false;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.StartsWith("```", StringComparison.Ordinal)) { fence = !fence; continue; }
                if (fence) continue;
                var m = s_entry.Match(line);
                if (m.Success)
                {
                    if (result.ContainsKey(m.Groups[1].Value)) errors.Add("rules.md: duplicate " + m.Groups[1].Value);
                    result[m.Groups[1].Value] = cur = new Dictionary<string, string>(StringComparer.Ordinal);
                    continue;
                }
                if (cur is null) continue;
                var f = Regex.Match(line, @"^\s*-\s*([A-Za-z0-9]+):\s*(.*)$", RegexOptions.CultureInvariant);
                if (f.Success) cur[f.Groups[1].Value] = f.Groups[2].Value.Trim();
            }
            return result;
        }

        private static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    }
}
