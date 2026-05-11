// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.3 — ASCII85Decode filter
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// Encodes binary data as ASCII base-85 characters.

using System;
using System.IO;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Implements the PDF ASCII85Decode filter.
/// 4 binary bytes → 5 ASCII characters. EOD marker is <c>~&gt;</c>.
/// Zero group of 4 bytes is encoded as single <c>z</c>.
/// PDF 32000-1:2008 §7.4.3.
/// </summary>
public sealed class Ascii85Filter : IStreamFilter
{
    /// <inheritdoc/>
    public string FilterName => "ASCII85Decode";

    private static readonly uint[] Powers85 = [52200625u, 614125u, 7225u, 85u, 1u];

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

        byte[] group = new byte[5];
        int groupLen = 0;

        while (true)
        {
            int b = input.ReadByte();

            if (b == -1)
            {
                break;
            }

            // Skip whitespace.
            if (b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32)
            {
                continue;
            }

            // EOD marker.
            if (b == '~')
            {
                int next = input.ReadByte();

                if (next != '>')
                {
                    throw new FilterException(FilterName,
                        "Invalid ASCII85 EOD: '~' not followed by '>'.");
                }

                break;
            }

            // 'z' shorthand: four zero bytes.
            if (b == 'z')
            {
                if (groupLen != 0)
                {
                    throw new FilterException(FilterName,
                        "'z' shorthand is not allowed in a partial group.");
                }

                output.WriteByte(0);
                output.WriteByte(0);
                output.WriteByte(0);
                output.WriteByte(0);
                continue;
            }

            // Valid range: '!' (33) to 'u' (117).
            if (b < 33 || b > 117)
            {
                throw new FilterException(FilterName,
                    $"Invalid ASCII85 character 0x{b:X2}.");
            }

            group[groupLen++] = (byte)(b - 33);

            if (groupLen == 5)
            {
                WriteDecodedGroup(output, group, 4);
                groupLen = 0;
            }
        }

        // Partial final group.
        if (groupLen > 0)
        {
            for (int i = groupLen; i < 5; i++)
            {
                group[i] = 84;
            }

            WriteDecodedGroup(output, group, groupLen - 1);
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

        byte[] buf = new byte[4];
        int col = 0;

        while (true)
        {
            int bytesRead = ReadExact(input, buf, 4);

            if (bytesRead == 0)
            {
                break;
            }

            if (bytesRead == 4)
            {
                uint value =
                    ((uint)buf[0] << 24) |
                    ((uint)buf[1] << 16) |
                    ((uint)buf[2] << 8) |
                    buf[3];

                if (value == 0)
                {
                    output.WriteByte((byte)'z');
                    col++;
                }
                else
                {
                    byte[] chars = new byte[5];

                    for (int i = 4; i >= 0; i--)
                    {
                        chars[i] = (byte)(value % 85 + 33);
                        value /= 85;
                    }

                    for (int i = 0; i < 5; i++)
                    {
                        output.WriteByte(chars[i]);
                        col++;

                        if (col >= 75)
                        {
                            output.WriteByte((byte)'\n');
                            col = 0;
                        }
                    }
                }
            }
            else
            {
                // Partial final group: pad with zeros.
                for (int i = bytesRead; i < 4; i++)
                {
                    buf[i] = 0;
                }

                uint value =
                    ((uint)buf[0] << 24) |
                    ((uint)buf[1] << 16) |
                    ((uint)buf[2] << 8) |
                    buf[3];

                byte[] chars = new byte[5];

                for (int i = 4; i >= 0; i--)
                {
                    chars[i] = (byte)(value % 85 + 33);
                    value /= 85;
                }

                for (int i = 0; i <= bytesRead; i++)
                {
                    output.WriteByte(chars[i]);
                }
            }
        }

        output.WriteByte((byte)'~');
        output.WriteByte((byte)'>');
    }

    private static void WriteDecodedGroup(Stream output, byte[] group, int bytesToWrite)
    {
        uint value = 0;

        for (int i = 0; i < 5; i++)
        {
            value += group[i] * Powers85[i];
        }

        byte b0 = (byte)((value >> 24) & 0xFF);
        byte b1 = (byte)((value >> 16) & 0xFF);
        byte b2 = (byte)((value >> 8) & 0xFF);
        byte b3 = (byte)(value & 0xFF);

        if (bytesToWrite >= 1) { output.WriteByte(b0); }
        if (bytesToWrite >= 2) { output.WriteByte(b1); }
        if (bytesToWrite >= 3) { output.WriteByte(b2); }
        if (bytesToWrite >= 4) { output.WriteByte(b3); }
    }

    private static int ReadExact(Stream stream, byte[] buffer, int count)
    {
        int total = 0;

        while (total < count)
        {
            int n = stream.Read(buffer, total, count - total);

            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }
}
