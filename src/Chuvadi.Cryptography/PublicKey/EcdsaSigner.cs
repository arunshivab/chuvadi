// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FIPS 186-4 §6.4 — ECDSA Signature Generation Algorithm
// PHASE: Phase 1.2.2 — ECDSA signing

using System;
using System.Numerics;
using System.Security.Cryptography;
using Chuvadi.Cryptography.Hashing;
using ChuvadiHashAlgorithmName = Chuvadi.Cryptography.Hashing.HashAlgorithmName;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// Hand-rolled ECDSA signing per FIPS 186-4 §6.4.
/// </summary>
/// <remarks>
/// <para>
/// Implementation is the textbook ECDSA primitive: hash the message,
/// truncate to <c>n</c>'s bit length, generate a random nonce k via
/// <see cref="RandomNumberGenerator"/>, compute (r, s), and encode as a
/// DER SEQUENCE of two INTEGERs per RFC 3279 §2.2.3.
/// </para>
/// <para>
/// Nonces are sampled from <see cref="RandomNumberGenerator"/> with
/// rejection sampling; RFC 6979 deterministic nonces are not yet
/// implemented (a future-session improvement, perf-neutral but more
/// reproducible).
/// </para>
/// </remarks>
public static class EcdsaSigner
{
    /// <summary>
    /// Signs a pre-computed message digest using ECDSA.
    /// </summary>
    /// <param name="privateKey">The ECDSA private key.</param>
    /// <param name="messageHash">The message digest.</param>
    /// <returns>The DER-encoded <c>(r, s)</c> signature.</returns>
    public static byte[] Sign(
        EcdsaPrivateKey privateKey,
        ReadOnlySpan<byte> messageHash)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        EcCurve curve = privateKey.Curve;
        BigInteger n = curve.N;
        BigInteger d = privateKey.D;

        // Step 1: convert hash to integer e, truncated to bit-length(n).
        BigInteger e = HashToInteger(messageHash, n);

        // Step 2-5: pick k, compute r, s. Loop until both are non-zero.
        for (int attempt = 0; attempt < 16; attempt++)
        {
            BigInteger k = GenerateRandomScalar(n);

            // (x1, y1) = k * G
            EcPoint kG = EcPoint.Generator(curve).Multiply(k);
            if (kG.IsInfinity) { continue; }

            BigInteger r = kG.X % n;
            if (r.Sign == 0) { continue; }

            // s = k^-1 * (e + r*d) mod n
            BigInteger kInv = EcPoint.ModInverse(k, n);
            BigInteger s = (kInv * (e + (r * d))) % n;
            if (s.Sign < 0) { s += n; }
            if (s.Sign == 0) { continue; }

            return EncodeSignature(r, s);
        }

        throw new InvalidOperationException(
            "ECDSA signing failed: could not produce a valid (r, s) pair after 16 attempts.");
    }

    /// <summary>
    /// Hashes <paramref name="message"/> with <paramref name="hashAlgorithm"/>
    /// then signs the digest.
    /// </summary>
    public static byte[] Sign(
        EcdsaPrivateKey privateKey,
        ChuvadiHashAlgorithmName hashAlgorithm,
        ReadOnlySpan<byte> message,
        bool hashIsAlreadyDigest)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (hashIsAlreadyDigest)
        {
            return Sign(privateKey, message);
        }
        IHashAlgorithm h = HashFactory.Create(hashAlgorithm);
        h.Update(message);
        byte[] digest = new byte[h.DigestSize];
        h.Finish(digest);
        return Sign(privateKey, digest);
    }

    /// <summary>
    /// Converts a message hash into the integer <c>e</c> used by ECDSA.
    /// Per FIPS 186-4 §6.4: if the hash is longer than the bit-length of
    /// <c>n</c>, take the leftmost bit-length(n) bits.
    /// </summary>
    /// <summary>
    /// Signs a hash deterministically per RFC 6979 instead of using random
    /// nonces. Bit-for-bit reproducible for the same key + message + hash
    /// algorithm; immune to RNG failures.
    /// </summary>
    /// <param name="privateKey">The signing key.</param>
    /// <param name="messageHash">The hash of the message being signed.</param>
    /// <param name="hashAlgorithm">The hash used for HMAC-DRBG inside RFC 6979.</param>
    /// <returns>The DER-encoded <c>(r, s)</c> signature.</returns>
    public static byte[] SignDeterministic(
        EcdsaPrivateKey privateKey,
        ReadOnlySpan<byte> messageHash,
        ChuvadiHashAlgorithmName hashAlgorithm)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        EcCurve curve = privateKey.Curve;
        BigInteger n = curve.N;
        BigInteger d = privateKey.D;

        BigInteger e = HashToInteger(messageHash, n);

        // RFC 6979 §3.2 retry loop: if r or s is zero, regenerate k by
        // continuing the HMAC chain. In practice the first try always
        // succeeds for the curves Chuvadi supports, but we honor the spec.
        BigInteger seedK = Rfc6979.DeriveNonce(n, d, messageHash, hashAlgorithm);
        BigInteger k = seedK;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            EcPoint kG = EcPoint.Generator(curve).Multiply(k);
            if (!kG.IsInfinity)
            {
                BigInteger r = kG.X % n;
                if (r.Sign > 0)
                {
                    BigInteger kInv = EcPoint.ModInverse(k, n);
                    BigInteger s = (kInv * (e + (r * d))) % n;
                    if (s.Sign < 0) { s += n; }
                    if (s.Sign > 0)
                    {
                        return EncodeSignature(r, s);
                    }
                }
            }

            // r or s was zero — extremely improbable. Continue the chain.
            // RFC 6979 §3.2 step h2: "k = ... try again". We re-derive with
            // a permuted seed to keep determinism: hash the prior k.
            byte[] reseed = k.ToByteArray(isUnsigned: true, isBigEndian: true);
            k = Rfc6979.DeriveNonce(n, d, reseed, hashAlgorithm);
        }

        throw new InvalidOperationException(
            "ECDSA deterministic signing failed: 16 attempts produced no valid (r, s).");
    }

    private static BigInteger HashToInteger(ReadOnlySpan<byte> hash, BigInteger n)
    {
        int nBits = (int)n.GetBitLength();
        BigInteger e = new(hash, isUnsigned: true, isBigEndian: true);
        int hashBits = hash.Length * 8;
        if (hashBits > nBits)
        {
            e >>= hashBits - nBits;
        }
        return e;
    }

    /// <summary>
    /// Generates a uniformly random scalar in [1, n-1] via rejection sampling.
    /// </summary>
    private static BigInteger GenerateRandomScalar(BigInteger n)
    {
        int nBits = (int)n.GetBitLength();
        int nBytes = (nBits + 7) / 8;
        // Mask off any high bits above nBits in the top byte so we don't
        // waste rejection rounds on values trivially larger than n.
        int topByteMask = nBits % 8 == 0 ? 0xFF : (1 << (nBits % 8)) - 1;
        byte[] buffer = new byte[nBytes];

        for (int attempt = 0; attempt < 256; attempt++)
        {
            RandomNumberGenerator.Fill(buffer);
            buffer[0] &= (byte)topByteMask;
            BigInteger k = new(buffer, isUnsigned: true, isBigEndian: true);
            if (k.Sign > 0 && k < n)
            {
                return k;
            }
        }
        throw new InvalidOperationException(
            "Failed to generate a random scalar in [1, n-1] after 256 attempts.");
    }

    /// <summary>
    /// DER-encodes an ECDSA signature as <c>SEQUENCE { r INTEGER, s INTEGER }</c>
    /// per RFC 3279 §2.2.3.
    /// </summary>
    private static byte[] EncodeSignature(BigInteger r, BigInteger s)
    {
        byte[] rBytes = EncodeInteger(r);
        byte[] sBytes = EncodeInteger(s);

        int contentLen = rBytes.Length + sBytes.Length;
        // SEQUENCE tag + length + content
        byte[] lengthBytes = EncodeLength(contentLen);
        byte[] result = new byte[1 + lengthBytes.Length + contentLen];
        int pos = 0;
        result[pos++] = 0x30;  // SEQUENCE
        Buffer.BlockCopy(lengthBytes, 0, result, pos, lengthBytes.Length);
        pos += lengthBytes.Length;
        Buffer.BlockCopy(rBytes, 0, result, pos, rBytes.Length);
        pos += rBytes.Length;
        Buffer.BlockCopy(sBytes, 0, result, pos, sBytes.Length);
        return result;
    }

    /// <summary>
    /// Encodes a non-negative BigInteger as a DER INTEGER (tag 0x02 + length + content).
    /// Big-endian, unsigned, with a leading 0x00 byte when the high bit of the magnitude is set.
    /// </summary>
    private static byte[] EncodeInteger(BigInteger value)
    {
        if (value.Sign < 0)
        {
            throw new ArgumentException("DER INTEGER encoding here expects non-negative values.", nameof(value));
        }
        byte[] mag = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        // If high bit set, prepend a 0x00 byte so DER reads it as positive.
        bool prependZero = mag.Length > 0 && (mag[0] & 0x80) != 0;
        // BigInteger.ToByteArray for zero returns a single 0x00 byte already.
        if (mag.Length == 0) { mag = new byte[] { 0x00 }; }

        int contentLen = mag.Length + (prependZero ? 1 : 0);
        byte[] lengthBytes = EncodeLength(contentLen);
        byte[] result = new byte[1 + lengthBytes.Length + contentLen];
        int pos = 0;
        result[pos++] = 0x02;  // INTEGER
        Buffer.BlockCopy(lengthBytes, 0, result, pos, lengthBytes.Length);
        pos += lengthBytes.Length;
        if (prependZero) { result[pos++] = 0x00; }
        Buffer.BlockCopy(mag, 0, result, pos, mag.Length);
        return result;
    }

    private static byte[] EncodeLength(int length)
    {
        if (length < 128) { return new byte[] { (byte)length }; }
        if (length < 256) { return new byte[] { 0x81, (byte)length }; }
        if (length < 65536) { return new byte[] { 0x82, (byte)(length >> 8), (byte)length }; }
        throw new ArgumentOutOfRangeException(nameof(length),
            "Lengths beyond 65535 not supported in this DER encoder.");
    }
}
