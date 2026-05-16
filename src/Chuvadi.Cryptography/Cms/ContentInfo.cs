// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 §3 — ContentInfo
// PHASE: Phase 1.1.4 — CMS / PKCS#7 SignedData decoder

using System;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.Cms;

/// <summary>
/// The outermost CMS structure — a tagged container that says "the following
/// bytes are of contentType X."
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// ContentInfo ::= SEQUENCE {
///   contentType  ContentType,
///   content      [0] EXPLICIT ANY DEFINED BY contentType
/// }
/// ContentType ::= OBJECT IDENTIFIER
/// </code>
/// Inside a PDF signature dictionary, the bytes at /Contents always form one
/// ContentInfo whose contentType is <c>id-signedData</c> (1.2.840.113549.1.7.2).
/// </remarks>
public sealed class ContentInfo
{
    /// <summary>Initialises a new ContentInfo.</summary>
    public ContentInfo(ObjectIdentifier contentType, byte[] contentEncoded)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(contentEncoded);
        ContentType = contentType;
        ContentEncoded = contentEncoded;
    }

    /// <summary>The content type OID.</summary>
    public ObjectIdentifier ContentType { get; }

    /// <summary>The complete encoded TLV bytes of the inner content.</summary>
    public byte[] ContentEncoded { get; }

    /// <summary>True when this ContentInfo wraps a SignedData.</summary>
    public bool IsSignedData
        => ContentType.Equals(Chuvadi.Cryptography.Oids.KnownOids.CmsSignedData);

    /// <summary>
    /// Decodes the inner content as a SignedData. Throws when this ContentInfo
    /// does not carry a SignedData.
    /// </summary>
    public SignedData GetSignedData()
    {
        if (!IsSignedData)
        {
            throw new InvalidOperationException(
                $"ContentInfo carries {ContentType}, not id-signedData.");
        }
        Asn1Reader r = new(ContentEncoded);
        SignedData sd = SignedData.Read(r);
        r.ExpectEnd();
        return sd;
    }

    /// <summary>Reads a ContentInfo from a reader at its SEQUENCE.</summary>
    public static ContentInfo Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();
        ObjectIdentifier oid = seq.ReadObjectIdentifier();

        // content [0] EXPLICIT ANY DEFINED BY contentType
        Asn1Reader contentWrapper = seq.ReadExplicit(0);
        byte[] inner = contentWrapper.ReadEncoded();
        contentWrapper.ExpectEnd();
        seq.ExpectEnd();
        return new ContentInfo(oid, inner);
    }
}
