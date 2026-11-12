using System;
using System.Collections.Generic;

namespace Chuvadi.Text.Shaping;

/// <summary>What to shape the text as: script, language system, direction and feature settings.</summary>
/// <remarks>
/// Features the script requires are always applied. <see cref="Features"/> are
/// settings layered over the default features; a setting with value 0 turns a
/// default feature off. With <see cref="UseDefaultFeatures"/> false, only the
/// script-required features and the listed settings apply.
/// The default features are the contract's list (ccmp, locl, calt, liga, cpsp, kern, mark, mkmk; P4 a, D-188). The
/// registry's required and on-by-default features (rvrn, rlig, rclt, curs, dist, abvm, blwm) and the automatic fraction
/// around U+2044 (frac, numr, dnom) always apply unless a setting with value 0 turns them off (E5, E7, D-319).
/// </remarks>
public sealed class ShapeOptions
{
    /// <summary>Gets a shared instance: Latin, default language system, left to right, default features.</summary>
    public static ShapeOptions Default { get; } = new ShapeOptions();

    /// <summary>Gets or inits the script. Default: <see cref="Script.Latin"/>.</summary>
    public Script Script { get; init; } = Script.Latin;

    /// <summary>Gets or inits the language system. Default: <see cref="Language.Default"/>.</summary>
    public Language Language { get; init; } = Language.Default;

    /// <summary>Gets or inits the direction. Default: left to right.</summary>
    public TextDirection Direction { get; init; } = TextDirection.LeftToRight;

    /// <summary>Gets or inits the feature settings, applied in order (a later setting for the same tag and range wins).</summary>
    public IReadOnlyList<Feature> Features { get; init; } = Array.Empty<Feature>();

    /// <summary>Gets or inits whether the default features apply. Default: true.</summary>
    public bool UseDefaultFeatures { get; init; } = true;

    /// <summary>Gets or inits the limits against hostile fonts. Default: <see cref="ShapingLimits.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public ShapingLimits Limits
    {
        get => _limits;
        init => _limits = value ?? throw new ArgumentNullException(nameof(value));
    }

    private readonly ShapingLimits _limits = ShapingLimits.Default;
}
