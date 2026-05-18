// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-2 §12.8.4.3 — Document Security Store (DSS)
// PHASE: Phase 1.1.4 — DSS dictionary extraction

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Signatures.Dss;

/// <summary>
/// The Document Security Store as defined in ISO 32000-2 §12.8.4.3.
/// </summary>
/// <remarks>
/// <para>
/// The DSS is a dictionary on the document Catalog (key <c>/DSS</c>) carrying
/// long-term validation material for the document's signatures: certificates,
/// CRLs, and OCSP responses, each stored as a stream object referenced by an
/// indirect reference inside the <c>/Certs</c>, <c>/CRLs</c>, and <c>/OCSPs</c>
/// arrays respectively. A signer attaches these so that the document can be
/// validated long after the issuing CA's CRL distribution points and OCSP
/// responders are unreachable.
/// </para>
/// <para>
/// This class extracts the top-level <c>/Certs</c>, <c>/CRLs</c>, and
/// <c>/OCSPs</c> arrays and decodes each into the corresponding Chuvadi type.
/// Streams that fail to decode are silently skipped rather than failing the
/// whole extraction — a single malformed CRL inside a DSS shouldn't poison
/// the rest. The optional <c>/VRI</c> sub-dictionary (per-signature validation
/// info, also defined in §12.8.4.3) is not yet parsed and is reserved for a
/// future session.
/// </para>
/// </remarks>
public sealed class DocumentSecurityStore
{
    private readonly X509Certificate[] _certificates;
    private readonly CertificateList[] _crls;
    private readonly OcspResponse[] _ocspResponses;
    private readonly Dictionary<string, VriEntry> _vri;

    /// <summary>Initialises a new DSS snapshot.</summary>
    public DocumentSecurityStore(
        IList<X509Certificate> certificates,
        IList<CertificateList> crls,
        IList<OcspResponse> ocspResponses)
        : this(certificates, crls, ocspResponses, new Dictionary<string, VriEntry>())
    {
    }

    /// <summary>Initialises a new DSS snapshot including a VRI sub-dictionary.</summary>
    public DocumentSecurityStore(
        IList<X509Certificate> certificates,
        IList<CertificateList> crls,
        IList<OcspResponse> ocspResponses,
        IDictionary<string, VriEntry> vri)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(crls);
        ArgumentNullException.ThrowIfNull(ocspResponses);
        ArgumentNullException.ThrowIfNull(vri);
        _certificates = new X509Certificate[certificates.Count];
        certificates.CopyTo(_certificates, 0);
        _crls = new CertificateList[crls.Count];
        crls.CopyTo(_crls, 0);
        _ocspResponses = new OcspResponse[ocspResponses.Count];
        ocspResponses.CopyTo(_ocspResponses, 0);
        _vri = new Dictionary<string, VriEntry>(vri, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The certificates carried in the DSS <c>/Certs</c> array.</summary>
    public ReadOnlyCollection<X509Certificate> Certificates => new(_certificates);

    /// <summary>The CRLs carried in the DSS <c>/CRLs</c> array.</summary>
    public ReadOnlyCollection<CertificateList> Crls => new(_crls);

    /// <summary>The OCSP responses carried in the DSS <c>/OCSPs</c> array.</summary>
    public ReadOnlyCollection<OcspResponse> OcspResponses => new(_ocspResponses);

    /// <summary>
    /// Per-signature validation material keyed by upper-case SHA-1 hex of the
    /// signature's binary <c>/Contents</c> bytes (ISO 32000-2 §12.8.4.3).
    /// Empty when the DSS contains no <c>/VRI</c> sub-dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, VriEntry> Vri => _vri;

    /// <summary>
    /// Looks up the VRI entry corresponding to a signature whose CMS
    /// <c>/Contents</c> bytes are <paramref name="signatureContents"/>.
    /// Returns null when there is no matching entry.
    /// </summary>
    /// <remarks>
    /// The VRI lookup key is the upper-case SHA-1 hex of the raw bytes
    /// inside the signature dictionary's <c>/Contents</c> value (before
    /// any hex decoding done by the PDF parser, expressed as binary).
    /// </remarks>
    public VriEntry? TryFindForSignature(byte[] signatureContents)
    {
        ArgumentNullException.ThrowIfNull(signatureContents);
        if (_vri.Count == 0) { return null; }
        byte[] digest = Sha1.Compute(signatureContents);
        string key = Convert.ToHexString(digest);  // already upper-case
        return _vri.TryGetValue(key, out VriEntry? entry) ? entry : null;
    }

    /// <summary>True iff the DSS is empty (no certs, CRLs, OCSPs, or VRI entries).</summary>
    public bool IsEmpty =>
        _certificates.Length == 0
        && _crls.Length == 0
        && _ocspResponses.Length == 0
        && _vri.Count == 0;

    /// <summary>
    /// Reads the <c>/DSS</c> dictionary from <paramref name="catalog"/> and
    /// decodes its arrays. Returns null when the catalog has no DSS.
    /// </summary>
    /// <param name="catalog">The document's Catalog dictionary.</param>
    /// <param name="objects">The object store used to resolve indirect refs.</param>
    public static DocumentSecurityStore? TryRead(PdfDictionary catalog, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(objects);

        PdfDictionary? dss = objects.ResolveDictionaryEntry<PdfDictionary>(catalog, PdfName.Intern("DSS"));
        if (dss is null) { return null; }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();

        List<X509Certificate> certs = new();
        foreach (byte[] der in EnumerateStreamArray(dss, "Certs", objects, pipeline))
        {
            try { certs.Add(X509Certificate.Decode(der)); }
            catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                          or ArgumentException)
            { /* skip malformed */ }
        }

        List<CertificateList> crls = new();
        foreach (byte[] der in EnumerateStreamArray(dss, "CRLs", objects, pipeline))
        {
            try { crls.Add(CertificateList.Decode(der)); }
            catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                          or NotSupportedException
                                          or ArgumentException)
            { /* skip */ }
        }

        List<OcspResponse> ocsps = new();
        foreach (byte[] der in EnumerateStreamArray(dss, "OCSPs", objects, pipeline))
        {
            try { ocsps.Add(OcspResponse.Decode(der)); }
            catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                          or ArgumentException)
            { /* skip */ }
        }

        Dictionary<string, VriEntry> vri = ReadVri(dss, objects, pipeline);

        return new DocumentSecurityStore(certs, crls, ocsps, vri);
    }

    private static Dictionary<string, VriEntry> ReadVri(
        PdfDictionary dss, PdfObjectStore objects, FilterPipeline pipeline)
    {
        Dictionary<string, VriEntry> result = new(StringComparer.OrdinalIgnoreCase);
        PdfDictionary? vriDict = objects.ResolveDictionaryEntry<PdfDictionary>(dss, PdfName.Intern("VRI"));
        if (vriDict is null) { return result; }

        foreach (KeyValuePair<PdfName, PdfPrimitive> kv in vriDict)
        {
            string key = kv.Key.Value;
            if (key.Length == 0) { continue; }

            PdfDictionary? entryDict = objects.ResolveAs<PdfDictionary>(kv.Value);
            if (entryDict is null) { continue; }

            // VRI entries use singular keys (Cert / CRL / OCSP), not plurals.
            List<X509Certificate> certs = new();
            foreach (byte[] der in EnumerateStreamArray(entryDict, "Cert", objects, pipeline))
            {
                try { certs.Add(X509Certificate.Decode(der)); }
                catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                              or ArgumentException)
                { /* skip malformed */ }
            }

            List<CertificateList> crls = new();
            foreach (byte[] der in EnumerateStreamArray(entryDict, "CRL", objects, pipeline))
            {
                try { crls.Add(CertificateList.Decode(der)); }
                catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                              or NotSupportedException
                                              or ArgumentException)
                { /* skip */ }
            }

            List<OcspResponse> ocsps = new();
            foreach (byte[] der in EnumerateStreamArray(entryDict, "OCSP", objects, pipeline))
            {
                try { ocsps.Add(OcspResponse.Decode(der)); }
                catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                              or ArgumentException)
                { /* skip */ }
            }

            // Normalise the lookup key to upper-case hex so callers can match
            // it from a SHA-1 digest with Convert.ToHexString without worrying
            // about which case was used in the source PDF.
            result[key.ToUpperInvariant()] = new VriEntry(certs, crls, ocsps);
        }
        return result;
    }

    /// <summary>
    /// Walks one of the DSS arrays (<paramref name="key"/> = "Certs" / "CRLs" / "OCSPs"),
    /// resolves each indirect reference to its PdfStream, applies filters, and
    /// yields the decoded raw bytes.
    /// </summary>
    private static IEnumerable<byte[]> EnumerateStreamArray(
        PdfDictionary dss, string key, PdfObjectStore objects, FilterPipeline pipeline)
    {
        PdfArray? array = objects.ResolveDictionaryEntry<PdfArray>(dss, PdfName.Intern(key));
        if (array is null) { yield break; }

        foreach (PdfPrimitive item in array)
        {
            PdfStream? stream = objects.ResolveAs<PdfStream>(item);
            if (stream is null) { continue; }

            byte[]? decoded = TryDecodeStream(stream, pipeline);
            if (decoded is not null) { yield return decoded; }
        }
    }

    /// <summary>
    /// Decodes a PdfStream, walking its <c>/Filter</c> chain.
    /// Returns null when decoding fails.
    /// </summary>
    private static byte[]? TryDecodeStream(PdfStream stream, FilterPipeline pipeline)
    {
        try
        {
            byte[] data = stream.RawBytes;
            PdfPrimitive? filter = stream.Filter;
            if (filter is null) { return data; }

            if (filter is PdfName single)
            {
                string resolved = FilterRegistry.ResolveAlias(single.Value);
                return pipeline.Decode(resolved, data, BuildFilterParams(stream.Dictionary, 0));
            }
            if (filter is PdfArray chain)
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    if (chain[i] is not PdfName fn) { return null; }
                    string resolved = FilterRegistry.ResolveAlias(fn.Value);
                    data = pipeline.Decode(resolved, data, BuildFilterParams(stream.Dictionary, i));
                }
                return data;
            }
            return null;
        }
        catch (Exception ex) when (ex is FilterException
                                      or InvalidDataException
                                      or ArgumentException)
        { return null; }
    }

    /// <summary>
    /// Returns the FilterParameters that apply to filter index <paramref name="index"/>
    /// of the stream's filter chain. DSS streams almost never carry
    /// <c>/DecodeParms</c> in practice (they use plain FlateDecode), so we return
    /// null and rely on the filter's defaults. If a future fixture requires
    /// custom decode parameters they can be threaded through here.
    /// </summary>
    private static FilterParameters? BuildFilterParams(PdfDictionary streamDict, int index)
    {
        _ = streamDict;
        _ = index;
        return null;
    }
}
