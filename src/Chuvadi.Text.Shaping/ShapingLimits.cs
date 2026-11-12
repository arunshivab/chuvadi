using System;

namespace Chuvadi.Text.Shaping;

// TRACE R-TS-0040
/// <summary>
/// Limits that keep a hostile or broken font from making shaping loop or explode (D-319, E12). When a limit is hit,
/// the run is returned as shaped so far and <see cref="ShapingDiagnosticKind.LimitReached"/> is reported.
/// </summary>
public sealed class ShapingLimits
{
    private readonly int _maxNestingDepth = 16;
    private readonly int _maxOperationsPerGlyph = 64;
    private readonly int _minOperations = 16384;

    /// <summary>Gets the default limits: nesting depth 16, 64 operations per glyph, at least 16,384 operations per call.</summary>
    public static ShapingLimits Default { get; } = new ShapingLimits();

    /// <summary>Gets or inits how deep contextual lookups may call other lookups. Default 16.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxNestingDepth
    {
        get => _maxNestingDepth;
        init => _maxNestingDepth = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "The nesting depth must be at least 1.");
    }

    /// <summary>Gets or inits the lookup applications allowed per glyph of the run. Default 64.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxOperationsPerGlyph
    {
        get => _maxOperationsPerGlyph;
        init => _maxOperationsPerGlyph = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "The operations per glyph must be at least 1.");
    }

    /// <summary>Gets or inits the smallest operation budget of a call, whatever the run's length. Default 16,384.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MinOperations
    {
        get => _minOperations;
        init => _minOperations = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "The minimum operations must be at least 1.");
    }

    internal long BudgetFor(int glyphs) => Math.Max(_minOperations, (long)_maxOperationsPerGlyph * glyphs);
}
