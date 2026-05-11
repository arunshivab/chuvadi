// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.2 — ASCIIHexDecode filter
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// Encodes binary data as hexadecimal ASCII characters.

using System;
using System.IO;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Implements the PDF ASCIIHexDecode filter.
/// Each byte is encoded as two uppercase hex characters.
/// Whitespace is ignored on decode. EOD marker is <c>&gt;</c>.
/// PDF 32000-1:2008 §7.4.2.
/// </summary>
public sealed class AsciiHexFilter : IStreamFilter
{
    /// <inheritdoc/>
    public string FilterName => "ASCIIHexDecode";

    /// <inheritdoc/>
    public void Decode(Stream input, Stream output, FilterParameters? decodeParms = null)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        int highNibble = -1;

        while (true)
        {
            int b = input.ReadByte();

            if (b == -1)
            {
                break;
            }

            if (b == '>')
            {
                break;
            }

            // Skip PDF whitespace.
            if (b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32)
            {
                continue;
            }

            int nibble = HexNibble((byte)b);

            if (nibble < 0)
            {
                throw new FilterException(FilterName,
                    $"Invalid hex character 0x{b:X2} in ASCIIHex stream.");
            }

            if (highNibble < 0)
            {
                highNibble = nibble;
            }
            else
            {
                output.WriteByte((byte)((highNibble << 4) | nibble));
                highNibble = -1;
            }
        }

        // Odd digit count: pad final nibble with 0. PDF 32000-1:2008 §7.4.2.
        if (highNibble >= 0)
        {
            output.WriteByte((byte)(highNibble << 4));
        }
    }

    /// <inheritdoc/>
    public void Encode(Stream input, Stream output, FilterParameters? encodeParms = null)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        int b;
        int col = 0;

        while ((b = input.ReadByte()) != -1)
        {
            output.WriteByte(ToHexChar((b >> 4) & 0xF));
            output.WriteByte(ToHexChar(b & 0xF));
            col += 2;

            if (col >= 78)
            {
                output.WriteByte((byte)'\n');
                col = 0;
            }
        }

        output.WriteByte((byte)'>');
    }

    private static int HexNibble(byte b)
    {
        if (b >= '0' && b <= '9')
        {
            return b - '0';
        }

        if (b >= 'A' && b <= 'F')
        {
            return b - 'A' + 10;
        }

        if (b >= 'a' && b <= 'f')
        {
            return b - 'a' + 10;
        }

        return -1;
    }

    private static byte ToHexChar(int nibble)
    {
        return nibble < 10
            ? (byte)('0' + nibble)
            : (byte)('A' + nibble - 10);
    }
}
