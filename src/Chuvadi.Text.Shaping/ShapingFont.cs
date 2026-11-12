using System;
using System.Runtime.CompilerServices;
using Chuvadi.OpenType;

namespace Chuvadi.Text.Shaping;

/// <summary>
/// A font prepared for shaping. Shaping plans are built from the font's layout
/// tables when first needed and kept for later calls. One instance per
/// <see cref="OpenTypeFont"/>; safe to use from several threads at once.
/// </summary>
public sealed class ShapingFont
{
    private static readonly ConditionalWeakTable<OpenTypeFont, ShapingFont> Cache = new ConditionalWeakTable<OpenTypeFont, ShapingFont>();

    private ShapingFont(OpenTypeFont font)
    {
        Font = font;
    }

    // TRACE R-TS-0034
    // E11 b: at most 16 plans per font, the least recently used dropped.
    private const int PlanCapacity = 16;
    private readonly object _planLock = new object();
    private readonly (ShapeOptions Key, Engine.ShapePlan Plan)[] _plans = new (ShapeOptions, Engine.ShapePlan)[PlanCapacity];
    private int _planCount;
    private Engine.LookupDigest? _gsubDigest;
    private Engine.LookupDigest? _gposDigest;

    /// <summary>Gets the font.</summary>
    public OpenTypeFont Font { get; }

    // TRACE R-TS-0042
    /// <summary>Gets the per-lookup glyph sets of the font's GSUB, or null when it has none.</summary>
    internal Engine.LookupDigest? GsubDigest => _gsubDigest ??= Font.Gsub is { } t ? Interlock(ref _gsubDigest, new Engine.LookupDigest(t, Font.GlyphCount, positioning: false)) : null;

    /// <summary>Gets the per-lookup glyph sets of the font's GPOS, or null when it has none.</summary>
    internal Engine.LookupDigest? GposDigest => _gposDigest ??= Font.Gpos is { } t ? Interlock(ref _gposDigest, new Engine.LookupDigest(t, Font.GlyphCount, positioning: true)) : null;

    /// <summary>Gets the font's units per em.</summary>
    public int UnitsPerEm => Font.UnitsPerEm;

    /// <summary>Gets the shaping font for a loaded font, creating it on first use. The same font always gives the same instance.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The shaping font.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    public static ShapingFont Get(OpenTypeFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        // TRACE R-TS-0010
        return Cache.GetValue(font, f => new ShapingFont(f));
    }

    internal Engine.ShapePlan GetPlan(ShapeOptions options)
    {
        lock (_planLock)
        {
            for (int i = 0; i < _planCount; i++)
            {
                if (ReferenceEquals(_plans[i].Key, options) || SamePlan(_plans[i].Key, options))
                {
                    (ShapeOptions, Engine.ShapePlan) hit = _plans[i];
                    Array.Copy(_plans, 0, _plans, 1, i);
                    _plans[0] = hit;
                    return hit.Item2;
                }
            }

            Engine.ShapePlan plan = Engine.ShapePlan.Build(Font, options);
            int keep = Math.Min(_planCount, PlanCapacity - 1);
            Array.Copy(_plans, 0, _plans, 1, keep);
            _plans[0] = (options, plan);
            _planCount = keep + 1;
            return plan;
        }
    }

    private static bool SamePlan(ShapeOptions a, ShapeOptions b)
    {
        if (a.Script != b.Script || a.Language != b.Language || a.UseDefaultFeatures != b.UseDefaultFeatures || a.Features.Count != b.Features.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Features.Count; i++)
        {
            if (a.Features[i] != b.Features[i])
            {
                return false;
            }
        }

        return true;
    }

    private static Engine.LookupDigest Interlock(ref Engine.LookupDigest? field, Engine.LookupDigest made) =>
        System.Threading.Interlocked.CompareExchange(ref field, made, null) ?? made;
}
