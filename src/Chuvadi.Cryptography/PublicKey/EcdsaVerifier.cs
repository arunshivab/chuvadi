// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FIPS 186-4 §6.4 — ECDSA Signature Verification
//        RFC 3279 §2.2.3 — ECDSA signature value encoding (ASN.1 SEQUENCE)
// PHASE: Phase 1.1.4 — Public-key cryptography

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// Verifies ECDSA signatures per FIPS 186-4 §6.4.
/// </summary>
public static class EcdsaVerifier
{
    /// <summary>
    /// Verifies an ECDSA signature.
    /// </summary>
    /// <param name="publicKey">Signer's ECDSA public key.</param>
    /// <param name="messageHash">The message digest (any hash; e is truncated as required).</param>
    /// <param name="signature">DER-encoded SEQUENCE { INTEGER r, INTEGER s }.</param>
    /// <returns>True iff the signature is valid.</returns>
    public static bool Verify(
        EcdsaPublicKey publicKey,
        ReadOnlySpan<byte> messageHash,
        ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        BigInteger r, s;
        try
        {
            (r, s) = DecodeSignature(signature.ToArray());
        }
        catch (Asn1Exception)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        EcCurve curve = publicKey.Curve;
        BigInteger n = curve.N;

        // Step 1: r, s in [1, n-1]
        if (r < BigInteger.One || r >= n) { return false; }
        if (s < BigInteger.One || s >= n) { return false; }

        // Step 2: e = integer derived from leftmost bits of hash
        BigInteger e = HashToInteger(messageHash, n);

        // Step 3: w = s^-1 mod n
        BigInteger w;
        try
        {
            w = EcPoint.ModInverse(s, n);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        // Step 4-5: u1, u2
        BigInteger u1 = EcPoint.Mod(e * w, n);
        BigInteger u2 = EcPoint.Mod(r * w, n);

        // Step 6: R = u1*G + u2*Q
        EcPoint g = EcPoint.Generator(curve);
        EcPoint q = publicKey.PublicPoint;

        EcPoint r1 = g.Multiply(u1);
        EcPoint r2 = q.Multiply(u2);
        EcPoint rPoint = r1.Add(r2);

        // Step 7: R != infinity
        if (rPoint.IsInfinity) { return false; }

        // Step 8: r ?= R.x mod n
        BigInteger v = EcPoint.Mod(rPoint.X, n);
        return v == r;
    }

    /// <summary>
    /// Decodes an ECDSA signature from its ASN.1 SEQUENCE { INTEGER r, INTEGER s } form.
    /// </summary>
    public static (BigInteger R, BigInteger S) DecodeSignature(byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        Asn1Reader outer = new(signature);
        Asn1Reader seq = outer.ReadSequence();
        BigInteger r = seq.ReadInteger();
        BigInteger s = seq.ReadInteger();
        seq.ExpectEnd();
        outer.ExpectEnd();
        return (r, s);
    }

    /// <summary>
    /// Converts a message hash to an integer per FIPS 186-4 §6.4 step 5:
    /// if hashLen*8 &gt; bitlen(n), use the leftmost bitlen(n) bits;
    /// otherwise interpret all bits as a big-endian integer.
    /// </summary>
    private static BigInteger HashToInteger(ReadOnlySpan<byte> hash, BigInteger n)
    {
        int nBits = GetBitLength(n);
        int hashBits = hash.Length * 8;

        // Interpret hash as big-endian unsigned integer
        byte[] le = new byte[hash.Length + 1];
        for (int i = 0; i < hash.Length; i++)
        {
            le[i] = hash[hash.Length - 1 - i];
        }
        BigInteger e = new(le);

        // If hash is longer than n in bits, shift right
        if (hashBits > nBits)
        {
            e >>= (hashBits - nBits);
        }
        return e;
    }

    private static int GetBitLength(BigInteger value)
    {
        if (value.IsZero) { return 0; }
        byte[] le = value.ToByteArray();
        int significant = le.Length;
        while (significant > 0 && le[significant - 1] == 0) { significant--; }
        if (significant == 0) { return 0; }
        int bits = (significant - 1) * 8;
        byte top = le[significant - 1];
        while (top > 0) { bits++; top >>= 1; }
        return bits;
    }
}
