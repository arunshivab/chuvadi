// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.3.2, Table 22 — User access permissions
// PHASE: v2.0.2 — regression test for default permission mask
//
// This test guards against a recurrence of the v1.4.0 bug where
// EncryptionOptions defaulted to Permissions = -3904 (which actually
// CLEARED every permission bit) under the mistaken belief that it
// meant "allow everything". The correct value is -4
// (EncryptionOptions.AllPermissionsAllowed).
//
// If anyone changes the default back to a value that denies any
// permission, the AllowPrint assertion below will fail.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class EncryptionDefaultsRoundTripTests
{
    [Fact]
    public void Aes256_DefaultOptions_RoundTripPreservesAllPermissions()
    {
        using MemoryStream output = new MemoryStream();
        EncryptionOptions options = EncryptionOptions.Aes256("user-password");

        // Sanity: the default value is the spec-correct "allow everything".
        options.Permissions.Should().Be(EncryptionOptions.AllPermissionsAllowed);
        options.Permissions.Should().Be(-4);

        WriteMinimalPdf(output, options);

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument doc = PdfDocument.Open(output, "user-password", leaveOpen: true);

        doc.Encryption.Should().NotBeNull();
        doc.Encryption!.Permissions.Should().Be(EncryptionOptions.AllPermissionsAllowed);
        doc.Encryption.AllowPrint.Should().BeTrue();
        doc.Encryption.AllowModify.Should().BeTrue();
        doc.Encryption.AllowCopy.Should().BeTrue();
        doc.Encryption.AllowAnnotate.Should().BeTrue();
        doc.Encryption.AllowFillForms.Should().BeTrue();
        doc.Encryption.AllowAccessibilityExtract.Should().BeTrue();
        doc.Encryption.AllowAssemble.Should().BeTrue();
        doc.Encryption.AllowPrintHighQuality.Should().BeTrue();
    }

    [Fact]
    public void Aes128_DefaultOptions_RoundTripPreservesAllPermissions()
    {
        using MemoryStream output = new MemoryStream();
        EncryptionOptions options = EncryptionOptions.Aes128("user-password");

        options.Permissions.Should().Be(EncryptionOptions.AllPermissionsAllowed);

        WriteMinimalPdf(output, options);

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument doc = PdfDocument.Open(output, "user-password", leaveOpen: true);

        doc.Encryption.Should().NotBeNull();
        doc.Encryption!.Permissions.Should().Be(EncryptionOptions.AllPermissionsAllowed);
        doc.Encryption.AllowPrint.Should().BeTrue();
        doc.Encryption.AllowPrintHighQuality.Should().BeTrue();
    }

    [Fact]
    public void AllPermissionsAllowed_IsMinusFour()
    {
        // The named constant must equal -4. If anyone "fixes" the constant
        // back to -3904, this test should be the loudest possible alarm.
        EncryptionOptions.AllPermissionsAllowed.Should().Be(-4);
    }

    // ── Test PDF builder ──────────────────────────────────────────────────

    private static void WriteMinimalPdf(Stream output, EncryptionOptions encryption)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);

        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792),
        ]));

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        PdfWriter.Write(output, objects, trailer, encryption);
    }
}
