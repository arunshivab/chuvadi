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

    /// <summary>Gets the font.</summary>
    public OpenTypeFont Font { get; }

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
}
