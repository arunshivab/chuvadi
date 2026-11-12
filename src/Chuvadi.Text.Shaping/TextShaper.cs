// PHASE 1. Latin and the other non-complex scripts go through the engine (Engine/DefaultShaper: normalisation, GSUB,
// GPOS); the Indic scripts keep the phase-0 path (character-to-glyph mapping and default advances) until their
// phases (E1, D-319).

using System;
using System.Globalization;
using Chuvadi.OpenType;
using Chuvadi.OpenType.Layout;

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
        CheckScript(f, options.Script, output);
        if (!IsIndic(options.Script))
        {
            Engine.DefaultShaper.Shape(font, text, start, length, options, output);
            return;
        }

        int end = start + length;
        int i = start;
        while (i < end)
        {
            int cluster = i;
            int cp = NextCodePoint(text, ref i, end);
            // TRACE R-TS-0011
            int glyph = cmap.GetGlyph(cp);
            if (glyph == 0 && !output.HasDiagnostic(ShapingDiagnosticKind.MissingGlyph))
            {
                // TRACE R-TS-0014
                output.AddDiagnostic(ShapingDiagnosticKind.MissingGlyph, string.Create(CultureInfo.InvariantCulture, $"U+{cp:X4} at index {cluster} has no glyph in the font; glyph 0 (.notdef) was used."));
            }

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

    // TRACE R-TS-0015
    private static void CheckScript(OpenTypeFont font, Script script, GlyphBuffer output)
    {
        ReadOnlySpan<Tag> tags = Layout.ScriptTags.For(script);
        LayoutTable? gsub = font.Gsub;
        LayoutTable? gpos = font.Gpos;
        if (tags.IsEmpty || (gsub is null && gpos is null))
        {
            return;
        }

        foreach (Tag tag in tags)
        {
            if ((gsub is not null && gsub.Scripts.TryFind(tag, out _)) || (gpos is not null && gpos.Scripts.TryFind(tag, out _)))
            {
                return;
            }
        }

        output.AddDiagnostic(ShapingDiagnosticKind.ScriptNotInFont, $"The font has no layout data for the script {script}; its default script system was used.");
    }

    // TRACE R-TS-0035
    private static bool IsIndic(Script s) =>
        s == Script.Devanagari || s == Script.Bengali || s == Script.Gurmukhi || s == Script.Gujarati || s == Script.Odia
        || s == Script.Tamil || s == Script.Telugu || s == Script.Kannada || s == Script.Malayalam;

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
