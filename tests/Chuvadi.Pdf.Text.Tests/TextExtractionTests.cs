// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.10 — Extraction of text content
// PHASE: Phase 1 — Chuvadi.Pdf.Text tests

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

// ── OperatorExtractor ─────────────────────────────────────────────────────

public sealed class OperatorExtractorTests
{
    [Fact]
    public void Extract_NullFragments_Throws()
    {
        OperatorExtractor ex = new OperatorExtractor();
        Action act = () => ex.Extract(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Extract_EmptyList_ReturnsEmpty()
    {
        OperatorExtractor ex = new OperatorExtractor();
        ex.Extract(new List<TextFragment>()).Should().BeEmpty();
    }

    [Fact]
    public void Extract_SingleFragment_ReturnsText()
    {
        OperatorExtractor ex = new OperatorExtractor();
        List<TextFragment> fragments = [new TextFragment("Hello", 0, 0, 12)];
        ex.Extract(fragments).Should().Be("Hello");
    }

    [Fact]
    public void Extract_AdjacentFragments_NoSpace()
    {
        OperatorExtractor ex = new OperatorExtractor();
        List<TextFragment> fragments =
        [
            new TextFragment("Hel", 0, 0, 12),
            new TextFragment("lo", 11, 0, 12),  // adjacent — width ~10.8
        ];
        ex.Extract(fragments).Should().Be("Hello");
    }

    [Fact]
    public void Extract_GapBetweenFragments_InsertsSpace()
    {
        OperatorExtractor ex = new OperatorExtractor();
        List<TextFragment> fragments =
        [
            new TextFragment("Hello", 0, 0, 12),
            new TextFragment("World", 100, 0, 12),  // large gap
        ];
        string result = ex.Extract(fragments);
        result.Should().Contain("Hello");
        result.Should().Contain("World");
        result.Should().Contain(" ");
    }

    [Fact]
    public void Extract_VerticalDrop_InsertsNewline()
    {
        OperatorExtractor ex = new OperatorExtractor();
        List<TextFragment> fragments =
        [
            new TextFragment("Line 1", 0, 100, 12),
            new TextFragment("Line 2", 0, 80, 12),  // dropped 20pt > 6pt threshold
        ];
        string result = ex.Extract(fragments);
        result.Should().Contain("Line 1");
        result.Should().Contain("Line 2");
        result.Should().Contain(Environment.NewLine);
    }

    [Fact]
    public void Extract_MultipleFragments_PreservesOrder()
    {
        OperatorExtractor ex = new OperatorExtractor();
        List<TextFragment> fragments =
        [
            new TextFragment("A", 0, 0, 12),
            new TextFragment("B", 20, 0, 12),
            new TextFragment("C", 40, 0, 12),
        ];
        string result = ex.Extract(fragments);
        result.IndexOf('A').Should().BeLessThan(result.IndexOf('B'));
        result.IndexOf('B').Should().BeLessThan(result.IndexOf('C'));
    }
}

// ── LayoutExtractor ───────────────────────────────────────────────────────

public sealed class LayoutExtractorTests
{
    [Fact]
    public void Extract_NullFragments_Throws()
    {
        LayoutExtractor ex = new LayoutExtractor();
        Action act = () => ex.Extract(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Extract_EmptyList_ReturnsEmpty()
    {
        LayoutExtractor ex = new LayoutExtractor();
        ex.Extract(new List<TextFragment>()).Should().BeEmpty();
    }

    [Fact]
    public void Extract_SingleFragment_ReturnsText()
    {
        LayoutExtractor ex = new LayoutExtractor();
        List<TextFragment> fragments = [new TextFragment("Hello", 0, 0, 12)];
        ex.Extract(fragments).Should().Be("Hello");
    }

    [Fact]
    public void Extract_TwoLines_SortedTopToBottom()
    {
        LayoutExtractor ex = new LayoutExtractor();
        // PDF Y increases upward, so Y=200 is above Y=100
        List<TextFragment> fragments =
        [
            new TextFragment("Second", 0, 100, 12),
            new TextFragment("First", 0, 200, 12),
        ];
        string result = ex.Extract(fragments);
        result.IndexOf("First").Should().BeLessThan(result.IndexOf("Second"));
    }

    [Fact]
    public void Extract_SameLine_SortedLeftToRight()
    {
        LayoutExtractor ex = new LayoutExtractor();
        List<TextFragment> fragments =
        [
            new TextFragment("World", 100, 100, 12),
            new TextFragment("Hello", 0, 100, 12),
        ];
        string result = ex.Extract(fragments);
        result.IndexOf("Hello").Should().BeLessThan(result.IndexOf("World"));
    }

    [Fact]
    public void Extract_MultipleLines_EachOnOwnLine()
    {
        LayoutExtractor ex = new LayoutExtractor();
        List<TextFragment> fragments =
        [
            new TextFragment("Line1", 0, 200, 12),
            new TextFragment("Line2", 0, 100, 12),
            new TextFragment("Line3", 0, 0, 12),
        ];
        string result = ex.Extract(fragments);
        result.Should().Contain("Line1");
        result.Should().Contain("Line2");
        result.Should().Contain("Line3");
        result.IndexOf("Line1").Should().BeLessThan(result.IndexOf("Line2"));
        result.IndexOf("Line2").Should().BeLessThan(result.IndexOf("Line3"));
    }

    [Fact]
    public void Extract_OutOfOrderStreamFragments_ReturnsCorrectOrder()
    {
        LayoutExtractor ex = new LayoutExtractor();
        // Simulate PDF that writes column 2 before column 1 in stream order
        List<TextFragment> fragments =
        [
            new TextFragment("RightCol", 400, 100, 12),
            new TextFragment("LeftCol", 0, 100, 12),
        ];
        string result = ex.Extract(fragments);
        result.IndexOf("LeftCol").Should().BeLessThan(result.IndexOf("RightCol"));
    }
}

// ── TextExtractor (with NullObjectStore) ──────────────────────────────────

public sealed class TextExtractorTests
{
    [Fact]
    public void Constructor_NullObjectStore_Throws()
    {
        Action act = () => new TextExtractor(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExtractText_NullPage_Throws()
    {
        Chuvadi.Pdf.Objects.PdfObjectStore store =
            new Chuvadi.Pdf.Objects.PdfObjectStore(_ => null);
        TextExtractor ex = new TextExtractor(store);
        Action act = () => ex.ExtractText(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
