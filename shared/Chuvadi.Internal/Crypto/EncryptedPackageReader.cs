using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Internal.Crypto;

/// <summary>
/// Thrown by the shared crypto plumbing when an encrypted package needs a password or the
/// supplied password is wrong. Each format package catches this and wraps it in its own
/// public exception type (PackagePasswordException, DocxPasswordRequiredException, ...)
/// so the shared internals never reference package-specific public surface.
/// </summary>
internal sealed class PackagePasswordException : Exception
{
    public PackagePasswordException(string message) : base(message) { }
    public PackagePasswordException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Reads a CFB-wrapped encrypted OOXML file (xlsx/docx), decrypts the package using the
/// supplied password, and returns the plaintext zip bytes.
/// </summary>
internal static class EncryptedPackageReader
{
    /// <summary>Sniffs the first 8 bytes of <paramref name="input"/>. Returns true if it's a CFB file.</summary>
    public static bool IsEncryptedPackage(Stream input)
    {
        long pos = input.Position;
        try
        {
            Span<byte> sig = stackalloc byte[8];
            // Use ReadAtLeast — handles short reads on slow streams (network shares etc.).
            int read = input.ReadAtLeast(sig, 8, throwOnEndOfStream: false);
            if (read < 8) return false;
            return sig[0] == 0xD0 && sig[1] == 0xCF && sig[2] == 0x11 && sig[3] == 0xE0
                && sig[4] == 0xA1 && sig[5] == 0xB1 && sig[6] == 0x1A && sig[7] == 0xE1;
        }
        finally
        {
            input.Position = pos;
        }
    }

    /// <summary>
    /// Decrypts an encrypted OOXML file into a plaintext byte array (the inner zip).
    /// Throws <see cref="PackagePasswordException"/> if the password is missing or wrong.
    /// </summary>
    public static byte[] DecryptToPlaintextPackage(Stream cfbInput, string? password)
    {
        if (string.IsNullOrEmpty(password))
            throw new PackagePasswordException(
                "This file is encrypted; supply a password to read it.");

        // 1. Read the CFB container.
        Dictionary<string, byte[]> streams;
        try
        {
            streams = CfbContainer.Read(cfbInput);
        }
        catch (Exception ex)
        {
            throw new PackagePasswordException(
                "File appears encrypted but the CFB container could not be parsed.", ex);
        }

        if (!streams.TryGetValue("EncryptionInfo", out var encInfoBytes))
            throw new PackagePasswordException("Encrypted file is missing the EncryptionInfo stream.");
        if (!streams.TryGetValue("EncryptedPackage", out var encPackageBytes))
            throw new PackagePasswordException("Encrypted file is missing the EncryptedPackage stream.");

        // 2. Parse EncryptionInfo.
        AgileEncryption.Params encParams;
        using (var ms = new MemoryStream(encInfoBytes))
        {
            encParams = EncryptionInfoXml.Read(ms);
        }

        // 3. Decrypt.
        try
        {
            using var pkgMs = new MemoryStream(encPackageBytes);
            return AgileEncryption.Decrypt(pkgMs, password, encParams);
        }
        catch (UnauthorizedAccessException)
        {
            throw new PackagePasswordException("Incorrect password.");
        }
    }
}
