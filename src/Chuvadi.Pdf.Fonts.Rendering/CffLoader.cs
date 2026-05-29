// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe Technical Note #5176 — CFF File Format
//        Adobe Technical Note #5177 — Type 2 Charstring Format
// PHASE: Phase 2.1 — CFF parser

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Loads a Compact Font Format (CFF) / Type 1C font program and produces
/// glyph outlines. Matches the public surface of <see cref="TrueTypeLoader"/>
/// for interoperability.
/// </summary>
/// <remarks>
/// <para>
/// CFF is the format embedded in PDFs via <c>/FontFile3</c> with subtype
/// <c>Type1C</c> (simple CFF) or <c>CIDFontType0C</c> (CID-keyed CFF).
/// </para>
/// <para>
/// Glyph outlines are produced by interpreting Type 2 charstrings — a
/// stack-based language with about 50 operators covering point movement,
/// curve construction, and subroutine calls. The interpreter here implements
/// the operators that appear in font files in practice; hint operators
/// (<c>hstem</c>, <c>vstem</c>, <c>hintmask</c>, <c>cntrmask</c>) are parsed
/// for stack consistency but produce no outline output.
/// </para>
/// </remarks>
public sealed class CffLoader
{
    private readonly byte[] _data;
    private readonly List<byte[]> _globalSubrs;
    private readonly List<byte[]> _charStrings;
    private readonly List<byte[]> _localSubrs;
    private readonly Dictionary<int, int> _charsetToGid;
    private readonly Dictionary<int, int> _cidToGid;
    private readonly Dictionary<string, int> _glyphNameToGid;
    private readonly bool _isCidFont;
    private readonly int _defaultWidthX;
    private readonly int _nominalWidthX;
    private readonly int _unitsPerEm;

    /// <summary>Parses a CFF font program.</summary>
    public CffLoader(byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        if (fontData.Length < 4) { throw new FontRenderingException("CFF data too short."); }
        _data = fontData;
        _globalSubrs = new List<byte[]>();
        _charStrings = new List<byte[]>();
        _localSubrs = new List<byte[]>();
        _charsetToGid = new Dictionary<int, int>();
        _cidToGid = new Dictionary<int, int>();
        _glyphNameToGid = new Dictionary<string, int>(StringComparer.Ordinal);

        // Header
        int cursor = 0;
        byte major = _data[cursor++];
        if (major != 1) { throw new FontRenderingException($"Unsupported CFF version: {major}"); }
        _ = _data[cursor++];        // minor
        byte hdrSize = _data[cursor++];
        _ = _data[cursor++];        // offsetSize
        cursor = hdrSize;

        // Name INDEX
        cursor = SkipIndex(cursor);

        // Top DICT INDEX
        List<byte[]> topDicts = ReadIndex(cursor, out cursor);
        if (topDicts.Count == 0) { throw new FontRenderingException("CFF Top DICT missing."); }
        Dictionary<int, double[]> topDict = ParseDict(topDicts[0]);

        // String INDEX — retained so the charset's SIDs can be resolved to
        // glyph names (SIDs >= 391 index this list as sid - 391).
        List<byte[]> stringIndex = ReadIndex(cursor, out cursor);

        // Global Subrs INDEX
        _globalSubrs = ReadIndex(cursor, out cursor);

        // Read offsets from Top DICT
        int charstringsOffset = (int)(topDict.GetValueOrDefault(17, new double[] { 0 })[0]);
        int privateSize = 0, privateOffset = 0;
        if (topDict.TryGetValue(18, out double[]? priv) && priv.Length >= 2)
        {
            privateSize = (int)priv[0];
            privateOffset = (int)priv[1];
        }
        int charsetOffset = (int)(topDict.GetValueOrDefault(15, new double[] { 0 })[0]);

        // ROS operator (12 30) is present iff the font is CID-keyed
        // (CIDFontType0C). For CID fonts the charset maps GID -> CID; for
        // simple fonts (Type1C) it maps GID -> SID (a glyph name).
        _isCidFont = topDict.ContainsKey(0x0C1E);

        // FontMatrix gives unitsPerEm. Default [0.001, 0, 0, 0.001, 0, 0] = 1000 units/em.
        _unitsPerEm = 1000;
        if (topDict.TryGetValue(0x0C07, out double[]? fm) && fm.Length >= 4 && fm[0] != 0)
        {
            _unitsPerEm = (int)Math.Round(1.0 / fm[0]);
        }

        // CharStrings INDEX
        if (charstringsOffset > 0)
        {
            _charStrings = ReadIndex(charstringsOffset, out _);
        }

        // Private DICT + Local Subrs
        if (privateSize > 0 && privateOffset > 0)
        {
            byte[] privBytes = new byte[privateSize];
            Array.Copy(_data, privateOffset, privBytes, 0, privateSize);
            Dictionary<int, double[]> privDict = ParseDict(privBytes);
            _defaultWidthX = (int)privDict.GetValueOrDefault(20, new double[] { 0 })[0];
            _nominalWidthX = (int)privDict.GetValueOrDefault(21, new double[] { 0 })[0];
            if (privDict.TryGetValue(19, out double[]? subrsOff) && subrsOff.Length > 0)
            {
                int subrsAbs = privateOffset + (int)subrsOff[0];
                _localSubrs = ReadIndex(subrsAbs, out _);
            }
        }

        // Build the charset maps. For CID-keyed fonts this yields CID -> GID
        // (CidToGid); for simple fonts it yields glyph-name -> GID
        // (GlyphNameToGid). The charstrings are addressed directly by GID.
        BuildCharsetMap(charsetOffset, stringIndex);
    }

    /// <summary>Font units per em (typically 1000 for CFF).</summary>
    public int UnitsPerEm => _unitsPerEm;

    /// <summary>Number of glyphs in the font.</summary>
    public int NumGlyphs => _charStrings.Count;

    /// <summary>
    /// Whether the font is CID-keyed (CIDFontType0C). When <c>true</c>,
    /// <see cref="CidToGid"/> is populated from the charset; when <c>false</c>,
    /// <see cref="GlyphNameToGid"/> is populated instead.
    /// </summary>
    public bool IsCidFont => _isCidFont;

    /// <summary>
    /// For CID-keyed fonts, maps each character identifier (CID) to its glyph
    /// index (GID). Empty for simple (Type1C) fonts.
    /// </summary>
    public IReadOnlyDictionary<int, int> CidToGid => _cidToGid;

    /// <summary>
    /// For simple (Type1C) fonts, maps each glyph name to its glyph index
    /// (GID). Empty for CID-keyed fonts.
    /// </summary>
    public IReadOnlyDictionary<string, int> GlyphNameToGid => _glyphNameToGid;

    /// <summary>Maps a Unicode code point to a glyph index. Returns 0 if not mapped.</summary>
    public int GetGlyphIndex(int codePoint)
        => _charsetToGid.GetValueOrDefault(codePoint, 0);

    /// <summary>Returns the rendered outline of the glyph with the given GID.</summary>
    public GlyphOutline GetGlyphOutline(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _charStrings.Count)
        {
            return new GlyphOutline(new Path(), new GlyphMetrics(0, 0, _unitsPerEm, new RectangleF(0, 0, 0, 0)));
        }
        Type2Interpreter interp = new(_globalSubrs, _localSubrs, _defaultWidthX, _nominalWidthX);
        Path outline = interp.Run(_charStrings[glyphId]);
        int adv = interp.AdvanceWidth;
        return new GlyphOutline(outline,
            new GlyphMetrics(adv, 0, _unitsPerEm, BoundsOf(outline)));
    }

    /// <summary>Returns metrics-only data for the glyph with the given GID.</summary>
    public GlyphMetrics GetGlyphMetrics(int glyphId) => GetGlyphOutline(glyphId).Metrics;

    // ── CFF parsing primitives ───────────────────────────────────────────

    private int SkipIndex(int offset)
    {
        if (offset >= _data.Length) { return offset; }
        int count = (_data[offset] << 8) | _data[offset + 1];
        if (count == 0) { return offset + 2; }
        int offSize = _data[offset + 2];
        int offsetsArrStart = offset + 3;
        int lastOffsetIdx = offsetsArrStart + count * offSize;
        int lastOffset = ReadOffset(lastOffsetIdx, offSize);
        return offsetsArrStart + (count + 1) * offSize + lastOffset - 1;
    }

    private List<byte[]> ReadIndex(int offset, out int newCursor)
    {
        List<byte[]> result = new();
        if (offset == 0 || offset >= _data.Length)
        {
            newCursor = offset;
            return result;
        }
        int count = (_data[offset] << 8) | _data[offset + 1];
        if (count == 0)
        {
            newCursor = offset + 2;
            return result;
        }
        int offSize = _data[offset + 2];
        int offsetsArrStart = offset + 3;
        int dataStart = offsetsArrStart + (count + 1) * offSize - 1;
        for (int i = 0; i < count; i++)
        {
            int start = ReadOffset(offsetsArrStart + i * offSize, offSize);
            int end = ReadOffset(offsetsArrStart + (i + 1) * offSize, offSize);
            int length = end - start;
            byte[] item = new byte[length];
            if (length > 0) { Array.Copy(_data, dataStart + start, item, 0, length); }
            result.Add(item);
        }
        int lastOffset = ReadOffset(offsetsArrStart + count * offSize, offSize);
        newCursor = dataStart + lastOffset;
        return result;
    }

    private int ReadOffset(int pos, int size) => size switch
    {
        1 => _data[pos],
        2 => (_data[pos] << 8) | _data[pos + 1],
        3 => (_data[pos] << 16) | (_data[pos + 1] << 8) | _data[pos + 2],
        4 => (_data[pos] << 24) | (_data[pos + 1] << 16) | (_data[pos + 2] << 8) | _data[pos + 3],
        _ => 0,
    };

    private static Dictionary<int, double[]> ParseDict(byte[] data)
    {
        Dictionary<int, double[]> result = new();
        List<double> operands = new();
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];
            if (b <= 21)
            {
                int op = b;
                if (b == 12 && i + 1 < data.Length)
                {
                    op = (12 << 8) | data[++i];
                }
                result[op] = operands.ToArray();
                operands.Clear();
                i++;
            }
            else if (b == 28)
            {
                short v = (short)((data[i + 1] << 8) | data[i + 2]);
                operands.Add(v); i += 3;
            }
            else if (b == 29)
            {
                int v = (data[i + 1] << 24) | (data[i + 2] << 16) | (data[i + 3] << 8) | data[i + 4];
                operands.Add(v); i += 5;
            }
            else if (b == 30)
            {
                // Real number (nibble-encoded)
                StringBuilder sb = new();
                i++;
                while (i < data.Length)
                {
                    byte nb = data[i++];
                    int hi = nb >> 4;
                    int lo = nb & 0x0F;
                    if (AppendNibble(sb, hi)) { break; }
                    if (AppendNibble(sb, lo)) { break; }
                }
                if (double.TryParse(sb.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double rv))
                {
                    operands.Add(rv);
                }
                else { operands.Add(0); }
            }
            else if (b >= 32 && b <= 246)
            {
                operands.Add(b - 139); i++;
            }
            else if (b >= 247 && b <= 250)
            {
                operands.Add((b - 247) * 256 + data[i + 1] + 108); i += 2;
            }
            else if (b >= 251 && b <= 254)
            {
                operands.Add(-((b - 251) * 256) - data[i + 1] - 108); i += 2;
            }
            else { i++; }
        }
        return result;
    }

    private static bool AppendNibble(StringBuilder sb, int n)
    {
        switch (n)
        {
            case 0:
            case 1:
            case 2:
            case 3:
            case 4:
            case 5:
            case 6:
            case 7:
            case 8:
            case 9:
                sb.Append((char)('0' + n)); return false;
            case 0xA: sb.Append('.'); return false;
            case 0xB: sb.Append('E'); return false;
            case 0xC: sb.Append("E-"); return false;
            case 0xE: sb.Append('-'); return false;
            case 0xF: return true;
            default: return false;
        }
    }

    private void BuildCharsetMap(int charsetOffset, List<byte[]> stringIndex)
    {
        int numGlyphs = _charStrings.Count;
        if (numGlyphs == 0) { return; }

        // gidToValue maps GID -> SID (simple fonts) or GID -> CID (CID fonts).
        // GID 0 (.notdef) is implicit and never stored in the charset data.
        Dictionary<int, int> gidToValue = new(numGlyphs) { [0] = 0 };

        // Predefined charsets (0 = ISOAdobe, 1 = Expert, 2 = ExpertSubset) and
        // any offset that would read out of bounds: fall back to identity, the
        // most useful default (GID == SID/CID for gid >= 1).
        if (charsetOffset <= 2 || charsetOffset >= _data.Length)
        {
            for (int gid = 1; gid < numGlyphs; gid++) { gidToValue[gid] = gid; }
        }
        else
        {
            ParseCharset(charsetOffset, numGlyphs, gidToValue);
        }

        if (_isCidFont)
        {
            // Charset values are CIDs. Invert to CID -> GID. First writer wins
            // so the lowest GID is kept on the rare duplicate CID.
            foreach (KeyValuePair<int, int> kv in gidToValue)
            {
                _cidToGid.TryAdd(kv.Value, kv.Key);
            }
        }
        else
        {
            // Charset values are SIDs (glyph names). Resolve each to a name and
            // map name -> GID.
            foreach (KeyValuePair<int, int> kv in gidToValue)
            {
                string? name = ResolveSid(kv.Value, stringIndex);
                if (name is not null)
                {
                    _glyphNameToGid.TryAdd(name, kv.Key);
                }
            }
        }
    }

    private void ParseCharset(int charsetOffset, int numGlyphs, Dictionary<int, int> gidToValue)
    {
        int format = _data[charsetOffset];
        int p = charsetOffset + 1;
        int gid = 1;

        if (format == 0)
        {
            while (gid < numGlyphs && p + 1 < _data.Length)
            {
                int sid = (_data[p] << 8) | _data[p + 1];
                p += 2;
                gidToValue[gid] = sid;
                gid++;
            }
        }
        else if (format == 1)
        {
            while (gid < numGlyphs && p + 2 < _data.Length)
            {
                int first = (_data[p] << 8) | _data[p + 1];
                int nLeft = _data[p + 2];
                p += 3;
                for (int i = 0; i <= nLeft && gid < numGlyphs; i++)
                {
                    gidToValue[gid] = first + i;
                    gid++;
                }
            }
        }
        else if (format == 2)
        {
            while (gid < numGlyphs && p + 3 < _data.Length)
            {
                int first = (_data[p] << 8) | _data[p + 1];
                int nLeft = (_data[p + 2] << 8) | _data[p + 3];
                p += 4;
                for (int i = 0; i <= nLeft && gid < numGlyphs; i++)
                {
                    gidToValue[gid] = first + i;
                    gid++;
                }
            }
        }
        else
        {
            // Unknown format — fall back to identity for the remaining glyphs.
            for (; gid < numGlyphs; gid++) { gidToValue[gid] = gid; }
        }
    }

    private static string? ResolveSid(int sid, List<byte[]> stringIndex)
    {
        string? standard = CffStandardStrings.Get(sid);
        if (standard is not null) { return standard; }

        int idx = sid - CffStandardStrings.Count;
        if (idx >= 0 && idx < stringIndex.Count)
        {
            return Encoding.Latin1.GetString(stringIndex[idx]);
        }

        return null;
    }

    private static RectangleF BoundsOf(Path p)
    {
        if (p.IsEmpty) { return new RectangleF(0, 0, 0, 0); }
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (PathSegment seg in p.Segments)
        {
            UpdateBounds(seg.P0, ref minX, ref minY, ref maxX, ref maxY);
            UpdateBounds(seg.P1, ref minX, ref minY, ref maxX, ref maxY);
            UpdateBounds(seg.P2, ref minX, ref minY, ref maxX, ref maxY);
        }
        return new RectangleF((float)minX, (float)minY, (float)(maxX - minX), (float)(maxY - minY));
    }

    private static void UpdateBounds(PointF pt, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        if (pt.X < minX) { minX = pt.X; }
        if (pt.Y < minY) { minY = pt.Y; }
        if (pt.X > maxX) { maxX = pt.X; }
        if (pt.Y > maxY) { maxY = pt.Y; }
    }
}
