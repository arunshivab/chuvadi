using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Zip;

/// <summary>One-liner conveniences over <see cref="ZipWriter"/> and <see cref="ZipReader"/>.</summary>
public static class ZipExtensions
{
    /// <summary>Zips a directory tree into <paramref name="outputZipPath"/>.</summary>
    public static void ZipDirectory(this string sourceDirectory, string outputZipPath, ZipDirectoryOptions? options = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(outputZipPath)) throw new ArgumentException("Output path required.", nameof(outputZipPath));
        options ??= new ZipDirectoryOptions();
        using var zip = ZipWriter.Create(outputZipPath, options.Writer);
        zip.AddDirectoryTree(sourceDirectory, options, ct);
    }

    /// <summary>Async version of <see cref="ZipDirectory"/>.</summary>
    public static async Task ZipDirectoryAsync(this string sourceDirectory, string outputZipPath, ZipDirectoryOptions? options = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(outputZipPath)) throw new ArgumentException("Output path required.", nameof(outputZipPath));
        options ??= new ZipDirectoryOptions();
        await using var zip = ZipWriter.Create(outputZipPath, options.Writer);
        await zip.AddDirectoryTreeAsync(sourceDirectory, options, ct).ConfigureAwait(false);
    }

    /// <summary>Zips a directory tree into a byte array (in memory).</summary>
    public static byte[] ZipDirectoryToBytes(this string sourceDirectory, ZipDirectoryOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ZipDirectoryOptions();
        return ZipWriter.ToBytes(w => w.AddDirectoryTree(sourceDirectory, options, ct), options.Writer);
    }

    /// <summary>Zips files; entry names are the file names.</summary>
    public static void ToZip(this IEnumerable<string> files, string outputZipPath, ZipWriterOptions? options = null, CancellationToken ct = default)
    {
        if (files is null) throw new ArgumentNullException(nameof(files));
        if (string.IsNullOrEmpty(outputZipPath)) throw new ArgumentException("Output path required.", nameof(outputZipPath));
        using var zip = ZipWriter.Create(outputZipPath, options);
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            zip.AddFile(Path.GetFileName(f), f);
        }
    }

    /// <summary>Async version of <see cref="ToZip"/>.</summary>
    public static async Task ToZipAsync(this IEnumerable<string> files, string outputZipPath, ZipWriterOptions? options = null, CancellationToken ct = default)
    {
        if (files is null) throw new ArgumentNullException(nameof(files));
        if (string.IsNullOrEmpty(outputZipPath)) throw new ArgumentException("Output path required.", nameof(outputZipPath));
        await using var zip = ZipWriter.Create(outputZipPath, options);
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            await zip.AddFileAsync(Path.GetFileName(f), f, null, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Extracts a zip file to a directory (zip-slip protected).</summary>
    public static ZipExtractionResult ExtractTo(this string zipPath, string targetDirectory,
        ZipExtractOptions? options = null, ZipReaderOptions? readerOptions = null)
    {
        using var r = ZipReader.Open(zipPath, readerOptions);
        return r.ExtractTo(targetDirectory, options);
    }

    /// <summary>Extracts in-memory zip data to a directory.</summary>
    public static ZipExtractionResult ExtractTo(this byte[] zipData, string targetDirectory,
        ZipExtractOptions? options = null, ZipReaderOptions? readerOptions = null)
    {
        using var r = ZipReader.Open(zipData, readerOptions);
        return r.ExtractTo(targetDirectory, options);
    }

    /// <summary>Async extraction of a zip file.</summary>
    public static async Task<ZipExtractionResult> ExtractToAsync(this string zipPath, string targetDirectory,
        ZipExtractOptions? options = null, ZipReaderOptions? readerOptions = null, CancellationToken ct = default)
    {
        using var r = ZipReader.Open(zipPath, readerOptions);
        return await r.ExtractToAsync(targetDirectory, options, ct).ConfigureAwait(false);
    }

    /// <summary>Lists the entries of a zip file without extracting.</summary>
    public static IReadOnlyList<ZipEntryInfo> ListZipEntries(this string zipPath, ZipReaderOptions? readerOptions = null)
    {
        using var r = ZipReader.Open(zipPath, readerOptions);
        return new List<ZipEntryInfo>(r.Entries);
    }

    /// <summary>Lists the entries of in-memory zip data.</summary>
    public static IReadOnlyList<ZipEntryInfo> ListZipEntries(this byte[] zipData, ZipReaderOptions? readerOptions = null)
    {
        using var r = ZipReader.Open(zipData, readerOptions);
        return new List<ZipEntryInfo>(r.Entries);
    }

    /// <summary>Reads one entry of a zip file fully into memory. Returns null if absent.</summary>
    public static byte[]? ReadZipEntry(this string zipPath, string entryName, string? password = null, ZipReaderOptions? readerOptions = null)
    {
        using var r = ZipReader.Open(zipPath, readerOptions);
        var e = r.FindEntry(entryName);
        return e is null ? null : r.ReadAllBytes(e, password);
    }
}
