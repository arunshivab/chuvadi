using Chuvadi.Layout.Acceptance;

namespace Chuvadi.Layout.Acceptance.Tests;

/// <summary>The acceptance harness (samples/Chuvadi.Layout.Acceptance) checked on generated references.</summary>
public class AcceptanceHarnessTests
{
    [Fact]
    public void Self_test_detects_moved_lines_changed_text_and_page_breaks()
    {
        string folder = Path.Combine(Path.GetTempPath(), "chuvadi-acceptance-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            StringWriter log = new StringWriter();
            Assert.Equal(0, SelfTest.Run(folder, log));
            Assert.True(File.Exists(Path.Combine(folder, "acceptance-report.md")));
            Assert.Contains("| faithful.txt |", File.ReadAllText(Path.Combine(folder, "acceptance-report.md")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Normalizer_folds_ligatures_hyphens_and_whitespace()
    {
        Assert.Equal("fine-tuned", TextNormalizer.Normalize("\uFB01ne\u2011 tuned\u00AD"));
    }

    [Fact]
    public void Lines_match_by_longest_common_subsequence()
    {
        TextLine L(string t, double y) => new TextLine(y, 0, 10, 10, t);
        TextLine[] reference = { L("a", 10), L("b", 20), L("c", 30), L("d", 40) };
        TextLine[] layout = { L("a", 10), L("c", 30), L("x", 35), L("d", 40) };
        Assert.Equal(new[] { 0, -1, 1, 3 }, Comparer.Match(reference, layout));
    }

    [Fact]
    public void Corpus_pairs_sources_with_same_name_pdfs_or_reads_the_manifest()
    {
        string folder = Path.Combine(Path.GetTempPath(), "chuvadi-corpus-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.txt"), "x");
            File.WriteAllText(Path.Combine(folder, "a.pdf"), "x");
            File.WriteAllText(Path.Combine(folder, "b.xlsx"), "x");
            CorpusEntry e = Assert.Single(Corpus.Discover(folder));
            Assert.Equal("a.txt", Path.GetFileName(e.Source));
            Assert.Equal(Corpus.NotStated, e.Printer);

            File.WriteAllText(Path.Combine(folder, Corpus.ManifestName), "source\treference\tprinter\tnotes\n# comment\nb.xlsx\tb-ref.pdf\tMicrosoft Print to PDF\tA4\n");
            CorpusEntry m = Assert.Single(Corpus.Discover(folder));
            Assert.Equal("b-ref.pdf", Path.GetFileName(m.Reference));
            Assert.Equal("Microsoft Print to PDF", m.Printer);
            EntryResult r = Runner.Run(m, RunSettings.Default);
            Assert.Equal(EntryStatus.Error, r.Status); // the reference is missing
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
