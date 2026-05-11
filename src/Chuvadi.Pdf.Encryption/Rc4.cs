// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RC4 — Schneier "Applied Cryptography" §17.1; RFC 6229 test vectors.
//        PDF 32000-1:2008 §7.6.2 specifies RC4 for V≤3 and V=4 with CFM=V2.
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption
//
// RC4 stream cipher in pure managed code. Used for decryption of legacy
// PDFs (V≤3 and V=4 with /CFM = /V2). Symmetric — Encrypt() and Decrypt()
// are the same operation.

using System;

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// RC4 stream cipher. Symmetric: the same operation encrypts and decrypts.
/// </summary>
/// <remarks>
/// RC4 has known cryptographic weaknesses and is included only for reading
/// legacy PDFs. New PDFs created by Chuvadi never use RC4.
/// </remarks>
public static class Rc4
{
    /// <summary>
    /// Runs RC4 over <paramref name="data"/> with the given key. Returns a new
    /// byte array; <paramref name="data"/> is not modified.
    /// </summary>
    public static byte[] Process(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        byte[] s = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            s[i] = (byte)i;
        }

        int j = 0;
        int keyLen = key.Length;

        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % keyLen]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        byte[] output = new byte[data.Length];
        int x = 0;
        int y = 0;

        for (int n = 0; n < data.Length; n++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            (s[x], s[y]) = (s[y], s[x]);
            byte k = s[(s[x] + s[y]) & 0xFF];
            output[n] = (byte)(data[n] ^ k);
        }

        return output;
    }
}
