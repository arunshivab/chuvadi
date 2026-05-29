// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.7 — Object streams
//                          §7.5.8 — Cross-reference streams
// PHASE: Phase 1 — Chuvadi.Pdf.IO tests
//
// Integration tests for the v2.1.7 fix: PdfReader now materialises objects
// that the xref stream marks as compressed (type 2 entries), by loading
// and decoding the containing /ObjStm via ObjectStreamReader. Before the
// fix, PdfReader.LoadObjectFromFile only honoured InUse entries and
// silently returned PdfNull for compressed objects, which propagated as
// missing fonts/resources to higher layers (most visibly: SVG export
// produced blank-looking <text> elements for Ghostscript-style PDFs).
//
// The fixture cff_type1c.pdf is a 6391-byte Ghostscript-round-tripped PDF
// containing the literal string "The quick brown fox 0123456789" in a
// /Type1C-embedded font, an xref stream, and a /Font sub-dict stored as
// a compressed object (object 12 inside the object stream that is object
// 17). All of those compressed-object references resolve correctly only
// after the v2.1.7 fix.

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class CompressedObjectIntegrationTests
{
    private const string FixturePath = "fixtures/cff_type1c.pdf";

    [Fact]
    public void XrefStreamPdf_Opens_WithoutThrowing()
    {
        File.Exists(FixturePath).Should().BeTrue(
            "the test fixture must be deployed to the test output directory");

        using PdfDocument doc = PdfDocument.Open(FixturePath);
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void CompressedObject_ResolvesToExpectedDictionary()
    {
        // Object 12 in cff_type1c.pdf is the page /Resources /Font sub-dict.
        // pikepdf shows it has a single key /R9 pointing at the font dict.
        // Pre-v2.1.7 this resolved to PdfNull because the xref entry is
        // type 2 (compressed inside object stream 17), which the reader
        // did not honour. Post-fix the entry materialises correctly.

        using PdfDocument doc = PdfDocument.Open(FixturePath);

        PdfPrimitive resolved = doc.Objects.ResolveById(new PdfObjectId(12, 0));

        resolved.Should().BeOfType<PdfDictionary>(
            "object 12 is the /Font sub-dict and must resolve through the xref " +
            "stream's compressed-entry path (v2.1.7)");

        PdfDictionary fontSubDict = (PdfDictionary)resolved;
        fontSubDict.TryGetValue(PdfName.Intern("R9"), out PdfPrimitive? _)
            .Should().BeTrue("the page Resources/Font sub-dict carries one font keyed /R9");
    }

    [Fact]
    public void PageResources_Font_DereferencesViaCompressedEntry()
    {
        // End-to-end: from page.Resources we should be able to follow the
        // /Font reference (an indirect ref to a compressed object) and
        // arrive at the same dictionary the previous test reached directly.

        using PdfDocument doc = PdfDocument.Open(FixturePath);
        PdfPage page = doc.Pages[0];

        PdfDictionary? resources = page.Resources;
        resources.Should().NotBeNull("the page must have a Resources dict");

        resources!.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontsPrim)
            .Should().BeTrue("Resources must declare /Font");

        PdfPrimitive resolved = doc.Objects.Resolve(fontsPrim!);
        resolved.Should().BeOfType<PdfDictionary>(
            "/Font resolves through the compressed-object path after v2.1.7");
    }

    [Fact]
    public void Store_LoadsMoreThanBootstrapObjects_WhenCompressedEntriesUsed()
    {
        // Before v2.1.7, repeatedly resolving compressed references would
        // never grow doc.Objects.Count past whatever was eagerly loaded
        // during Open (catalog, page tree root, page 0 — about 3 objects
        // for this fixture). After the fix, each compressed resolution
        // hydrates both the container /ObjStm and the inner object, so
        // the count grows as we touch more references.

        using PdfDocument doc = PdfDocument.Open(FixturePath);
        int countAfterOpen = doc.Objects.Count;

        // Touch the compressed object 12 — this materialises both the
        // container object stream and object 12 itself.
        _ = doc.Objects.ResolveById(new PdfObjectId(12, 0));

        doc.Objects.Count.Should().BeGreaterThan(countAfterOpen,
            "resolving a compressed entry must hydrate the container plus the inner object");
    }
}
