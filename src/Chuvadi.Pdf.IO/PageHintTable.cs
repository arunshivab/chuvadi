// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1:2008 §F.3 — Page offset hint table
// PHASE: Phase 1.1.6 — Chuvadi.Pdf.IO linearization
//
// Encodes and decodes the primary (page offset) hint stream that a linearized
// PDF places between the catalog and the page-2-onwards section.

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Per-page information used to build or parse the primary hint stream.
/// </summary>
internal sealed class PageHintEntry
{
    public int ObjectCount { get; set; }
    public int LengthInBytes { get; set; }
    public int ContentStreamOffset { get; set; }
    public int ContentStreamLength { get; set; }
    public List<int> SharedObjectIdentifiers { get; set; } = new();
    public List<int> SharedObjectNumerators { get; set; } = new();
}

/// <summary>
/// Encoder/decoder for the primary "page offset" hint stream
/// (ISO 32000-1 §F.3).
/// </summary>
internal static class PageHintTable
{
    /// <summary>
    /// Encodes a primary hint stream from a list of <see cref="PageHintEntry"/>
    /// (one per page including page 1).
    /// </summary>
    /// <param name="pages">Per-page hint data.</param>
    /// <param name="firstPageObjectNumber">/O entry value.</param>
    /// <returns>Raw hint stream bytes ready to be wrapped in a PdfStream.</returns>
    public static byte[] Encode(List<PageHintEntry> pages, int firstPageObjectNumber)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count == 0)
        {
            return Array.Empty<byte>();
        }

        // Compute minima and bit widths across the whole table.
        int minObjects = int.MaxValue;
        int maxObjects = 0;
        int minLength = int.MaxValue;
        int maxLength = 0;
        int minContentOffset = int.MaxValue;
        int maxContentOffset = 0;
        int minContentLength = int.MaxValue;
        int maxContentLength = 0;
        int maxSharedIds = 0;

        foreach (PageHintEntry p in pages)
        {
            if (p.ObjectCount < minObjects) { minObjects = p.ObjectCount; }
            if (p.ObjectCount > maxObjects) { maxObjects = p.ObjectCount; }
            if (p.LengthInBytes < minLength) { minLength = p.LengthInBytes; }
            if (p.LengthInBytes > maxLength) { maxLength = p.LengthInBytes; }
            if (p.ContentStreamOffset < minContentOffset) { minContentOffset = p.ContentStreamOffset; }
            if (p.ContentStreamOffset > maxContentOffset) { maxContentOffset = p.ContentStreamOffset; }
            if (p.ContentStreamLength < minContentLength) { minContentLength = p.ContentStreamLength; }
            if (p.ContentStreamLength > maxContentLength) { maxContentLength = p.ContentStreamLength; }
            if (p.SharedObjectIdentifiers.Count > maxSharedIds) { maxSharedIds = p.SharedObjectIdentifiers.Count; }
        }

        int bitsObjects = BitsNeeded(maxObjects - minObjects);
        int bitsLength = BitsNeeded(maxLength - minLength);
        int bitsContentOffset = BitsNeeded(maxContentOffset - minContentOffset);
        int bitsContentLength = BitsNeeded(maxContentLength - minContentLength);
        int bitsSharedCount = BitsNeeded(maxSharedIds);

        // ── Header (17 fields per spec, but we only use what we emit) ─────
        using MemoryStream headerStream = new();
        BinaryWriter hw = new BinaryWriter(headerStream);

        WriteUInt32(hw, (uint)firstPageObjectNumber);  // Item 1: first-page object number
        WriteUInt32(hw, 0);                             // Item 2: location of first page (filled by caller later)
        WriteUInt32(hw, (uint)bitsObjects);             // Item 3: bits for object count delta
        WriteUInt32(hw, (uint)minLength);               // Item 4: least page length
        WriteUInt32(hw, (uint)bitsLength);              // Item 5: bits for page-length delta
        WriteUInt32(hw, (uint)minContentOffset);        // Item 6: least content-stream offset
        WriteUInt32(hw, (uint)bitsContentOffset);       // Item 7: bits for content-offset delta
        WriteUInt32(hw, (uint)minContentLength);        // Item 8: least content-stream length
        WriteUInt32(hw, (uint)bitsContentLength);       // Item 9: bits for content-length delta
        WriteUInt32(hw, (uint)bitsSharedCount);         // Item 10: bits for shared-object count
        WriteUInt32(hw, 0);                             // Item 11: bits for shared identifier (we emit 0 — no shared objects)
        WriteUInt32(hw, 0);                             // Item 12: bits for shared numerator
        WriteUInt32(hw, 16);                            // Item 13: shared denominator (fixed)

        // ── Per-page entries (bit-packed) ─────────────────────────────────
        BitWriter bw = new BitWriter();

        foreach (PageHintEntry p in pages)
        {
            bw.WriteBits((long)(p.ObjectCount - minObjects), bitsObjects);
            bw.WriteBits((long)(p.LengthInBytes - minLength), bitsLength);
            bw.WriteBits((long)p.SharedObjectIdentifiers.Count, bitsSharedCount);
            // Shared identifiers + numerators omitted (we declare 0 bits for them).
            bw.WriteBits((long)(p.ContentStreamOffset - minContentOffset), bitsContentOffset);
            bw.WriteBits((long)(p.ContentStreamLength - minContentLength), bitsContentLength);
        }

        byte[] packed = bw.ToArray();
        byte[] header = headerStream.ToArray();

        byte[] result = new byte[header.Length + packed.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(packed, 0, result, header.Length, packed.Length);
        return result;
    }

    /// <summary>
    /// Decodes a primary hint stream. Returns per-page entries with the values
    /// already adjusted by their minima.
    /// </summary>
    public static List<PageHintEntry> Decode(byte[] data, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (pageCount <= 0 || data.Length < 52)
        {
            return new List<PageHintEntry>();
        }

        // Parse header
        int p = 0;
        uint firstPageObjectNumber = ReadUInt32(data, ref p);
        _ = firstPageObjectNumber; // header-only, not needed for decoding
        uint locationOfFirstPage = ReadUInt32(data, ref p);
        _ = locationOfFirstPage;
        int bitsObjects = (int)ReadUInt32(data, ref p);
        int minLength = (int)ReadUInt32(data, ref p);
        int bitsLength = (int)ReadUInt32(data, ref p);
        int minContentOffset = (int)ReadUInt32(data, ref p);
        int bitsContentOffset = (int)ReadUInt32(data, ref p);
        int minContentLength = (int)ReadUInt32(data, ref p);
        int bitsContentLength = (int)ReadUInt32(data, ref p);
        int bitsSharedCount = (int)ReadUInt32(data, ref p);
        _ = ReadUInt32(data, ref p); // bits shared id
        _ = ReadUInt32(data, ref p); // bits shared num
        _ = ReadUInt32(data, ref p); // shared denominator

        // Bit-packed body starts at p
        byte[] body = new byte[data.Length - p];
        Buffer.BlockCopy(data, p, body, 0, body.Length);
        BitReader br = new BitReader(body);

        List<PageHintEntry> result = new(pageCount);

        for (int i = 0; i < pageCount; i++)
        {
            PageHintEntry entry = new();
            entry.ObjectCount = (int)br.ReadBits(bitsObjects);
            entry.LengthInBytes = (int)br.ReadBits(bitsLength) + minLength;
            int sharedCount = (int)br.ReadBits(bitsSharedCount);
            for (int s = 0; s < sharedCount; s++)
            {
                _ = sharedCount; // shared-id width is 0 in our encoder
            }
            entry.ContentStreamOffset = (int)br.ReadBits(bitsContentOffset) + minContentOffset;
            entry.ContentStreamLength = (int)br.ReadBits(bitsContentLength) + minContentLength;
            result.Add(entry);
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static int BitsNeeded(int range)
    {
        if (range <= 0)
        {
            return 1;
        }

        int bits = 0;
        while ((1L << bits) <= range)
        {
            bits++;
        }
        return Math.Max(bits, 1);
    }

    private static void WriteUInt32(BinaryWriter w, uint v)
    {
        // Big-endian
        w.Write((byte)((v >> 24) & 0xFF));
        w.Write((byte)((v >> 16) & 0xFF));
        w.Write((byte)((v >> 8) & 0xFF));
        w.Write((byte)(v & 0xFF));
    }

    private static uint ReadUInt32(byte[] data, ref int pos)
    {
        uint v = (uint)((data[pos] << 24) | (data[pos + 1] << 16) |
                        (data[pos + 2] << 8) | data[pos + 3]);
        pos += 4;
        return v;
    }
}
