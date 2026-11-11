using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Chuvadi.Layout;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine.Grid;
using Chuvadi.Layout.Model;
using Chuvadi.Layout.Sheets;
using Xunit;

namespace Chuvadi.Layout.Sheets.Tests;

/// <summary>
/// D-291: pages built only when asked for, the page limit on building rather than opening, and the printed range as
/// Excel measures it. The expected values in <see cref="ExcelMeasured"/> come from Excel 2021 PDF exports of the
/// workbooks in Fixtures/measured (see its README).
/// </summary>
public class PrintedPagesTests
{
    private static string Measured(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "measured", file);

    private static LaidOutDocument Open(string path, LayoutOptions? o = null)
    {
        using FileStream fs = File.OpenRead(path);
        return SheetsLayout.Open(fs, null, o ?? SheetsImportTests.Options());
    }

    private static GridEngine Engine(LaidOutDocument doc) => (GridEngine)doc.PageSource;

    /// <summary>Workbook, sheet, Excel's page count for the sheet, and the range Excel printed (from its headings).</summary>
    public static TheoryData<string, string, int, string?> ExcelMeasured => new TheoryData<string, string, int, string?>
    {
        // d287-blank-pages.xlsx: Excel prints every page of the range, blank ones included.
        { "d287-blank-pages.xlsx", "ValueFar", 15, null },
        { "d287-blank-pages.xlsx", "FillFar", 15, null },
        { "d287-blank-pages.xlsx", "BorderFar", 15, null },
        { "d287-blank-pages.xlsx", "TitlesFar", 15, null },
        { "d287-blank-pages.xlsx", "StyleOnlyFar", 1, null },
        // d287-formats.xlsx: only a visible fill or border on an empty cell extends the range.
        { "d287-formats.xlsx", "01 Fill (control)", 15, null },
        { "d287-formats.xlsx", "02 Border (control)", 15, null },
        { "d287-formats.xlsx", "03 Bold", 1, null },
        { "d287-formats.xlsx", "04 Italic underline", 1, null },
        { "d287-formats.xlsx", "05 Font name size", 1, null },
        { "d287-formats.xlsx", "06 Font colour", 1, null },
        { "d287-formats.xlsx", "07 Number format", 1, null },
        { "d287-formats.xlsx", "08 Alignment", 1, null },
        { "d287-formats.xlsx", "09 Wrap text", 1, null },
        { "d287-formats.xlsx", "10 Indent", 1, null },
        { "d287-formats.xlsx", "11 Unlocked", 1, null },
        { "d287-formats.xlsx", "12 Column width only", 1, null },
        { "d287-formats.xlsx", "13 Row height only", 1, null },
        { "d287-formats.xlsx", "14 Column fill", 1, null },
        { "d287-formats.xlsx", "15 Row fill", 1, null },
        // d287-edges.xlsx: printing starts at A1; gridlines do not change which cells count.
        { "d287-edges.xlsx", "1 Lead none", 1, "A1:D5" },
        { "d287-edges.xlsx", "2 Lead bold", 1, "A1:D5" },
        { "d287-edges.xlsx", "3 Lead fill", 1, "A1:D5" },
        { "d287-edges.xlsx", "4 Grid lead bold", 1, "A1:C3" },
        { "d287-edges.xlsx", "5 Grid trail bold", 1, "A1" },
    };

    [Theory]
    [MemberData(nameof(ExcelMeasured))]
    public void Sheet_prints_the_pages_Excel_prints(string file, string sheet, int pages, string? range)
    {
        using LaidOutDocument doc = Open(Measured(file));
        GridEngine engine = Engine(doc);
        GridWorkbook book = SheetsImporter.Import(File.OpenRead(Measured(file)), null, SheetsImportTests.Options());
        int s = book.Sheets.ToList().FindIndex(x => x.Name == sheet);
        Assert.True(s >= 0, sheet);
        List<GridAreaPages> areas = engine.Areas.Where(a => a.Sheet == s).ToList();
        Assert.Equal(pages, areas.Sum(a => a.Count));
        Assert.All(areas, a => Assert.Equal((0, 0), (a.Area.FirstRow, a.Area.FirstColumn)));
        if (range is not null)
        {
            Assert.Equal(range, Assert.Single(areas).Area.ToString());
        }
    }

    [Fact]
    public void A_sheet_reaching_the_last_cell_opens_at_once_and_shows_page_1()
    {
        // The Reader's case (D-282): A1 and XFD1048576. Before D-291 opening built every page record (3.3 GB, 5.9 s)
        // and then failed on MaxPages.
        Stopwatch watch = Stopwatch.StartNew();
        using LaidOutDocument doc = Open(Measured("extent-a1-xfd1048576.xlsx"));
        LaidOutPage first = doc.GetPage(0);
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"open and page 1 took {watch.Elapsed}");
        Assert.True(doc.IsPageCountFinal);
        Assert.True(doc.PageCount > 30_000_000, doc.PageCount.ToString(CultureInfo.InvariantCulture));
        Assert.Contains("A1", first.GetText(), StringComparison.Ordinal);
        GridAreaPages area = Assert.Single(Engine(doc).Areas);
        Assert.Equal("A1:XFD1048576", area.Area.ToString());
        Assert.Equal((long)doc.PageCount, area.Count);

        // Building past MaxPages (10,000 by default) throws with the page count and where it is.
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(() => doc.GetPage(10_000));
        Assert.Equal(nameof(LayoutLimits.MaxPages), ex.LimitName);
        Assert.Equal(10_000, ex.Limit);
        Assert.Equal(doc.PageCount, ex.Actual);
        Assert.Equal("Sheet1!A1:XFD1048576", ex.Location);
        Assert.Contains("Sheet1!A1:XFD1048576 prints as", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_higher_limit_the_last_page_holds_the_last_cell()
    {
        LayoutOptions o = SheetsImportTests.Options();
        o.Limits.MaxPages = int.MaxValue;
        using LaidOutDocument doc = Open(Measured("extent-a1-xfd1048576.xlsx"), o);
        Assert.Contains("XFD1048576", doc.GetPage(doc.PageCount - 1).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_corpus_page_matches_its_recorded_drawing()
    {
        // Each workbook page's SVG hash, recorded on main bc1bfba (before D-291), after it, and after whole letters at
        // page edges. Consecutive records differ only on the pages listed; today's output must match the last record.
        Dictionary<string, string> before = Read("printed-pages-before-d291.tsv");
        Dictionary<string, string> after = Read("printed-pages-after-d291.tsv");
        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
        string[] changed = before.Keys.Where(k => before[k] != after[k]).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(ChangedByD291.OrderBy(k => k, StringComparer.Ordinal), changed);

        // Then whole letters at page edges (Arun's option B for x03): only the pages where overflowing text crosses a
        // page edge change.
        Dictionary<string, string> whole = Read("printed-pages-whole-letters.tsv");
        Assert.Equal(after.Keys.OrderBy(k => k, StringComparer.Ordinal), whole.Keys.OrderBy(k => k, StringComparer.Ordinal));
        string[] changedByWholeLetters = after.Keys.Where(k => after[k] != whole[k]).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(ChangedByWholeLetters.OrderBy(k => k, StringComparer.Ordinal), changedByWholeLetters);

        Dictionary<string, string> now = Hashes();
        Assert.Equal(whole.Count, now.Count);
        foreach ((string key, string hash) in whole)
        {
            Assert.True(now.TryGetValue(key, out string? h), "missing page " + key);
            Assert.True(h == hash, "page differs: " + key);
        }
    }

    /// <summary>
    /// The pages D-291 changes: empty formatted cells no longer extend the printed range. Page counts and every other
    /// page are unchanged.
    /// </summary>
    private static readonly string[] ChangedByD291 =
    {
        "ConditionalFormattingSamples.xlsx p1", "ConditionalFormattingSamples.xlsx p2", "ConditionalFormattingSamples.xlsx p8",
        "ConditionalFormattingSamples.xlsx p9", "ConditionalFormattingSamples.xlsx p14", "ConditionalFormattingSamples.xlsx p15",
        "ConditionalFormattingSamples.xlsx p27", "ConditionalFormattingSamples.xlsx p35", "ConditionalFormattingSamples.xlsx p36",
        "x05-borders.xlsx p1",
    };

    /// <summary>
    /// The pages where text overflowing across a page edge now ends and starts with whole letters: x03 (Tamil), x22,
    /// and ConditionalFormattingSamples page 1 (the "n" of "button") and page 9 (its clip moves; no pixel changes).
    /// </summary>
    private static readonly string[] ChangedByWholeLetters =
    {
        "ConditionalFormattingSamples.xlsx p1", "ConditionalFormattingSamples.xlsx p9",
        "x03-fonts-alignment.xlsx p1", "x03-fonts-alignment.xlsx p2", "x22-overflow.xlsx p1", "x22-overflow.xlsx p2",
    };

    private static Dictionary<string, string> Read(string name)
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", name)))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] f = line.Split('\t');
            map[f[0]] = f[1];
        }
        return map;
    }

    /// <summary>Every page of every workbook in the reference corpus and the Excel fixtures, keyed "file pN".</summary>
    internal static Dictionary<string, string> Hashes()
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in CorpusWorkbooks())
        {
            using LaidOutDocument doc = Open(path);
            int n = doc.ResolvePageCount();
            for (int i = 0; i < n; i++)
            {
                byte[] svg = Encoding.UTF8.GetBytes(LayoutSvg.ToSvg(doc.GetPage(i)));
                map[Path.GetFileName(path) + " p" + (i + 1).ToString(CultureInfo.InvariantCulture)] = Convert.ToHexString(SHA256.HashData(svg));
            }
        }
        string? write = Environment.GetEnvironmentVariable("CHUVADI_WRITE_PAGE_HASHES");
        if (!string.IsNullOrEmpty(write))
        {
            File.WriteAllLines(write, map.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "\t" + k.Value));
        }
        return map;
    }

    private static IEnumerable<string> CorpusWorkbooks()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Chuvadi.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("repository root not found");
        }
        IEnumerable<string> corpus = Directory.GetFiles(Path.Combine(root, "staging", "tests", "Chuvadi.Layout.ReferenceCorpus", "corpus"), "*.xlsx");
        IEnumerable<string> fixtures = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xlsx"), "*.xlsx");
        return corpus.Concat(fixtures).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal);
    }
}
