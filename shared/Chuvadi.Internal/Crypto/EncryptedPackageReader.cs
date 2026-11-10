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
    /// <summary>
    /// True when <paramref name="input"/> is an encrypted Office Open XML file: a compound file (CFB) whose directory
    /// holds both an <c>EncryptionInfo</c> and an <c>EncryptedPackage</c> stream. False for anything that is not a
    /// compound file (a normal zip package, or garbage: the caller's zip reader reports that).
    /// <para>
    /// A compound file without those two streams is an old binary Office file (Word 97-2003 .doc, Excel 97-2003 .xls,
    /// PowerPoint 97-2003 .ppt) or some other OLE file (or an encrypted file that lost one of its two streams). It is not encrypted and not readable here, so this throws an
    /// <see cref="InvalidDataException"/> that names the legacy format; the callers already report any
    /// <see cref="InvalidDataException"/> from reading the package as their own format exception. Only the directory
    /// is read (no stream body), so the cost does not depend on the size of the streams. If the directory itself
    /// cannot be read, the old answer stays: true, so a damaged encrypted container reaches the decryptor.
    /// </para>
    /// The stream position is restored.
    /// </summary>
    public static bool IsEncryptedPackage(Stream input)
    {
        long pos = input.Position;
        try
        {
            Span<byte> sig = stackalloc byte[8];
            // Use ReadAtLeast — handles short reads on slow streams (network shares etc.).
            int read = input.ReadAtLeast(sig, 8, throwOnEndOfStream: false);
            if (read < 8) return false;
            bool cfb = sig[0] == 0xD0 && sig[1] == 0xCF && sig[2] == 0x11 && sig[3] == 0xE0
                && sig[4] == 0xA1 && sig[5] == 0xB1 && sig[6] == 0x1A && sig[7] == 0xE1;
            if (!cfb) return false;

            HashSet<string> names;
            try
            {
                names = CfbContainer.ListStreamNames(input);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException
                or ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                return true;   // Directory unreadable: let the decryptor report the damaged container.
            }

            if (names.Contains("EncryptionInfo") && names.Contains("EncryptedPackage")) return true;
            throw new InvalidDataException(LegacyFormatMessage(names));
        }
        finally
        {
            input.Position = pos;
        }
    }

    private static string LegacyFormatMessage(HashSet<string> names)
    {
        // One of the two encryption streams without the other: a damaged encrypted file, not a legacy one.
        bool hasInfo = names.Contains("EncryptionInfo");
        if (hasInfo || names.Contains("EncryptedPackage"))
            return "The file looks like an encrypted Office file but its " + (hasInfo ? "EncryptedPackage" : "EncryptionInfo")
                + " stream is missing, so it cannot be read.";
        string kind =
            names.Contains("WordDocument") ? "a Word 97-2003 binary file (.doc)" :
            names.Contains("Workbook") || names.Contains("Book") ? "an Excel 97-2003 binary file (.xls)" :
            names.Contains("PowerPoint Document") ? "a PowerPoint 97-2003 binary file (.ppt)" :
            "an old binary Office file (.doc, .xls or .ppt) or another OLE compound file";
        return "This is " + kind + ", not an Office Open XML package, and it is not encrypted. "
            + "Open it in Office and save it as .docx or .xlsx.";
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
