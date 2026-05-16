// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1 §12.8.1 — Signature dictionary
// PHASE: Phase 1.1.4 — PDF signature field reading

using System;
using Chuvadi.Cryptography.Cms;

namespace Chuvadi.Pdf.Signatures;

/// <summary>
/// One digital signature found in a PDF document.
/// </summary>
/// <remarks>
/// A PDF signature is the combination of a signature dictionary and the
/// AcroForm field that points to it. PDF 32000-1 §12.8.1 lays out the
/// signature dictionary entries:
/// <list type="bullet">
///   <item>/Type — must be /Sig (or /DocTimeStamp for document timestamps).</item>
///   <item>/Filter — the preferred handler, typically /Adobe.PPKLite.</item>
///   <item>/SubFilter — encoding of the /Contents value; see <see cref="SignatureSubFilter"/>.</item>
///   <item>/ByteRange — the two regions of the file the signature covers.</item>
///   <item>/Contents — the cryptographic envelope itself (CMS / PKCS#7 SignedData
///         for the common SubFilter values).</item>
///   <item>/M — signing time (PDF date string; optional and often unreliable —
///         the authoritative signing time, when present, lives inside the CMS
///         signed attributes).</item>
///   <item>/Name, /Reason, /Location, /ContactInfo — optional signer metadata.</item>
/// </list>
/// </remarks>
public sealed class PdfSignature
{
    /// <summary>Initialises a new PdfSignature.</summary>
    public PdfSignature(
        string fieldName,
        string? filter,
        string? subFilter,
        ByteRange byteRange,
        byte[] contents,
        string? name,
        string? reason,
        string? location,
        string? contactInfo,
        DateTimeOffset? signingTimeFromDictionary,
        bool isDocumentTimestamp)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(byteRange);
        ArgumentNullException.ThrowIfNull(contents);
        FieldName = fieldName;
        Filter = filter;
        SubFilter = subFilter;
        ByteRange = byteRange;
        Contents = contents;
        Name = name;
        Reason = reason;
        Location = location;
        ContactInfo = contactInfo;
        SigningTimeFromDictionary = signingTimeFromDictionary;
        IsDocumentTimestamp = isDocumentTimestamp;
    }

    /// <summary>The AcroForm field name that holds this signature (the /T entry).</summary>
    public string FieldName { get; }

    /// <summary>The /Filter entry — preferred signature handler.</summary>
    public string? Filter { get; }

    /// <summary>The /SubFilter entry — encoding of <see cref="Contents"/>.</summary>
    public string? SubFilter { get; }

    /// <summary>The /ByteRange covering the signed regions.</summary>
    public ByteRange ByteRange { get; }

    /// <summary>The /Contents bytes — the cryptographic envelope.</summary>
    public byte[] Contents { get; }

    /// <summary>The /Name entry — declared signer name.</summary>
    public string? Name { get; }

    /// <summary>The /Reason entry — declared reason for signing.</summary>
    public string? Reason { get; }

    /// <summary>The /Location entry — declared location.</summary>
    public string? Location { get; }

    /// <summary>The /ContactInfo entry.</summary>
    public string? ContactInfo { get; }

    /// <summary>The /M entry parsed as a date, or null when absent or unparseable.</summary>
    /// <remarks>
    /// This is the signer-declared time from the signature dictionary. It is
    /// not trustworthy — only the signingTime attribute inside the CMS signed
    /// attributes (or a CAdES timestamp token) provides a tamper-evident time.
    /// </remarks>
    public DateTimeOffset? SigningTimeFromDictionary { get; }

    /// <summary>True when this is a document timestamp (/Type /DocTimeStamp), not a signature.</summary>
    public bool IsDocumentTimestamp { get; }

    /// <summary>
    /// True when the /SubFilter indicates the /Contents bytes are a CMS / PKCS#7
    /// SignedData container that can be parsed by <see cref="DecodeCms"/>.
    /// </summary>
    public bool IsCmsBased => SignatureSubFilter.IsCmsBased(SubFilter ?? string.Empty);

    /// <summary>
    /// Decodes <see cref="Contents"/> as a CMS / PKCS#7 SignedData container.
    /// Throws if <see cref="SubFilter"/> is not CMS-based.
    /// </summary>
    public SignedData DecodeCms()
    {
        if (!IsCmsBased)
        {
            throw new InvalidOperationException(
                $"Cannot decode /Contents as CMS — SubFilter is '{SubFilter ?? "null"}'.");
        }
        return CmsDecoder.DecodeSignedData(Contents);
    }
}
