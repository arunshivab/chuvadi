using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

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

/// <summary>Which decryption limit a <see cref="PackageLimitException"/> reports.</summary>
internal enum PackageLimit
{
    /// <summary>The file's declared key-derivation spin count exceeds the caller's cap.</summary>
    SpinCount,
}

/// <summary>
/// Thrown by the shared crypto plumbing when an encrypted file exceeds a caller-supplied
/// limit, BEFORE any expensive work starts. Like <see cref="PackagePasswordException"/>, each
/// format package translates it into its own public exception type.
/// </summary>
internal sealed class PackageLimitException : Exception
{
    public PackageLimit Limit { get; }
    public long Actual { get; }
    public long Allowed { get; }

    public PackageLimitException(PackageLimit limit, long actual, long allowed)
        : base($"Encrypted file exceeds the {limit} limit: {actual:N0} > {allowed:N0}.")
    {
        Limit = limit;
        Actual = actual;
        Allowed = allowed;
    }
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
        => DecryptToPlaintextPackage(cfbInput, password, maxSpinCount: null, CancellationToken.None);

    /// <summary>
    /// As <see cref="DecryptToPlaintextPackage(Stream, string?)"/>, with a spin-count cap and
    /// cancellation. When <paramref name="maxSpinCount"/> is set and the file declares more,
    /// <see cref="PackageLimitException"/> is thrown after EncryptionInfo is parsed and before
    /// any key derivation (a hostile spin count is otherwise an uninterruptible CPU stall).
    /// <paramref name="cancellationToken"/> is observed inside the key-derivation loop;
    /// cancellation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static byte[] DecryptToPlaintextPackage(Stream cfbInput, string? password, int? maxSpinCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        // 2b. Caller's cap on the declared spin count — checked before any derivation.
        if (maxSpinCount is int maxSpin && (encParams.SpinCount > maxSpin || encParams.SpinCount < 0))
            throw new PackageLimitException(PackageLimit.SpinCount, encParams.SpinCount, maxSpin);

        // 3. Decrypt.
        try
        {
            using var pkgMs = new MemoryStream(encPackageBytes);
            return AgileEncryption.Decrypt(pkgMs, password, encParams, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            throw new PackagePasswordException("Incorrect password.");
        }
    }
}
