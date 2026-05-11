// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  TIFF 6.0 specification (Aldus / Adobe, June 1992)
// PHASE: Phase 1.1.9 — Chuvadi.Pdf.Images TIFF support
//
// Baseline TIFF 6.0 writer. Writes:
//   - Little-endian byte order ("II")
//   - 8-bit per channel
//   - RGB photometric (2)
//   - PackBits compression (32773) — broadly supported, simple to implement
//   - Single strip per page covering the full image
//   - Multi-page: each frame becomes one IFD chained via NextIFDOffset

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Encodes one or more <see cref="ImageFrame"/> objects to a baseline TIFF 6.0
/// byte stream.
/// </summary>
/// <remarks>
/// Output format:
/// - Little-endian.
/// - 8 bits per sample, 3 samples per pixel (RGB photometric).
/// - PackBits compression.
/// - Single strip per page.
///
/// Multi-frame inputs produce a multi-page TIFF.
/// </remarks>
public static class TiffEncoder
{
    /// <summary>Encodes a single image frame to a TIFF byte stream.</summary>
    public static byte[] Encode(ImageFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        return EncodeAll(new[] { frame });
    }

    /// <summary>Encodes a sequence of image frames to a multi-page TIFF byte stream.</summary>
    public static byte[] EncodeAll(IEnumerable<ImageFrame> frames)
    {
        if (frames is null)
        {
            throw new ArgumentNullException(nameof(frames));
        }

        List<ImageFrame> list = new List<ImageFrame>(frames);

        if (list.Count == 0)
        {
            throw new TiffException("EncodeAll requires at least one frame.");
        }

        using MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);

        // Header: II, 42, offset of first IFD (placeholder, will patch)
        w.Write((byte)'I');
        w.Write((byte)'I');
        WriteU16(w, 42);
        long firstIfdOffsetPos = ms.Position;
        WriteU32(w, 0);

        // For each frame:
        // 1. Write the compressed strip bytes.
        // 2. Write the IFD with a NextIFD pointer that we patch when writing the
        //    following frame's IFD, or to 0 at the end.
        long previousNextIfdPos = firstIfdOffsetPos;

        for (int i = 0; i < list.Count; i++)
        {
            ImageFrame frame = list[i];
            int width = frame.Width;
            int height = frame.Height;

            // Build RGB bytes from the BGRA pixel buffer.
            byte[] rgb = new byte[width * height * 3];
            ReadOnlySpan<byte> pixels = frame.Pixels.Pixels;
            int dst = 0;

            for (int p = 0; p < pixels.Length; p += 4)
            {
                rgb[dst++] = pixels[p + 2]; // R
                rgb[dst++] = pixels[p + 1]; // G
                rgb[dst++] = pixels[p];     // B
            }

            byte[] compressed = PackBitsCompress(rgb);

            // Write strip data at current position
            long stripOffset = ms.Position;
            w.Write(compressed);

            // Patch the previous "next IFD" pointer to point at the IFD we're about to write
            long ifdOffset = ms.Position;
            long endOfStripData = ifdOffset;

            // Pad to even boundary (TIFF convention)
            if (ifdOffset % 2 != 0)
            {
                w.Write((byte)0);
                ifdOffset++;
            }

            long savedPos = ms.Position;
            ms.Position = previousNextIfdPos;
            WriteU32(w, (uint)ifdOffset);
            ms.Position = savedPos;

            // Build IFD entries (12 entries — tags below)
            ushort numEntries = 12;
            WriteU16(w, numEntries);

            // We need an offset for BitsPerSample (3 SHORTs = 6 bytes > 4, so it goes external)
            // Compute its offset after the IFD body.
            long ifdBodyEnd = ms.Position + (numEntries * 12) + 4;
            long bitsPerSampleOffset = ifdBodyEnd;

            // Write entries (tag, type, count, value-or-offset)
            WriteEntry(w, 256, 4, 1, (uint)width);                // ImageWidth (LONG)
            WriteEntry(w, 257, 4, 1, (uint)height);               // ImageLength (LONG)
            WriteEntry(w, 258, 3, 3, (uint)bitsPerSampleOffset);  // BitsPerSample → external
            WriteEntry(w, 259, 3, 1, 32773);                       // Compression: PackBits
            WriteEntry(w, 262, 3, 1, 2);                           // Photometric: RGB
            WriteEntry(w, 273, 4, 1, (uint)stripOffset);          // StripOffsets
            WriteEntry(w, 277, 3, 1, 3);                           // SamplesPerPixel: 3
            WriteEntry(w, 278, 4, 1, (uint)height);               // RowsPerStrip
            WriteEntry(w, 279, 4, 1, (uint)compressed.Length);    // StripByteCounts
            WriteEntry(w, 282, 5, 1, (uint)(ifdBodyEnd + 6));     // XResolution → external
            WriteEntry(w, 283, 5, 1, (uint)(ifdBodyEnd + 14));    // YResolution → external
            WriteEntry(w, 296, 3, 1, 2);                           // ResolutionUnit: inch

            // NextIFDOffset: 0 for now, will be patched on next iteration
            previousNextIfdPos = ms.Position;
            WriteU32(w, 0);

            // External value for BitsPerSample (three SHORTs = 6 bytes)
            WriteU16(w, 8);
            WriteU16(w, 8);
            WriteU16(w, 8);

            // XResolution (RATIONAL: 72/1)
            WriteU32(w, 72);
            WriteU32(w, 1);

            // YResolution (RATIONAL: 72/1)
            WriteU32(w, 72);
            WriteU32(w, 1);

            // (Strip data was already written before the IFD. endOfStripData is unused below.)
            _ = endOfStripData;
        }

        return ms.ToArray();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void WriteEntry(BinaryWriter w, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        WriteU16(w, tag);
        WriteU16(w, type);
        WriteU32(w, count);
        WriteU32(w, valueOrOffset);
    }

    private static void WriteU16(BinaryWriter w, ushort v)
    {
        w.Write((byte)(v & 0xFF));
        w.Write((byte)((v >> 8) & 0xFF));
    }

    private static void WriteU32(BinaryWriter w, uint v)
    {
        w.Write((byte)(v & 0xFF));
        w.Write((byte)((v >> 8) & 0xFF));
        w.Write((byte)((v >> 16) & 0xFF));
        w.Write((byte)((v >> 24) & 0xFF));
    }

    private static byte[] PackBitsCompress(byte[] input)
    {
        // PackBits encoder. Walks the input emitting runs (>=2 identical bytes)
        // as (-(n-1), byte) and literal sequences as (n-1, bytes...).
        // Max run length per packet: 128.
        List<byte> output = new List<byte>(input.Length);
        int i = 0;

        while (i < input.Length)
        {
            // Try to find a run
            int runStart = i;
            int runByte = input[i];
            int runLen = 1;

            while (i + runLen < input.Length && input[i + runLen] == runByte && runLen < 128)
            {
                runLen++;
            }

            if (runLen >= 3)
            {
                // Encode as a run
                output.Add((byte)(sbyte)(-(runLen - 1)));
                output.Add((byte)runByte);
                i += runLen;
            }
            else
            {
                // Literal run — collect bytes until a run-of-3 starts or we hit 128
                int litStart = i;
                int litLen = 0;

                while (i < input.Length && litLen < 128)
                {
                    // Check if a run of 3 starts here
                    if (i + 2 < input.Length &&
                        input[i] == input[i + 1] && input[i + 1] == input[i + 2])
                    {
                        break;
                    }

                    i++;
                    litLen++;
                }

                output.Add((byte)(sbyte)(litLen - 1));
                for (int k = 0; k < litLen; k++)
                {
                    output.Add(input[litStart + k]);
                }
            }
        }

        return output.ToArray();
    }
}
