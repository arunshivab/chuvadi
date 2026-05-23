// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.1.1 — attribute escaping regression

using System.IO;
using System.Xml;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

/// <summary>
/// Regression tests for attribute-value escaping in <c>SvgWriter</c>.
/// </summary>
/// <remarks>
/// The bug these guard against: <c>SvgWriter</c> was emitting attribute
/// values verbatim, including the double quotes inside CSS-style font
/// family strings like <c>Times, "Times New Roman", serif</c>. The
/// resulting SVG broke XML at the first inner quote.
/// </remarks>
public sealed class SvgWriterEscapingTests
{
    [Fact]
    public void RenderPage_TimesRomanText_ProducesWellFormedXml()
    {
        // Times-Roman triggers the CssFamilyFor path that returns a font
        // family string with embedded double quotes. Before v2.1.1 the
        // resulting SVG was not valid XML.
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText(
            "Hello",
            50, 50,
            StandardFonts.TimesRoman,
            12,
            Colors.Black);
        byte[] pdf = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        ParseAsXml(svg);   // throws if malformed
    }

    [Fact]
    public void RenderPage_TimesBoldText_ProducesWellFormedXml()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText(
            "Bold",
            50, 50,
            StandardFonts.TimesBold,
            12,
            Colors.Black);
        byte[] pdf = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        ParseAsXml(svg);
    }

    [Fact]
    public void RenderPage_CourierText_ProducesWellFormedXml()
    {
        // Courier also has a quoted-name fallback ("Courier New").
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText(
            "Mono",
            50, 50,
            StandardFonts.Courier,
            12,
            Colors.Black);
        byte[] pdf = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        ParseAsXml(svg);
    }

    [Fact]
    public void RenderPage_ZapfDingbatsText_ProducesWellFormedXml()
    {
        // ZapfDingbats maps to a quoted family ("Zapf Dingbats").
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText(
            "X",
            50, 50,
            StandardFonts.ZapfDingbats,
            12,
            Colors.Black);
        byte[] pdf = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        ParseAsXml(svg);
    }

    [Fact]
    public void RenderPage_TimesRoman_FontFamilyAttributeIsProperlyEscaped()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText(
            "Hello",
            50, 50,
            StandardFonts.TimesRoman,
            12,
            Colors.Black);
        byte[] pdf = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        // The unescaped form would contain a literal " inside the attribute.
        // The escaped form contains &quot; instead.
        svg.Should().NotContain("font-family=\"Times, \"Times");
        svg.Should().Contain("&quot;Times New Roman&quot;");
    }

    [Fact]
    public void RenderPage_TextWithSpecialChars_ContentIsEscaped()
    {
        // Text content has always been escaped in <text> bodies; this is a
        // sanity check that the audit didn't regress that path.
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText(
            "x < y && z > 0",
            50, 50,
            StandardFonts.Helvetica,
            12,
            Colors.Black);
        byte[] pdf = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        ParseAsXml(svg);
        svg.Should().Contain("&lt;");
        svg.Should().Contain("&amp;");
        svg.Should().Contain("&gt;");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void ParseAsXml(string svg)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            ConformanceLevel = ConformanceLevel.Document,
        };

        try
        {
            using StringReader reader = new(svg);
            using XmlReader xml = XmlReader.Create(reader, settings);
            while (xml.Read())
            {
                // Drain the stream; XmlReader throws XmlException on malformed input.
            }
        }
        catch (XmlException ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"Rendered SVG is not well-formed XML at line {ex.LineNumber}, " +
                $"column {ex.LinePosition}: {ex.Message}\n\nSVG length: {svg.Length}");
        }
    }
}
