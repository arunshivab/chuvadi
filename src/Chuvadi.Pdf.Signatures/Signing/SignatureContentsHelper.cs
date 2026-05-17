// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.6 — shared helper for incremental signing operations
//
// Internal helper shared by PdfSigner (full-document signing), PdfCounterSigner
// (adding a second signature via incremental update), PdfDocumentTimestamper
// (/DocTimeStamp via incremental update), and PdfLtvUpdater (VRI emission via
// incremental update). Centralises the byte-range placeholder locating and
// patching logic so the four call sites can stay in lockstep.

using System;
using System.Globalization;
using System.Text;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>Byte-range placeholder utilities shared across signing operations.</summary>
internal static class SignatureContentsHelper
{
    internal const int ByteRangeSlotWidth = 10;
    internal const int ByteRangePlaceholderValue = 999_999_999;

    /// <summary>
    /// Locates the position of the <c>/ByteRange</c> array's opening bracket
    /// in a PDF byte stream that contains a single signature dictionary with
    /// a placeholder byte range.
    /// </summary>
    internal static int LocateByteRangeArrayStart(byte[] bytes)
    {
        ReadOnlySpan<byte> needle = "/ByteRange"u8;
        for (int i = 0; i <= bytes.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j]) { match = false; break; }
            }
            if (!match) { continue; }
            int k = i + needle.Length;
            while (k < bytes.Length && bytes[k] != (byte)'[') { k++; }
            if (k < bytes.Length) { return k; }
        }
        return -1;
    }

    /// <summary>
    /// Locates the position of the <c>/Contents</c> hex-string opening <c>&lt;</c>.
    /// </summary>
    internal static int LocateContentsHexStart(byte[] bytes)
    {
        ReadOnlySpan<byte> needle = "/Contents"u8;
        for (int i = 0; i <= bytes.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j]) { match = false; break; }
            }
            if (!match) { continue; }
            int k = i + needle.Length;
            while (k < bytes.Length && bytes[k] != (byte)'<') { k++; }
            if (k < bytes.Length) { return k; }
        }
        return -1;
    }

    /// <summary>Replaces the four placeholder integers in the <c>/ByteRange</c> array.</summary>
    /// <remarks>
    /// Layout written by the array emitter: <c>[v0 v1 v2 v3]</c> where each
    /// vN is a zero-padded ByteRangeSlotWidth-character integer and the
    /// separators are single spaces. We assume the placeholder was emitted by
    /// the array writer with <see cref="Chuvadi.Pdf.Primitives.PdfPaddedInteger"/>
    /// at this width.
    /// </remarks>
    internal static void PatchByteRange(
        byte[] bytes, int arrayStart, int v0, int v1, int v2, int v3)
    {
        if (bytes[arrayStart] != (byte)'[')
        {
            throw new InvalidOperationException(
                $"Expected '[' at byte {arrayStart}, got 0x{bytes[arrayStart]:X2}.");
        }
        int p = arrayStart + 1;
        WriteIntPadded(bytes, p, v0, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++; // separator
        WriteIntPadded(bytes, p, v1, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++;
        WriteIntPadded(bytes, p, v2, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++;
        WriteIntPadded(bytes, p, v3, ByteRangeSlotWidth);
    }

    private static void WriteIntPadded(byte[] bytes, int offset, int value, int width)
    {
        if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
        for (int i = width - 1; i >= 0; i--)
        {
            bytes[offset + i] = (byte)('0' + (value % 10));
            value /= 10;
        }
        if (value != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), $"Value does not fit in {width} digits.");
        }
    }

    /// <summary>
    /// Writes <paramref name="data"/> as ASCII hex at the given byte position.
    /// </summary>
    internal static void WriteHexAt(byte[] bytes, int offset, byte[] data)
    {
        const string Digits = "0123456789ABCDEF";
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            bytes[offset + (2 * i)] = (byte)Digits[b >> 4];
            bytes[offset + (2 * i) + 1] = (byte)Digits[b & 0x0F];
        }
    }

    /// <summary>Formats a <see cref="DateTimeOffset"/> as a PDF date string.</summary>
    internal static string FormatPdfDate(DateTimeOffset time)
    {
        StringBuilder sb = new();
        sb.Append("D:");
        sb.Append(time.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        TimeSpan offset = time.Offset;
        if (offset == TimeSpan.Zero) { sb.Append('Z'); }
        else
        {
            sb.Append(offset.Ticks >= 0 ? '+' : '-');
            sb.Append(Math.Abs(offset.Hours).ToString("D2", CultureInfo.InvariantCulture));
            sb.Append('\'');
            sb.Append(Math.Abs(offset.Minutes).ToString("D2", CultureInfo.InvariantCulture));
            sb.Append('\'');
        }
        return sb.ToString();
    }
}
