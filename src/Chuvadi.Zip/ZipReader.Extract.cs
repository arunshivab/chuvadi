using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Zip.Internal;

namespace Chuvadi.Zip;

public sealed partial class ZipReader
{
    /// <summary>Extracts every entry under <paramref name="targetDirectory"/> with zip-slip protection and the reader's limits.</summary>
    public ZipExtractionResult ExtractTo(string targetDirectory, ZipExtractOptions? options = null)
        => ExtractEntries(_entries, targetDirectory, options);

    /// <summary>Async, cancellable extraction.</summary>
    public Task<ZipExtractionResult> ExtractToAsync(string targetDirectory, ZipExtractOptions? options = null, CancellationToken ct = default)
        => ExtractEntriesAsync(_entries, targetDirectory, options, ct);

    /// <summary>Extracts the given entries (which must belong to this reader).</summary>
    public ZipExtractionResult ExtractEntries(IEnumerable<ZipEntryInfo> entries, string targetDirectory, ZipExtractOptions? options = null)
    {
        EnsureNotDisposed();
        if (entries is null) throw new ArgumentNullException(nameof(entries));
        if (string.IsNullOrEmpty(targetDirectory)) throw new ArgumentException("Target directory required.", nameof(targetDirectory));
        options ??= new ZipExtractOptions();
        string root = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(root);
        var result = new ZipExtractionResult();
        var list = entries as IReadOnlyList<ZipEntryInfo> ?? new List<ZipEntryInfo>(entries);
        var buf = new byte[81920];
        long processed = 0;

        foreach (var entry in list)
        {
            var target = Plan(entry, root, options, result);
            if (target is null) continue;
            if (entry.IsDirectory)
            {
                if (!Directory.Exists(target)) { Directory.CreateDirectory(target); result.DirectoriesCreated++; }
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            EnsureNoLinkEscape(root, target, entry.Name);
            long written = 0;
            bool complete = false;
            try
            {
                using (var src = OpenRead(entry, options.Password))
                using (var dst = new FileStream(target, options.Overwrite == ZipOverwritePolicy.Fail ? FileMode.CreateNew : FileMode.Create,
                           FileAccess.Write, FileShare.None, 81920))
                {
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        written += n;
                    }
                }
                complete = true;
            }
            finally
            {
                if (!complete && options.DeletePartialFiles) { try { File.Delete(target); } catch { } }
            }
            Finish(entry, target, options);
            result.FilesExtracted++;
            result.BytesWritten += written;
            processed += written;
            options.Progress?.Report(new ZipProgress(entry.Name, entry.Index, list.Count, processed));
        }
        return result;
    }

    /// <summary>Async version of <see cref="ExtractEntries"/>.</summary>
    public async Task<ZipExtractionResult> ExtractEntriesAsync(IEnumerable<ZipEntryInfo> entries, string targetDirectory,
        ZipExtractOptions? options = null, CancellationToken ct = default)
    {
        EnsureNotDisposed();
        if (entries is null) throw new ArgumentNullException(nameof(entries));
        if (string.IsNullOrEmpty(targetDirectory)) throw new ArgumentException("Target directory required.", nameof(targetDirectory));
        options ??= new ZipExtractOptions();
        string root = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(root);
        var result = new ZipExtractionResult();
        var list = entries as IReadOnlyList<ZipEntryInfo> ?? new List<ZipEntryInfo>(entries);
        var buf = new byte[81920];
        long processed = 0;

        foreach (var entry in list)
        {
            ct.ThrowIfCancellationRequested();
            var target = Plan(entry, root, options, result);
            if (target is null) continue;
            if (entry.IsDirectory)
            {
                if (!Directory.Exists(target)) { Directory.CreateDirectory(target); result.DirectoriesCreated++; }
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            EnsureNoLinkEscape(root, target, entry.Name);
            long written = 0;
            bool complete = false;
            try
            {
                await using (var src = OpenRead(entry, options.Password))
                await using (var dst = new FileStream(target, options.Overwrite == ZipOverwritePolicy.Fail ? FileMode.CreateNew : FileMode.Create,
                                 FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    int n;
                    while ((n = await src.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                        written += n;
                    }
                }
                complete = true;
            }
            finally
            {
                if (!complete && options.DeletePartialFiles) { try { File.Delete(target); } catch { } }
            }
            Finish(entry, target, options);
            result.FilesExtracted++;
            result.BytesWritten += written;
            processed += written;
            options.Progress?.Report(new ZipProgress(entry.Name, entry.Index, list.Count, processed));
        }
        return result;
    }

    /// <summary>Returns the full target path, or null if the entry is skipped.</summary>
    private string? Plan(ZipEntryInfo entry, string root, ZipExtractOptions options, ZipExtractionResult result)
    {
        if (!ReferenceEquals(entry.Owner, this)) throw new ArgumentException("Entry belongs to a different reader.");
        if (options.Filter is not null && !options.Filter(entry)) { result.SkippedFiltered.Add(entry.Name); return null; }

        if (entry.UnsafeReason is not null)
        {
            if (options.UnsafePaths == ZipUnsafePathPolicy.Skip) { result.SkippedUnsafe.Add(entry.Name); return null; }
            throw new ZipUnsafePathException(entry.Name,
                $"Refusing to extract entry '{entry.Name}': {entry.UnsafeReason} This is likely a malicious archive (zip-slip).");
        }
        string norm = entry.NormalizedName!;
        if (norm.Length == 0) return null;                       // "./" - the root itself

        if (entry.IsSymlink)
        {
            switch (options.Symlinks)
            {
                case ZipSymlinkPolicy.Skip: result.SkippedSymlinks.Add(entry.Name); return null;
                case ZipSymlinkPolicy.Throw:
                    throw new ZipUnsafePathException(entry.Name, $"Entry '{entry.Name}' is a symbolic link; symlinks are not extracted.");
            }
        }

        string? full = PathSafety.ResolveUnderRoot(root, norm);
        if (full is null)
        {
            if (options.UnsafePaths == ZipUnsafePathPolicy.Skip) { result.SkippedUnsafe.Add(entry.Name); return null; }
            throw new ZipUnsafePathException(entry.Name,
                $"Refusing to extract entry '{entry.Name}': it resolves outside the target directory (zip-slip).");
        }

        if (!entry.IsDirectory)
        {
            var fi = new FileInfo(full);
            if (fi.LinkTarget is not null) { try { File.Delete(full); } catch { } }   // never write through a link
            else if (fi.Exists)
            {
                if (options.Overwrite == ZipOverwritePolicy.Skip) { result.SkippedExisting.Add(entry.Name); return null; }
                if (options.Overwrite == ZipOverwritePolicy.Fail) throw new IOException($"File already exists: {full}");
            }
        }
        return full;
    }

    /// <summary>Rejects targets whose existing parent directories are links that might lead outside the root.</summary>
    private static void EnsureNoLinkEscape(string root, string target, string entryName)
    {
        string? dir = Path.GetDirectoryName(target);
        var rootTrim = root.TrimEnd(Path.DirectorySeparatorChar);
        while (!string.IsNullOrEmpty(dir) && dir.Length > rootTrim.Length)
        {
            if (new DirectoryInfo(dir).LinkTarget is not null)
                throw new ZipUnsafePathException(entryName,
                    $"Refusing to extract entry '{entryName}': a parent directory is a symbolic link or junction.");
            dir = Path.GetDirectoryName(dir);
        }
    }

    private static void Finish(ZipEntryInfo entry, string target, ZipExtractOptions options)
    {
        if (options.RestoreTimestamps)
        {
            try { File.SetLastWriteTimeUtc(target, entry.LastModified.UtcDateTime); } catch { }
        }
        if (options.RestoreUnixPermissions && !OperatingSystem.IsWindows() && entry.UnixMode is int m && !entry.IsSymlink)
        {
            try { File.SetUnixFileMode(target, (UnixFileMode)(m & 0x1FF)); } catch { }
        }
    }
}
