// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  NIST FIPS 180-4 §6.4 (SHA-512), §6.5 (SHA-384)
// PHASE: Phase 1.1.4 — Cryptographic primitives
//
// SHA-512 and SHA-384 share identical mechanics; they differ only in initial
// hash values and final truncation. Both operate on 128-byte blocks of 64-bit
// big-endian words across 80 rounds.
//
// Padding: append 0x80, zero-pad so length+16 is a multiple of 128, append
// message length as a 128-bit big-endian bit count (we use 64-bit upper word
// of zero since C# applications can't exceed 2^64 bits).
//
// Constants K[0..79] are first 64 bits of fractional parts of cube roots of
// the first 80 primes (FIPS 180-4 §4.2.3).

using System;
using System.Buffers.Binary;

namespace Chuvadi.Cryptography.Hashing;

/// <summary>
/// SHA-512 and SHA-384 hash functions per FIPS 180-4 §6.4 and §6.5.
/// </summary>
public sealed class Sha512 : IHashAlgorithm
{
    private const int BlockSizeBytes = 128;
    private const int Sha512DigestSizeBytes = 64;
    private const int Sha384DigestSizeBytes = 48;

    private static readonly ulong[] K =
    {
        0x428A2F98D728AE22, 0x7137449123EF65CD, 0xB5C0FBCFEC4D3B2F, 0xE9B5DBA58189DBBC,
        0x3956C25BF348B538, 0x59F111F1B605D019, 0x923F82A4AF194F9B, 0xAB1C5ED5DA6D8118,
        0xD807AA98A3030242, 0x12835B0145706FBE, 0x243185BE4EE4B28C, 0x550C7DC3D5FFB4E2,
        0x72BE5D74F27B896F, 0x80DEB1FE3B1696B1, 0x9BDC06A725C71235, 0xC19BF174CF692694,
        0xE49B69C19EF14AD2, 0xEFBE4786384F25E3, 0x0FC19DC68B8CD5B5, 0x240CA1CC77AC9C65,
        0x2DE92C6F592B0275, 0x4A7484AA6EA6E483, 0x5CB0A9DCBD41FBD4, 0x76F988DA831153B5,
        0x983E5152EE66DFAB, 0xA831C66D2DB43210, 0xB00327C898FB213F, 0xBF597FC7BEEF0EE4,
        0xC6E00BF33DA88FC2, 0xD5A79147930AA725, 0x06CA6351E003826F, 0x142929670A0E6E70,
        0x27B70A8546D22FFC, 0x2E1B21385C26C926, 0x4D2C6DFC5AC42AED, 0x53380D139D95B3DF,
        0x650A73548BAF63DE, 0x766A0ABB3C77B2A8, 0x81C2C92E47EDAEE6, 0x92722C851482353B,
        0xA2BFE8A14CF10364, 0xA81A664BBC423001, 0xC24B8B70D0F89791, 0xC76C51A30654BE30,
        0xD192E819D6EF5218, 0xD69906245565A910, 0xF40E35855771202A, 0x106AA07032BBD1B8,
        0x19A4C116B8D2D0C8, 0x1E376C085141AB53, 0x2748774CDF8EEB99, 0x34B0BCB5E19B48A8,
        0x391C0CB3C5C95A63, 0x4ED8AA4AE3418ACB, 0x5B9CCA4F7763E373, 0x682E6FF3D6B2B8A3,
        0x748F82EE5DEFB2FC, 0x78A5636F43172F60, 0x84C87814A1F0AB72, 0x8CC702081A6439EC,
        0x90BEFFFA23631E28, 0xA4506CEBDE82BDE9, 0xBEF9A3F7B2C67915, 0xC67178F2E372532B,
        0xCA273ECEEA26619C, 0xD186B8C721C0C207, 0xEADA7DD6CDE0EB1E, 0xF57D4F7FEE6ED178,
        0x06F067AA72176FBA, 0x0A637DC5A2C898A6, 0x113F9804BEF90DAE, 0x1B710B35131C471B,
        0x28DB77F523047D84, 0x32CAAB7B40C72493, 0x3C9EBE0A15C9BEBC, 0x431D67C49C100D4C,
        0x4CC5D4BECB3E42B6, 0x597F299CFC657E2A, 0x5FCB6FAB3AD6FAEC, 0x6C44198C4A475817,
    };

    private readonly ulong[] _h = new ulong[8];
    private readonly byte[] _buffer = new byte[BlockSizeBytes];
    private int _bufferLength;
    private ulong _totalBytes;
    private bool _finished;
    private readonly HashAlgorithmName _name;
    private readonly int _digestSize;

    /// <summary>Initialises a SHA-512 instance.</summary>
    public Sha512() : this(HashAlgorithmName.Sha512) { }

    /// <summary>Initialises an instance for either SHA-512 or SHA-384.</summary>
    public Sha512(HashAlgorithmName name)
    {
        if (name != HashAlgorithmName.Sha512 && name != HashAlgorithmName.Sha384)
        {
            throw new ArgumentException(
                "Sha512 supports only SHA-512 and SHA-384.", nameof(name));
        }
        _name = name;
        _digestSize = name == HashAlgorithmName.Sha512
            ? Sha512DigestSizeBytes
            : Sha384DigestSizeBytes;
        Reset();
    }

    /// <inheritdoc/>
    public HashAlgorithmName Name => _name;

    /// <inheritdoc/>
    public int DigestSize => _digestSize;

    /// <inheritdoc/>
    public int BlockSize => BlockSizeBytes;

    /// <inheritdoc/>
    public void Reset()
    {
        if (_name == HashAlgorithmName.Sha512)
        {
            // FIPS 180-4 §5.3.5
            _h[0] = 0x6A09E667F3BCC908;
            _h[1] = 0xBB67AE8584CAA73B;
            _h[2] = 0x3C6EF372FE94F82B;
            _h[3] = 0xA54FF53A5F1D36F1;
            _h[4] = 0x510E527FADE682D1;
            _h[5] = 0x9B05688C2B3E6C1F;
            _h[6] = 0x1F83D9ABFB41BD6B;
            _h[7] = 0x5BE0CD19137E2179;
        }
        else
        {
            // SHA-384 IV — FIPS 180-4 §5.3.4
            _h[0] = 0xCBBB9D5DC1059ED8;
            _h[1] = 0x629A292A367CD507;
            _h[2] = 0x9159015A3070DD17;
            _h[3] = 0x152FECD8F70E5939;
            _h[4] = 0x67332667FFC00B31;
            _h[5] = 0x8EB44A8768581511;
            _h[6] = 0xDB0C2E0D64F98FA7;
            _h[7] = 0x47B5481DBEFA4FA4;
        }
        _bufferLength = 0;
        _totalBytes = 0;
        _finished = false;
    }

    /// <inheritdoc/>
    public void Update(ReadOnlySpan<byte> data)
    {
        if (_finished)
        {
            throw new InvalidOperationException("Cannot Update after Finish; call Reset first.");
        }

        _totalBytes += (ulong)data.Length;

        int pos = 0;
        while (pos < data.Length)
        {
            int needed = BlockSizeBytes - _bufferLength;
            int take = Math.Min(needed, data.Length - pos);
            data.Slice(pos, take).CopyTo(_buffer.AsSpan(_bufferLength));
            _bufferLength += take;
            pos += take;

            if (_bufferLength == BlockSizeBytes)
            {
                ProcessBlock(_buffer);
                _bufferLength = 0;
            }
        }
    }

    /// <inheritdoc/>
    public int Finish(Span<byte> destination)
    {
        if (_finished)
        {
            throw new InvalidOperationException("Hash already finalised; call Reset first.");
        }
        if (destination.Length < _digestSize)
        {
            throw new ArgumentException(
                $"Destination must be at least {_digestSize} bytes.", nameof(destination));
        }

        ulong bitLength = _totalBytes * 8;

        // Padding: 0x80 then zeros until length mod 128 == 112, then 128-bit length.
        Span<byte> tail = stackalloc byte[BlockSizeBytes * 2];
        int tailIndex = 0;
        tail[tailIndex++] = 0x80;

        int zeroPadding;
        if (_bufferLength < 112)
        {
            zeroPadding = 112 - _bufferLength - 1;
        }
        else
        {
            zeroPadding = BlockSizeBytes + 112 - _bufferLength - 1;
        }
        for (int i = 0; i < zeroPadding; i++) { tail[tailIndex++] = 0; }

        // 128-bit length: upper 64 bits are zero (we don't track values that high)
        for (int i = 0; i < 8; i++) { tail[tailIndex++] = 0; }
        BinaryPrimitives.WriteUInt64BigEndian(tail.Slice(tailIndex, 8), bitLength);
        tailIndex += 8;

        Update(tail.Slice(0, tailIndex));

        int wordsToWrite = _digestSize / 8;
        for (int i = 0; i < wordsToWrite; i++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(i * 8, 8), _h[i]);
        }
        _finished = true;
        return _digestSize;
    }

    private void ProcessBlock(ReadOnlySpan<byte> block)
    {
        Span<ulong> w = stackalloc ulong[80];

        for (int i = 0; i < 16; i++)
        {
            w[i] = BinaryPrimitives.ReadUInt64BigEndian(block.Slice(i * 8, 8));
        }
        for (int i = 16; i < 80; i++)
        {
            ulong s0 = RotateRight(w[i - 15], 1) ^ RotateRight(w[i - 15], 8) ^ (w[i - 15] >> 7);
            ulong s1 = RotateRight(w[i - 2], 19) ^ RotateRight(w[i - 2], 61) ^ (w[i - 2] >> 6);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }

        ulong a = _h[0];
        ulong b = _h[1];
        ulong c = _h[2];
        ulong d = _h[3];
        ulong e = _h[4];
        ulong f = _h[5];
        ulong g = _h[6];
        ulong h = _h[7];

        for (int t = 0; t < 80; t++)
        {
            ulong big1 = RotateRight(e, 14) ^ RotateRight(e, 18) ^ RotateRight(e, 41);
            ulong ch = (e & f) ^ (~e & g);
            ulong t1 = h + big1 + ch + K[t] + w[t];
            ulong big0 = RotateRight(a, 28) ^ RotateRight(a, 34) ^ RotateRight(a, 39);
            ulong maj = (a & b) ^ (a & c) ^ (b & c);
            ulong t2 = big0 + maj;

            h = g;
            g = f;
            f = e;
            e = d + t1;
            d = c;
            c = b;
            b = a;
            a = t1 + t2;
        }

        _h[0] += a;
        _h[1] += b;
        _h[2] += c;
        _h[3] += d;
        _h[4] += e;
        _h[5] += f;
        _h[6] += g;
        _h[7] += h;
    }

    private static ulong RotateRight(ulong value, int amount)
        => (value >> amount) | (value << (64 - amount));

    /// <summary>One-shot SHA-512: hashes <paramref name="data"/> and returns the digest.</summary>
    public static byte[] HashDataSha512(ReadOnlySpan<byte> data)
    {
        Sha512 sha = new(HashAlgorithmName.Sha512);
        sha.Update(data);
        byte[] digest = new byte[Sha512DigestSizeBytes];
        sha.Finish(digest);
        return digest;
    }

    /// <summary>One-shot SHA-384: hashes <paramref name="data"/> and returns the digest.</summary>
    public static byte[] HashDataSha384(ReadOnlySpan<byte> data)
    {
        Sha512 sha = new(HashAlgorithmName.Sha384);
        sha.Update(data);
        byte[] digest = new byte[Sha384DigestSizeBytes];
        sha.Finish(digest);
        return digest;
    }
}
