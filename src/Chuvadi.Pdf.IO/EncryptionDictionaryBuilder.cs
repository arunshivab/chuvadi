// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.4.3 — Algorithm 3 (O value) and Algorithm 5 (U value)
//        PDF 32000-2:2020 §7.6.4.3.3 — Algorithm 8 (U/UE) and Algorithm 9 (O/OE) for R=6
// PHASE: Phase 1.1.5 (integration) — Chuvadi.Pdf.IO
//
// Builds the /Encrypt dictionary for a new encrypted PDF from an EncryptionOptions
// plus the file's /ID[0]. This is the inverse of EncryptionDictionary.Parse.

using System;
using System.Security.Cryptography;
using System.Text;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Builds an /Encrypt dictionary for writing encrypted PDFs.
/// </summary>
internal static class EncryptionDictionaryBuilder
{
    /// <summary>
    /// Constructs the /Encrypt dictionary for the chosen algorithm/passwords.
    /// </summary>
    public static PdfDictionary Build(EncryptionOptions options, byte[] firstFileId)
    {
        return options.Algorithm switch
        {
            EncryptionAlgorithm.Aes_128 => BuildAes128(options, firstFileId),
            EncryptionAlgorithm.Aes_256 => BuildAes256(options),
            _ => throw new ArgumentException(
                $"Unsupported algorithm for writing: {options.Algorithm}", nameof(options)),
        };
    }

    // ── AES-128 (V=4 R=4) ─────────────────────────────────────────────────

    private static PdfDictionary BuildAes128(EncryptionOptions options, byte[] firstFileId)
    {
        // Algorithm 3: O value
        byte[] oValue = ComputeOValueR4(options.UserPassword, options.OwnerPassword, 16, revision: 4);

        // Algorithm 5: U value — uses the file key
        // For writing we derive the file key from the user password the same way
        // a reader would, so that opening the result with the user password
        // re-derives the same key. But options.FileKey was randomly generated,
        // which is fine; we recompute the U value using a key derived from the
        // user password and place that in /U. The reader will derive the same
        // key and decrypt successfully.

        byte[] userDerivedKey = StandardSecurityHandler.DeriveKeyR4(
            options.UserPassword,
            oValue,
            options.Permissions,
            firstFileId,
            revision: 4,
            keyLength: 16,
            options.EncryptMetadata);

        // The actual file key used to encrypt streams is userDerivedKey, NOT a
        // random key. Overwrite options.FileKey by storing it back through the
        // Encryptor; we'll handle this by mutating the options at construction
        // time inside PdfWriter instead — see note there.
        // For now, build U from userDerivedKey:
        byte[] uValue = ComputeUValueR4(userDerivedKey, firstFileId, revision: 4);

        // CRITICAL: replace the file key in options with the derived key so that
        // PdfWriter encrypts with the same key the reader will derive.
        Buffer.BlockCopy(userDerivedKey, 0, options.FileKey, 0, Math.Min(userDerivedKey.Length, options.FileKey.Length));

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern("Standard"));
        dict.Set(PdfName.Intern("V"), 4);
        dict.Set(PdfName.Intern("R"), 4);
        dict.Set(PdfName.Intern("Length"), 128);
        dict.Set(PdfName.Intern("P"), options.Permissions);
        dict.Set(PdfName.Intern("O"), new PdfString(oValue));
        dict.Set(PdfName.Intern("U"), new PdfString(uValue));

        if (!options.EncryptMetadata)
        {
            dict.Set(PdfName.Intern("EncryptMetadata"), false);
        }

        // /CF crypt filter mapping (V=4 requirement)
        PdfDictionary stdCF = new PdfDictionary();
        stdCF.Set(PdfName.Intern("CFM"), PdfName.Intern("AESV2"));
        stdCF.Set(PdfName.Intern("Length"), 16);
        stdCF.Set(PdfName.Intern("AuthEvent"), PdfName.Intern("DocOpen"));

        PdfDictionary cf = new PdfDictionary();
        cf.Set(PdfName.Intern("StdCF"), stdCF);
        dict.Set(PdfName.Intern("CF"), cf);
        dict.Set(PdfName.Intern("StmF"), PdfName.Intern("StdCF"));
        dict.Set(PdfName.Intern("StrF"), PdfName.Intern("StdCF"));

        return dict;
    }

    // Padding string from PDF 32000 §7.6.4.3.2 step 1
    private static readonly byte[] Padding = new byte[]
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    };

    private static byte[] ComputeOValueR4(string userPwd, string ownerPwd, int keyLen, int revision)
    {
        // Algorithm 3: compute /O
        // Steps roughly:
        //  1. Pad both passwords to 32 bytes
        //  2. MD5(padded owner password)
        //  3. For R>=3, hash 50 more times
        //  4. Take first keyLen bytes → RC4 key
        //  5. Pad user password, RC4 it with that key
        //  6. For R>=3, RC4 19 more iterations with key XORed by index
        byte[] paddedOwner = PadPassword(ownerPwd);

        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(paddedOwner);

        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
            {
                using MD5 inner = MD5.Create();
                hash = inner.ComputeHash(hash, 0, keyLen);
            }
        }

        byte[] rc4Key = new byte[keyLen];
        Array.Copy(hash, rc4Key, keyLen);

        byte[] paddedUser = PadPassword(userPwd);
        byte[] o = Rc4.Process(rc4Key, paddedUser);

        if (revision >= 3)
        {
            for (int i = 1; i <= 19; i++)
            {
                byte[] iterKey = new byte[keyLen];
                for (int k = 0; k < keyLen; k++)
                {
                    iterKey[k] = (byte)(rc4Key[k] ^ i);
                }
                o = Rc4.Process(iterKey, o);
            }
        }

        return o;
    }

    private static byte[] ComputeUValueR4(byte[] key, byte[] firstFileId, int revision)
    {
        if (revision == 2)
        {
            return Rc4.Process(key, Padding);
        }

        using MD5 md5 = MD5.Create();
        md5.TransformBlock(Padding, 0, 32, null, 0);
        md5.TransformBlock(firstFileId, 0, firstFileId.Length, null, 0);
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        byte[] hash = md5.Hash!;

        byte[] u = Rc4.Process(key, hash);

        for (int i = 1; i <= 19; i++)
        {
            byte[] iterKey = new byte[key.Length];
            for (int k = 0; k < key.Length; k++)
            {
                iterKey[k] = (byte)(key[k] ^ i);
            }
            u = Rc4.Process(iterKey, u);
        }

        byte[] padded = new byte[32];
        Array.Copy(u, padded, 16);
        return padded;
    }

    private static byte[] PadPassword(string password)
    {
        byte[] result = new byte[32];
        byte[] pwd = Encoding.Latin1.GetBytes(password ?? string.Empty);
        int pwdLen = Math.Min(pwd.Length, 32);
        Array.Copy(pwd, result, pwdLen);

        if (pwdLen < 32)
        {
            Array.Copy(Padding, 0, result, pwdLen, 32 - pwdLen);
        }

        return result;
    }

    // ── AES-256 (V=5 R=6) ─────────────────────────────────────────────────

    private static PdfDictionary BuildAes256(EncryptionOptions options)
    {
        // ISO 32000-2 Algorithms 8 and 9: build U, UE, O, OE, Perms
        byte[] fileKey = options.FileKey;
        byte[] userPwd = Encoding.UTF8.GetBytes(options.UserPassword ?? string.Empty);
        byte[] ownerPwd = Encoding.UTF8.GetBytes(options.OwnerPassword ?? string.Empty);

        // Validation and key salts (random)
        byte[] userValSalt = RandomBytes(8);
        byte[] userKeySalt = RandomBytes(8);
        byte[] ownerValSalt = RandomBytes(8);
        byte[] ownerKeySalt = RandomBytes(8);

        // U = hash(password || validation salt) || validation salt || key salt
        byte[] uHash = HashR6(userPwd, userValSalt, Array.Empty<byte>());
        byte[] uValue = Concat(uHash, userValSalt, userKeySalt);

        // UE = AES-256-CBC encrypt(file key) with key=hash(password || key salt), IV=0
        byte[] userKey = HashR6(userPwd, userKeySalt, Array.Empty<byte>());
        byte[] ueValue = AesEcbLikeEncrypt(userKey, fileKey);

        // O = hash(password || validation salt || U) || validation salt || key salt
        byte[] oHash = HashR6(ownerPwd, ownerValSalt, uValue);
        byte[] oValue = Concat(oHash, ownerValSalt, ownerKeySalt);

        byte[] ownerKey = HashR6(ownerPwd, ownerKeySalt, uValue);
        byte[] oeValue = AesEcbLikeEncrypt(ownerKey, fileKey);

        // Perms: AES-256-ECB encrypt 16-byte permission block with file key
        byte[] permsBlock = BuildPermsBlock(options.Permissions, options.EncryptMetadata);
        byte[] permsEncrypted = AesEcbEncryptOneBlock(fileKey, permsBlock);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern("Standard"));
        dict.Set(PdfName.Intern("V"), 5);
        dict.Set(PdfName.Intern("R"), 6);
        dict.Set(PdfName.Intern("Length"), 256);
        dict.Set(PdfName.Intern("P"), options.Permissions);
        dict.Set(PdfName.Intern("O"), new PdfString(oValue));
        dict.Set(PdfName.Intern("U"), new PdfString(uValue));
        dict.Set(PdfName.Intern("OE"), new PdfString(oeValue));
        dict.Set(PdfName.Intern("UE"), new PdfString(ueValue));
        dict.Set(PdfName.Intern("Perms"), new PdfString(permsEncrypted));

        if (!options.EncryptMetadata)
        {
            dict.Set(PdfName.Intern("EncryptMetadata"), false);
        }

        PdfDictionary stdCF = new PdfDictionary();
        stdCF.Set(PdfName.Intern("CFM"), PdfName.Intern("AESV3"));
        stdCF.Set(PdfName.Intern("Length"), 32);
        stdCF.Set(PdfName.Intern("AuthEvent"), PdfName.Intern("DocOpen"));

        PdfDictionary cf = new PdfDictionary();
        cf.Set(PdfName.Intern("StdCF"), stdCF);
        dict.Set(PdfName.Intern("CF"), cf);
        dict.Set(PdfName.Intern("StmF"), PdfName.Intern("StdCF"));
        dict.Set(PdfName.Intern("StrF"), PdfName.Intern("StdCF"));

        return dict;
    }

    private static byte[] HashR6(byte[] password, byte[] salt, byte[] uData)
    {
        // Mirror of StandardSecurityHandler.ComputeHashR6, internalised here.
        using SHA256 sha = SHA256.Create();
        sha.TransformBlock(password, 0, password.Length, null, 0);
        sha.TransformBlock(salt, 0, salt.Length, null, 0);
        if (uData.Length > 0)
        {
            sha.TransformBlock(uData, 0, uData.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        byte[] k = sha.Hash!;

        int round = 0;
        while (true)
        {
            int blockLen = password.Length + k.Length + uData.Length;
            byte[] k1 = new byte[blockLen * 64];
            for (int i = 0; i < 64; i++)
            {
                int offset = i * blockLen;
                Buffer.BlockCopy(password, 0, k1, offset, password.Length);
                Buffer.BlockCopy(k, 0, k1, offset + password.Length, k.Length);
                if (uData.Length > 0)
                {
                    Buffer.BlockCopy(uData, 0, k1, offset + password.Length + k.Length, uData.Length);
                }
            }

            byte[] keyBytes = new byte[16];
            byte[] iv = new byte[16];
            Array.Copy(k, 0, keyBytes, 0, 16);
            Array.Copy(k, 16, iv, 0, 16);

            using Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = keyBytes;
            aes.IV = iv;
            using ICryptoTransform enc = aes.CreateEncryptor();
            byte[] e = enc.TransformFinalBlock(k1, 0, k1.Length);

            int mod3 = 0;
            for (int i = 0; i < 16; i++)
            {
                mod3 = ((mod3 << 8) | e[i]) % 3;
            }

            HashAlgorithm next = mod3 switch
            {
                0 => SHA256.Create(),
                1 => SHA384.Create(),
                _ => SHA512.Create(),
            };

            k = next.ComputeHash(e);
            next.Dispose();
            round++;

            if (round >= 64 && e[e.Length - 1] <= round - 32)
            {
                break;
            }
        }

        byte[] result = new byte[32];
        Array.Copy(k, result, 32);
        return result;
    }

    private static byte[] AesEcbLikeEncrypt(byte[] key, byte[] data)
    {
        // AES-CBC with zero IV and no padding — equivalent to ECB for one block,
        // and to CBC-zero for multi-block. PDF Algorithm 8/9 uses this.
        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = new byte[16];
        using ICryptoTransform enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] AesEcbEncryptOneBlock(byte[] key, byte[] data)
    {
        if (data.Length != 16)
        {
            throw new ArgumentException("Block must be exactly 16 bytes.", nameof(data));
        }
        return AesEcbLikeEncrypt(key, data);
    }

    private static byte[] BuildPermsBlock(int permissions, bool encryptMetadata)
    {
        // Per ISO 32000-2 §7.6.4.4: 16-byte block
        //   bytes 0-3: permissions (little-endian)
        //   bytes 4-7: 0xFF 0xFF 0xFF 0xFF
        //   byte 8:    'T' if EncryptMetadata else 'F'
        //   bytes 9-11: 'a','d','b' (literal)
        //   bytes 12-15: random
        byte[] block = new byte[16];
        block[0] = (byte)(permissions & 0xFF);
        block[1] = (byte)((permissions >> 8) & 0xFF);
        block[2] = (byte)((permissions >> 16) & 0xFF);
        block[3] = (byte)((permissions >> 24) & 0xFF);
        block[4] = 0xFF;
        block[5] = 0xFF;
        block[6] = 0xFF;
        block[7] = 0xFF;
        block[8] = (byte)(encryptMetadata ? 'T' : 'F');
        block[9] = (byte)'a';
        block[10] = (byte)'d';
        block[11] = (byte)'b';

        byte[] rand = RandomBytes(4);
        block[12] = rand[0];
        block[13] = rand[1];
        block[14] = rand[2];
        block[15] = rand[3];

        return block;
    }

    private static byte[] RandomBytes(int n)
    {
        byte[] b = new byte[n];
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(b);
        return b;
    }

    private static byte[] Concat(byte[] a, byte[] b, byte[] c)
    {
        byte[] result = new byte[a.Length + b.Length + c.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        Buffer.BlockCopy(c, 0, result, a.Length + b.Length, c.Length);
        return result;
    }
}
