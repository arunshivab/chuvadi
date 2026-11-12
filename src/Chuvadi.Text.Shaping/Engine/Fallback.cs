using System;
using Chuvadi.OpenType;
using Chuvadi.Text.Shaping.Unicode;

namespace Chuvadi.Text.Shaping.Engine;

/// <summary>
/// Fallbacks for what a font lacks (D-354): widths for space characters the font has no glyph for, and positions for
/// combining marks the font has no glyph for, in a font with no GPOS table (D-361). Built from the Unicode Standard
/// (chapter 6, space characters; chapter 3, canonical combining classes; chapter 5, rendering of nonspacing marks), with
/// the distances the standard leaves open taken from the black-box statements in docs/Chuvadi.Text.Shaping/inputs/
/// (d345-fallback-statement.md, corrected by fallback-narrow-base-statement.md).
/// </summary>
internal static class Fallback
{
    // TRACE R-TS-0036
    /// <summary>
    /// The width of a space character the font has no glyph for, in font units, or -1 when it is not one the fallback
    /// replaces. The space glyph is used for it.
    /// </summary>
    internal static int SpaceWidth(OpenTypeFont font, int codePoint, int spaceAdvance)
    {
        int em = font.UnitsPerEm;
        return codePoint switch
        {
            0x00A0 => spaceAdvance,
            0x2000 or 0x2002 => Fraction(em, 1, 2),
            0x2001 or 0x2003 or 0x3000 => em,
            0x2004 => Fraction(em, 1, 3),
            0x2005 => Fraction(em, 1, 4),
            0x2006 => Fraction(em, 1, 6),
            0x2007 => AdvanceOf(font, '0', spaceAdvance),
            0x2008 => AdvanceOf(font, '.', spaceAdvance),
            0x2009 => Fraction(em, 1, 5),
            0x200A => Fraction(em, 1, 16),
            0x202F => Fraction(em, 1, 8),
            0x205F => Fraction(em, 4, 18),
            _ => -1,
        };
    }

    // TRACE R-TS-0037
    /// <summary>
    /// Positions every combining mark that maps to glyph 0, in a font with no GPOS table (D-361): advance 0, centred on its
    /// base's advance, and stacked above or below its ink by canonical combining class.
    /// </summary>
    internal static void PositionMarks(OpenTypeFont font, GlyphRun run)
    {
        // Glyph boxes are only read when there is a mark to place (a CFF box means running its charstring).
        int first = 0;
        while (first < run.Count && !(run[first].Glyph == 0 && run[first].AttachKind == 0 && !run[first].Ignorable && UnicodeData.IsMark(run[first].CodePoint)))
        {
            first++;
        }

        if (first == run.Count)
        {
            return;
        }

        int gap = font.UnitsPerEm / 16;
        GlyphBounds notdef = font.GetGlyphBounds(0);
        int baseIndex = -1;
        int aboveTop = 0, belowBottom = 0;
        for (int i = 0; i < run.Count; i++)
        {
            ref GlyphInfo g = ref run[i];
            bool fallbackMark = g.Glyph == 0 && g.AttachKind == 0 && !g.Ignorable && UnicodeData.IsMark(g.CodePoint);
            if (!fallbackMark)
            {
                if (!UnicodeData.IsMark(g.CodePoint) && !g.Ignorable)
                {
                    baseIndex = i;
                    GlyphBounds b = Ink(font, g.Glyph, out bool inked);
                    aboveTop = inked ? b.YMax + g.YOffset : g.YOffset;
                    belowBottom = inked ? b.YMin + g.YOffset : g.YOffset;
                }

                continue;
            }

            int ownAdvance = g.XAdvance;
            g.XAdvance = 0;
            int ccc = UnicodeData.GetCombiningClass(g.CodePoint);
            if (baseIndex < 0 || run[baseIndex].Cluster != g.Cluster)
            {
                // A mark with no base (statement: advance 0, x minus its advance, y 0).
                g.XOffset = -ownAdvance;
                g.YOffset = 0;
                continue;
            }

            ref GlyphInfo baseGlyph = ref run[baseIndex];
            int pen = 0;
            for (int k = baseIndex; k < i; k++)
            {
                pen += run[k].XAdvance;
            }

            // Horizontal reference: the base's advance, not its ink (the narrow-base statement,
            // docs/Chuvadi.Text.Shaping/inputs/fallback-narrow-base-statement.md). The right classes put the
            // mark's ink right edge at the end of the advance (the horn, 216, as observed; the others by the same
            // reference), the left classes its ink left edge at the start, the double classes centre on the boundary
            // after the base, everything else is centred on the advance.
            int origin = baseGlyph.XOffset - pen;
            int advance = baseGlyph.XAdvance;
            int markCentre = (notdef.XMin + notdef.XMax) / 2;
            g.XOffset = ccc switch
            {
                216 or 222 or 226 or 232 => origin + advance - notdef.XMax,
                200 or 218 or 224 or 228 => origin - notdef.XMin,
                233 or 234 => origin + advance - markCentre,
                _ => origin + (advance / 2) - markCentre,
            };

            // Vertical, by combining class: above and below are separate stacks, each mark one gap past the previous
            // ink; the attached classes touch with no gap; overlay and the rest stay on the baseline.
            switch (ccc)
            {
                case 228 or 230 or 232 or 234:
                    g.YOffset = aboveTop + gap - notdef.YMin;
                    aboveTop = g.YOffset + notdef.YMax;
                    break;
                case 214 or 216:
                    g.YOffset = aboveTop - notdef.YMin;
                    aboveTop = g.YOffset + notdef.YMax;
                    break;
                case 218 or 220 or 222 or 233:
                    g.YOffset = belowBottom - gap - notdef.YMax;
                    belowBottom = g.YOffset + notdef.YMin;
                    break;
                case 200 or 202:
                    g.YOffset = belowBottom - notdef.YMax;
                    belowBottom = g.YOffset + notdef.YMin;
                    break;
                default:
                    g.YOffset = 0;
                    break;
            }
        }
    }

    private static GlyphBounds Ink(OpenTypeFont font, int glyph, out bool inked)
    {
        inked = font.TryGetGlyphBounds(glyph, out GlyphBounds b) && b.Width > 0 && b.Height > 0;
        return b;
    }

    private static int AdvanceOf(OpenTypeFont font, char c, int spaceAdvance)
    {
        int glyph = font.CharacterMap.GetGlyph(c);
        return glyph != 0 ? font.GetAdvanceWidth(glyph) : spaceAdvance;
    }

    private static int Fraction(int em, int numerator, int denominator) =>
        (int)Math.Round((double)em * numerator / denominator, MidpointRounding.AwayFromZero);
}
