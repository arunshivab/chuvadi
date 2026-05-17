// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 8017 §9.2 — EMSA-PKCS1-v1_5 encoding
// PHASE: Phase 1.1.4 — RSA signing (shared with RSA verification)

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// RFC 8017 §9.2 EMSA-PKCS1-v1_5 encoding. Builds the encoded message that
/// RSA's modular-exp primitive operates on for both signing and verification.
/// </summary>
internal static class Pkcs1V15Encoding
{
    /// <summary>
    /// Builds <c>EM = 0x00 || 0x01 || PS || 0x00 || DigestInfo(hash)</c> with
    /// total length <paramref name="modulusSizeBytes"/>. Throws if the modulus
    /// is too small to fit the padding plus DigestInfo for this hash.
    /// </summary>
    public static byte[] BuildEncodedMessage(
        HashAlgorithmName hashAlgorithm,
        ReadOnlySpan<byte> hash,
        int modulusSizeBytes)
    {
        ObjectIdentifier hashOid = hashAlgorithm switch
        {
            HashAlgorithmName.Sha256 => KnownOids.Sha256,
            HashAlgorithmName.Sha384 => KnownOids.Sha384,
            HashAlgorithmName.Sha512 => KnownOids.Sha512,
            _ => throw new ArgumentException(
                $"Unsupported hash algorithm: {hashAlgorithm}", nameof(hashAlgorithm)),
        };

        // DigestInfo ::= SEQUENCE {
        //   digestAlgorithm AlgorithmIdentifier,
        //   digest          OCTET STRING
        // }
        Asn1Writer w = new();
        w.PushSequence();
        w.PushSequence();
        w.WriteObjectIdentifier(hashOid);
        w.WriteNull();
        w.PopSequence();
        w.WriteOctetString(hash);
        w.PopSequence();
        byte[] t = w.ToArray();

        // EM = 0x00 || 0x01 || PS || 0x00 || T
        // PS = (k - 3 - T.Length) bytes of 0xFF; minimum 8 per RFC 8017.
        if (modulusSizeBytes < t.Length + 11)
        {
            throw new ArgumentException(
                $"RSA modulus too small for PKCS#1 v1.5 padding with this hash "
                + $"(need at least {t.Length + 11} bytes, have {modulusSizeBytes}).");
        }

        byte[] em = new byte[modulusSizeBytes];
        em[0] = 0x00;
        em[1] = 0x01;
        int psLen = modulusSizeBytes - t.Length - 3;
        for (int i = 0; i < psLen; i++) { em[2 + i] = 0xFF; }
        em[2 + psLen] = 0x00;
        Buffer.BlockCopy(t, 0, em, 2 + psLen + 1, t.Length);
        return em;
    }
}
