// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  NIST FIPS 180-4 §6.2 — SHA-256 specification
// PHASE: Phase 1.1.4 — Cryptographic primitives
//
// Implementation notes:
// - Operates on 64-byte blocks; 32-bit big-endian words.
// - Final padding: append 0x80, zero-pad to 56 (mod 64) bytes, append message
//   length as 64-bit big-endian bit count.
// - Constants K[0..63] are first 32 bits of fractional parts of cube roots of
//   the first 64 primes (FIPS 180-4 §4.2.2).
// - Initial hash values H[0..7] are first 32 bits of fractional parts of
//   square roots of the first 8 primes (FIPS 180-4 §5.3.3).

using System;
using System.Buffers.Binary;

namespace Chuvadi.Cryptography.Hashing;

/// <summary>
/// SHA-256 hash function per FIPS 180-4 §6.2.
/// </summary>
public sealed class Sha256 : IHashAlgorithm
{
    private const int BlockSizeBytes = 64;
    private const int DigestSizeBytes = 32;

    private static readonly uint[] K =
    {
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
        0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
        0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
        0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
        0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2,
    };

    private readonly uint[] _h = new uint[8];
    private readonly byte[] _buffer = new byte[BlockSizeBytes];
    private int _bufferLength;
    private ulong _totalBytes;
    private bool _finished;

    /// <summary>Initialises a new SHA-256 instance.</summary>
    public Sha256() { Reset(); }

    /// <inheritdoc/>
    public HashAlgorithmName Name => HashAlgorithmName.Sha256;

    /// <inheritdoc/>
    public int DigestSize => DigestSizeBytes;

    /// <inheritdoc/>
    public int BlockSize => BlockSizeBytes;

    /// <inheritdoc/>
    public void Reset()
    {
        _h[0] = 0x6A09E667;
        _h[1] = 0xBB67AE85;
        _h[2] = 0x3C6EF372;
        _h[3] = 0xA54FF53A;
        _h[4] = 0x510E527F;
        _h[5] = 0x9B05688C;
        _h[6] = 0x1F83D9AB;
        _h[7] = 0x5BE0CD19;
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

        // Drain into buffer until a full block is available, then process.
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
        if (destination.Length < DigestSizeBytes)
        {
            throw new ArgumentException(
                $"Destination must be at least {DigestSizeBytes} bytes.", nameof(destination));
        }

        // Append 0x80, zero-pad so length+8 is a multiple of 64, append 64-bit
        // big-endian bit count.
        ulong bitLength = _totalBytes * 8;

        Span<byte> tail = stackalloc byte[BlockSizeBytes * 2];
        int tailIndex = 0;
        tail[tailIndex++] = 0x80;

        int zeroPadding;
        if (_bufferLength < 56)
        {
            zeroPadding = 56 - _bufferLength - 1;
        }
        else
        {
            zeroPadding = BlockSizeBytes + 56 - _bufferLength - 1;
        }
        for (int i = 0; i < zeroPadding; i++) { tail[tailIndex++] = 0; }

        BinaryPrimitives.WriteUInt64BigEndian(tail.Slice(tailIndex, 8), bitLength);
        tailIndex += 8;

        Update(tail.Slice(0, tailIndex));

        // _bufferLength must now be 0
        for (int i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(i * 4, 4), _h[i]);
        }
        _finished = true;
        return DigestSizeBytes;
    }

    private void ProcessBlock(ReadOnlySpan<byte> block)
    {
        Span<uint> w = stackalloc uint[64];

        for (int i = 0; i < 16; i++)
        {
            w[i] = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(i * 4, 4));
        }
        for (int i = 16; i < 64; i++)
        {
            uint s0 = RotateRight(w[i - 15], 7) ^ RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
            uint s1 = RotateRight(w[i - 2], 17) ^ RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }

        uint a = _h[0];
        uint b = _h[1];
        uint c = _h[2];
        uint d = _h[3];
        uint e = _h[4];
        uint f = _h[5];
        uint g = _h[6];
        uint h = _h[7];

        for (int t = 0; t < 64; t++)
        {
            uint big1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
            uint ch = (e & f) ^ (~e & g);
            uint t1 = h + big1 + ch + K[t] + w[t];
            uint big0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
            uint maj = (a & b) ^ (a & c) ^ (b & c);
            uint t2 = big0 + maj;

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

    private static uint RotateRight(uint value, int amount)
        => (value >> amount) | (value << (32 - amount));

    /// <summary>One-shot convenience: hashes <paramref name="data"/> and returns the digest.</summary>
    public static byte[] HashData(ReadOnlySpan<byte> data)
    {
        Sha256 sha = new();
        sha.Update(data);
        byte[] digest = new byte[DigestSizeBytes];
        sha.Finish(digest);
        return digest;
    }
}
