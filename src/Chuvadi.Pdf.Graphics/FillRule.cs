// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.3.3 — Filling
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// Fill rule enumeration for path painting.

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// Determines how the interior of a path is defined when the path
/// self-intersects or has nested sub-paths.
/// PDF 32000-1:2008 §8.5.3.3 — Filling.
/// </summary>
public enum FillRule
{
    /// <summary>
    /// Non-zero winding number rule. A point is inside if a ray from
    /// that point crosses the path in a way that the winding number is non-zero.
    /// Default rule for PDF operator 'f' and 'F'.
    /// </summary>
    NonZeroWinding,

    /// <summary>
    /// Even-odd rule. A point is inside if a ray from that point crosses
    /// the path boundary an odd number of times.
    /// Used by PDF operator 'f*'.
    /// </summary>
    EvenOdd,
}
