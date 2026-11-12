using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.OpenType;
using Chuvadi.OpenType.Layout;
using Chuvadi.OpenType.Tables;
using Chuvadi.Text.Shaping.Unicode;

namespace Chuvadi.Text.Shaping.Engine;

/// <summary>
/// The engine for the default, non-complex path (phase 1, E1): Latin and every script without its own shaping model.
/// Pipeline: code points, initial clusters (E3), normalisation against the font (E2), cmap and variation selectors,
/// GDEF classes, automatic fractions (E7), GSUB, default ignorables (E8), advances, GPOS or legacy 'kern' (E10),
/// attachments, output.
/// </summary>
internal static class DefaultShaper
{
    // Per-thread working storage, reused by every call so shaping allocates nothing once warm (E11).
    [ThreadStatic]
    private static GlyphRun? _cachedRun;

    [ThreadStatic]
    private static LookupApplier? _cachedApplier;

    [ThreadStatic]
    private static List<(int Cp, int Source, int Cluster)>? _cps;

    [ThreadStatic]
    private static List<(int Cp, int Source, int Cluster)>? _norm;

    [ThreadStatic]
    private static List<(int Cp, int Source, int Cluster)>? _decomposed;

    [ThreadStatic]
    private static List<int>? _parts;

    internal static void Shape(ShapingFont font, ReadOnlySpan<char> text, int start, int length, ShapeOptions options, GlyphBuffer output)
    {
        OpenTypeFont f = font.Font;
        ShapePlan plan = font.GetPlan(options);
        GlyphRun run = _cachedRun ??= new GlyphRun();
        run.Clear();

        Prepare(f, text, start, start + length, plan, run, output);
        MarkFractions(run, plan);

        LookupApplier applier = _cachedApplier ??= new LookupApplier();
        applier.Reset(run, f, options.Limits);
        if (f.Gsub is LayoutTable gsub && plan.Gsub.Length > 0)
        {
            applier.ApplyGsub(gsub, plan.Gsub, font.GsubDigest!);
        }

        // TRACE R-TS-0029
        // E8 a: default ignorables become invisible, zero-advance glyphs after GSUB: the space glyph, else glyph 0.
        int space = f.CharacterMap.GetGlyph(0x20);
        for (int i = 0; i < run.Count; i++)
        {
            ref GlyphInfo g = ref run[i];
            if (g.Ignorable)
            {
                g.Glyph = space;
                g.Class = 0;
                g.XAdvance = 0;
            }
            else
            {
                g.XAdvance = g.FallbackAdvance >= 0 && g.Glyph == space ? g.FallbackAdvance : f.GetAdvanceWidth(g.Glyph);
            }
        }

        if (f.Gpos is LayoutTable gpos)
        {
            if (plan.Gpos.Length > 0)
            {
                applier.ApplyGpos(gpos, plan.Gpos, font.GposDigest!);
            }
        }
        else if (plan.KernMask != 0 && f.Kern is KernTable kern)
        {
            LegacyKern(run, kern, plan.KernMask);
        }

        // TRACE R-TS-0037
        // D-354: only in a font without GPOS (the statement's scope), for marks the font has no glyph for.
        if (f.Gpos is null)
        {
            Fallback.PositionMarks(f, run);
        }
        ResolveAttachments(run);

        for (int i = 0; i < run.Count; i++)
        {
            ref GlyphInfo g = ref run[i];
            output.Add(g.Glyph, g.Cluster, g.XAdvance, g.XOffset, g.YOffset);
        }

        if (applier.LimitHit is string limit && !output.HasDiagnostic(ShapingDiagnosticKind.LimitReached))
        {
            // TRACE R-TS-0030
            output.AddDiagnostic(ShapingDiagnosticKind.LimitReached, "Shaping stopped early: the font's lookups reached the " + limit + " limit; the run is returned as shaped so far.");
        }
    }

    // TRACE R-TS-0031
    private static void Prepare(OpenTypeFont font, ReadOnlySpan<char> text, int start, int end, ShapePlan plan, GlyphRun run, GlyphBuffer output)
    {
        CharacterMap cmap = font.CharacterMap;
        GdefTable? gdef = font.Gdef;

        // Code points and initial clusters (E3 b, D-345): a combining mark (Mn, Mc, Me) or ZWJ joins the cluster of the
        // character before it; ZWNJ and every other character start their own.
        List<(int Cp, int Source, int Cluster)> cps = _cps ??= new List<(int, int, int)>(64);
        cps.Clear();
        int i = start;
        while (i < end)
        {
            int source = i;
            int cp = NextCodePoint(text, ref i, end);
            bool joins = cps.Count > 0 && (cp == 0x200D || UnicodeData.IsMark(cp));
            cps.Add((cp, source, joins ? cps[^1].Cluster : source));
        }

        // Normalisation against the font, one cluster at a time (E2 b).
        List<(int Cp, int Source, int Cluster)> norm = _norm ??= new List<(int, int, int)>(64);
        norm.Clear();
        int k = 0;
        while (k < cps.Count)
        {
            int e = k + 1;
            while (e < cps.Count && cps[e].Cluster == cps[k].Cluster)
            {
                e++;
            }

            Normalize(cmap, cps, k, e, norm);
            k = e;
        }

        bool missingReported = output.HasDiagnostic(ShapingDiagnosticKind.MissingGlyph);
        int spaceGlyph = cmap.GetGlyph(0x20);
        int spaceAdvance = spaceGlyph != 0 ? font.GetAdvanceWidth(spaceGlyph) : 0;
        for (int n = 0; n < norm.Count; n++)
        {
            (int cp, int source, int cluster) = norm[n];
            GlyphInfo g = default;
            g.CodePoint = cp;
            g.SourceIndex = source;
            g.Cluster = cluster;
            g.Ignorable = UnicodeData.IsDefaultIgnorable(cp);
            g.Glyph = cmap.GetGlyph(cp);
            g.FallbackAdvance = -1;
            if (g.Glyph == 0 && !g.Ignorable && spaceGlyph != 0)
            {
                // TRACE R-TS-0036
                // D-354: a space character the font lacks takes the space glyph with the width for its kind.
                int width = Fallback.SpaceWidth(font, cp, spaceAdvance);
                if (width >= 0)
                {
                    g.Glyph = spaceGlyph;
                    g.FallbackAdvance = width;
                }
            }

            g.Mask = ShapePlan.GlobalBit;
            foreach (RangedBit r in plan.Ranges)
            {
                if (source >= r.Start && source < r.End)
                {
                    g.Mask |= r.Bit;
                }
            }

            // A variation selector picks the base's variant glyph when the font lists the sequence (cmap format 14).
            if (IsVariationSelector(cp) && run.Count > 0 && run[run.Count - 1].Cluster == cluster)
            {
                ref GlyphInfo b = ref run[run.Count - 1];
                if (cmap.GetVariationKind(b.CodePoint, cp) != VariationSequenceKind.NotListed)
                {
                    b.Glyph = cmap.GetGlyph(b.CodePoint, cp);
                    b.Class = gdef is null ? (byte)0 : (byte)gdef.GetGlyphClass(b.Glyph);
                }
            }

            g.Class = gdef is null ? (byte)0 : (byte)gdef.GetGlyphClass(g.Glyph);
            if (g.Glyph == 0 && !g.Ignorable && !missingReported)
            {
                // TRACE R-TS-0014
                output.AddDiagnostic(ShapingDiagnosticKind.MissingGlyph, string.Create(CultureInfo.InvariantCulture, $"U+{cp:X4} at index {source} has no glyph in the font; glyph 0 (.notdef) was used."));
                missingReported = true;
            }

            run.Add(g);
        }
    }

    // TRACE R-TS-0032
    private static void Normalize(CharacterMap cmap, List<(int Cp, int Source, int Cluster)> cps, int from, int to, List<(int, int, int)> output)
    {
        if (to - from == 1 && (cmap.HasGlyph(cps[from].Cp) || !UnicodeData.TryGetCanonicalDecomposition(cps[from].Cp, out _, out _)))
        {
            output.Add(cps[from]);
            return;
        }

        // Full canonical decomposition, canonical ordering, then canonical composition limited to characters the font maps.
        List<int> parts = _parts ??= new List<int>(4);
        List<(int Cp, int Source, int Cluster)> d = _decomposed ??= new List<(int, int, int)>(8);
        d.Clear();
        for (int k = from; k < to; k++)
        {
            parts.Clear();
            Normalization.Decompose(cps[k].Cp, parts);
            foreach (int p in parts)
            {
                d.Add((p, cps[k].Source, cps[k].Cluster));
            }
        }

        for (int k = 1; k < d.Count; k++)
        {
            int cc = UnicodeData.GetCombiningClass(d[k].Cp);
            int j = k;
            while (cc != 0 && j > 0 && UnicodeData.GetCombiningClass(d[j - 1].Cp) > cc)
            {
                (d[j - 1], d[j]) = (d[j], d[j - 1]);
                j--;
            }
        }

        int starter = 0;
        int lastClass = UnicodeData.GetCombiningClass(d[0].Cp) == 0 ? 0 : 256;
        int kept = 1;
        for (int k = 1; k < d.Count; k++)
        {
            int cc = UnicodeData.GetCombiningClass(d[k].Cp);
            if ((lastClass < cc || lastClass == 0) && lastClass != 256
                && UnicodeData.TryCompose(d[starter].Cp, d[k].Cp, out int composite) && cmap.HasGlyph(composite))
            {
                d[starter] = (composite, d[starter].Source, d[starter].Cluster);
                continue;
            }

            if (cc == 0)
            {
                starter = kept;
            }

            lastClass = cc;
            d[kept++] = d[k];
        }

        d.RemoveRange(kept, d.Count - kept);

        // E2 b: use the normalised form when the font maps all of it, or when it leaves fewer characters without
        // glyphs than the text as given; otherwise keep the text as given (a precomposed character is not split into
        // parts the font does not all have).
        int candidateMissing = Missing(cmap, d, 0, d.Count);
        if (candidateMissing == 0 || candidateMissing < Missing(cmap, cps, from, to))
        {
            foreach ((int, int, int) x in d)
            {
                output.Add(x);
            }
        }
        else
        {
            // The text as given, still in canonical order (D-354 statement: marks are first put into canonical order;
            // reordering is canonically equivalent, Unicode Standard chapter 3.11).
            int first = output.Count;
            for (int k = from; k < to; k++)
            {
                output.Add(cps[k]);
            }

            for (int k = first + 1; k < output.Count; k++)
            {
                int cc = UnicodeData.GetCombiningClass(output[k].Item1);
                int j = k;
                while (cc != 0 && j > first && UnicodeData.GetCombiningClass(output[j - 1].Item1) > cc)
                {
                    (output[j - 1], output[j]) = (output[j], output[j - 1]);
                    j--;
                }
            }
        }
    }

    private static int Missing(CharacterMap cmap, List<(int Cp, int Source, int Cluster)> cps, int from, int to)
    {
        int n = 0;
        for (int k = from; k < to; k++)
        {
            if (!cmap.HasGlyph(cps[k].Cp) && !UnicodeData.IsDefaultIgnorable(cps[k].Cp))
            {
                n++;
            }
        }

        return n;
    }

    // TRACE R-TS-0019
    private static void MarkFractions(GlyphRun run, ShapePlan plan)
    {
        if (plan.FracBit == 0)
        {
            return;
        }

        for (int k = 0; k < run.Count; k++)
        {
            if (run[k].CodePoint != 0x2044)
            {
                continue;
            }

            int first = k;
            while (first > 0 && IsDigit(run[first - 1].CodePoint))
            {
                first--;
            }

            int last = k;
            while (last + 1 < run.Count && IsDigit(run[last + 1].CodePoint))
            {
                last++;
            }

            if (first == k || last == k)
            {
                continue;
            }

            for (int j = first; j <= last; j++)
            {
                run[j].Mask |= plan.FracBit | (j < k ? plan.NumrBit : j > k ? plan.DnomBit : 0);
            }

            k = last;
        }
    }

    // TRACE R-TS-0033
    private static void LegacyKern(GlyphRun run, KernTable kern, ulong mask)
    {
        int previous = -1;
        for (int i = 0; i < run.Count; i++)
        {
            if (run[i].Ignorable)
            {
                continue;
            }

            if (previous >= 0 && (run[previous].Mask & mask) != 0)
            {
                int value = 0;
                IReadOnlyList<KernSubtable> subtables = kern.Subtables;
                for (int s = 0; s < subtables.Count; s++)
                {
                    KernSubtable st = subtables[s];
                    if (st.Format == 0 && st.IsHorizontal && !st.IsCrossStream && !st.IsMinimum && st.TryGetValue(run[previous].Glyph, run[i].Glyph, out int v))
                    {
                        value = st.IsOverride ? v : value + v;
                    }
                }

                run[previous].XAdvance += value;
            }

            previous = i;
        }
    }

    // TRACE R-TS-0027
    private static void ResolveAttachments(GlyphRun run)
    {
        for (int i = 0; i < run.Count; i++)
        {
            ref GlyphInfo g = ref run[i];
            if (g.AttachKind == 0)
            {
                continue;
            }

            int p = i + g.AttachTo;
            if ((uint)p >= (uint)run.Count || p >= i)
            {
                continue;
            }

            g.YOffset += run[p].YOffset + g.AttachDy;
            if (g.AttachKind == GlyphRun.AttachMark)
            {
                int pen = 0;
                for (int k = p; k < i; k++)
                {
                    pen += run[k].XAdvance;
                }

                g.XOffset += run[p].XOffset + g.AttachDx - pen;
            }
        }
    }

    private static bool IsDigit(int cp) => UnicodeData.GetGeneralCategory(cp) == GeneralCategory.DecimalNumber;

    private static bool IsVariationSelector(int cp) => (cp >= 0xFE00 && cp <= 0xFE0F) || (cp >= 0xE0100 && cp <= 0xE01EF) || (cp >= 0x180B && cp <= 0x180D) || cp == 0x180F;

    // TRACE R-TS-0012
    private static int NextCodePoint(ReadOnlySpan<char> text, ref int i, int end)
    {
        char c = text[i++];
        if (char.IsHighSurrogate(c))
        {
            if (i < end && char.IsLowSurrogate(text[i]))
            {
                return char.ConvertToUtf32(c, text[i++]);
            }

            return 0xFFFD;
        }

        return char.IsLowSurrogate(c) ? 0xFFFD : c;
    }
}
