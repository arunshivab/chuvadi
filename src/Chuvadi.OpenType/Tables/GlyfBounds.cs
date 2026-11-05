using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Tables;

/// <summary>Reads TrueType glyph bounding boxes from the glyph headers in 'glyf', located through 'loca'.</summary>
internal static class GlyfBounds
{
    // TRACE R-OT-0025
    internal static bool TryGet(ReadOnlySpan<byte> glyf, ReadOnlySpan<byte> loca, int indexToLocFormat, int glyphCount, int glyphId, out GlyphBounds bounds)
    {
        bounds = default;
        long start;
        long end;
        if (indexToLocFormat == 0)
        {
            if (!BigEndian.Has(loca, (glyphId + 1) * 2, 2))
            {
                return false;
            }

            start = BigEndian.U16(loca, glyphId * 2) * 2L;
            end = BigEndian.U16(loca, (glyphId + 1) * 2) * 2L;
        }
        else
        {
            if (!BigEndian.Has(loca, (glyphId + 1) * 4, 4))
            {
                return false;
            }

            start = BigEndian.U32(loca, glyphId * 4);
            end = BigEndian.U32(loca, (glyphId + 1) * 4);
        }

        // A glyph with no outline has loca[n] == loca[n+1]. A glyph header is 10 bytes.
        if (end - start < 10 || end > glyf.Length)
        {
            return false;
        }

        int o = (int)start;
        bounds = new GlyphBounds(BigEndian.I16(glyf, o + 2), BigEndian.I16(glyf, o + 4), BigEndian.I16(glyf, o + 6), BigEndian.I16(glyf, o + 8));
        return true;
    }
}
