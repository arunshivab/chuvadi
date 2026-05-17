// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-2 §12.8.5 / ETSI EN 319 142-1 — Document Timestamp
// PHASE: Phase 1.2.6 — /DocTimeStamp via incremental update

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Timestamps;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Adds a document-wide RFC 3161 timestamp (<c>/Type /DocTimeStamp</c>) to a
/// PDF via an incremental update. The timestamp covers the entire document
/// up to (but not including) the new signature's <c>/Contents</c> bytes,
/// proving the document existed and was unchanged at the TSA's time.
/// </summary>
/// <remarks>
/// <para>
/// Unlike an embedded signature timestamp (RFC 3161 token inside a CMS
/// SignerInfo's unsigned attributes), a document timestamp is a
/// standalone signature dictionary whose <c>/Contents</c> is the TSA
/// token itself and whose <c>/SubFilter</c> is <c>ETSI.RFC3161</c>. PDF
/// 2.0 / PAdES / ETSI EN 319 142 LTV workflows lean on this: each
/// archival period adds a fresh document timestamp, extending the
/// trust horizon.
/// </para>
/// <para>
/// The implementation uses <see cref="PdfWriter.WriteIncrementalUpdate"/>,
/// so any pre-existing signatures keep verifying.
/// </para>
/// </remarks>
public static class PdfDocumentTimestamper
{
    /// <summary>
    /// Options for adding a document timestamp.
    /// </summary>
    public sealed class Options
    {
        /// <summary>The TSA client to use.</summary>
        public required ITsaClient TsaClient { get; init; }

        /// <summary>Hash algorithm for the TSA request. Defaults to SHA-256.</summary>
        public HashAlgorithmName HashAlgorithm { get; init; } = HashAlgorithmName.Sha256;

        /// <summary>
        /// Reserved size for the <c>/Contents</c> placeholder.
        /// Defaults to 16 KB which fits typical TSA tokens with margin.
        /// </summary>
        public int ContentsPlaceholderSize { get; init; } = 16384;

        /// <summary>Field name for the document timestamp signature field. Defaults to "Timestamp1".</summary>
        public string FieldName { get; init; } = "Timestamp1";
    }

    /// <summary>
    /// Adds a document timestamp to <paramref name="signedPdfBytes"/>,
    /// returning the augmented bytes.
    /// </summary>
    public static byte[] AddDocumentTimestamp(byte[] signedPdfBytes, Options options)
    {
        ArgumentNullException.ThrowIfNull(signedPdfBytes);
        ArgumentNullException.ThrowIfNull(options);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signedPdfBytes), leaveOpen: false);

        if (!doc.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootVal) || rootVal is not PdfReference rootRef)
        {
            throw new InvalidOperationException("Source trailer has no /Root reference.");
        }
        PdfObjectId catalogId = rootRef.ObjectId;

        if (!doc.Trailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizeVal) || sizeVal is not PdfInteger sizeInt)
        {
            throw new InvalidOperationException("Source trailer has no /Size.");
        }
        int nextId = sizeInt.Value;

        // ── Build the /DocTimeStamp signature dictionary with placeholders ──
        PdfObjectId sigDictId = new(nextId++, 0);
        PdfObjectId sigFieldId = new(nextId++, 0);
        PdfObjectId acroFormId = new(nextId++, 0);

        byte[] contentsPlaceholder = new byte[options.ContentsPlaceholderSize];
        PdfDictionary sigDict = new();
        sigDict.Set(PdfName.Type, PdfName.Intern("DocTimeStamp"));
        sigDict.Set(PdfName.Filter, PdfName.Intern("Adobe.PPKLite"));
        sigDict.Set(PdfName.Intern("SubFilter"), PdfName.Intern("ETSI.RFC3161"));
        sigDict.Set(PdfName.Intern("V"), PdfName.Intern("0.0"));  // /V version per ETSI.

        const int sw = SignatureContentsHelper.ByteRangeSlotWidth;
        const int ph = SignatureContentsHelper.ByteRangePlaceholderValue;
        PdfArray placeholderByteRange = new();
        placeholderByteRange.Add(new PdfPaddedInteger(0, sw));
        placeholderByteRange.Add(new PdfPaddedInteger(ph, sw));
        placeholderByteRange.Add(new PdfPaddedInteger(ph, sw));
        placeholderByteRange.Add(new PdfPaddedInteger(ph, sw));
        sigDict.Set(PdfName.Intern("ByteRange"), placeholderByteRange);

        sigDict.Set(PdfName.Intern("Contents"), new PdfString(contentsPlaceholder, preferHexForm: true));

        // ── Signature field for the timestamp ──
        PdfDictionary sigField = new();
        sigField.Set(PdfName.Intern("FT"), PdfName.Intern("Sig"));
        sigField.Set(PdfName.Intern("T"), new PdfString(options.FieldName));
        sigField.Set(PdfName.Intern("V"), new PdfReference(sigDictId));

        // ── Extend AcroForm.Fields ──
        PdfArray fields = new();
        if (doc.Catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormVal))
        {
            PdfDictionary? existingAcro = doc.Objects.ResolveAs<PdfDictionary>(acroFormVal);
            if (existingAcro is not null
                && existingAcro.TryGetValue(PdfName.Intern("Fields"), out PdfPrimitive? existingFieldsVal))
            {
                PdfArray? existingFields = doc.Objects.ResolveAs<PdfArray>(existingFieldsVal);
                if (existingFields is not null)
                {
                    foreach (PdfPrimitive f in existingFields) { fields.Add(f); }
                }
            }
        }
        fields.Add(new PdfReference(sigFieldId));

        PdfDictionary acroForm = new();
        acroForm.Set(PdfName.Intern("Fields"), fields);
        acroForm.Set(PdfName.Intern("SigFlags"), (PdfPrimitive)new PdfInteger(3));

        // ── New catalog with updated /AcroForm ──
        PdfDictionary newCatalog = new();
        foreach (KeyValuePair<PdfName, PdfPrimitive> kv in doc.Catalog)
        {
            newCatalog.Set(kv.Key, kv.Value);
        }
        newCatalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));

        // ── Write the incremental update WITH PLACEHOLDERS to know layout ──
        List<PdfIndirectObject> newObjects = new()
        {
            new PdfIndirectObject(catalogId, newCatalog),
            new PdfIndirectObject(sigDictId, sigDict),
            new PdfIndirectObject(sigFieldId, sigField),
            new PdfIndirectObject(acroFormId, acroForm),
        };

        byte[] withPlaceholders = PdfWriter.WriteIncrementalUpdate(signedPdfBytes, newObjects);

        // ── Locate /ByteRange and /Contents inside the appended section ──
        // Search only past signedPdfBytes.Length to disambiguate against existing signatures.
        int searchFrom = signedPdfBytes.Length;
        int byteRangeStart = FindAfter(withPlaceholders, "/ByteRange"u8, searchFrom);
        if (byteRangeStart < 0)
        {
            throw new InvalidOperationException("Could not locate /ByteRange in the appended section.");
        }
        // Advance to '['
        while (byteRangeStart < withPlaceholders.Length && withPlaceholders[byteRangeStart] != (byte)'[')
        {
            byteRangeStart++;
        }

        int contentsValueStart = FindAfter(withPlaceholders, "/Contents"u8, searchFrom);
        if (contentsValueStart < 0)
        {
            throw new InvalidOperationException("Could not locate /Contents in the appended section.");
        }
        while (contentsValueStart < withPlaceholders.Length && withPlaceholders[contentsValueStart] != (byte)'<')
        {
            contentsValueStart++;
        }
        int contentsValueEnd = contentsValueStart + 1 + (options.ContentsPlaceholderSize * 2) + 1;

        // ── Patch the /ByteRange ──
        int firstLen = contentsValueStart;
        int secondStart = contentsValueEnd;
        int secondLen = withPlaceholders.Length - contentsValueEnd;
        SignatureContentsHelper.PatchByteRange(withPlaceholders, byteRangeStart, 0, firstLen, secondStart, secondLen);

        // ── Hash the byte-range-covered content ──
        byte[] toHash = new byte[firstLen + secondLen];
        Buffer.BlockCopy(withPlaceholders, 0, toHash, 0, firstLen);
        Buffer.BlockCopy(withPlaceholders, secondStart, toHash, firstLen, secondLen);

        IHashAlgorithm hasher = HashFactory.Create(options.HashAlgorithm);
        hasher.Update(toHash);
        byte[] digest = new byte[hasher.DigestSize];
        hasher.Finish(digest);

        // ── Build the TSA request and fetch ──
        TimeStampRequest tsReq = TimeStampRequest.ForDigest(digest, options.HashAlgorithm, certReq: true);
        TimeStampResponse tsResp = options.TsaClient.Fetch(tsReq);
        if (!tsResp.IsGranted || tsResp.TimeStampToken is null)
        {
            throw new InvalidOperationException(
                $"TSA did not grant the timestamp: status={tsResp.Status}.");
        }

        byte[] token = tsResp.TimeStampToken.RawEncoding;
        if (token.Length > options.ContentsPlaceholderSize)
        {
            throw new InvalidOperationException(
                $"TSA token ({token.Length} bytes) exceeds /Contents placeholder size "
                + $"({options.ContentsPlaceholderSize} bytes). Increase Options.ContentsPlaceholderSize.");
        }

        // ── Splice token into /Contents ──
        SignatureContentsHelper.WriteHexAt(withPlaceholders, contentsValueStart + 1, token);

        return withPlaceholders;
    }

    private static int FindAfter(byte[] bytes, ReadOnlySpan<byte> needle, int searchFrom)
    {
        for (int i = searchFrom; i <= bytes.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j]) { match = false; break; }
            }
            if (match) { return i; }
        }
        return -1;
    }
}
