// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate

using System;
using System.Collections;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// A page's content as a neutral, ordered sequence of <see cref="RenderOp"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Built by <see cref="DisplayListBuilder.Build"/>; consumed by output adapters
/// such as <c>SvgRenderer</c>, <c>WpfRenderer</c>, or future software rasterizers.
/// </para>
/// <para>
/// Pure value-like type: same page bytes, same display list. No rendering side
/// effects.
/// </para>
/// </remarks>
public sealed class PageDisplayList : IReadOnlyList<RenderOp>
{
    private readonly IReadOnlyList<RenderOp> _ops;

    /// <summary>Initialises a display list with the given ops and page metadata.</summary>
    public PageDisplayList(
        IReadOnlyList<RenderOp> ops,
        double mediaWidth,
        double mediaHeight,
        int rotation)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        MediaWidth = mediaWidth;
        MediaHeight = mediaHeight;
        Rotation = rotation;
    }

    /// <summary>Page width in points.</summary>
    public double MediaWidth { get; }

    /// <summary>Page height in points.</summary>
    public double MediaHeight { get; }

    /// <summary>Clockwise rotation in degrees (0, 90, 180, 270).</summary>
    public int Rotation { get; }

    /// <inheritdoc />
    public int Count => _ops.Count;

    /// <inheritdoc />
    public RenderOp this[int index] => _ops[index];

    /// <inheritdoc />
    public IEnumerator<RenderOp> GetEnumerator() => _ops.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
