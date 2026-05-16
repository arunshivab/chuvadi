// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 navigation
//
// High-level builder for nested ASN.1 DER structures. Because DER requires
// length prefixes (no indefinite form), we buffer constructed content and
// emit it with the correct length once the construction is closed.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Build-style writer for nested ASN.1 DER structures.
/// </summary>
/// <remarks>
/// Constructed types (SEQUENCE, SET, EXPLICIT tags) need their content length
/// before the content can be emitted. This writer holds a stack of pending
/// constructions, each backed by its own internal buffer. Calling
/// <see cref="PushSequence"/> opens a new constructed scope; subsequent
/// writes go into that scope until <see cref="PopSequence"/> closes it and
/// flushes the assembled bytes into the parent scope (or the output stream
/// if at the root).
/// </remarks>
public sealed class Asn1Writer
{
    private readonly Stack<(Asn1Tag tag, MemoryStream buffer)> _stack = new();
    private readonly MemoryStream _root;

    /// <summary>Creates a writer accumulating to an internal buffer.</summary>
    public Asn1Writer()
    {
        _root = new MemoryStream();
        _stack.Push((default, _root));
    }

    /// <summary>Returns the complete DER bytes. Must not have unclosed constructions.</summary>
    public byte[] ToArray()
    {
        if (_stack.Count != 1)
        {
            throw new InvalidOperationException(
                $"{_stack.Count - 1} unclosed construction(s) on writer stack.");
        }
        return _root.ToArray();
    }

    private Stream Current => _stack.Peek().buffer;

    // ── Constructed scopes ────────────────────────────────────────────────

    /// <summary>Opens a SEQUENCE scope. Subsequent writes accumulate as its content.</summary>
    public void PushSequence() => Push(Asn1Tag.Constructed(Asn1UniversalTag.Sequence));

    /// <summary>Closes the innermost SEQUENCE scope.</summary>
    public void PopSequence() => Pop(Asn1Tag.Constructed(Asn1UniversalTag.Sequence));

    /// <summary>Opens a SET scope.</summary>
    public void PushSet() => Push(Asn1Tag.Constructed(Asn1UniversalTag.Set));

    /// <summary>Closes the innermost SET scope.</summary>
    public void PopSet() => Pop(Asn1Tag.Constructed(Asn1UniversalTag.Set));

    /// <summary>Opens an EXPLICIT context-specific [n] scope.</summary>
    public void PushExplicit(int tagNumber)
        => Push(new Asn1Tag(Asn1TagClass.ContextSpecific, isConstructed: true, tagNumber));

    /// <summary>Closes the innermost EXPLICIT scope.</summary>
    public void PopExplicit(int tagNumber)
        => Pop(new Asn1Tag(Asn1TagClass.ContextSpecific, isConstructed: true, tagNumber));

    // ── Primitive value writers ───────────────────────────────────────────

    /// <summary>Writes a BOOLEAN.</summary>
    public void WriteBoolean(bool value) => Asn1Boolean.Write(Current, value);

    /// <summary>Writes an INTEGER.</summary>
    public void WriteInteger(BigInteger value) => Asn1Integer.Write(Current, value);

    /// <summary>Writes an INTEGER.</summary>
    public void WriteInteger(int value) => Asn1Integer.Write(Current, value);

    /// <summary>Writes an INTEGER.</summary>
    public void WriteInteger(long value) => Asn1Integer.Write(Current, value);

    /// <summary>Writes a NULL.</summary>
    public void WriteNull() => Asn1Null.Write(Current);

    /// <summary>Writes an OCTET STRING.</summary>
    public void WriteOctetString(ReadOnlySpan<byte> value) => Asn1OctetString.Write(Current, value);

    /// <summary>Writes a BIT STRING.</summary>
    public void WriteBitString(BitStringValue value) => Asn1BitString.Write(Current, value);

    /// <summary>Writes a BIT STRING with zero unused bits.</summary>
    public void WriteBitString(ReadOnlySpan<byte> bytes) => Asn1BitString.Write(Current, bytes);

    /// <summary>Writes an OBJECT IDENTIFIER.</summary>
    public void WriteObjectIdentifier(ObjectIdentifier oid) => Asn1ObjectIdentifier.Write(Current, oid);

    /// <summary>Writes a UTF8String.</summary>
    public void WriteUtf8String(string value) => Asn1String.WriteUtf8(Current, value);

    /// <summary>Writes a PrintableString.</summary>
    public void WritePrintableString(string value) => Asn1String.WritePrintable(Current, value);

    /// <summary>Writes an IA5String.</summary>
    public void WriteIA5String(string value) => Asn1String.WriteIA5(Current, value);

    /// <summary>Writes a BMPString.</summary>
    public void WriteBmpString(string value) => Asn1String.WriteBmp(Current, value);

    /// <summary>Writes a UTCTime.</summary>
    public void WriteUtcTime(DateTimeOffset value) => Asn1Time.WriteUtcTime(Current, value);

    /// <summary>Writes a GeneralizedTime.</summary>
    public void WriteGeneralizedTime(DateTimeOffset value) => Asn1Time.WriteGeneralizedTime(Current, value);

    /// <summary>
    /// Writes a raw pre-encoded ASN.1 element. The caller is responsible for the bytes
    /// being valid DER; useful when copying TBS regions verbatim.
    /// </summary>
    public void WriteEncoded(ReadOnlySpan<byte> encoded)
    {
        foreach (byte b in encoded)
        {
            Current.WriteByte(b);
        }
    }

    // ── Internal scope management ─────────────────────────────────────────

    private void Push(Asn1Tag tag)
    {
        _stack.Push((tag, new MemoryStream()));
    }

    private void Pop(Asn1Tag expected)
    {
        if (_stack.Count <= 1)
        {
            throw new InvalidOperationException(
                "No matching push to pop.");
        }
        (Asn1Tag tag, MemoryStream buffer) = _stack.Pop();
        if (tag != expected)
        {
            throw new InvalidOperationException(
                $"Pop tag {expected} does not match push tag {tag}.");
        }
        byte[] content = buffer.ToArray();
        Stream parent = Current;
        Asn1TagLength.Write(parent, tag, content.Length);
        parent.Write(content, 0, content.Length);
    }
}
