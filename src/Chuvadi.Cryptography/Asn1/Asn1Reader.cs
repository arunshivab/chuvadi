// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 navigation
//
// High-level structured reader. Wraps the low-level (tag, length) codec and
// provides a pull-style API for walking nested ASN.1 structures. Designed for
// the natural shape of X.509, CMS, OCSP, and similar specs: "open a SEQUENCE,
// read its fields in order, close it, verify nothing was left over."

using System;
using System.Numerics;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Pull-style reader for nested ASN.1 BER/DER structures.
/// </summary>
/// <remarks>
/// Construct one over a byte buffer, then call methods that match the expected
/// shape: <see cref="ReadSequence"/> opens a SEQUENCE and returns a sub-reader
/// bounded by its content; <see cref="ReadInteger"/> reads an INTEGER value
/// and advances. Each sub-reader is a separate view over the same buffer with
/// its own bounds — you can have multiple sub-readers active at once, but
/// each must be closed (via <see cref="ExpectEnd"/>) before its parent is.
/// </remarks>
public sealed class Asn1Reader
{
    private readonly byte[] _source;
    private int _pos;
    private readonly int _end;

    /// <summary>Constructs a reader over the entire buffer.</summary>
    public Asn1Reader(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _pos = 0;
        _end = source.Length;
    }

    private Asn1Reader(byte[] source, int start, int end)
    {
        _source = source;
        _pos = start;
        _end = end;
    }

    /// <summary>True when no more bytes remain in this view.</summary>
    public bool IsAtEnd => _pos >= _end;

    /// <summary>The current byte offset within the underlying source.</summary>
    public int Position => _pos;

    /// <summary>Throws if any bytes remain unread.</summary>
    public void ExpectEnd()
    {
        if (!IsAtEnd)
        {
            throw new Asn1Exception(
                $"Unexpected trailing bytes (expected end at {_end}, position is {_pos})", _pos);
        }
    }

    /// <summary>Peeks at the next tag without consuming any bytes.</summary>
    public Asn1Tag PeekTag()
    {
        if (_pos >= _end)
        {
            throw new Asn1Exception("Peek past end of buffer", _pos);
        }
        Asn1TagLength.Read(_source, _pos, out Asn1Tag tag, out _, out _);
        return tag;
    }

    /// <summary>Returns true when the next element's tag matches the given expected tag.</summary>
    public bool TryPeekTag(Asn1Tag expected)
    {
        if (_pos >= _end) { return false; }
        Asn1TagLength.Read(_source, _pos, out Asn1Tag tag, out _, out _);
        return tag == expected;
    }

    // ── Universal type readers ────────────────────────────────────────────

    /// <summary>Opens a SEQUENCE. Returns a sub-reader bounded by its content.</summary>
    public Asn1Reader ReadSequence() => ReadConstructed(Asn1UniversalTag.Sequence);

    /// <summary>Opens a SET. Returns a sub-reader bounded by its content.</summary>
    public Asn1Reader ReadSet() => ReadConstructed(Asn1UniversalTag.Set);

    /// <summary>Reads a BOOLEAN.</summary>
    public bool ReadBoolean()
    {
        _pos = Asn1Boolean.Read(_source, _pos, out bool value);
        return value;
    }

    /// <summary>Reads an INTEGER as BigInteger.</summary>
    public BigInteger ReadInteger()
    {
        _pos = Asn1Integer.Read(_source, _pos, out BigInteger value);
        return value;
    }

    /// <summary>Reads an INTEGER constrained to Int32.</summary>
    public int ReadInt32()
    {
        BigInteger v = ReadInteger();
        if (v < int.MinValue || v > int.MaxValue)
        {
            throw new Asn1Exception("INTEGER does not fit in Int32", _pos);
        }
        return (int)v;
    }

    /// <summary>Reads a NULL.</summary>
    public void ReadNull() { _pos = Asn1Null.Read(_source, _pos); }

    /// <summary>Reads an OCTET STRING.</summary>
    public byte[] ReadOctetString()
    {
        _pos = Asn1OctetString.Read(_source, _pos, out byte[] value);
        return value;
    }

    /// <summary>Reads a BIT STRING.</summary>
    public BitStringValue ReadBitString()
    {
        _pos = Asn1BitString.Read(_source, _pos, out BitStringValue value);
        return value;
    }

    /// <summary>Reads an OBJECT IDENTIFIER.</summary>
    public ObjectIdentifier ReadObjectIdentifier()
    {
        _pos = Asn1ObjectIdentifier.Read(_source, _pos, out ObjectIdentifier oid);
        return oid;
    }

    /// <summary>Reads a UTF8String.</summary>
    public string ReadUtf8String()
    {
        _pos = Asn1String.ReadUtf8(_source, _pos, out string value);
        return value;
    }

    /// <summary>Reads a PrintableString.</summary>
    public string ReadPrintableString()
    {
        _pos = Asn1String.ReadPrintable(_source, _pos, out string value);
        return value;
    }

    /// <summary>Reads an IA5String.</summary>
    public string ReadIA5String()
    {
        _pos = Asn1String.ReadIA5(_source, _pos, out string value);
        return value;
    }

    /// <summary>Reads a BMPString.</summary>
    public string ReadBmpString()
    {
        _pos = Asn1String.ReadBmp(_source, _pos, out string value);
        return value;
    }

    /// <summary>Reads a UTCTime.</summary>
    public DateTimeOffset ReadUtcTime()
    {
        _pos = Asn1Time.ReadUtcTime(_source, _pos, out DateTimeOffset value);
        return value;
    }

    /// <summary>Reads a GeneralizedTime.</summary>
    public DateTimeOffset ReadGeneralizedTime()
    {
        _pos = Asn1Time.ReadGeneralizedTime(_source, _pos, out DateTimeOffset value);
        return value;
    }

    // ── Context-specific tagged elements ──────────────────────────────────

    /// <summary>
    /// Reads an EXPLICITLY tagged context-specific element by descending into
    /// it and returning the inner sub-reader.
    /// </summary>
    public Asn1Reader ReadExplicit(int tagNumber)
    {
        if (_pos >= _end)
        {
            throw new Asn1Exception("Read past end of buffer", _pos);
        }
        int after = Asn1TagLength.Read(_source, _pos, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag.TagClass != Asn1TagClass.ContextSpecific || tag.TagNumber != tagNumber)
        {
            throw new Asn1Exception(
                $"Expected context-specific [{tagNumber}] EXPLICIT, got {tag}", _pos);
        }
        if (!tag.IsConstructed)
        {
            throw new Asn1Exception(
                $"EXPLICIT tags must be constructed; got primitive for [{tagNumber}]", _pos);
        }
        Asn1Reader sub = new(_source, contentOffset, contentOffset + len);
        _pos = after;
        return sub;
    }

    /// <summary>
    /// Reads an IMPLICITLY tagged element. The caller specifies what underlying
    /// universal type it represents; the tag class/number are checked but the
    /// inner content is parsed as if the universal tag were present.
    /// </summary>
    public byte[] ReadImplicitOctets(int tagNumber)
    {
        if (_pos >= _end)
        {
            throw new Asn1Exception("Read past end of buffer", _pos);
        }
        int after = Asn1TagLength.Read(_source, _pos, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag.TagClass != Asn1TagClass.ContextSpecific || tag.TagNumber != tagNumber)
        {
            throw new Asn1Exception(
                $"Expected context-specific [{tagNumber}] IMPLICIT, got {tag}", _pos);
        }
        byte[] result = new byte[len];
        Array.Copy(_source, contentOffset, result, 0, len);
        _pos = after;
        return result;
    }

    /// <summary>
    /// Returns true if a context-specific [<paramref name="tagNumber"/>] element
    /// is next. Useful for OPTIONAL fields.
    /// </summary>
    public bool HasContextSpecific(int tagNumber)
    {
        if (_pos >= _end) { return false; }
        Asn1TagLength.Read(_source, _pos, out Asn1Tag tag, out _, out _);
        return tag.TagClass == Asn1TagClass.ContextSpecific && tag.TagNumber == tagNumber;
    }

    // ── Raw access ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the complete encoded bytes of the next element (tag, length,
    /// content) without consuming it. Useful for capturing a region while still
    /// needing to parse it.
    /// </summary>
    public byte[] PeekEncoded()
    {
        if (_pos >= _end)
        {
            throw new Asn1Exception("Peek past end of buffer", _pos);
        }
        int start = _pos;
        int after = Asn1TagLength.Read(_source, _pos, out _, out _, out _);
        int totalLen = after - start;
        byte[] copy = new byte[totalLen];
        Array.Copy(_source, start, copy, 0, totalLen);
        return copy;
    }

    /// <summary>
    /// Reads the next element and returns its complete encoded bytes including
    /// tag and length. Useful for capturing TBS regions for signature verification.
    /// </summary>
    public byte[] ReadEncoded()
    {
        if (_pos >= _end)
        {
            throw new Asn1Exception("Read past end of buffer", _pos);
        }
        int start = _pos;
        int after = Asn1TagLength.Read(_source, _pos, out _, out _, out _);
        int totalLen = after - start;
        byte[] copy = new byte[totalLen];
        Array.Copy(_source, start, copy, 0, totalLen);
        _pos = after;
        return copy;
    }

    /// <summary>
    /// Skips the next element regardless of tag.
    /// </summary>
    public void Skip()
    {
        if (_pos >= _end)
        {
            throw new Asn1Exception("Skip past end of buffer", _pos);
        }
        _pos = Asn1TagLength.Read(_source, _pos, out _, out _, out _);
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    private Asn1Reader ReadConstructed(Asn1UniversalTag universalTag)
    {
        if (_pos >= _end)
        {
            throw new Asn1Exception("Read past end of buffer", _pos);
        }
        int after = Asn1TagLength.Read(_source, _pos, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Constructed(universalTag))
        {
            throw new Asn1Exception(
                $"Expected constructed {universalTag}, got {tag}", _pos);
        }
        Asn1Reader sub = new(_source, contentOffset, contentOffset + len);
        _pos = after;
        return sub;
    }
}
