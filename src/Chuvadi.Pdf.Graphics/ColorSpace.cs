// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6 — Colour spaces
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// Enumeration of PDF device colour spaces.

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// The colour space of a <see cref="ColorF"/> value.
/// PDF 32000-1:2008 §8.6 — Colour spaces.
/// </summary>
public enum ColorSpace
{
    /// <summary>
    /// Single-channel grey (0 = black, 1 = white).
    /// PDF DeviceGray. PDF 32000-1:2008 §8.6.4.1.
    /// </summary>
    Gray,

    /// <summary>
    /// Red, Green, Blue (each 0–1).
    /// PDF DeviceRGB. PDF 32000-1:2008 §8.6.4.2.
    /// </summary>
    Rgb,

    /// <summary>
    /// Cyan, Magenta, Yellow, Key (Black) (each 0–1).
    /// PDF DeviceCMYK. PDF 32000-1:2008 §8.6.4.4.
    /// </summary>
    Cmyk,
}
