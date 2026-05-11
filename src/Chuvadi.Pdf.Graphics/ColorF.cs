// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6 — Colour spaces
//        PDF 32000-1:2008 §8.6.4 — Device colour spaces
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// Universal colour value covering Gray, RGB, and CMYK colour spaces.

using System;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// An immutable colour value, with support for DeviceGray, DeviceRGB,
/// and DeviceCMYK colour spaces.
/// All component values are in the range [0, 1].
/// PDF 32000-1:2008 §8.6.4 — Device colour spaces.
/// </summary>
public readonly struct ColorF : IEquatable<ColorF>
{
    // Packed storage: C0=R/Gray/C, C1=G/M, C2=B/Y, C3=A/K
    private readonly float _c0;
    private readonly float _c1;
    private readonly float _c2;
    private readonly float _c3;

    private ColorF(ColorSpace space, float c0, float c1, float c2, float c3)
    {
        Space = space;
        _c0 = c0;
        _c1 = c1;
        _c2 = c2;
        _c3 = c3;
    }

    /// <summary>Gets the colour space of this colour.</summary>
    public ColorSpace Space { get; }

    // ── Factory methods ───────────────────────────────────────────────────

    /// <summary>
    /// Creates a DeviceGray colour.
    /// PDF 32000-1:2008 §8.6.4.1 — DeviceGray.
    /// </summary>
    /// <param name="gray">Gray level [0 = black, 1 = white].</param>
    /// <param name="alpha">Opacity [0 = transparent, 1 = opaque].</param>
    public static ColorF FromGray(float gray, float alpha = 1f)
    {
        return new ColorF(ColorSpace.Gray, Clamp(gray), 0, 0, Clamp(alpha));
    }

    /// <summary>
    /// Creates a DeviceRGB colour.
    /// PDF 32000-1:2008 §8.6.4.2 — DeviceRGB.
    /// </summary>
    public static ColorF FromRgb(float r, float g, float b, float alpha = 1f)
    {
        return new ColorF(ColorSpace.Rgb, Clamp(r), Clamp(g), Clamp(b), Clamp(alpha));
    }

    /// <summary>
    /// Creates a DeviceCMYK colour.
    /// PDF 32000-1:2008 §8.6.4.4 — DeviceCMYK.
    /// </summary>
    public static ColorF FromCmyk(float c, float m, float y, float k)
    {
        return new ColorF(ColorSpace.Cmyk, Clamp(c), Clamp(m), Clamp(y), Clamp(k));
    }

    /// <summary>
    /// Creates a colour from 8-bit sRGB integers (0–255).
    /// </summary>
    public static ColorF FromRgb8(byte r, byte g, byte b, byte a = 255)
    {
        return FromRgb(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    // ── Named colours ─────────────────────────────────────────────────────

    /// <summary>Opaque black (DeviceGray 0).</summary>
    public static ColorF Black { get; } = FromGray(0f);

    /// <summary>Opaque white (DeviceGray 1).</summary>
    public static ColorF White { get; } = FromGray(1f);

    /// <summary>Fully transparent (DeviceGray 0, alpha 0).</summary>
    public static ColorF Transparent { get; } = FromGray(0f, 0f);

    // ── Component accessors ───────────────────────────────────────────────

    /// <summary>
    /// Gray level for <see cref="ColorSpace.Gray"/>;
    /// Red for <see cref="ColorSpace.Rgb"/>;
    /// Cyan for <see cref="ColorSpace.Cmyk"/>.
    /// </summary>
    public float C0 => _c0;

    /// <summary>
    /// Alpha for <see cref="ColorSpace.Gray"/>;
    /// Green for <see cref="ColorSpace.Rgb"/>;
    /// Magenta for <see cref="ColorSpace.Cmyk"/>.
    /// </summary>
    public float C1 => _c1;

    /// <summary>
    /// Zero for <see cref="ColorSpace.Gray"/>;
    /// Blue for <see cref="ColorSpace.Rgb"/>;
    /// Yellow for <see cref="ColorSpace.Cmyk"/>.
    /// </summary>
    public float C2 => _c2;

    /// <summary>
    /// Alpha for <see cref="ColorSpace.Gray"/> (stored separately);
    /// Alpha for <see cref="ColorSpace.Rgb"/>;
    /// Key (black) for <see cref="ColorSpace.Cmyk"/>.
    /// </summary>
    public float C3 => _c3;

    /// <summary>Red component (DeviceRGB only).</summary>
    public float R => Space == ColorSpace.Rgb ? _c0 : 0f;

    /// <summary>Green component (DeviceRGB only).</summary>
    public float G => Space == ColorSpace.Rgb ? _c1 : 0f;

    /// <summary>Blue component (DeviceRGB only).</summary>
    public float B => Space == ColorSpace.Rgb ? _c2 : 0f;

    /// <summary>Alpha/opacity [0 = transparent, 1 = opaque]. Not applicable to CMYK.</summary>
    public float Alpha => Space == ColorSpace.Cmyk ? 1f : _c3;

    /// <summary>Gray level (DeviceGray only).</summary>
    public float Gray => Space == ColorSpace.Gray ? _c0 : 0f;

    // ── Conversion ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts this colour to DeviceRGB for compositing purposes.
    /// CMYK → RGB uses the standard formula: R = (1-C)*(1-K) etc.
    /// PDF 32000-1:2008 §10.3 — Conversions between colour spaces.
    /// </summary>
    public ColorF ToRgb()
    {
        if (Space == ColorSpace.Rgb)
        {
            return this;
        }

        if (Space == ColorSpace.Gray)
        {
            return FromRgb(_c0, _c0, _c0, _c3);
        }

        // CMYK → RGB
        float r = (1f - _c0) * (1f - _c3);
        float g = (1f - _c1) * (1f - _c3);
        float b = (1f - _c2) * (1f - _c3);
        return FromRgb(r, g, b);
    }

    /// <summary>
    /// Returns this colour as a packed ARGB 32-bit integer (sRGB).
    /// Alpha in bits 31-24, Red in 23-16, Green in 15-8, Blue in 7-0.
    /// </summary>
    public uint ToArgb32()
    {
        ColorF rgb = ToRgb();
        uint a = (uint)(rgb.Alpha * 255f + 0.5f) & 0xFF;
        uint r = (uint)(rgb.R * 255f + 0.5f) & 0xFF;
        uint g = (uint)(rgb.G * 255f + 0.5f) & 0xFF;
        uint bv = (uint)(rgb.B * 255f + 0.5f) & 0xFF;
        return (a << 24) | (r << 16) | (g << 8) | bv;
    }

    // ── Equality ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool Equals(ColorF other) =>
        Space == other.Space &&
        _c0 == other._c0 && _c1 == other._c1 &&
        _c2 == other._c2 && _c3 == other._c3;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ColorF c && Equals(c);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Space, _c0, _c1, _c2, _c3);

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Space == ColorSpace.Gray)
        {
            return $"Gray({_c0:F3} a={_c3:F3})";
        }

        if (Space == ColorSpace.Rgb)
        {
            return $"RGB({_c0:F3} {_c1:F3} {_c2:F3} a={_c3:F3})";
        }

        return $"CMYK({_c0:F3} {_c1:F3} {_c2:F3} {_c3:F3})";
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ColorF left, ColorF right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ColorF left, ColorF right) => !left.Equals(right);

    // ── Private helpers ───────────────────────────────────────────────────

    private static float Clamp(float v) =>
        v < 0f ? 0f : v > 1f ? 1f : v;
}
