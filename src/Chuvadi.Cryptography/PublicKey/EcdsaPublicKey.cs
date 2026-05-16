// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5480 §2.2 — ECC SubjectPublicKey encoding
//        SEC 1 v2.0 §2.3.3 — Elliptic-curve point octet-string conversion
// PHASE: Phase 1.1.4 — Public-key cryptography

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// An ECDSA public key — a point on a named curve.
/// </summary>
public sealed class EcdsaPublicKey : IPublicKey
{
    /// <summary>Initialises a new EcdsaPublicKey from a curve and a public point.</summary>
    public EcdsaPublicKey(EcPoint publicPoint)
    {
        ArgumentNullException.ThrowIfNull(publicPoint);
        if (publicPoint.IsInfinity)
        {
            throw new ArgumentException("ECDSA public point must not be the point at infinity.",
                nameof(publicPoint));
        }
        PublicPoint = publicPoint;
    }

    /// <inheritdoc/>
    public PublicKeyAlgorithm Algorithm => PublicKeyAlgorithm.Ecdsa;

    /// <summary>The public-key point on the curve.</summary>
    public EcPoint PublicPoint { get; }

    /// <summary>The curve this key is defined over.</summary>
    public EcCurve Curve => PublicPoint.Curve;

    /// <summary>
    /// Parses a SEC 1 §2.3.3 ECPoint octet string.
    /// </summary>
    /// <remarks>
    /// Recognises the uncompressed form <c>04 || X || Y</c> where X and Y are
    /// each <c>FieldSizeBytes</c> big-endian unsigned integers. Compressed
    /// (<c>02</c>/<c>03</c>) and hybrid (<c>06</c>/<c>07</c>) forms are
    /// intentionally not supported — they require a square-root computation in
    /// the field that's rarely seen for ECDSA signatures in PDF context.
    /// </remarks>
    public static EcdsaPublicKey FromUncompressedPoint(EcCurve curve, byte[] ecPoint)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(ecPoint);

        int expected = 1 + (2 * curve.FieldSizeBytes);
        if (ecPoint.Length != expected || ecPoint[0] != 0x04)
        {
            throw new ArgumentException(
                $"Expected uncompressed ECPoint (0x04 || X || Y) of {expected} bytes; got {ecPoint.Length} bytes starting with 0x{ecPoint[0]:X2}.",
                nameof(ecPoint));
        }

        BigInteger x = ReadUnsignedBigEndian(ecPoint, 1, curve.FieldSizeBytes);
        BigInteger y = ReadUnsignedBigEndian(ecPoint, 1 + curve.FieldSizeBytes, curve.FieldSizeBytes);
        return new EcdsaPublicKey(EcPoint.Create(curve, x, y));
    }

    /// <summary>
    /// Parses an ECDSA public key from an X.509 SubjectPublicKeyInfo.
    /// </summary>
    public static EcdsaPublicKey FromSubjectPublicKeyInfo(SubjectPublicKeyInfo spki)
    {
        ArgumentNullException.ThrowIfNull(spki);
        if (!spki.Algorithm.Algorithm.Equals(KnownOids.EcPublicKey))
        {
            throw new ArgumentException(
                $"SubjectPublicKeyInfo algorithm is {spki.Algorithm.Algorithm}, expected id-ecPublicKey.",
                nameof(spki));
        }
        if (spki.Algorithm.ParametersAreAbsent || spki.Algorithm.ParametersAreNull)
        {
            throw new ArgumentException(
                "EC SubjectPublicKeyInfo requires namedCurve parameters.", nameof(spki));
        }
        if (spki.SubjectPublicKey.UnusedBitsInFinalOctet != 0)
        {
            throw new ArgumentException(
                "EC SubjectPublicKey BIT STRING must have zero unused bits.", nameof(spki));
        }

        // Parameters is an ANY field per RFC 5480; for namedCurve it's a bare OID
        Asn1Reader r = new(spki.Algorithm.Parameters);
        ObjectIdentifier curveOid = r.ReadObjectIdentifier();
        r.ExpectEnd();
        EcCurve curve = EcCurve.FromOid(curveOid);

        return FromUncompressedPoint(curve, spki.SubjectPublicKey.Bytes);
    }

    private static BigInteger ReadUnsignedBigEndian(byte[] bytes, int offset, int length)
    {
        // Reverse into a little-endian array with a trailing zero to force non-negative.
        byte[] le = new byte[length + 1];
        for (int i = 0; i < length; i++)
        {
            le[i] = bytes[offset + length - 1 - i];
        }
        return new BigInteger(le);
    }
}
