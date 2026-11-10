using System;
using System.Numerics;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Layout;

/// <summary>Which fields a value record stores (ValueFormat).</summary>
[Flags]
public enum ValueFormat
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Horizontal placement adjustment.</summary>
    XPlacement = 0x0001,

    /// <summary>Vertical placement adjustment.</summary>
    YPlacement = 0x0002,

    /// <summary>Horizontal advance adjustment.</summary>
    XAdvance = 0x0004,

    /// <summary>Vertical advance adjustment.</summary>
    YAdvance = 0x0008,

    /// <summary>Device or VariationIndex table for horizontal placement.</summary>
    XPlacementDevice = 0x0010,

    /// <summary>Device or VariationIndex table for vertical placement.</summary>
    YPlacementDevice = 0x0020,

    /// <summary>Device or VariationIndex table for horizontal advance.</summary>
    XAdvanceDevice = 0x0040,

    /// <summary>Device or VariationIndex table for vertical advance.</summary>
    YAdvanceDevice = 0x0080,
}

/// <summary>A GPOS value record, read with its ValueFormat; absent fields are 0.</summary>
public readonly struct ValueRecord
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _parent;
    private readonly int _xpd;
    private readonly int _ypd;
    private readonly int _xad;
    private readonly int _yad;

    internal ValueRecord(ReadOnlyMemory<byte> table, int recordOffset, ValueFormat format, int parentOffset)
    {
        // TRACE R-OT-0049
        _t = table;
        _parent = parentOffset;
        Format = format;
        ReadOnlySpan<byte> s = table.Span;
        int at = recordOffset;
        XPlacement = (format & ValueFormat.XPlacement) != 0 ? Next(s, ref at) : 0;
        YPlacement = (format & ValueFormat.YPlacement) != 0 ? Next(s, ref at) : 0;
        XAdvance = (format & ValueFormat.XAdvance) != 0 ? Next(s, ref at) : 0;
        YAdvance = (format & ValueFormat.YAdvance) != 0 ? Next(s, ref at) : 0;
        _xpd = (format & ValueFormat.XPlacementDevice) != 0 ? (ushort)Next(s, ref at) : 0;
        _ypd = (format & ValueFormat.YPlacementDevice) != 0 ? (ushort)Next(s, ref at) : 0;
        _xad = (format & ValueFormat.XAdvanceDevice) != 0 ? (ushort)Next(s, ref at) : 0;
        _yad = (format & ValueFormat.YAdvanceDevice) != 0 ? (ushort)Next(s, ref at) : 0;
    }

    /// <summary>The value format the record was read with.</summary>
    public ValueFormat Format { get; }

    /// <summary>Horizontal placement adjustment, font units.</summary>
    public int XPlacement { get; }

    /// <summary>Vertical placement adjustment, font units.</summary>
    public int YPlacement { get; }

    /// <summary>Horizontal advance adjustment, font units.</summary>
    public int XAdvance { get; }

    /// <summary>Vertical advance adjustment, font units.</summary>
    public int YAdvance { get; }

    /// <summary>Device or VariationIndex table for horizontal placement.</summary>
    public DeviceTable XPlacementDevice => Device(_xpd);

    /// <summary>Device or VariationIndex table for vertical placement.</summary>
    public DeviceTable YPlacementDevice => Device(_ypd);

    /// <summary>Device or VariationIndex table for horizontal advance.</summary>
    public DeviceTable XAdvanceDevice => Device(_xad);

    /// <summary>Device or VariationIndex table for vertical advance.</summary>
    public DeviceTable YAdvanceDevice => Device(_yad);

    /// <summary>The size in bytes of a value record with this format.</summary>
    public static int SizeOf(ValueFormat format) => BitOperations.PopCount((uint)format & 0xFF) * 2;

    private DeviceTable Device(int offset) => offset == 0 ? default : new DeviceTable(_t, _parent + offset);

    private static int Next(ReadOnlySpan<byte> s, ref int at)
    {
        int v = BigEndian.I16(s, at);
        at += 2;
        return v;
    }
}

/// <summary>A GPOS anchor point (formats 1, 2 and 3).</summary>
public readonly struct Anchor
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal Anchor(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when the anchor table exists (a NULL offset means no anchor).</summary>
    public bool Exists => _o > 0 && _o < _t.Length && Format is 1 or 2 or 3;

    /// <summary>The anchor format.</summary>
    public int Format => _o > 0 ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>X coordinate, font units.</summary>
    public int X => Exists ? BigEndian.I16(_t.Span, _o + 2) : 0;

    /// <summary>Y coordinate, font units.</summary>
    public int Y => Exists ? BigEndian.I16(_t.Span, _o + 4) : 0;

    /// <summary>Format 2: the contour point index; -1 otherwise.</summary>
    public int AnchorPoint => Format == 2 ? BigEndian.U16(_t.Span, _o + 6) : -1;

    /// <summary>Format 3: Device or VariationIndex table for X.</summary>
    public DeviceTable XDevice => Device(6);

    /// <summary>Format 3: Device or VariationIndex table for Y.</summary>
    public DeviceTable YDevice => Device(8);

    private DeviceTable Device(int field)
    {
        if (Format != 3)
        {
            return default;
        }

        int off = BigEndian.U16(_t.Span, _o + field);
        return off == 0 ? default : new DeviceTable(_t, _o + off);
    }
}

/// <summary>GPOS lookup type 1: adjust the position of single glyphs.</summary>
public readonly struct SinglePositioning
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal SinglePositioning(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when this is a type 1 subtable of format 1 or 2.</summary>
    public bool Exists => _o > 0 && Format is 1 or 2;

    /// <summary>The format: 1 (one value for all) or 2 (a value per glyph).</summary>
    public int Format => _o > 0 ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>The covered glyphs.</summary>
    public Coverage Coverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2)) : default;

    /// <summary>The value format.</summary>
    public ValueFormat ValueFormat => Exists ? (ValueFormat)BigEndian.U16(_t.Span, _o + 4) : ValueFormat.None;

    /// <summary>The adjustment for <paramref name="glyphId"/>, when it is covered.</summary>
    public bool TryGetValue(int glyphId, out ValueRecord value)
    {
        // TRACE R-OT-0049
        value = default;
        int index = Coverage.GetIndex(glyphId);
        if (index < 0)
        {
            return false;
        }

        ValueFormat vf = ValueFormat;
        if (Format == 1)
        {
            value = new ValueRecord(_t, _o + 6, vf, _o);
            return true;
        }

        if (index >= BigEndian.U16(_t.Span, _o + 6))
        {
            return false;
        }

        value = new ValueRecord(_t, _o + 8 + (index * ValueRecord.SizeOf(vf)), vf, _o);
        return true;
    }
}

/// <summary>GPOS lookup type 2: adjust the positions of a pair of glyphs.</summary>
public readonly struct PairPositioning
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal PairPositioning(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when this is a type 2 subtable of format 1 or 2.</summary>
    public bool Exists => _o > 0 && Format is 1 or 2;

    /// <summary>The format: 1 (glyph pairs) or 2 (class pairs).</summary>
    public int Format => _o > 0 ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>The first glyphs of the pairs.</summary>
    public Coverage Coverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2)) : default;

    /// <summary>The value format for the first glyph.</summary>
    public ValueFormat ValueFormat1 => Exists ? (ValueFormat)BigEndian.U16(_t.Span, _o + 4) : ValueFormat.None;

    /// <summary>The value format for the second glyph.</summary>
    public ValueFormat ValueFormat2 => Exists ? (ValueFormat)BigEndian.U16(_t.Span, _o + 6) : ValueFormat.None;

    /// <summary>Format 2: class definition for the first glyph.</summary>
    public ClassDefinition ClassDefinition1 => Format == 2 ? new ClassDefinition(_t, _o + BigEndian.U16(_t.Span, _o + 8)) : default;

    /// <summary>Format 2: class definition for the second glyph.</summary>
    public ClassDefinition ClassDefinition2 => Format == 2 ? new ClassDefinition(_t, _o + BigEndian.U16(_t.Span, _o + 10)) : default;

    /// <summary>Format 2: the number of first-glyph classes.</summary>
    public int Class1Count => Format == 2 ? BigEndian.U16(_t.Span, _o + 12) : 0;

    /// <summary>Format 2: the number of second-glyph classes.</summary>
    public int Class2Count => Format == 2 ? BigEndian.U16(_t.Span, _o + 14) : 0;

    /// <summary>
    /// The adjustments for the pair (<paramref name="first"/>, <paramref name="second"/>), when the subtable has one.
    /// </summary>
    public bool TryGetPair(int first, int second, out ValueRecord firstValue, out ValueRecord secondValue)
    {
        // TRACE R-OT-0050
        firstValue = default;
        secondValue = default;
        int index = Coverage.GetIndex(first);
        if (index < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> s = _t.Span;
        ValueFormat vf1 = ValueFormat1;
        ValueFormat vf2 = ValueFormat2;
        int size1 = ValueRecord.SizeOf(vf1);
        int size2 = ValueRecord.SizeOf(vf2);
        if (Format == 1)
        {
            if (index >= BigEndian.U16(s, _o + 8))
            {
                return false;
            }

            int set = _o + BigEndian.U16(s, _o + 10 + (index * 2));
            int recordSize = 2 + size1 + size2;
            int lo = 0;
            int hi = BigEndian.U16(s, set) - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int r = set + 2 + (mid * recordSize);
                int g = BigEndian.U16(s, r);
                if (g < second)
                {
                    lo = mid + 1;
                }
                else if (g > second)
                {
                    hi = mid - 1;
                }
                else
                {
                    firstValue = new ValueRecord(_t, r + 2, vf1, set);
                    secondValue = new ValueRecord(_t, r + 2 + size1, vf2, set);
                    return true;
                }
            }

            return false;
        }

        int c1 = ClassDefinition1.GetClass(first);
        int c2 = ClassDefinition2.GetClass(second);
        if (c1 >= Class1Count || c2 >= Class2Count)
        {
            return false;
        }

        int rec = _o + 16 + (((c1 * Class2Count) + c2) * (size1 + size2));
        firstValue = new ValueRecord(_t, rec, vf1, _o);
        secondValue = new ValueRecord(_t, rec + size1, vf2, _o);
        return true;
    }
}

/// <summary>GPOS lookup type 3: cursive attachment (entry and exit anchors).</summary>
public readonly struct CursivePositioning
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal CursivePositioning(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when this is a type 3 subtable of format 1.</summary>
    public bool Exists => _o > 0 && BigEndian.U16(_t.Span, _o) == 1;

    /// <summary>The covered glyphs.</summary>
    public Coverage Coverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2)) : default;

    /// <summary>The entry and exit anchors of <paramref name="glyphId"/>; either may be absent.</summary>
    public bool TryGetAnchors(int glyphId, out Anchor entry, out Anchor exit)
    {
        // TRACE R-OT-0051
        entry = default;
        exit = default;
        int index = Coverage.GetIndex(glyphId);
        ReadOnlySpan<byte> s = _t.Span;
        if (index < 0 || index >= BigEndian.U16(s, _o + 4))
        {
            return false;
        }

        int r = _o + 6 + (index * 4);
        int en = BigEndian.U16(s, r);
        int ex = BigEndian.U16(s, r + 2);
        entry = en == 0 ? default : new Anchor(_t, _o + en);
        exit = ex == 0 ? default : new Anchor(_t, _o + ex);
        return true;
    }
}

/// <summary>
/// GPOS lookup types 4 (mark-to-base), 5 (mark-to-ligature) and 6 (mark-to-mark): a mark array and the anchors of
/// the glyphs the marks attach to.
/// </summary>
public readonly struct MarkAttachment
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal MarkAttachment(ReadOnlyMemory<byte> table, int offset, int lookupType)
    {
        _t = table;
        _o = offset;
        LookupType = offset > 0 ? lookupType : 0;
    }

    /// <summary>4, 5 or 6; 0 when absent.</summary>
    public int LookupType { get; }

    /// <summary>True when the subtable exists and has format 1.</summary>
    public bool Exists => _o > 0 && BigEndian.U16(_t.Span, _o) == 1;

    /// <summary>The marks (for type 6, the attaching marks, mark1).</summary>
    public Coverage MarkCoverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2)) : default;

    /// <summary>The glyphs marks attach to: bases (type 4), ligatures (type 5) or mark2 glyphs (type 6).</summary>
    public Coverage TargetCoverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 4)) : default;

    /// <summary>The number of mark classes.</summary>
    public int MarkClassCount => Exists ? BigEndian.U16(_t.Span, _o + 6) : 0;

    private int MarkArray => _o + BigEndian.U16(_t.Span, _o + 8);

    private int TargetArray => _o + BigEndian.U16(_t.Span, _o + 10);

    /// <summary>The class and anchor of the mark at <paramref name="markCoverageIndex"/>.</summary>
    public bool TryGetMark(int markCoverageIndex, out int markClass, out Anchor anchor)
    {
        // TRACE R-OT-0052
        markClass = 0;
        anchor = default;
        if (!Exists)
        {
            return false;
        }

        ReadOnlySpan<byte> s = _t.Span;
        int array = MarkArray;
        if ((uint)markCoverageIndex >= (uint)BigEndian.U16(s, array))
        {
            return false;
        }

        int r = array + 2 + (markCoverageIndex * 4);
        markClass = BigEndian.U16(s, r);
        int off = BigEndian.U16(s, r + 2);
        anchor = off == 0 ? default : new Anchor(_t, array + off);
        return true;
    }

    /// <summary>
    /// Types 4 and 6: the anchor of the base (or mark2) glyph at <paramref name="targetCoverageIndex"/> for a mark
    /// class; absent when the font has none.
    /// </summary>
    public Anchor GetTargetAnchor(int targetCoverageIndex, int markClass)
    {
        // TRACE R-OT-0052
        if (!Exists || LookupType == 5 || (uint)markClass >= (uint)MarkClassCount)
        {
            return default;
        }

        ReadOnlySpan<byte> s = _t.Span;
        int array = TargetArray;
        if ((uint)targetCoverageIndex >= (uint)BigEndian.U16(s, array))
        {
            return default;
        }

        int off = BigEndian.U16(s, array + 2 + (((targetCoverageIndex * MarkClassCount) + markClass) * 2));
        return off == 0 ? default : new Anchor(_t, array + off);
    }

    /// <summary>Type 5: the number of components of the ligature at <paramref name="ligatureCoverageIndex"/>.</summary>
    public int GetComponentCount(int ligatureCoverageIndex)
    {
        int attach = LigatureAttach(ligatureCoverageIndex);
        return attach > 0 ? BigEndian.U16(_t.Span, attach) : 0;
    }

    /// <summary>Type 5: the anchor for a mark class on component <paramref name="component"/> of a ligature.</summary>
    public Anchor GetLigatureAnchor(int ligatureCoverageIndex, int component, int markClass)
    {
        // TRACE R-OT-0052
        int attach = LigatureAttach(ligatureCoverageIndex);
        if (attach <= 0 || (uint)markClass >= (uint)MarkClassCount || (uint)component >= (uint)BigEndian.U16(_t.Span, attach))
        {
            return default;
        }

        int off = BigEndian.U16(_t.Span, attach + 2 + (((component * MarkClassCount) + markClass) * 2));
        return off == 0 ? default : new Anchor(_t, attach + off);
    }

    private int LigatureAttach(int ligatureCoverageIndex)
    {
        if (!Exists || LookupType != 5)
        {
            return 0;
        }

        ReadOnlySpan<byte> s = _t.Span;
        int array = TargetArray;
        if ((uint)ligatureCoverageIndex >= (uint)BigEndian.U16(s, array))
        {
            return 0;
        }

        int off = BigEndian.U16(s, array + 2 + (ligatureCoverageIndex * 2));
        return off == 0 ? 0 : array + off;
    }
}
