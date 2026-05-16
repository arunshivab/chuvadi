// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.690 §8.1.2 (identifier), §8.1.3 (length)
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 foundation
//
// Low-level tag and length codec. Reads and writes the (tag, length) prefix
// of an ASN.1 BER/DER element. Does NOT touch the value bytes — that's a
// higher layer's job. Keeping this layer tiny and obsessively tested gives
// every later parser a trustworthy foundation.

using System;
using System.IO;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Stateless low-level codec for ASN.1 BER/DER tag and length prefixes.
/// </summary>
/// <remarks>
/// Parsing an ASN.1 element decomposes naturally into three steps: read the
/// tag, read the length, then read length bytes of value. This class handles
/// only the first two steps. The contents bytes are returned as a span or
/// byte range for the caller to interpret.
/// <para>
/// Both DER and BER encodings are accepted on the read side. Indefinite-length
/// form (length octet 0x80 followed by content terminated by two zero bytes)
/// is rejected by default — Chuvadi's signing workflows require DER, which
/// forbids it. Writing always produces strict DER.
/// </para>
/// </remarks>
public static class Asn1TagLength
{
    /// <summary>
    /// Reads a tag and length from <paramref name="source"/> starting at <paramref name="offset"/>.
    /// </summary>
    /// <param name="source">Byte array containing the ASN.1 element.</param>
    /// <param name="offset">Position at which to begin reading.</param>
    /// <param name="tag">On success, the decoded tag.</param>
    /// <param name="contentOffset">On success, the offset of the first content byte.</param>
    /// <param name="contentLength">On success, the length of the content in bytes.</param>
    /// <returns>The offset immediately after the encoded element (contentOffset + contentLength).</returns>
    /// <exception cref="ArgumentNullException">If source is null.</exception>
    /// <exception cref="Asn1Exception">If the tag or length is malformed, indefinite-length is used, or the element extends past the end of source.</exception>
    public static int Read(
        byte[] source,
        int offset,
        out Asn1Tag tag,
        out int contentOffset,
        out int contentLength)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (offset < 0 || offset > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        int pos = offset;

        // ── Read tag (identifier octets) ─────────────────────────────────
        if (pos >= source.Length)
        {
            throw new Asn1Exception("Unexpected end of input while reading tag", pos);
        }

        byte firstByte = source[pos];
        pos++;

        Asn1TagClass tagClass = (Asn1TagClass)((firstByte >> 6) & 0x03);
        bool isConstructed = (firstByte & 0x20) != 0;
        int tagNumberLow = firstByte & 0x1F;
        int tagNumber;

        if (tagNumberLow < 31)
        {
            // Short form — tag number fits in the low 5 bits
            tagNumber = tagNumberLow;
        }
        else
        {
            // Long form — subsequent octets, base-128 big-endian,
            // continuation bit = MSB. X.690 §8.1.2.4.
            tagNumber = 0;
            int byteCount = 0;
            while (true)
            {
                if (pos >= source.Length)
                {
                    throw new Asn1Exception("Unexpected end of input in multi-byte tag", pos);
                }
                byte b = source[pos];
                pos++;
                byteCount++;

                // Defend against tag-number overflow. A tag taking more than
                // four continuation bytes would overflow Int32; reject it.
                if (byteCount > 4)
                {
                    throw new Asn1Exception("ASN.1 tag number too large to fit in Int32", pos);
                }

                // X.690 §8.1.2.4.2 (c): the leading byte of a long-form tag
                // number must not be 0x80 (would imply leading zeros, forbidden).
                if (byteCount == 1 && b == 0x80)
                {
                    throw new Asn1Exception("Long-form tag number has leading zero byte", pos);
                }

                tagNumber = (tagNumber << 7) | (b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            // Long form is only valid when the actual tag number is >= 31.
            // X.690 §8.1.2.2 (c).
            if (tagNumber < 31)
            {
                throw new Asn1Exception("Long-form tag used for tag number < 31", pos);
            }
        }

        tag = new Asn1Tag(tagClass, isConstructed, tagNumber);

        // ── Read length octets ────────────────────────────────────────────
        if (pos >= source.Length)
        {
            throw new Asn1Exception("Unexpected end of input while reading length", pos);
        }

        byte lengthFirst = source[pos];
        pos++;

        if (lengthFirst == 0x80)
        {
            throw new Asn1Exception(
                "Indefinite-length form is not supported (DER forbids it)", pos);
        }

        int length;

        if ((lengthFirst & 0x80) == 0)
        {
            // Short form — length 0..127
            length = lengthFirst;
        }
        else
        {
            // Long form — bottom 7 bits is the number of subsequent length octets.
            int lengthOctetCount = lengthFirst & 0x7F;

            // 0xFF is reserved per X.690 §8.1.3.5 (c).
            if (lengthOctetCount == 0x7F)
            {
                throw new Asn1Exception("Reserved length-of-length value 0xFF", pos);
            }

            // We support lengths up to Int32.MaxValue. Four octets is enough.
            if (lengthOctetCount > 4)
            {
                throw new Asn1Exception(
                    "Length octet count exceeds 4 (would overflow Int32)", pos);
            }

            if (pos + lengthOctetCount > source.Length)
            {
                throw new Asn1Exception("Unexpected end of input reading length octets", pos);
            }

            // DER requires the minimum number of length octets (X.690 §10.1).
            // We currently accept any encoding on the read side; a stricter
            // DER-mode toggle is future work.
            length = 0;
            for (int i = 0; i < lengthOctetCount; i++)
            {
                length = (length << 8) | source[pos];
                pos++;
                if (length < 0)
                {
                    throw new Asn1Exception("Length overflowed Int32", pos);
                }
            }
        }

        // ── Compute content range ────────────────────────────────────────
        contentOffset = pos;

        // Defend against integer overflow when content extends past Int32 range
        // or past the end of the source array.
        long contentEnd = (long)contentOffset + length;
        if (contentEnd > source.Length)
        {
            throw new Asn1Exception(
                $"Element content (length {length}) extends past end of buffer (available {source.Length - contentOffset})",
                pos);
        }

        contentLength = length;
        return (int)contentEnd;
    }

    /// <summary>
    /// Writes a tag and length prefix to <paramref name="output"/> in strict DER form.
    /// </summary>
    /// <param name="output">Writable destination stream.</param>
    /// <param name="tag">The tag to write.</param>
    /// <param name="contentLength">Length of the content that will follow.</param>
    /// <exception cref="ArgumentNullException">If output is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If contentLength is negative.</exception>
    public static void Write(Stream output, Asn1Tag tag, int contentLength)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (contentLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentLength),
                "Content length cannot be negative.");
        }

        // ── Encode tag ────────────────────────────────────────────────────
        byte classBits = (byte)((byte)tag.TagClass << 6);
        byte constructedBit = (byte)(tag.IsConstructed ? 0x20 : 0x00);

        if (tag.TagNumber < 31)
        {
            // Short form
            output.WriteByte((byte)(classBits | constructedBit | (byte)tag.TagNumber));
        }
        else
        {
            // Long form — first byte has all bits set in the tag-number field.
            output.WriteByte((byte)(classBits | constructedBit | 0x1F));

            // Encode the tag number as base-128 big-endian. The high bit of
            // every byte except the last is 1.
            int number = tag.TagNumber;
            Span<byte> buffer = stackalloc byte[5];  // Int32 needs at most 5 bytes in base 128
            int written = 0;
            do
            {
                buffer[written] = (byte)(number & 0x7F);
                number >>= 7;
                written++;
            }
            while (number > 0);

            // Bytes were written least-significant first; reverse and set
            // continuation bits on all but the last (which is the LSB).
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

        // ── Encode length (strict DER: minimum-length form) ──────────────
        if (contentLength < 0x80)
        {
            output.WriteByte((byte)contentLength);
        }
        else
        {
            // Long form. Compute the minimum number of bytes.
            Span<byte> lengthBytes = stackalloc byte[4];
            int idx = 0;
            int n = contentLength;
            while (n > 0)
            {
                lengthBytes[idx] = (byte)(n & 0xFF);
                n >>= 8;
                idx++;
            }

            output.WriteByte((byte)(0x80 | idx));
            for (int i = idx - 1; i >= 0; i--)
            {
                output.WriteByte(lengthBytes[i]);
            }
        }
    }
}
