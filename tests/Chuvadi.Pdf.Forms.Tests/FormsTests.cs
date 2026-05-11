// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7, §12.3.3
// PHASE: Phase 2 — Chuvadi.Pdf.Forms tests

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Forms.Tests;

// ── FormException ─────────────────────────────────────────────────────────

public sealed class FormExceptionTests
{
    [Fact]
    public void DefaultConstructor_HasMessage()
    {
        FormException ex = new FormException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        FormException ex = new FormException("bad field");
        ex.Message.Should().Be("bad field");
    }

    [Fact]
    public void InnerExceptionConstructor_PreservesInner()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        FormException ex = new FormException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}

// ── FormField ─────────────────────────────────────────────────────────────

public sealed class FormFieldTests
{
    [Fact]
    public void Constructor_NullName_Throws()
    {
        Action act = () => new FormField(null!, FormFieldType.Text, null,
            new PdfObjectId(1, 0), new List<FormField>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullChildren_Throws()
    {
        Action act = () => new FormField("x", FormFieldType.Text, null,
            new PdfObjectId(1, 0), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsLeaf_NoChildren_IsTrue()
    {
        FormField field = new FormField("x", FormFieldType.Text, "v",
            new PdfObjectId(1, 0), new List<FormField>());
        field.IsLeaf.Should().BeTrue();
    }
}

// ── OutlineItem ───────────────────────────────────────────────────────────

public sealed class OutlineItemTests
{
    [Fact]
    public void Constructor_NullTitle_Throws()
    {
        Action act = () => new OutlineItem(null!, 0, new List<OutlineItem>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_SetsAllFields()
    {
        OutlineItem item = new OutlineItem("Chapter 1", 3, new List<OutlineItem>());
        item.Title.Should().Be("Chapter 1");
        item.DestinationPageIndex.Should().Be(3);
    }
}

// ── FormReader ────────────────────────────────────────────────────────────

public sealed partial class FormReaderTests
{
    [Fact]
    public void GetFields_NullDocument_Throws()
    {
        Action act = () => FormReader.GetFields(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetFields_NoAcroForm_ReturnsEmpty()
    {
        using (MemoryStream ms = BuildPlainPdf())
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            IReadOnlyList<FormField> fields = FormReader.GetFields(doc);
            fields.Should().BeEmpty();
        }
    }

    [Fact]
    public void GetFields_WithTextField_ReadsName()
    {
        using (MemoryStream ms = BuildFormPdf("firstName", "Tx", "John"))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            IReadOnlyList<FormField> fields = FormReader.GetFields(doc);
            fields.Should().HaveCount(1);
            fields[0].FullyQualifiedName.Should().Be("firstName");
            fields[0].Type.Should().Be(FormFieldType.Text);
            fields[0].Value.Should().Be("John");
        }
    }

    [Fact]
    public void GetLeafFields_ReturnsLeavesOnly()
    {
        using (MemoryStream ms = BuildFormPdf("email", "Tx", null))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            IReadOnlyList<FormField> leaves = FormReader.GetLeafFields(doc);
            leaves.Should().HaveCount(1);
            leaves[0].IsLeaf.Should().BeTrue();
        }
    }
}

// ── FormFiller ────────────────────────────────────────────────────────────

public sealed partial class FormFillerTests
{
    [Fact]
    public void Fill_NullOutput_Throws()
    {
        using (MemoryStream ms = BuildPlainPdf())
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            Action act = () => FormFiller.Fill(null!, doc, new Dictionary<string, string>());
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void Fill_NullDocument_Throws()
    {
        Action act = () => FormFiller.Fill(new MemoryStream(), null!, new Dictionary<string, string>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Fill_NullValues_Throws()
    {
        using (MemoryStream ms = BuildPlainPdf())
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            Action act = () => FormFiller.Fill(new MemoryStream(), doc, null!);
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void Fill_UpdatesFieldValue()
    {
        using (MemoryStream source = BuildFormPdf("name", "Tx", "OldValue"))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                ["name"] = "NewValue",
            };
            FormFiller.Fill(output, doc, values);

            output.Seek(0, SeekOrigin.Begin);

            using (PdfDocument result = PdfDocument.Open(output, leaveOpen: true))
            {
                IReadOnlyList<FormField> fields = FormReader.GetFields(result);
                fields.Should().HaveCount(1);
                fields[0].Value.Should().Be("NewValue");
            }
        }
    }

    [Fact]
    public void Fill_SetsNeedAppearancesTrue()
    {
        using (MemoryStream source = BuildFormPdf("x", "Tx", "v"))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                ["x"] = "new",
            };
            FormFiller.Fill(output, doc, values);

            output.Seek(0, SeekOrigin.Begin);
            string outputText = Encoding.Latin1.GetString(output.ToArray());
            outputText.Should().Contain("NeedAppearances");
        }
    }
}

// ── OutlineReader ─────────────────────────────────────────────────────────

public sealed partial class OutlineReaderTests
{
    [Fact]
    public void GetOutlines_NullDocument_Throws()
    {
        Action act = () => OutlineReader.GetOutlines(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetOutlines_NoOutlines_ReturnsEmpty()
    {
        using (MemoryStream ms = BuildPlainPdf())
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(doc);
            outlines.Should().BeEmpty();
        }
    }
}

// ── PDF builder helpers ───────────────────────────────────────────────────

internal static class Builder
{
    internal static MemoryStream BuildPlainPdfImpl()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId   = new PdfObjectId(2, 0);
        PdfObjectId pageId    = new PdfObjectId(3, 0);

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

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    internal static MemoryStream BuildFormPdfImpl(string fieldName, string fieldType, string? value)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId   = new PdfObjectId(2, 0);
        PdfObjectId pageId    = new PdfObjectId(3, 0);
        PdfObjectId acroFormId = new PdfObjectId(4, 0);
        PdfObjectId fieldId   = new PdfObjectId(5, 0);

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

        // AcroForm field
        PdfDictionary fieldDict = new PdfDictionary();
        fieldDict.Set(PdfName.Intern("FT"), PdfName.Intern(fieldType));
        fieldDict.Set(PdfName.Intern("T"), new PdfString(Encoding.Latin1.GetBytes(fieldName)));

        if (value is not null)
        {
            fieldDict.Set(PdfName.Intern("V"), new PdfString(Encoding.Latin1.GetBytes(value)));
        }

        // AcroForm dict
        PdfDictionary acroFormDict = new PdfDictionary();
        acroFormDict.Set(PdfName.Intern("Fields"), new PdfArray([new PdfReference(fieldId)]));

        // Catalog with AcroForm reference
        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        catalogDict.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(acroFormId, acroFormDict),
            new PdfIndirectObject(fieldId, fieldDict),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }
}

// Bridge to allow test classes to call internal builders without each having a static method
public sealed partial class FormReaderTests
{
    internal static MemoryStream BuildPlainPdf() => Builder.BuildPlainPdfImpl();
    internal static MemoryStream BuildFormPdf(string n, string t, string? v) =>
        Builder.BuildFormPdfImpl(n, t, v);
}

public sealed partial class FormFillerTests
{
    internal static MemoryStream BuildPlainPdf() => Builder.BuildPlainPdfImpl();
    internal static MemoryStream BuildFormPdf(string n, string t, string? v) =>
        Builder.BuildFormPdfImpl(n, t, v);
}

public sealed partial class OutlineReaderTests
{
    internal static MemoryStream BuildPlainPdf() => Builder.BuildPlainPdfImpl();
}
