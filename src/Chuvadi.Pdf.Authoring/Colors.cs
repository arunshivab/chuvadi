// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.3 — Authoring module

using System;
using System.Globalization;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// An RGB color in [0, 1] floating-point space. Internally maps to PDF DeviceRGB.
/// </summary>
public readonly record struct Color(double R, double G, double B)
{
    /// <summary>Creates a color from 0–255 byte channels.</summary>
    public static Color FromBytes(byte r, byte g, byte b)
        => new(r / 255.0, g / 255.0, b / 255.0);

    /// <summary>Creates a color from a hex string ("#RRGGBB" or "RRGGBB").</summary>
    public static Color FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        string s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length != 6)
        {
            throw new ArgumentException("Hex color must be 6 hex digits.", nameof(hex));
        }
        byte r = byte.Parse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return FromBytes(r, g, b);
    }
}

/// <summary>Common named colors.</summary>
public static class Colors
{
    /// <summary>Pure black (0, 0, 0).</summary>
    public static Color Black { get; } = new(0, 0, 0);

    /// <summary>Pure white (1, 1, 1).</summary>
    public static Color White { get; } = new(1, 1, 1);

    /// <summary>Mid gray (0.5, 0.5, 0.5).</summary>
    public static Color Gray { get; } = new(0.5, 0.5, 0.5);

    /// <summary>Light gray (0.85, 0.85, 0.85).</summary>
    public static Color LightGray { get; } = new(0.85, 0.85, 0.85);

    /// <summary>Dark gray (0.25, 0.25, 0.25).</summary>
    public static Color DarkGray { get; } = new(0.25, 0.25, 0.25);

    /// <summary>Pure red.</summary>
    public static Color Red { get; } = new(1, 0, 0);

    /// <summary>Pure green.</summary>
    public static Color Green { get; } = new(0, 1, 0);

    /// <summary>Pure blue.</summary>
    public static Color Blue { get; } = new(0, 0, 1);

    /// <summary>Hyperlink-style blue (#1a5490).</summary>
    public static Color LinkBlue { get; } = Color.FromHex("#1a5490");
}

/// <summary>Text alignment within a block or table cell.</summary>
public enum TextAlignment
{
    /// <summary>Left-aligned (default).</summary>
    Left = 0,
    /// <summary>Centered horizontally.</summary>
    Center = 1,
    /// <summary>Right-aligned.</summary>
    Right = 2,
    /// <summary>Justified — fill the line width.</summary>
    Justify = 3,
}

/// <summary>Vertical alignment within a table cell.</summary>
public enum VerticalAlignment
{
    /// <summary>Top-aligned (default).</summary>
    Top = 0,
    /// <summary>Centered vertically.</summary>
    Middle = 1,
    /// <summary>Bottom-aligned.</summary>
    Bottom = 2,
}

/// <summary>Border style for tables and rectangles.</summary>
public enum BorderStyle
{
    /// <summary>No border.</summary>
    None = 0,
    /// <summary>A single solid line.</summary>
    Single = 1,
}
