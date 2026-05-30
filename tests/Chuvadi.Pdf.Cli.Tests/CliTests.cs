// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Cli tests

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Cli.Tests;

// ── ArgParser ─────────────────────────────────────────────────────────────

public sealed class ArgParserTests
{
    [Fact]
    public void Parse_NullArgs_Throws()
    {
        Action act = () => ArgParser.Parse(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_PositionalArgs_Captured()
    {
        ParsedArgs p = ArgParser.Parse(["input.pdf", "out.pdf"]);
        p.Positional.Should().Equal("input.pdf", "out.pdf");
    }

    [Fact]
    public void Parse_OptionWithValue_Captured()
    {
        ParsedArgs p = ArgParser.Parse(["--output", "out.pdf"]);
        p.Get("output").Should().Be("out.pdf");
    }

    [Fact]
    public void Parse_OptionEqualsForm_Captured()
    {
        ParsedArgs p = ArgParser.Parse(["--text=DRAFT"]);
        p.Get("text").Should().Be("DRAFT");
    }

    [Fact]
    public void Parse_RepeatedOption_AllValuesCaptured()
    {
        ParsedArgs p = ArgParser.Parse(["--rect", "0,0,0,10,10", "--rect", "1,0,0,10,10"]);
        p.GetAll("rect").Should().HaveCount(2);
    }

    [Fact]
    public void Parse_BooleanFlag_Captured()
    {
        ParsedArgs p = ArgParser.Parse(["--verbose"]);
        p.HasFlag("verbose").Should().BeTrue();
    }

    [Fact]
    public void Parse_DefaultValue_UsedWhenAbsent()
    {
        ParsedArgs p = ArgParser.Parse(["input.pdf"]);
        p.Get("dpi", "96").Should().Be("96");
    }
}

// ── CommandRegistry ───────────────────────────────────────────────────────

public sealed class CommandRegistryTests
{
    [Theory]
    [InlineData("info")]
    [InlineData("render")]
    [InlineData("watermark")]
    [InlineData("redact")]
    [InlineData("form-fill")]
    [InlineData("extract-text")]
    [InlineData("outlines")]
    [InlineData("merge")]
    [InlineData("split")]
    [InlineData("delete")]
    [InlineData("rotate")]
    [InlineData("tokenize")]
    [InlineData("dump-objects")]
    [InlineData("parse-content")]
    [InlineData("decode-stream")]
    [InlineData("inspect-xref")]
    [InlineData("validate-fonts")]
    public void Find_KnownVerbs_ReturnsCommand(string verb)
    {
        CommandRegistry.Find(verb).Should().NotBeNull();
    }

    [Fact]
    public void Find_UnknownVerb_ReturnsNull()
    {
        CommandRegistry.Find("nope").Should().BeNull();
    }
}

// ── Program.Run dispatcher ────────────────────────────────────────────────

public sealed class ProgramTests
{
    [Fact]
    public void Run_NullArgs_Throws()
    {
        Action act = () => Program.Run(null!, new StringWriter(), new StringWriter());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Run_NoArgs_PrintsUsageAndReturnsZero()
    {
        StringWriter sout = new StringWriter();
        int code = Program.Run([], sout, new StringWriter());
        code.Should().Be(0);
        sout.ToString().Should().Contain("chuvadi");
    }

    [Fact]
    public void Run_HelpFlag_PrintsUsage()
    {
        StringWriter sout = new StringWriter();
        Program.Run(["--help"], sout, new StringWriter());
        sout.ToString().Should().Contain("User commands");
    }

    [Fact]
    public void Run_UnknownVerb_ReturnsTwo()
    {
        StringWriter serr = new StringWriter();
        int code = Program.Run(["bogus"], new StringWriter(), serr);
        code.Should().Be(2);
        serr.ToString().Should().Contain("unknown command");
    }
}

// ── End-to-end smoke tests ────────────────────────────────────────────────

public sealed class CommandSmokeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _samplePdf;

    public CommandSmokeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chuvadi_cli_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_tempDir);
        _samplePdf = Path.Combine(_tempDir, "sample.pdf");
        WriteSamplePdf(_samplePdf);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Info_OnSamplePdf_ReportsPageCount()
    {
        StringWriter sout = new StringWriter();
        int code = Program.Run(["info", _samplePdf], sout, new StringWriter());
        code.Should().Be(0);
        sout.ToString().Should().Contain("Pages:");
    }

    [Fact]
    public void Info_NoArgs_ReturnsTwo()
    {
        int code = Program.Run(["info"], new StringWriter(), new StringWriter());
        code.Should().Be(2);
    }

    [Fact]
    public void DumpObjects_OnSamplePdf_ListsCatalog()
    {
        StringWriter sout = new StringWriter();
        int code = Program.Run(["dump-objects", _samplePdf], sout, new StringWriter());
        code.Should().Be(0);
        sout.ToString().Should().Contain("Catalog");
    }

    [Fact]
    public void InspectXref_OnSamplePdf_PrintsHeader()
    {
        StringWriter sout = new StringWriter();
        int code = Program.Run(["inspect-xref", _samplePdf], sout, new StringWriter());
        code.Should().Be(0);
        sout.ToString().Should().Contain("Obj");
    }

    [Fact]
    public void ValidateFonts_OnPdfWithoutFonts_ReportsNone()
    {
        StringWriter sout = new StringWriter();
        int code = Program.Run(["validate-fonts", _samplePdf], sout, new StringWriter());
        code.Should().Be(0);
        sout.ToString().Should().Contain("no fonts");
    }

    [Fact]
    public void Outlines_OnPdfWithoutBookmarks_ReportsNone()
    {
        StringWriter sout = new StringWriter();
        int code = Program.Run(["outlines", _samplePdf], sout, new StringWriter());
        code.Should().Be(0);
        sout.ToString().Should().Contain("no outlines");
    }

    [Fact]
    public void Watermark_AppliesAndWrites()
    {
        string outPath = Path.Combine(_tempDir, "wm.pdf");
        StringWriter sout = new StringWriter();
        int code = Program.Run(
            ["watermark", _samplePdf, "--output", outPath, "--text", "DRAFT"],
            sout, new StringWriter());
        code.Should().Be(0);
        File.Exists(outPath).Should().BeTrue();
    }

    [Fact]
    public void ExtractText_NoArgs_ReturnsTwo()
    {
        int code = Program.Run(["extract-text"], new StringWriter(), new StringWriter());
        code.Should().Be(2);
    }

    [Fact]
    public void Tokenize_OnPdfWithoutContent_Succeeds()
    {
        StringWriter sout = new StringWriter();
        int code = Program.Run(["tokenize", _samplePdf], sout, new StringWriter());
        code.Should().Be(0);
    }

    // ── Helper: build a minimal PDF on disk ────────────────────────────────

    private static void WriteSamplePdf(string path)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792),
        ]));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using (FileStream fs = File.Create(path))
        {
            PdfWriter.Write(fs, objects, trailer);
        }
    }
}
