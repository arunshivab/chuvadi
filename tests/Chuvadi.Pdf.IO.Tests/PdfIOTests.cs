// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure
// PHASE: Phase 1 — Chuvadi.Pdf.IO tests

using System;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class PdfWriterTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static MemoryStream BuildMinimalPdf()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([]));
        pagesDict.Set(PdfName.Count, 0);

        PdfIndirectObject[] objects =
        [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    // ── PdfReaderException ────────────────────────────────────────────────

    [Fact]
    public void PdfReaderException_DefaultConstructor_HasMessage()
    {
        PdfReaderException ex = new PdfReaderException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void PdfReaderException_Message_Preserved()
    {
        PdfReaderException ex = new PdfReaderException("test error");
        ex.Message.Should().Be("test error");
    }

    [Fact]
    public void PdfReaderException_InnerException_Preserved()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        PdfReaderException ex = new PdfReaderException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    // ── PdfWriter — structural correctness ────────────────────────────────

    [Fact]
    public void Write_ProducesNonEmptyStream()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Length.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Write_StartsWithPdfHeader()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[8];
            ms.Read(header, 0, 8);
            Encoding.ASCII.GetString(header).Should().StartWith("%PDF-1.");
        }
    }

    [Fact]
    public void Write_ContainsStartxrefAndEof()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            string content = Encoding.Latin1.GetString(ms.ToArray());
            content.Should().Contain("startxref");
            content.Should().Contain("%%EOF");
        }
    }

    [Fact]
    public void Write_ContainsXrefSection()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            string content = Encoding.Latin1.GetString(ms.ToArray());
            content.Should().Contain("xref");
            content.Should().Contain("trailer");
        }
    }

    [Fact]
    public void Write_ContainsObjectDefinitions()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            string content = Encoding.Latin1.GetString(ms.ToArray());
            content.Should().Contain("1 0 obj");
            content.Should().Contain("2 0 obj");
            content.Should().Contain("endobj");
        }
    }

    [Fact]
    public void Write_NullOutput_Throws()
    {
        Action act = () => PdfWriter.Write(null!, [], new PdfDictionary());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Write_NullObjects_Throws()
    {
        Action act = () => PdfWriter.Write(new MemoryStream(), null!, new PdfDictionary());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Write_NullTrailer_Throws()
    {
        Action act = () => PdfWriter.Write(new MemoryStream(), [], null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── PdfReader — open written PDF ──────────────────────────────────────

    [Fact]
    public void Reader_Open_ReadsTrailer()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfReader reader = PdfReader.Open(ms, leaveOpen: true))
            {
                reader.Trailer.Should().NotBeNull();
                reader.Trailer.ContainsKey(PdfName.Root).Should().BeTrue();
                reader.Trailer.ContainsKey(PdfName.Size).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void Reader_Open_ReadsCatalog()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfReader reader = PdfReader.Open(ms, leaveOpen: true))
            {
                PdfDictionary? catalog = reader.Catalog;
                catalog.Should().NotBeNull();
                catalog!.Type.Should().Be(PdfName.Catalog);
            }
        }
    }

    [Fact]
    public void Reader_Open_ReadsPageTree()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfReader reader = PdfReader.Open(ms, leaveOpen: true))
            {
                PdfDictionary? catalog = reader.Catalog;
                catalog.Should().NotBeNull();

                PdfPrimitive? pagesRef = catalog!.GetAs<PdfPrimitive>(PdfName.Pages);
                pagesRef.Should().BeOfType<PdfReference>();

                PdfPrimitive resolvedPages = reader.Objects.Resolve(pagesRef!);
                resolvedPages.Should().BeOfType<PdfDictionary>();

                PdfDictionary pagesDict = (PdfDictionary)resolvedPages;
                pagesDict.Type.Should().Be(PdfName.Pages);
                pagesDict.GetInteger(PdfName.Count).Should().Be(0);
            }
        }
    }

    [Fact]
    public void Reader_Open_NullStream_Throws()
    {
        Action act = () => PdfReader.Open(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reader_Open_EmptyStream_Throws()
    {
        using (MemoryStream ms = new MemoryStream())
        {
            Action act = () => PdfReader.Open(ms, leaveOpen: true);
            act.Should().Throw<PdfReaderException>();
        }
    }

    // ── Round-trip: integer value preserved ───────────────────────────────

    [Fact]
    public void RoundTrip_IntegerValue_PreservesValue()
    {
        using (MemoryStream ms = new MemoryStream())
        {
            PdfObjectId catalogId = new PdfObjectId(1, 0);
            PdfObjectId pagesId = new PdfObjectId(2, 0);
            PdfObjectId dataId = new PdfObjectId(3, 0);

            PdfDictionary catalogDict = new PdfDictionary();
            catalogDict.Set(PdfName.Type, PdfName.Catalog);
            catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

            PdfDictionary pagesDict = new PdfDictionary();
            pagesDict.Set(PdfName.Type, PdfName.Pages);
            pagesDict.Set(PdfName.Kids, new PdfArray([]));
            pagesDict.Set(PdfName.Count, 0);

            PdfDictionary dataDict = new PdfDictionary();
            dataDict.Set(PdfName.Intern("Answer"), (PdfPrimitive)new PdfInteger(42));

            PdfIndirectObject[] objects =
            [
                new PdfIndirectObject(catalogId, catalogDict),
                new PdfIndirectObject(pagesId, pagesDict),
                new PdfIndirectObject(dataId, dataDict),
            ];

            PdfDictionary trailer = new PdfDictionary();
            trailer.Set(PdfName.Root, new PdfReference(catalogId));

            PdfWriter.Write(ms, objects, trailer);
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfReader reader = PdfReader.Open(ms, leaveOpen: true))
            {
                PdfPrimitive dataObj = reader.Objects.ResolveById(dataId);
                dataObj.Should().BeOfType<PdfDictionary>();

                PdfDictionary dataResult = (PdfDictionary)dataObj;
                dataResult.GetInteger(PdfName.Intern("Answer")).Should().Be(42);
            }
        }
    }
}
