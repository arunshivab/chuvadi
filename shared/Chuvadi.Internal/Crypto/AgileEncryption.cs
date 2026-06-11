using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Chuvadi.Internal.Crypto;

/// <summary>
/// OOXML Agile Encryption (per [MS-OFFCRYPTO] §2.3.4.10-15). Encrypts an entire OOXML package
/// (zip blob) as one opaque encrypted stream, with metadata describing the parameters.
///
/// The scheme in summary:
///
///   1. Generate random salt + key.
///   2. Derive intermediate key from password via a PBKDF2-style iterated SHA-512
///      (100,000 iterations default — see <see cref="DefaultSpinCount"/>).
///   3. Use intermediate key to encrypt the actual content key (key encryption).
///   4. Use the content key to AES-CBC encrypt the package, with per-block IVs derived
///      from a "block key" hash.
///   5. Generate two verifier blocks for password verification (separate from the content).
///   6. Generate a HMAC-SHA512 over the encrypted content for integrity.
///
/// Decrypting reverses all of this and verifies both the verifier and the HMAC.
/// </summary>
internal static class AgileEncryption
{
    // Spec-defined "block keys" — single-byte sequences used to derive per-purpose keys.
    // See [MS-OFFCRYPTO] §2.3.4.13.
    private static readonly byte[] BlockKey_VerifierHashInput      = new byte[] { 0xfe, 0xa7, 0xd2, 0x76, 0x3b, 0x4b, 0x9e, 0x79 };
    private static readonly byte[] BlockKey_VerifierHashValue      = new byte[] { 0xd7, 0xaa, 0x0f, 0x6d, 0x30, 0x61, 0x34, 0x4e };
    private static readonly byte[] BlockKey_EncryptedKeyValue      = new byte[] { 0x14, 0x6e, 0x0b, 0xe7, 0xab, 0xac, 0xd0, 0xd6 };
    private static readonly byte[] BlockKey_DataIntegrityHmacKey   = new byte[] { 0x5f, 0xb2, 0xad, 0x01, 0x0c, 0xb9, 0xe1, 0xf6 };
    private static readonly byte[] BlockKey_DataIntegrityHmacValue = new byte[] { 0xa0, 0x67, 0x7f, 0x02, 0xb2, 0x2c, 0x84, 0x33 };

    public const int DefaultSpinCount = 100_000;
    public const int SaltSize = 16;       // bytes
    public const int KeySize = 32;        // 256-bit AES
    public const int BlockSize = 16;      // AES block size
    public const int HashSize = 64;       // SHA-512 output

    /// <summary>
    /// Parameters of an agile-encrypted package. Used both for writing (output of Encrypt)
    /// and reading (input to Decrypt).
    /// </summary>
    public sealed class Params
    {
        public byte[] KeySalt = Array.Empty<byte>();
        public byte[] EncryptedKeyValue = Array.Empty<byte>();
        public byte[] VerifierSalt = Array.Empty<byte>();
        public byte[] EncryptedVerifierHashInput = Array.Empty<byte>();
        public byte[] EncryptedVerifierHashValue = Array.Empty<byte>();
        public byte[] EncryptedHmacKey = Array.Empty<byte>();
        public byte[] EncryptedHmacValue = Array.Empty<byte>();
        public int SpinCount = DefaultSpinCount;
        public long DataSize;
    }

    // ---- Encryption ----------------------------------------------------------------

    /// <summary>
    /// Encrypts <paramref name="plaintextPackage"/> (the unencrypted zip bytes of an xlsx) into
    /// <paramref name="output"/>. Returns the parameter block needed to construct the
    /// EncryptionInfo XML stream.
    /// </summary>
    public static Params Encrypt(byte[] plaintextPackage, string password, Stream output, int spinCount = DefaultSpinCount)
    {
        using var ms = new MemoryStream(plaintextPackage, writable: false);
        return Encrypt(ms, plaintextPackage.Length, password, output, spinCount);
    }

    /// <summary>
    /// Stream-based encryption: reads the plaintext package from <paramref name="plaintext"/>
    /// (exactly <paramref name="length"/> bytes from the current position) and writes the
    /// encrypted stream to <paramref name="output"/>. The plaintext is processed in
    /// 4096-byte segments and is never fully resident in memory, and the integrity HMAC is
    /// computed incrementally while writing — <paramref name="output"/> does not need to be
    /// seekable.
    /// </summary>
    public static Params Encrypt(Stream plaintext, long length, string password, Stream output, int spinCount = DefaultSpinCount)
    {
        if (spinCount < 1) throw new ArgumentOutOfRangeException(nameof(spinCount));
        if (length < 0 || length > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(length), "Encrypted packages are limited to 2 GB.");

        // 1. Generate random material.
        var keySalt = RandomBytes(SaltSize);
        var contentKey = RandomBytes(KeySize);
        var verifierSalt = RandomBytes(SaltSize);
        var hmacKey = RandomBytes(HashSize);
        var verifierHashInput = RandomBytes(SaltSize);  // 16-byte verifier secret

        // 2. Encrypt the content key with a key derived from the password.
        //    Per spec, this uses the VERIFIER salt (the salt from the password keyEncryptor element),
        //    NOT the keyData salt. The verifier salt is used for both the password→key derivation
        //    and the IV.
        var encryptedKeyValue = EncryptKeyValue(password, verifierSalt, spinCount, contentKey);

        // 3. Encrypt the verifier blocks (used to validate password without decrypting content).
        var (encVerHashInput, encVerHashValue) = EncryptVerifier(password, verifierSalt, spinCount, verifierHashInput);

        // 4. Stream-encrypt the package, prefixed by an 8-byte little-endian length, while
        //    feeding every written byte into the integrity HMAC ([MS-OFFCRYPTO] §2.3.4.14
        //    HMACs the encrypted stream segment including the length prefix).
        using var hmac = new HMACSHA512(hmacKey);

        Span<byte> prefix = stackalloc byte[8];
        WriteUInt64LEToSpan(prefix, (ulong)length);
        output.Write(prefix);
        hmac.TransformBlock(prefix.ToArray(), 0, 8, null, 0);

        EncryptDataInBlocksStreaming(plaintext, length, contentKey, keySalt, output, hmac);

        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hmacValue = hmac.Hash!;

        var encryptedHmacKey = EncryptHmacKey(contentKey, keySalt, hmacKey);
        var encryptedHmacValue = EncryptHmacValue(contentKey, keySalt, hmacValue);

        return new Params
        {
            KeySalt = keySalt,
            EncryptedKeyValue = encryptedKeyValue,
            VerifierSalt = verifierSalt,
            EncryptedVerifierHashInput = encVerHashInput,
            EncryptedVerifierHashValue = encVerHashValue,
            EncryptedHmacKey = encryptedHmacKey,
            EncryptedHmacValue = encryptedHmacValue,
            SpinCount = spinCount,
            DataSize = length,
        };
    }

    // ---- Decryption ----------------------------------------------------------------

    /// <summary>
    /// Decrypts the encrypted package stream and returns the plaintext bytes (the xlsx zip).
    /// Throws <see cref="UnauthorizedAccessException"/> if the password is wrong.
    /// </summary>
    public static byte[] Decrypt(Stream input, string password, Params p)
    {
        // 1. Verify the password by decrypting the verifier blocks.
        if (!VerifyPassword(password, p))
            throw new UnauthorizedAccessException("Incorrect password.");

        // 2. Re-derive the content key from the password, honoring the file's spin count.
        //    Uses the VERIFIER salt (not key salt) — see EncryptKeyValue for the explanation.
        var contentKey = DecryptKeyValue(password, p.VerifierSalt, p.SpinCount, p.EncryptedKeyValue);

        // 3. Read the encrypted segment (8-byte length prefix + encrypted blocks) so we can
        //    HMAC it exactly as written before decrypting anything.
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        var encryptedSegment = ms.ToArray();
        if (encryptedSegment.Length < 8)
            throw new InvalidDataException("Encrypted package is truncated.");

        // 4. Verify the data-integrity HMAC ([MS-OFFCRYPTO] §2.3.4.14) when the file carries
        //    one. The stored HMAC key/value are AES-CBC encrypted with the content key and
        //    block-key-derived IVs; decrypt both, recompute, and compare in constant time.
        //    A failed check means the ciphertext was tampered with or corrupted in transit.
        if (p.EncryptedHmacKey.Length > 0 && p.EncryptedHmacValue.Length > 0)
        {
            var hmacKey = TrimTo(AesCbcDecrypt(p.EncryptedHmacKey, contentKey, DeriveIv(p.KeySalt, BlockKey_DataIntegrityHmacKey)), HashSize);
            var expected = TrimTo(AesCbcDecrypt(p.EncryptedHmacValue, contentKey, DeriveIv(p.KeySalt, BlockKey_DataIntegrityHmacValue)), HashSize);
            var actual = ComputeHmac(hmacKey, encryptedSegment);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                throw new InvalidDataException(
                    "Encrypted package failed its integrity check (HMAC mismatch). The file is corrupted or has been tampered with.");
        }

        // 5. Read the 8-byte length prefix.
        var declaredLength = (long)ReadUInt64LEFromArray(encryptedSegment);
        if (declaredLength < 0 || declaredLength > int.MaxValue)
            throw new InvalidDataException("Encrypted package declares an unreasonable size.");

        // 6. Decrypt block-by-block (the blocks follow the 8-byte prefix).
        var encryptedBlocks = new byte[encryptedSegment.Length - 8];
        Array.Copy(encryptedSegment, 8, encryptedBlocks, 0, encryptedBlocks.Length);
        return DecryptDataInBlocks(encryptedBlocks, contentKey, p.KeySalt, (int)declaredLength);
    }

    /// <summary>Returns the first <paramref name="len"/> bytes of <paramref name="data"/> (zero-padded if shorter).</summary>
    private static byte[] TrimTo(byte[] data, int len)
    {
        var result = new byte[len];
        Array.Copy(data, 0, result, 0, Math.Min(len, data.Length));
        return result;
    }

    private static ulong ReadUInt64LEFromArray(byte[] b)
        => (ulong)b[0]
            | ((ulong)b[1] << 8)
            | ((ulong)b[2] << 16)
            | ((ulong)b[3] << 24)
            | ((ulong)b[4] << 32)
            | ((ulong)b[5] << 40)
            | ((ulong)b[6] << 48)
            | ((ulong)b[7] << 56);

    /// <summary>Returns true iff the password can decrypt the verifier hash.</summary>
    public static bool VerifyPassword(string password, Params p)
    {
        try
        {
            var verifierKeyForHashInput  = DeriveKey(password, p.VerifierSalt, p.SpinCount, BlockKey_VerifierHashInput);
            var verifierKeyForHashValue  = DeriveKey(password, p.VerifierSalt, p.SpinCount, BlockKey_VerifierHashValue);

            // Decrypt the verifier hash input (a 16-byte random value).
            var hashInput = AesCbcDecrypt(p.EncryptedVerifierHashInput, verifierKeyForHashInput, p.VerifierSalt);
            // hashInput is exactly SaltSize bytes (we encrypted it as such), but after decryption may have padding.
            // Per spec we take the first SaltSize bytes.
            if (hashInput.Length < SaltSize) return false;
            var hashInputTrimmed = new byte[SaltSize];
            Array.Copy(hashInput, 0, hashInputTrimmed, 0, SaltSize);

            // Decrypt the verifier hash value.
            var expectedHashValue = AesCbcDecrypt(p.EncryptedVerifierHashValue, verifierKeyForHashValue, p.VerifierSalt);

            // Compute SHA-512 of the hash input and compare.
            using var sha = SHA512.Create();
            var computed = sha.ComputeHash(hashInputTrimmed);

            // Constant-time compare on the first HashSize bytes.
            if (expectedHashValue.Length < HashSize) return false;
            return CryptographicOperations.FixedTimeEquals(
                new ReadOnlySpan<byte>(computed, 0, HashSize),
                new ReadOnlySpan<byte>(expectedHashValue, 0, HashSize));
        }
        catch
        {
            return false;
        }
    }

    // ---- Internal: key derivation --------------------------------------------------

    /// <summary>
    /// Derives a key per [MS-OFFCRYPTO] §2.3.4.11. The base hash is PBKDF2-style iterated
    /// SHA-512 over salt+password, then one final round mixes in the block key.
    /// </summary>
    private static byte[] DeriveKey(string password, byte[] salt, int spinCount, byte[] blockKey)
    {
        using var sha = SHA512.Create();

        // Step 1: H_0 = SHA-512(salt || password_utf16le)
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var initial = new byte[salt.Length + passwordBytes.Length];
        Buffer.BlockCopy(salt, 0, initial, 0, salt.Length);
        Buffer.BlockCopy(passwordBytes, 0, initial, salt.Length, passwordBytes.Length);
        var hash = sha.ComputeHash(initial);

        // Step 2: H_n = SHA-512(iteration_index_LE || H_{n-1})  for n = 1..spinCount
        //         NOTE THE ORDER: iteration counter goes FIRST, hash second.
        //         This differs from the sheet-protection hash where the order is reversed.
        var buf = new byte[4 + hash.Length];
        for (int i = 0; i < spinCount; i++)
        {
            buf[0] = (byte)(i & 0xFF);
            buf[1] = (byte)((i >> 8) & 0xFF);
            buf[2] = (byte)((i >> 16) & 0xFF);
            buf[3] = (byte)((i >> 24) & 0xFF);
            Buffer.BlockCopy(hash, 0, buf, 4, hash.Length);
            hash = sha.ComputeHash(buf);
        }

        // Step 3: H_final = SHA-512(H_spinCount || blockKey)
        var combined = new byte[hash.Length + blockKey.Length];
        Buffer.BlockCopy(hash, 0, combined, 0, hash.Length);
        Buffer.BlockCopy(blockKey, 0, combined, hash.Length, blockKey.Length);
        var keyMaterial = sha.ComputeHash(combined);

        // Step 4: truncate or pad to KeySize bytes.
        var key = new byte[KeySize];
        var copyLen = Math.Min(KeySize, keyMaterial.Length);
        Array.Copy(keyMaterial, 0, key, 0, copyLen);
        // If we needed more material than the hash provides, pad with 0x36 per spec.
        for (int i = copyLen; i < KeySize; i++) key[i] = 0x36;
        return key;
    }

    // ---- Internal: verifier ------------------------------------------------------

    private static (byte[] encInput, byte[] encValue) EncryptVerifier(string password, byte[] verifierSalt, int spinCount, byte[] hashInput)
    {
        var keyForInput = DeriveKey(password, verifierSalt, spinCount, BlockKey_VerifierHashInput);
        var keyForValue = DeriveKey(password, verifierSalt, spinCount, BlockKey_VerifierHashValue);

        using var sha = SHA512.Create();
        var hashValue = sha.ComputeHash(hashInput);

        var encInput = AesCbcEncrypt(hashInput, keyForInput, verifierSalt);
        var encValue = AesCbcEncrypt(hashValue, keyForValue, verifierSalt);
        return (encInput, encValue);
    }

    // ---- Internal: content-key encryption ----------------------------------------

    /// <summary>
    /// Encrypts the content key using a key derived from the password and the verifier salt.
    /// Per [MS-OFFCRYPTO] §2.3.4.15, the EncryptedKey block uses the salt from the
    /// password keyEncryptor (which is the verifier salt), NOT the keyData salt.
    /// </summary>
    private static byte[] EncryptKeyValue(string password, byte[] verifierSalt, int spinCount, byte[] contentKey)
    {
        var key = DeriveKey(password, verifierSalt, spinCount, BlockKey_EncryptedKeyValue);
        return AesCbcEncrypt(contentKey, key, verifierSalt);
    }

    private static byte[] DecryptKeyValue(string password, byte[] verifierSalt, int spinCount, byte[] encryptedContentKey)
    {
        var key = DeriveKey(password, verifierSalt, spinCount, BlockKey_EncryptedKeyValue);
        var result = AesCbcDecrypt(encryptedContentKey, key, verifierSalt);
        // Trim/pad to KeySize.
        var contentKey = new byte[KeySize];
        var copyLen = Math.Min(KeySize, result.Length);
        Array.Copy(result, 0, contentKey, 0, copyLen);
        return contentKey;
    }

    // ---- Internal: HMAC over encrypted data --------------------------------------

    private static byte[] EncryptHmacKey(byte[] contentKey, byte[] keySalt, byte[] hmacKey)
    {
        // Derive an IV from keySalt + block key, then AES-CBC encrypt the hmacKey.
        var iv = DeriveIv(keySalt, BlockKey_DataIntegrityHmacKey);
        return AesCbcEncrypt(hmacKey, contentKey, iv);
    }

    private static byte[] EncryptHmacValue(byte[] contentKey, byte[] keySalt, byte[] hmacValue)
    {
        var iv = DeriveIv(keySalt, BlockKey_DataIntegrityHmacValue);
        return AesCbcEncrypt(hmacValue, contentKey, iv);
    }

    private static byte[] ComputeHmac(byte[] hmacKey, byte[] data)
    {
        using var hmac = new HMACSHA512(hmacKey);
        return hmac.ComputeHash(data);
    }

    /// <summary>Derives an IV by SHA-512(salt || blockKey), truncated to BlockSize.</summary>
    private static byte[] DeriveIv(byte[] salt, byte[] blockKey)
    {
        using var sha = SHA512.Create();
        var combined = new byte[salt.Length + blockKey.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(blockKey, 0, combined, salt.Length, blockKey.Length);
        var hash = sha.ComputeHash(combined);
        var iv = new byte[BlockSize];
        Array.Copy(hash, 0, iv, 0, BlockSize);
        return iv;
    }

    // ---- Internal: block-by-block content encryption -----------------------------

    /// <summary>
    /// Encrypts the package in 4096-byte segments read from a stream. Each segment is
    /// AES-CBC encrypted with a per-segment IV (SHA-512(keySalt || segmentIndex_LE),
    /// truncated to 16 bytes), written to <paramref name="output"/>, and fed into
    /// <paramref name="hmac"/> so the integrity hash is computed without buffering or
    /// re-reading the encrypted data.
    /// </summary>
    private static void EncryptDataInBlocksStreaming(
        Stream plaintext, long length, byte[] contentKey, byte[] keySalt, Stream output, HMACSHA512 hmac)
    {
        const int SegmentSize = 4096;
        using var sha = SHA512.Create();
        var chunk = new byte[SegmentSize];
        int segmentIndex = 0;
        long remaining = length;

        while (remaining > 0)
        {
            int chunkLen = (int)Math.Min(SegmentSize, remaining);
            plaintext.ReadExactly(chunk, 0, chunkLen);

            // Pad the chunk to multiple of BlockSize. Per spec, padding is zero bytes (NOT PKCS7).
            int paddedLen = (chunkLen + BlockSize - 1) / BlockSize * BlockSize;
            var paddedChunk = new byte[paddedLen];
            Array.Copy(chunk, 0, paddedChunk, 0, chunkLen);

            var iv = ComputeSegmentIv(sha, keySalt, segmentIndex);
            var encChunk = AesCbcEncryptNoPadding(paddedChunk, contentKey, iv);
            output.Write(encChunk, 0, encChunk.Length);
            hmac.TransformBlock(encChunk, 0, encChunk.Length, null, 0);

            remaining -= chunkLen;
            segmentIndex++;
        }
    }

    private static byte[] DecryptDataInBlocks(byte[] encryptedBlocks, byte[] contentKey, byte[] keySalt, int totalPlaintextSize)
    {
        const int SegmentSize = 4096;
        using var sha = SHA512.Create();
        var output = new byte[totalPlaintextSize];

        int segmentIndex = 0;
        int encOffset = 0;
        int plainOffset = 0;

        while (plainOffset < totalPlaintextSize)
        {
            int chunkLen = Math.Min(SegmentSize, totalPlaintextSize - plainOffset);
            int paddedLen = (chunkLen + BlockSize - 1) / BlockSize * BlockSize;

            if (encOffset + paddedLen > encryptedBlocks.Length)
                throw new InvalidDataException("Encrypted package is shorter than expected.");

            var encChunk = new byte[paddedLen];
            Array.Copy(encryptedBlocks, encOffset, encChunk, 0, paddedLen);

            var iv = ComputeSegmentIv(sha, keySalt, segmentIndex);
            var paddedPlain = AesCbcDecryptNoPadding(encChunk, contentKey, iv);

            Array.Copy(paddedPlain, 0, output, plainOffset, chunkLen);

            plainOffset += chunkLen;
            encOffset += paddedLen;
            segmentIndex++;
        }

        return output;
    }

    private static byte[] ComputeSegmentIv(SHA512 sha, byte[] keySalt, int segmentIndex)
    {
        var buf = new byte[keySalt.Length + 4];
        Buffer.BlockCopy(keySalt, 0, buf, 0, keySalt.Length);
        buf[keySalt.Length + 0] = (byte)(segmentIndex & 0xFF);
        buf[keySalt.Length + 1] = (byte)((segmentIndex >> 8) & 0xFF);
        buf[keySalt.Length + 2] = (byte)((segmentIndex >> 16) & 0xFF);
        buf[keySalt.Length + 3] = (byte)((segmentIndex >> 24) & 0xFF);
        var hash = sha.ComputeHash(buf);
        var iv = new byte[BlockSize];
        Array.Copy(hash, 0, iv, 0, BlockSize);
        return iv;
    }

    // ---- AES helpers ---------------------------------------------------------------

    /// <summary>
    /// AES-256-CBC encrypt with NO padding (per [MS-OFFCRYPTO] §2.3.4.13-15).
    /// Input must be a multiple of BlockSize (16 bytes); we zero-pad to that boundary.
    /// PKCS7 would add a padding block at the end, breaking strict spec implementations.
    /// </summary>
    private static byte[] AesCbcEncrypt(byte[] data, byte[] key, byte[] iv)
    {
        // Zero-pad input to block boundary if not already aligned.
        int paddedLen = (data.Length + BlockSize - 1) / BlockSize * BlockSize;
        var aligned = data;
        if (paddedLen != data.Length)
        {
            aligned = new byte[paddedLen];
            Array.Copy(data, 0, aligned, 0, data.Length);
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.KeySize = 256;
        aes.Key = key;
        var paddedIv = new byte[BlockSize];
        Array.Copy(iv, 0, paddedIv, 0, Math.Min(iv.Length, BlockSize));
        aes.IV = paddedIv;
        return aes.EncryptCbc(aligned, paddedIv, PaddingMode.None);
    }

    /// <summary>
    /// AES-256-CBC decrypt with NO padding (per spec). Caller takes the first N bytes
    /// of the result according to the expected output size.
    /// </summary>
    private static byte[] AesCbcDecrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.KeySize = 256;
        aes.Key = key;
        var paddedIv = new byte[BlockSize];
        Array.Copy(iv, 0, paddedIv, 0, Math.Min(iv.Length, BlockSize));
        aes.IV = paddedIv;
        return aes.DecryptCbc(data, paddedIv, PaddingMode.None);
    }

    private static byte[] AesCbcEncryptNoPadding(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.KeySize = 256;
        aes.Key = key;
        aes.IV = iv;
        return aes.EncryptCbc(data, iv, PaddingMode.None);
    }

    private static byte[] AesCbcDecryptNoPadding(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.KeySize = 256;
        aes.Key = key;
        aes.IV = iv;
        return aes.DecryptCbc(data, iv, PaddingMode.None);
    }

    // ---- Misc ---------------------------------------------------------------------

    private static byte[] RandomBytes(int length)
    {
        var b = new byte[length];
        RandomNumberGenerator.Fill(b);
        return b;
    }

    private static void WriteUInt64LEToSpan(Span<byte> b, ulong value)
    {
        b[0] = (byte)(value & 0xFF);
        b[1] = (byte)((value >> 8) & 0xFF);
        b[2] = (byte)((value >> 16) & 0xFF);
        b[3] = (byte)((value >> 24) & 0xFF);
        b[4] = (byte)((value >> 32) & 0xFF);
        b[5] = (byte)((value >> 40) & 0xFF);
        b[6] = (byte)((value >> 48) & 0xFF);
        b[7] = (byte)((value >> 56) & 0xFF);
    }

    private static ulong ReadUInt64LE(Stream s)
    {
        Span<byte> b = stackalloc byte[8];
        s.ReadExactly(b);
        return (ulong)b[0]
            | ((ulong)b[1] << 8)
            | ((ulong)b[2] << 16)
            | ((ulong)b[3] << 24)
            | ((ulong)b[4] << 32)
            | ((ulong)b[5] << 40)
            | ((ulong)b[6] << 48)
            | ((ulong)b[7] << 56);
    }
}
