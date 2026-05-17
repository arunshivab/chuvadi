// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5915 (ECPrivateKey); RFC 5208 (PKCS#8 PrivateKeyInfo)
// PHASE: Phase 1.2.2 — ECDSA signing

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// An ECDSA private key — a scalar d in [1, n-1] on a fixed curve.
/// </summary>
/// <remarks>
/// <para>
/// Curves supported: NIST P-256, P-384, P-521. Loaded from PKCS#8
/// unencrypted PrivateKeyInfo (RFC 5208) via <see cref="FromPkcs8"/>, or
/// from the bare RFC 5915 <c>ECPrivateKey</c> DER via
/// <see cref="FromEcPrivateKey"/>.
/// </para>
/// </remarks>
public sealed class EcdsaPrivateKey
{
    /// <summary>Initialises a new ECDSA private key.</summary>
    /// <param name="curve">The curve the scalar lives on.</param>
    /// <param name="d">The private scalar; must be in [1, n-1].</param>
    public EcdsaPrivateKey(EcCurve curve, BigInteger d)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (d.Sign <= 0 || d >= curve.N)
        {
            throw new ArgumentException(
                "ECDSA private scalar must be in [1, n-1].", nameof(d));
        }
        Curve = curve;
        D = d;
    }

    /// <summary>The elliptic curve.</summary>
    public EcCurve Curve { get; }

    /// <summary>The private scalar d.</summary>
    public BigInteger D { get; }

    /// <summary>The corresponding ECDSA public key (Q = d·G).</summary>
    public EcdsaPublicKey PublicKey
    {
        get
        {
            EcPoint q = EcPoint.Generator(Curve).Multiply(D);
            return new EcdsaPublicKey(q);
        }
    }

    /// <summary>
    /// Parses a PKCS#8 unencrypted PrivateKeyInfo (RFC 5208) carrying an
    /// ECPrivateKey payload. The curve is identified by the
    /// <c>privateKeyAlgorithm.parameters</c> field.
    /// </summary>
    public static EcdsaPrivateKey FromPkcs8(byte[] pkcs8Der)
    {
        ArgumentNullException.ThrowIfNull(pkcs8Der);
        Asn1Reader root = new(pkcs8Der);
        Asn1Reader pki = root.ReadSequence();

        BigInteger version = pki.ReadInteger();
        if (version != BigInteger.Zero)
        {
            throw new Asn1Exception($"Unsupported PKCS#8 PrivateKeyInfo version: {version}");
        }

        // privateKeyAlgorithm: SEQUENCE { OID id-ecPublicKey, OID curve }
        Asn1Reader algSeq = pki.ReadSequence();
        ObjectIdentifier algOid = algSeq.ReadObjectIdentifier();
        if (!algOid.Equals(KnownOids.EcPublicKey))
        {
            throw new Asn1Exception(
                $"PKCS#8 key is not ECDSA (algorithm OID {algOid}, expected {KnownOids.EcPublicKey}).");
        }
        ObjectIdentifier curveOid = algSeq.ReadObjectIdentifier();
        EcCurve curve = EcCurve.FromOid(curveOid);

        // privateKey: OCTET STRING wrapping ECPrivateKey
        byte[] ecPrivBytes = pki.ReadOctetString();
        return FromEcPrivateKey(ecPrivBytes, curve);
    }

    /// <summary>
    /// Parses an RFC 5915 <c>ECPrivateKey</c> DER. The curve must be supplied
    /// because the structure's curve <c>parameters</c> field is optional.
    /// </summary>
    public static EcdsaPrivateKey FromEcPrivateKey(byte[] ecPrivateKeyDer, EcCurve curve)
    {
        ArgumentNullException.ThrowIfNull(ecPrivateKeyDer);
        ArgumentNullException.ThrowIfNull(curve);
        Asn1Reader root = new(ecPrivateKeyDer);
        Asn1Reader seq = root.ReadSequence();

        BigInteger version = seq.ReadInteger();
        if (version != BigInteger.One)
        {
            throw new Asn1Exception($"Unsupported ECPrivateKey version: {version}");
        }

        // privateKey: OCTET STRING — the d scalar, big-endian unsigned, fixed width.
        byte[] dBytes = seq.ReadOctetString();
        BigInteger d = new(dBytes, isUnsigned: true, isBigEndian: true);

        // The remaining optional fields ([0] curve parameters, [1] public key) are
        // ignored — the caller supplied the curve and we derive Q on demand.

        return new EcdsaPrivateKey(curve, d);
    }
}
