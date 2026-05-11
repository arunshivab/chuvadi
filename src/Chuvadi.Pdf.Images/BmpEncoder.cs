// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  BMP file format — BITMAPFILEHEADER + BITMAPINFOHEADER (DIB)
//        Windows BMP v3 — 24-bit or 32-bit BGR/BGRA pixel rows, top-down
// PHASE: Phase 2 — Chuvadi.Pdf.Images
// Encodes an ImageFrame to a Windows BMP file.

using System;
using System.IO;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Encodes an <see cref="ImageFrame"/> to Windows BMP format.
/// </summary>
/// <remarks>
/// Writes a 24-bit BGR BMP (no alpha) by default, or 32-bit BGRA when
/// includeAlpha is true. The BMP pixel rows are stored top-down.
/// Row padding is applied to 4-byte alignment.
/// BMP v3 (BITMAPINFOHEADER) — no compression, no colour table.
/// </remarks>
public static class BmpEncoder
{
    /// <summary>
    /// Encodes an <see cref="ImageFrame"/> to BMP and writes it to
    /// <paramref name="output"/>.
    /// </summary>
    /// <param name="frame">The image to encode.</param>
    /// <param name="output">The stream to write the BMP to. Must be writable.</param>
    /// <param name="includeAlpha">
    /// True to write 32-bit BGRA; false to write 24-bit BGR.
    /// </param>
    public static void Encode(ImageFrame frame, Stream output, bool includeAlpha = false)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        int width = frame.Width;
        int height = frame.Height;
        int bytesPerPixel = includeAlpha ? 4 : 3;
        int rowStride = (width * bytesPerPixel + 3) & ~3;
        int pixelDataSize = rowStride * height;
        int fileHeaderSize = 14;
        int dibHeaderSize = 40;
        int pixelDataOffset = fileHeaderSize + dibHeaderSize;
        int fileSize = pixelDataOffset + pixelDataSize;

        // BITMAPFILEHEADER
        output.WriteByte((byte)'B');
        output.WriteByte((byte)'M');
        WriteInt32Le(output, fileSize);
        WriteInt16Le(output, 0);
        WriteInt16Le(output, 0);
        WriteInt32Le(output, pixelDataOffset);

        // BITMAPINFOHEADER
        WriteInt32Le(output, dibHeaderSize);
        WriteInt32Le(output, width);
        WriteInt32Le(output, -height); // negative = top-down
        WriteInt16Le(output, 1);
        WriteInt16Le(output, (short)(bytesPerPixel * 8));
        WriteInt32Le(output, 0);            // BI_RGB
        WriteInt32Le(output, pixelDataSize);
        WriteInt32Le(output, 2835);
        WriteInt32Le(output, 2835);
        WriteInt32Le(output, 0);
        WriteInt32Le(output, 0);

        // Pixel data
        byte[] rowBuffer = new byte[rowStride];

        for (int y = 0; y < height; y++)
        {
            System.ReadOnlySpan<byte> srcRow = frame.Pixels.GetRow(y);

            for (int x = 0; x < width; x++)
            {
                int srcOff = x * 4;
                int dstOff = x * bytesPerPixel;
                rowBuffer[dstOff]     = srcRow[srcOff];     // B
                rowBuffer[dstOff + 1] = srcRow[srcOff + 1]; // G
                rowBuffer[dstOff + 2] = srcRow[srcOff + 2]; // R

                if (includeAlpha)
                {
                    rowBuffer[dstOff + 3] = srcRow[srcOff + 3]; // A
                }
            }

            for (int i = width * bytesPerPixel; i < rowStride; i++)
            {
                rowBuffer[i] = 0;
            }

            output.Write(rowBuffer, 0, rowStride);
        }
    }

    private static void WriteInt32Le(Stream s, int value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
        s.WriteByte((byte)((value >> 16) & 0xFF));
        s.WriteByte((byte)((value >> 24) & 0xFF));
    }

    private static void WriteInt16Le(Stream s, short value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
    }
}
