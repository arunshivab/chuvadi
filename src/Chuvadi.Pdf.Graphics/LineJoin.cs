// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4.3.4 — Line join style
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// Line join style enumeration.

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// Specifies the shape of corners where two path segments meet when stroked.
/// PDF 32000-1:2008 §8.4.3.4 — Line join style, Table 55.
/// </summary>
public enum LineJoin
{
    /// <summary>
    /// Miter join. The outer edges of the strokes are extended to meet at a point.
    /// Clipped to a bevel when the miter length exceeds the miter limit.
    /// PDF value 0.
    /// </summary>
    Miter = 0,

    /// <summary>
    /// Round join. A circular arc is drawn at the corner.
    /// PDF value 1.
    /// </summary>
    Round = 1,

    /// <summary>
    /// Bevel join. The corner is finished with a straight line segment.
    /// PDF value 2.
    /// </summary>
    Bevel = 2,
}
