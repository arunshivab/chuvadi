// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.1 — shape annotation tests

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Annotations.Tests;

// ── BorderStyle ──────────────────────────────────────────────────────────

public sealed class BorderStyleTests
{
    [Fact]
    public void Defaults_AreSolidOneUnit()
    {
        BorderStyle bs = new BorderStyle();
        bs.Width.Should().Be(1f);
        bs.Style.Should().Be(BorderStyleType.Solid);
        bs.DashPattern.Should().BeNull();
    }

    [Fact]
    public void NegativeWidth_Throws()
    {
        Action act = () => new BorderStyle(width: -0.5f);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ZeroWidth_IsAllowed()
    {
        // Width 0 means thinnest renderable line per PDF spec.
        BorderStyle bs = new BorderStyle(width: 0f);
        bs.Width.Should().Be(0f);
    }

    [Fact]
    public void DashedStyle_StoresPattern()
    {
        float[] pattern = [3f, 2f];
        BorderStyle bs = new BorderStyle(2f, BorderStyleType.Dashed, pattern);
        bs.Style.Should().Be(BorderStyleType.Dashed);
        bs.DashPattern.Should().Equal(3f, 2f);
    }
}

// ── Shape model constructors ─────────────────────────────────────────────

public sealed class ShapeAnnotationModelTests
{
    [Fact]
    public void Square_StoresGeometryAndStyle()
    {
        BorderStyle bs = new BorderStyle(2f);
        ColorF ic = ColorF.FromRgb(1f, 1f, 0f);
        SquareAnnotation s = new SquareAnnotation(
            0, new RectangleF(10, 20, 30, 40), bs, ic, "note", ColorF.FromRgb(1f, 0f, 0f), "author");

        s.Type.Should().Be(AnnotationType.Square);
        s.Rect.Should().Be(new RectangleF(10, 20, 30, 40));
        s.BorderStyle.Should().BeSameAs(bs);
        s.InteriorColor.Should().Be(ic);
    }

    [Fact]
    public void Circle_StoresGeometryAndStyle()
    {
        CircleAnnotation c = new CircleAnnotation(0, new RectangleF(0, 0, 100, 50));
        c.Type.Should().Be(AnnotationType.Circle);
        c.BorderStyle.Should().BeNull();
        c.InteriorColor.Should().BeNull();
    }

    [Fact]
    public void Line_StoresEndpoints()
    {
        LineAnnotation l = new LineAnnotation(
            0, new RectangleF(0, 0, 100, 100),
            new PointF(10, 20), new PointF(90, 80),
            lineEndingStart: LineEnding.OpenArrow,
            lineEndingEnd: LineEnding.ClosedArrow);

        l.Type.Should().Be(AnnotationType.Line);
        l.Start.Should().Be(new PointF(10, 20));
        l.End.Should().Be(new PointF(90, 80));
        l.LineEndingStart.Should().Be(LineEnding.OpenArrow);
        l.LineEndingEnd.Should().Be(LineEnding.ClosedArrow);
    }

    [Fact]
    public void Polygon_LessThanThreeVertices_Throws()
    {
        Action act = () => new PolygonAnnotation(
            0, new RectangleF(0, 0, 100, 100),
            new[] { new PointF(0, 0), new PointF(10, 0) });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Polygon_NullVertices_Throws()
    {
        Action act = () => new PolygonAnnotation(
            0, new RectangleF(0, 0, 100, 100), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Polygon_ThreeVerticesAccepted()
    {
        PointF[] verts = [new(0, 0), new(10, 0), new(5, 10)];
        PolygonAnnotation p = new PolygonAnnotation(
            0, new RectangleF(0, 0, 100, 100), verts);
        p.Vertices.Should().HaveCount(3);
    }

    [Fact]
    public void PolyLine_LessThanTwoVertices_Throws()
    {
        Action act = () => new PolyLineAnnotation(
            0, new RectangleF(0, 0, 100, 100),
            new[] { new PointF(0, 0) });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PolyLine_NullVertices_Throws()
    {
        Action act = () => new PolyLineAnnotation(
            0, new RectangleF(0, 0, 100, 100), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

// ── Round-trip via Reader+Writer ─────────────────────────────────────────

public sealed class ShapeAnnotationRoundTripTests
{
    [Fact]
    public void Square_RoundTrips()
    {
        using MemoryStream source = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        BorderStyle bs = new BorderStyle(3f, BorderStyleType.Dashed, new float[] { 4f, 2f });
        ColorF ic = ColorF.FromRgb(0f, 1f, 0f);
        SquareAnnotation s = new SquareAnnotation(
            0, new RectangleF(50, 60, 100, 80), bs, ic,
            contents: "review", color: ColorF.FromRgb(0f, 0f, 0f));

        AnnotationWriter.Add(output, doc, new[] { s });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(result, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<SquareAnnotation>();
        SquareAnnotation got = (SquareAnnotation)annots[0];
        got.Type.Should().Be(AnnotationType.Square);
        got.Contents.Should().Be("review");
        got.BorderStyle.Should().NotBeNull();
        got.BorderStyle!.Width.Should().Be(3f);
        got.BorderStyle.Style.Should().Be(BorderStyleType.Dashed);
        got.BorderStyle.DashPattern.Should().Equal(4f, 2f);
        got.InteriorColor.Should().NotBeNull();
    }

    [Fact]
    public void Circle_RoundTripsWithoutStyle()
    {
        using MemoryStream source = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CircleAnnotation c = new CircleAnnotation(
            0, new RectangleF(100, 100, 50, 50));
        AnnotationWriter.Add(output, doc, new[] { c });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(result, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<CircleAnnotation>();
        CircleAnnotation got = (CircleAnnotation)annots[0];
        got.Type.Should().Be(AnnotationType.Circle);
        got.BorderStyle.Should().BeNull();
        got.InteriorColor.Should().BeNull();
    }

    [Fact]
    public void Line_RoundTripsWithEndpointsAndEndings()
    {
        using MemoryStream source = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        LineAnnotation l = new LineAnnotation(
            0, new RectangleF(20, 30, 200, 100),
            new PointF(30, 40), new PointF(210, 130),
            borderStyle: new BorderStyle(1.5f),
            lineEndingStart: LineEnding.None,
            lineEndingEnd: LineEnding.OpenArrow);

        AnnotationWriter.Add(output, doc, new[] { l });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(result, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<LineAnnotation>();
        LineAnnotation got = (LineAnnotation)annots[0];
        got.Start.X.Should().BeApproximately(30f, 0.01f);
        got.Start.Y.Should().BeApproximately(40f, 0.01f);
        got.End.X.Should().BeApproximately(210f, 0.01f);
        got.End.Y.Should().BeApproximately(130f, 0.01f);
        got.LineEndingStart.Should().Be(LineEnding.None);
        got.LineEndingEnd.Should().Be(LineEnding.OpenArrow);
    }

    [Fact]
    public void Polygon_RoundTripsVerticesAndFill()
    {
        using MemoryStream source = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        PointF[] verts = [
            new PointF(100, 100),
            new PointF(200, 100),
            new PointF(150, 180),
        ];

        PolygonAnnotation p = new PolygonAnnotation(
            0, new RectangleF(100, 100, 100, 80), verts,
            interiorColor: ColorF.FromRgb(0.5f, 0.5f, 0.5f));

        AnnotationWriter.Add(output, doc, new[] { p });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(result, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<PolygonAnnotation>();
        PolygonAnnotation got = (PolygonAnnotation)annots[0];
        got.Vertices.Should().HaveCount(3);
        got.Vertices[0].X.Should().BeApproximately(100f, 0.01f);
        got.Vertices[2].Y.Should().BeApproximately(180f, 0.01f);
        got.InteriorColor.Should().NotBeNull();
    }

    [Fact]
    public void PolyLine_RoundTripsVerticesAndEndings()
    {
        using MemoryStream source = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        PointF[] verts = [
            new PointF(10, 10),
            new PointF(50, 50),
            new PointF(90, 30),
            new PointF(120, 70),
        ];

        PolyLineAnnotation pl = new PolyLineAnnotation(
            0, new RectangleF(10, 10, 120, 60), verts,
            lineEndingStart: LineEnding.Circle,
            lineEndingEnd: LineEnding.Diamond);

        AnnotationWriter.Add(output, doc, new[] { pl });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(result, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<PolyLineAnnotation>();
        PolyLineAnnotation got = (PolyLineAnnotation)annots[0];
        got.Vertices.Should().HaveCount(4);
        got.LineEndingStart.Should().Be(LineEnding.Circle);
        got.LineEndingEnd.Should().Be(LineEnding.Diamond);
    }
}
