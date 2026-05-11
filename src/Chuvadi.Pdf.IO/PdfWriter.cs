// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure
// PHASE: Phase 1 — Chuvadi.Pdf.IO
// Writes PDF files in full-rewrite mode with classic xref tables.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Writes a complete PDF file to an output stream.
/// </summary>
/// <remarks>
/// <see cref="PdfWriter"/> performs a full rewrite — it serialises all
/// provided indirect objects, builds a fresh cross-reference table, and
/// writes a valid PDF trailer.
///
/// Streams are written with their existing raw bytes unchanged.
/// The <c>/Length</c> entry is updated to reflect the actual byte count.
///
/// PDF version written: <c>%PDF-1.7</c>.
/// xref format: classic cross-reference table (not a cross-reference stream).
///
/// PDF 32000-1:2008 §7.5 — File structure.
/// </remarks>
public static class PdfWriter
{
    private static readonly byte[] PdfHeader =
        Encoding.ASCII.GetBytes("%PDF-1.7\n%\xE2\xE3\xCF\xD3\n");

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a complete PDF file containing the given indirect objects.
    /// </summary>
    /// <param name="output">The stream to write to. Must be writable.</param>
    /// <param name="objects">
    /// The indirect objects to include. Object 0 (the free list head) is
    /// added automatically and must not be included by the caller.
    /// </param>
    /// <param name="trailer">
    /// The trailer dictionary. <c>/Size</c> is computed automatically.
    /// <c>/Root</c> must be set by the caller.
    /// </param>
    public static void Write(
        Stream output,
        IEnumerable<PdfIndirectObject> objects,
        PdfDictionary trailer)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (objects is null)
        {
            throw new ArgumentNullException(nameof(objects));
        }

        if (trailer is null)
        {
            throw new ArgumentNullException(nameof(trailer));
        }

        // Write PDF header.
        output.Write(PdfHeader, 0, PdfHeader.Length);

        // Build sorted object list and record byte offsets.
        List<PdfIndirectObject> sortedObjects = new List<PdfIndirectObject>(objects);
        sortedObjects.Sort((a, b) => a.Id.ObjectNumber.CompareTo(b.Id.ObjectNumber));

        XrefTable xref = new XrefTable();
        int maxObjectNumber = 0;

        foreach (PdfIndirectObject obj in sortedObjects)
        {
            long offset = output.Position;
            WriteIndirectObject(output, obj);
            xref.Set(new XrefEntry(obj.Id.ObjectNumber, obj.Id.Generation, offset));

            if (obj.Id.ObjectNumber > maxObjectNumber)
            {
                maxObjectNumber = obj.Id.ObjectNumber;
            }
        }

        // Write xref table and remember its offset.
        long xrefOffset = xref.Write(output);

        // Write trailer.
        int size = maxObjectNumber + 1;
        trailer.Set(PdfName.Size, size);

        byte[] trailerLine = Encoding.ASCII.GetBytes("trailer\n");
        output.Write(trailerLine, 0, trailerLine.Length);
        WriteValue(output, trailer);
        output.WriteByte((byte)'\n');

        // Write startxref and %%EOF.
        string startxref = $"\nstartxref\n{xrefOffset}\n%%EOF\n";
        byte[] startxrefBytes = Encoding.ASCII.GetBytes(startxref);
        output.Write(startxrefBytes, 0, startxrefBytes.Length);
    }

    // ── Object serialisation ──────────────────────────────────────────────

    private static void WriteIndirectObject(Stream output, PdfIndirectObject obj)
    {
        // Write: "N G obj\n<value>\nendobj\n"
        string header = $"{obj.Id.ObjectNumber} {obj.Id.Generation} obj\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        output.Write(headerBytes, 0, headerBytes.Length);
        WriteValue(output, obj.Value);
        byte[] endobj = Encoding.ASCII.GetBytes("\nendobj\n");
        output.Write(endobj, 0, endobj.Length);
    }

    private static void WriteValue(Stream output, PdfPrimitive value)
    {
        switch (value)
        {
            case PdfNull _:
                WriteAscii(output, "null");
                break;

            case PdfBoolean b:
                WriteAscii(output, b.Value ? "true" : "false");
                break;

            case PdfInteger i:
                WriteAscii(output, i.Value.ToString(CultureInfo.InvariantCulture));
                break;

            case PdfReal r:
                WriteReal(output, r.Value);
                break;

            case PdfName n:
                WriteAscii(output, "/");
                WriteAscii(output, EncodeName(n.Value));
                break;

            case PdfString s:
                WriteString(output, s);
                break;

            case PdfReference rf:
                WriteAscii(output,
                    $"{rf.ObjectId.ObjectNumber} {rf.ObjectId.Generation} R");
                break;

            case PdfStream st:
                WriteStream(output, st);
                break;

            case PdfDictionary d:
                WriteDictionary(output, d);
                break;

            case PdfArray a:
                WriteArray(output, a);
                break;

            default:
                WriteAscii(output, "null");
                break;
        }
    }

    private static void WriteDictionary(Stream output, PdfDictionary dict)
    {
        WriteAscii(output, "<<");

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
        {
            WriteAscii(output, "\n/");
            WriteAscii(output, EncodeName(entry.Key.Value));
            WriteAscii(output, " ");
            WriteValue(output, entry.Value);
        }

        WriteAscii(output, "\n>>");
    }

    private static void WriteArray(Stream output, PdfArray array)
    {
        WriteAscii(output, "[");
        bool first = true;

        foreach (PdfPrimitive item in array)
        {
            if (!first)
            {
                WriteAscii(output, " ");
            }

            WriteValue(output, item);
            first = false;
        }

        WriteAscii(output, "]");
    }

    private static void WriteStream(Stream output, PdfStream stream)
    {
        // Update /Length to reflect actual raw bytes.
        PdfDictionary dict = stream.Dictionary;
        dict.Set(PdfName.Length, stream.RawBytes.Length);

        WriteDictionary(output, dict);
        WriteAscii(output, "\nstream\n");
        output.Write(stream.RawBytes, 0, stream.RawBytes.Length);
        WriteAscii(output, "\nendstream");
    }

    private static void WriteString(Stream output, PdfString s)
    {
        // Write as hex string for simplicity — always unambiguous.
        WriteAscii(output, "<");
        byte[] bytes = s.Bytes;

        foreach (byte b in bytes)
        {
            WriteAscii(output, b.ToString("X2", CultureInfo.InvariantCulture));
        }

        WriteAscii(output, ">");
    }

    private static void WriteReal(Stream output, double value)
    {
        // Use up to 6 significant digits, no trailing zeros, no scientific notation.
        string formatted = value.ToString("G6", CultureInfo.InvariantCulture);

        // Ensure a decimal point is present (PDF requires reals to have one).
        if (!formatted.Contains('.') && !formatted.Contains('E') && !formatted.Contains('e'))
        {
            formatted += ".0";
        }

        WriteAscii(output, formatted);
    }

    private static string EncodeName(string name)
    {
        // Encode characters that need #XX escaping in PDF names.
        // PDF 32000-1:2008 §7.3.5.
        StringBuilder sb = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            if (c == '#' || c < 33 || c > 126)
            {
                sb.Append('#');
                sb.Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static void WriteAscii(Stream output, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }
}
