using System;
using System.Collections.Generic;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.Text
{
    /// <summary>
    /// One positioned glyph, in font units, in logical order. <see cref="Cluster"/> is the UTF-16 index (in the
    /// string passed to the shaper) of the first character the glyph belongs to; glyphs of one cluster share it.
    /// </summary>
    public readonly record struct ShapedGlyph(int GlyphId, int Cluster, int XAdvance, int XOffset, int YOffset);

    /// <summary>Per-run shaping settings.</summary>
    public sealed record ShapingOptions
    {
        /// <summary>Resolved script of the run.</summary>
        public UnicodeScript Script { get; init; } = UnicodeScript.Latin;

        /// <summary>BCP 47 language tag, when known (language-specific forms).</summary>
        public string? Language { get; init; }

        /// <summary>Apply pair kerning (Word: run kerning on and size at or above the threshold; Excel: never).</summary>
        public bool Kerning { get; init; }

        /// <summary>Apply standard ligatures (Word's default is off).</summary>
        public bool Ligatures { get; init; }

        public static ShapingOptions Default { get; } = new ShapingOptions();
    }

    /// <summary>
    /// Converts a run of text in one face and one script to positioned glyphs. Implementations must be
    /// thread-safe. Layout calls <see cref="Shape"/> while fitting lines, so it should be fast and should
    /// cache per-face state.
    /// </summary>
    public interface IShaper
    {
        /// <summary>Human-readable name for warnings and reports.</summary>
        string Name { get; }

        /// <summary>True when this shaper renders <paramref name="script"/> correctly with <paramref name="face"/>.</summary>
        bool Supports(FontFace face, UnicodeScript script);

        /// <summary>Shapes <c>text[start..start+length]</c>. Cluster values are indices into <paramref name="text"/>.</summary>
        IReadOnlyList<ShapedGlyph> Shape(FontFace face, string text, int start, int length, ShapingOptions options);
    }

    /// <summary>
    /// The built-in shaper: one glyph per code point from the cmap, with optional pair kerning. Correct for
    /// scripts without contextual forms (Latin, Greek, Cyrillic, Armenian, Georgian, CJK ideographs, kana,
    /// Hangul syllables, symbols). Complex scripts produce nominal glyphs; layout reports
    /// <c>ComplexScriptNotShaped</c> when no shaper that <see cref="IShaper.Supports"/> them is configured.
    /// Default-ignorable characters (ZWJ, ZWNJ, soft hyphen, variation selectors, bidi controls) get no glyph.
    /// </summary>
    public sealed class SimpleShaper : IShaper
    {
        public static SimpleShaper Instance { get; } = new SimpleShaper();

        public string Name => "Chuvadi.Layout simple shaper";

        public bool Supports(FontFace face, UnicodeScript script) => !UnicodeScripts.NeedsComplexShaping(script);

        public IReadOnlyList<ShapedGlyph> Shape(FontFace face, string text, int start, int length, ShapingOptions options)
        {
            ArgumentNullException.ThrowIfNull(face);
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(options);
            if (start < 0 || length < 0 || start + length > text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            List<ShapedGlyph> glyphs = new List<ShapedGlyph>(length);
            int end = start + length;
            int prevGlyph = -1;
            for (int i = start; i < end; i++)
            {
                int cluster = i;
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < end && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                if (IsDefaultIgnorable(cp))
                {
                    continue;
                }
                int g = face.GetGlyphId(cp);
                int advance = face.GetAdvance(g);
                if (options.Kerning && prevGlyph >= 0)
                {
                    int k = face.GetKerning(prevGlyph, g);
                    if (k != 0)
                    {
                        ShapedGlyph last = glyphs[^1];
                        glyphs[^1] = last with { XAdvance = last.XAdvance + k };
                    }
                }
                glyphs.Add(new ShapedGlyph(g, cluster, advance, 0, 0));
                prevGlyph = g;
            }
            return glyphs;
        }

        /// <summary>Default_Ignorable_Code_Point characters that never produce a visible glyph.</summary>
        public static bool IsDefaultIgnorable(int cp)
            => cp is 0x00AD or 0x034F or 0x061C or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or 0x3164 or 0xFEFF or 0xFFA0
               || (cp >= 0x180B && cp <= 0x180F) || (cp >= 0x200B && cp <= 0x200F) || (cp >= 0x202A && cp <= 0x202E)
               || (cp >= 0x2060 && cp <= 0x206F) || (cp >= 0xFE00 && cp <= 0xFE0F) || (cp >= 0xE0000 && cp <= 0xE0FFF);
    }

    /// <summary>A maximal run of text in one resolved script.</summary>
    public readonly record struct ScriptRun(int Start, int Length, UnicodeScript Script);

    /// <summary>
    /// Splits text into script runs (UAX #24 resolution): Common and Inherited characters take the script of
    /// the preceding character, or of the following one at the start of the text; text with no script at all
    /// is reported as <see cref="UnicodeScript.Common"/>.
    /// </summary>
    public static class ScriptItemizer
    {
        public static List<ScriptRun> Itemize(string text, int start, int length)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (start < 0 || length < 0 || start + length > text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            List<ScriptRun> runs = new List<ScriptRun>();
            int end = start + length;
            UnicodeScript current = UnicodeScript.Common;
            int runStart = start;
            // Script of the first strong character, used for leading Common/Inherited text.
            for (int i = start; i < end; i++)
            {
                int cp = GraphemeClusters.Decode(text, i, out int len);
                UnicodeScript s = UnicodeScripts.GetScript(cp);
                if (s is not (UnicodeScript.Common or UnicodeScript.Inherited or UnicodeScript.Unknown))
                {
                    current = s;
                    break;
                }
                i += len - 1;
            }
            for (int i = start; i < end;)
            {
                int cp = GraphemeClusters.Decode(text, i, out int len);
                UnicodeScript s = UnicodeScripts.GetScript(cp);
                if (s is not (UnicodeScript.Common or UnicodeScript.Inherited or UnicodeScript.Unknown) && s != current)
                {
                    if (i > runStart)
                    {
                        runs.Add(new ScriptRun(runStart, i - runStart, current));
                    }
                    runStart = i;
                    current = s;
                }
                i += len;
            }
            if (end > runStart)
            {
                runs.Add(new ScriptRun(runStart, end - runStart, current));
            }
            return runs;
        }
    }
}
