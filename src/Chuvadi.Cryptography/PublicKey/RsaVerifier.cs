// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 8017 — PKCS #1 v2.2: RSA Cryptography Specifications
//          §5.2.2  — RSAVP1 (verification primitive)
//          §8.2.2  — RSASSA-PKCS1-v1_5 verification
//          §9.2    — EMSA-PKCS1-v1_5 encoding (used by 8.2.2)
//          §8.1.2  — RSASSA-PSS verification
//          §9.1.2  — EMSA-PSS verification
//          §B.2.1  — MGF1 mask generation function
// PHASE: Phase 1.1.4 — Public-key cryptography

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// Verifies RSA signatures in PKCS#1 v1.5 (RSASSA-PKCS1-v1_5) and PSS
/// (RSASSA-PSS) formats per RFC 8017.
/// </summary>
public static class RsaVerifier
{
    // ── Public verification entry points ─────────────────────────────────

    /// <summary>
    /// Verifies a PKCS#1 v1.5 signature.
    /// </summary>
    /// <param name="publicKey">The signer's RSA public key.</param>
    /// <param name="hashAlgorithm">The hash algorithm used to digest the message.</param>
    /// <param name="messageHash">The message digest (output of <paramref name="hashAlgorithm"/>).</param>
    /// <param name="signature">The signature bytes (length must equal modulus size).</param>
    /// <returns>True when the signature is valid; false otherwise.</returns>
    public static bool VerifyPkcs1v15(
        RsaPublicKey publicKey,
        HashAlgorithmName hashAlgorithm,
        ReadOnlySpan<byte> messageHash,
        ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        int k = publicKey.ModulusSizeBytes;
        if (signature.Length != k)
        {
            return false;
        }

        // RFC 8017 §8.2.2 step 1: signature representative
        byte[]? em = RsaVerificationPrimitive(publicKey, signature);
        if (em is null) { return false; }

        // RFC 8017 §9.2: build the expected EM
        byte[] expected = BuildPkcs1v15Em(hashAlgorithm, messageHash, k);

        // Constant-time comparison
        return ConstantTimeEquals(em, expected);
    }

    /// <summary>
    /// Verifies an RSASSA-PSS signature.
    /// </summary>
    /// <param name="publicKey">The signer's RSA public key.</param>
    /// <param name="hashAlgorithm">The hash algorithm used to digest the message.</param>
    /// <param name="mgfHashAlgorithm">The hash algorithm used inside MGF1 (typically same as <paramref name="hashAlgorithm"/>).</param>
    /// <param name="saltLength">The PSS salt length in bytes.</param>
    /// <param name="messageHash">The message digest.</param>
    /// <param name="signature">The signature bytes.</param>
    public static bool VerifyPss(
        RsaPublicKey publicKey,
        HashAlgorithmName hashAlgorithm,
        HashAlgorithmName mgfHashAlgorithm,
        int saltLength,
        ReadOnlySpan<byte> messageHash,
        ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (saltLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(saltLength), "saltLength must be non-negative.");
        }

        int modBits = GetModulusBitLength(publicKey.Modulus);
        int emBits = modBits - 1;
        int emLen = (emBits + 7) / 8;

        int k = publicKey.ModulusSizeBytes;
        if (signature.Length != k) { return false; }

        byte[]? em = RsaVerificationPrimitive(publicKey, signature);
        if (em is null) { return false; }

        // RFC 8017 §9.1.2 EMSA-PSS-VERIFY
        // 1. mHash length check (caller already ensured)
        // 2. emLen check
        int mLen = messageHash.Length;
        int hLen = HashFactory.Create(hashAlgorithm).DigestSize;
        if (mLen != hLen) { return false; }
        if (emLen < hLen + saltLength + 2) { return false; }

        // 3. last byte must be 0xBC
        // Note: em may have leading byte from k-byte representation; PSS works on emLen bytes
        // where the leading bits zero-out to fit emBits. Extract the trailing emLen bytes.
        if (em.Length < emLen) { return false; }
        ReadOnlySpan<byte> emPss = em.AsSpan(em.Length - emLen, emLen);
        if (emPss[emPss.Length - 1] != 0xBC) { return false; }

        // 4. split: maskedDB || H || 0xBC
        int dbLen = emLen - hLen - 1;
        ReadOnlySpan<byte> maskedDb = emPss.Slice(0, dbLen);
        ReadOnlySpan<byte> hSpan = emPss.Slice(dbLen, hLen);

        // 5. check leftmost 8*emLen - emBits bits of maskedDb are zero
        int zeroBits = (8 * emLen) - emBits;
        if (zeroBits > 0)
        {
            byte mask = (byte)(0xFF >> zeroBits);
            byte topByte = (byte)~mask;
            if ((maskedDb[0] & topByte) != 0) { return false; }
        }

        // 6. dbMask = MGF1(H, dbLen)
        byte[] dbMask = Mgf1(hSpan, dbLen, mgfHashAlgorithm);

        // 7. DB = maskedDB XOR dbMask
        byte[] db = new byte[dbLen];
        for (int i = 0; i < dbLen; i++)
        {
            db[i] = (byte)(maskedDb[i] ^ dbMask[i]);
        }

        // 8. Clear the leftmost 8*emLen - emBits bits
        if (zeroBits > 0)
        {
            db[0] &= (byte)(0xFF >> zeroBits);
        }

        // 9. DB must be PS (all 0x00) || 0x01 || salt
        int psEnd = dbLen - saltLength - 1;
        for (int i = 0; i < psEnd; i++)
        {
            if (db[i] != 0x00) { return false; }
        }
        if (db[psEnd] != 0x01) { return false; }

        // 10. salt = last saltLength bytes of DB
        ReadOnlySpan<byte> salt = db.AsSpan(dbLen - saltLength, saltLength);

        // 11. M' = (0x00)8 || mHash || salt
        // 12. H' = Hash(M')
        IHashAlgorithm h = HashFactory.Create(hashAlgorithm);
        Span<byte> zero8 = stackalloc byte[8];
        h.Update(zero8);
        h.Update(messageHash);
        h.Update(salt);
        byte[] hPrime = new byte[hLen];
        h.Finish(hPrime);

        // 13. H == H'
        return ConstantTimeEquals(hSpan.ToArray(), hPrime);
    }

    // ── Internal helpers ─────────────────────────────────────────────────

    /// <summary>
    /// RSAVP1 — RFC 8017 §5.2.2. Computes s^e mod n. Returns the encoded
    /// message representative EM as k bytes (left-padded with zeros).
    /// Returns null when s >= n (a signature forgery attempt).
    /// </summary>
    private static byte[]? RsaVerificationPrimitive(RsaPublicKey publicKey, ReadOnlySpan<byte> signature)
    {
        // s = OS2IP(signature) (big-endian unsigned)
        BigInteger s = OS2IP(signature);
        if (s >= publicKey.Modulus)
        {
            // §5.2.2 step 1: signature representative out of range
            return null;
        }

        BigInteger m = BigInteger.ModPow(s, publicKey.PublicExponent, publicKey.Modulus);
        return I2OSP(m, publicKey.ModulusSizeBytes);
    }

    /// <summary>RFC 8017 §4.1 — Integer-to-Octet-String Primitive.</summary>
    private static byte[] I2OSP(BigInteger value, int length)
    {
        if (value.Sign < 0)
        {
            throw new ArgumentException("I2OSP value must be non-negative.", nameof(value));
        }
        // BigInteger.ToByteArray returns little-endian, possibly with leading sign byte.
        byte[] le = value.ToByteArray();
        // Strip trailing 0x00 (sign byte) if present.
        int significant = le.Length;
        while (significant > 0 && le[significant - 1] == 0)
        {
            significant--;
        }
        if (significant > length)
        {
            throw new ArgumentException("Integer too large for requested octet string length.");
        }
        byte[] result = new byte[length];
        // Place big-endian into the right of result
        for (int i = 0; i < significant; i++)
        {
            result[length - 1 - i] = le[i];
        }
        return result;
    }

    /// <summary>RFC 8017 §4.2 — Octet-String-to-Integer Primitive.</summary>
    private static BigInteger OS2IP(ReadOnlySpan<byte> bytes)
    {
        // Big-endian unsigned. BigInteger constructor reads little-endian signed.
        byte[] le = new byte[bytes.Length + 1];
        for (int i = 0; i < bytes.Length; i++)
        {
            le[i] = bytes[bytes.Length - 1 - i];
        }
        // le[bytes.Length] = 0 — forces non-negative interpretation
        return new BigInteger(le);
    }

    /// <summary>
    /// RFC 8017 §9.2 — builds the expected EMSA-PKCS1-v1_5 encoding of <paramref name="hash"/>.
    /// </summary>
    /// <remarks>
    /// EM = 0x00 || 0x01 || PS || 0x00 || T
    /// where T = DigestInfo encoded as:
    ///   SEQUENCE { SEQUENCE { OID hashAlgorithm, NULL }, OCTET STRING hash }
    /// </remarks>
    private static byte[] BuildPkcs1v15Em(HashAlgorithmName hashAlgorithm,
        ReadOnlySpan<byte> hash, int k)
    {
        ObjectIdentifier hashOid = hashAlgorithm switch
        {
            HashAlgorithmName.Sha256 => KnownOids.Sha256,
            HashAlgorithmName.Sha384 => KnownOids.Sha384,
            HashAlgorithmName.Sha512 => KnownOids.Sha512,
            _ => throw new ArgumentException($"Unsupported hash algorithm: {hashAlgorithm}", nameof(hashAlgorithm)),
        };

        Asn1Writer w = new();
        w.PushSequence();    // DigestInfo
        w.PushSequence();    // AlgorithmIdentifier
        w.WriteObjectIdentifier(hashOid);
        w.WriteNull();
        w.PopSequence();
        w.WriteOctetString(hash);
        w.PopSequence();
        byte[] t = w.ToArray();

        // EM = 0x00 || 0x01 || PS || 0x00 || T
        // PS = (k - 3 - T.Length) bytes of 0xFF; must be >= 8 per RFC 8017
        if (k < t.Length + 11)
        {
            throw new ArgumentException(
                $"RSA modulus too small for PKCS#1 v1.5 padding with this hash (need at least {t.Length + 11} bytes, have {k}).");
        }

        byte[] em = new byte[k];
        em[0] = 0x00;
        em[1] = 0x01;
        int psLen = k - t.Length - 3;
        for (int i = 0; i < psLen; i++) { em[2 + i] = 0xFF; }
        em[2 + psLen] = 0x00;
        Buffer.BlockCopy(t, 0, em, 2 + psLen + 1, t.Length);
        return em;
    }

    /// <summary>RFC 8017 §B.2.1 — MGF1 mask generation function.</summary>
    private static byte[] Mgf1(ReadOnlySpan<byte> seed, int length, HashAlgorithmName hashAlgorithm)
    {
        IHashAlgorithm prototype = HashFactory.Create(hashAlgorithm);
        int hLen = prototype.DigestSize;
        if (length > (long)hLen * 0x1_0000_0000L)
        {
            throw new ArgumentException("Requested MGF1 output too long.");
        }

        byte[] mask = new byte[length];
        int pos = 0;
        uint counter = 0;
        byte[] digest = new byte[hLen];

        Span<byte> counterBytes = stackalloc byte[4];
        while (pos < length)
        {
            IHashAlgorithm h = HashFactory.Create(hashAlgorithm);
            h.Update(seed);
            counterBytes[0] = (byte)(counter >> 24);
            counterBytes[1] = (byte)(counter >> 16);
            counterBytes[2] = (byte)(counter >> 8);
            counterBytes[3] = (byte)counter;
            h.Update(counterBytes);
            h.Finish(digest);

            int copy = Math.Min(hLen, length - pos);
            Buffer.BlockCopy(digest, 0, mask, pos, copy);
            pos += copy;
            counter++;
        }

        return mask;
    }

    private static int GetModulusBitLength(BigInteger modulus)
    {
        if (modulus.IsZero) { return 0; }
        byte[] le = modulus.ToByteArray();
        // Strip trailing 0 sign byte if present
        int significant = le.Length;
        while (significant > 0 && le[significant - 1] == 0) { significant--; }
        if (significant == 0) { return 0; }

        int bits = (significant - 1) * 8;
        byte top = le[significant - 1];
        while (top > 0)
        {
            bits++;
            top >>= 1;
        }
        return bits;
    }

    /// <summary>
    /// Constant-time byte-array comparison. Returns true iff the arrays are equal length
    /// and equal contents. The execution time depends on the length but not the contents.
    /// </summary>
    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) { return false; }
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }
}
