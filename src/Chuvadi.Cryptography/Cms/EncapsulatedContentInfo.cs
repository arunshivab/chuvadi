// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 §5.2 — EncapsulatedContentInfo
// PHASE: Phase 1.1.4 — CMS / PKCS#7 SignedData decoder

using System;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.Cms;

/// <summary>
/// The content being signed (attached) or referenced (detached) by a SignedData.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// EncapsulatedContentInfo ::= SEQUENCE {
///   eContentType    OBJECT IDENTIFIER,
///   eContent    [0] EXPLICIT OCTET STRING OPTIONAL
/// }
/// </code>
/// For PDF signatures the typical pattern is:
/// <list type="bullet">
///   <item><c>adbe.pkcs7.detached</c> — eContentType = id-data (1.2.840.113549.1.7.1)
///         and eContent is absent. The signed bytes are the PDF byte range, supplied
///         out-of-band.</item>
///   <item><c>ETSI.RFC3161</c> — eContentType = id-ct-TSTInfo (1.2.840.113549.1.9.16.1.4)
///         and eContent contains the encoded TSTInfo.</item>
/// </list>
/// </remarks>
public sealed class EncapsulatedContentInfo
{
    /// <summary>Initialises a new EncapsulatedContentInfo.</summary>
    public EncapsulatedContentInfo(ObjectIdentifier contentType, byte[]? content)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        ContentType = contentType;
        Content = content;
    }

    /// <summary>The content type OID (eContentType).</summary>
    public ObjectIdentifier ContentType { get; }

    /// <summary>
    /// The wrapped content bytes when present; null when this is a detached signature.
    /// </summary>
    public byte[]? Content { get; }

    /// <summary>True when this is a detached signature (no eContent).</summary>
    public bool IsDetached => Content is null;

    /// <summary>Reads an EncapsulatedContentInfo from a reader at its SEQUENCE.</summary>
    public static EncapsulatedContentInfo Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();
        ObjectIdentifier oid = seq.ReadObjectIdentifier();

        byte[]? content = null;
        if (seq.HasContextSpecific(0))
        {
            // [0] EXPLICIT OCTET STRING
            Asn1Reader inner = seq.ReadExplicit(0);
            content = inner.ReadOctetString();
            inner.ExpectEnd();
        }

        seq.ExpectEnd();
        return new EncapsulatedContentInfo(oid, content);
    }
}
