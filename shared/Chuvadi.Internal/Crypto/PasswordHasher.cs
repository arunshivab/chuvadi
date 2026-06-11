using System;
using System.Security.Cryptography;
using System.Text;

namespace Chuvadi.Internal.Crypto;

/// <summary>
/// Implements Excel's password hash for sheet/workbook protection. Per [MS-OFFCRYPTO] §2.4.2.4:
///
///   H_0 = H(salt || password_utf16le)
///   H_n = H(H_{n-1} || (n-1 as little-endian uint32))   for n = 1..iterations
///   hashValue = H_iterations
///
/// Where H is SHA-512 by default. The salt, iterations, and hashValue are stored as
/// base64 in the &lt;sheetProtection&gt;/&lt;workbookProtection&gt; element along with
/// algorithmName="SHA-512".
///
/// This is DIFFERENT from agile encryption's key derivation (which uses PBKDF2 in a
/// specific block-encryption scheme). Sheet/workbook protection uses this simpler iterated
/// hash because the goal is verification, not key derivation.
/// </summary>
internal static class PasswordHasher
{
    /// <summary>Default iteration count Excel writes when protecting a sheet (matches Excel 2016+).</summary>
    public const int DefaultSpinCount = 100_000;

    /// <summary>Default salt length in bytes.</summary>
    public const int DefaultSaltLength = 16;

    /// <summary>Generates a random salt of the requested length.</summary>
    public static byte[] GenerateSalt(int length = DefaultSaltLength)
    {
        var salt = new byte[length];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    /// <summary>
    /// Computes the iterated SHA-512 hash matching Excel's sheet/workbook protection scheme.
    /// Returns the base64-encoded hash value suitable for the <c>hashValue</c> attribute.
    /// </summary>
    public static string ComputeHashBase64(string password, byte[] salt, int spinCount)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));
        if (salt is null) throw new ArgumentNullException(nameof(salt));
        if (spinCount < 1) throw new ArgumentOutOfRangeException(nameof(spinCount));

        // Password is encoded as UTF-16 LE (Excel's convention).
        var passwordBytes = Encoding.Unicode.GetBytes(password);

        using var sha = SHA512.Create();

        // H_0 = SHA-512(salt || password)
        var combined = new byte[salt.Length + passwordBytes.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(passwordBytes, 0, combined, salt.Length, passwordBytes.Length);
        var hash = sha.ComputeHash(combined);

        // H_n = SHA-512(H_{n-1} || iteration_index_as_uint32_le)  for n = 1..spinCount
        var buffer = new byte[hash.Length + 4];
        for (int i = 0; i < spinCount; i++)
        {
            Buffer.BlockCopy(hash, 0, buffer, 0, hash.Length);
            buffer[hash.Length + 0] = (byte)(i & 0xFF);
            buffer[hash.Length + 1] = (byte)((i >> 8) & 0xFF);
            buffer[hash.Length + 2] = (byte)((i >> 16) & 0xFF);
            buffer[hash.Length + 3] = (byte)((i >> 24) & 0xFF);
            hash = sha.ComputeHash(buffer);
        }

        return Convert.ToBase64String(hash);
    }
}
