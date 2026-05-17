// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 6960 §4.1.1 — CertID
// PHASE: Phase 1.1.4 — OCSP

using System;
using System.Numerics;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Ocsp;

/// <summary>
/// Identifies a certificate inside an OCSP response.
/// </summary>
/// <remarks>
/// RFC 6960 §4.1.1:
/// <code>
/// CertID ::= SEQUENCE {
///   hashAlgorithm   AlgorithmIdentifier,
///   issuerNameHash  OCTET STRING,  -- Hash of issuer's DN
///   issuerKeyHash   OCTET STRING,  -- Hash of issuer's public key
///   serialNumber    CertificateSerialNumber
/// }
/// </code>
/// The two hashes are computed over the issuer cert's <c>tbsCertificate.subject</c>
/// (DER bytes) and the <c>BIT STRING</c> content of its <c>subjectPublicKey</c>
/// respectively, using <c>hashAlgorithm</c>.
/// </remarks>
public sealed class CertId
{
    /// <summary>Initialises a new CertID.</summary>
    public CertId(AlgorithmIdentifier hashAlgorithm, byte[] issuerNameHash,
        byte[] issuerKeyHash, BigInteger serialNumber)
    {
        ArgumentNullException.ThrowIfNull(hashAlgorithm);
        ArgumentNullException.ThrowIfNull(issuerNameHash);
        ArgumentNullException.ThrowIfNull(issuerKeyHash);
        HashAlgorithm = hashAlgorithm;
        IssuerNameHash = issuerNameHash;
        IssuerKeyHash = issuerKeyHash;
        SerialNumber = serialNumber;
    }

    /// <summary>Hash algorithm used to compute the two issuer-derived hashes.</summary>
    public AlgorithmIdentifier HashAlgorithm { get; }

    /// <summary>Hash of the issuer's distinguished name.</summary>
    public byte[] IssuerNameHash { get; }

    /// <summary>Hash of the issuer's public key.</summary>
    public byte[] IssuerKeyHash { get; }

    /// <summary>The subject certificate's serial number.</summary>
    public BigInteger SerialNumber { get; }
}
