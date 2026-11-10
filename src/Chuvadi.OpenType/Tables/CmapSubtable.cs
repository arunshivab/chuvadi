using System;
using System.Collections.Generic;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>
/// One 'cmap' subtable. Lookups are by character code in the subtable's own
/// encoding (Unicode for Unicode subtables, Mac Roman bytes for (1, 0), and so on).
/// Formats 0, 2, 4, 6, 10, 12 and 13 map codes; format 14 holds variation
/// sequences and is used through <see cref="CharacterMap"/>. Format 8 is listed
/// but not read.
/// </summary>
public sealed class CmapSubtable
{
    private readonly ReadOnlyMemory<byte> _table;
    private readonly int _offset;
    private readonly int _glyphCount;

    internal CmapSubtable(ReadOnlyMemory<byte> table, int offset, int platformId, int encodingId, int glyphCount)
    {
        _table = table;
        _offset = offset;
        _glyphCount = glyphCount;
        PlatformId = platformId;
        EncodingId = encodingId;
        ReadOnlySpan<byte> s = table.Span;
        Format = BigEndian.U16(s, offset);
        Language = Format switch
        {
            8 or 10 or 12 or 13 => (int)BigEndian.U32(s, offset + 8),
            14 => 0,
            _ => BigEndian.U16(s, offset + 4),
        };
    }

    /// <summary>Gets the platform ID of the encoding record.</summary>
    public int PlatformId { get; }

    /// <summary>Gets the platform-specific encoding ID of the encoding record.</summary>
    public int EncodingId { get; }

    /// <summary>Gets the subtable format.</summary>
    public int Format { get; }

    /// <summary>Gets the language field (Macintosh language ID + 1, or 0).</summary>
    public int Language { get; }

    /// <summary>Gets whether this reader maps codes with this subtable's format.</summary>
    public bool IsSupported => Format is 0 or 2 or 4 or 6 or 10 or 12 or 13;

    /// <summary>Gets whether the format can map codes above U+FFFF (formats 10, 12, 13).</summary>
    public bool Is32Bit => Format is 10 or 12 or 13;

    /// <summary>Gets the glyph for a character code in this subtable's encoding.</summary>
    /// <param name="code">The character code.</param>
    /// <returns>The glyph index, or 0 when unmapped, unsupported, or the stored glyph is not below the font's glyph count.</returns>
    public int GetGlyph(int code)
    {
        if (code < 0)
        {
            return 0;
        }

        ReadOnlySpan<byte> s = BigEndian.SliceFrom(_table.Span, _offset);
        int g = Format switch
        {
            0 => Format0(s, code),
            2 => Format2(s, code),
            4 => Format4(s, code),
            6 => Format6(s, code),
            10 => Format10(s, code),
            12 => Format12Or13(s, code, false),
            13 => Format12Or13(s, code, true),
            _ => 0,
        };

        // TRACE R-OT-0017
        return g < _glyphCount ? g : 0;
    }

    /// <summary>Enumerates (code, glyph) pairs with a non-zero glyph, in ascending code order.</summary>
    /// <returns>The mappings.</returns>
    public IEnumerable<KeyValuePair<int, int>> EnumerateMappings()
    {
        foreach (int code in EnumerateCodes())
        {
            int g = GetGlyph(code);
            if (g != 0)
            {
                yield return new KeyValuePair<int, int>(code, g);
            }
        }
    }

    internal ReadOnlySpan<byte> Span => BigEndian.SliceFrom(_table.Span, _offset);

    internal ReadOnlyMemory<byte> Memory => _offset >= 0 && _offset < _table.Length ? _table.Slice(_offset) : ReadOnlyMemory<byte>.Empty;

    private IEnumerable<int> EnumerateCodes()
    {
        switch (Format)
        {
            case 0:
                for (int c = 0; c < 256; c++)
                {
                    yield return c;
                }

                break;

            case 2:
                for (int c = 0; c < 0x10000; c++)
                {
                    yield return c;
                }

                break;

            case 4:
                {
                    int segX2 = BigEndian.U16(Span, 6);
                    List<(int Start, int End)> segs = new List<(int, int)>();
                    for (int i = 0; i < segX2 / 2; i++)
                    {
                        int end = BigEndian.U16(Span, 14 + (2 * i));
                        int start = BigEndian.U16(Span, 16 + segX2 + (2 * i));
                        if (start <= end)
                        {
                            segs.Add((start, end));
                        }
                    }

                    segs.Sort();
                    int last = -1;
                    foreach ((int start, int end) in segs)
                    {
                        for (int c = Math.Max(start, last + 1); c <= end; c++)
                        {
                            yield return c;
                            last = c;
                        }
                    }

                    break;
                }

            case 6:
                {
                    int first = BigEndian.U16(Span, 6);
                    int count = BigEndian.U16(Span, 8);
                    for (int i = 0; i < count; i++)
                    {
                        yield return first + i;
                    }

                    break;
                }

            case 10:
                {
                    uint first = BigEndian.U32(Span, 12);
                    uint count = BigEndian.U32(Span, 16);
                    for (uint i = 0; i < count && first + i <= 0x10FFFF; i++)
                    {
                        yield return (int)(first + i);
                    }

                    break;
                }

            case 12:
            case 13:
                {
                    uint nGroups = BigEndian.U32(Span, 12);
                    List<(uint Start, uint End)> groups = new List<(uint, uint)>();
                    for (uint i = 0; i < nGroups && BigEndian.Has(Span, 16 + (int)(12 * i), 12); i++)
                    {
                        int p = 16 + (int)(12 * i);
                        uint start = BigEndian.U32(Span, p);
                        uint end = Math.Min(BigEndian.U32(Span, p + 4), 0x10FFFFu);
                        if (start <= end)
                        {
                            groups.Add((start, end));
                        }
                    }

                    groups.Sort();
                    long last = -1;
                    foreach ((uint start, uint end) in groups)
                    {
                        for (long c = Math.Max(start, last + 1); c <= end; c++)
                        {
                            yield return (int)c;
                            last = c;
                        }
                    }

                    break;
                }
        }
    }

    // TRACE R-OT-0018
    private static int Format0(ReadOnlySpan<byte> s, int code) => code < 256 ? BigEndian.U8(s, 6 + code) : 0;

    // TRACE R-OT-0019
    private static int Format2(ReadOnlySpan<byte> s, int code)
    {
        if (code > 0xFFFF)
        {
            return 0;
        }

        int hi = code >> 8;
        int lo = code & 0xFF;
        int key;
        int b;
        if (hi == 0)
        {
            // Single-byte code: valid only when subHeaderKeys[code] selects subHeader 0.
            key = BigEndian.U16(s, 6 + (2 * lo)) / 8;
            if (key != 0)
            {
                return 0;
            }

            b = lo;
        }
        else
        {
            key = BigEndian.U16(s, 6 + (2 * hi)) / 8;
            if (key == 0)
            {
                return 0;
            }

            b = lo;
        }

        int sh = 518 + (8 * key);
        int firstCode = BigEndian.U16(s, sh);
        int entryCount = BigEndian.U16(s, sh + 2);
        short idDelta = BigEndian.I16(s, sh + 4);
        int idRangeOffset = BigEndian.U16(s, sh + 6);
        if (b < firstCode || b >= firstCode + entryCount)
        {
            return 0;
        }

        int g = BigEndian.U16(s, sh + 6 + idRangeOffset + (2 * (b - firstCode)));
        return g == 0 ? 0 : (g + idDelta) & 0xFFFF;
    }

    // TRACE R-OT-0020
    private static int Format4(ReadOnlySpan<byte> s, int code)
    {
        if (code > 0xFFFF)
        {
            return 0;
        }

        int segX2 = BigEndian.U16(s, 6);
        int segCount = segX2 / 2;
        int endCodes = 14;
        int startCodes = 16 + segX2;
        int idDeltas = 16 + (2 * segX2);
        int idRangeOffsets = 16 + (3 * segX2);
        int lo = 0;
        int hi = segCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int end = BigEndian.U16(s, endCodes + (2 * mid));
            if (end < code)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (lo >= segCount)
        {
            return 0;
        }

        int start = BigEndian.U16(s, startCodes + (2 * lo));
        if (code < start)
        {
            return 0;
        }

        int delta = BigEndian.U16(s, idDeltas + (2 * lo));
        int rangeOffsetPos = idRangeOffsets + (2 * lo);
        int rangeOffset = BigEndian.U16(s, rangeOffsetPos);
        if (rangeOffset == 0)
        {
            return (code + delta) & 0xFFFF;
        }

        int g = BigEndian.U16(s, rangeOffsetPos + rangeOffset + (2 * (code - start)));
        return g == 0 ? 0 : (g + delta) & 0xFFFF;
    }

    // TRACE R-OT-0021
    private static int Format6(ReadOnlySpan<byte> s, int code)
    {
        int first = BigEndian.U16(s, 6);
        int count = BigEndian.U16(s, 8);
        return code >= first && code < first + count ? BigEndian.U16(s, 10 + (2 * (code - first))) : 0;
    }

    // TRACE R-OT-0022
    private static int Format10(ReadOnlySpan<byte> s, int code)
    {
        uint first = BigEndian.U32(s, 12);
        uint count = BigEndian.U32(s, 16);
        uint c = (uint)code;
        if (c < first || c - first >= count)
        {
            return 0;
        }

        long p = 20 + (2L * (c - first));
        return p > int.MaxValue ? 0 : BigEndian.U16(s, (int)p);
    }

    // TRACE R-OT-0023
    private static int Format12Or13(ReadOnlySpan<byte> s, int code, bool manyToOne)
    {
        uint nGroups = BigEndian.U32(s, 12);
        long maxGroups = (s.Length - 16) / 12;
        int n = (int)Math.Min(nGroups, Math.Max(0, maxGroups));
        uint c = (uint)code;
        int lo = 0;
        int hi = n - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int p = 16 + (12 * mid);
            uint start = BigEndian.U32(s, p);
            uint end = BigEndian.U32(s, p + 4);
            if (c < start)
            {
                hi = mid - 1;
            }
            else if (c > end)
            {
                lo = mid + 1;
            }
            else
            {
                uint startGlyph = BigEndian.U32(s, p + 8);
                ulong g = manyToOne ? startGlyph : (ulong)startGlyph + (c - start);
                return g > 0xFFFF ? 0 : (int)g;
            }
        }

        return 0;
    }
}
