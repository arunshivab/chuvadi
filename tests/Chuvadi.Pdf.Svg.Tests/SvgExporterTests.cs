// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.0 — SVG export
//
// SvgExporter is marked [Obsolete] but still shipped — callers can still
// use it. These tests intentionally exercise the obsolete API to verify
// it keeps working as long as it's part of the surface. Remove this
// pragma when SvgExporter itself is removed from the library.
#pragma warning disable CS0618 // Type or member is obsolete

using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;
namespace Chuvadi.Pdf.Svg.Tests;
public sealed class SvgExporterTests
{
    [Fact]
    public void ExportPage_TextOnly_ProducesValidSvg()
    {
        byte[] pdf = BuildPdf(p => p.DrawText("Hello", 50, 50,
            StandardFonts.Helvetica, 12, Colors.Black));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = SvgExporter.ExportPage(doc, 0);
        svg.Should().StartWith("<svg");
        svg.Should().Contain("Hello");
        svg.Should().Contain("viewBox=\"0 0 595 842\"");
        svg.Should().Contain("<text");
    }
    [Fact]
    public void ExportPage_Rectangle_EmitsPathWithFill()
    {
        byte[] pdf = BuildPdf(p => p.DrawRectangle(50, 50, 100, 50,
            fill: Colors.Red));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = SvgExporter.ExportPage(doc, 0);
        svg.Should().Contain("<path");
        svg.Should().Contain("rgb(255,0,0)");
    }
    [Fact]
    public void ExportPage_Line_EmitsPathWithStroke()
    {
        byte[] pdf = BuildPdf(p => p.DrawLine(0, 100, 200, 100, Colors.Blue, 2));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = SvgExporter.ExportPage(doc, 0);
        svg.Should().Contain("stroke=\"rgb(0,0,255)\"");
    }
    [Fact]
    public void ExportPages_MultiPage_ReturnsOneSvgPerPage()
    {
        var builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText("P1", 50, 50,
            StandardFonts.Helvetica, 12, Colors.Black);
        builder.AddPage(PageSize.A4).DrawText("P2", 50, 50,
            StandardFonts.Helvetica, 12, Colors.Black);
        byte[] pdf = builder.ToByteArray();
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        int n = 0;
        bool p1 = false, p2 = false;
        foreach (string svg in SvgExporter.ExportPages(doc))
        {
            n++;
            if (svg.Contains("P1")) { p1 = true; }
            if (svg.Contains("P2")) { p2 = true; }
        }
        n.Should().Be(2);
        p1.Should().BeTrue();
        p2.Should().BeTrue();
    }
    [Fact]
    public void ExportPageBytes_ReturnsUtf8Encoded()
    {
        byte[] pdf = BuildPdf(p => p.DrawText("X", 50, 50,
            StandardFonts.Helvetica, 12, Colors.Black));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        byte[] svgBytes = SvgExporter.ExportPageBytes(doc, 0);
        svgBytes.Length.Should().BeGreaterThan(0);
        svgBytes[0].Should().Be((byte)'<');
    }
    [Fact]
    public void ExportPage_PageIndexOutOfRange_Throws()
    {
        byte[] pdf = BuildPdf(p => p.DrawText("X", 50, 50,
            StandardFonts.Helvetica, 12, Colors.Black));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        System.Action act = () => SvgExporter.ExportPage(doc, 5);
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }
    private static byte[] BuildPdf(System.Action<PageBuilder> draw)
    {
        var builder = PdfDocumentBuilder.Create();
        draw(builder.AddPage(PageSize.A4));
        return builder.ToByteArray();
    }
}
