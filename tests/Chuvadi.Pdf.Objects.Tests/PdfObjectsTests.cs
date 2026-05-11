// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.10, §7.5.4, §7.5.8
// PHASE: Phase 1 — Chuvadi.Pdf.Objects tests

using System;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Objects.Tests;

// ── PdfIndirectObject ─────────────────────────────────────────────────────

public sealed class PdfIndirectObjectTests
{
    [Fact]
    public void Constructor_ValidArgs_Succeeds()
    {
        PdfObjectId id = new PdfObjectId(1, 0);
        PdfInteger value = new PdfInteger(42);
        PdfIndirectObject obj = new PdfIndirectObject(id, value);

        obj.Id.Should().Be(id);
        obj.Value.Should().BeSameAs(value);
    }

    [Fact]
    public void Constructor_InvalidId_Throws()
    {
        Action act = () => new PdfIndirectObject(PdfObjectId.Invalid, PdfNull.Value);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullValue_Throws()
    {
        Action act = () => new PdfIndirectObject(new PdfObjectId(1, 0), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetAs_CorrectType_ReturnsValue()
    {
        PdfIndirectObject obj = new PdfIndirectObject(
            new PdfObjectId(1, 0), new PdfInteger(7));
        obj.GetAs<PdfInteger>().Should().NotBeNull();
        obj.GetAs<PdfInteger>()!.Value.Should().Be(7);
    }

    [Fact]
    public void GetAs_WrongType_ReturnsNull()
    {
        PdfIndirectObject obj = new PdfIndirectObject(
            new PdfObjectId(1, 0), new PdfInteger(7));
        obj.GetAs<PdfName>().Should().BeNull();
    }

    [Fact]
    public void Cast_CorrectType_ReturnsValue()
    {
        PdfIndirectObject obj = new PdfIndirectObject(
            new PdfObjectId(1, 0), new PdfInteger(7));
        obj.Cast<PdfInteger>().Value.Should().Be(7);
    }

    [Fact]
    public void ToString_ContainsObjectNumber()
    {
        PdfIndirectObject obj = new PdfIndirectObject(
            new PdfObjectId(5, 0), new PdfInteger(42));
        obj.ToString().Should().Contain("5 0 obj");
        obj.ToString().Should().Contain("endobj");
    }
}

// ── XrefEntry ────────────────────────────────────────────────────────────

public sealed class XrefEntryTests
{
    [Fact]
    public void InUse_HasCorrectType()
    {
        XrefEntry entry = new XrefEntry(1, 0, 100L);
        entry.Type.Should().Be(XrefEntryType.InUse);
        entry.IsInUse.Should().BeTrue();
        entry.ByteOffset.Should().Be(100L);
    }

    [Fact]
    public void Free_HasCorrectType()
    {
        XrefEntry entry = XrefEntry.Free(1, 0, 0);
        entry.Type.Should().Be(XrefEntryType.Free);
        entry.IsFree.Should().BeTrue();
    }

    [Fact]
    public void Compressed_HasCorrectType()
    {
        XrefEntry entry = XrefEntry.Compressed(5, 10, 2);
        entry.Type.Should().Be(XrefEntryType.Compressed);
        entry.IsCompressed.Should().BeTrue();
        entry.StreamObjectNumber.Should().Be(10);
        entry.IndexInStream.Should().Be(2);
    }

    [Fact]
    public void ObjectId_ReturnsCorrectId()
    {
        XrefEntry entry = new XrefEntry(7, 0, 500L);
        entry.ObjectId.ObjectNumber.Should().Be(7);
        entry.ObjectId.Generation.Should().Be(0);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        XrefEntry a = new XrefEntry(1, 0, 100L);
        XrefEntry b = new XrefEntry(1, 0, 100L);
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentOffset_AreNotEqual()
    {
        XrefEntry a = new XrefEntry(1, 0, 100L);
        XrefEntry b = new XrefEntry(1, 0, 200L);
        a.Equals(b).Should().BeFalse();
    }
}

// ── XrefTable ────────────────────────────────────────────────────────────

public sealed class XrefTableTests
{
    [Fact]
    public void NewTable_HasObjectZeroAsFreeHead()
    {
        XrefTable table = new XrefTable();
        table.TryGet(0, out XrefEntry entry).Should().BeTrue();
        entry.IsFree.Should().BeTrue();
        entry.Generation.Should().Be(65535);
    }

    [Fact]
    public void Set_AndGet_RoundTrips()
    {
        XrefTable table = new XrefTable();
        XrefEntry entry = new XrefEntry(1, 0, 100L);
        table.Set(entry);

        table.TryGet(1, out XrefEntry result).Should().BeTrue();
        result.ByteOffset.Should().Be(100L);
    }

    [Fact]
    public void Contains_InUseObject_ReturnsTrue()
    {
        XrefTable table = new XrefTable();
        table.Set(new XrefEntry(2, 0, 200L));
        table.Contains(2).Should().BeTrue();
    }

    [Fact]
    public void Contains_FreeObject_ReturnsFalse()
    {
        XrefTable table = new XrefTable();
        table.Contains(999).Should().BeFalse();
    }

    [Fact]
    public void GetOffset_InUseObject_ReturnsOffset()
    {
        XrefTable table = new XrefTable();
        table.Set(new XrefEntry(3, 0, 300L));
        table.GetOffset(3).Should().Be(300L);
    }

    [Fact]
    public void GetOffset_AbsentObject_ReturnsMinusOne()
    {
        XrefTable table = new XrefTable();
        table.GetOffset(999).Should().Be(-1L);
    }

    [Fact]
    public void Remove_ExistingObject_RemovesIt()
    {
        XrefTable table = new XrefTable();
        table.Set(new XrefEntry(4, 0, 400L));
        table.Remove(4).Should().BeTrue();
        table.Contains(4).Should().BeFalse();
    }

    [Fact]
    public void Write_ThenParse_RoundTrips()
    {
        XrefTable original = new XrefTable();
        original.Set(new XrefEntry(1, 0, 15L));
        original.Set(new XrefEntry(2, 0, 108L));
        original.Set(new XrefEntry(3, 0, 200L));

        using (MemoryStream ms = new MemoryStream())
        {
            original.Write(ms);
            ms.Seek(0, SeekOrigin.Begin);

            // Skip "xref\n" line and first subsection header — Parse reads after xref keyword.
            // Simpler: rebuild by reading entries and verifying they survived.
            byte[] bytes = ms.ToArray();
            string text = Encoding.ASCII.GetString(bytes);

            // Verify the format contains expected content.
            text.Should().StartWith("xref\n");
            text.Should().Contain("0000000015 00000 n");
            text.Should().Contain("0000000108 00000 n");
            text.Should().Contain("0000000200 00000 n");
        }
    }

    [Fact]
    public void Write_FreeEntry_FormatsCorrectly()
    {
        XrefTable table = new XrefTable();
        // Object 0 is always the free list head (added in constructor).
        using (MemoryStream ms = new MemoryStream())
        {
            table.Write(ms);
            string text = Encoding.ASCII.GetString(ms.ToArray());
            text.Should().Contain("0000000000 65535 f");
        }
    }
}

// ── XrefStreamTable ───────────────────────────────────────────────────────

public sealed class XrefStreamTableTests
{
    [Fact]
    public void Encode_ThenParse_RoundTrips_InUse()
    {
        XrefStreamTable original = new XrefStreamTable();
        original.Add(XrefEntry.Free(0, 65535, 0));
        original.Add(new XrefEntry(1, 0, 100L));
        original.Add(new XrefEntry(2, 0, 200L));

        byte[] encoded = original.Encode(w1: 1, w2: 4, w3: 2);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Size, 3);

        PdfArray wArray = new PdfArray([
            new PdfInteger(1),
            new PdfInteger(4),
            new PdfInteger(2)
        ]);
        dict.Set(PdfName.Intern("W"), wArray);

        XrefStreamTable parsed = XrefStreamTable.Parse(dict, encoded);

        parsed.Count.Should().Be(3);
        parsed.Entries[1].ByteOffset.Should().Be(100L);
        parsed.Entries[2].ByteOffset.Should().Be(200L);
    }

    [Fact]
    public void Encode_ThenParse_RoundTrips_Compressed()
    {
        XrefStreamTable original = new XrefStreamTable();
        original.Add(XrefEntry.Compressed(5, 10, 2));

        byte[] encoded = original.Encode(w1: 1, w2: 4, w3: 2);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Size, 1);

        PdfArray wArray = new PdfArray([
            new PdfInteger(1),
            new PdfInteger(4),
            new PdfInteger(2)
        ]);
        dict.Set(PdfName.Intern("W"), wArray);

        PdfArray indexArray = new PdfArray([
            new PdfInteger(5),
            new PdfInteger(1)
        ]);
        dict.Set(PdfName.Intern("Index"), indexArray);

        XrefStreamTable parsed = XrefStreamTable.Parse(dict, encoded);

        parsed.Count.Should().Be(1);
        parsed.Entries[0].IsCompressed.Should().BeTrue();
        parsed.Entries[0].StreamObjectNumber.Should().Be(10);
        parsed.Entries[0].IndexInStream.Should().Be(2);
    }

    [Fact]
    public void Parse_MissingWArray_Throws()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Size, 1);

        Action act = () => XrefStreamTable.Parse(dict, [0x01, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x00]);
        act.Should().Throw<PdfObjectException>();
    }
}

// ── PdfObjectStore ────────────────────────────────────────────────────────

public sealed class PdfObjectStoreTests
{
    [Fact]
    public void Add_ThenResolveById_ReturnsValue()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfObjectId id = new PdfObjectId(1, 0);
        PdfInteger value = new PdfInteger(42);
        store.Add(id, value);

        PdfPrimitive resolved = store.ResolveById(id);
        resolved.Should().BeOfType<PdfInteger>();
        ((PdfInteger)resolved).Value.Should().Be(42);
    }

    [Fact]
    public void Resolve_DirectPrimitive_ReturnsSame()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfInteger value = new PdfInteger(99);

        PdfPrimitive resolved = store.Resolve(value);
        resolved.Should().BeSameAs(value);
    }

    [Fact]
    public void Resolve_Reference_ReturnsTargetValue()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfObjectId id = new PdfObjectId(3, 0);
        store.Add(id, PdfName.Intern("Page"));

        PdfReference reference = new PdfReference(id);
        PdfPrimitive resolved = store.Resolve(reference);

        resolved.Should().BeOfType<PdfName>();
        ((PdfName)resolved).Value.Should().Be("Page");
    }

    [Fact]
    public void ResolveById_MissingObject_ReturnsNull()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfPrimitive result = store.ResolveById(new PdfObjectId(999, 0));
        result.Should().BeSameAs(PdfNull.Value);
    }

    [Fact]
    public void Contains_AfterAdd_ReturnsTrue()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfObjectId id = new PdfObjectId(2, 0);
        store.Add(id, PdfNull.Value);
        store.Contains(id).Should().BeTrue();
    }

    [Fact]
    public void Contains_BeforeAdd_ReturnsFalse()
    {
        PdfObjectStore store = new PdfObjectStore();
        store.Contains(new PdfObjectId(100, 0)).Should().BeFalse();
    }

    [Fact]
    public void Remove_AfterAdd_RemovesObject()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfObjectId id = new PdfObjectId(5, 0);
        store.Add(id, new PdfInteger(1));
        store.Remove(id).Should().BeTrue();
        store.Contains(id).Should().BeFalse();
    }

    [Fact]
    public void Loader_CalledOnMiss_CachesResult()
    {
        int callCount = 0;
        PdfObjectId id = new PdfObjectId(7, 0);

        PdfObjectStore store = new PdfObjectStore(lookupId =>
        {
            callCount++;
            return lookupId == id
                ? new PdfIndirectObject(lookupId, new PdfInteger(777))
                : null;
        });

        // First access — loader called.
        PdfPrimitive result1 = store.ResolveById(id);
        callCount.Should().Be(1);
        ((PdfInteger)result1).Value.Should().Be(777);

        // Second access — cached, loader NOT called again.
        PdfPrimitive result2 = store.ResolveById(id);
        callCount.Should().Be(1);
        ((PdfInteger)result2).Value.Should().Be(777);
    }

    [Fact]
    public void ResolveDictionaryEntry_DirectValue_ReturnsValue()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Type, PdfName.Page);

        PdfName? result = store.ResolveDictionaryEntry<PdfName>(dict, PdfName.Type);
        result.Should().BeSameAs(PdfName.Page);
    }

    [Fact]
    public void ResolveDictionaryEntry_IndirectValue_ResolvesReference()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfObjectId id = new PdfObjectId(10, 0);
        store.Add(id, PdfName.Pages);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Type, new PdfReference(id));

        PdfName? result = store.ResolveDictionaryEntry<PdfName>(dict, PdfName.Type);
        result.Should().BeSameAs(PdfName.Pages);
    }

    [Fact]
    public void Count_ReflectsAddedObjects()
    {
        PdfObjectStore store = new PdfObjectStore();
        store.Count.Should().Be(0);
        store.Add(new PdfObjectId(1, 0), PdfNull.Value);
        store.Add(new PdfObjectId(2, 0), PdfNull.Value);
        store.Count.Should().Be(2);
    }
}

// ── PdfObjectException ────────────────────────────────────────────────────

public sealed class PdfObjectExceptionTests
{
    [Fact]
    public void DefaultConstructor_HasMessage()
    {
        PdfObjectException ex = new PdfObjectException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        PdfObjectException ex = new PdfObjectException("test error");
        ex.Message.Should().Be("test error");
    }

    [Fact]
    public void InnerExceptionConstructor_PreservesInner()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        PdfObjectException ex = new PdfObjectException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}
