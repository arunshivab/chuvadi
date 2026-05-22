// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.6.7  — Line annotations
//        PDF 32000-1:2008 §12.5.6.8  — Square and Circle annotations
//        PDF 32000-1:2008 §12.5.6.9  — Polygon and PolyLine annotations
// PHASE: v2.0.1 — shape annotation models

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Annotations;

/// <summary>
/// Line ending style for Line and PolyLine annotations. PDF 32000-1:2008
/// §12.5.6.7, Table 176 — /LE entry values. Names match the PDF spec.
/// </summary>
public enum LineEnding
{
    /// <summary>No specific ending. (PDF /LE = None, the default.)</summary>
    None,

    /// <summary>Filled square. (PDF /LE = Square.)</summary>
    Square,

    /// <summary>Filled circle. (PDF /LE = Circle.)</summary>
    Circle,

    /// <summary>Filled diamond. (PDF /LE = Diamond.)</summary>
    Diamond,

    /// <summary>Open arrowhead pointing along the line. (PDF /LE = OpenArrow.)</summary>
    OpenArrow,

    /// <summary>Filled arrowhead pointing along the line. (PDF /LE = ClosedArrow.)</summary>
    ClosedArrow,

    /// <summary>Short line perpendicular to the line ending. (PDF /LE = Butt.)</summary>
    Butt,

    /// <summary>Open arrowhead pointing away from the line. (PDF /LE = ROpenArrow.)</summary>
    ROpenArrow,

    /// <summary>Filled arrowhead pointing away from the line. (PDF /LE = RClosedArrow.)</summary>
    RClosedArrow,

    /// <summary>Short line at 45° to the line ending. (PDF /LE = Slash.)</summary>
    Slash,
}

// ── Shape annotations ────────────────────────────────────────────────────────

/// <summary>
/// Square (rectangle outline) annotation. PDF 32000-1:2008 §12.5.6.8.
/// </summary>
public sealed class SquareAnnotation : PdfAnnotation
{
    /// <summary>Initialises a square annotation.</summary>
    public SquareAnnotation(
        int pageIndex,
        RectangleF rect,
        BorderStyle? borderStyle = null,
        ColorF? interiorColor = null,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(AnnotationType.Square, pageIndex, rect, contents, color, author, opacity)
    {
        BorderStyle = borderStyle;
        InteriorColor = interiorColor;
    }

    /// <summary>Gets the border style, or null for the viewer default.</summary>
    public BorderStyle? BorderStyle { get; }

    /// <summary>
    /// Gets the interior (fill) colour, or null for an unfilled outline.
    /// PDF /IC entry.
    /// </summary>
    public ColorF? InteriorColor { get; }
}

/// <summary>
/// Circle (ellipse outline) annotation. The ellipse is inscribed in
/// <see cref="PdfAnnotation.Rect"/>. PDF 32000-1:2008 §12.5.6.8.
/// </summary>
public sealed class CircleAnnotation : PdfAnnotation
{
    /// <summary>Initialises a circle annotation.</summary>
    public CircleAnnotation(
        int pageIndex,
        RectangleF rect,
        BorderStyle? borderStyle = null,
        ColorF? interiorColor = null,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(AnnotationType.Circle, pageIndex, rect, contents, color, author, opacity)
    {
        BorderStyle = borderStyle;
        InteriorColor = interiorColor;
    }

    /// <summary>Gets the border style, or null for the viewer default.</summary>
    public BorderStyle? BorderStyle { get; }

    /// <summary>
    /// Gets the interior (fill) colour, or null for an unfilled outline.
    /// PDF /IC entry.
    /// </summary>
    public ColorF? InteriorColor { get; }
}

/// <summary>
/// Line annotation. PDF 32000-1:2008 §12.5.6.7.
/// </summary>
/// <remarks>
/// The line runs from <see cref="Start"/> to <see cref="End"/>, both in PDF
/// user-space coordinates. The annotation <see cref="PdfAnnotation.Rect"/>
/// must enclose both endpoints.
/// </remarks>
public sealed class LineAnnotation : PdfAnnotation
{
    /// <summary>Initialises a line annotation.</summary>
    public LineAnnotation(
        int pageIndex,
        RectangleF rect,
        PointF start,
        PointF end,
        BorderStyle? borderStyle = null,
        LineEnding lineEndingStart = LineEnding.None,
        LineEnding lineEndingEnd = LineEnding.None,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(AnnotationType.Line, pageIndex, rect, contents, color, author, opacity)
    {
        Start = start;
        End = end;
        BorderStyle = borderStyle;
        LineEndingStart = lineEndingStart;
        LineEndingEnd = lineEndingEnd;
    }

    /// <summary>Gets the start point of the line (PDF /L entry, first pair).</summary>
    public PointF Start { get; }

    /// <summary>Gets the end point of the line (PDF /L entry, second pair).</summary>
    public PointF End { get; }

    /// <summary>Gets the border style, or null for the viewer default.</summary>
    public BorderStyle? BorderStyle { get; }

    /// <summary>Gets the line-ending style at <see cref="Start"/>. PDF /LE[0].</summary>
    public LineEnding LineEndingStart { get; }

    /// <summary>Gets the line-ending style at <see cref="End"/>. PDF /LE[1].</summary>
    public LineEnding LineEndingEnd { get; }
}

/// <summary>
/// Polygon annotation: a closed shape connecting <see cref="Vertices"/>.
/// PDF 32000-1:2008 §12.5.6.9.
/// </summary>
public sealed class PolygonAnnotation : PdfAnnotation
{
    /// <summary>Initialises a polygon annotation.</summary>
    /// <exception cref="ArgumentNullException">When <paramref name="vertices"/> is null.</exception>
    /// <exception cref="ArgumentException">When fewer than 3 vertices are provided.</exception>
    public PolygonAnnotation(
        int pageIndex,
        RectangleF rect,
        IReadOnlyList<PointF> vertices,
        BorderStyle? borderStyle = null,
        ColorF? interiorColor = null,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(AnnotationType.Polygon, pageIndex, rect, contents, color, author, opacity)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        if (vertices.Count < 3)
        {
            throw new ArgumentException(
                "A polygon annotation requires at least three vertices.",
                nameof(vertices));
        }

        Vertices = vertices;
        BorderStyle = borderStyle;
        InteriorColor = interiorColor;
    }

    /// <summary>Gets the vertices of the polygon in order. PDF /Vertices entry.</summary>
    public IReadOnlyList<PointF> Vertices { get; }

    /// <summary>Gets the border style, or null for the viewer default.</summary>
    public BorderStyle? BorderStyle { get; }

    /// <summary>
    /// Gets the interior (fill) colour, or null for an unfilled outline.
    /// PDF /IC entry.
    /// </summary>
    public ColorF? InteriorColor { get; }
}

/// <summary>
/// PolyLine annotation: an open shape connecting <see cref="Vertices"/>.
/// PDF 32000-1:2008 §12.5.6.9.
/// </summary>
public sealed class PolyLineAnnotation : PdfAnnotation
{
    /// <summary>Initialises a polyline annotation.</summary>
    /// <exception cref="ArgumentNullException">When <paramref name="vertices"/> is null.</exception>
    /// <exception cref="ArgumentException">When fewer than 2 vertices are provided.</exception>
    public PolyLineAnnotation(
        int pageIndex,
        RectangleF rect,
        IReadOnlyList<PointF> vertices,
        BorderStyle? borderStyle = null,
        LineEnding lineEndingStart = LineEnding.None,
        LineEnding lineEndingEnd = LineEnding.None,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(AnnotationType.PolyLine, pageIndex, rect, contents, color, author, opacity)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        if (vertices.Count < 2)
        {
            throw new ArgumentException(
                "A polyline annotation requires at least two vertices.",
                nameof(vertices));
        }

        Vertices = vertices;
        BorderStyle = borderStyle;
        LineEndingStart = lineEndingStart;
        LineEndingEnd = lineEndingEnd;
    }

    /// <summary>Gets the vertices of the polyline in order. PDF /Vertices entry.</summary>
    public IReadOnlyList<PointF> Vertices { get; }

    /// <summary>Gets the border style, or null for the viewer default.</summary>
    public BorderStyle? BorderStyle { get; }

    /// <summary>Gets the line-ending style at the first vertex. PDF /LE[0].</summary>
    public LineEnding LineEndingStart { get; }

    /// <summary>Gets the line-ending style at the last vertex. PDF /LE[1].</summary>
    public LineEnding LineEndingEnd { get; }
}
