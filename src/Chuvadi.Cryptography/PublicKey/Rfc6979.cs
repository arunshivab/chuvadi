// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 6979 — Deterministic Usage of the DSA and ECDSA
// PHASE: Phase 1.2.6 — primitives batch

using System;
using System.Numerics;
using Chuvadi.Cryptography.Hashing;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// Deterministic ECDSA / DSA nonce generation per RFC 6979.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the per-signature random k with a value derived deterministically
/// from the private key and the message hash. The result is bit-for-bit
/// reproducible (useful for testing and audit), removes the dependency on a
/// strong RNG at sign time, and — crucially — eliminates the catastrophic
/// failure mode where a poor RNG leaks the private key (PlayStation 3 / Sony,
/// 2010).
/// </para>
/// <para>
/// The algorithm is described in RFC 6979 §3.2 and uses HMAC-DRBG with the
/// same hash function the caller is signing under.
/// </para>
/// </remarks>
public static class Rfc6979
{
    /// <summary>
    /// Derives a deterministic nonce in <c>[1, q-1]</c> for ECDSA / DSA.
    /// </summary>
    /// <param name="q">The group order.</param>
    /// <param name="privateKey">The integer private key.</param>
    /// <param name="messageHash">The hash of the message being signed.</param>
    /// <param name="hashAlgorithm">The hash algorithm; HMAC uses the same.</param>
    public static BigInteger DeriveNonce(
        BigInteger q,
        BigInteger privateKey,
        ReadOnlySpan<byte> messageHash,
        HashAlgorithmName hashAlgorithm)
    {
        if (q.Sign <= 0) { throw new ArgumentOutOfRangeException(nameof(q), "q must be positive."); }
        if (privateKey.Sign <= 0 || privateKey >= q)
        {
            throw new ArgumentOutOfRangeException(nameof(privateKey), "private key must lie in [1, q-1].");
        }

        int qlen = (int)q.GetBitLength();
        int rolen = (qlen + 7) / 8;

        // RFC 6979 §3.2 step a: h1 = H(m). Caller supplies messageHash.
        // bits2octets(h1): bits2int truncated mod q, then int2octets to rolen.
        BigInteger h1Int = Bits2Int(messageHash, qlen);
        BigInteger h1Mod = h1Int % q;
        if (h1Mod.Sign < 0) { h1Mod += q; }
        byte[] bx = Int2Octets(h1Mod, rolen);

        // int2octets(x) for the private key
        byte[] xOct = Int2Octets(privateKey, rolen);

        int hlen = HashFactory.Create(hashAlgorithm).DigestSize;

        // Step b: V = 0x01 0x01 ... 0x01 (hlen bytes)
        byte[] v = new byte[hlen];
        for (int i = 0; i < hlen; i++) { v[i] = 0x01; }

        // Step c: K = 0x00 0x00 ... 0x00 (hlen bytes)
        byte[] k = new byte[hlen];

        // Step d: K = HMAC_K(V || 0x00 || int2octets(x) || bits2octets(h1))
        k = Hmac.Compute(hashAlgorithm, k, Concat(v, new byte[] { 0x00 }, xOct, bx));

        // Step e: V = HMAC_K(V)
        v = Hmac.Compute(hashAlgorithm, k, v);

        // Step f: K = HMAC_K(V || 0x01 || int2octets(x) || bits2octets(h1))
        k = Hmac.Compute(hashAlgorithm, k, Concat(v, new byte[] { 0x01 }, xOct, bx));

        // Step g: V = HMAC_K(V)
        v = Hmac.Compute(hashAlgorithm, k, v);

        // Step h: loop until we find a valid k.
        while (true)
        {
            byte[] t = Array.Empty<byte>();
            while (t.Length < rolen)
            {
                v = Hmac.Compute(hashAlgorithm, k, v);
                t = Concat(t, v);
            }

            BigInteger candidate = Bits2Int(t, qlen);
            if (candidate.Sign > 0 && candidate < q)
            {
                return candidate;
            }

            // Failure: reseed and retry.
            k = Hmac.Compute(hashAlgorithm, k, Concat(v, new byte[] { 0x00 }));
            v = Hmac.Compute(hashAlgorithm, k, v);
        }
    }

    /// <summary>RFC 6979 §2.3.2: bits2int.</summary>
    private static BigInteger Bits2Int(ReadOnlySpan<byte> data, int qlen)
    {
        // Take the leftmost qlen bits of data as a big-endian integer.
        // If data is shorter than qlen bits, zero-extend on the right (in bit space).
        int blen = data.Length * 8;

        // Big-endian unsigned: prepend a 0x00 byte to force non-negative.
        byte[] sized = new byte[data.Length + 1];
        sized[0] = 0;
        data.CopyTo(sized.AsSpan(1));
        // BigInteger ctor uses little-endian; reverse.
        Array.Reverse(sized);
        BigInteger n = new(sized);

        if (blen > qlen)
        {
            n >>= (blen - qlen);
        }
        return n;
    }

    /// <summary>RFC 6979 §2.3.3: int2octets.</summary>
    private static byte[] Int2Octets(BigInteger x, int rolen)
    {
        byte[] raw = x.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length == rolen) { return raw; }
        if (raw.Length < rolen)
        {
            // Left-pad with zeros.
            byte[] padded = new byte[rolen];
            Buffer.BlockCopy(raw, 0, padded, rolen - raw.Length, raw.Length);
            return padded;
        }
        // raw.Length > rolen: take rightmost rolen bytes.
        byte[] trimmed = new byte[rolen];
        Buffer.BlockCopy(raw, raw.Length - rolen, trimmed, 0, rolen);
        return trimmed;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (byte[] p in parts) { total += p.Length; }
        byte[] result = new byte[total];
        int offset = 0;
        foreach (byte[] p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }
}
