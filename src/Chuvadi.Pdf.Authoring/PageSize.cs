// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.3 — Authoring module

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// A page size in PDF points (1 pt = 1/72 inch).
/// </summary>
public readonly record struct PageSize(double Width, double Height)
{
    /// <summary>A4: 595 × 842 pt (210 × 297 mm).</summary>
    public static PageSize A4 { get; } = new(595, 842);

    /// <summary>A3: 842 × 1191 pt.</summary>
    public static PageSize A3 { get; } = new(842, 1191);

    /// <summary>A5: 420 × 595 pt.</summary>
    public static PageSize A5 { get; } = new(420, 595);

    /// <summary>US Letter: 612 × 792 pt (8.5 × 11 inch).</summary>
    public static PageSize Letter { get; } = new(612, 792);

    /// <summary>US Legal: 612 × 1008 pt (8.5 × 14 inch).</summary>
    public static PageSize Legal { get; } = new(612, 1008);

    /// <summary>US Tabloid: 792 × 1224 pt (11 × 17 inch).</summary>
    public static PageSize Tabloid { get; } = new(792, 1224);

    /// <summary>Returns a landscape-oriented version of this size (swaps width and height).</summary>
    public PageSize Landscape() => new(Height, Width);
}
