using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Sheets.Zip;

/// <summary>
/// One-liner zip extension methods for common cases. Thin convenience wrappers over
/// <see cref="ZipWriter"/> and <see cref="ZipReader"/>.
/// </summary>
public static class ZipExtensions
{
    // ---- Zip a directory ----------------------------------------------------------

    /// <summary>
    /// Zips an entire directory tree to <paramref name="outputZipPath"/>. Entries are named
    /// relative to <paramref name="sourceDirectory"/>, using forward slashes.
    /// </summary>
    public static void ZipDirectory(this string sourceDirectory, string outputZipPath,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        if (string.IsNullOrEmpty(sourceDirectory)) throw new ArgumentException("Source directory required.", nameof(sourceDirectory));
        if (string.IsNullOrEmpty(outputZipPath)) throw new ArgumentException("Output path required.", nameof(outputZipPath));
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);

        var rootFull = Path.GetFullPath(sourceDirectory);
        using var zip = ZipWriter.Create(outputZipPath, level);

        foreach (var filePath in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(rootFull, filePath).Replace('\\', '/');
            zip.AddFile(relative, filePath);
        }
    }

    public static async Task ZipDirectoryAsync(this string sourceDirectory, string outputZipPath,
        CompressionLevel level = CompressionLevel.Optimal, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sourceDirectory)) throw new ArgumentException("Source directory required.", nameof(sourceDirectory));
        if (string.IsNullOrEmpty(outputZipPath)) throw new ArgumentException("Output path required.", nameof(outputZipPath));
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);

        var rootFull = Path.GetFullPath(sourceDirectory);
        await using var zip = ZipWriter.Create(outputZipPath, level);

        foreach (var filePath in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(rootFull, filePath).Replace('\\', '/');
            await zip.AddFileAsync(relative, filePath, ct).ConfigureAwait(false);
        }
    }

    // ---- Zip a list of files ------------------------------------------------------

    /// <summary>
    /// Zips a list of files. Entry names are the file basenames (e.g. "/a/b/x.pdf" becomes "x.pdf").
    /// For full control over entry names, use <see cref="ZipWriter"/> directly.
    /// </summary>
    public static void ToZip(this IEnumerable<string> files, string outputZipPath,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        if (files is null) throw new ArgumentNullException(nameof(files));
        if (string.IsNullOrEmpty(outputZipPath)) throw new ArgumentException("Output path required.", nameof(outputZipPath));

        using var zip = ZipWriter.Create(outputZipPath, level);
        foreach (var f in files)
        {
            if (!File.Exists(f)) throw new FileNotFoundException("File to zip not found.", f);
            zip.AddFile(Path.GetFileName(f), f);
        }
    }

    public static async Task ToZipAsync(this IEnumerable<string> files, string outputZipPath,
        CompressionLevel level = CompressionLevel.Optimal, CancellationToken ct = default)
    {
        if (files is null) throw new ArgumentNullException(nameof(files));
        if (string.IsNullOrEmpty(outputZipPath)) throw new ArgumentException("Output path required.", nameof(outputZipPath));

        await using var zip = ZipWriter.Create(outputZipPath, level);
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(f)) throw new FileNotFoundException("File to zip not found.", f);
            await zip.AddFileAsync(Path.GetFileName(f), f, ct).ConfigureAwait(false);
        }
    }

    // ---- Extract -------------------------------------------------------------------

    /// <summary>
    /// Extracts every entry from <paramref name="zipPath"/> to <paramref name="targetDirectory"/>.
    /// Refuses entries that would write outside the target (zip-slip protection).
    /// </summary>
    public static void ExtractTo(this string zipPath, string targetDirectory)
        => ExtractTo(zipPath, targetDirectory, limits: null);

    /// <summary>
    /// Extraction with resource caps for untrusted archives — see <see cref="ZipExtractionLimits"/>.
    /// </summary>
    public static void ExtractTo(this string zipPath, string targetDirectory, ZipExtractionLimits? limits)
    {
        if (string.IsNullOrEmpty(zipPath)) throw new ArgumentException("Zip path required.", nameof(zipPath));
        using var reader = ZipReader.Open(zipPath);
        reader.ExtractTo(targetDirectory, limits);
    }

    public static Task ExtractToAsync(this string zipPath, string targetDirectory, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(zipPath)) throw new ArgumentException("Zip path required.", nameof(zipPath));
        var reader = ZipReader.Open(zipPath);
        // Manual disposal because ExtractToAsync is async.
        return ExtractToAsyncImpl(reader, targetDirectory, ct);

        static async Task ExtractToAsyncImpl(ZipReader r, string td, CancellationToken c)
        {
            try { await r.ExtractToAsync(td, c).ConfigureAwait(false); }
            finally { r.Dispose(); }
        }
    }

    // ---- List entries --------------------------------------------------------------

    /// <summary>Returns metadata for every entry in the zip without extracting anything.</summary>
    public static IReadOnlyList<ZipEntryInfo> ListZipEntries(this string zipPath)
    {
        if (string.IsNullOrEmpty(zipPath)) throw new ArgumentException("Zip path required.", nameof(zipPath));
        // Open, snapshot entries (deferred materialization would be unsafe — reader gets disposed).
        using var reader = ZipReader.Open(zipPath);
        return reader.Entries.ToList();
    }
}
