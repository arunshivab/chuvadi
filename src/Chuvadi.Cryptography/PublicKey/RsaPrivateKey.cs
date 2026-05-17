// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 8017 §3.2 (RSAPrivateKey); RFC 5208 (PKCS#8 PrivateKeyInfo)
// PHASE: Phase 1.1.4 — RSA signing

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// An RSA private key — modulus n, public exponent e, and private exponent d.
/// </summary>
/// <remarks>
/// <para>
/// This class carries the minimum data needed for signing (n, e, d). Production
/// CRT parameters (p, q, dP, dQ, qInv) per RFC 8017 §3.2 are not currently
/// stored — signing operates via plain modular exponentiation. CRT will be
/// added in a future session for performance.
/// </para>
/// <para>
/// Loaded from PKCS#8 unencrypted PrivateKeyInfo (RFC 5208) via
/// <see cref="FromPkcs8"/>, or from the bare RSAPrivateKey DER via
/// <see cref="FromRsaPrivateKey"/>.
/// </para>
/// </remarks>
public sealed class RsaPrivateKey
{
    /// <summary>Initialises a new RSA private key.</summary>
    /// <param name="modulus">The RSA modulus n; must be positive.</param>
    /// <param name="publicExponent">The RSA public exponent e; must be positive.</param>
    /// <param name="privateExponent">The RSA private exponent d; must be positive.</param>
    public RsaPrivateKey(BigInteger modulus, BigInteger publicExponent, BigInteger privateExponent)
    {
        if (modulus.Sign <= 0)
        {
            throw new ArgumentException("RSA modulus must be positive.", nameof(modulus));
        }
        if (publicExponent.Sign <= 0)
        {
            throw new ArgumentException("RSA public exponent must be positive.", nameof(publicExponent));
        }
        if (privateExponent.Sign <= 0)
        {
            throw new ArgumentException("RSA private exponent must be positive.", nameof(privateExponent));
        }
        Modulus = modulus;
        PublicExponent = publicExponent;
        PrivateExponent = privateExponent;
    }

    /// <summary>The RSA modulus (n).</summary>
    public BigInteger Modulus { get; }

    /// <summary>The RSA public exponent (e), typically 65537.</summary>
    public BigInteger PublicExponent { get; }

    /// <summary>The RSA private exponent (d).</summary>
    public BigInteger PrivateExponent { get; }

    /// <summary>The size of the modulus in bytes (k = ⌈log256 n⌉).</summary>
    public int ModulusSizeBytes => (int)((Modulus.GetBitLength() + 7) / 8);

    /// <summary>The corresponding RSA public key.</summary>
    public RsaPublicKey PublicKey => new(Modulus, PublicExponent);

    /// <summary>
    /// Parses a PKCS#8 unencrypted PrivateKeyInfo (RFC 5208) carrying an
    /// RSAPrivateKey payload.
    /// </summary>
    /// <exception cref="Asn1Exception">If the bytes are not a valid PKCS#8 RSA key.</exception>
    public static RsaPrivateKey FromPkcs8(byte[] pkcs8Der)
    {
        ArgumentNullException.ThrowIfNull(pkcs8Der);
        Asn1Reader root = new(pkcs8Der);
        Asn1Reader pki = root.ReadSequence();

        BigInteger version = pki.ReadInteger();
        if (version != BigInteger.Zero)
        {
            throw new Asn1Exception($"Unsupported PKCS#8 PrivateKeyInfo version: {version}");
        }

        // privateKeyAlgorithm AlgorithmIdentifier
        Asn1Reader algSeq = pki.ReadSequence();
        ObjectIdentifier algOid = algSeq.ReadObjectIdentifier();
        if (!algOid.Equals(KnownOids.RsaEncryption))
        {
            throw new Asn1Exception(
                $"PKCS#8 key is not RSA (algorithm OID {algOid}, expected {KnownOids.RsaEncryption}).");
        }

        // privateKey OCTET STRING wrapping the RSAPrivateKey DER
        byte[] keyBytes = pki.ReadOctetString();
        return FromRsaPrivateKey(keyBytes);
    }

    /// <summary>
    /// Parses an RSAPrivateKey DER (RFC 8017 §3.2). Used internally by
    /// <see cref="FromPkcs8"/>; exposed for callers that have the raw key bytes
    /// without the PKCS#8 wrapper.
    /// </summary>
    public static RsaPrivateKey FromRsaPrivateKey(byte[] rsaPrivateKeyDer)
    {
        ArgumentNullException.ThrowIfNull(rsaPrivateKeyDer);
        Asn1Reader root = new(rsaPrivateKeyDer);
        Asn1Reader seq = root.ReadSequence();

        BigInteger version = seq.ReadInteger();
        if (version != BigInteger.Zero && version != BigInteger.One)
        {
            throw new Asn1Exception($"Unsupported RSAPrivateKey version: {version}");
        }

        BigInteger modulus = seq.ReadInteger();
        BigInteger publicExponent = seq.ReadInteger();
        BigInteger privateExponent = seq.ReadInteger();
        // Further fields (prime1, prime2, exponent1, exponent2, coefficient, otherPrimeInfos)
        // are present but not yet used. Skip the rest of the SEQUENCE.

        return new RsaPrivateKey(modulus, publicExponent, privateExponent);
    }
}
