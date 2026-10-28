using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Chuvadi.Docs.Word;
using Chuvadi.Internal.Crypto;

namespace Chuvadi.Docs.Internal;

/// <summary>What the pre-open scan learned about the zip container.</summary>
internal sealed class PackageScanResult
{
    public required IReadOnlyList<string> EntryNames { get; init; }
    public bool ContainsVbaProject { get; init; }
    public bool HasActiveXParts { get; init; }
    public bool HasEmbeddedObjects { get; init; }
}

/// <summary>
/// Pre-open checks applied to untrusted docx input, all Docs-local (no change to the shared
/// OOXML internals):
/// <list type="bullet">
/// <item>zip container: entry count, total declared uncompressed size, per-entry compression
/// ratio — read from the central directory without inflating anything. Declared sizes are
/// a hard ceiling because <see cref="ZipArchive"/> never inflates an entry past its declared
/// length (verified empirically on .NET 10), so a forged header cannot smuggle extra bytes;</item>
/// <item>encrypted container: decryption through the shared decryptor with the spin-count
/// cap and a cancellable key-derivation loop (see <see cref="Decrypt"/>).</item>
/// </list>
/// </summary>
internal static class PackageGuard
{
    /// <summary>
    /// Returns a seekable stream over <paramref name="input"/>, enforcing
    /// <see cref="DocxReaderOptions.MaxPackageBytes"/> on the input size. Seekable inputs are
    /// returned as-is (their remaining length is checked); others are buffered, cancellably
    /// and with the same cap, into a <see cref="MemoryStream"/> handed back via
    /// <paramref name="buffered"/> for the caller to dispose.
    /// </summary>
    public static Stream EnsureSeekable(Stream input, DocxReaderOptions o, CancellationToken ct, out MemoryStream? buffered)
    {
        buffered = null;
        if (input.CanSeek)
        {
            long remaining = input.Length - input.Position;
            if (o.MaxPackageBytes is long maxIn && remaining > maxIn)
                throw new DocxSecurityLimitException(DocxLimit.PackageBytes, remaining, maxIn);
            return input;
        }
        buffered = CopyBounded(input, o, ct);
        return buffered;
    }

    /// <summary>Copies a stream into memory, cancellably, refusing more than MaxPackageBytes.</summary>
    public static MemoryStream CopyBounded(Stream input, DocxReaderOptions o, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = input.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            ms.Write(buf, 0, n);
            if (o.MaxPackageBytes is long maxIn && ms.Length > maxIn)
            {
                long seen = ms.Length;
                ms.Dispose();
                throw new DocxSecurityLimitException(DocxLimit.PackageBytes, seen, maxIn);
            }
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Scans the zip central directory of a seekable package stream and enforces the
    /// container limits. The stream position is restored afterwards. Throws
    /// <see cref="DocxSecurityLimitException"/> on a breach; lets <see cref="InvalidDataException"/>
    /// from a non-zip input propagate for the caller to translate.
    /// </summary>
    public static PackageScanResult Scan(Stream package, DocxReaderOptions o, CancellationToken ct)
    {
        long start = package.Position;
        try
        {
            using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            var entries = zip.Entries;

            if (o.MaxEntries is int maxEntries && entries.Count > maxEntries)
                throw new DocxSecurityLimitException(DocxLimit.EntryCount, entries.Count, maxEntries);

            var names = new List<string>(entries.Count);
            long total = 0;
            bool vba = false, activeX = false, embeddings = false;

            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                names.Add(e.FullName);

                long len = e.Length;
                total += len;
                if (o.MaxTotalUncompressedBytes is long maxTotal && total > maxTotal)
                    throw new DocxSecurityLimitException(DocxLimit.TotalUncompressedBytes, total, maxTotal);

                if (o.MaxCompressionRatio is int maxRatio && len >= o.CompressionRatioThresholdBytes)
                {
                    long compressed = e.CompressedLength;
                    long ratio = compressed <= 0 ? long.MaxValue : len / compressed;
                    if (ratio > maxRatio)
                        throw new DocxSecurityLimitException(DocxLimit.CompressionRatio, ratio, maxRatio, e.FullName);
                }

                var name = e.FullName;
                if (name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)) vba = true;
                if (name.StartsWith("word/activeX/", StringComparison.OrdinalIgnoreCase)) activeX = true;
                if (name.StartsWith("word/embeddings/", StringComparison.OrdinalIgnoreCase)) embeddings = true;
            }

            return new PackageScanResult
            {
                EntryNames = names,
                ContainsVbaProject = vba,
                HasActiveXParts = activeX,
                HasEmbeddedObjects = embeddings,
            };
        }
        finally
        {
            package.Position = start;
        }
    }

    /// <summary>
    /// Decrypts an ENCRYPTED (CFB) input through the shared decryptor with the spin-count cap
    /// (<see cref="DocxReaderOptions.MaxSpinCount"/>, checked before any key derivation) and
    /// cancellation observed inside the key-derivation loop, translating the shared internal
    /// exceptions into Chuvadi.Docs' public ones.
    /// </summary>
    public static byte[] Decrypt(Stream cfb, string? password, DocxReaderOptions o, CancellationToken ct)
    {
        try
        {
            return EncryptedPackageReader.DecryptToPlaintextPackage(cfb, password, o.MaxSpinCount, ct);
        }
        catch (PackageLimitException ex) when (ex.Limit == PackageLimit.SpinCount)
        {
            throw new DocxSecurityLimitException(DocxLimit.SpinCount, ex.Actual, ex.Allowed);
        }
        catch (PackagePasswordException ex)
        {
            throw new DocxPasswordRequiredException(ex.Message, ex);
        }
    }
}
