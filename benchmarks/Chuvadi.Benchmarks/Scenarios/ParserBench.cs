// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.2 stage 4 — parser open-time benchmark

using System.IO;
using BenchmarkDotNet.Attributes;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Benchmarks.Scenarios;

/// <summary>
/// Measures how long <see cref="PdfDocument.Open(Stream, bool)"/> takes on representative
/// PDFs. Tracks parser-startup performance — useful for catching regressions in xref
/// parsing, object cataloging, or trailer reading.
/// </summary>
/// <remarks>
/// Inputs are synthesized via <see cref="PdfDocumentBuilder"/> so the benchmark always
/// has something to measure regardless of <c>corpus/</c> contents.
/// </remarks>
[MemoryDiagnoser]
public class ParserOpenBench
{
    private byte[]? _smallPdfBytes;
    private byte[]? _multiPagePdfBytes;

    [GlobalSetup]
    public void Setup()
    {
        _smallPdfBytes = ParserCorpus.SyntheticSinglePage();
        _multiPagePdfBytes = ParserCorpus.SyntheticMultiPage(pageCount: 20);
    }

    [Benchmark(Baseline = true, Description = "Open single-page synthetic PDF")]
    public int OpenSinglePage()
    {
        using MemoryStream ms = new(_smallPdfBytes!);
        using PdfDocument doc = PdfDocument.Open(ms);
        return doc.PageCount;
    }

    [Benchmark(Description = "Open 20-page synthetic PDF")]
    public int OpenMultiPage()
    {
        using MemoryStream ms = new(_multiPagePdfBytes!);
        using PdfDocument doc = PdfDocument.Open(ms);
        return doc.PageCount;
    }
}

internal static class ParserCorpus
{
    internal static byte[] SyntheticSinglePage()
    {
        PdfDocumentBuilder doc = PdfDocumentBuilder.Create()
            .SetTitle("Benchmark PDF")
            .SetAuthor("Chuvadi.Benchmarks");
        PageBuilder page = doc.AddPage(PageSize.A4);
        page.DrawText("Benchmark", x: 50, y: 50,
            font: StandardFonts.HelveticaBold,
            size: 14, color: Colors.Black);

        using MemoryStream output = new();
        doc.Save(output);
        return output.ToArray();
    }

    internal static byte[] SyntheticMultiPage(int pageCount)
    {
        PdfDocumentBuilder doc = PdfDocumentBuilder.Create()
            .SetTitle("Multi-page benchmark PDF")
            .SetAuthor("Chuvadi.Benchmarks");
        for (int i = 0; i < pageCount; i++)
        {
            PageBuilder page = doc.AddPage(PageSize.A4);
            page.DrawText($"Page {i + 1} of {pageCount}", x: 50, y: 50,
                font: StandardFonts.Helvetica,
                size: 11, color: Colors.Black);
        }

        using MemoryStream output = new();
        doc.Save(output);
        return output.ToArray();
    }
}
