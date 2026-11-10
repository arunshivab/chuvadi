using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Layout;

/// <summary>Glyph classes defined by GDEF.</summary>
public enum GlyphClass
{
    /// <summary>Not classified (absent from the class definition).</summary>
    Unclassified = 0,

    /// <summary>Base glyph (single character, spacing glyph).</summary>
    Base = 1,

    /// <summary>Ligature glyph (multiple characters, spacing glyph).</summary>
    Ligature = 2,

    /// <summary>Mark glyph (non-spacing combining glyph).</summary>
    Mark = 3,

    /// <summary>Component glyph (part of a single character, spacing glyph).</summary>
    Component = 4,
}

/// <summary>
/// A read-only view of the 'GDEF' table: glyph classes, mark attachment classes, mark glyph sets, attachment
/// points and ligature carets. Lenient: damaged parts read as empty.
/// </summary>
public sealed class GdefTable
{
    private readonly ReadOnlyMemory<byte> _t;

    internal GdefTable(ReadOnlyMemory<byte> table)
    {
        // TRACE R-OT-0053
        _t = table;
        ReadOnlySpan<byte> s = table.Span;
        MajorVersion = BigEndian.U16(s, 0);
        MinorVersion = BigEndian.U16(s, 2);
        GlyphClassDefinition = Class(BigEndian.U16(s, 4));
        AttachListOffset = BigEndian.U16(s, 6);
        LigatureCaretListOffset = BigEndian.U16(s, 8);
        MarkAttachmentClassDefinition = Class(BigEndian.U16(s, 10));
        MarkGlyphSetsOffset = MinorVersion >= 2 ? BigEndian.U16(s, 12) : 0;
        ItemVariationStoreOffset = MinorVersion >= 3 ? Math.Max(0, BigEndian.Offset32(s, 14)) : 0;
    }

    /// <summary>Major version (1).</summary>
    public int MajorVersion { get; }

    /// <summary>Minor version (0, 2 or 3).</summary>
    public int MinorVersion { get; }

    /// <summary>The glyph class definition.</summary>
    public ClassDefinition GlyphClassDefinition { get; }

    /// <summary>True when the font assigns glyph classes.</summary>
    public bool HasGlyphClasses => GlyphClassDefinition.Exists;

    /// <summary>The mark attachment class definition.</summary>
    public ClassDefinition MarkAttachmentClassDefinition { get; }

    /// <summary>Offset of the attachment point list; 0 when absent.</summary>
    public int AttachListOffset { get; }

    /// <summary>Offset of the ligature caret list; 0 when absent.</summary>
    public int LigatureCaretListOffset { get; }

    /// <summary>Offset of the mark glyph sets definition (version 1.2 and later); 0 when absent.</summary>
    public int MarkGlyphSetsOffset { get; }

    /// <summary>Offset of the item variation store (version 1.3); 0 when absent. Not applied (default instance, D-194).</summary>
    public int ItemVariationStoreOffset { get; }

    /// <summary>The GDEF class of a glyph.</summary>
    public GlyphClass GetGlyphClass(int glyphId)
    {
        int c = GlyphClassDefinition.GetClass(glyphId);
        return c is >= 1 and <= 4 ? (GlyphClass)c : GlyphClass.Unclassified;
    }

    /// <summary>The mark attachment class of a glyph; 0 when none.</summary>
    public int GetMarkAttachmentClass(int glyphId) => MarkAttachmentClassDefinition.GetClass(glyphId);

    /// <summary>The number of mark glyph sets.</summary>
    public int MarkGlyphSetCount =>
        MarkGlyphSetsOffset > 0 && BigEndian.U16(_t.Span, MarkGlyphSetsOffset) == 1 ? BigEndian.U16(_t.Span, MarkGlyphSetsOffset + 2) : 0;

    /// <summary>True when <paramref name="glyphId"/> is in mark glyph set <paramref name="setIndex"/>.</summary>
    public bool IsInMarkGlyphSet(int setIndex, int glyphId)
    {
        // TRACE R-OT-0053
        if ((uint)setIndex >= (uint)MarkGlyphSetCount)
        {
            return false;
        }

        int off = BigEndian.Offset32(_t.Span, MarkGlyphSetsOffset + 4 + (setIndex * 4));
        return off > 0 && new Coverage(_t, MarkGlyphSetsOffset + off).GetIndex(glyphId) >= 0;
    }

    /// <summary>The contour point indices of a glyph's attachment points; empty when none.</summary>
    public UInt16List GetAttachmentPoints(int glyphId)
    {
        if (AttachListOffset == 0)
        {
            return default;
        }

        ReadOnlySpan<byte> s = _t.Span;
        int list = AttachListOffset;
        int index = new Coverage(_t, list + BigEndian.U16(s, list)).GetIndex(glyphId);
        if (index < 0 || index >= BigEndian.U16(s, list + 2))
        {
            return default;
        }

        int point = list + BigEndian.U16(s, list + 4 + (index * 2));
        return new UInt16List(_t, point + 2, BigEndian.U16(s, point));
    }

    /// <summary>The number of ligature carets defined for a ligature glyph.</summary>
    public int GetLigatureCaretCount(int glyphId)
    {
        int lig = LigatureGlyph(glyphId);
        return lig > 0 ? BigEndian.U16(_t.Span, lig) : 0;
    }

    /// <summary>
    /// Ligature caret <paramref name="index"/>: format 1 and 3 give a coordinate in font units; format 2 gives a
    /// contour point index instead (<paramref name="isPointIndex"/> is true).
    /// </summary>
    public int GetLigatureCaret(int glyphId, int index, out bool isPointIndex)
    {
        isPointIndex = false;
        int lig = LigatureGlyph(glyphId);
        ReadOnlySpan<byte> s = _t.Span;
        if (lig <= 0 || (uint)index >= (uint)BigEndian.U16(s, lig))
        {
            return 0;
        }

        int caret = lig + BigEndian.U16(s, lig + 2 + (index * 2));
        int format = BigEndian.U16(s, caret);
        if (format == 2)
        {
            isPointIndex = true;
            return BigEndian.U16(s, caret + 2);
        }

        return format is 1 or 3 ? BigEndian.I16(s, caret + 2) : 0;
    }

    private ClassDefinition Class(int offset) => offset == 0 ? default : new ClassDefinition(_t, offset);

    private int LigatureGlyph(int glyphId)
    {
        if (LigatureCaretListOffset == 0)
        {
            return 0;
        }

        ReadOnlySpan<byte> s = _t.Span;
        int list = LigatureCaretListOffset;
        int index = new Coverage(_t, list + BigEndian.U16(s, list)).GetIndex(glyphId);
        if (index < 0 || index >= BigEndian.U16(s, list + 2))
        {
            return 0;
        }

        return list + BigEndian.U16(s, list + 4 + (index * 2));
    }
}
