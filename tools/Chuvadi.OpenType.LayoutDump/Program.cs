using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.OpenType;
using Chuvadi.OpenType.Layout;

// Canonical dump of GDEF, GSUB and GPOS as read by Chuvadi.OpenType. Usage: LayoutDump <font> <out.txt>
// The same lines are written by fonttools_dump.py; the two sorted files are compared with diff.
internal static class Program
{
    private static readonly List<string> Lines = new List<string>();

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: LayoutDump <font> <out.txt>");
            return 2;
        }

        OpenTypeFont font = OpenTypeFont.Load(File.ReadAllBytes(args[0]));
        int n = font.GlyphCount;
        if (font.Gdef is GdefTable gdef)
        {
            for (int g = 0; g < n; g++)
            {
                if (gdef.GetGlyphClass(g) != GlyphClass.Unclassified)
                {
                    Add($"GDEF class {g} {(int)gdef.GetGlyphClass(g)}");
                }

                if (gdef.GetMarkAttachmentClass(g) != 0)
                {
                    Add($"GDEF markattach {g} {gdef.GetMarkAttachmentClass(g)}");
                }

                for (int set = 0; set < gdef.MarkGlyphSetCount; set++)
                {
                    if (gdef.IsInMarkGlyphSet(set, g))
                    {
                        Add($"GDEF markset {set} {g}");
                    }
                }
            }
        }

        DumpLayout("GSUB", font.Gsub, n);
        DumpLayout("GPOS", font.Gpos, n);
        Lines.Sort(StringComparer.Ordinal);
        File.WriteAllText(args[1], string.Concat(Lines.Select(l => l + "\n")), new UTF8Encoding(false)); // LF on every OS, as fonttools_dump.py
        Console.WriteLine($"{Path.GetFileName(args[0])}: {Lines.Count} lines");
        return 0;
    }

    private static void Add(string line) => Lines.Add(line);

    private static string Join(IEnumerable<int> values) => string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));

    private static string List(UInt16List l) => Join(Enumerable.Range(0, l.Count).Select(i => l[i]));

    private static void DumpLayout(string name, LayoutTable? table, int n)
    {
        if (table is null)
        {
            return;
        }

        for (int i = 0; i < table.Scripts.Count; i++)
        {
            Script script = table.Scripts.GetScript(i);
            if (script.DefaultLanguageSystem.Exists)
            {
                LangSys(name, script.Tag, "dflt", script.DefaultLanguageSystem);
            }

            for (int j = 0; j < script.LanguageSystemCount; j++)
            {
                LangSys(name, script.Tag, script.GetLanguageTag(j).ToString().TrimEnd(), script.GetLanguageSystem(j));
            }
        }

        for (int i = 0; i < table.Features.Count; i++)
        {
            Feature f = table.Features.GetFeature(i);
            Add($"{name} feature {i} {f.Tag.ToString().TrimEnd()} lookups {List(f.LookupIndices)}");
        }

        for (int i = 0; i < table.Lookups.Count; i++)
        {
            Lookup lookup = table.Lookups.GetLookup(i);
            int mfs = lookup.MarkFilteringSet;
            Add($"{name} lookup {i} type {lookup.LookupType} flag {(int)lookup.Flags} mfs {mfs} sub {lookup.SubtableCount}");
            for (int j = 0; j < lookup.SubtableCount; j++)
            {
                LookupSubtable st = lookup.GetSubtable(j);
                string p = $"{name} L{i}.{j} t{st.LookupType}";
                if (name == "GSUB")
                {
                    DumpSubstitution(p, st, n);
                }
                else
                {
                    DumpPositioning(p, st, n);
                }
            }
        }
    }

    private static void LangSys(string name, Tag script, string lang, LanguageSystem ls) =>
        Add($"{name} script {script.ToString().TrimEnd()} lang {lang} req {ls.RequiredFeatureIndex} feat {List(ls.FeatureIndices)}");

    private static void DumpSubstitution(string p, LookupSubtable st, int n)
    {
        switch (st.LookupType)
        {
            case 1:
                SingleSubstitution s1 = st.AsSingleSubstitution();
                foreach ((int g, _) in s1.Coverage.GetGlyphs())
                {
                    if (s1.TryGetSubstitute(g, out int s))
                    {
                        Add($"{p} single {g} {s}");
                    }
                }

                break;
            case 2:
                MultipleSubstitution s2 = st.AsMultipleSubstitution();
                foreach ((int g, _) in s2.Coverage.GetGlyphs())
                {
                    if (s2.TryGetSequence(g, out UInt16List seq))
                    {
                        Add($"{p} multiple {g} {List(seq)}");
                    }
                }

                break;
            case 3:
                AlternateSubstitution s3 = st.AsAlternateSubstitution();
                foreach ((int g, _) in s3.Coverage.GetGlyphs())
                {
                    if (s3.TryGetAlternates(g, out UInt16List alt))
                    {
                        Add($"{p} alternate {g} {List(alt)}");
                    }
                }

                break;
            case 4:
                LigatureSubstitution s4 = st.AsLigatureSubstitution();
                foreach ((int g, _) in s4.Coverage.GetGlyphs())
                {
                    for (int k = 0; k < s4.GetLigatureCount(g); k++)
                    {
                        Ligature lig = s4.GetLigature(g, k);
                        Add($"{p} ligature {g} {k} {List(lig.FollowingComponents)} {lig.LigatureGlyph}");
                    }
                }

                break;
            case 5:
                DumpContext(p, st.AsContext(), n);
                break;
            case 6:
                DumpContext(p, st.AsChainedContext(), n);
                break;
            case 8:
                ReverseChainSingleSubstitution r = st.AsReverseChainSingleSubstitution();
                foreach ((int g, int ci) in r.Coverage.GetGlyphs())
                {
                    Add($"{p} rev cov {g} sub {r.Substitutes[ci]}");
                }

                for (int i = 0; i < r.BacktrackCount; i++)
                {
                    Cov($"{p} rev bt {i}", r.GetBacktrack(i));
                }

                for (int i = 0; i < r.LookaheadCount; i++)
                {
                    Cov($"{p} rev la {i}", r.GetLookahead(i));
                }

                break;
        }
    }

    private static void DumpPositioning(string p, LookupSubtable st, int n)
    {
        switch (st.LookupType)
        {
            case 1:
                SinglePositioning p1 = st.AsSinglePositioning();
                foreach ((int g, _) in p1.Coverage.GetGlyphs())
                {
                    if (p1.TryGetValue(g, out ValueRecord v))
                    {
                        Add($"{p} single {g} {V(v)}");
                    }
                }

                break;
            case 2:
                PairPositioning p2 = st.AsPairPositioning();
                if (p2.Format == 1)
                {
                    DumpPairFormat1(p, p2, n);
                }
                else if (p2.Format == 2)
                {
                    Cov($"{p} pcov", p2.Coverage);
                    for (int g = 0; g < n; g++)
                    {
                        int c1 = p2.ClassDefinition1.GetClass(g);
                        int c2 = p2.ClassDefinition2.GetClass(g);
                        if (c1 != 0)
                        {
                            Add($"{p} pc1 {g} {c1}");
                        }

                        if (c2 != 0)
                        {
                            Add($"{p} pc2 {g} {c2}");
                        }
                    }

                    // Cells are read through one representative glyph per class: first classes from the covered
                    // glyphs, second classes from all glyph ids of the font.
                    Dictionary<int, int> rep1 = new Dictionary<int, int>();
                    foreach ((int g, _) in p2.Coverage.GetGlyphs())
                    {
                        rep1.TryAdd(p2.ClassDefinition1.GetClass(g), g);
                    }

                    Dictionary<int, int> rep2 = new Dictionary<int, int>();
                    for (int g = 0; g < n; g++)
                    {
                        rep2.TryAdd(p2.ClassDefinition2.GetClass(g), g);
                    }

                    foreach ((int c1, int g1) in rep1)
                    {
                        foreach ((int c2, int g2) in rep2)
                        {
                            if (p2.TryGetPair(g1, g2, out ValueRecord a, out ValueRecord b) && (V(a) != "0,0,0,0" || V(b) != "0,0,0,0"))
                            {
                                Add($"{p} pcell {c1} {c2} {V(a)} {V(b)}");
                            }
                        }
                    }
                }

                break;
            case 3:
                CursivePositioning p3 = st.AsCursivePositioning();
                foreach ((int g, _) in p3.Coverage.GetGlyphs())
                {
                    if (p3.TryGetAnchors(g, out Anchor en, out Anchor ex))
                    {
                        Add($"{p} cursive {g} entry {A(en)} exit {A(ex)}");
                    }
                }

                break;
            case 4:
            case 5:
            case 6:
                MarkAttachment m = st.AsMarkAttachment();
                foreach ((int g, int ci) in m.MarkCoverage.GetGlyphs())
                {
                    if (m.TryGetMark(ci, out int cls, out Anchor an))
                    {
                        Add($"{p} mark {g} {cls} {A(an)}");
                    }
                }

                foreach ((int g, int ci) in m.TargetCoverage.GetGlyphs())
                {
                    for (int c = 0; c < m.MarkClassCount; c++)
                    {
                        if (st.LookupType == 5)
                        {
                            for (int k = 0; k < m.GetComponentCount(ci); k++)
                            {
                                Add($"{p} lig {g} comp {k} class {c} {A(m.GetLigatureAnchor(ci, k, c))}");
                            }
                        }
                        else
                        {
                            Add($"{p} base {g} class {c} {A(m.GetTargetAnchor(ci, c))}");
                        }
                    }
                }

                break;
            case 7:
                DumpContext(p, st.AsContext(), n);
                break;
            case 8:
                DumpContext(p, st.AsChainedContext(), n);
                break;
        }
    }

    private static void DumpPairFormat1(string p, PairPositioning p2, int n)
    {
        // Every first glyph against every glyph id of the font as the second glyph (the view answers by binary search).
        foreach ((int g1, _) in p2.Coverage.GetGlyphs())
        {
            for (int g2 = 0; g2 < n; g2++)
            {
                if (p2.TryGetPair(g1, g2, out ValueRecord a, out ValueRecord b))
                {
                    Add($"{p} pair {g1} {g2} {V(a)} {V(b)}");
                }
            }
        }
    }

    private static void DumpContext(string p, ContextualSubtable c, int n)
    {
        if (c.Format == 3)
        {
            ContextRule r = c.GetFormat3Rule();
            for (int i = 0; i < r.BacktrackCount; i++)
            {
                Cov($"{p} ctx3 bt {i}", r.GetBacktrackCoverage(i));
            }

            for (int i = 0; i < r.InputCount; i++)
            {
                Cov($"{p} ctx3 in {i}", r.GetInputCoverage(i));
            }

            for (int i = 0; i < r.LookaheadCount; i++)
            {
                Cov($"{p} ctx3 la {i}", r.GetLookaheadCoverage(i));
            }

            Add($"{p} ctx3 rec {Records(r)}");
            return;
        }

        Cov($"{p} cov", c.Coverage);
        if (c.Format == 2)
        {
            for (int g = 0; g < n; g++)
            {
                int ci = c.InputClassDefinition.GetClass(g);
                if (ci != 0)
                {
                    Add($"{p} cdin {g} {ci}");
                }

                int cb = c.BacktrackClassDefinition.GetClass(g);
                if (cb != 0)
                {
                    Add($"{p} cdbt {g} {cb}");
                }

                int cl = c.LookaheadClassDefinition.GetClass(g);
                if (cl != 0)
                {
                    Add($"{p} cdla {g} {cl}");
                }
            }
        }

        for (int set = 0; set < c.RuleSetCount; set++)
        {
            string key = c.Format == 1 ? $"glyph {CoverageGlyph(c.Coverage, set)}" : $"class {set}";
            for (int k = 0; k < c.GetRuleCount(set); k++)
            {
                ContextRule r = c.GetRule(set, k);
                string bt = Join(Enumerable.Range(0, r.BacktrackCount).Select(r.GetBacktrack));
                string input = Join(Enumerable.Range(1, Math.Max(0, r.InputCount - 1)).Select(r.GetInput));
                string la = Join(Enumerable.Range(0, r.LookaheadCount).Select(r.GetLookahead));
                Add($"{p} ctx{c.Format} {key} {k} bt {bt} in {input} la {la} rec {Records(r)}");
            }
        }
    }

    private static int CoverageGlyph(Coverage coverage, int index)
    {
        foreach ((int g, int ci) in coverage.GetGlyphs())
        {
            if (ci == index)
            {
                return g;
            }
        }

        return -1;
    }

    private static string Records(ContextRule r) =>
        string.Join(",", Enumerable.Range(0, r.LookupRecordCount).Select(i => { (int si, int li) = r.GetLookupRecord(i); return $"{si}:{li}"; }));

    private static void Cov(string prefix, Coverage coverage)
    {
        foreach ((int g, _) in coverage.GetGlyphs())
        {
            Add($"{prefix} {g}");
        }
    }

    private static string V(ValueRecord v) => $"{v.XPlacement},{v.YPlacement},{v.XAdvance},{v.YAdvance}";

    private static string A(Anchor a) => !a.Exists ? "-" : a.Format == 2 ? $"{a.X},{a.Y},p{a.AnchorPoint}" : $"{a.X},{a.Y}";
}
