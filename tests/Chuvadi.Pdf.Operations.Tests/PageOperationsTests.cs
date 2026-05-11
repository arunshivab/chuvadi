// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 — Page tree
// PHASE: Phase 1 — Chuvadi.Pdf.Operations tests

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PageOperationsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static MemoryStream BuildPdf(int pageCount)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kidsArray = new PdfArray([]);
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kidsArray);
        pagesDict.Set(PdfName.Count, pageCount);
        PdfArray mediaBox = new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(595), new PdfInteger(842)
        ]);
        pagesDict.Set(PdfName.MediaBox, mediaBox);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        };

        for (int i = 0; i < pageCount; i++)
        {
            PdfObjectId pageId = new PdfObjectId(3 + i, 0);
            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            objects.Add(new PdfIndirectObject(pageId, pageDict));
            kidsArray.Add(new PdfReference(pageId));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    private static PdfDocument OpenPdf(MemoryStream ms)
    {
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }

    // ── OperationsException ───────────────────────────────────────────────

    [Fact]
    public void OperationsException_DefaultConstructor_HasMessage()
    {
        OperationsException ex = new OperationsException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void OperationsException_MessagePreserved()
    {
        OperationsException ex = new OperationsException("test");
        ex.Message.Should().Be("test");
    }

    [Fact]
    public void OperationsException_InnerExceptionPreserved()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        OperationsException ex = new OperationsException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    // ── Merge ─────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_NullOutput_Throws()
    {
        using (MemoryStream src = BuildPdf(1))
        using (PdfDocument doc = OpenPdf(src))
        {
            Action act = () => PageOperations.Merge(null!, doc);
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void Merge_NullDocuments_Throws()
    {
        Action act = () => PageOperations.Merge(new MemoryStream(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Merge_EmptyDocuments_Throws()
    {
        Action act = () => PageOperations.Merge(new MemoryStream());
        act.Should().Throw<OperationsException>();
    }

    [Fact]
    public void Merge_TwoDocuments_CombinesPageCount()
    {
        using (MemoryStream src1 = BuildPdf(2))
        using (MemoryStream src2 = BuildPdf(3))
        using (PdfDocument doc1 = OpenPdf(src1))
        using (PdfDocument doc2 = OpenPdf(src2))
        using (MemoryStream output = new MemoryStream())
        {
            PageOperations.Merge(output, doc1, doc2);
            output.Length.Should().BeGreaterThan(0);

            using (PdfDocument merged = OpenPdf(output))
            {
                merged.PageCount.Should().Be(5);
            }
        }
    }

    [Fact]
    public void Merge_SingleDocument_PreservesPages()
    {
        using (MemoryStream src = BuildPdf(3))
        using (PdfDocument doc = OpenPdf(src))
        using (MemoryStream output = new MemoryStream())
        {
            PageOperations.Merge(output, doc);

            using (PdfDocument result = OpenPdf(output))
            {
                result.PageCount.Should().Be(3);
            }
        }
    }

    // ── Split ─────────────────────────────────────────────────────────────

    [Fact]
    public void SplitPages_NullDocument_Throws()
    {
        Action act = () => PageOperations.SplitPages(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SplitPages_ThreePages_ReturnsThreeStreams()
    {
        using (MemoryStream src = BuildPdf(3))
        using (PdfDocument doc = OpenPdf(src))
        {
            List<MemoryStream> pages = PageOperations.SplitPages(doc);
            pages.Should().HaveCount(3);

            foreach (MemoryStream page in pages)
            {
                page.Length.Should().BeGreaterThan(0);

                using (PdfDocument single = OpenPdf(page))
                {
                    single.PageCount.Should().Be(1);
                }

                page.Dispose();
            }
        }
    }

    // ── ExtractPages ──────────────────────────────────────────────────────

    [Fact]
    public void ExtractPages_NullOutput_Throws()
    {
        using (MemoryStream src = BuildPdf(3))
        using (PdfDocument doc = OpenPdf(src))
        {
            Action act = () => PageOperations.ExtractPages(null!, doc, 0, 1);
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void ExtractPages_ValidRange_ExtractsCorrectCount()
    {
        using (MemoryStream src = BuildPdf(5))
        using (PdfDocument doc = OpenPdf(src))
        using (MemoryStream output = new MemoryStream())
        {
            PageOperations.ExtractPages(output, doc, 1, 3);

            using (PdfDocument result = OpenPdf(output))
            {
                result.PageCount.Should().Be(3);
            }
        }
    }

    [Fact]
    public void ExtractPages_InvalidStart_Throws()
    {
        using (MemoryStream src = BuildPdf(3))
        using (PdfDocument doc = OpenPdf(src))
        {
            Action act = () => PageOperations.ExtractPages(new MemoryStream(), doc, 10, 1);
            act.Should().Throw<OperationsException>();
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [Fact]
    public void DeletePages_NullOutput_Throws()
    {
        using (MemoryStream src = BuildPdf(2))
        using (PdfDocument doc = OpenPdf(src))
        {
            Action act = () => PageOperations.DeletePages(null!, doc, [0]);
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void DeletePages_OneOfThree_LeavesTwo()
    {
        using (MemoryStream src = BuildPdf(3))
        using (PdfDocument doc = OpenPdf(src))
        using (MemoryStream output = new MemoryStream())
        {
            PageOperations.DeletePages(output, doc, [1]);

            using (PdfDocument result = OpenPdf(output))
            {
                result.PageCount.Should().Be(2);
            }
        }
    }

    [Fact]
    public void DeletePages_AllPages_Throws()
    {
        using (MemoryStream src = BuildPdf(2))
        using (PdfDocument doc = OpenPdf(src))
        {
            Action act = () => PageOperations.DeletePages(new MemoryStream(), doc, [0, 1]);
            act.Should().Throw<OperationsException>();
        }
    }

    // ── Rotate ────────────────────────────────────────────────────────────

    [Fact]
    public void RotatePages_InvalidDegrees_Throws()
    {
        using (MemoryStream src = BuildPdf(1))
        using (PdfDocument doc = OpenPdf(src))
        {
            Action act = () => PageOperations.RotatePages(new MemoryStream(), doc, 45);
            act.Should().Throw<OperationsException>();
        }
    }

    [Fact]
    public void RotatePages_AllPages_ProducesValidPdf()
    {
        using (MemoryStream src = BuildPdf(2))
        using (PdfDocument doc = OpenPdf(src))
        using (MemoryStream output = new MemoryStream())
        {
            PageOperations.RotatePages(output, doc, 90);

            using (PdfDocument result = OpenPdf(output))
            {
                result.PageCount.Should().Be(2);
                result.Pages[0].Rotate.Should().Be(90);
                result.Pages[1].Rotate.Should().Be(90);
            }
        }
    }

    [Fact]
    public void RotatePages_SpecificPage_OnlyRotatesThatPage()
    {
        using (MemoryStream src = BuildPdf(3))
        using (PdfDocument doc = OpenPdf(src))
        using (MemoryStream output = new MemoryStream())
        {
            PageOperations.RotatePages(output, doc, 180, [1]);

            using (PdfDocument result = OpenPdf(output))
            {
                result.Pages[0].Rotate.Should().Be(0);
                result.Pages[1].Rotate.Should().Be(180);
                result.Pages[2].Rotate.Should().Be(0);
            }
        }
    }

    // ── Reorder ───────────────────────────────────────────────────────────

    [Fact]
    public void ReorderPages_NullNewOrder_Throws()
    {
        using (MemoryStream src = BuildPdf(2))
        using (PdfDocument doc = OpenPdf(src))
        {
            Action act = () => PageOperations.ReorderPages(new MemoryStream(), doc, null!);
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void ReorderPages_WrongCount_Throws()
    {
        using (MemoryStream src = BuildPdf(3))
        using (PdfDocument doc = OpenPdf(src))
        {
            Action act = () => PageOperations.ReorderPages(
                new MemoryStream(), doc, [0, 1]);
            act.Should().Throw<OperationsException>();
        }
    }

    [Fact]
    public void ReorderPages_Reversed_ProducesValidPdf()
    {
        using (MemoryStream src = BuildPdf(3))
        using (PdfDocument doc = OpenPdf(src))
        using (MemoryStream output = new MemoryStream())
        {
            PageOperations.ReorderPages(output, doc, [2, 1, 0]);

            using (PdfDocument result = OpenPdf(output))
            {
                result.PageCount.Should().Be(3);
            }
        }
    }
}
