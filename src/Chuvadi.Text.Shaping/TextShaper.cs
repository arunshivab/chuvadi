// PHASE 0. Character-to-glyph mapping and default advances only; no layout
// features yet. The pipeline stages (normalisation, script engines, GSUB, GPOS,
// fallback positioning) are added in phases 3 to 6 of design note v1.

using System;
using Chuvadi.OpenType;

namespace Chuvadi.Text.Shaping;

/// <summary>Shapes runs of text with an OpenType font.</summary>
public static class TextShaper
{
    /// <summary>Shapes a run of text into a reusable buffer.</summary>
    /// <param name="font">The font.</param>
    /// <param name="text">The whole paragraph (or line). Characters outside the run are context only.</param>
    /// <param name="start">The UTF-16 index where the run starts.</param>
    /// <param name="length">The run's length in UTF-16 units.</param>
    /// <param name="options">Script, language, direction and features; <see cref="ShapeOptions.Default"/> when null.</param>
    /// <param name="output">The buffer to fill. Its previous content is discarded.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> or <paramref name="output"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The run is not inside <paramref name="text"/>.</exception>
    /// <exception cref="NotSupportedException">The direction is right to left (not supported yet).</exception>
    public static void Shape(ShapingFont font, ReadOnlySpan<char> text, int start, int length, ShapeOptions? options, GlyphBuffer output)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(output);
        if (start < 0 || length < 0 || start > text.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The run must lie inside the text.");
        }

        options ??= ShapeOptions.Default;
        if (options.Direction == TextDirection.RightToLeft)
        {
            throw new NotSupportedException("Right-to-left shaping is not supported yet.");
        }

        OpenTypeFont f = font.Font;
        CharacterMap cmap = f.CharacterMap;
        output.Reset(f.UnitsPerEm, length);
        int end = start + length;
        int i = start;
        while (i < end)
        {
            int cluster = i;
            int cp = NextCodePoint(text, ref i, end);
            // TRACE R-TS-0011
            int glyph = cmap.GetGlyph(cp);
            output.Add(glyph, cluster, f.GetAdvanceWidth(glyph), 0, 0);
        }
    }

    /// <summary>Shapes a whole text into a reusable buffer.</summary>
    /// <param name="font">The font.</param>
    /// <param name="text">The text.</param>
    /// <param name="options">Script, language, direction and features; <see cref="ShapeOptions.Default"/> when null.</param>
    /// <param name="output">The buffer to fill.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> or <paramref name="output"/> is null.</exception>
    /// <exception cref="NotSupportedException">The direction is right to left (not supported yet).</exception>
    public static void Shape(ShapingFont font, ReadOnlySpan<char> text, ShapeOptions? options, GlyphBuffer output) =>
        Shape(font, text, 0, text.Length, options, output);

    /// <summary>Shapes a whole text and returns a new immutable run.</summary>
    /// <param name="font">The font.</param>
    /// <param name="text">The text.</param>
    /// <param name="options">Script, language, direction and features; <see cref="ShapeOptions.Default"/> when null.</param>
    /// <returns>The shaped run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> or <paramref name="text"/> is null.</exception>
    /// <exception cref="NotSupportedException">The direction is right to left (not supported yet).</exception>
    public static ShapedRun Shape(ShapingFont font, string text, ShapeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        GlyphBuffer buffer = new GlyphBuffer(text.Length);
        Shape(font, text.AsSpan(), 0, text.Length, options, buffer);
        return buffer.ToShapedRun();
    }

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
