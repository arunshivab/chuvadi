// Usage: dotnet run -c Release --project tools/Chuvadi.OpenType.Oracle -- <pack folder> [--out <file>]
// Checks the fonts in <pack>/fonts and, when present, <pack>/oldtags/fonts and <pack>/uvs/fonts.
// Exit code 0 only when every comparison agrees.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.OpenType;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Fonts.Woff2;

namespace Chuvadi.OpenType.Oracle;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1 || !Directory.Exists(Path.Combine(args[0], "fonts")))
        {
            Console.Error.WriteLine("usage: Chuvadi.OpenType.Oracle <pack folder> [--out <file>]");
            return 2;
        }

        string? outFile = args.Length >= 3 && args[1] == "--out" ? args[2] : null;
        StringBuilder report = new StringBuilder();
        report.AppendLine("Chuvadi.OpenType oracle check against Chuvadi.Pdf.Fonts.Rendering FontFace (public API)");
        report.AppendLine();
        int totalDifferences = 0;
        string[] folders = { "fonts", Path.Combine("oldtags", "fonts"), Path.Combine("uvs", "fonts") };
        IEnumerable<string> paths = folders
            .Select(f => Path.Combine(args[0], f))
            .Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f).Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal));
        foreach (string path in paths)
        {
            string name = Path.GetRelativePath(args[0], path).Replace('\\', '/');
            byte[] raw = File.ReadAllBytes(path);
            bool woff2 = name.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase);
            // Woff2Unpacker is obsolete since Chuvadi.Pdf 3.31.0 (removed at the Pdf move); this test tool keeps the
            // cross-check of our WOFF2 decoder while the class exists (D-179, D-163). Suppressed on this line only.
#pragma warning disable CS0618
            byte[] sfnt = woff2 ? Woff2Unpacker.Unpack(raw) : raw;
#pragma warning restore CS0618
            FontFace face = FontFace.Load(sfnt, 0);
            OpenTypeFont ours = OpenTypeFont.Load(sfnt);
            List<string> diffs = new List<string>();

            Compare(diffs, "UnitsPerEm", Convert.ToInt64(face.UnitsPerEm, CultureInfo.InvariantCulture), ours.UnitsPerEm);
            Compare(diffs, "GlyphCount", Convert.ToInt64(face.GlyphCount, CultureInfo.InvariantCulture), ours.GlyphCount);
            Compare(diffs, "IsCff", face.IsCff ? 1 : 0, ours.OutlineFormat == OutlineFormat.Cff ? 1 : 0);

            int glyphs = ours.GlyphCount;
            for (int g = 0; g < glyphs; g++)
            {
                Compare(diffs, "advance g" + g, Convert.ToInt64(face.GetAdvanceWidth(g), CultureInfo.InvariantCulture), ours.GetAdvanceWidth(g));
                Compare(diffs, "lsb g" + g, Convert.ToInt64(face.GetLeftSideBearing(g), CultureInfo.InvariantCulture), ours.GetLeftSideBearing(g));

                // FontFace.GetGlyphBounds returns FontBox (XMin, YMin, XMax, YMax in design units; all zero for an
                // empty glyph, per its XML docs). Ours: TryGetGlyphBounds, false for a glyph without outline.
                FontBox fb = face.GetGlyphBounds(g);
                GlyphBounds ob = ours.TryGetGlyphBounds(g, out GlyphBounds b) ? b : default;
                if (fb.XMin != ob.XMin || fb.YMin != ob.YMin || fb.XMax != ob.XMax || fb.YMax != ob.YMax)
                {
                    diffs.Add(string.Create(CultureInfo.InvariantCulture, $"bounds g{g}: FontFace ({fb.XMin}, {fb.YMin}, {fb.XMax}, {fb.YMax}), ours ({ob.XMin}, {ob.YMin}, {ob.XMax}, {ob.YMax})"));
                }
            }

            int cmapChecked = 0;
            for (int cp = 0; cp <= 0x10FFFF; cp++)
            {
                if (cp >= 0xD800 && cp <= 0xDFFF)
                {
                    continue;
                }

                Compare(diffs, "cmap U+" + cp.ToString("X4", CultureInfo.InvariantCulture), Convert.ToInt64(face.GetGlyphIndex(cp), CultureInfo.InvariantCulture), ours.CharacterMap.GetGlyph(cp));
                cmapChecked++;
            }

            // CharacterMap: Authoring only enumerates FontFace.CharacterMap (IReadOnlyDictionary<int, int>), so compare
            // that pair set with our EnumerateMappings().
            Dictionary<int, int> ourPairs = ours.CharacterMap.EnumerateMappings().ToDictionary(kv => kv.Key, kv => kv.Value);
            int pairDiffs = 0;
            foreach (KeyValuePair<int, int> kv in face.CharacterMap)
            {
                if (!ourPairs.TryGetValue(kv.Key, out int g) || g != kv.Value)
                {
                    if (pairDiffs++ < 10)
                    {
                        diffs.Add($"CharacterMap U+{kv.Key:X4}: FontFace {kv.Value}, ours {(ourPairs.TryGetValue(kv.Key, out int o) ? o.ToString(CultureInfo.InvariantCulture) : "absent")}");
                    }
                }
            }

            int extra = ourPairs.Keys.Count(k => !face.CharacterMap.ContainsKey(k));
            if (extra > 0)
            {
                diffs.Add($"CharacterMap: {extra} pairs in ours that FontFace.CharacterMap does not list");
            }

            CompareMetrics(diffs, face, ours.Metrics);
            int sequences = CompareVariationSequences(diffs, face, ours.CharacterMap);

            // Chuvadi.OpenType's own WOFF2 decoding against the published unpacker: same tables, same metrics and bounds.
            if (woff2)
            {
                OpenTypeFont mine = OpenTypeFont.Load(raw);
                foreach (TableRecord t in ours.Tables)
                {
                    string tag = t.Tag.ToString();
                    if (tag is "glyf" or "loca" or "hmtx" or "head")
                    {
                        continue;
                    }

                    if (!mine.GetTableData(t.Tag).Span.SequenceEqual(ours.GetTableData(t.Tag).Span))
                    {
                        diffs.Add($"woff2 table {tag}: bytes differ");
                    }
                }

                for (int g = 0; g < glyphs; g++)
                {
                    Compare(diffs, "woff2 advance g" + g, ours.GetAdvanceWidth(g), mine.GetAdvanceWidth(g));
                    Compare(diffs, "woff2 lsb g" + g, ours.GetLeftSideBearing(g), mine.GetLeftSideBearing(g));
                    bool a = ours.TryGetGlyphBounds(g, out GlyphBounds ba);
                    bool b = mine.TryGetGlyphBounds(g, out GlyphBounds bb);
                    if (a != b || ba != bb)
                    {
                        diffs.Add($"woff2 bounds g{g}: unpacker {(a ? ba.ToString() : "none")}, ours {(b ? bb.ToString() : "none")}");
                    }
                }
            }

            totalDifferences += diffs.Count;
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{name,-48} glyphs {glyphs,6}  code points {cmapChecked,8}  sequences {sequences,5}  differences {diffs.Count,6}"));
            foreach (string d in diffs.Take(20))
            {
                report.AppendLine("    " + d);
            }
        }

        string probes = Path.Combine(args[0], "uvs", "variants-3.31.1.tsv");
        if (File.Exists(probes))
        {
            List<string> diffs = CheckRecordedProbes(args[0], probes, out int lines);
            totalDifferences += diffs.Count;
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{"uvs/variants-3.31.1.tsv",-48} probes {lines,6}  differences {diffs.Count,6}"));
            foreach (string d in diffs.Take(20))
            {
                report.AppendLine("    " + d);
            }
        }

        report.AppendLine();
        report.AppendLine(totalDifferences == 0 ? "ORACLE AGREES" : "ORACLE DIFFERS");
        Console.Write(report.ToString());
        if (outFile is not null)
        {
            File.WriteAllText(outFile, report.ToString(), new UTF8Encoding(false));
        }

        return totalDifferences == 0 ? 0 : 1;
    }

    // FaceMetrics against the FontFace members of the same name (D-201, D-247).
    private static void CompareMetrics(List<string> diffs, FontFace face, FaceMetrics m)
    {
        CompareText(diffs, "FamilyName", face.FamilyName, m.FamilyName);
        CompareText(diffs, "SubfamilyName", face.SubfamilyName, m.SubfamilyName);
        CompareText(diffs, "FullName", face.FullName, m.FullName);
        CompareText(diffs, "PostScriptName", face.PostScriptName, m.PostScriptName);
        Compare(diffs, "WeightClass", face.WeightClass, m.WeightClass);
        Compare(diffs, "IsBold", face.IsBold ? 1 : 0, m.IsBold ? 1 : 0);
        Compare(diffs, "IsItalic", face.IsItalic ? 1 : 0, m.IsItalic ? 1 : 0);
        Compare(diffs, "IsFixedPitch", face.IsFixedPitch ? 1 : 0, m.IsFixedPitch ? 1 : 0);
        FontBox fb = face.FontBounds;
        Compare(diffs, "FontBounds.XMin", Convert.ToInt64(fb.XMin, CultureInfo.InvariantCulture), m.FontBounds.XMin);
        Compare(diffs, "FontBounds.YMin", Convert.ToInt64(fb.YMin, CultureInfo.InvariantCulture), m.FontBounds.YMin);
        Compare(diffs, "FontBounds.XMax", Convert.ToInt64(fb.XMax, CultureInfo.InvariantCulture), m.FontBounds.XMax);
        Compare(diffs, "FontBounds.YMax", Convert.ToInt64(fb.YMax, CultureInfo.InvariantCulture), m.FontBounds.YMax);
        Compare(diffs, "HheaAscender", face.HheaAscender, m.HheaAscender);
        Compare(diffs, "HheaDescender", face.HheaDescender, m.HheaDescender);
        Compare(diffs, "HheaLineGap", face.HheaLineGap, m.HheaLineGap);
        Compare(diffs, "HasOs2Table", face.HasOs2Table ? 1 : 0, m.HasOs2Table ? 1 : 0);
        Compare(diffs, "TypoAscender", face.TypoAscender, m.TypoAscender);
        Compare(diffs, "TypoDescender", face.TypoDescender, m.TypoDescender);
        Compare(diffs, "TypoLineGap", face.TypoLineGap, m.TypoLineGap);
        Compare(diffs, "WinAscent", face.WinAscent, m.WinAscent);
        Compare(diffs, "WinDescent", face.WinDescent, m.WinDescent);
        Compare(diffs, "UseTypoMetrics", face.UseTypoMetrics ? 1 : 0, m.UseTypoMetrics ? 1 : 0);
        Compare(diffs, "Ascender", face.Ascender, m.Ascender);
        Compare(diffs, "Descender", face.Descender, m.Descender);
        Compare(diffs, "LineGap", face.LineGap, m.LineGap);
        Compare(diffs, "CapHeight", face.CapHeight, m.CapHeight);
        Compare(diffs, "XHeight", face.XHeight, m.XHeight);
        Compare(diffs, "UnderlinePosition", face.UnderlinePosition, m.UnderlinePosition);
        Compare(diffs, "UnderlineThickness", face.UnderlineThickness, m.UnderlineThickness);
        Compare(diffs, "StrikeoutPosition", face.StrikeoutPosition, m.StrikeoutPosition);
        Compare(diffs, "StrikeoutSize", face.StrikeoutSize, m.StrikeoutSize);
        if (face.ItalicAngle != m.ItalicAngle)
        {
            diffs.Add(string.Create(CultureInfo.InvariantCulture, $"ItalicAngle: FontFace {face.ItalicAngle}, ours {m.ItalicAngle}"));
        }
    }

    // Format 14: every sequence the subtable lists, and the code points just before and after each, against
    // FontFace.GetVariantGlyphIndex (-1 when not listed). Ours: GetVariationKind and GetGlyph(cp, vs).
    private static int CompareVariationSequences(List<string> diffs, FontFace face, CharacterMap cmap)
    {
        HashSet<(int, int)> probes = new HashSet<(int, int)>();
        foreach ((int cp, int vs, VariationSequenceKind _, int _) in cmap.EnumerateVariationSequences())
        {
            probes.Add((cp, vs));
            if (cp > 0)
            {
                probes.Add((cp - 1, vs));
            }

            if (cp < 0x10FFFF)
            {
                probes.Add((cp + 1, vs));
            }
        }

        foreach ((int cp, int vs) in probes.OrderBy(p => p.Item2).ThenBy(p => p.Item1))
        {
            Compare(diffs, string.Create(CultureInfo.InvariantCulture, $"variant U+{cp:X4} U+{vs:X4}"), face.GetVariantGlyphIndex(cp, vs), Ours(cmap, cp, vs));
        }

        return probes.Count;
    }

    private static int Ours(CharacterMap cmap, int cp, int vs) =>
        cmap.GetVariationKind(cp, vs) == VariationSequenceKind.NotListed ? -1 : cmap.GetGlyph(cp, vs);

    // The probes recorded from 3.31.1 in the uvs fixture (D-206): ours against the recorded value and the live FontFace.
    private static List<string> CheckRecordedProbes(string pack, string file, out int lines)
    {
        List<string> diffs = new List<string>();
        byte[] font = File.ReadAllBytes(Path.Combine(pack, "uvs", "fonts", "LiPi-Sans-Latin-UVS.ttf"));
        FontFace face = FontFace.Load(font, 0);
        CharacterMap cmap = OpenTypeFont.Load(font).CharacterMap;
        lines = 0;
        foreach (string line in File.ReadLines(file))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            int cp = int.Parse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int vs = int.Parse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int recorded = int.Parse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
            string what = string.Create(CultureInfo.InvariantCulture, $"probe U+{cp:X4} U+{vs:X4}");
            Compare(diffs, what + " (recorded 3.31.1)", recorded, Ours(cmap, cp, vs));
            Compare(diffs, what + " (live)", face.GetVariantGlyphIndex(cp, vs), Ours(cmap, cp, vs));
            Compare(diffs, what + " base", int.Parse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture), cmap.GetGlyph(cp));
            lines++;
        }

        return diffs;
    }

    private static void CompareText(List<string> diffs, string what, string? expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            diffs.Add($"{what}: FontFace \"{expected}\", ours \"{actual}\"");
        }
    }

    private static void Compare(List<string> diffs, string what, long expected, long actual)
    {
        if (expected != actual)
        {
            diffs.Add($"{what}: FontFace {expected}, ours {actual}");
        }
    }
}
