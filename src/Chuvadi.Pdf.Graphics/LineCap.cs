// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4.3.3 — Line cap style
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// Line cap style enumeration.

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// Specifies the shape of the ends of open subpaths when stroked.
/// PDF 32000-1:2008 §8.4.3.3 — Line cap style, Table 54.
/// </summary>
public enum LineCap
{
    /// <summary>
    /// Butt cap. The stroke ends exactly at the endpoint with no extension.
    /// PDF value 0.
    /// </summary>
    Butt = 0,

    /// <summary>
    /// Round cap. A semicircle of diameter equal to line width is drawn
    /// beyond the endpoint.
    /// PDF value 1.
    /// </summary>
    Round = 1,

    /// <summary>
    /// Projecting square cap. The stroke extends half the line width
    /// beyond the endpoint in a square shape.
    /// PDF value 2.
    /// </summary>
    Square = 2,
}
