// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6 — Algorithm 1 for V=4, AES-128
//        PDF 32000-2:2020 §7.6.4.3 — Algorithm 9 / 10 (R=6 file-key generation)
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption
//
// Encrypts strings and streams for writing encrypted PDFs. Symmetric with
// Decryptor in the per-object key derivation. Only AES-128 (V=4 R=4) and
// AES-256 (V=5 R=6) are supported for writing; legacy RC4 is read-only.

using System;
using System.Security.Cryptography;

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// Encrypts individual strings and streams for writing an encrypted PDF.
/// </summary>
public sealed class Encryptor
{
    private readonly byte[] _fileKey;
    private readonly EncryptionAlgorithm _algorithm;

    /// <summary>Constructs an encryptor for the given file key and algorithm.</summary>
    public Encryptor(byte[] fileKey, EncryptionAlgorithm algorithm)
    {
        if (algorithm != EncryptionAlgorithm.Aes_128 &&
            algorithm != EncryptionAlgorithm.Aes_256)
        {
            throw new EncryptionException(
                $"Encryption for writing is only supported for AES-128 and AES-256; got {algorithm}.");
        }

        _fileKey = fileKey ?? throw new ArgumentNullException(nameof(fileKey));
        _algorithm = algorithm;
    }

    /// <summary>Encrypts data belonging to a specific indirect object.</summary>
    public byte[] Encrypt(byte[] data, int objectNumber, int generation)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return _algorithm switch
        {
            EncryptionAlgorithm.Aes_128 => EncryptAes128PerObject(data, objectNumber, generation),
            EncryptionAlgorithm.Aes_256 => AesCrypto.Encrypt(_fileKey, data),
            _ => throw new EncryptionException($"Cannot encrypt: unsupported algorithm {_algorithm}."),
        };
    }

    private byte[] EncryptAes128PerObject(byte[] data, int objectNumber, int generation)
    {
        byte[] objKey = DerivePerObjectKey(objectNumber, generation, aes: true);
        return AesCrypto.Encrypt(objKey, data);
    }

    private byte[] DerivePerObjectKey(int objectNumber, int generation, bool aes)
    {
        int extra = aes ? 9 : 5;
        byte[] input = new byte[_fileKey.Length + extra];
        Array.Copy(_fileKey, input, _fileKey.Length);

        int p = _fileKey.Length;
        input[p++] = (byte)(objectNumber & 0xFF);
        input[p++] = (byte)((objectNumber >> 8) & 0xFF);
        input[p++] = (byte)((objectNumber >> 16) & 0xFF);
        input[p++] = (byte)(generation & 0xFF);
        input[p++] = (byte)((generation >> 8) & 0xFF);

        if (aes)
        {
            input[p++] = (byte)'s';
            input[p++] = (byte)'A';
            input[p++] = (byte)'l';
            input[p++] = (byte)'T';
        }

        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(input);

        int keyLen = Math.Min(_fileKey.Length + 5, 16);
        byte[] key = new byte[keyLen];
        Array.Copy(hash, key, keyLen);
        return key;
    }

    /// <summary>
    /// Generates a random 32-byte file key suitable for AES-256 encryption.
    /// </summary>
    public static byte[] GenerateFileKeyAes256()
    {
        byte[] key = new byte[32];
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);
        return key;
    }

    /// <summary>
    /// Generates a random 16-byte file key suitable for AES-128 encryption.
    /// </summary>
    public static byte[] GenerateFileKeyAes128()
    {
        byte[] key = new byte[16];
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);
        return key;
    }
}
