// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — ICC color conversion

using System;

namespace Chuvadi.Pdf.Color;

/// <summary>
/// Static color-conversion helpers. Default path uses the pure-math
/// CMYK→sRGB approximation; pass an <see cref="IccProfile"/> for
/// ICC-accurate conversion.
/// </summary>
public static class ColorConversion
{
    /// <summary>Converts a single CMYK pixel to sRGB using the math approximation.</summary>
    public static (byte R, byte G, byte B) CmykToSrgb(byte c, byte m, byte y, byte k)
    {
        double r = (1 - c / 255.0) * (1 - k / 255.0);
        double g = (1 - m / 255.0) * (1 - k / 255.0);
        double b = (1 - y / 255.0) * (1 - k / 255.0);
        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    /// <summary>Converts an interleaved CMYK pixel buffer to interleaved RGB in-place.</summary>
    public static void CmykToSrgb(ReadOnlySpan<byte> cmyk, Span<byte> rgb)
    {
        if (cmyk.Length % 4 != 0)
        {
            throw new ArgumentException("CMYK buffer must be a multiple of 4 bytes.", nameof(cmyk));
        }
        int pixels = cmyk.Length / 4;
        if (rgb.Length < pixels * 3)
        {
            throw new ArgumentException("RGB buffer too small.", nameof(rgb));
        }
        for (int i = 0; i < pixels; i++)
        {
            (byte r, byte g, byte b) = CmykToSrgb(cmyk[i * 4], cmyk[i * 4 + 1], cmyk[i * 4 + 2], cmyk[i * 4 + 3]);
            rgb[i * 3] = r;
            rgb[i * 3 + 1] = g;
            rgb[i * 3 + 2] = b;
        }
    }

    /// <summary>Converts an interleaved CMYK buffer to RGB using an ICC profile.</summary>
    public static void CmykToSrgb(IccProfile profile, ReadOnlySpan<byte> cmyk, Span<byte> rgb)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (cmyk.Length % 4 != 0)
        {
            throw new ArgumentException("CMYK buffer must be a multiple of 4 bytes.", nameof(cmyk));
        }
        int pixels = cmyk.Length / 4;
        if (rgb.Length < pixels * 3)
        {
            throw new ArgumentException("RGB buffer too small.", nameof(rgb));
        }
        double[] input = new double[4];
        for (int i = 0; i < pixels; i++)
        {
            input[0] = cmyk[i * 4] / 255.0;
            input[1] = cmyk[i * 4 + 1] / 255.0;
            input[2] = cmyk[i * 4 + 2] / 255.0;
            input[3] = cmyk[i * 4 + 3] / 255.0;
            (double r, double g, double b) = profile.ToSrgb(input);
            rgb[i * 3] = (byte)Math.Clamp(r * 255, 0, 255);
            rgb[i * 3 + 1] = (byte)Math.Clamp(g * 255, 0, 255);
            rgb[i * 3 + 2] = (byte)Math.Clamp(b * 255, 0, 255);
        }
    }
}
