using System.Globalization;
using Chuvadi.Layout.Acceptance;

namespace Chuvadi.Layout.Acceptance.Tests;

/// <summary>
/// The reference corpus (D-40, staging/tests/Chuvadi.Layout.ReferenceCorpus): every document laid out again and compared
/// with its LibreOffice reference, against the baseline in expected.tsv. A document that passed must still pass; none may
/// change its page count or match fewer reference lines than it did. Improvements are fine (update expected.tsv).
/// </summary>
public class ReferenceCorpusTests
{
    private sealed record Expected(string File, int LayoutPages, string Status, double LinesMatched);

    [Fact]
    public void Reference_corpus_matches_its_baseline()
    {
        string root = CorpusRoot();
        Dictionary<string, Expected> expected = ReadExpected(Path.Combine(root, "expected.tsv"));
        List<CorpusEntry> entries = Corpus.Discover(Path.Combine(root, "corpus"));
        Assert.Equal(expected.Count, entries.Count);
        List<string> problems = new List<string>();
        foreach (CorpusEntry entry in entries)
        {
            string name = Path.GetFileName(entry.Source);
            Assert.True(expected.TryGetValue(name, out Expected? e), name + " is not in expected.tsv");
            EntryResult r = Runner.Run(entry, RunSettings.Default);
            if (r.Status != EntryStatus.Compared || r.Comparison is not FileComparison c)
            {
                problems.Add($"{name}: {r.Status} {r.Message}");
                continue;
            }
            if (c.LayoutPages != e!.LayoutPages)
            {
                problems.Add($"{name}: {c.LayoutPages} pages, baseline {e.LayoutPages}");
            }
            if (e.Status == "pass" && !c.GeometryPass)
            {
                problems.Add($"{name}: passed in the baseline, now {r.Message}");
            }
            double lines = c.LineMatchRatio * 100;
            if (lines < e.LinesMatched - 0.1) // expected.tsv holds the report's value, rounded to 0.1
            {
                problems.Add($"{name}: {lines:0.0}% of reference lines matched, baseline {e.LinesMatched}%");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static Dictionary<string, Expected> ReadExpected(string path)
    {
        Dictionary<string, Expected> map = new Dictionary<string, Expected>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(path);
        string[] head = lines[0].Split('\t');
        int file = Array.IndexOf(head, "file"), pages = Array.IndexOf(head, "layout_pages"), status = Array.IndexOf(head, "status"), matched = Array.IndexOf(head, "lines_matched");
        foreach (string line in lines.Skip(1))
        {
            if (line.Length == 0)
            {
                continue;
            }
            string[] c = line.Split('\t');
            map[c[file]] = new Expected(c[file], int.Parse(c[pages], CultureInfo.InvariantCulture), c[status], double.Parse(c[matched], CultureInfo.InvariantCulture));
        }
        return map;
    }

    private static string CorpusRoot()
    {
        DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            string candidate = Path.Combine(d.FullName, "staging", "tests", "Chuvadi.Layout.ReferenceCorpus");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("staging/tests/Chuvadi.Layout.ReferenceCorpus not found above " + AppContext.BaseDirectory);
    }
}
