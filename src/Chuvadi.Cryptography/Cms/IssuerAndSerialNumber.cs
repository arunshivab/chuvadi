// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 §10.2.4 — IssuerAndSerialNumber
// PHASE: Phase 1.1.4 — CMS / PKCS#7 SignedData decoder

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Cms;

/// <summary>
/// Identifies an X.509 certificate by its issuer's distinguished name and the
/// certificate's serial number.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// IssuerAndSerialNumber ::= SEQUENCE {
///   issuer        Name,
///   serialNumber  CertificateSerialNumber
/// }
/// CertificateSerialNumber ::= INTEGER
/// </code>
/// The pair (issuer DN, serial number) uniquely identifies a certificate — issuers
/// are required by RFC 5280 to never reuse a serial number within their domain.
/// This is the most common SignerIdentifier form in CMS signatures used by PDFs
/// today, including all signatures produced by Adobe Acrobat.
/// </remarks>
public sealed class IssuerAndSerialNumber
{
    /// <summary>Initialises a new IssuerAndSerialNumber.</summary>
    public IssuerAndSerialNumber(X509Name issuer, BigInteger serialNumber)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        Issuer = issuer;
        SerialNumber = serialNumber;
    }

    /// <summary>The issuer distinguished name.</summary>
    public X509Name Issuer { get; }

    /// <summary>The certificate serial number.</summary>
    public BigInteger SerialNumber { get; }

    /// <summary>
    /// True when this identifier matches the given certificate.
    /// </summary>
    /// <remarks>
    /// Matching uses byte-identical comparison of the issuer Name encoding
    /// (per RFC 5280 §7.1) and arithmetic equality of the serial number.
    /// </remarks>
    public bool Matches(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.Tbs.SerialNumber.Equals(SerialNumber))
        {
            return false;
        }

        byte[] a = Issuer.RawEncoding;
        byte[] b = certificate.Tbs.Issuer.RawEncoding;
        if (a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) { return false; }
        }
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Issuer} / serial #{SerialNumber}";

    /// <summary>Reads an IssuerAndSerialNumber from a reader at its SEQUENCE.</summary>
    public static IssuerAndSerialNumber Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();
        X509Name issuer = X509Name.Read(seq);
        BigInteger serial = seq.ReadInteger();
        seq.ExpectEnd();
        return new IssuerAndSerialNumber(issuer, serial);
    }
}
