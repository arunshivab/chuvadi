// Local shaping gate. Shapes every line of the clean-room pack corpus with
// Chuvadi.Text.Shaping and compares the result with the expected glyphs, field
// by field, in font units. Exit code 0 only when every line matches (and, with
// --parity, every byte-array shaping equals the cached-font shaping).
//
// Usage:
//   dotnet run -c Release --project tools/Chuvadi.Text.Shaping.Gate -- --pack <pack folder>
//       [--set syllables|words] [--script <LipiScript name>] [--diffs <n>] [--out <file>]
//       [--features <file>] [--parity] [--bench <rounds>] [--fontcheck] [--expect-lines <n>]
//
// --expect-lines n checks a phase target instead of the final gate: it reports PHASE TARGET MET when
// exactly n lines match (with --parity and --fontcheck also clean). GATE PASSED needs every line.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.OpenType;

namespace Chuvadi.Text.Shaping.Gate;

internal static class Program
{
    private static readonly string[] Sets = { "syllables", "words" };

    private static readonly Dictionary<string, Script> Scripts = new Dictionary<string, Script>(StringComparer.Ordinal)
    {
        ["Latin"] = Script.Latin,
        ["Tamil"] = Script.Tamil,
        ["Devanagari"] = Script.Devanagari,
        ["Bengali"] = Script.Bengali,
        ["Gurmukhi"] = Script.Gurmukhi,
        ["Gujarati"] = Script.Gujarati,
        ["Odia"] = Script.Odia,
        ["Telugu"] = Script.Telugu,
        ["Kannada"] = Script.Kannada,
        ["Malayalam"] = Script.Malayalam,
    };

    private static int Main(string[] args)
    {
        Options o;
        try
        {
            o = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        IReadOnlyList<Feature> features = ReadFeatures(o.FeaturesFile);
        Dictionary<string, (byte[] Sfnt, OpenTypeFont Font)> fonts = new Dictionary<string, (byte[], OpenTypeFont)>(StringComparer.Ordinal);
        List<Line> lines = LoadCorpus(o);
        foreach (Line l in lines)
        {
            if (!fonts.ContainsKey(l.Font))
            {
                // WOFF2 files are decoded by Chuvadi.OpenType itself; the decoded sfnt is kept for the byte-array checks.
                OpenTypeFont font = OpenTypeFont.Load(File.ReadAllBytes(Path.Combine(o.Pack, "fonts", l.Font)));
                fonts[l.Font] = (font.Data.ToArray(), font);
            }
        }

        Dictionary<string, ShapeOptions> optionsByScript = Scripts.ToDictionary(
            kv => kv.Key,
            kv => new ShapeOptions { Script = kv.Value, Language = Language.Default, UseDefaultFeatures = false, Features = features }, // language always explicit (D-182)
            StringComparer.Ordinal);

        GlyphBuffer buffer = new GlyphBuffer();
        GlyphBuffer parityBuffer = new GlyphBuffer();
        List<(Line Line, bool Match)> results = new List<(Line, bool)>(lines.Count);
        Dictionary<string, int> fieldCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        List<string> diffs = new List<string>();
        int parityFailures = 0;
        foreach (Line l in lines)
        {
            ShapingFont font = ShapingFont.Get(fonts[l.Font].Font);
            ShapeOptions options = optionsByScript[l.Script];
            string? error = null;
            Glyph[] actual;
            try
            {
                TextShaper.Shape(font, l.Text, options, buffer);
                actual = Read(buffer);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                actual = Array.Empty<Glyph>();
                error = ex.GetType().Name + ": " + ex.Message;
            }

            if (o.Parity && error is null)
            {
                // Byte-array path: a freshly loaded font from the same bytes must give identical output.
                OpenTypeFont fresh = OpenTypeFont.Load(fonts[l.Font].Sfnt.ToArray());
                TextShaper.Shape(ShapingFont.Get(fresh), l.Text, options, parityBuffer);
                if (!Read(parityBuffer).SequenceEqual(actual))
                {
                    parityFailures++;
                }
            }

            string? field = error is not null ? "exception" : FirstDifference(l.Expected, actual);
            results.Add((l, field is null));
            if (field is not null)
            {
                fieldCounts[field] = fieldCounts.TryGetValue(field, out int n) ? n + 1 : 1;
                if (diffs.Count < o.MaxDiffs)
                {
                    diffs.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{l.Set}:{l.LineNumber} {l.Script} {l.Font} [{l.Hex}] first difference: {field}\n  expected {Format(l.Expected)}\n  actual   {error ?? Format(actual)}"));
                }
            }
        }

        StringBuilder report = new StringBuilder();
        Report(report, results, fieldCounts, diffs);
        bool pass = results.Count > 0 && results.All(r => r.Match);
        if (o.Parity)
        {
            report.AppendLine();
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Byte-array parity: {results.Count - parityFailures} of {results.Count} lines identical."));
            pass &= parityFailures == 0;
        }

        bool fontCheckOk = true;
        if (o.FontCheck)
        {
            report.AppendLine();
            fontCheckOk = FontCheck(report, fonts);
            pass &= fontCheckOk;
        }

        if (o.BenchRounds > 0)
        {
            report.AppendLine();
            Bench(report, lines, fonts, optionsByScript, o.BenchRounds);
        }

        int matched = results.Count(r => r.Match);
        if (o.ExpectLines >= 0)
        {
            bool met = matched == o.ExpectLines && (!o.Parity || parityFailures == 0) && fontCheckOk;
            report.AppendLine();
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Phase target: exactly {o.ExpectLines} matching lines; got {matched}."));
            report.AppendLine(met ? "PHASE TARGET MET" : "PHASE TARGET NOT MET");
            pass = met;
        }
        else
        {
            report.AppendLine();
            report.AppendLine(pass ? "GATE PASSED" : "GATE FAILED");
        }
        Console.Write(report.ToString());
        if (o.Out is not null)
        {
            File.WriteAllText(o.Out, report.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
        }

        return pass ? 0 : 1;
    }

    // Reader sanity checks on the pack fonts that need no oracle: every glyph box lies inside the 'head' box,
    // and when 'head' flags bit 1 is set (left side bearing point at x = 0) every glyph's lsb equals its xMin.
    private static bool FontCheck(StringBuilder sb, Dictionary<string, (byte[] Sfnt, OpenTypeFont Font)> fonts)
    {
        bool ok = true;
        sb.AppendLine("Font reader check (glyph boxes inside 'head' box; lsb = xMin when head flags bit 1 is set):");
        foreach (KeyValuePair<string, (byte[] Sfnt, OpenTypeFont Font)> kv in fonts.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            OpenTypeFont f = kv.Value.Font;
            int outside = 0;
            int lsb = 0;
            int withBounds = 0;
            bool lsbRule = (f.Head.Flags & 2) != 0;
            for (int g = 0; g < f.GlyphCount; g++)
            {
                if (!f.TryGetGlyphBounds(g, out GlyphBounds b))
                {
                    continue;
                }

                withBounds++;
                if (b.XMin < f.Head.XMin || b.YMin < f.Head.YMin || b.XMax > f.Head.XMax || b.YMax > f.Head.YMax)
                {
                    outside++;
                }

                if (lsbRule && b.XMin != f.GetLeftSideBearing(g))
                {
                    lsb++;
                }
            }

            ok &= outside == 0 && lsb == 0;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {kv.Key,-30} glyphs {f.GlyphCount,6} with boxes {withBounds,6} outside head box {outside,5} lsb != xMin {lsb,5}"));
        }

        return ok;
    }

    private static void Bench(StringBuilder sb, List<Line> lines, Dictionary<string, (byte[] Sfnt, OpenTypeFont Font)> fonts, Dictionary<string, ShapeOptions> optionsByScript, int rounds)
    {
        GlyphBuffer buffer = new GlyphBuffer();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Speed ({rounds} rounds per script, microseconds per call, this machine):"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {"script",-11} {"lines",7} {"cached font",12} {"bytes each call",16}"));
        foreach (IGrouping<string, Line> g in lines.GroupBy(l => l.Script).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Line[] group = g.ToArray();
            ShapeOptions options = optionsByScript[g.Key];

            // Warm-up.
            foreach (Line l in group)
            {
                TextShaper.Shape(ShapingFont.Get(fonts[l.Font].Font), l.Text, options, buffer);
            }

            Stopwatch sw = Stopwatch.StartNew();
            for (int r = 0; r < rounds; r++)
            {
                foreach (Line l in group)
                {
                    TextShaper.Shape(ShapingFont.Get(fonts[l.Font].Font), l.Text, options, buffer);
                }
            }

            double cached = sw.Elapsed.TotalMilliseconds * 1000.0 / (rounds * (double)group.Length);

            sw.Restart();
            for (int r = 0; r < rounds; r++)
            {
                foreach (Line l in group)
                {
                    OpenTypeFont f = OpenTypeFont.Load(fonts[l.Font].Sfnt);
                    TextShaper.Shape(ShapingFont.Get(f), l.Text, options, buffer);
                }
            }

            double bytesEachCall = sw.Elapsed.TotalMilliseconds * 1000.0 / (rounds * (double)group.Length);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {g.Key,-11} {group.Length,7} {cached,12:0.00} {bytesEachCall,16:0.00}"));
        }
    }

    private static List<Line> LoadCorpus(Options o)
    {
        List<Line> lines = new List<Line>();
        foreach (string set in Sets)
        {
            if (o.Set is not null && o.Set != set)
            {
                continue;
            }

            int lineNumber = 0;
            foreach (string raw in File.ReadLines(Path.Combine(o.Pack, "corpus", set + ".tsv")))
            {
                lineNumber++;
                if (raw.Length == 0 || raw[0] == '#')
                {
                    continue;
                }

                string[] cols = raw.Split('\t');
                if (cols.Length != 4)
                {
                    throw new InvalidDataException($"{set}.tsv line {lineNumber}: expected 4 columns, found {cols.Length}.");
                }

                if (o.Script is not null && !string.Equals(o.Script, cols[0], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                lines.Add(new Line(set, lineNumber, cols[0], cols[1], cols[2], Decode(cols[2]), ParseGlyphs(cols[3])));
            }
        }

        return lines;
    }

    private static IReadOnlyList<Feature> ReadFeatures(string path)
    {
        List<Feature> list = new List<Feature>();
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] kv = line.Split('=');
            list.Add(new Feature(Tag.Parse(kv[0].Trim()), uint.Parse(kv[1].Trim(), CultureInfo.InvariantCulture)));
        }

        return list;
    }

    private static Glyph[] Read(GlyphBuffer b)
    {
        Glyph[] g = new Glyph[b.Count];
        for (int i = 0; i < b.Count; i++)
        {
            g[i] = new Glyph(b.GlyphIds[i], b.Clusters[i], b.XAdvances[i], b.XOffsets[i], b.YOffsets[i]);
        }

        return g;
    }

    private static string Decode(string hex)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string cp in hex.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(char.ConvertFromUtf32(int.Parse(cp, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    private static Glyph[] ParseGlyphs(string field) =>
        field.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(g =>
            {
                int[] v = g.Split(':').Select(s => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture)).ToArray();
                return new Glyph(v[0], v[1], v[2], v[3], v[4]);
            })
            .ToArray();

    private static string? FirstDifference(Glyph[] expected, Glyph[] actual)
    {
        int n = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < n; i++)
        {
            Glyph e = expected[i];
            Glyph a = actual[i];
            if (e.Id != a.Id)
            {
                return "glyph id";
            }

            if (e.Cluster != a.Cluster)
            {
                return "cluster";
            }

            if (e.XAdvance != a.XAdvance)
            {
                return "x advance";
            }

            if (e.XOffset != a.XOffset || e.YOffset != a.YOffset)
            {
                return "offset";
            }
        }

        return expected.Length == actual.Length ? null : "glyph count";
    }

    private static string Format(Glyph[] glyphs) =>
        string.Join(' ', glyphs.Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Id}:{g.Cluster}:{g.XAdvance}:{g.XOffset}:{g.YOffset}")));

    private static void Report(StringBuilder sb, List<(Line Line, bool Match)> results, Dictionary<string, int> fieldCounts, List<string> diffs)
    {
        sb.AppendLine("Chuvadi.Text.Shaping local gate");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{"set",-10} {"script",-11} {"lines",7} {"match",7} {"percent",8}"));
        foreach (IGrouping<(string Set, string Script), (Line Line, bool Match)> g in results.GroupBy(r => (r.Line.Set, r.Line.Script)).OrderBy(g => g.Key.Set, StringComparer.Ordinal).ThenBy(g => g.Key.Script, StringComparer.Ordinal))
        {
            Row(sb, g.Key.Set, g.Key.Script, g.Count(), g.Count(r => r.Match));
        }

        foreach (IGrouping<string, (Line Line, bool Match)> g in results.GroupBy(r => r.Line.Set).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Row(sb, g.Key, "all", g.Count(), g.Count(r => r.Match));
        }

        Row(sb, "all", "all", results.Count, results.Count(r => r.Match));
        sb.AppendLine();
        sb.AppendLine("By font:");
        foreach (IGrouping<string, (Line Line, bool Match)> g in results.GroupBy(r => r.Line.Font).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            int n = g.Count();
            int m = g.Count(r => r.Match);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {g.Key,-30} {n,7} {m,7} {(n == 0 ? 0 : 100.0 * m / n),7:0.00}%"));
        }

        if (fieldCounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Failing lines by first differing field:");
            foreach (KeyValuePair<string, int> kv in fieldCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {kv.Key,-12} {kv.Value,7}"));
            }

            sb.AppendLine();
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"First {diffs.Count} differences:"));
            foreach (string d in diffs)
            {
                sb.AppendLine(d);
            }
        }
    }

    private static void Row(StringBuilder sb, string set, string script, int lines, int match)
    {
        double percent = lines == 0 ? 0 : 100.0 * match / lines;
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{set,-10} {script,-11} {lines,7} {match,7} {percent,7:0.00}%"));
    }

    private readonly record struct Glyph(int Id, int Cluster, int XAdvance, int XOffset, int YOffset);

    private sealed record Line(string Set, int LineNumber, string Script, string Font, string Hex, string Text, Glyph[] Expected);

    private sealed class Options
    {
        internal string Pack { get; private set; } = Environment.GetEnvironmentVariable("CHUVADI_SHAPING_PACK") ?? string.Empty;

        internal string? Set { get; private set; }

        internal string? Script { get; private set; }

        internal int MaxDiffs { get; private set; } = 50;

        internal string? Out { get; private set; }

        internal string FeaturesFile { get; private set; } = Path.Combine(AppContext.BaseDirectory, "corpus-features.txt");

        internal bool Parity { get; private set; }

        internal int BenchRounds { get; private set; }

        internal bool FontCheck { get; private set; }

        internal int ExpectLines { get; private set; } = -1;

        internal static Options Parse(string[] args)
        {
            Options o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--parity":
                        o.Parity = true;
                        continue;
                    case "--fontcheck":
                        o.FontCheck = true;
                        continue;
                }

                string value = i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing value after " + args[i]);
                switch (args[i])
                {
                    case "--pack": o.Pack = value; break;
                    case "--set": o.Set = Array.IndexOf(Sets, value) >= 0 ? value : throw new ArgumentException("--set must be syllables or words"); break;
                    case "--script": o.Script = value; break;
                    case "--diffs": o.MaxDiffs = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--out": o.Out = value; break;
                    case "--features": o.FeaturesFile = value; break;
                    case "--bench": o.BenchRounds = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--expect-lines": o.ExpectLines = int.Parse(value, CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException("Unknown option " + args[i]);
                }

                i++;
            }

            if (o.Pack.Length == 0 || !Directory.Exists(Path.Combine(o.Pack, "corpus")) || !Directory.Exists(Path.Combine(o.Pack, "fonts")))
            {
                throw new ArgumentException("Give the clean-room pack folder with --pack (or set CHUVADI_SHAPING_PACK); it must contain corpus/ and fonts/.");
            }

            return o;
        }
    }
}
