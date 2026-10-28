using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Zip.Internal;

namespace Chuvadi.Zip;

public sealed partial class ZipWriter
{
    /// <summary>Adds an entry from a byte buffer.</summary>
    public void AddBytes(string name, ReadOnlySpan<byte> data, ZipEntryOptions? options = null)
    {
        using var s = CreateEntry(name, WithExpected(options, data.Length));
        s.Write(data);
    }

    /// <summary>Adds an entry from text (UTF-8 without BOM by default).</summary>
    public void AddText(string name, string text, Encoding? encoding = null, ZipEntryOptions? options = null)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        AddBytes(name, (encoding ?? new UTF8Encoding(false)).GetBytes(text), options);
    }

    /// <summary>Adds an entry by streaming <paramref name="source"/> from its current position to its end.</summary>
    public void AddStream(string name, Stream source, ZipEntryOptions? options = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        long? remaining = source.CanSeek ? source.Length - source.Position : null;
        using var s = CreateEntry(name, WithExpected(options, remaining));
        source.CopyTo(s, 81920);
    }

    /// <summary>Async, cancellable version of <see cref="AddStream"/>.</summary>
    public async Task AddStreamAsync(string name, Stream source, ZipEntryOptions? options = null, CancellationToken ct = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        long? remaining = source.CanSeek ? source.Length - source.Position : null;
        await using var s = CreateEntry(name, WithExpected(options, remaining));
        var buf = new byte[81920];
        int n;
        while ((n = await source.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            s.Write(buf, 0, n);
    }

    /// <summary>Adds a file from disk; its timestamp (and Unix permissions on Unix) are recorded unless overridden.</summary>
    public void AddFile(string name, string path, ZipEntryOptions? options = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException("File to add not found.", path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using var s = CreateEntry(name, FileEntryOptions(fi, options, fs.Length));
        fs.CopyTo(s, 81920);
    }

    /// <summary>Async, cancellable version of <see cref="AddFile"/>.</summary>
    public async Task AddFileAsync(string name, string path, ZipEntryOptions? options = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException("File to add not found.", path);
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, System.IO.FileOptions.Asynchronous | System.IO.FileOptions.SequentialScan);
        await using var s = CreateEntry(name, FileEntryOptions(fi, options, fs.Length));
        var buf = new byte[81920];
        int n;
        while ((n = await fs.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            s.Write(buf, 0, n);
    }

    /// <summary>Adds an explicit directory entry ("name/").</summary>
    public void AddDirectory(string name, ZipEntryOptions? options = null)
    {
        using var s = BeginEntry(name, options, directory: true);
    }

    private static ZipEntryOptions WithExpected(ZipEntryOptions? o, long? size)
    {
        if (o is null) return new ZipEntryOptions { ExpectedSize = size };
        if (o.ExpectedSize is not null || size is null) return o;
        return Clone(o, expected: size);
    }

    private static ZipEntryOptions FileEntryOptions(FileInfo fi, ZipEntryOptions? o, long length)
    {
        DateTimeOffset? when = o?.LastModified ?? new DateTimeOffset(fi.LastWriteTime);
        int? mode = o?.UnixMode;
        if (mode is null && !OperatingSystem.IsWindows())
        {
            try { mode = (int)fi.UnixFileMode; } catch { }
        }
        return Clone(o, expected: o?.ExpectedSize ?? length, lastModified: when, unixMode: mode);
    }

    private static ZipEntryOptions Clone(ZipEntryOptions? o, long? expected = null, DateTimeOffset? lastModified = null, int? unixMode = null)
        => new()
        {
            Method = o?.Method,
            Level = o?.Level,
            Comment = o?.Comment,
            LastModified = lastModified ?? o?.LastModified,
            UnixMode = unixMode ?? o?.UnixMode,
            DosAttributes = o?.DosAttributes,
            ExternalAttributes = o?.ExternalAttributes,
            Encryption = o?.Encryption,
            Password = o?.Password,
            ExpectedSize = expected ?? o?.ExpectedSize,
            Zip64 = o?.Zip64 ?? false,
        };

    // ---- Directory trees ----------------------------------------------------------

    /// <summary>Adds a whole directory tree (sorted, deterministic order).</summary>
    public void AddDirectoryTree(string sourceDirectory, ZipDirectoryOptions? options = null, CancellationToken ct = default)
        => AddTreeCore(sourceDirectory, options, ct, async: false).GetAwaiter().GetResult();

    /// <summary>Async version of <see cref="AddDirectoryTree"/>.</summary>
    public Task AddDirectoryTreeAsync(string sourceDirectory, ZipDirectoryOptions? options = null, CancellationToken ct = default)
        => AddTreeCore(sourceDirectory, options, ct, async: true);

    private async Task AddTreeCore(string sourceDirectory, ZipDirectoryOptions? options, CancellationToken ct, bool async)
    {
        if (string.IsNullOrEmpty(sourceDirectory)) throw new ArgumentException("Source directory required.", nameof(sourceDirectory));
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
        options ??= new ZipDirectoryOptions();
        string root = Path.GetFullPath(sourceDirectory);
        string prefix = options.EntryPrefix is { Length: > 0 } p ? p.Replace('\\', '/').TrimEnd('/') + "/" : string.Empty;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        long processed = 0;

        async Task Walk(DirectoryInfo dir, string relPrefix)
        {
            ct.ThrowIfCancellationRequested();
            if (options.FollowSymlinks)
            {
                var real = (dir.ResolveLinkTarget(true) ?? dir).FullName;
                if (!visited.Add(real)) return;      // loop
            }
            var children = new List<FileSystemInfo>(dir.EnumerateFileSystemInfos());
            children.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            bool any = false;
            foreach (var c in children)
            {
                ct.ThrowIfCancellationRequested();
                if (c.LinkTarget is not null && !options.FollowSymlinks) continue;
                if (options.Filter is not null && !options.Filter(c.FullName)) continue;
                any = true;
                string rel = relPrefix + c.Name;
                if (c is DirectoryInfo d)
                {
                    if (options.Recurse) await Walk(d, rel + "/").ConfigureAwait(false);
                    else if (options.IncludeEmptyDirectories) AddDirectory(prefix + rel + "/", DirOptions(options, d));
                }
                else if (c is FileInfo f)
                {
                    if (OutputFullPath is not null && string.Equals(f.FullName, OutputFullPath, PathSafety.FileSystemComparison)) continue;
                    var eo = TreeOptions(options, f);
                    if (async) await AddFileAsync(prefix + rel, f.FullName, eo, ct).ConfigureAwait(false);
                    else AddFile(prefix + rel, f.FullName, eo);
                    processed += f.Length;
                    options.Progress?.Report(new ZipProgress(prefix + rel, index++, -1, processed));
                }
            }
            if (!any && options.IncludeEmptyDirectories && relPrefix.Length > 0)
                AddDirectory(prefix + relPrefix, DirOptions(options, dir));
        }

        await Walk(new DirectoryInfo(root), string.Empty).ConfigureAwait(false);
    }

    private static ZipEntryOptions TreeOptions(ZipDirectoryOptions o, FileInfo f)
    {
        DateTimeOffset? when = o.PreserveTimestamps ? new DateTimeOffset(f.LastWriteTime) : null;
        int? mode = null;
        if (o.PreserveUnixPermissions && !OperatingSystem.IsWindows()) { try { mode = (int)f.UnixFileMode; } catch { } }
        return Clone(o.Entry, lastModified: o.Entry?.LastModified ?? when, unixMode: o.Entry?.UnixMode ?? mode);
    }

    private static ZipEntryOptions DirOptions(ZipDirectoryOptions o, DirectoryInfo d)
    {
        DateTimeOffset? when = o.PreserveTimestamps ? new DateTimeOffset(d.LastWriteTime) : null;
        return Clone(o.Entry, lastModified: o.Entry?.LastModified ?? when);
    }
}
