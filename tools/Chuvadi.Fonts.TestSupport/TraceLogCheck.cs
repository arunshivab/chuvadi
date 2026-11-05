// Checks the clean-room trace log (D-74): every "TRACE R-xx-nnnn" tag in the
// code has an entry in the library's trace/rules.md, and every entry names a
// code file that exists and still carries its tag.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Chuvadi.TestSupport;

/// <summary>Cross-checks TRACE tags in code against a trace/rules.md file.</summary>
public static class TraceLogCheck
{
    private static readonly Regex TagInCode = new Regex(@"TRACE (R-[A-Z]{2}-\d{4})", RegexOptions.CultureInvariant);
    private static readonly Regex EntryHeading = new Regex(@"^## (R-[A-Z]{2}-\d{4})\b", RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex CodeLine = new Regex(@"^- Code: (.+)$", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>Finds the repository root: the nearest ancestor folder that holds both src/ and docs/.</summary>
    /// <returns>The folder path.</returns>
    public static string FindRepositoryRoot()
    {
        for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src")) && Directory.Exists(Path.Combine(d.FullName, "docs")) && Directory.Exists(Path.Combine(d.FullName, "tools")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("Cannot find the repository root (a folder with src/, docs/ and tools/).");
    }

    /// <summary>Returns the problems found; an empty list means the log and the code agree.</summary>
    /// <param name="root">The repository root.</param>
    /// <param name="rulesFile">The rules.md path relative to the root.</param>
    /// <param name="prefix">The rule ID prefix this log owns, for example "R-OT-".</param>
    /// <param name="codeFolders">Code folders (relative to the root) whose tags with this prefix must be logged.</param>
    /// <returns>The problems.</returns>
    public static IReadOnlyList<string> Check(string root, string rulesFile, string prefix, params string[] codeFolders)
    {
        List<string> problems = new List<string>();
        string rules = File.ReadAllText(Path.Combine(root, rulesFile));
        Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.Ordinal);
        MatchCollection headings = EntryHeading.Matches(rules);
        for (int i = 0; i < headings.Count; i++)
        {
            int start = headings[i].Index;
            int end = i + 1 < headings.Count ? headings[i + 1].Index : rules.Length;
            string id = headings[i].Groups[1].Value;
            if (!entries.TryAdd(id, rules.Substring(start, end - start)))
            {
                problems.Add($"{id}: listed twice in {rulesFile}");
            }
        }

        Dictionary<string, HashSet<string>> tagsByFile = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string folder in codeFolders)
        {
            string full = Path.Combine(root, folder);
            foreach (string file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                foreach (Match m in TagInCode.Matches(File.ReadAllText(file)))
                {
                    string id = m.Groups[1].Value;
                    if (!id.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!tagsByFile.TryGetValue(rel, out HashSet<string>? set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        tagsByFile[rel] = set;
                    }

                    set.Add(id);
                    if (!entries.ContainsKey(id))
                    {
                        problems.Add($"{id}: tagged in {rel} but has no entry in {rulesFile}");
                    }
                }
            }
        }

        foreach (KeyValuePair<string, string> kv in entries)
        {
            MatchCollection codes = CodeLine.Matches(kv.Value);
            if (codes.Count == 0)
            {
                problems.Add($"{kv.Key}: entry has no '- Code:' line");
                continue;
            }

            foreach (Match c in codes)
            {
                foreach (string path in c.Groups[1].Value.Split(',').Select(p => p.Trim().Trim('`')))
                {
                    if (!File.Exists(Path.Combine(root, path)))
                    {
                        problems.Add($"{kv.Key}: code file {path} does not exist");
                    }
                    else if (!tagsByFile.TryGetValue(path, out HashSet<string>? set) || !set.Contains(kv.Key))
                    {
                        problems.Add($"{kv.Key}: {path} does not carry the tag");
                    }
                }
            }
        }

        return problems;
    }

    private static bool IsBuildOutput(string path)
    {
        string p = path.Replace('\\', '/');
        return p.Contains("/bin/", StringComparison.Ordinal) || p.Contains("/obj/", StringComparison.Ordinal);
    }
}
