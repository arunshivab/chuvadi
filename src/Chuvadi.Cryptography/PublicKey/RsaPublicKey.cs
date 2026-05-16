// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 3279 §2.3.1 — RSA SubjectPublicKey encoding
//        RFC 8017 §3.1 — RSAPublicKey ASN.1 structure
// PHASE: Phase 1.1.4 — Public-key cryptography

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// An RSA public key — modulus n and public exponent e.
/// </summary>
/// <remarks>
/// ASN.1 (RFC 8017 §3.1):
/// <code>
/// RSAPublicKey ::= SEQUENCE {
///   modulus         INTEGER,  -- n
///   publicExponent  INTEGER   -- e
/// }
/// </code>
/// Inside an X.509 SubjectPublicKeyInfo the algorithm OID is
/// <see cref="Oids.KnownOids.RsaEncryption"/> (1.2.840.113549.1.1.1) and the
/// BIT STRING contents are exactly the DER encoding above.
/// </remarks>
public sealed class RsaPublicKey : IPublicKey
{
    /// <summary>Initialises a new RsaPublicKey.</summary>
    public RsaPublicKey(BigInteger modulus, BigInteger publicExponent)
    {
        if (modulus.Sign <= 0)
        {
            throw new ArgumentException("RSA modulus must be positive.", nameof(modulus));
        }
        if (publicExponent.Sign <= 0)
        {
            throw new ArgumentException("RSA public exponent must be positive.", nameof(publicExponent));
        }
        Modulus = modulus;
        PublicExponent = publicExponent;
    }

    /// <inheritdoc/>
    public PublicKeyAlgorithm Algorithm => PublicKeyAlgorithm.Rsa;

    /// <summary>The RSA modulus (n).</summary>
    public BigInteger Modulus { get; }

    /// <summary>The RSA public exponent (e), typically 65537.</summary>
    public BigInteger PublicExponent { get; }

    /// <summary>The modulus size in bytes (k), used for padding calculations in RFC 8017.</summary>
    public int ModulusSizeBytes
    {
        get
        {
            int bits = (int)Math.Ceiling(BigInteger.Log(Modulus, 2));
            // Round up to whole bytes
            return (bits + 7) / 8;
        }
    }

    /// <summary>
    /// Parses an RSA public key from the BIT STRING contents of a SubjectPublicKeyInfo.
    /// </summary>
    /// <remarks>
    /// The input is the raw bytes inside the BIT STRING (i.e. the DER encoding of the
    /// RSAPublicKey SEQUENCE), not the BIT STRING itself.
    /// </remarks>
    public static RsaPublicKey FromSubjectPublicKey(byte[] subjectPublicKey)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKey);
        Asn1Reader r = new(subjectPublicKey);
        Asn1Reader seq = r.ReadSequence();
        BigInteger modulus = seq.ReadInteger();
        BigInteger exponent = seq.ReadInteger();
        seq.ExpectEnd();
        r.ExpectEnd();
        return new RsaPublicKey(modulus, exponent);
    }

    /// <summary>
    /// Parses an RSA public key from a SubjectPublicKeyInfo container.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the SubjectPublicKeyInfo algorithm is not RSA, or the BIT STRING has padding bits.
    /// </exception>
    public static RsaPublicKey FromSubjectPublicKeyInfo(SubjectPublicKeyInfo spki)
    {
        ArgumentNullException.ThrowIfNull(spki);
        if (!spki.Algorithm.Algorithm.Equals(Chuvadi.Cryptography.Oids.KnownOids.RsaEncryption))
        {
            throw new ArgumentException(
                $"SubjectPublicKeyInfo algorithm is {spki.Algorithm.Algorithm}, expected RSA encryption.",
                nameof(spki));
        }
        if (spki.SubjectPublicKey.UnusedBitsInFinalOctet != 0)
        {
            throw new ArgumentException(
                "RSA SubjectPublicKey BIT STRING must have zero unused bits.", nameof(spki));
        }
        return FromSubjectPublicKey(spki.SubjectPublicKey.Bytes);
    }
}
