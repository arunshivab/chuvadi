using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Chuvadi.Zip.Internal;

/// <summary>WinZip AES (AE-1 / AE-2) parameters and key derivation.</summary>
internal static class ZipAes
{
    public const int MacLength = 10;
    public const int VerifierLength = 2;
    public const int Pbkdf2Iterations = 1000;

    public static int KeyBytes(int strength) => strength switch
    {
        1 => 16,
        2 => 24,
        3 => 32,
        _ => throw new ZipUnsupportedException($"Unsupported AES strength code {strength}."),
    };

    public static int SaltBytes(int strength) => KeyBytes(strength) / 2;

    public static int StrengthFor(ZipEncryption e) => e switch
    {
        ZipEncryption.Aes128 => 1,
        ZipEncryption.Aes192 => 2,
        ZipEncryption.Aes256 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    public static ZipEncryption EncryptionFor(int strength) => strength switch
    {
        1 => ZipEncryption.Aes128,
        2 => ZipEncryption.Aes192,
        3 => ZipEncryption.Aes256,
        _ => throw new ZipUnsupportedException($"Unsupported AES strength code {strength}."),
    };

    /// <summary>Derives (encryption key, authentication key, 2-byte password verifier).</summary>
    public static void Derive(byte[] password, byte[] salt, int keyBytes,
        out byte[] encKey, out byte[] authKey, out byte[] verifier)
    {
        byte[] okm = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations,
            HashAlgorithmName.SHA1, 2 * keyBytes + VerifierLength);
        encKey = okm.AsSpan(0, keyBytes).ToArray();
        authKey = okm.AsSpan(keyBytes, keyBytes).ToArray();
        verifier = okm.AsSpan(2 * keyBytes, VerifierLength).ToArray();
        CryptographicOperations.ZeroMemory(okm);
    }
}

/// <summary>
/// AES in the WinZip CTR variant: 128-bit little-endian counter starting at 1, keystream =
/// AES-ECB(counter). The stream is symmetric (encrypt == decrypt) and position-continuous.
/// </summary>
internal sealed class AesCtr : IDisposable
{
    private const int Blocks = 256;                    // 4 KiB of keystream per refill
    private readonly Aes _aes;
    private readonly byte[] _counters = new byte[Blocks * 16];
    private readonly byte[] _keystream = new byte[Blocks * 16];
    private int _ksPos = Blocks * 16;
    private ulong _counter = 1;

    public AesCtr(byte[] key)
    {
        _aes = Aes.Create();
        _aes.Key = key;
    }

    private void Refill()
    {
        for (int b = 0; b < Blocks; b++)
        {
            var block = _counters.AsSpan(b * 16, 16);
            block.Clear();
            BinaryPrimitives.WriteUInt64LittleEndian(block, _counter++);
        }
        _aes.EncryptEcb(_counters, _keystream, PaddingMode.None);
        _ksPos = 0;
    }

    public void Transform(Span<byte> data)
    {
        int i = 0;
        while (i < data.Length)
        {
            if (_ksPos == _keystream.Length) Refill();
            int n = Math.Min(data.Length - i, _keystream.Length - _ksPos);
            for (int j = 0; j < n; j++) data[i + j] ^= _keystream[_ksPos + j];
            _ksPos += n;
            i += n;
        }
    }

    public void Dispose()
    {
        _aes.Dispose();
        CryptographicOperations.ZeroMemory(_keystream);
    }
}
