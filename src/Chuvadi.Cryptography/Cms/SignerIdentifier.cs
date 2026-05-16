// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 §5.3 — SignerIdentifier
// PHASE: Phase 1.1.4 — CMS / PKCS#7 SignedData decoder

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Cms;

/// <summary>The two variants of a SignerIdentifier.</summary>
public enum SignerIdentifierKind
{
    /// <summary>issuerAndSerialNumber — by issuer DN and certificate serial.</summary>
    IssuerAndSerial = 0,
    /// <summary>subjectKeyIdentifier [0] — by SubjectKeyIdentifier from the cert's extension.</summary>
    SubjectKeyIdentifier = 1,
}

/// <summary>
/// Identifies which certificate in the SignedData.certificates set produced a
/// particular SignerInfo.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// SignerIdentifier ::= CHOICE {
///   issuerAndSerialNumber IssuerAndSerialNumber,
///   subjectKeyIdentifier  [0] SubjectKeyIdentifier
/// }
/// SubjectKeyIdentifier ::= OCTET STRING
/// </code>
/// CMS v1 SignerInfo uses issuerAndSerialNumber only. CMS v3 SignerInfo
/// (RFC 5652 §5.3) added the SKI variant to support keys without a containing
/// certificate, though in practice almost every PDF signature still uses
/// issuerAndSerialNumber.
/// </remarks>
public sealed class SignerIdentifier
{
    private SignerIdentifier(SignerIdentifierKind kind,
        IssuerAndSerialNumber? issuerAndSerial, byte[]? subjectKeyIdentifier)
    {
        Kind = kind;
        IssuerAndSerial = issuerAndSerial;
        SubjectKeyIdentifier = subjectKeyIdentifier;
    }

    /// <summary>Which CHOICE variant this identifier uses.</summary>
    public SignerIdentifierKind Kind { get; }

    /// <summary>The IssuerAndSerial value, populated when Kind == IssuerAndSerial.</summary>
    public IssuerAndSerialNumber? IssuerAndSerial { get; }

    /// <summary>The SKI bytes, populated when Kind == SubjectKeyIdentifier.</summary>
    public byte[]? SubjectKeyIdentifier { get; }

    /// <summary>True when this identifier matches the given certificate.</summary>
    public bool Matches(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (Kind == SignerIdentifierKind.IssuerAndSerial)
        {
            return IssuerAndSerial!.Matches(certificate);
        }

        // SKI match: look up the SKI extension on the certificate.
        X509Extension? skiExt = certificate.Tbs.FindExtension(
            Chuvadi.Cryptography.Oids.KnownOids.SubjectKeyIdentifier);
        if (skiExt is null) { return false; }

        SubjectKeyIdentifierExtension parsed = SubjectKeyIdentifierExtension.Parse(skiExt.Value);
        byte[] a = parsed.KeyIdentifier;
        byte[] b = SubjectKeyIdentifier!;
        if (a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) { return false; }
        }
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => Kind == SignerIdentifierKind.IssuerAndSerial
        ? IssuerAndSerial!.ToString()
        : $"SKI:{Convert.ToHexString(SubjectKeyIdentifier!)}";

    /// <summary>Reads a SignerIdentifier from the reader.</summary>
    public static SignerIdentifier Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Tag tag = reader.PeekTag();
        if (tag.TagClass == Asn1TagClass.Universal &&
            tag.TagNumber == (int)Asn1UniversalTag.Sequence)
        {
            IssuerAndSerialNumber ias = IssuerAndSerialNumber.Read(reader);
            return new SignerIdentifier(SignerIdentifierKind.IssuerAndSerial, ias, null);
        }
        if (tag.TagClass == Asn1TagClass.ContextSpecific && tag.TagNumber == 0)
        {
            byte[] ski = reader.ReadImplicitOctets(0);
            return new SignerIdentifier(SignerIdentifierKind.SubjectKeyIdentifier, null, ski);
        }
        throw new Asn1Exception($"Unexpected SignerIdentifier tag {tag}");
    }
}
