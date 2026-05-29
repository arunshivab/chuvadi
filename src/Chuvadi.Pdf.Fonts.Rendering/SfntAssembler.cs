// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — Table Directory, head.checkSumAdjustment
//        https://docs.microsoft.com/typography/opentype/spec/otff
//        https://docs.microsoft.com/typography/opentype/spec/head
// PHASE: Phase 2.1 — v2.1.6
//        Extracted verbatim from TrueTypeFontPatch (v2.1.5) so the
//        CFF-to-OpenType wrapper (OpenTypeFontBuilder) can reuse SFNT
//        assembly. Pure extraction: byte-for-byte identical output to the
//        prior in-place SerializeFont implementation.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Assembles an SFNT-wrapped font program (TrueType/OpenType) from a set of
/// tables: builds the offset table and table directory, pads each table to a
/// 4-byte boundary, computes table checksums, and finalises
/// <c>head.checkSumAdjustment</c> per the OpenType specification.
/// </summary>
internal static class SfntAssembler
{
    /// <summary>A single SFNT table: its 4-byte tag and raw, unpadded body bytes.</summary>
    /// <param name="Tag">The big-endian table tag (e.g. <c>0x636D6170</c> for <c>cmap</c>).</param>
    /// <param name="Data">The table's raw, unpadded body bytes.</param>
    internal sealed record TableEntry(uint Tag, byte[] Data);

    /// <summary>
    /// Re-serialises a font from its tables, rebuilding the offset table and
    /// table directory and updating <c>head.checkSumAdjustment</c>. Tables are
    /// emitted in ascending tag order for deterministic output. The loca/glyf
    /// relationship is preserved because each table's bytes are copied
    /// unchanged; only the directory offsets are recomputed.
    /// </summary>
    /// <param name="sfVersion">The SFNT version tag (e.g. <c>0x00010000</c> or <c>0x4F54544F</c>).</param>
    /// <param name="tables">The tables to assemble. The list is sorted in place by tag.</param>
    /// <returns>The assembled font program bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tables"/> is null.</exception>
    /// <exception cref="FontRenderingException">Thrown when a present head table is too short.</exception>
    internal static byte[] Assemble(uint sfVersion, List<TableEntry> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        // Sort by tag for determinism. Preserving each table's bytes unchanged
        // keeps loca offsets (which are relative to glyf) valid.
        tables.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        int numTables = tables.Count;
        int dirSize = 12 + numTables * 16;

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

        // Write each table's data, recording its offset, padding to 4 bytes.
        int cursor = dirSize;
        int[] offsets = new int[numTables];
        for (int i = 0; i < numTables; i++)
        {
            offsets[i] = cursor;
            Array.Copy(tables[i].Data, 0, output, cursor, tables[i].Data.Length);
            cursor += AlignUp4(tables[i].Data.Length);
        }

        // Neutralise head.checkSumAdjustment BEFORE computing any checksums, so
        // the head table's own directory checksum is taken with the field
        // zeroed. This makes a second application of this method idempotent and
        // keeps the head directory checksum on-spec for any input whose
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

        // Whole-font checksum (adjustment still zero, directory now written):
        //   checkSumAdjustment = 0xB1B0AFBA - wholeFontChecksum
        if (headIdx >= 0)
        {
            int adjOffset = offsets[headIdx] + 8;
            uint totalSum = CalcTableChecksum(output, 0, output.Length);
            uint adjustment = unchecked(0xB1B0AFBAu - totalSum);
            WriteUInt32BE(output, adjOffset, adjustment);
        }

        return output;
    }

    /// <summary>Reads a big-endian unsigned 16-bit value as an int.</summary>
    /// <param name="data">The source buffer.</param>
    /// <param name="offset">The byte offset to read from.</param>
    /// <returns>The decoded value.</returns>
    internal static int ReadUInt16BE(byte[] data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }

    /// <summary>Reads a big-endian unsigned 32-bit value.</summary>
    /// <param name="data">The source buffer.</param>
    /// <param name="offset">The byte offset to read from.</param>
    /// <returns>The decoded value.</returns>
    internal static uint ReadUInt32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24)
             | ((uint)data[offset + 1] << 16)
             | ((uint)data[offset + 2] << 8)
             | data[offset + 3];
    }

    private static void WriteUInt16BE(byte[] data, int offset, int value)
    {
        data[offset] = (byte)((value >> 8) & 0xFF);
        data[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteUInt32BE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)((value >> 24) & 0xFF);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    private static int AlignUp4(int n) => (n + 3) & ~3;

    private static uint CalcTableChecksum(byte[] data, int offset, int length)
    {
        // Length is always a multiple of 4 here (padded). Unsigned overflow sum.
        uint sum = 0;
        int end = offset + length;
        for (int i = offset; i + 3 < end; i += 4)
        {
            sum = unchecked(sum + ReadUInt32BE(data, i));
        }

        return sum;
    }
}
