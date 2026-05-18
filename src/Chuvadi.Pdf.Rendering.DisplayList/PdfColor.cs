// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>The source color space of a <see cref="PdfColor"/>.</summary>
public enum PdfColorSpace
{
    /// <summary>DeviceGray (single component, [0, 1]).</summary>
    DeviceGray = 0,
    /// <summary>DeviceRGB (three components, [0, 1] each).</summary>
    DeviceRgb = 1,
    /// <summary>DeviceCMYK (four components, [0, 1] each).</summary>
    DeviceCmyk = 2,
}

/// <summary>
/// A color value with explicit source color space. Conversion to sRGB happens
/// in the renderer, not in the display list.
/// </summary>
public readonly record struct PdfColor(PdfColorSpace Space, double C0, double C1, double C2, double C3)
{
    /// <summary>Pure black in DeviceGray.</summary>
    public static PdfColor Black { get; } = new(PdfColorSpace.DeviceGray, 0, 0, 0, 0);

    /// <summary>Pure white in DeviceGray.</summary>
    public static PdfColor White { get; } = new(PdfColorSpace.DeviceGray, 1, 0, 0, 0);

    /// <summary>Creates a DeviceGray color.</summary>
    public static PdfColor Gray(double g) => new(PdfColorSpace.DeviceGray, g, 0, 0, 0);

    /// <summary>Creates a DeviceRGB color.</summary>
    public static PdfColor Rgb(double r, double g, double b) => new(PdfColorSpace.DeviceRgb, r, g, b, 0);

    /// <summary>Creates a DeviceCMYK color.</summary>
    public static PdfColor Cmyk(double c, double m, double y, double k)
        => new(PdfColorSpace.DeviceCmyk, c, m, y, k);

    /// <summary>Converts this color to sRGB (r, g, b) in [0, 1].</summary>
    public (double R, double G, double B) ToSrgb() => Space switch
    {
        PdfColorSpace.DeviceGray => (C0, C0, C0),
        PdfColorSpace.DeviceRgb => (C0, C1, C2),
        PdfColorSpace.DeviceCmyk => ((1 - C0) * (1 - C3), (1 - C1) * (1 - C3), (1 - C2) * (1 - C3)),
        _ => (0, 0, 0),
    };
}
