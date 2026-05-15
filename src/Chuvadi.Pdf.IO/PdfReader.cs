// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure
// PHASE: Phase 1 — Chuvadi.Pdf.IO
// Opens a PDF file, locates its xref, and provides lazy object access.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Opens an existing PDF file and provides access to its object graph.
/// PDF 32000-1:2008 §7.5 — File structure.
/// </summary>
public sealed class PdfReader : IDisposable
{
    private const int BackwardScanLimit = 1024;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private bool _disposed;

    private PdfReader(
        Stream stream,
        bool leaveOpen,
        PdfDictionary trailer,
        PdfObjectStore objects)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _disposed = false;
        Trailer = trailer;
        Objects = objects;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>Opens a PDF file from the given readable, seekable stream.</summary>
    /// <exception cref="PdfReaderException">
    /// Thrown when the file is encrypted. Use the password overload to open
    /// encrypted PDFs.
    /// </exception>
    public static PdfReader Open(Stream stream, bool leaveOpen = false)
    {
        return OpenInternal(stream, password: null, leaveOpen);
    }

    /// <summary>
    /// Opens a PDF file with the given password. For unencrypted PDFs the
    /// password is ignored.
    /// </summary>
    /// <param name="stream">Readable, seekable stream containing the PDF.</param>
    /// <param name="password">User or owner password. Empty string for default.</param>
    /// <param name="leaveOpen">Whether to leave the stream open on dispose.</param>
    /// <exception cref="PdfReaderException">Thrown when the password is incorrect.</exception>
    public static PdfReader Open(Stream stream, string password, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(password);
        return OpenInternal(stream, password, leaveOpen);
    }

    private static PdfReader OpenInternal(Stream stream, string? password, bool leaveOpen)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "Stream must be readable and seekable.", nameof(stream));
        }

        long startXrefOffset = FindStartXref(stream);

        PdfDictionary trailer = new PdfDictionary();
        XrefTable xref = new XrefTable();
        LoadXrefChain(stream, startXrefOffset, xref, trailer);

        PdfObjectParser parser = new PdfObjectParser(stream);

        // Check for encryption. The /Encrypt entry in the trailer holds the
        // encryption dictionary (or an indirect reference to it).
        Decryptor? decryptor = null;
        int encryptObjectNumber = -1;
        bool encryptMetadata = true;

        if (trailer.TryGetValue(PdfName.Intern("Encrypt"), out PdfPrimitive? encryptPrim))
        {
            (decryptor, encryptObjectNumber, encryptMetadata) = BuildDecryptor(
                parser, xref, trailer, encryptPrim, password);
        }

        Decryptor? finalDecryptor = decryptor;
        int finalEncryptObjNum = encryptObjectNumber;
        bool finalEncryptMetadata = encryptMetadata;

        PdfObjectStore objects = new PdfObjectStore(id =>
        {
            PdfIndirectObject? loaded = LoadObjectFromFile(parser, xref, id);

            if (loaded is null || finalDecryptor is null)
            {
                return loaded;
            }

            // The /Encrypt object itself is NEVER decrypted (chicken-and-egg).
            if (id.ObjectNumber == finalEncryptObjNum)
            {
                return loaded;
            }

            PdfPrimitive decryptedValue = EncryptionVisitor.Transform(
                loaded.Value,
                id.ObjectNumber,
                id.Generation,
                finalDecryptor.Decrypt,
                skipMetadataEncryption: !finalEncryptMetadata);

            return new PdfIndirectObject(loaded.Id, decryptedValue);
        });

        return new PdfReader(stream, leaveOpen, trailer, objects);
    }

    private static (Decryptor decryptor, int encryptObjectNumber, bool encryptMetadata)
        BuildDecryptor(
            PdfObjectParser parser,
            XrefTable xref,
            PdfDictionary trailer,
            PdfPrimitive encryptPrim,
            string? password)
    {
        PdfDictionary encryptDict;
        int encryptObjectNumber = -1;

        if (encryptPrim is PdfReference encRef)
        {
            encryptObjectNumber = encRef.ObjectId.ObjectNumber;
            PdfIndirectObject? indirect = LoadObjectFromFile(parser, xref, encRef.ObjectId);

            if (indirect?.Value is not PdfDictionary d)
            {
                throw new PdfReaderException(
                    "Document /Encrypt entry could not be resolved to a dictionary.");
            }

            encryptDict = d;
        }
        else if (encryptPrim is PdfDictionary inlineDict)
        {
            encryptDict = inlineDict;
        }
        else
        {
            throw new PdfReaderException("Document /Encrypt entry has an invalid type.");
        }

        byte[] firstFileId = Array.Empty<byte>();

        if (trailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? idPrim) &&
            idPrim is PdfArray idArr && idArr.Count >= 1 && idArr[0] is PdfString idStr)
        {
            firstFileId = idStr.Bytes;
        }

        Decryptor decryptor = PdfEncryption.TryOpen(encryptDict, firstFileId, password ?? string.Empty)
            ?? throw new PdfReaderException(
                "PDF is encrypted; provide the correct user or owner password.");

        bool encryptMetadata = true;
        if (encryptDict.TryGetValue(PdfName.Intern("EncryptMetadata"), out PdfPrimitive? emPrim) &&
            emPrim is PdfBoolean emb)
        {
            encryptMetadata = emb.Value;
        }

        return (decryptor, encryptObjectNumber, encryptMetadata);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Gets the PDF trailer dictionary.</summary>
    public PdfDictionary Trailer { get; }

    /// <summary>Gets the lazy object store.</summary>
    public PdfObjectStore Objects { get; }

    /// <summary>Gets the document Catalog dictionary, or null.</summary>
    public PdfDictionary? Catalog
    {
        get
        {
            PdfPrimitive? root = Trailer.GetAs<PdfPrimitive>(PdfName.Root);

            if (root is PdfReference rootRef)
            {
                return Objects.ResolveById(rootRef.ObjectId) as PdfDictionary;
            }

            return root as PdfDictionary;
        }
    }

    /// <summary>Gets the document information dictionary, if present.</summary>
    public PdfDictionary? Info
    {
        get
        {
            PdfPrimitive? info = Trailer.GetAs<PdfPrimitive>(PdfName.Info);

            if (info is PdfReference infoRef)
            {
                return Objects.ResolveById(infoRef.ObjectId) as PdfDictionary;
            }

            return info as PdfDictionary;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }
    }

    // ── Private: xref location ────────────────────────────────────────────

    private static long FindStartXref(Stream stream)
    {
        long fileLen = stream.Length;
        long scanStart = Math.Max(0, fileLen - BackwardScanLimit);
        int scanLen = (int)(fileLen - scanStart);

        stream.Seek(scanStart, SeekOrigin.Begin);
        byte[] tail = new byte[scanLen];
        int read = 0;

        while (read < scanLen)
        {
            int n = stream.Read(tail, read, scanLen - read);

            if (n == 0)
            {
                break;
            }

            read += n;
        }

        string tailText = Encoding.Latin1.GetString(tail, 0, read);
        int startxrefIdx = tailText.LastIndexOf("startxref", StringComparison.Ordinal);

        if (startxrefIdx < 0)
        {
            throw new PdfReaderException(
                "Could not locate 'startxref' keyword in the last 1024 bytes of the file.");
        }

        int pos = startxrefIdx + "startxref".Length;

        while (pos < tailText.Length && IsWhitespace(tailText[pos]))
        {
            pos++;
        }

        int numStart = pos;

        while (pos < tailText.Length && char.IsDigit(tailText[pos]))
        {
            pos++;
        }

        string offsetText = tailText[numStart..pos].Trim();

        if (!long.TryParse(offsetText, NumberStyles.None, CultureInfo.InvariantCulture,
            out long xrefOffset))
        {
            throw new PdfReaderException(
                $"Invalid startxref offset value: '{offsetText}'.");
        }

        return xrefOffset;
    }

    // ── Private: xref chain ───────────────────────────────────────────────

    private static void LoadXrefChain(
        Stream stream,
        long offset,
        XrefTable xref,
        PdfDictionary mergedTrailer)
    {
        HashSet<long> visited = new HashSet<long>();

        while (offset >= 0 && !visited.Contains(offset))
        {
            visited.Add(offset);
            stream.Seek(offset, SeekOrigin.Begin);

            byte[] peek = new byte[5];
            int peekRead = stream.Read(peek, 0, 5);
            string peekText = Encoding.Latin1.GetString(peek, 0, peekRead);

            if (peekText.StartsWith("xref", StringComparison.Ordinal))
            {
                PdfDictionary sectionTrailer = LoadClassicXref(stream, offset, xref);
                MergeTrailer(mergedTrailer, sectionTrailer);
                offset = sectionTrailer.GetInteger(PdfName.Prev, -1);
            }
            else
            {
                PdfDictionary sectionTrailer = LoadXrefStream(stream, offset, xref);
                MergeTrailer(mergedTrailer, sectionTrailer);
                offset = sectionTrailer.GetInteger(PdfName.Prev, -1);
            }
        }
    }

    private static PdfDictionary LoadClassicXref(
        Stream stream,
        long offset,
        XrefTable xref)
    {
        // Parse the xref entries. XrefTable.Parse handles the "xref" keyword
        // itself (it skips it if present). Note: Parse uses StreamReader internally
        // which buffers ahead, so the stream position after Parse is unreliable.
        stream.Seek(offset, SeekOrigin.Begin);
        XrefTable section = XrefTable.Parse(stream);

        foreach (XrefEntry entry in section.Entries)
        {
            if (!xref.Contains(entry.ObjectNumber) || entry.IsFree)
            {
                xref.Set(entry);
            }
        }

        // Because StreamReader in XrefTable.Parse buffers ahead, the stream
        // position after Parse may be past the "trailer" keyword.
        // Scan the raw bytes from 'offset' to find "trailer" precisely.
        // PDF 32000-1:2008 §7.5.5 — the trailer keyword follows the xref entries.
        long trailerPos = ScanForKeyword(stream, offset, "trailer");

        if (trailerPos < 0)
        {
            throw new PdfReaderException(
                $"Could not find 'trailer' keyword in xref section at offset {offset}.");
        }

        // Position past "trailer" keyword and read the trailer dictionary.
        PdfObjectParser trailerParser = new PdfObjectParser(stream);
        trailerParser.Seek(trailerPos + 7); // 7 = "trailer".Length
        PdfPrimitive trailerValue = trailerParser.ReadValue();

        if (trailerValue is not PdfDictionary trailerDict)
        {
            throw new PdfReaderException(
                $"Trailer must be a dictionary, got {trailerValue.GetType().Name}.");
        }

        return trailerDict;
    }

    /// <summary>
    /// Scans raw bytes from <paramref name="startFrom"/> forward, looking
    /// for the ASCII keyword. Returns the absolute stream offset of the first
    /// character of the keyword, or -1 if not found.
    /// </summary>
    private static long ScanForKeyword(Stream stream, long startFrom, string keyword)
    {
        byte[] kw = Encoding.ASCII.GetBytes(keyword);
        stream.Seek(startFrom, SeekOrigin.Begin);

        // Read from startFrom to end of file (or a generous window).
        using (MemoryStream buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();

            for (int i = 0; i <= bytes.Length - kw.Length; i++)
            {
                bool match = true;

                for (int j = 0; j < kw.Length; j++)
                {
                    if (bytes[i + j] != kw[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return startFrom + i;
                }
            }
        }

        return -1;
    }

    private static PdfDictionary LoadXrefStream(
        Stream stream,
        long offset,
        XrefTable xref)
    {
        PdfObjectParser parser = new PdfObjectParser(stream);
        parser.Seek(offset);

        PdfIndirectObject obj = parser.ReadIndirectObject();

        if (obj.Value is not PdfStream xrefStream)
        {
            throw new PdfReaderException(
                $"Expected cross-reference stream at offset {offset}, " +
                $"got {obj.Value.GetType().Name}.");
        }

        PdfDictionary dict = xrefStream.Dictionary;
        byte[] rawBytes = xrefStream.RawBytes;
        byte[] decodedBytes = DecodeStreamBytes(dict, rawBytes);

        XrefStreamTable streamTable = XrefStreamTable.Parse(dict, decodedBytes);

        foreach (XrefEntry entry in streamTable.Entries)
        {
            if (!xref.Contains(entry.ObjectNumber) || entry.IsFree)
            {
                xref.Set(entry);
            }
        }

        return dict;
    }

    private static byte[] DecodeStreamBytes(PdfDictionary dict, byte[] rawBytes)
    {
        PdfName? filterName = dict.GetName(PdfName.Filter);

        if (filterName is null)
        {
            return rawBytes;
        }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        string resolvedFilter = FilterRegistry.ResolveAlias(filterName.Value);
        return pipeline.Decode(resolvedFilter, rawBytes);
    }

    private static void MergeTrailer(PdfDictionary target, PdfDictionary source)
    {
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in source)
        {
            if (!target.ContainsKey(entry.Key))
            {
                target.Set(entry.Key, entry.Value);
            }
        }
    }

    // ── Private: object loading ───────────────────────────────────────────

    private static PdfIndirectObject? LoadObjectFromFile(
        PdfObjectParser parser,
        XrefTable xref,
        PdfObjectId id)
    {
        long offset = xref.GetOffset(id.ObjectNumber);

        if (offset < 0)
        {
            return null;
        }

        try
        {
            parser.Seek(offset);
            return parser.ReadIndirectObject();
        }
        catch (Exception ex) when (ex is not PdfReaderException)
        {
            throw new PdfReaderException(
                $"Error reading object {id} at offset {offset}.", ex);
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────────

    private static bool IsWhitespace(char c)
    {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f';
    }
}
