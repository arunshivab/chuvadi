// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate
//        v2.1.2 — FontDictsByKey allows downstream renderers to extract
//                 font program bytes for embedding (e.g. SVG @font-face)
//                 without re-resolving resources from the source document.

using System;
using System.Collections;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

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
/// <para>
/// v2.1.2: also exposes the page's font dictionaries keyed by the resource
/// name used in <c>TextOp.FontKey</c>. This allows downstream renderers that
/// want to embed font programs (e.g. <c>SvgRenderer</c> emitting CSS
/// <c>@font-face</c> rules with base64-encoded TrueType data URLs) to access
/// the source font dictionaries without re-walking the page resources or
/// holding a reference to the original <c>PdfDocument</c>.
/// </para>
/// </remarks>
public sealed class PageDisplayList : IReadOnlyList<RenderOp>
{
    private static readonly IReadOnlyDictionary<string, PdfDictionary> EmptyFonts
        = new Dictionary<string, PdfDictionary>(0);

    private static readonly IReadOnlyList<RenderingDiagnostic> EmptyDiagnostics
        = Array.Empty<RenderingDiagnostic>();

    private readonly IReadOnlyList<RenderOp> _ops;

    /// <summary>
    /// Initialises a display list with the given ops and page metadata, with
    /// no font dictionaries attached. Equivalent to passing an empty
    /// dictionary for the font registry overload.
    /// </summary>
    public PageDisplayList(
        IReadOnlyList<RenderOp> ops,
        double mediaWidth,
        double mediaHeight,
        int rotation)
        : this(ops, mediaWidth, mediaHeight, rotation, EmptyFonts)
    {
    }

    /// <summary>
    /// Initialises a display list with the given ops, page metadata, and the
    /// page's font dictionaries keyed by the resource-name used in
    /// <c>TextOp.FontKey</c> (e.g. <c>"F1"</c>, <c>"TT2"</c>).
    /// </summary>
    public PageDisplayList(
        IReadOnlyList<RenderOp> ops,
        double mediaWidth,
        double mediaHeight,
        int rotation,
        IReadOnlyDictionary<string, PdfDictionary> fontDictsByKey)
        : this(ops, mediaWidth, mediaHeight, rotation, fontDictsByKey, EmptyDiagnostics)
    {
    }

    /// <summary>
    /// Initialises a display list with the given ops, page metadata, font
    /// dictionaries, and the diagnostics accumulated during build
    /// (graceful-degradation events such as a font that could not be
    /// resolved). New in v2.1.8.
    /// </summary>
    public PageDisplayList(
        IReadOnlyList<RenderOp> ops,
        double mediaWidth,
        double mediaHeight,
        int rotation,
        IReadOnlyDictionary<string, PdfDictionary> fontDictsByKey,
        IReadOnlyList<RenderingDiagnostic> diagnostics)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(fontDictsByKey);
        ArgumentNullException.ThrowIfNull(diagnostics);
        MediaWidth = mediaWidth;
        MediaHeight = mediaHeight;
        Rotation = rotation;
        FontDictsByKey = fontDictsByKey;
        Diagnostics = diagnostics;
    }

    /// <summary>Page width in points.</summary>
    public double MediaWidth { get; }

    /// <summary>Page height in points.</summary>
    public double MediaHeight { get; }

    /// <summary>Clockwise rotation in degrees (0, 90, 180, 270).</summary>
    public int Rotation { get; }

    /// <summary>
    /// Font dictionaries for every font referenced on this page, keyed by
    /// the resource-name used in <c>TextOp.FontKey</c>. Empty when the
    /// builder did not populate it (e.g. legacy callers using the
    /// four-argument constructor). Never null.
    /// </summary>
    public IReadOnlyDictionary<string, PdfDictionary> FontDictsByKey { get; }

    /// <summary>
    /// Graceful-degradation events recorded by the builder during page
    /// construction (e.g. a font that could not be resolved, causing
    /// <see cref="DiagnosticKind.DecodeFallback"/>). Empty when nothing
    /// went wrong. Never null. New in v2.1.8 — older callers using
    /// constructors without this argument see an empty list.
    /// </summary>
    public IReadOnlyList<RenderingDiagnostic> Diagnostics { get; }

    /// <inheritdoc />
    public int Count => _ops.Count;

    /// <inheritdoc />
    public RenderOp this[int index] => _ops[index];

    /// <inheritdoc />
    public IEnumerator<RenderOp> GetEnumerator() => _ops.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
