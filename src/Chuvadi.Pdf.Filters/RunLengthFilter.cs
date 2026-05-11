// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.5 — RunLengthDecode filter
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// PackBits run-length compression for PDF streams.

using System;
using System.IO;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Implements the PDF RunLengthDecode filter (PackBits algorithm).
/// Header 0-127: literal run. Header 129-255: repeat run. Header 128: EOD.
/// PDF 32000-1:2008 §7.4.5.
/// </summary>
public sealed class RunLengthFilter : IStreamFilter
{
    /// <inheritdoc/>
    public string FilterName => "RunLengthDecode";

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

        while (true)
        {
            int header = input.ReadByte();

            if (header == -1 || header == 128)
            {
                break;
            }

            if (header < 128)
            {
                int count = header + 1;

                for (int i = 0; i < count; i++)
                {
                    int b = input.ReadByte();

                    if (b == -1)
                    {
                        throw new FilterException(FilterName,
                            "Unexpected end of stream in literal run.");
                    }

                    output.WriteByte((byte)b);
                }
            }
            else
            {
                int count = 257 - header;
                int b = input.ReadByte();

                if (b == -1)
                {
                    throw new FilterException(FilterName,
                        "Unexpected end of stream in repeat run.");
                }

                for (int i = 0; i < count; i++)
                {
                    output.WriteByte((byte)b);
                }
            }
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

        byte[] data = ReadAllBytes(input);
        int pos = 0;

        while (pos < data.Length)
        {
            int runEnd = pos + 1;

            while (runEnd < data.Length &&
                   runEnd - pos < 128 &&
                   data[runEnd] == data[pos])
            {
                runEnd++;
            }

            int runLen = runEnd - pos;

            if (runLen >= 2)
            {
                output.WriteByte((byte)(257 - runLen));
                output.WriteByte(data[pos]);
                pos = runEnd;
            }
            else
            {
                int litEnd = pos + 1;

                while (litEnd < data.Length && litEnd - pos < 128)
                {
                    if (litEnd + 1 < data.Length && data[litEnd] == data[litEnd + 1])
                    {
                        break;
                    }

                    litEnd++;
                }

                int litLen = litEnd - pos;
                output.WriteByte((byte)(litLen - 1));

                for (int i = pos; i < litEnd; i++)
                {
                    output.WriteByte(data[i]);
                }

                pos = litEnd;
            }
        }

        output.WriteByte(128);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
