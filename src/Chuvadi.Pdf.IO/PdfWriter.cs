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
using Chuvadi.Pdf.Encryption;
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
        Write(output, objects, trailer, encryption: null);
    }

    /// <summary>
    /// Writes a linearized (Fast Web View) PDF.
    /// </summary>
    /// <param name="output">Writable output stream.</param>
    /// <param name="objects">Indirect objects to write.</param>
    /// <param name="trailer">Trailer dictionary with /Root.</param>
    /// <remarks>
    /// Per ISO 32000-1 Annex F, the output is laid out so that the first page
    /// can be rendered after reading only the file's prefix. Encryption is not
    /// yet supported in combination with linearization.
    /// </remarks>
    public static void WriteLinearized(
        Stream output,
        IEnumerable<PdfIndirectObject> objects,
        PdfDictionary trailer)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(trailer);

        List<PdfIndirectObject> list = new List<PdfIndirectObject>(objects);
        LinearizedWriter.Write(output, list, trailer);
    }

    /// <summary>
    /// Writes a PDF with optional encryption applied to every string and stream
    /// inside the written objects.
    /// </summary>
    /// <param name="output">Writable, seekable output stream.</param>
    /// <param name="objects">Indirect objects to write.</param>
    /// <param name="trailer">
    /// Trailer dictionary. When <paramref name="encryption"/> is supplied, this
    /// method appends an /Encrypt entry referencing a newly created encryption
    /// dictionary; the trailer must NOT already contain one.
    /// </param>
    /// <param name="encryption">
    /// Encryption configuration. When null, no encryption is applied.
    /// </param>
    public static void Write(
        Stream output,
        IEnumerable<PdfIndirectObject> objects,
        PdfDictionary trailer,
        EncryptionOptions? encryption)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(trailer);

        // Write PDF header.
        output.Write(PdfHeader, 0, PdfHeader.Length);

        // Build sorted object list.
        List<PdfIndirectObject> sortedObjects = new List<PdfIndirectObject>(objects);
        sortedObjects.Sort((a, b) => a.Id.ObjectNumber.CompareTo(b.Id.ObjectNumber));

        int maxObjectNumber = 0;
        foreach (PdfIndirectObject obj in sortedObjects)
        {
            if (obj.Id.ObjectNumber > maxObjectNumber)
            {
                maxObjectNumber = obj.Id.ObjectNumber;
            }
        }

        // If encrypting, create the /Encrypt indirect object and append to objects.
        int encryptObjectNumber = -1;
        Encryptor? encryptor = null;
        bool encryptMetadata = true;

        if (encryption is not null)
        {
            encryptObjectNumber = maxObjectNumber + 1;
            PdfObjectId encryptId = new PdfObjectId(encryptObjectNumber, 0);

            PdfDictionary encryptDict = EncryptionDictionaryBuilder.Build(
                encryption, GetOrCreateFileId(trailer));

            sortedObjects.Add(new PdfIndirectObject(encryptId, encryptDict));
            maxObjectNumber = encryptObjectNumber;

            trailer.Set(PdfName.Intern("Encrypt"), new PdfReference(encryptId));
            encryptor = new Encryptor(encryption.FileKey, encryption.Algorithm);
            encryptMetadata = encryption.EncryptMetadata;
        }

        XrefTable xref = new XrefTable();

        foreach (PdfIndirectObject obj in sortedObjects)
        {
            long offset = output.Position;
            PdfIndirectObject toWrite = obj;

            // Encrypt every object EXCEPT the /Encrypt dictionary itself.
            if (encryptor is not null && obj.Id.ObjectNumber != encryptObjectNumber)
            {
                PdfPrimitive encryptedValue = EncryptionVisitor.Transform(
                    obj.Value,
                    obj.Id.ObjectNumber,
                    obj.Id.Generation,
                    encryptor.Encrypt,
                    skipMetadataEncryption: !encryptMetadata);
                toWrite = new PdfIndirectObject(obj.Id, encryptedValue);
            }

            WriteIndirectObject(output, toWrite);
            xref.Set(new XrefEntry(obj.Id.ObjectNumber, obj.Id.Generation, offset));
        }

        // Xref + trailer + EOF.
        long xrefOffset = xref.Write(output);
        int size = maxObjectNumber + 1;
        trailer.Set(PdfName.Size, size);

        byte[] trailerLine = Encoding.ASCII.GetBytes("trailer\n");
        output.Write(trailerLine, 0, trailerLine.Length);
        WriteValue(output, trailer);
        output.WriteByte((byte)'\n');

        string startxref = $"\nstartxref\n{xrefOffset}\n%%EOF\n";
        byte[] startxrefBytes = Encoding.ASCII.GetBytes(startxref);
        output.Write(startxrefBytes, 0, startxrefBytes.Length);
    }

    private static byte[] GetOrCreateFileId(PdfDictionary trailer)
    {
        if (trailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? idPrim) &&
            idPrim is PdfArray idArr && idArr.Count >= 1 && idArr[0] is PdfString idStr)
        {
            return idStr.Bytes;
        }

        // Generate a fresh /ID (two identical 16-byte random IDs for a new doc).
        byte[] fid = new byte[16];
        using (System.Security.Cryptography.RandomNumberGenerator rng =
            System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(fid);
        }

        PdfString idString = new PdfString(fid);
        trailer.Set(PdfName.Intern("ID"), new PdfArray([idString, idString]));
        return fid;
    }

    // ── Object serialisation ──────────────────────────────────────────────

    internal static void WriteIndirectObject(Stream output, PdfIndirectObject obj)
    {
        // Write: "N G obj\n<value>\nendobj\n"
        string header = $"{obj.Id.ObjectNumber} {obj.Id.Generation} obj\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        output.Write(headerBytes, 0, headerBytes.Length);
        WriteValue(output, obj.Value);
        byte[] endobj = Encoding.ASCII.GetBytes("\nendobj\n");
        output.Write(endobj, 0, endobj.Length);
    }

    internal static void WriteValue(Stream output, PdfPrimitive value)
    {
        switch (value)
        {
            case PdfNull _:
                WriteAscii(output, "null");
                break;

            case PdfBoolean b:
                WriteAscii(output, b.Value ? "true" : "false");
                break;

            case PdfPaddedInteger pi:
                // Width-preserving padded form, used by signature emitters that
                // need fixed-width /ByteRange slots so subsequent byte positions
                // don't shift when the placeholder is patched.
                WriteAscii(output, pi.ToString());
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
