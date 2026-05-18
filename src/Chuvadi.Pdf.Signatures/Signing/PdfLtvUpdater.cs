// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-2 §12.8.4.3 — DSS / VRI
// PHASE: Phase 1.2.6 — VRI emission post-signing

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Signatures.Dss;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Adds (or augments) a Long-Term Validation <c>/DSS</c> dictionary on an
/// already-signed PDF, optionally emitting <c>/VRI</c> entries keyed by
/// SHA-1 of each signature's <c>/Contents</c>.
/// </summary>
/// <remarks>
/// <para>
/// The result is appended as an ISO 32000-1 §7.5.6 incremental update,
/// so all existing signatures on the source document remain valid: the
/// new <c>/DSS</c> bytes sit outside their byte ranges.
/// </para>
/// <para>
/// This is the natural counterpart to <see cref="PdfSigner"/>'s LTV
/// embedding: where <c>PdfSigner</c> bakes <c>/DSS</c> in at sign time
/// (without VRI, because the VRI key would land inside the signed
/// range), <c>PdfLtvUpdater</c> adds <c>/DSS</c> + <c>/VRI</c> after
/// signing. Common workflows:
/// </para>
/// <list type="bullet">
///   <item>Sign with the LTV material you have at sign time, then call
///   <see cref="AddLtvMaterial"/> later (after fresh OCSP / CRL fetches)
///   to refresh the validation data without invalidating the signature.</item>
///   <item>Sign without any LTV material, then add a full
///   <c>/DSS</c> + <c>/VRI</c> pair afterward.</item>
/// </list>
/// </remarks>
public static class PdfLtvUpdater
{
    /// <summary>
    /// Appends an incremental update carrying a <c>/DSS</c> dictionary that
    /// merges <paramref name="material"/> with any existing DSS in the source.
    /// </summary>
    /// <param name="signedPdfBytes">The source PDF (must be signed; any
    /// existing /DSS is preserved and extended).</param>
    /// <param name="material">The LTV material to embed. <c>IncludeVri</c>
    /// controls whether per-signature VRI entries are added.</param>
    public static byte[] AddLtvMaterial(byte[] signedPdfBytes, LtvOptions material)
    {
        ArgumentNullException.ThrowIfNull(signedPdfBytes);
        ArgumentNullException.ThrowIfNull(material);
        if (!material.HasMaterial)
        {
            throw new ArgumentException("LtvOptions carries no material.", nameof(material));
        }

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signedPdfBytes), leaveOpen: false);

        // Identify the catalog object ID (from trailer /Root).
        if (!doc.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootVal) || rootVal is not PdfReference rootRef)
        {
            throw new InvalidOperationException("Source trailer has no /Root reference.");
        }
        PdfObjectId catalogId = rootRef.ObjectId;

        // Find the next free object number for new objects.
        int nextId;
        if (doc.Trailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizeVal) && sizeVal is PdfInteger si)
        {
            nextId = si.Value;
        }
        else
        {
            throw new InvalidOperationException("Source trailer has no /Size.");
        }

        // Gather existing DSS material (so we can merge).
        DocumentSecurityStore? existing = DocumentSecurityStore.TryRead(doc.Catalog, doc.Objects);

        // Collect merged material.
        List<X509Certificate> allCerts = new();
        List<CertificateList> allCrls = new();
        List<OcspResponse> allOcsps = new();
        if (existing is not null)
        {
            allCerts.AddRange(existing.Certificates);
            allCrls.AddRange(existing.Crls);
            allOcsps.AddRange(existing.OcspResponses);
        }
        if (material.Certificates is not null) { allCerts.AddRange(material.Certificates); }
        if (material.Crls is not null) { allCrls.AddRange(material.Crls); }
        if (material.OcspResponses is not null) { allOcsps.AddRange(material.OcspResponses); }

        // Allocate stream objects for each cert / CRL / OCSP.
        List<PdfIndirectObject> newObjects = new();
        PdfArray certArr = NewStreamArray(allCerts, c => c.RawEncoding, ref nextId, newObjects);
        PdfArray crlArr = NewStreamArray(allCrls, c => c.RawEncoding, ref nextId, newObjects);
        PdfArray ocspArr = NewStreamArray(allOcsps, o => o.RawEncoding, ref nextId, newObjects);

        PdfDictionary dssDict = new();
        dssDict.Set(PdfName.Type, PdfName.Intern("DSS"));
        if (certArr.Count > 0) { dssDict.Set(PdfName.Intern("Certs"), certArr); }
        if (crlArr.Count > 0) { dssDict.Set(PdfName.Intern("CRLs"), crlArr); }
        if (ocspArr.Count > 0) { dssDict.Set(PdfName.Intern("OCSPs"), ocspArr); }

        // Per-signature /VRI entries.
        if (material.IncludeVri)
        {
            // For each signature in the doc, compute SHA-1(Contents) and emit a /VRI entry
            // with the (full) material at the top level mirrored as singular keys.
            PdfDictionary vriDict = new();
            foreach (PdfSignature sig in doc.Signatures())
            {
                byte[] sha1 = Sha1.Compute(sig.Contents);
                string keyHex = Convert.ToHexString(sha1);

                PdfDictionary vriEntry = new();
                if (certArr.Count > 0) { vriEntry.Set(PdfName.Intern("Cert"), CloneArray(certArr)); }
                if (crlArr.Count > 0) { vriEntry.Set(PdfName.Intern("CRL"), CloneArray(crlArr)); }
                if (ocspArr.Count > 0) { vriEntry.Set(PdfName.Intern("OCSP"), CloneArray(ocspArr)); }

                PdfObjectId vriEntryId = new(nextId++, 0);
                newObjects.Add(new PdfIndirectObject(vriEntryId, vriEntry));

                vriDict.Set(PdfName.Intern(keyHex), new PdfReference(vriEntryId));
            }
            if (doc.Signatures().Count > 0)
            {
                dssDict.Set(PdfName.Intern("VRI"), vriDict);
            }
        }

        // Emit the DSS itself.
        PdfObjectId dssId = new(nextId++, 0);
        newObjects.Add(new PdfIndirectObject(dssId, dssDict));

        // Build a new catalog object that references the new DSS. We copy
        // through the existing catalog and overlay /DSS. The catalog is
        // identified by `catalogId`; reusing the same ID makes the
        // incremental update replace it.
        PdfDictionary newCatalog = new();
        foreach (KeyValuePair<PdfName, PdfPrimitive> kv in doc.Catalog)
        {
            newCatalog.Set(kv.Key, kv.Value);
        }
        newCatalog.Set(PdfName.Intern("DSS"), new PdfReference(dssId));
        newObjects.Add(new PdfIndirectObject(catalogId, newCatalog));

        return PdfWriter.WriteIncrementalUpdate(signedPdfBytes, newObjects);
    }

    private static PdfArray NewStreamArray<T>(
        IEnumerable<T> items,
        Func<T, byte[]> getBytes,
        ref int nextId,
        List<PdfIndirectObject> objects)
    {
        PdfArray array = new();
        foreach (T item in items)
        {
            byte[] data = getBytes(item);
            PdfDictionary streamDict = new();
            streamDict.Set(PdfName.Length, data.Length);
            PdfObjectId id = new(nextId++, 0);
            objects.Add(new PdfIndirectObject(id, new PdfStream(streamDict, data)));
            array.Add(new PdfReference(id));
        }
        return array;
    }

    private static PdfArray CloneArray(PdfArray source)
    {
        PdfArray copy = new();
        foreach (PdfPrimitive entry in source) { copy.Add(entry); }
        return copy;
    }
}
