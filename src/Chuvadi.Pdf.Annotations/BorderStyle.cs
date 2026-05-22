// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.4 — Border styles
// PHASE: v2.0.1 — shape annotation support

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Annotations;

/// <summary>
/// PDF border-style kind. PDF 32000-1:2008 §12.5.4, Table 166 — /S entry.
/// </summary>
public enum BorderStyleType
{
    /// <summary>Solid border (PDF /S = S, the default).</summary>
    Solid,

    /// <summary>Dashed border (PDF /S = D).</summary>
    Dashed,

    /// <summary>Beveled border, raised appearance (PDF /S = B).</summary>
    Beveled,

    /// <summary>Inset border, recessed appearance (PDF /S = I).</summary>
    Inset,

    /// <summary>Underline border, single line below (PDF /S = U).</summary>
    Underline,
}

/// <summary>
/// Border style for an annotation, describing width, style, and (for dashed
/// borders) dash pattern. PDF 32000-1:2008 §12.5.4 — Border styles.
/// </summary>
public sealed class BorderStyle
{
    /// <summary>Initialises a border style.</summary>
    /// <param name="width">Border width in PDF user-space units. Must be ≥ 0.</param>
    /// <param name="style">Border style kind. Default Solid.</param>
    /// <param name="dashPattern">
    /// Dash pattern (alternating on/off lengths). Required when
    /// <paramref name="style"/> is <see cref="BorderStyleType.Dashed"/>;
    /// ignored otherwise. Null is treated as the default [3] for Dashed.
    /// </param>
    public BorderStyle(
        float width = 1f,
        BorderStyleType style = BorderStyleType.Solid,
        IReadOnlyList<float>? dashPattern = null)
    {
        if (width < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Border width must be non-negative.");
        }

        Width = width;
        Style = style;
        DashPattern = dashPattern;
    }

    /// <summary>Gets the border width in PDF user-space units.</summary>
    public float Width { get; }

    /// <summary>Gets the border style kind.</summary>
    public BorderStyleType Style { get; }

    /// <summary>
    /// Gets the dash pattern. Each entry alternates between on-length and
    /// off-length. Null means use the default (3 units on, 3 off) when
    /// <see cref="Style"/> is <see cref="BorderStyleType.Dashed"/>, and is
    /// ignored otherwise.
    /// </summary>
    public IReadOnlyList<float>? DashPattern { get; }
}
