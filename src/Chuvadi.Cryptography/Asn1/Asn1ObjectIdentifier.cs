// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.690 §8.19 — OBJECT IDENTIFIER encoding
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 values

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// An ASN.1 OBJECT IDENTIFIER — an ordered sequence of non-negative arcs.
/// </summary>
/// <remarks>
/// First arc is constrained to 0, 1, or 2 (X.690 §8.19.4). When first arc is
/// 0 or 1 the second arc is 0..39. When first arc is 2 the second arc may be
/// any non-negative integer. The encoding packs the first two arcs into a
/// single value: <c>40 * arc1 + arc2</c>, then each subsequent arc is encoded
/// as a base-128 big-endian SubIdentifier with continuation bits.
/// </remarks>
public sealed class ObjectIdentifier : IEquatable<ObjectIdentifier>
{
    private readonly long[] _arcs;
    private readonly string _dotted;

    /// <summary>Initialises an OID from its arcs.</summary>
    public ObjectIdentifier(params long[] arcs)
    {
        ArgumentNullException.ThrowIfNull(arcs);
        if (arcs.Length < 2)
        {
            throw new ArgumentException("OID must have at least two arcs.", nameof(arcs));
        }
        for (int i = 0; i < arcs.Length; i++)
        {
            if (arcs[i] < 0)
            {
                throw new ArgumentException("OID arcs must be non-negative.", nameof(arcs));
            }
        }
        if (arcs[0] > 2)
        {
            throw new ArgumentException(
                "OID first arc must be 0, 1, or 2.", nameof(arcs));
        }
        if (arcs[0] < 2 && arcs[1] > 39)
        {
            throw new ArgumentException(
                "OID second arc must be 0..39 when first arc is 0 or 1.", nameof(arcs));
        }

        _arcs = (long[])arcs.Clone();
        StringBuilder sb = new();
        for (int i = 0; i < _arcs.Length; i++)
        {
            if (i > 0) { sb.Append('.'); }
            sb.Append(_arcs[i].ToString(CultureInfo.InvariantCulture));
        }
        _dotted = sb.ToString();
    }

    /// <summary>Initialises an OID from dotted form (e.g. "1.2.840.113549.1.7.2").</summary>
    public ObjectIdentifier(string dotted) : this(ParseDotted(dotted)) { }

    /// <summary>The arcs as an array (defensive copy).</summary>
    public long[] Arcs => (long[])_arcs.Clone();

    /// <summary>The OID in dotted-decimal form.</summary>
    public string Dotted => _dotted;

    /// <inheritdoc/>
    public override string ToString() => _dotted;

    /// <inheritdoc/>
    public bool Equals(ObjectIdentifier? other)
    {
        if (other is null) { return false; }
        if (_arcs.Length != other._arcs.Length) { return false; }
        for (int i = 0; i < _arcs.Length; i++)
        {
            if (_arcs[i] != other._arcs[i]) { return false; }
        }
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ObjectIdentifier);

    /// <inheritdoc/>
    public override int GetHashCode() => _dotted.GetHashCode(StringComparison.Ordinal);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ObjectIdentifier? left, ObjectIdentifier? right)
    {
        if (left is null) { return right is null; }
        return left.Equals(right);
    }

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ObjectIdentifier? left, ObjectIdentifier? right)
        => !(left == right);

    private static long[] ParseDotted(string dotted)
    {
        ArgumentNullException.ThrowIfNull(dotted);
        string[] parts = dotted.Split('.');
        long[] arcs = new long[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out arcs[i]))
            {
                throw new ArgumentException(
                    $"Invalid OID arc '{parts[i]}' in '{dotted}'.", nameof(dotted));
            }
        }
        return arcs;
    }
}

/// <summary>Encode and decode ASN.1 OBJECT IDENTIFIER values.</summary>
public static class Asn1ObjectIdentifier
{
    /// <summary>Writes an OID in DER form.</summary>
    public static void Write(Stream output, ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(oid);
        byte[] content = EncodeContent(oid);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.ObjectIdentifier), content.Length);
        output.Write(content, 0, content.Length);
    }

    /// <summary>Encodes the OID's content octets without tag/length.</summary>
    public static byte[] EncodeContent(ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(oid);
        long[] arcs = oid.Arcs;

        using MemoryStream ms = new();

        // First two arcs packed into a single SubIdentifier
        long first = (40 * arcs[0]) + arcs[1];
        WriteSubIdentifier(ms, first);

        // Subsequent arcs each encoded as one SubIdentifier
        for (int i = 2; i < arcs.Length; i++)
        {
            WriteSubIdentifier(ms, arcs[i]);
        }

        return ms.ToArray();
    }

    /// <summary>Reads an OID. Returns the offset just past the encoded value.</summary>
    public static int Read(byte[] source, int offset, out ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.ObjectIdentifier))
        {
            throw new Asn1Exception($"Expected OBJECT IDENTIFIER tag, got {tag}", offset);
        }
        if (len < 1)
        {
            throw new Asn1Exception("OBJECT IDENTIFIER content must be at least 1 byte", offset);
        }

        oid = DecodeContent(source, contentOffset, len, offset);
        return after;
    }

    /// <summary>Decodes content octets without the tag/length wrapper.</summary>
    public static ObjectIdentifier DecodeContent(byte[] source, int contentOffset, int length, long errorOffset)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Read SubIdentifiers, then unpack the first into two arcs.
        System.Collections.Generic.List<long> subIds = new();
        int pos = contentOffset;
        int end = contentOffset + length;

        while (pos < end)
        {
            long sub = ReadSubIdentifier(source, ref pos, end, errorOffset);
            subIds.Add(sub);
        }

        if (subIds.Count == 0)
        {
            throw new Asn1Exception("OID has no SubIdentifiers", errorOffset);
        }

        long first = subIds[0];
        long arc0, arc1;
        if (first < 40)
        {
            arc0 = 0;
            arc1 = first;
        }
        else if (first < 80)
        {
            arc0 = 1;
            arc1 = first - 40;
        }
        else
        {
            arc0 = 2;
            arc1 = first - 80;
        }

        long[] arcs = new long[subIds.Count + 1];
        arcs[0] = arc0;
        arcs[1] = arc1;
        for (int i = 1; i < subIds.Count; i++)
        {
            arcs[i + 1] = subIds[i];
        }
        return new ObjectIdentifier(arcs);
    }

    // ── Internal SubIdentifier codec ──────────────────────────────────────

    private static void WriteSubIdentifier(Stream output, long value)
    {
        // SubIdentifier encoded as base-128 big-endian, high bit set on all
        // bytes except the last (X.690 §8.19.2).
        Span<byte> buffer = stackalloc byte[10];  // ample for any non-negative long
        int written = 0;
        long n = value;
        do
        {
            buffer[written] = (byte)(n & 0x7F);
            n >>= 7;
            written++;
        }
        while (n > 0);

        for (int i = written - 1; i >= 0; i--)
        {
            byte b = buffer[i];
            if (i != 0)
            {
                b |= 0x80;
            }
            output.WriteByte(b);
        }
    }

    private static long ReadSubIdentifier(byte[] source, ref int pos, int end, long errorOffset)
    {
        long result = 0;
        int byteCount = 0;

        while (true)
        {
            if (pos >= end)
            {
                throw new Asn1Exception(
                    "Truncated OID SubIdentifier (continuation expected)", errorOffset);
            }
            byte b = source[pos];
            pos++;
            byteCount++;

            // Defend against overflow: 10 bytes * 7 bits = 70 bits, exceeds Int64.
            if (byteCount > 9)
            {
                throw new Asn1Exception("OID SubIdentifier overflows Int64", errorOffset);
            }

            // X.690 §8.19.2: leading 0x80 alone is forbidden (leading zeros).
            if (byteCount == 1 && b == 0x80)
            {
                throw new Asn1Exception(
                    "OID SubIdentifier has leading zero byte", errorOffset);
            }

            result = (result << 7) | (long)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }
    }
}
