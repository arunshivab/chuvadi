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

    /// <summary>
    /// Signs <paramref name="messageHash"/> with RSASSA-PSS per RFC 8017 §8.1.
    /// </summary>
    /// <param name="privateKey">The signing key.</param>
    /// <param name="hashAlgorithm">Hash used to derive <paramref name="messageHash"/>.</param>
    /// <param name="mgfHashAlgorithm">Hash used for MGF1 (commonly equal to <paramref name="hashAlgorithm"/>).</param>
    /// <param name="saltLength">Salt length in bytes (commonly the hash output size).</param>
    /// <param name="messageHash">The message digest.</param>
    public static byte[] SignPss(
        RsaPrivateKey privateKey,
        HashAlgorithmName hashAlgorithm,
        HashAlgorithmName mgfHashAlgorithm,
        int saltLength,
        ReadOnlySpan<byte> messageHash)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (saltLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(saltLength), "saltLength must be non-negative.");
        }

        int modBits = (int)privateKey.Modulus.GetBitLength();
        int emBits = modBits - 1;
        int emLen = (emBits + 7) / 8;
        int hLen = HashFactory.Create(hashAlgorithm).DigestSize;

        if (messageHash.Length != hLen)
        {
            throw new ArgumentException(
                $"messageHash length {messageHash.Length} does not match {hashAlgorithm} digest size {hLen}.",
                nameof(messageHash));
        }
        if (emLen < hLen + saltLength + 2)
        {
            throw new InvalidOperationException(
                "RSA modulus is too small for the chosen hash and salt length.");
        }

        // 1. Generate a fresh salt.
        byte[] salt = new byte[saltLength];
        if (saltLength > 0)
        {
            using System.Security.Cryptography.RandomNumberGenerator rng =
                System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(salt);
        }

        // 2. M' = 0x00 0x00 0x00 0x00 0x00 0x00 0x00 0x00 || mHash || salt
        byte[] mPrime = new byte[8 + hLen + saltLength];
        messageHash.CopyTo(mPrime.AsSpan(8, hLen));
        Buffer.BlockCopy(salt, 0, mPrime, 8 + hLen, saltLength);

        // 3. H = Hash(M')
        IHashAlgorithm hasher = HashFactory.Create(hashAlgorithm);
        hasher.Update(mPrime);
        byte[] h = new byte[hLen];
        hasher.Finish(h);

        // 4. PS = octet string of (emLen - sLen - hLen - 2) zero bytes
        // 5. DB = PS || 0x01 || salt
        int dbLen = emLen - hLen - 1;
        byte[] db = new byte[dbLen];
        db[dbLen - saltLength - 1] = 0x01;
        Buffer.BlockCopy(salt, 0, db, dbLen - saltLength, saltLength);

        // 6. dbMask = MGF1(H, dbLen)
        byte[] dbMask = Mgf1(h, dbLen, mgfHashAlgorithm);

        // 7. maskedDB = DB XOR dbMask
        for (int i = 0; i < dbLen; i++) { db[i] ^= dbMask[i]; }

        // 8. Set leftmost (8*emLen - emBits) bits of maskedDB to zero.
        int zeroBits = (8 * emLen) - emBits;
        if (zeroBits > 0)
        {
            byte mask = (byte)(0xFF >> zeroBits);
            db[0] &= mask;
        }

        // 9. EM = maskedDB || H || 0xBC
        byte[] em = new byte[emLen];
        Buffer.BlockCopy(db, 0, em, 0, dbLen);
        Buffer.BlockCopy(h, 0, em, dbLen, hLen);
        em[emLen - 1] = 0xBC;

        // If modBits-1 is not a multiple of 8, EM is one byte shorter than k.
        // We zero-pad on the left up to k bytes for RSA.
        int k = privateKey.ModulusSizeBytes;
        byte[] emFull;
        if (em.Length == k)
        {
            emFull = em;
        }
        else
        {
            emFull = new byte[k];
            Buffer.BlockCopy(em, 0, emFull, k - em.Length, em.Length);
        }

        // 10. RSASP1 — m^d mod n
        BigInteger mInt = OsToInteger(emFull);
        BigInteger sInt = BigInteger.ModPow(mInt, privateKey.PrivateExponent, privateKey.Modulus);
        return IntegerToOctets(sInt, k);
    }

    /// <summary>MGF1 mask generation function per RFC 8017 §B.2.1.</summary>
    private static byte[] Mgf1(ReadOnlySpan<byte> seed, int maskLen, HashAlgorithmName hashAlgorithm)
    {
        int hLen = HashFactory.Create(hashAlgorithm).DigestSize;
        byte[] mask = new byte[maskLen];
        int written = 0;
        uint counter = 0;
        while (written < maskLen)
        {
            byte[] c = new byte[4];
            c[0] = (byte)(counter >> 24);
            c[1] = (byte)(counter >> 16);
            c[2] = (byte)(counter >> 8);
            c[3] = (byte)counter;

            IHashAlgorithm h = HashFactory.Create(hashAlgorithm);
            h.Update(seed);
            h.Update(c);
            byte[] block = new byte[hLen];
            h.Finish(block);
            int take = Math.Min(hLen, maskLen - written);
            Buffer.BlockCopy(block, 0, mask, written, take);
            written += take;
            counter++;
        }
        return mask;
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
