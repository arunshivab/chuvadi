// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  TIFF 6.0 §17 "Separated images" — Photometric 5, InkSet 1 (CMYK).
// PHASE: Phase 1.1.8 — Chuvadi.Pdf.Images CMYK TIFF output
//
// CMYK TIFF writer. Same structure as the RGB TiffEncoder but with 4
// SamplesPerPixel and Photometric=5. PackBits compression.

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Encodes <see cref="CmykImage"/> objects to a baseline TIFF 6.0 byte stream
/// with CMYK photometric interpretation (5).
/// </summary>
public static class CmykTiffEncoder
{
    /// <summary>Encodes a single CMYK image to a TIFF byte stream.</summary>
    public static byte[] Encode(CmykImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return EncodeAll(new[] { image });
    }

    /// <summary>Encodes a sequence of CMYK images to a multi-page TIFF.</summary>
    public static byte[] EncodeAll(IEnumerable<CmykImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        List<CmykImage> list = new List<CmykImage>(images);

        if (list.Count == 0)
        {
            throw new TiffException("EncodeAll requires at least one image.");
        }

        using MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);

        // Header
        w.Write((byte)'I');
        w.Write((byte)'I');
        WriteU16(w, 42);
        long firstIfdOffsetPos = ms.Position;
        WriteU32(w, 0);

        long previousNextIfdPos = firstIfdOffsetPos;

        foreach (CmykImage img in list)
        {
            int width = img.Width;
            int height = img.Height;

            // Compress the CMYK byte stream with PackBits
            byte[] compressed = PackBitsCompress(img.Pixels.ToArray());

            long stripOffset = ms.Position;
            w.Write(compressed);

            long ifdOffset = ms.Position;

            // Even-boundary alignment
            if (ifdOffset % 2 != 0)
            {
                w.Write((byte)0);
                ifdOffset++;
            }

            long savedPos = ms.Position;
            ms.Position = previousNextIfdPos;
            WriteU32(w, (uint)ifdOffset);
            ms.Position = savedPos;

            // IFD: 13 entries (12 from RGB + InkSet)
            ushort numEntries = 13;
            WriteU16(w, numEntries);

            long ifdBodyEnd = ms.Position + (numEntries * 12) + 4;
            long bitsPerSampleOffset = ifdBodyEnd;

            WriteEntry(w, 256, 4, 1, (uint)width);                       // ImageWidth
            WriteEntry(w, 257, 4, 1, (uint)height);                      // ImageLength
            WriteEntry(w, 258, 3, 4, (uint)bitsPerSampleOffset);         // BitsPerSample (4 SHORTs → external)
            WriteEntry(w, 259, 3, 1, 32773);                              // Compression: PackBits
            WriteEntry(w, 262, 3, 1, 5);                                  // Photometric: Separated (CMYK)
            WriteEntry(w, 273, 4, 1, (uint)stripOffset);                 // StripOffsets
            WriteEntry(w, 277, 3, 1, 4);                                  // SamplesPerPixel: 4 (C, M, Y, K)
            WriteEntry(w, 278, 4, 1, (uint)height);                      // RowsPerStrip
            WriteEntry(w, 279, 4, 1, (uint)compressed.Length);           // StripByteCounts
            WriteEntry(w, 282, 5, 1, (uint)(ifdBodyEnd + 8));            // XResolution → external
            WriteEntry(w, 283, 5, 1, (uint)(ifdBodyEnd + 16));           // YResolution → external
            WriteEntry(w, 296, 3, 1, 2);                                  // ResolutionUnit: inch
            WriteEntry(w, 332, 3, 1, 1);                                  // InkSet: CMYK

            previousNextIfdPos = ms.Position;
            WriteU32(w, 0);

            // BitsPerSample (4 × SHORT)
            WriteU16(w, 8);
            WriteU16(w, 8);
            WriteU16(w, 8);
            WriteU16(w, 8);

            // XResolution (RATIONAL 72/1)
            WriteU32(w, 72);
            WriteU32(w, 1);

            // YResolution
            WriteU32(w, 72);
            WriteU32(w, 1);
        }

        return ms.ToArray();
    }

    // ── Helpers (clones from TiffEncoder; kept local to avoid coupling) ───

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
        List<byte> output = new List<byte>(input.Length);
        int i = 0;

        while (i < input.Length)
        {
            int runByte = input[i];
            int runLen = 1;

            while (i + runLen < input.Length && input[i + runLen] == runByte && runLen < 128)
            {
                runLen++;
            }

            if (runLen >= 3)
            {
                output.Add((byte)(sbyte)(-(runLen - 1)));
                output.Add((byte)runByte);
                i += runLen;
            }
            else
            {
                int litStart = i;
                int litLen = 0;

                while (i < input.Length && litLen < 128)
                {
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
