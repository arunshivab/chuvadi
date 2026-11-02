using System.Reflection;
using System.Text;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.Tests;

public class SegmentationConformanceTests
{
    private static IEnumerable<(string Line, int[] Cps, bool[] Breaks)> ReadTestFile(string name)
    {
        using Stream s = typeof(SegmentationConformanceTests).Assembly.GetManifestResourceStream("Chuvadi.Layout.Tests.Data." + name)!;
        using StreamReader r = new StreamReader(s, Encoding.UTF8);
        string? line;
        while ((line = r.ReadLine()) is not null)
        {
            string body = line.Split('#')[0].Trim();
            if (body.Length == 0)
            {
                continue;
            }
            string[] tokens = body.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<int> cps = new List<int>();
            List<bool> breaks = new List<bool>();
            foreach (string t in tokens)
            {
                if (t == "\u00F7")
                {
                    breaks.Add(true);
                }
                else if (t == "\u00D7")
                {
                    breaks.Add(false);
                }
                else
                {
                    cps.Add(Convert.ToInt32(t, 16));
                }
            }
            yield return (line, cps.ToArray(), breaks.ToArray());
        }
    }

    private static string ToUtf16(int[] cps, out int[] offsets)
    {
        StringBuilder sb = new StringBuilder();
        offsets = new int[cps.Length + 1];
        for (int i = 0; i < cps.Length; i++)
        {
            offsets[i] = sb.Length;
            sb.Append(char.ConvertFromUtf32(cps[i] is >= 0xD800 and <= 0xDFFF ? 0xFFFD : cps[i]));
        }
        offsets[cps.Length] = sb.Length;
        return sb.ToString();
    }

    [Fact]
    public void LineBreakTest_txt_passes_completely()
    {
        int total = 0;
        List<string> failures = new List<string>();
        foreach ((string line, int[] cps, bool[] expected) in ReadTestFile("LineBreakTest.txt"))
        {
            total++;
            LineBreakOpportunity[] got = new LineBreakOpportunity[cps.Length + 1];
            LineBreaker.Compute(cps, got);
            for (int k = 1; k <= cps.Length; k++)
            {
                bool brk = got[k] != LineBreakOpportunity.NoBreak;
                if (brk != expected[k])
                {
                    failures.Add($"at {k}: {line}");
                    break;
                }
            }
        }
        Assert.True(total > 15000, "test file not loaded");
        Assert.True(failures.Count == 0, $"{failures.Count} of {total} failed. First: {string.Join(Environment.NewLine, failures.Take(10))}");
    }

    [Fact]
    public void GraphemeBreakTest_txt_passes_completely()
    {
        int total = 0;
        List<string> failures = new List<string>();
        foreach ((string line, int[] cps, bool[] expected) in ReadTestFile("GraphemeBreakTest.txt"))
        {
            total++;
            string s = ToUtf16(cps, out int[] offsets);
            bool[] got = GraphemeClusters.GetBoundaries(s);
            for (int k = 1; k <= cps.Length; k++)
            {
                if (got[offsets[k]] != expected[k])
                {
                    failures.Add($"at {k}: {line}");
                    break;
                }
            }
        }
        Assert.True(total > 700, "test file not loaded");
        Assert.True(failures.Count == 0, $"{failures.Count} of {total} failed. First: {string.Join(Environment.NewLine, failures.Take(10))}");
    }
}

public class LineBreakerTests
{
    private static string Mark(string text)
    {
        LineBreakOpportunity[] o = LineBreaker.GetOpportunities(text);
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (i > 0 && o[i] == LineBreakOpportunity.Allowed)
            {
                sb.Append('|');
            }
            else if (i > 0 && o[i] == LineBreakOpportunity.Mandatory)
            {
                sb.Append('\u00B6');
            }
            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    [Theory]
    [InlineData("Hello world, again.", "Hello |world, |again.")]
    [InlineData("well-known fact", "well-|known |fact")]
    [InlineData("(see p. 12)", "(see |p. |12)")]
    [InlineData("$12.50 each", "$12.50 |each")]
    [InlineData("line1\nline2", "line1\n\u00B6line2")]
    [InlineData("a\u00A0b c", "a\u00A0b |c")]
    public void Latin_examples(string text, string expected)
    {
        Assert.Equal(expected, Mark(text));
    }

    [Fact]
    public void End_of_text_is_mandatory_and_start_is_not_a_break()
    {
        LineBreakOpportunity[] o = LineBreaker.GetOpportunities("ab");
        Assert.Equal(LineBreakOpportunity.NoBreak, o[0]);
        Assert.Equal(LineBreakOpportunity.Mandatory, o[2]);
        Assert.Equal(new[] { LineBreakOpportunity.Mandatory }, LineBreaker.GetOpportunities(""));
    }

    [Fact]
    public void Surrogate_pairs_are_never_split()
    {
        string s = "a\U0001F600b";
        LineBreakOpportunity[] o = LineBreaker.GetOpportunities(s);
        Assert.Equal(LineBreakOpportunity.NoBreak, o[2]); // inside the pair
    }

    [Fact]
    public void Tamil_words_break_only_at_spaces()
    {
        // "\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD \u0BAE\u0BCA\u0BB4\u0BBF" (Tamil language): no break inside either word.
        string s = "\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD \u0BAE\u0BCA\u0BB4\u0BBF";
        LineBreakOpportunity[] o = LineBreaker.GetOpportunities(s);
        int[] allowed = Enumerable.Range(1, s.Length - 1).Where(i => o[i] == LineBreakOpportunity.Allowed).ToArray();
        Assert.Equal(new[] { 6 }, allowed);
    }

    [Fact]
    public void Ideographs_break_between_characters()
    {
        string s = "\u65E5\u672C\u8A9E";
        LineBreakOpportunity[] o = LineBreaker.GetOpportunities(s);
        Assert.Equal(LineBreakOpportunity.Allowed, o[1]);
        Assert.Equal(LineBreakOpportunity.Allowed, o[2]);
    }

    [Fact]
    public void Result_span_length_is_validated()
    {
        Assert.Throws<ArgumentException>(() => LineBreaker.GetOpportunities("abc", new LineBreakOpportunity[3]));
    }
}

public class GraphemeAndShapingTests
{
    [Fact]
    public void Devanagari_conjunct_is_one_cluster()
    {
        // \u0915\u094D\u0937 = KA + VIRAMA + SSA: GB9c keeps it together.
        string s = "\u0915\u094D\u0937";
        bool[] b = GraphemeClusters.GetBoundaries(s);
        Assert.True(b[0]);
        Assert.False(b[1]);
        Assert.False(b[2]);
        Assert.True(b[3]);
        Assert.Equal(3, GraphemeClusters.NextBoundary(s, 0));
    }

    [Fact]
    public void Emoji_zwj_sequence_and_flags_are_clusters()
    {
        string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        Assert.Equal(family.Length, GraphemeClusters.NextBoundary(family, 0));
        string flags = "\U0001F1EE\U0001F1F3\U0001F1FA\U0001F1F8"; // IN US
        Assert.Equal(4, GraphemeClusters.NextBoundary(flags, 0));
        Assert.Equal(8, GraphemeClusters.NextBoundary(flags, 4));
    }

    [Fact]
    public void Simple_shaper_maps_advances_and_kerns_when_asked()
    {
        var face = TestFonts.Face("LiberationSans-Regular.ttf");
        IReadOnlyList<ShapedGlyph> plain = SimpleShaper.Instance.Shape(face, "AV", 0, 2, ShapingOptions.Default);
        IReadOnlyList<ShapedGlyph> kerned = SimpleShaper.Instance.Shape(face, "AV", 0, 2, new ShapingOptions { Kerning = true });
        Assert.Equal(2, plain.Count);
        Assert.Equal(face.GetAdvance(face.GetGlyphId('A')), plain[0].XAdvance);
        Assert.Equal(plain[0].XAdvance - 152, kerned[0].XAdvance);
        Assert.Equal(new[] { 0, 1 }, kerned.Select(g => g.Cluster));
    }

    [Fact]
    public void Simple_shaper_skips_ignorables_and_keeps_cluster_indices()
    {
        var face = TestFonts.Face("Carlito-Regular.ttf");
        string s = "x\u00ADy\U0001F600";
        IReadOnlyList<ShapedGlyph> g = SimpleShaper.Instance.Shape(face, s, 0, s.Length, ShapingOptions.Default);
        Assert.Equal(new[] { 0, 2, 3 }, g.Select(x => x.Cluster));
        Assert.Equal(0, g[2].GlyphId); // Carlito has no emoji: .notdef
    }

    [Fact]
    public void Simple_shaper_declines_complex_scripts()
    {
        var face = TestFonts.Face("LiPi-Sans-Tamil-Regular.ttf");
        Assert.False(SimpleShaper.Instance.Supports(face, UnicodeScript.Tamil));
        Assert.True(SimpleShaper.Instance.Supports(face, UnicodeScript.Latin));
    }

    [Fact]
    public void Script_itemizer_attaches_common_characters_to_neighbours()
    {
        string s = "(abc) \u0BA4\u0BAE\u0BBF\u0BB4\u0BCD 123";
        List<ScriptRun> runs = ScriptItemizer.Itemize(s, 0, s.Length);
        Assert.Equal(2, runs.Count);
        Assert.Equal(new ScriptRun(0, 6, UnicodeScript.Latin), runs[0]);
        Assert.Equal(UnicodeScript.Tamil, runs[1].Script);
        Assert.Equal(s.Length, runs[1].Start + runs[1].Length);
        Assert.Equal(new ScriptRun(0, 3, UnicodeScript.Common), Assert.Single(ScriptItemizer.Itemize("123", 0, 3)));
    }
}
