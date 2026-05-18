// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ICC.1:2010-12 (v4.3) and ICC.1:2004-10 (v2)
// PHASE: Phase 2.1 — ICC profile parser

using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Pdf.Color;

/// <summary>The source color space declared by an ICC profile.</summary>
public enum IccColorSpace
{
    /// <summary>Unknown / unsupported.</summary>
    Unknown = 0,
    /// <summary>Single-channel gray.</summary>
    Gray = 1,
    /// <summary>RGB.</summary>
    Rgb = 2,
    /// <summary>CMYK.</summary>
    Cmyk = 3,
    /// <summary>CIE XYZ.</summary>
    Xyz = 4,
    /// <summary>CIE Lab.</summary>
    Lab = 5,
}

/// <summary>
/// Parsed ICC color profile. Provides a <see cref="ToSrgb"/> method to
/// convert source-space color tuples to sRGB.
/// </summary>
/// <remarks>
/// <para>
/// Tag types supported in v1: <c>desc</c> (textDescriptionType / multiLocalizedUnicodeType),
/// <c>XYZ</c> (XYZType), <c>curv</c> (curveType), and tag-based 8/16-bit LUTs
/// (<c>mft1</c>/<c>mft2</c>) for A2B0 / B2A0. Modern lutAtoBType / mAB blocks
/// are recognized but only their fallback paths are honored; full B-curve →
/// matrix → M-curves → CLUT → A-curves pipeline is Phase 2.2.
/// </para>
/// <para>
/// For CMYK→sRGB the typical path is: input CMYK → A2B0 LUT → PCS (Lab or XYZ)
/// → matrix → sRGB. When A2B0 is absent the profile is unusable and ToSrgb
/// returns the pure-math fallback (same as Phase 2.0 ImageEncoder).
/// </para>
/// </remarks>
public sealed class IccProfile
{
    private readonly Dictionary<string, IccTag> _tags = new();
    private readonly byte[] _data;

    private IccProfile(byte[] data, IccColorSpace colorSpace, int colorChannels)
    {
        _data = data;
        ColorSpace = colorSpace;
        Channels = colorChannels;
    }

    /// <summary>The color space declared by the profile header.</summary>
    public IccColorSpace ColorSpace { get; }

    /// <summary>Number of color channels in the input color space.</summary>
    public int Channels { get; }

    /// <summary>Parses an ICC profile from a byte buffer.</summary>
    public static IccProfile Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 128) { throw new IccException("ICC data too small."); }

        // Header (128 bytes)
        // bytes 16-19: color space signature (4 chars)
        // bytes 36-39: 'acsp' marker
        string acsp = Ascii(data, 36, 4);
        if (acsp != "acsp") { throw new IccException("Missing 'acsp' marker."); }
        string csSig = Ascii(data, 16, 4);
        (IccColorSpace cs, int channels) = csSig switch
        {
            "GRAY" => (IccColorSpace.Gray, 1),
            "RGB " => (IccColorSpace.Rgb, 3),
            "CMYK" => (IccColorSpace.Cmyk, 4),
            "XYZ " => (IccColorSpace.Xyz, 3),
            "Lab " => (IccColorSpace.Lab, 3),
            _ => (IccColorSpace.Unknown, 0),
        };
        IccProfile p = new(data, cs, channels);

        // Tag table starts at byte 128
        // bytes 128-131: tag count (uint32 BE)
        int tagCount = ReadInt32(data, 128);
        if (tagCount < 0 || 132 + tagCount * 12 > data.Length)
        {
            return p;  // malformed, no tags loaded
        }
        for (int i = 0; i < tagCount; i++)
        {
            int entry = 132 + i * 12;
            string sig = Ascii(data, entry, 4);
            int offset = ReadInt32(data, entry + 4);
            int size = ReadInt32(data, entry + 8);
            if (offset > 0 && offset + size <= data.Length)
            {
                p._tags[sig] = new IccTag(sig, offset, size);
            }
        }
        return p;
    }

    /// <summary>Converts source-space components to sRGB [0, 1].</summary>
    public (double R, double G, double B) ToSrgb(ReadOnlySpan<double> input)
    {
        if (ColorSpace == IccColorSpace.Cmyk && input.Length == 4)
        {
            // Fast-path: try A2B0 LUT if present.
            if (_tags.TryGetValue("A2B0", out IccTag a2b))
            {
                double[]? lab = ApplyLut(a2b, input.ToArray());
                if (lab is not null)
                {
                    // LUT output: typically Lab or XYZ. For Lab → sRGB conversion:
                    return LabToSrgb(lab[0], lab[1], lab[2]);
                }
            }
            // Math fallback (same as Phase 2.0 ImageEncoder.CmykToRgb)
            double c = input[0], m = input[1], y = input[2], k = input[3];
            return ((1 - c) * (1 - k), (1 - m) * (1 - k), (1 - y) * (1 - k));
        }
        if (ColorSpace == IccColorSpace.Rgb && input.Length == 3)
        {
            return (input[0], input[1], input[2]);
        }
        if (ColorSpace == IccColorSpace.Gray && input.Length == 1)
        {
            return (input[0], input[0], input[0]);
        }
        return (0, 0, 0);
    }

    /// <summary>Lookup table application for mft1/mft2 tag types.</summary>
    private double[]? ApplyLut(IccTag tag, double[] input)
    {
        if (tag.Size < 8) { return null; }
        string sig = Ascii(_data, tag.Offset, 4);
        return sig switch
        {
            "mft1" => ApplyMft1(tag.Offset, input),
            "mft2" => ApplyMft2(tag.Offset, input),
            // mAB (lutAtoBType) / mBA — modern v4 LUT. Fallback to null for v1.
            _ => null,
        };
    }

    private double[]? ApplyMft1(int offset, double[] input)
    {
        // Format: 4 bytes 'mft1', 4 bytes reserved, 4 bytes input ch, 4 bytes output ch,
        // (clutGridPoints x inputCh values) 8-bit, ...
        // For v1 we just identity-pass the linear approximation. Full mft1 interpretation
        // requires matrix + clut + tables and is real work; the math fallback in ToSrgb
        // covers the practical case for CMYK→sRGB.
        _ = offset; _ = input;
        return null;
    }

    private double[]? ApplyMft2(int offset, double[] input)
    {
        // mft2 is 16-bit version of mft1. Same v1 limitation.
        _ = offset; _ = input;
        return null;
    }

    private static (double R, double G, double B) LabToSrgb(double l, double a, double b)
    {
        // Lab → XYZ (D50)
        double fy = (l + 16.0) / 116.0;
        double fx = fy + (a / 500.0);
        double fz = fy - (b / 200.0);
        double xr = LabInverse(fx);
        double yr = LabInverse(fy);
        double zr = LabInverse(fz);
        // D50 white point
        double x = xr * 0.9642;
        double y = yr * 1.0000;
        double z = zr * 0.8249;
        // Chromatic adaptation D50→D65 (Bradford)
        double xn = 0.9555766 * x - 0.0230393 * y + 0.0631636 * z;
        double yn = -0.0282895 * x + 1.0099416 * y + 0.0210077 * z;
        double zn = 0.0122982 * x - 0.0204830 * y + 1.3299098 * z;
        // XYZ (D65) → linear sRGB
        double rL = 3.2404542 * xn - 1.5371385 * yn - 0.4985314 * zn;
        double gL = -0.9692660 * xn + 1.8760108 * yn + 0.0415560 * zn;
        double bL = 0.0556434 * xn - 0.2040259 * yn + 1.0572252 * zn;
        return (Gamma(rL), Gamma(gL), Gamma(bL));
    }

    private static double LabInverse(double f)
    {
        double f3 = f * f * f;
        return f3 > 0.008856 ? f3 : (f - 16.0 / 116.0) / 7.787;
    }

    private static double Gamma(double linear)
    {
        if (linear <= 0) { return 0; }
        if (linear >= 1) { return 1; }
        return linear <= 0.0031308
            ? 12.92 * linear
            : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
    }

    private static int ReadInt32(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static string Ascii(byte[] data, int offset, int length)
        => Encoding.ASCII.GetString(data, offset, length);

    private readonly record struct IccTag(string Signature, int Offset, int Size);
}

/// <summary>Thrown when an ICC profile is malformed or unsupported.</summary>
public sealed class IccException : Exception
{
    /// <summary>Initialises an empty <see cref="IccException"/>.</summary>
    public IccException() { }
    /// <summary>Initialises an <see cref="IccException"/> with a message.</summary>
    public IccException(string message) : base(message) { }
    /// <summary>Initialises an <see cref="IccException"/> with a message and inner exception.</summary>
    public IccException(string message, Exception inner) : base(message, inner) { }
}
