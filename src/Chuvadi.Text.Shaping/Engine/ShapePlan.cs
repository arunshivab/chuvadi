using System;
using System.Collections.Generic;
using Chuvadi.OpenType;
using Chuvadi.OpenType.Layout;
using LayoutFeature = Chuvadi.OpenType.Layout.Feature;
using LayoutScript = Chuvadi.OpenType.Layout.Script;

namespace Chuvadi.Text.Shaping.Engine;

/// <summary>A lookup to apply, with the glyph mask that lets it start and the feature value it came with.</summary>
internal readonly record struct PlannedLookup(int Index, ulong Mask, uint Value);

/// <summary>A feature setting over a range of the text, with the mask bit it sets.</summary>
internal readonly record struct RangedBit(int Start, int End, ulong Bit);

/// <summary>
/// An immutable shaping plan for one font, script, language system and set of feature settings (E5, E6, E11): the
/// GSUB and GPOS lookups in LookupList order with their masks.
/// </summary>
internal sealed class ShapePlan
{
    /// <summary>The bit every glyph carries: global features use it.</summary>
    internal const ulong GlobalBit = 1;

    // TRACE R-TS-0017
    // The contract's default list (P4 a, D-188) and the registry's required and on-by-default features for the
    // default path, always on unless a setting turns them off (E5 a, D-319).
    private static readonly Tag[] DefaultTags = Tags("ccmp", "locl", "calt", "liga", "cpsp", "kern", "mark", "mkmk");
    private static readonly Tag[] AlwaysOnTags = Tags("rvrn", "rlig", "rclt", "curs", "dist", "abvm", "blwm");

    internal static readonly Tag Kern = Tag.Parse("kern");
    internal static readonly Tag Frac = Tag.Parse("frac");
    internal static readonly Tag Numr = Tag.Parse("numr");
    internal static readonly Tag Dnom = Tag.Parse("dnom");

    private ShapePlan(PlannedLookup[] gsub, PlannedLookup[] gpos, RangedBit[] ranges, ulong kernMask, ulong fracBit, ulong numrBit, ulong dnomBit)
    {
        Gsub = gsub;
        Gpos = gpos;
        Ranges = ranges;
        KernMask = kernMask;
        FracBit = fracBit;
        NumrBit = numrBit;
        DnomBit = dnomBit;
    }

    internal PlannedLookup[] Gsub { get; }

    internal PlannedLookup[] Gpos { get; }

    /// <summary>The feature settings that cover only part of the text.</summary>
    internal RangedBit[] Ranges { get; }

    /// <summary>The mask under which the legacy 'kern' table applies (E10); 0 when 'kern' is off.</summary>
    internal ulong KernMask { get; }

    /// <summary>The bits for the automatic fraction (E7); 0 when it is switched off.</summary>
    internal ulong FracBit { get; }

    internal ulong NumrBit { get; }

    internal ulong DnomBit { get; }

    internal static ShapePlan Build(OpenTypeFont font, ShapeOptions options)
    {
        // The settings, tag by tag: defaults first, then the caller's settings in order (a later setting for the same
        // tag and range wins).
        List<(Tag Tag, uint Value, int Start, int End)> settings = new List<(Tag, uint, int, int)>();
        foreach (Tag t in AlwaysOnTags)
        {
            settings.Add((t, 1, 0, int.MaxValue));
        }

        if (options.UseDefaultFeatures)
        {
            foreach (Tag t in DefaultTags)
            {
                settings.Add((t, 1, 0, int.MaxValue));
            }
        }

        foreach (Feature f in options.Features)
        {
            int i = settings.FindIndex(s => s.Tag == f.Tag && s.Start == f.Start && s.End == f.End);
            if (i >= 0)
            {
                settings[i] = (f.Tag, f.Value, f.Start, f.End);
            }
            else
            {
                settings.Add((f.Tag, f.Value, f.Start, f.End));
            }
        }

        // Bits: global settings share GlobalBit; each ranged setting and the three fraction features get their own.
        int nextBit = 1;
        List<RangedBit> ranges = new List<RangedBit>();
        Dictionary<Tag, List<(uint Value, ulong Bit)>> byTag = new Dictionary<Tag, List<(uint, ulong)>>();
        foreach ((Tag tag, uint value, int start, int end) in settings)
        {
            ulong bit = GlobalBit;
            bool global = start <= 0 && end == int.MaxValue;
            if (!global)
            {
                if (nextBit >= 64)
                {
                    continue; // more than 63 ranged settings: the rest are dropped (documented in R-TS-0017)
                }

                bit = 1UL << nextBit++;
                ranges.Add(new RangedBit(start, end, bit));
            }

            if (!byTag.TryGetValue(tag, out List<(uint, ulong)>? list))
            {
                byTag[tag] = list = new List<(uint, ulong)>();
            }

            if (global)
            {
                list.RemoveAll(e => e.Item2 == GlobalBit);
            }

            if (value != 0)
            {
                list.Add((value, bit));
            }
        }

        // TRACE R-TS-0019
        // Automatic fraction (E7 a): on unless a global 'frac' setting of 0 turns it off.
        bool fracOff = settings.FindLastIndex(s => s.Tag == Frac && s.Start <= 0 && s.End == int.MaxValue && s.Value == 0) >= 0;
        ulong fracBit = 0, numrBit = 0, dnomBit = 0;
        if (!fracOff && nextBit <= 61)
        {
            fracBit = 1UL << nextBit++;
            numrBit = 1UL << nextBit++;
            dnomBit = 1UL << nextBit++;
            Add(byTag, Frac, fracBit);
            Add(byTag, Numr, numrBit);
            Add(byTag, Dnom, dnomBit);
        }

        ulong kernMask = 0;
        if (byTag.TryGetValue(Kern, out List<(uint Value, ulong Bit)>? kern))
        {
            foreach ((uint _, ulong b) in kern)
            {
                kernMask |= b;
            }
        }

        PlannedLookup[] gsub = Collect(font.Gsub, options, byTag);
        PlannedLookup[] gpos = Collect(font.Gpos, options, byTag);
        return new ShapePlan(gsub, gpos, ranges.ToArray(), kernMask, fracBit, numrBit, dnomBit);
    }

    // TRACE R-TS-0016
    internal static bool TryFindLanguageSystem(LayoutTable table, ShapeOptions options, out LanguageSystem languageSystem)
    {
        languageSystem = default;
        LayoutScript script = default;
        bool found = false;
        foreach (Tag t in Layout.ScriptTags.For(options.Script))
        {
            if (table.Scripts.TryFind(t, out script))
            {
                found = true;
                break;
            }
        }

        if (!found && !table.Scripts.TryFind(Layout.ScriptTags.Default, out script))
        {
            return false;
        }

        if (options.Language.OpenTypeTag != Language.Default.OpenTypeTag && script.TryFindLanguageSystem(options.Language.OpenTypeTag, out languageSystem))
        {
            return true;
        }

        languageSystem = script.DefaultLanguageSystem;
        return languageSystem.FeatureIndices.Count > 0 || languageSystem.RequiredFeatureIndex >= 0;
    }

    private static PlannedLookup[] Collect(LayoutTable? table, ShapeOptions options, Dictionary<Tag, List<(uint Value, ulong Bit)>> byTag)
    {
        if (table is null || !TryFindLanguageSystem(table, options, out LanguageSystem ls))
        {
            return Array.Empty<PlannedLookup>();
        }

        SortedDictionary<int, PlannedLookup> lookups = new SortedDictionary<int, PlannedLookup>();
        if (ls.RequiredFeatureIndex >= 0)
        {
            AddFeature(table, ls.RequiredFeatureIndex, GlobalBit, 1, lookups);
        }

        for (int i = 0; i < ls.FeatureIndices.Count; i++)
        {
            int fi = ls.FeatureIndices[i];
            if (fi == ls.RequiredFeatureIndex || !byTag.TryGetValue(table.Features.GetTag(fi), out List<(uint Value, ulong Bit)>? settings))
            {
                continue;
            }

            foreach ((uint value, ulong bit) in settings)
            {
                AddFeature(table, fi, bit, value, lookups);
            }
        }

        PlannedLookup[] result = new PlannedLookup[lookups.Count];
        lookups.Values.CopyTo(result, 0);
        return result;
    }

    // TRACE R-TS-0018
    private static void AddFeature(LayoutTable table, int featureIndex, ulong bit, uint value, SortedDictionary<int, PlannedLookup> lookups)
    {
        LayoutFeature feature = table.GetFeatureAtDefaultInstance(featureIndex);
        for (int k = 0; k < feature.LookupIndices.Count; k++)
        {
            int li = feature.LookupIndices[k];
            if (li >= table.Lookups.Count)
            {
                continue;
            }

            lookups[li] = lookups.TryGetValue(li, out PlannedLookup old) ? old with { Mask = old.Mask | bit } : new PlannedLookup(li, bit, value);
        }
    }

    private static void Add(Dictionary<Tag, List<(uint Value, ulong Bit)>> byTag, Tag tag, ulong bit)
    {
        if (!byTag.TryGetValue(tag, out List<(uint, ulong)>? list))
        {
            byTag[tag] = list = new List<(uint, ulong)>();
        }

        list.Add((1, bit));
    }

    private static Tag[] Tags(params string[] tags) => Array.ConvertAll(tags, Tag.Parse);
}
