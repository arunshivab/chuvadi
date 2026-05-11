// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Redaction
// A single rectangle of PHI to remove from a specific page.

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// One rectangle of content to permanently remove from a PDF page.
/// </summary>
/// <remarks>
/// Rectangle coordinates are in PDF user space (PDF points, bottom-left origin).
/// Any text-showing operator (Tj, TJ, ', '') whose visible position falls
/// inside these bounds will be removed from the content stream and the area
/// overpainted with an opaque rectangle.
/// </remarks>
public sealed class RedactionRect
{
    /// <summary>
    /// Initialises a new <see cref="RedactionRect"/>.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="bounds">Rectangle in PDF user space, bottom-left origin.</param>
    public RedactionRect(int pageIndex, RectangleF bounds)
    {
        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex),
                "Page index must be non-negative.");
        }

        PageIndex = pageIndex;
        Bounds = bounds;
    }

    /// <summary>Gets the zero-based page index targeted by this redaction.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the rectangle to redact, in PDF user space.</summary>
    public RectangleF Bounds { get; }
}
