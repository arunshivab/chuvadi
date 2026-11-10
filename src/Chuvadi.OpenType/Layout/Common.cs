using System;
using System.Collections.Generic;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Layout;

/// <summary>
/// A Coverage table: the set of glyphs a subtable applies to, each with its coverage index.
/// Read in place; a damaged table reads as empty.
/// </summary>
public readonly struct Coverage
{
    private readonly ReadOnlyMemory<byte> _table;
    private readonly int _offset;

    internal Coverage(ReadOnlyMemory<byte> table, int offset)
    {
        _table = table;
        _offset = offset;
    }

    /// <summary>True when the coverage table exists (its offset is inside the table).</summary>
    public bool Exists => _offset > 0 && _offset < _table.Length;

    /// <summary>The coverage format: 1 (glyph array) or 2 (ranges); 0 when absent or unknown.</summary>
    public int Format
    {
        get
        {
            int f = Exists ? BigEndian.U16(_table.Span, _offset) : 0;
            return f is 1 or 2 ? f : 0;
        }
    }

    /// <summary>
    /// The coverage index of a glyph, or -1 when the glyph is not covered.
    /// </summary>
    public int GetIndex(int glyphId)
    {
        // TRACE R-OT-0041
        if (glyphId < 0 || glyphId > 0xFFFF)
        {
            return -1;
        }

        ReadOnlySpan<byte> s = _table.Span;
        int format = Format;
        int count = BigEndian.U16(s, _offset + 2);
        int lo = 0;
        int hi = count - 1;
        if (format == 1)
        {
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int g = BigEndian.U16(s, _offset + 4 + (mid * 2));
                if (g < glyphId)
                {
                    lo = mid + 1;
                }
                else if (g > glyphId)
                {
                    hi = mid - 1;
                }
                else
                {
                    return mid;
                }
            }

            return -1;
        }

        if (format == 2)
        {
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int r = _offset + 4 + (mid * 6);
                int start = BigEndian.U16(s, r);
                int end = BigEndian.U16(s, r + 2);
                if (end < glyphId)
                {
                    lo = mid + 1;
                }
                else if (start > glyphId)
                {
                    hi = mid - 1;
                }
                else
                {
                    return start <= end ? BigEndian.U16(s, r + 4) + (glyphId - start) : -1;
                }
            }
        }

        return -1;
    }

    /// <summary>The glyphs covered, in table order, with their coverage indices.</summary>
    public IEnumerable<(int GlyphId, int CoverageIndex)> GetGlyphs()
    {
        ReadOnlyMemory<byte> table = _table;
        int offset = _offset;
        int format = Format;
        int count = Exists ? BigEndian.U16(table.Span, offset + 2) : 0;
        for (int i = 0; i < count; i++)
        {
            if (format == 1)
            {
                yield return (BigEndian.U16(table.Span, offset + 4 + (i * 2)), i);
            }
            else if (format == 2)
            {
                int r = offset + 4 + (i * 6);
                int start = BigEndian.U16(table.Span, r);
                int end = BigEndian.U16(table.Span, r + 2);
                int first = BigEndian.U16(table.Span, r + 4);
                for (int g = start; g <= end; g++)
                {
                    yield return (g, first + (g - start));
                }
            }
        }
    }
}

/// <summary>
/// A Class Definition table: assigns a class value to glyphs. Glyphs not listed are in class 0.
/// </summary>
public readonly struct ClassDefinition
{
    private readonly ReadOnlyMemory<byte> _table;
    private readonly int _offset;

    internal ClassDefinition(ReadOnlyMemory<byte> table, int offset)
    {
        _table = table;
        _offset = offset;
    }

    /// <summary>True when the class definition table exists.</summary>
    public bool Exists => _offset > 0 && _offset < _table.Length;

    /// <summary>The format: 1 (class array) or 2 (class ranges); 0 when absent or unknown.</summary>
    public int Format
    {
        get
        {
            int f = Exists ? BigEndian.U16(_table.Span, _offset) : 0;
            return f is 1 or 2 ? f : 0;
        }
    }

    /// <summary>The class of a glyph; 0 when it is not listed or the table is absent.</summary>
    public int GetClass(int glyphId)
    {
        // TRACE R-OT-0042
        ReadOnlySpan<byte> s = _table.Span;
        int format = Format;
        if (format == 1)
        {
            int start = BigEndian.U16(s, _offset + 2);
            int count = BigEndian.U16(s, _offset + 4);
            int i = glyphId - start;
            return i >= 0 && i < count ? BigEndian.U16(s, _offset + 6 + (i * 2)) : 0;
        }

        if (format == 2)
        {
            int lo = 0;
            int hi = BigEndian.U16(s, _offset + 2) - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int r = _offset + 4 + (mid * 6);
                int start = BigEndian.U16(s, r);
                int end = BigEndian.U16(s, r + 2);
                if (end < glyphId)
                {
                    lo = mid + 1;
                }
                else if (start > glyphId)
                {
                    hi = mid - 1;
                }
                else
                {
                    return BigEndian.U16(s, r + 4);
                }
            }
        }

        return 0;
    }
}

/// <summary>
/// A Device table (hinting adjustments by ppem size) or a VariationIndex table (variable fonts).
/// </summary>
public readonly struct DeviceTable
{
    private readonly ReadOnlyMemory<byte> _table;
    private readonly int _offset;

    internal DeviceTable(ReadOnlyMemory<byte> table, int offset)
    {
        _table = table;
        _offset = offset;
    }

    /// <summary>True when the table exists.</summary>
    public bool Exists => _offset > 0 && _offset < _table.Length;

    /// <summary>The deltaFormat field: 1, 2 or 3 for a Device table, 0x8000 for a VariationIndex table.</summary>
    public int DeltaFormat => Exists ? BigEndian.U16(_table.Span, _offset + 4) : 0;

    /// <summary>True for a VariationIndex table.</summary>
    public bool IsVariationIndex => DeltaFormat == 0x8000;

    /// <summary>Device table: the smallest ppem size with a delta. VariationIndex: the outer index.</summary>
    public int StartSizeOrOuterIndex => Exists ? BigEndian.U16(_table.Span, _offset) : 0;

    /// <summary>Device table: the largest ppem size with a delta. VariationIndex: the inner index.</summary>
    public int EndSizeOrInnerIndex => Exists ? BigEndian.U16(_table.Span, _offset + 2) : 0;

    /// <summary>
    /// The adjustment at the default instance of a variable font, in font units. A VariationIndex table refers to
    /// deltas that are zero at the default instance (D-194), and a Device table depends on a ppem size, so both give 0.
    /// </summary>
    public int DefaultInstanceDelta => 0; // TRACE R-OT-0043

    /// <summary>The Device table delta for a ppem size, in pixels; 0 outside the range or for a VariationIndex table.</summary>
    public int GetDelta(int ppem)
    {
        // TRACE R-OT-0043
        int format = DeltaFormat;
        if (format is < 1 or > 3)
        {
            return 0;
        }

        int start = StartSizeOrOuterIndex;
        int end = EndSizeOrInnerIndex;
        if (ppem < start || ppem > end)
        {
            return 0;
        }

        int bits = 1 << format; // 2, 4 or 8 bits per value
        int perWord = 16 / bits;
        int index = ppem - start;
        int word = BigEndian.U16(_table.Span, _offset + 6 + ((index / perWord) * 2));
        int shift = 16 - (bits * ((index % perWord) + 1));
        int value = (word >> shift) & ((1 << bits) - 1);
        int signBit = 1 << (bits - 1);
        return (value & signBit) != 0 ? value - (1 << bits) : value;
    }
}

/// <summary>A list of uint16 values (glyph ids or class values) in place.</summary>
public readonly struct UInt16List
{
    private readonly ReadOnlyMemory<byte> _table;
    private readonly int _offset;

    internal UInt16List(ReadOnlyMemory<byte> table, int offset, int count)
    {
        _table = table;
        _offset = offset;
        Count = count < 0 ? 0 : count;
    }

    /// <summary>The number of values.</summary>
    public int Count { get; }

    /// <summary>A value; 0 outside the list.</summary>
    public int this[int index] => (uint)index < (uint)Count ? BigEndian.U16(_table.Span, _offset + (index * 2)) : 0;
}
