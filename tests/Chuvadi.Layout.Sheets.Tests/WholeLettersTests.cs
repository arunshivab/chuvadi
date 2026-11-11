using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Layout;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Sheets;
using Xunit;

namespace Chuvadi.Layout.Sheets.Tests;

/// <summary>
/// Text that overflows across a page edge (F11) is never cut through a letter: each shaping cluster is drawn whole on
/// the page where it starts and left out of the next one (Arun's choice B; Excel 2021 cuts through the letter,
/// measured on Fixtures/measured/overflow-split.xlsx). A letter is a grapheme cluster (a consonant with its vowel signs
/// and pulli in Tamil). Checked on every page, and the pages together must show each line exactly once, in order.
/// </summary>
public class WholeLettersTests
{
    private const string Latin = "The quick brown fox jumps over the lazy dog. Insurance consultant pharmacy machine was follow-up; radiation planning dose review and discharge summary for the oncology ward. ";
    private const string Tamil = "நோயாளியின் உடல்நிலை சீராக உள்ளது; மருத்துவர் நாளை மீண்டும் பார்வையிடுவார். தமிழ் உலகின் மிகப் பழமையான மொழிகளில் ஒன்று. ";

    private static LaidOutDocument Open(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return SheetsLayout.Open(fs, null, SheetsImportTests.Options());
    }

    private static string Measured(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "measured", file);

    private static string Corpus(string file)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Chuvadi.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("repository root not found");
        }
        return Path.Combine(root, "staging", "tests", "Chuvadi.Layout.ReferenceCorpus", "corpus", file);
    }

    [Fact]
    public void No_page_cuts_a_letter_and_the_pages_together_show_each_line_once()
    {
        using LaidOutDocument doc = Open(Measured("overflow-split.xlsx"));
        Assert.Equal(6, doc.ResolvePageCount()); // as Excel prints it
        Dictionary<double, StringBuilder> rows = Visible(doc);

        string[] expected =
        {
            "Calibration: long text overflowing across page boundaries (no print area, no wrap). Export to PDF; do not change anything.",
            Latin + Latin,
            Tamil + Tamil,
            Tamil + Tamil,
            new string('W', 250),
            "|" + new string('ம', 120),
            "Next row: same Tamil text starting in column D",
            Tamil + Tamil,
        };
        List<string> seen = rows.OrderBy(r => r.Key).Select(r => Squash(r.Value.ToString())).ToList();
        Assert.Equal(expected.Select(Squash), seen);
    }

    [Fact]
    public void x03_breaks_the_Tamil_line_between_letters_not_through_one()
    {
        using LaidOutDocument doc = Open(Corpus("x03-fonts-alignment.xlsx"));
        Assert.Equal(2, doc.ResolvePageCount());
        Dictionary<double, StringBuilder> rows = Visible(doc);
        string line = "நோயாளியின் உடல்நிலை சீராக உள்ளது; மருத்துவர் நாளை மீண்டும் பார்வையிடுவார்.";
        Assert.Contains(rows.Values, r => Squash(r.ToString()) == Squash(line));

        // The page edge falls in டு, which starts on page 1, so page 1 ends with the whole டு and page 2 starts with வா.
        // Before, the edge cut வ in half and its right half read as ப on page 2 ("பார்.").
        string page1 = Squash(string.Concat(VisibleClusters(doc.GetPage(0)).Select(c => c.Text)));
        string page2 = Squash(string.Concat(VisibleClusters(doc.GetPage(1)).Select(c => c.Text)));
        Assert.EndsWith("பார்வையிடு", page1, StringComparison.Ordinal);
        Assert.Equal("வார்.", page2);
    }

    /// <summary>
    /// The text of every cluster drawn whole inside its clip, per baseline, across all pages in order. Fails if a
    /// cluster is partly inside a clip: that is a letter cut by a page edge.
    /// </summary>
    private static Dictionary<double, StringBuilder> Visible(LaidOutDocument doc)
    {
        Dictionary<double, StringBuilder> rows = new Dictionary<double, StringBuilder>();
        int n = doc.ResolvePageCount();
        for (int i = 0; i < n; i++)
        {
            foreach ((double y, string text) in VisibleClusters(doc.GetPage(i)))
            {
                if (!rows.TryGetValue(y, out StringBuilder? sb))
                {
                    rows[y] = sb = new StringBuilder();
                }
                sb.Append(text);
            }
        }
        return rows;
    }

    private static List<(double Y, string Text)> VisibleClusters(LaidOutPage page)
    {
        const double Tol = 0.01;
        List<(double Y, double X, string Text)> found = new List<(double, double, string)>();
        Stack<LayoutRect> clips = new Stack<LayoutRect>();
        foreach (LayoutOp op in page.DisplayList)
        {
            if (op is PushClipOp push)
            {
                clips.Push(push.Rect);
            }
            else if (op is PopClipOp)
            {
                clips.Pop();
            }
            else if (op is GlyphRunOp run && run.Glyphs.Count > 0)
            {
                double left = clips.Count > 0 ? clips.Max(c => c.X) : double.NegativeInfinity;
                double right = clips.Count > 0 ? clips.Min(c => c.Right) : double.PositiveInfinity;
                // Letters are grapheme clusters, worked out here independently of the engine.
                int[] letters = System.Globalization.StringInfo.ParseCombiningCharacters(run.Text);
                for (int k = 0; k < letters.Length; k++)
                {
                    int from = letters[k];
                    int to = k + 1 < letters.Length ? letters[k + 1] : run.Text.Length;
                    PositionedGlyph[] glyphs = run.Glyphs.Where(g => g.Cluster >= from && g.Cluster < to).ToArray();
                    if (glyphs.Length == 0)
                    {
                        continue;
                    }
                    double x0 = glyphs.Min(g => run.X + g.X);
                    double x1 = glyphs.Max(g => run.X + g.X + (g.Advance * run.HorizontalScale));
                    string text = run.Text.Substring(from, to - from);
                    bool inside = x0 >= left - Tol && x1 <= right + Tol;
                    bool outside = x1 <= left + Tol || x0 >= right - Tol;
                    Assert.True(inside || outside, $"page {page.Index + 1}: '{text}' at {x0:0.##}-{x1:0.##} is cut by the clip {left:0.##}-{right:0.##}");
                    if (inside)
                    {
                        found.Add((Math.Round(run.Y, 2), x0, text));
                    }
                }
            }
        }
        return found.OrderBy(f => f.Y).ThenBy(f => f.X).Select(f => (f.Y, f.Text)).ToList();
    }

    /// <summary>Spaces are drawn as gaps, not clusters, so the comparison leaves them out.</summary>
    private static string Squash(string s) => new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
}
