// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.2: AES-128 CBC with PKCS#7 padding, IV prefix
//        PDF 32000-2:2020 §7.6.3: AES-256 CBC, same wrapping
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption
//
// Wraps System.Security.Cryptography.Aes. The PDF spec mandates the same
// CBC mode + PKCS#7 padding + IV-prefix wire format for both AES-128 and
// AES-256. The 16-byte IV is the first 16 bytes of the ciphertext; the
// remaining bytes are the encrypted payload.

using System;
using System.Security.Cryptography;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// AES-CBC encryption/decryption with PDF's IV-prefix wire format.
/// </summary>
/// <remarks>
/// PDF 32000 mandates CBC mode with PKCS#7 padding. The 16-byte IV is
/// stored as a prefix on the ciphertext; ciphertext length is therefore
/// always a multiple of 16 plus 16 bytes for the IV. AES-128 uses a
/// 128-bit key; AES-256 uses a 256-bit key. The mode is identical.
/// </remarks>
public static class AesCrypto
{
    /// <summary>
    /// Decrypts data that begins with a 16-byte IV followed by AES-CBC ciphertext.
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> ivAndCipher)
    {
        if (ivAndCipher.Length < 16)
        {
            throw new PdfEncryptionException("AES payload too short to contain an IV.");
        }

        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = key.Length * 8;
        aes.Key = key.ToArray();
        aes.IV = ivAndCipher[..16].ToArray();

        byte[] cipher = ivAndCipher[16..].ToArray();

        using ICryptoTransform transform = aes.CreateDecryptor();

        try
        {
            return transform.TransformFinalBlock(cipher, 0, cipher.Length);
        }
        catch (CryptographicException ex)
        {
            throw new PdfEncryptionException("AES decryption failed (wrong key or corrupted data).", ex);
        }
    }

    /// <summary>
    /// Encrypts data with AES-CBC and prefixes a 16-byte IV. The IV is generated
    /// from a cryptographically strong random source.
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plain)
    {
        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = key.Length * 8;
        aes.Key = key.ToArray();
        aes.GenerateIV();

        byte[] iv = aes.IV;
        using ICryptoTransform transform = aes.CreateEncryptor();
        byte[] cipher = transform.TransformFinalBlock(plain.ToArray(), 0, plain.Length);

        byte[] output = new byte[iv.Length + cipher.Length];
        Buffer.BlockCopy(iv, 0, output, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, output, iv.Length, cipher.Length);
        return output;
    }
}
