// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — Table Directory, cmap (formats 4 and 12), head
//        https://docs.microsoft.com/typography/opentype/spec/otff
//        https://docs.microsoft.com/typography/opentype/spec/cmap
//        https://docs.microsoft.com/typography/opentype/spec/head
// PHASE: Phase 2.1 — v2.1.5
//        Rewrites a TrueType font program's cmap table so that glyph
//        indices reachable through legacy code points (e.g. Wingdings glyph
//        at code 0xFC) are ALSO reachable at the semantic Unicode code
//        points declared by a PDF ToUnicode CMap (e.g. U+2713 → the same
//        glyph). Without this, browsers asked to render U+2713 fall back
//        to a generic checkmark from a system font instead of using the
//        embedded Wingdings glyph.

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Rewrites the cmap table of an embedded TrueType font program so the
/// browser can locate the embedded glyph by its semantic Unicode code
/// point rather than the font's legacy encoding code point.
/// </summary>
/// <remarks>
/// <para>
/// PDF symbol fonts (Wingdings, Symbol, Webdings, ZapfDingbats, MT Extra,
/// and similar) carry glyphs at legacy Windows-symbol code points — for
/// example, the Wingdings checkmark glyph is reachable at character code
/// 0xFC in the original Windows encoding. PDF readers do not use the
/// font's cmap; they address glyphs directly by glyph index (CID). When
/// such a PDF is exported to SVG and the embedded font is placed in a
/// <c>@font-face</c> rule, the browser DOES use the cmap table and asks
/// for the glyph at the semantic Unicode code point (U+2713 ✓ for the
/// checkmark). Since no entry exists in the font's cmap at U+2713, the
/// browser falls back to a system font and renders the wrong glyph.
/// </para>
/// <para>
/// The fix is to add a new cmap subtable mapping each semantic Unicode
/// code point (from the ToUnicode CMap) to the corresponding glyph index.
/// This implementation replaces the cmap table entirely with a fresh one
/// containing a single Windows Unicode subtable — format 4 (BMP only) or
/// format 12 (full Unicode range). The original cmap subtables are not
/// preserved; the embedded font is used only by browser rendering of the
/// SVG, where the new mappings are sufficient.
/// </para>
/// <para>
/// This approach mirrors pdf2htmlEX's font remapping strategy. The
/// alternative of mutating the original cmap in place would require
/// parsing every cmap subtable format the source font might use; replacing
/// the table is simpler and equally effective for the SVG use case.
/// </para>
/// </remarks>
public static class TrueTypeFontPatch
{
    /// <summary>
    /// Returns a copy of <paramref name="fontBytes"/> whose cmap table has
    /// been replaced by a fresh table containing the given Unicode-to-glyph
    /// mappings. The original cmap subtables are discarded.
    /// </summary>
    /// <param name="fontBytes">Original TrueType/OpenType font program bytes.</param>
    /// <param name="unicodeToGid">
    /// Map from Unicode code point to glyph index. Code points outside
    /// the BMP (≥ 0x10000) trigger a format-12 cmap subtable; if all code
    /// points are in the BMP the result uses format 4.
    /// </param>
    /// <returns>New font program bytes with the cmap table replaced.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontBytes"/> or <paramref name="unicodeToGid"/> is null.
    /// </exception>
    /// <exception cref="FontRenderingException">
    /// Thrown when the input bytes are not a valid TrueType/OpenType font
    /// or are too short to contain an offset table.
    /// </exception>
    public static byte[] WithAugmentedCmap(
        byte[] fontBytes,
        IReadOnlyDictionary<int, int> unicodeToGid)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);
        ArgumentNullException.ThrowIfNull(unicodeToGid);

        if (fontBytes.Length < 12)
        {
            throw new FontRenderingException(
                "Font data too short to contain an offset table.");
        }

        uint sfVersion = ReadUInt32BE(fontBytes, 0);
        if (sfVersion != 0x00010000 && sfVersion != 0x4F54544F
            && sfVersion != 0x74727565 && sfVersion != 0x74797031)
        {
            throw new FontRenderingException(
                $"Not a valid TrueType/OpenType font. sfVersion = 0x{sfVersion:X8}.");
        }

        int numTables = ReadUInt16BE(fontBytes, 4);
        if (12 + numTables * 16 > fontBytes.Length)
        {
            throw new FontRenderingException("Truncated table directory.");
        }

        // Collect existing tables, then replace cmap (or insert one if absent).
        List<TableEntry> tables = ReadTableDirectory(fontBytes, numTables);

        // Sort mappings so we can build segments deterministically.
        SortedDictionary<int, int> sorted = new();
        foreach (KeyValuePair<int, int> kv in unicodeToGid)
        {
            if (kv.Key < 0 || kv.Value < 0 || kv.Value > 0xFFFF)
            {
                // Glyph indices are uint16; out-of-range entries are silently
                // skipped. The remaining mappings still produce a valid cmap.
                continue;
            }

            sorted[kv.Key] = kv.Value;
        }

        byte[] newCmap = BuildCmapTable(sorted);

        // Replace cmap entry; add it if missing.
        int cmapIdx = tables.FindIndex(t => t.Tag == 0x636D6170u);
        TableEntry cmapEntry = new(0x636D6170u, 0, 0, newCmap);
        if (cmapIdx >= 0)
        {
            tables[cmapIdx] = cmapEntry;
        }
        else
        {
            tables.Add(cmapEntry);
        }

        return SerializeFont(sfVersion, tables, fontBytes);
    }

    // ── Table directory ──────────────────────────────────────────────────

    private sealed record TableEntry(uint Tag, uint Checksum, uint Length, byte[] Data);

    private static List<TableEntry> ReadTableDirectory(byte[] data, int numTables)
    {
        List<TableEntry> tables = new(numTables);
        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = 12 + i * 16;
            uint tag = ReadUInt32BE(data, entryOffset);
            uint checksum = ReadUInt32BE(data, entryOffset + 4);
            uint offset = ReadUInt32BE(data, entryOffset + 8);
            uint length = ReadUInt32BE(data, entryOffset + 12);

            if (offset > data.Length || offset + length > data.Length)
            {
                throw new FontRenderingException(
                    $"Table {TagToString(tag)} extends past end of font.");
            }

            byte[] tableData = new byte[length];
            Array.Copy(data, (int)offset, tableData, 0, (int)length);
            tables.Add(new TableEntry(tag, checksum, length, tableData));
        }

        return tables;
    }

    // ── cmap table construction ──────────────────────────────────────────

    private static byte[] BuildCmapTable(SortedDictionary<int, int> mappings)
    {
        // cmap header: 4 bytes (version + numTables) + 8 bytes per EncodingRecord.
        // We emit a single EncodingRecord (Windows, Unicode BMP or full).
        bool needsFormat12 = false;
        foreach (int cp in mappings.Keys)
        {
            if (cp > 0xFFFF)
            {
                needsFormat12 = true;
                break;
            }
        }

        byte[] subtable;
        int encodingId;
        if (needsFormat12)
        {
            subtable = BuildFormat12Subtable(mappings);
            encodingId = 10; // Windows / Unicode UCS-4
        }
        else
        {
            subtable = BuildFormat4Subtable(mappings);
            encodingId = 1;  // Windows / Unicode BMP
        }

        // 4-byte header + one 8-byte EncodingRecord + subtable body.
        using MemoryStream ms = new();
        WriteUInt16BE(ms, 0);                 // version
        WriteUInt16BE(ms, 1);                 // numTables
        WriteUInt16BE(ms, 3);                 // platformID = Windows
        WriteUInt16BE(ms, encodingId);
        WriteUInt32BE(ms, 12u);               // subtableOffset (4 header + 8 record)
        ms.Write(subtable, 0, subtable.Length);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a cmap format-4 (segment mapping to delta values) subtable.
    /// One single-codepoint segment per mapping plus the mandatory 0xFFFF
    /// sentinel. Verbose but minimal and correct.
    /// </summary>
    private static byte[] BuildFormat4Subtable(SortedDictionary<int, int> mappings)
    {
        // segments: one per mapping + final sentinel (0xFFFF → glyph 0).
        List<int> endCodes = new();
        List<int> startCodes = new();
        List<int> idDeltas = new();

        foreach (KeyValuePair<int, int> kv in mappings)
        {
            int cp = kv.Key;
            int gid = kv.Value;
            endCodes.Add(cp);
            startCodes.Add(cp);
            // idDelta is added modulo 65536 to the code point to produce GID.
            idDeltas.Add((gid - cp) & 0xFFFF);
        }

        // Mandatory terminating segment: startCode = endCode = 0xFFFF.
        endCodes.Add(0xFFFF);
        startCodes.Add(0xFFFF);
        idDeltas.Add(1);  // 0xFFFF + 1 mod 65536 = 0 = .notdef

        int segCount = endCodes.Count;
        int segCountX2 = segCount * 2;

        // searchRange = 2 × 2^floor(log2(segCount))
        int searchRange = 2;
        int entrySelector = 0;
        while (searchRange * 2 <= segCountX2)
        {
            searchRange *= 2;
            entrySelector++;
        }
        int rangeShift = segCountX2 - searchRange;

        // Layout: 14-byte header
        //       + endCodes (2*segCount)
        //       + reservedPad (2)
        //       + startCodes (2*segCount)
        //       + idDeltas (2*segCount)
        //       + idRangeOffsets (2*segCount, all zero)
        //       + glyphIdArray (0 bytes — unused since idRangeOffsets are zero)
        int length = 14 + (2 * segCount) + 2 + (2 * segCount) + (2 * segCount) + (2 * segCount);

        using MemoryStream ms = new();
        WriteUInt16BE(ms, 4);              // format
        WriteUInt16BE(ms, length);          // length
        WriteUInt16BE(ms, 0);              // language
        WriteUInt16BE(ms, segCountX2);
        WriteUInt16BE(ms, searchRange);
        WriteUInt16BE(ms, entrySelector);
        WriteUInt16BE(ms, rangeShift);

        foreach (int v in endCodes) { WriteUInt16BE(ms, v); }
        WriteUInt16BE(ms, 0);              // reservedPad
        foreach (int v in startCodes) { WriteUInt16BE(ms, v); }
        foreach (int v in idDeltas) { WriteUInt16BE(ms, v); }
        for (int i = 0; i < segCount; i++) { WriteUInt16BE(ms, 0); } // idRangeOffsets

        return ms.ToArray();
    }

    /// <summary>
    /// Builds a cmap format-12 (segmented coverage) subtable. Used when
    /// any code point exceeds 0xFFFF.
    /// </summary>
    private static byte[] BuildFormat12Subtable(SortedDictionary<int, int> mappings)
    {
        // Group contiguous (startCharCode, endCharCode, startGlyphID) runs
        // where consecutive code points map to consecutive glyph indices.
        // For sparse symbol fonts this collapses to one group per entry,
        // which is the worst case but still correct.
        List<(int Start, int End, int StartGid)> groups = new();
        foreach (KeyValuePair<int, int> kv in mappings)
        {
            int cp = kv.Key;
            int gid = kv.Value;
            if (groups.Count > 0)
            {
                (int Start, int End, int StartGid) last = groups[^1];
                if (cp == last.End + 1 && gid == last.StartGid + (last.End - last.Start + 1))
                {
                    groups[^1] = (last.Start, cp, last.StartGid);
                    continue;
                }
            }

            groups.Add((cp, cp, gid));
        }

        // Header (16 bytes) + 12 bytes per group.
        uint length = (uint)(16 + groups.Count * 12);

        using MemoryStream ms = new();
        WriteUInt16BE(ms, 12);             // format
        WriteUInt16BE(ms, 0);              // reserved
        WriteUInt32BE(ms, length);
        WriteUInt32BE(ms, 0);              // language
        WriteUInt32BE(ms, (uint)groups.Count);

        foreach ((int start, int end, int startGid) in groups)
        {
            WriteUInt32BE(ms, (uint)start);
            WriteUInt32BE(ms, (uint)end);
            WriteUInt32BE(ms, (uint)startGid);
        }

        return ms.ToArray();
    }

    // ── Font serialisation ───────────────────────────────────────────────

    /// <summary>
    /// Re-serialises the font with the (potentially modified) tables,
    /// rebuilding the offset table and updating <c>head.checkSumAdjustment</c>
    /// per the OpenType spec.
    /// </summary>
    private static byte[] SerializeFont(
        uint sfVersion, List<TableEntry> tables, byte[] originalBytes)
    {
        // OpenType file order recommendation: head, hhea, maxp, OS/2, hmtx,
        // LTSH, VDMX, hdmx, cmap, fpgm, prep, cvt, loca, glyf, kern, name,
        // post, gasp, PCLT, DSIG. We're not strict — sorting by tag is fine
        // and is what most font compilers do — but we MUST keep loca and
        // glyf in their correct relative positions so that loca offsets
        // remain valid. Since loca offsets are RELATIVE to the start of the
        // glyf table, simply preserving each table's bytes unchanged is
        // sufficient; only the offsets in the Table Directory need updating.
        // We sort by tag for determinism.

        tables.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        int numTables = tables.Count;
        int dirSize = 12 + numTables * 16;

        // Allocate output buffer. Worst case: directory + sum of (padded) lengths.
        int totalLen = dirSize;
        foreach (TableEntry t in tables)
        {
            totalLen += AlignUp4(t.Data.Length);
        }

        byte[] output = new byte[totalLen];

        // Offset table header.
        WriteUInt32BE(output, 0, sfVersion);
        WriteUInt16BE(output, 4, numTables);

        int searchRange = 16;
        int entrySelector = 0;
        while ((searchRange * 2) <= (numTables * 16))
        {
            searchRange *= 2;
            entrySelector++;
        }

        int rangeShift = (numTables * 16) - searchRange;
        WriteUInt16BE(output, 6, searchRange);
        WriteUInt16BE(output, 8, entrySelector);
        WriteUInt16BE(output, 10, rangeShift);

        // Write each table's data, recording its offset. Pad each table up
        // to a 4-byte boundary with zero bytes (required by the OpenType
        // spec for checksum computation).
        int cursor = dirSize;
        int[] offsets = new int[numTables];
        for (int i = 0; i < numTables; i++)
        {
            offsets[i] = cursor;
            Array.Copy(tables[i].Data, 0, output, cursor, tables[i].Data.Length);
            cursor += AlignUp4(tables[i].Data.Length);
        }

        // Neutralise head.checkSumAdjustment BEFORE computing any checksums.
        //
        // OpenType spec, head table: the head table checksum and the
        // whole-font checksum must both be computed with checkSumAdjustment
        // set to zero. Doing this here (rather than only just before the
        // final whole-font sum) guarantees the head table's directory
        // checksum is also taken with the field zeroed. Without it the head
        // directory checksum would reflect whatever adjustment value the
        // input font carried — which makes a second application of this
        // method produce different bytes (non-idempotent) and leaves the
        // head directory checksum technically off-spec on any input whose
        // checkSumAdjustment was non-zero.
        int headIdx = tables.FindIndex(t => t.Tag == 0x68656164u); // 'head'
        if (headIdx >= 0)
        {
            int adjOffset = offsets[headIdx] + 8;
            if (adjOffset + 4 > output.Length)
            {
                throw new FontRenderingException("head table too short for checkSumAdjustment.");
            }

            output[adjOffset] = 0;
            output[adjOffset + 1] = 0;
            output[adjOffset + 2] = 0;
            output[adjOffset + 3] = 0;
        }

        // Compute each table's checksum from the (now adj-zeroed) output.
        uint[] checksums = new uint[numTables];
        for (int i = 0; i < numTables; i++)
        {
            int padded = AlignUp4(tables[i].Data.Length);
            checksums[i] = CalcTableChecksum(output, offsets[i], padded);
        }

        // Fill in directory entries.
        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = 12 + i * 16;
            WriteUInt32BE(output, entryOffset, tables[i].Tag);
            WriteUInt32BE(output, entryOffset + 4, checksums[i]);
            WriteUInt32BE(output, entryOffset + 8, (uint)offsets[i]);
            WriteUInt32BE(output, entryOffset + 12, (uint)tables[i].Data.Length);
        }

        // Compute the whole-font checksum (with checkSumAdjustment still
        // zero and the directory entries now written) and store the final
        // adjustment value per the spec:
        //   checkSumAdjustment = 0xB1B0AFBA - wholeFontChecksum
        if (headIdx >= 0)
        {
            int adjOffset = offsets[headIdx] + 8;
            uint totalSum = CalcTableChecksum(output, 0, output.Length);
            uint adjustment = unchecked(0xB1B0AFBAu - totalSum);
            WriteUInt32BE(output, adjOffset, adjustment);
        }

        // If we got here without a head table the font is malformed but
        // the result is still a valid SFNT shape; preserve the original
        // bytes' philosophy and don't throw.
        _ = originalBytes;
        return output;
    }

    private static uint CalcTableChecksum(byte[] data, int offset, int length)
    {
        // Length is always a multiple of 4 here because we always pass
        // padded lengths. Sum is in unsigned overflow arithmetic.
        uint sum = 0;
        int end = offset + length;
        for (int i = offset; i + 3 < end; i += 4)
        {
            sum = unchecked(sum + ReadUInt32BE(data, i));
        }

        return sum;
    }

    // ── Binary helpers ───────────────────────────────────────────────────

    private static int AlignUp4(int n) => (n + 3) & ~3;

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24)
             | ((uint)data[offset + 1] << 16)
             | ((uint)data[offset + 2] << 8)
             | data[offset + 3];
    }

    private static int ReadUInt16BE(byte[] data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }

    private static void WriteUInt32BE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)((value >> 24) & 0xFF);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    private static void WriteUInt16BE(byte[] data, int offset, int value)
    {
        data[offset] = (byte)((value >> 8) & 0xFF);
        data[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteUInt32BE(MemoryStream ms, uint value)
    {
        ms.WriteByte((byte)((value >> 24) & 0xFF));
        ms.WriteByte((byte)((value >> 16) & 0xFF));
        ms.WriteByte((byte)((value >> 8) & 0xFF));
        ms.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteUInt16BE(MemoryStream ms, int value)
    {
        ms.WriteByte((byte)((value >> 8) & 0xFF));
        ms.WriteByte((byte)(value & 0xFF));
    }

    private static string TagToString(uint tag)
    {
        return new string(new[]
        {
            (char)((tag >> 24) & 0xFF),
            (char)((tag >> 16) & 0xFF),
            (char)((tag >> 8) & 0xFF),
            (char)(tag & 0xFF),
        });
    }
}
