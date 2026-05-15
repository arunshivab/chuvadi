// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe Photoshop CMYK conversion (naive — uses no ICC profile)
// PHASE: Phase 1.1.8 — Chuvadi.Pdf.Images CMYK output
//
// Converts a BGRA PixelBuffer into 8-bit-per-channel CMYK bytes for output
// to formats that expect process-colour data (e.g. print-bound TIFFs).

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Converts <see cref="PixelBuffer"/> BGRA data to packed CMYK 8 bits per channel.
/// </summary>
/// <remarks>
/// This is a naive, ICC-profile-free conversion suitable for previews and
/// non-colour-critical output. For colour-managed print workflows, run the
/// output through an ICC-aware converter (e.g. Little CMS) afterwards.
///
/// Formula (per Adobe Photoshop): for normalised RGB in [0,1]
///   K = 1 - max(R, G, B)
///   C = (1 - R - K) / (1 - K), 0 if K = 1
///   M = (1 - G - K) / (1 - K), 0 if K = 1
///   Y = (1 - B - K) / (1 - K), 0 if K = 1
/// Then scale each channel to [0, 255].
/// </remarks>
public static class CmykConverter
{
    /// <summary>
    /// Converts a BGRA pixel buffer to packed CMYK bytes (4 bytes per pixel,
    /// row-major, top-down).
    /// </summary>
    /// <param name="source">Source BGRA pixel buffer.</param>
    /// <returns>Packed CMYK output, exactly <c>Width × Height × 4</c> bytes.</returns>
    public static byte[] ToCmyk(PixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int width = source.Width;
        int height = source.Height;
        byte[] result = new byte[width * height * 4];

        ReadOnlySpan<byte> pixels = source.Pixels;
        int dst = 0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            // alpha at pixels[i + 3] is dropped; CMYK has no transparency

            // RGB -> CMY normalised to [0, 1]
            double rN = r / 255.0;
            double gN = g / 255.0;
            double bN = b / 255.0;

            double k = 1.0 - Math.Max(rN, Math.Max(gN, bN));
            double c, m, y;

            if (k >= 0.999)
            {
                c = m = y = 0.0;
                k = 1.0;
            }
            else
            {
                double inv = 1.0 - k;
                c = (1.0 - rN - k) / inv;
                m = (1.0 - gN - k) / inv;
                y = (1.0 - bN - k) / inv;
            }

            result[dst++] = (byte)Math.Clamp(c * 255.0, 0.0, 255.0);
            result[dst++] = (byte)Math.Clamp(m * 255.0, 0.0, 255.0);
            result[dst++] = (byte)Math.Clamp(y * 255.0, 0.0, 255.0);
            result[dst++] = (byte)Math.Clamp(k * 255.0, 0.0, 255.0);
        }

        return result;
    }

    /// <summary>
    /// Returns the four CMYK channels as a single <see cref="ImageFrame"/> tagged as
    /// <see cref="ImageColorFormat.Cmyk32"/>. The pixel buffer itself stays BGRA
    /// (a re-interpretation: B=C, G=M, R=Y, A=K) so downstream encoders can detect
    /// the format via <c>OriginalFormat</c> and emit the right photometric.
    /// </summary>
    public static ImageFrame ToCmykFrame(PixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);

        byte[] cmyk = ToCmyk(source);

        // Repack into a BGRA-shaped buffer: B=C, G=M, R=Y, A=K
        PixelBuffer buf = new PixelBuffer(source.Width, source.Height);

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int srcIdx = ((y * source.Width) + x) * 4;
                buf.SetPixelBgra(x, y,
                    cmyk[srcIdx],     // B slot = C
                    cmyk[srcIdx + 1], // G slot = M
                    cmyk[srcIdx + 2], // R slot = Y
                    cmyk[srcIdx + 3]); // A slot = K
            }
        }

        return new ImageFrame(buf, ImageColorFormat.Cmyk32);
    }
}
