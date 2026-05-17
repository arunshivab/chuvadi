// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 8017 §8.2 — RSASSA-PKCS1-v1_5 sign/verify
// PHASE: Phase 1.1.4 — RSA signing

using System;
using System.Numerics;
using Chuvadi.Cryptography.Hashing;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// Hand-rolled RSASSA-PKCS1-v1_5 signing per RFC 8017 §8.2.
/// </summary>
/// <remarks>
/// Implementation is the textbook RSASP1 primitive (modular exponentiation
/// with the private exponent) wrapped in EMSA-PKCS1-v1_5 encoding. CRT is
/// not yet applied; signing operates on the full (n, d) pair. CRT will be
/// added in a future session for performance, not correctness.
/// </remarks>
public static class RsaSigner
{
    /// <summary>
    /// Signs a pre-computed message digest using RSASSA-PKCS1-v1_5.
    /// </summary>
    /// <param name="privateKey">The RSA private key.</param>
    /// <param name="hashAlgorithm">The hash algorithm that produced the digest.</param>
    /// <param name="messageHash">The message digest to sign.</param>
    /// <returns>The signature bytes, length = modulus size.</returns>
    public static byte[] SignPkcs1v15(
        RsaPrivateKey privateKey,
        HashAlgorithmName hashAlgorithm,
        ReadOnlySpan<byte> messageHash)
    {
        ArgumentNullException.ThrowIfNull(privateKey);

        int k = privateKey.ModulusSizeBytes;

        // Step 1: EMSA-PKCS1-v1_5 encoding
        byte[] em = Pkcs1V15Encoding.BuildEncodedMessage(hashAlgorithm, messageHash, k);

        // Step 2: OS2IP — octet string to integer (big-endian, unsigned)
        BigInteger m = OsToInteger(em);

        // Step 3: RSASP1 — m^d mod n
        BigInteger s = BigInteger.ModPow(m, privateKey.PrivateExponent, privateKey.Modulus);

        // Step 4: I2OSP — integer back to k octets, big-endian, left-padded with zeros
        return IntegerToOctets(s, k);
    }

    /// <summary>
    /// Hashes <paramref name="message"/> with <paramref name="hashAlgorithm"/>
    /// then signs the digest via <see cref="SignPkcs1v15(RsaPrivateKey, HashAlgorithmName, ReadOnlySpan{byte})"/>.
    /// </summary>
    public static byte[] SignPkcs1v15(
        RsaPrivateKey privateKey,
        HashAlgorithmName hashAlgorithm,
        ReadOnlySpan<byte> message,
        bool hashIsAlreadyDigest)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (hashIsAlreadyDigest)
        {
            return SignPkcs1v15(privateKey, hashAlgorithm, message);
        }
        IHashAlgorithm h = HashFactory.Create(hashAlgorithm);
        h.Update(message);
        byte[] digest = new byte[h.DigestSize];
        h.Finish(digest);
        return SignPkcs1v15(privateKey, hashAlgorithm, digest);
    }

    /// <summary>OS2IP — octet string to non-negative integer (big-endian).</summary>
    private static BigInteger OsToInteger(byte[] octets)
    {
        // BigInteger ctor takes little-endian by default; we need big-endian unsigned.
        byte[] le = new byte[octets.Length + 1];
        for (int i = 0; i < octets.Length; i++)
        {
            le[i] = octets[octets.Length - 1 - i];
        }
        // Final 0 byte forces non-negative interpretation.
        le[octets.Length] = 0;
        return new BigInteger(le);
    }

    /// <summary>I2OSP — non-negative integer to fixed-length octet string (big-endian).</summary>
    private static byte[] IntegerToOctets(BigInteger value, int length)
    {
        if (value.Sign < 0)
        {
            throw new ArgumentException("I2OSP value must be non-negative.", nameof(value));
        }
        byte[] le = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (le.Length > length)
        {
            throw new ArgumentException(
                $"Integer is too large to fit in {length} octets.", nameof(length));
        }
        byte[] result = new byte[length];
        for (int i = 0; i < le.Length; i++)
        {
            result[length - 1 - i] = le[i];
        }
        return result;
    }
}
