// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — SHA-1 for OCSP CertID matching only
//
// This helper exists because RFC 6960 §4.1.1 mandates SHA-1 for the
// IssuerNameHash / IssuerKeyHash inside a CertID. SHA-1 is otherwise deprecated
// in Chuvadi (HashFactory refuses it). This helper is INTERNAL and used only
// for the non-security-critical task of matching an OCSP CertID to a cert+issuer
// pair. The signature on the OCSP response itself uses a strong algorithm; this
// hash is merely a lookup key.

using System;

namespace Chuvadi.Cryptography.PathValidation;

internal static class Sha1
{
    public static byte[] Compute(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        uint h0 = 0x67452301, h1 = 0xEFCDAB89, h2 = 0x98BADCFE, h3 = 0x10325476, h4 = 0xC3D2E1F0;
        long bitLen = (long)data.Length * 8;
        int padLen = (56 - (data.Length + 1) % 64 + 64) % 64;
        byte[] padded = new byte[data.Length + 1 + padLen + 8];
        Buffer.BlockCopy(data, 0, padded, 0, data.Length);
        padded[data.Length] = 0x80;
        for (int i = 0; i < 8; i++)
        {
            padded[padded.Length - 1 - i] = (byte)(bitLen >> (8 * i));
        }
        for (int chunk = 0; chunk < padded.Length; chunk += 64)
        {
            uint[] w = new uint[80];
            for (int i = 0; i < 16; i++)
            {
                w[i] = ((uint)padded[chunk + i * 4] << 24)
                     | ((uint)padded[chunk + i * 4 + 1] << 16)
                     | ((uint)padded[chunk + i * 4 + 2] << 8)
                     | (uint)padded[chunk + i * 4 + 3];
            }
            for (int i = 16; i < 80; i++)
            {
                uint v = w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16];
                w[i] = (v << 1) | (v >> 31);
            }
            uint a = h0, b = h1, c = h2, d = h3, e = h4;
            for (int i = 0; i < 80; i++)
            {
                uint f, k;
                if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                uint temp = ((a << 5) | (a >> 27)) + f + e + k + w[i];
                e = d; d = c; c = (b << 30) | (b >> 2); b = a; a = temp;
            }
            h0 += a; h1 += b; h2 += c; h3 += d; h4 += e;
        }
        byte[] result = new byte[20];
        WriteUInt32(result, 0, h0);
        WriteUInt32(result, 4, h1);
        WriteUInt32(result, 8, h2);
        WriteUInt32(result, 12, h3);
        WriteUInt32(result, 16, h4);
        return result;
    }

    private static void WriteUInt32(byte[] dest, int offset, uint value)
    {
        dest[offset] = (byte)(value >> 24);
        dest[offset + 1] = (byte)(value >> 16);
        dest[offset + 2] = (byte)(value >> 8);
        dest[offset + 3] = (byte)value;
    }
}
