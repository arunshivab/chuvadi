using System;
using System.Collections.Generic;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>
/// The legacy 'kern' table in the OpenType (version 0) form. Pair values are read
/// from format 0 subtables; format 2 subtables are listed but their values are
/// not read in this version. Apple's version 1 form is not part of OpenType and
/// reads as a table with no subtables.
/// </summary>
public sealed class KernTable
{
    private readonly KernSubtable[] _subtables;

    private KernTable(int version, KernSubtable[] subtables)
    {
        Version = version;
        _subtables = subtables;
    }

    /// <summary>Gets the table version: 0 for the OpenType form; 1 for Apple's form (whose subtables are not read).</summary>
    public int Version { get; }

    /// <summary>Gets the subtables in stored order.</summary>
    public IReadOnlyList<KernSubtable> Subtables => _subtables;

    // TRACE R-OT-0015
    internal static KernTable? TryRead(ReadOnlySpan<byte> s)
    {
        int version = BigEndian.U16(s, 0);
        if (version != 0)
        {
            return BigEndian.U32(s, 0) == 0x00010000u ? new KernTable(1, Array.Empty<KernSubtable>()) : null;
        }

        int nTables = BigEndian.U16(s, 2);
        List<KernSubtable> list = new List<KernSubtable>(nTables);
        int offset = 4;
        for (int i = 0; i < nTables; i++)
        {
            if (!BigEndian.Has(s, offset, 6))
            {
                break;
            }

            int length = BigEndian.U16(s, offset + 2);
            int coverage = BigEndian.U16(s, offset + 4);
            if (length < 6)
            {
                break;
            }

            ReadOnlyMemory<byte> data = BigEndian.SliceClamped(s, offset, length).ToArray();
            list.Add(new KernSubtable(coverage, data));
            offset += length;
        }

        return new KernTable(0, list.ToArray());
    }
}

/// <summary>One subtable of the OpenType 'kern' table.</summary>
public sealed class KernSubtable
{
    private readonly ReadOnlyMemory<byte> _data;

    internal KernSubtable(int coverage, ReadOnlyMemory<byte> data)
    {
        Coverage = coverage;
        _data = data;
    }

    /// <summary>Gets the coverage field as stored.</summary>
    public int Coverage { get; }

    /// <summary>Gets the subtable format (bits 8–15 of coverage): 0 or 2.</summary>
    public int Format => Coverage >> 8;

    /// <summary>Gets whether the subtable has horizontal data (coverage bit 0).</summary>
    public bool IsHorizontal => (Coverage & 1) != 0;

    /// <summary>Gets whether the subtable has minimum values rather than kerning values (coverage bit 1).</summary>
    public bool IsMinimum => (Coverage & 2) != 0;

    /// <summary>Gets whether kerning is perpendicular to the text flow (coverage bit 2).</summary>
    public bool IsCrossStream => (Coverage & 4) != 0;

    /// <summary>Gets whether the values replace rather than add to the accumulated value (coverage bit 3).</summary>
    public bool IsOverride => (Coverage & 8) != 0;

    /// <summary>Gets the value for a glyph pair from a format 0 subtable.</summary>
    /// <param name="left">The left glyph index.</param>
    /// <param name="right">The right glyph index.</param>
    /// <param name="value">The value in font units, or 0.</param>
    /// <returns>True when the subtable is format 0 and lists the pair.</returns>
    public bool TryGetValue(int left, int right, out int value)
    {
        value = 0;
        if (Format != 0 || (uint)left > 0xFFFF || (uint)right > 0xFFFF)
        {
            return false;
        }

        // TRACE R-OT-0015
        ReadOnlySpan<byte> s = _data.Span;
        int nPairs = BigEndian.U16(s, 6);
        uint key = ((uint)left << 16) | (uint)right;
        int lo = 0;
        int hi = Math.Min(nPairs, (s.Length - 14) / 6) - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int p = 14 + (6 * mid);
            uint k = BigEndian.U32(s, p);
            if (k == key)
            {
                value = BigEndian.I16(s, p + 4);
                return true;
            }

            if (k < key)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return false;
    }
}
