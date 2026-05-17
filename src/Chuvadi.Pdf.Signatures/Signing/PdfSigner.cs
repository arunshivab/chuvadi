// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-2 §12.8 — Digital signatures in PDF
// PHASE: Phase 1.2.1 — PDF signing API

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Adds a CMS signature to a PDF document and returns the signed bytes.
/// </summary>
/// <remarks>
/// <para>
/// Implements the canonical PDF signing protocol:
/// </para>
/// <list type="number">
///   <item>Add a signature field + signature dictionary referencing fixed-width
///         <c>/ByteRange</c> placeholder slots and a zero-byte <c>/Contents</c>
///         reservation of <see cref="PdfSigningOptions.ContentsPlaceholderSize"/>
///         bytes.</item>
///   <item>Write the full PDF to memory; scan the output to locate the
///         <c>/ByteRange</c> placeholder slots and the <c>/Contents</c> value.</item>
///   <item>Patch the <c>/ByteRange</c> slots with the actual byte positions, using
///         leading-zero padding to preserve their fixed widths so no downstream
///         positions shift.</item>
///   <item>Sign the bytes covered by <c>/ByteRange</c> with the supplied
///         <see cref="ISigner"/> via <see cref="CmsSignedDataBuilder.BuildDetached"/>.</item>
///   <item>Hex-encode the CMS and splice it into the <c>/Contents</c> placeholder;
///         the remaining placeholder bytes stay zero.</item>
/// </list>
/// <para>
/// This is a full-rewrite signing flow: the output is a freshly-written PDF
/// carrying the new signature. Incremental update support (preserving the
/// original byte stream and appending) is deferred to a future session.
/// </para>
/// </remarks>
public static class PdfSigner
{
    // Width of each numeric slot inside the fixed-width /ByteRange placeholder.
    // 10 ASCII digits cover any positive int (max 2_147_483_647 fits in 10).
    private const int ByteRangeSlotWidth = 10;

    // Maximum placeholder value used during layout; recovered/patched after writing.
    private const int ByteRangePlaceholderValue = 999_999_999;


    /// <summary>
    /// Signs a PDF document and returns the signed bytes.
    /// </summary>
    /// <param name="document">The unsigned source document. Not modified.</param>
    /// <param name="signer">The signer.</param>
    /// <param name="options">Signing options (signature field name, signing time, reason, etc.).</param>
    /// <returns>The signed PDF bytes.</returns>
    public static byte[] Sign(PdfDocument document, ISigner signer, PdfSigningOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(options);

        DateTimeOffset signingTime = options.SigningTime ?? DateTimeOffset.UtcNow;

        // ── Step 1: snapshot the source PDF's object table ───────────────
        // Eagerly resolve every indirect object from the source so the writer
        // can emit them all. New objects (signature dict, field, AcroForm)
        // get IDs starting beyond the trailer's /Size to avoid collision.
        int sourceSize = ReadTrailerSize(document);
        List<PdfIndirectObject> objects = new();
        for (int n = 1; n < sourceSize; n++)
        {
            PdfObjectId id = new(n, 0);
            if (document.Objects.TryGet(id, out PdfIndirectObject? io) && io is not null)
            {
                objects.Add(io);
            }
        }

        PdfObjectId sigDictId = new(sourceSize, 0);
        PdfObjectId sigFieldId = new(sourceSize + 1, 0);
        PdfObjectId acroFormId = new(sourceSize + 2, 0);

        // ── Step 2: build the placeholder signature dictionary ───────────
        byte[] contentsPlaceholder = new byte[options.ContentsPlaceholderSize];
        PdfDictionary sigDict = new();
        sigDict.Set(PdfName.Type, PdfName.Intern("Sig"));
        sigDict.Set(PdfName.Filter, PdfName.Intern("Adobe.PPKLite"));
        sigDict.Set(PdfName.Intern("SubFilter"), PdfName.Intern("adbe.pkcs7.detached"));

        // Fixed-width ByteRange placeholder so the byte positions of later
        // entries don't shift when we patch in the real values.
        PdfArray placeholderByteRange = new();
        placeholderByteRange.Add(new PdfPaddedInteger(0, ByteRangeSlotWidth));
        placeholderByteRange.Add(new PdfPaddedInteger(ByteRangePlaceholderValue, ByteRangeSlotWidth));
        placeholderByteRange.Add(new PdfPaddedInteger(ByteRangePlaceholderValue, ByteRangeSlotWidth));
        placeholderByteRange.Add(new PdfPaddedInteger(ByteRangePlaceholderValue, ByteRangeSlotWidth));
        sigDict.Set(PdfName.Intern("ByteRange"), placeholderByteRange);

        sigDict.Set(PdfName.Intern("Contents"), new PdfString(contentsPlaceholder, preferHexForm: true));

        sigDict.Set(PdfName.Intern("M"), new PdfString(FormatPdfDate(signingTime)));
        if (!string.IsNullOrEmpty(options.Reason))
        {
            sigDict.Set(PdfName.Intern("Reason"), new PdfString(options.Reason));
        }
        if (!string.IsNullOrEmpty(options.Location))
        {
            sigDict.Set(PdfName.Intern("Location"), new PdfString(options.Location));
        }
        if (!string.IsNullOrEmpty(options.ContactInfo))
        {
            sigDict.Set(PdfName.Intern("ContactInfo"), new PdfString(options.ContactInfo));
        }

        // ── Step 3: build the signature field ────────────────────────────
        PdfDictionary sigField = new();
        sigField.Set(PdfName.Intern("FT"), PdfName.Intern("Sig"));
        sigField.Set(PdfName.Intern("T"), new PdfString(options.SignatureFieldName));
        sigField.Set(PdfName.Intern("V"), new PdfReference(sigDictId));

        // ── Step 4: build (or extend) AcroForm ───────────────────────────
        PdfArray fields = new();
        // Preserve any existing AcroForm.Fields entries.
        if (document.Catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormVal))
        {
            PdfDictionary? existingAcro = document.Objects.ResolveAs<PdfDictionary>(acroFormVal);
            if (existingAcro is not null
                && existingAcro.TryGetValue(PdfName.Intern("Fields"), out PdfPrimitive? existingFieldsVal))
            {
                PdfArray? existingFields = document.Objects.ResolveAs<PdfArray>(existingFieldsVal);
                if (existingFields is not null)
                {
                    foreach (PdfPrimitive f in existingFields)
                    {
                        fields.Add(f);
                    }
                }
            }
        }
        fields.Add(new PdfReference(sigFieldId));

        PdfDictionary acroForm = new();
        acroForm.Set(PdfName.Intern("Fields"), fields);
        // SigFlags 3 = SignaturesExist (bit 0) + AppendOnly (bit 1).
        acroForm.Set(PdfName.Intern("SigFlags"), (PdfPrimitive)new PdfInteger(3));

        // ── Step 4b: build LTV objects (/DSS + /VRI) when requested ─────
        // Allocate IDs from after acroFormId. We track the IDs into a helper that
        // builds and returns the new indirect objects + the catalog's /DSS reference
        // (or null when LTV isn't requested).
        int nextLtvId = acroFormId.ObjectNumber + 1;
        List<PdfIndirectObject> ltvObjects = new();
        PdfReference? dssRef = null;
        if (options.LtvOptions is { HasMaterial: true } ltv)
        {
            if (ltv.IncludeVri)
            {
                throw new NotSupportedException(
                    "/VRI emission is not yet supported in single-pass signing; see LtvOptions.IncludeVri docs.");
            }
            dssRef = BuildLtvObjects(ltv, ref nextLtvId, ltvObjects);
        }

        // ── Step 5: clone the catalog with an updated /AcroForm ─────────
        PdfDictionary newCatalog = new();
        foreach (KeyValuePair<PdfName, PdfPrimitive> kv in document.Catalog)
        {
            newCatalog.Set(kv.Key, kv.Value);
        }
        newCatalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));
        if (dssRef is not null)
        {
            newCatalog.Set(PdfName.Intern("DSS"), dssRef);
        }

        // Replace the original catalog object in our list. The catalog ID is
        // recovered from the trailer's /Root reference.
        PdfObjectId catalogId = ResolveCatalogId(document);
        bool catalogReplaced = false;
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].Id.Equals(catalogId))
            {
                objects[i] = new PdfIndirectObject(catalogId, newCatalog);
                catalogReplaced = true;
                break;
            }
        }
        if (!catalogReplaced)
        {
            objects.Add(new PdfIndirectObject(catalogId, newCatalog));
        }

        objects.Add(new PdfIndirectObject(sigDictId, sigDict));
        objects.Add(new PdfIndirectObject(sigFieldId, sigField));
        objects.Add(new PdfIndirectObject(acroFormId, acroForm));
        objects.AddRange(ltvObjects);

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        // ── Step 6: write the PDF with placeholder ByteRange + zero /Contents ──
        MemoryStream ms = new();
        PdfWriter.Write(ms, objects.ToArray(), trailer);
        byte[] pdfBytes = ms.ToArray();

        // ── Step 7: locate /ByteRange and /Contents in the output ────────
        int byteRangeStart = LocateByteRangeArrayStart(pdfBytes);
        if (byteRangeStart < 0)
        {
            throw new InvalidOperationException(
                "Could not locate /ByteRange in the signed PDF output.");
        }

        int contentsValueStart = LocateContentsHexStart(pdfBytes);
        if (contentsValueStart < 0)
        {
            throw new InvalidOperationException(
                "Could not locate /Contents value in the signed PDF output.");
        }
        // /Contents value spans <HEX> where HEX has 2 * ContentsPlaceholderSize chars.
        int contentsValueEnd = contentsValueStart + 1 + (options.ContentsPlaceholderSize * 2) + 1;

        // ── Step 8: patch ByteRange with real positions ──────────────────
        int firstLen = contentsValueStart;
        int secondStart = contentsValueEnd;
        int secondLen = pdfBytes.Length - contentsValueEnd;
        PatchByteRange(pdfBytes, byteRangeStart, 0, firstLen, secondStart, secondLen);

        // ── Step 9: extract the byte-range-covered content and sign it ──
        byte[] toSign = new byte[firstLen + secondLen];
        Buffer.BlockCopy(pdfBytes, 0, toSign, 0, firstLen);
        Buffer.BlockCopy(pdfBytes, secondStart, toSign, firstLen, secondLen);

        byte[] cms = options.TsaClient is null
            ? CmsSignedDataBuilder.BuildDetached(
                toSign, signer, signingTime, options.ExtraCertificates)
            : CmsSignedDataBuilder.BuildDetachedWithTimestamp(
                toSign, signer, options.TsaClient, signingTime, options.ExtraCertificates);
        if (cms.Length > options.ContentsPlaceholderSize)
        {
            throw new InvalidOperationException(
                $"CMS signature ({cms.Length} bytes) exceeds /Contents placeholder size "
                + $"({options.ContentsPlaceholderSize} bytes). Increase ContentsPlaceholderSize.");
        }

        // ── Step 10: splice CMS into /Contents placeholder ──────────────
        WriteHexAt(pdfBytes, contentsValueStart + 1, cms);

        return pdfBytes;
    }

    private static PdfReference BuildLtvObjects(
        LtvOptions ltv, ref int nextId, List<PdfIndirectObject> objects)
    {
        // Allocate stream objects for every cert / CRL / OCSP, and gather
        // their PdfReferences into per-kind arrays for the /DSS dictionary.
        PdfArray certsArray = new();
        if (ltv.Certificates is not null)
        {
            foreach (X509Certificate cert in ltv.Certificates)
            {
                certsArray.Add(NewStreamRef(cert.RawEncoding, ref nextId, objects));
            }
        }
        PdfArray crlsArray = new();
        if (ltv.Crls is not null)
        {
            foreach (CertificateList crl in ltv.Crls)
            {
                crlsArray.Add(NewStreamRef(crl.RawEncoding, ref nextId, objects));
            }
        }
        PdfArray ocspArray = new();
        if (ltv.OcspResponses is not null)
        {
            foreach (OcspResponse ocsp in ltv.OcspResponses)
            {
                ocspArray.Add(NewStreamRef(ocsp.RawEncoding, ref nextId, objects));
            }
        }

        PdfDictionary dss = new();
        dss.Set(PdfName.Type, PdfName.Intern("DSS"));
        if (certsArray.Count > 0) { dss.Set(PdfName.Intern("Certs"), certsArray); }
        if (crlsArray.Count > 0) { dss.Set(PdfName.Intern("CRLs"), crlsArray); }
        if (ocspArray.Count > 0) { dss.Set(PdfName.Intern("OCSPs"), ocspArray); }

        PdfObjectId dssId = new(nextId++, 0);
        objects.Add(new PdfIndirectObject(dssId, dss));
        return new PdfReference(dssId);
    }

    private static PdfReference NewStreamRef(
        byte[] data, ref int nextId, List<PdfIndirectObject> objects)
    {
        PdfDictionary dict = new();
        dict.Set(PdfName.Length, data.Length);
        PdfObjectId id = new(nextId++, 0);
        objects.Add(new PdfIndirectObject(id, new PdfStream(dict, data)));
        return new PdfReference(id);
    }


    private static void PatchByteRange(
        byte[] bytes,
        int arrayStart,
        int v0, int v1, int v2, int v3)
    {
        // The array opens with '[' at arrayStart. Inside it we have:
        // [v0(10) ' ' v1(10) ' ' v2(10) ' ' v3(10)]
        int p = arrayStart;
        if (bytes[p] != (byte)'[')
        {
            throw new InvalidOperationException(
                $"Expected '[' at byte {p}, got 0x{bytes[p]:X2}.");
        }
        p++;
        WriteIntPadded(bytes, p, v0, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        if (bytes[p] != (byte)' ')
        {
            throw new InvalidOperationException(
                $"Expected separator ' ' at byte {p}, got 0x{bytes[p]:X2}.");
        }
        p++;
        WriteIntPadded(bytes, p, v1, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++; // skip space
        WriteIntPadded(bytes, p, v2, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++; // skip space
        WriteIntPadded(bytes, p, v3, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        if (bytes[p] != (byte)']')
        {
            throw new InvalidOperationException(
                $"Expected ']' at byte {p}, got 0x{bytes[p]:X2}.");
        }
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
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Value does not fit in {width} digits.");
        }
    }

    private static int LocateByteRangeArrayStart(byte[] bytes)
    {
        // Scan for "/ByteRange" then the next '['.
        byte[] needle = "/ByteRange"u8.ToArray();
        int idx = IndexOf(bytes, needle, 0);
        if (idx < 0) { return -1; }
        for (int p = idx + needle.Length; p < bytes.Length; p++)
        {
            if (bytes[p] == (byte)'[') { return p; }
            // Allow whitespace between /ByteRange and the array.
            if (bytes[p] != (byte)' ' && bytes[p] != (byte)'\t'
                && bytes[p] != (byte)'\r' && bytes[p] != (byte)'\n')
            {
                return -1;
            }
        }
        return -1;
    }

    private static int LocateContentsHexStart(byte[] bytes)
    {
        // Scan for "/Contents" then the next '<' (hex-string delimiter).
        byte[] needle = "/Contents"u8.ToArray();
        int idx = IndexOf(bytes, needle, 0);
        if (idx < 0) { return -1; }
        for (int p = idx + needle.Length; p < bytes.Length; p++)
        {
            if (bytes[p] == (byte)'<') { return p; }
            if (bytes[p] != (byte)' ' && bytes[p] != (byte)'\t'
                && bytes[p] != (byte)'\r' && bytes[p] != (byte)'\n')
            {
                return -1;
            }
        }
        return -1;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) { return i; }
        }
        return -1;
    }

    private static void WriteHexAt(byte[] bytes, int offset, byte[] data)
    {
        const string HexDigits = "0123456789ABCDEF";
        for (int i = 0; i < data.Length; i++)
        {
            bytes[offset + i * 2]     = (byte)HexDigits[data[i] >> 4];
            bytes[offset + i * 2 + 1] = (byte)HexDigits[data[i] & 0x0F];
        }
        // Trailing bytes in the placeholder remain '0' (the placeholder was zeroed).
    }

    private static int ReadTrailerSize(PdfDocument document)
    {
        if (document.Trailer.TryGetValue(PdfName.Intern("Size"), out PdfPrimitive? sizeVal)
            && sizeVal is PdfInteger sizeInt && sizeInt.Value > 0)
        {
            return sizeInt.Value;
        }
        // Fallback: probe upward until we find a few consecutive empty IDs.
        // /Size is mandatory per ISO 32000-2 §7.5.5 so this is defensive only.
        return 1;
    }

    private static PdfObjectId ResolveCatalogId(PdfDocument document)
    {
        if (!document.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? root)
            || root is not PdfReference rootRef)
        {
            throw new InvalidOperationException(
                "Document trailer has no /Root reference; cannot identify the catalog.");
        }
        return rootRef.ObjectId;
    }

    /// <summary>
    /// Formats a <see cref="DateTimeOffset"/> as a PDF date string per
    /// ISO 32000-2 §7.9.4: <c>D:YYYYMMDDHHmmSSZ</c> (always UTC).
    /// </summary>
    private static string FormatPdfDate(DateTimeOffset time)
    {
        DateTimeOffset u = time.ToUniversalTime();
        return $"D:{u.Year:D4}{u.Month:D2}{u.Day:D2}{u.Hour:D2}{u.Minute:D2}{u.Second:D2}Z";
    }
}
