// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 2104 — HMAC: Keyed-Hashing for Message Authentication
// PHASE: Phase 1.2.6 — primitives batch

using System;

namespace Chuvadi.Cryptography.Hashing;

/// <summary>
/// HMAC keyed-hash message authentication code per RFC 2104.
/// </summary>
/// <remarks>
/// Implementation directly follows RFC 2104 §2. The key is reduced to
/// blocksize bytes (hashed if longer, zero-padded if shorter); inner and
/// outer pads are XORed with 0x36 and 0x5C respectively; the MAC is
/// <c>H(K xor opad || H(K xor ipad || message))</c>.
/// </remarks>
public static class Hmac
{
    /// <summary>Computes <c>HMAC-H(key, message)</c>.</summary>
    public static byte[] Compute(
        HashAlgorithmName hashAlgorithm,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> message)
    {
        IHashAlgorithm probe = HashFactory.Create(hashAlgorithm);
        int blockSize = probe.BlockSize;
        int digestSize = probe.DigestSize;

        // Reduce the key.
        byte[] k0 = new byte[blockSize];
        if (key.Length > blockSize)
        {
            byte[] hashed = new byte[digestSize];
            probe.Update(key);
            probe.Finish(hashed);
            Buffer.BlockCopy(hashed, 0, k0, 0, hashed.Length);
        }
        else
        {
            key.CopyTo(k0);
            // probe was unused — recreate freshly below.
        }

        // Inner: H((K0 xor ipad) || message)
        byte[] innerKey = new byte[blockSize];
        for (int i = 0; i < blockSize; i++) { innerKey[i] = (byte)(k0[i] ^ 0x36); }
        IHashAlgorithm inner = HashFactory.Create(hashAlgorithm);
        byte[] innerDigest = new byte[digestSize];
        inner.Update(innerKey);
        inner.Update(message);
        inner.Finish(innerDigest);

        // Outer: H((K0 xor opad) || innerDigest)
        byte[] outerKey = new byte[blockSize];
        for (int i = 0; i < blockSize; i++) { outerKey[i] = (byte)(k0[i] ^ 0x5C); }
        IHashAlgorithm outer = HashFactory.Create(hashAlgorithm);
        byte[] outerDigest = new byte[digestSize];
        outer.Update(outerKey);
        outer.Update(innerDigest);
        outer.Finish(outerDigest);
        return outerDigest;
    }
}
