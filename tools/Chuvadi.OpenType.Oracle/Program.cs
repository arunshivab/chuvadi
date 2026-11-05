// Usage: dotnet run -c Release --project tools/Chuvadi.OpenType.Oracle -- <pack folder> [--out <file>]
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
        foreach (string path in Directory.EnumerateFiles(Path.Combine(args[0], "fonts")).Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            byte[] raw = File.ReadAllBytes(path);
            bool woff2 = name.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase);
            // Woff2Unpacker is obsolete in Chuvadi.Pdf 3.31.0 (removed at the Pdf move); this test tool keeps the
            // cross-check of our WOFF2 decoder while the class exists (D-179, D-163). Suppressed on this line only.
#pragma warning disable CS0618
            byte[] sfnt = woff2 ? Woff2Unpacker.Unpack(raw) : raw;
#pragma warning restore CS0618
            FontFace face = FontFace.Load(sfnt);
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
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{name,-30} glyphs {glyphs,6}  code points {cmapChecked,8}  differences {diffs.Count,6}"));
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

    private static void Compare(List<string> diffs, string what, long expected, long actual)
    {
        if (expected != actual)
        {
            diffs.Add($"{what}: FontFace {expected}, ours {actual}");
        }
    }
}
