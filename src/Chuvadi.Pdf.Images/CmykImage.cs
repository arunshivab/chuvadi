// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Standard naive RGB→CMYK conversion. Not a colour-managed transform —
//        for print-accurate output, use an ICC-based transform externally.
// PHASE: Phase 1.1.8 — Chuvadi.Pdf.Images CMYK output
//
// A CMYK pixel buffer + RGB→CMYK conversion. Used as an intermediate for
// rasterizing PDF pages to CMYK TIFFs, the common requirement for press
// integrations.

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// A planar CMYK 8-bit-per-channel image.
/// </summary>
/// <remarks>
/// Stores four bytes per pixel in C, M, Y, K order. The conversion from BGRA
/// is the standard subtractive formula; it does NOT apply ICC colour management.
/// For print-accurate output, layer an ICC transform on top of this buffer
/// (e.g., via Little CMS or your raster image processor).
/// </remarks>
public sealed class CmykImage
{
    private readonly byte[] _pixels;

    /// <summary>Initialises a new CMYK image with all channels zeroed (white in subtractive).</summary>
    public CmykImage(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Width = width;
        Height = height;
        _pixels = new byte[width * height * 4];
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Total byte count (Width × Height × 4).</summary>
    public int ByteCount => _pixels.Length;

    /// <summary>Row stride in bytes (Width × 4).</summary>
    public int Stride => Width * 4;

    /// <summary>Raw CMYK pixel bytes (C, M, Y, K interleaved per pixel).</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>Sets a single pixel's C, M, Y, K components (0..255 each).</summary>
    public void SetPixel(int x, int y, byte c, byte m, byte yel, byte k)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        int idx = ((y * Width) + x) * 4;
        _pixels[idx] = c;
        _pixels[idx + 1] = m;
        _pixels[idx + 2] = yel;
        _pixels[idx + 3] = k;
    }

    /// <summary>
    /// Creates a <see cref="CmykImage"/> from a BGRA <see cref="PixelBuffer"/> using
    /// the standard subtractive RGB→CMYK conversion.
    /// </summary>
    public static CmykImage FromBgra(PixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);

        CmykImage cmyk = new(source.Width, source.Height);
        ReadOnlySpan<byte> src = source.Pixels;

        for (int y = 0; y < source.Height; y++)
        {
            int rowStart = y * source.Stride;

            for (int x = 0; x < source.Width; x++)
            {
                int p = rowStart + (x * 4);
                byte bb = src[p];
                byte gg = src[p + 1];
                byte rr = src[p + 2];
                // alpha at src[p + 3] — flattened against background by caller

                double rN = rr / 255.0;
                double gN = gg / 255.0;
                double bN = bb / 255.0;

                double maxC = Math.Max(rN, Math.Max(gN, bN));
                double k = 1.0 - maxC;

                double c, m, yC;
                if (k >= 1.0 - 1e-9)
                {
                    c = m = yC = 0.0;
                }
                else
                {
                    double inv = 1.0 - k;
                    c = (1.0 - rN - k) / inv;
                    m = (1.0 - gN - k) / inv;
                    yC = (1.0 - bN - k) / inv;
                }

                cmyk.SetPixel(
                    x, y,
                    (byte)Math.Round(c * 255.0),
                    (byte)Math.Round(m * 255.0),
                    (byte)Math.Round(yC * 255.0),
                    (byte)Math.Round(k * 255.0));
            }
        }

        return cmyk;
    }
}
