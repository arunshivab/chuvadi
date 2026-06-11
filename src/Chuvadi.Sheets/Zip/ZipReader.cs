using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Sheets.Zip;

/// <summary>
/// Read-only access to a zip archive. Open with <see cref="Open(string)"/> or
/// <see cref="Open(System.IO.Stream, bool)"/>, enumerate <see cref="Entries"/>, then dispose.
///
/// <code>
/// using (var zip = ZipReader.Open("bundle.zip"))
/// {
///     foreach (var entry in zip.Entries)
///     {
///         using var s = entry.OpenRead();
///         // process bytes
///     }
/// }
/// </code>
/// </summary>
public sealed class ZipReader : IDisposable
{
    private readonly Stream _inputStream;
    private readonly bool _ownsInputStream;
    private readonly ZipArchive _archive;
    private bool _disposed;

    private ZipReader(Stream input, bool ownsStream)
    {
        _inputStream = input;
        _ownsInputStream = ownsStream;
        _archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: !ownsStream);
    }

    // ---- Construction --------------------------------------------------------------

    public static ZipReader Open(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return new ZipReader(stream, ownsStream: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <param name="input">The seekable stream containing the zip archive.</param>
    /// <param name="leaveOpen">If true (default), the stream is NOT closed when the reader disposes.</param>
    public static ZipReader Open(Stream input, bool leaveOpen = true)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        return new ZipReader(input, ownsStream: !leaveOpen);
    }

    // ---- Entry enumeration ---------------------------------------------------------

    /// <summary>All entries in the archive, in their stored order.</summary>
    public IReadOnlyList<ZipEntryInfo> Entries
        => _archive.Entries.Select(e => new ZipEntryInfo(e)).ToList();

    /// <summary>Looks up an entry by exact name (case-sensitive). Returns null if not found.</summary>
    public ZipEntryInfo? FindEntry(string entryName)
    {
        var e = _archive.GetEntry(entryName);
        return e is not null ? new ZipEntryInfo(e) : null;
    }

    // ---- Extraction ---------------------------------------------------------------

    /// <summary>
    /// Extracts every entry to <paramref name="targetDirectory"/>. Creates subdirectories
    /// as needed. Refuses entries whose path would escape the target ("zip slip" protection).
    /// </summary>
    public void ExtractTo(string targetDirectory) => ExtractTo(targetDirectory, limits: null);

    /// <summary>
    /// Extracts with optional resource caps for untrusted archives — see
    /// <see cref="ZipExtractionLimits"/>. Exceeding a cap throws <see cref="ZipFormatException"/>;
    /// files already extracted are left in place for the caller to clean up.
    /// </summary>
    public void ExtractTo(string targetDirectory, ZipExtractionLimits? limits)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(targetDirectory))
            throw new ArgumentException("Target directory required.", nameof(targetDirectory));

        Directory.CreateDirectory(targetDirectory);
        var safeRoot = Path.GetFullPath(targetDirectory);

        limits?.ValidateEntryCount(_archive.Entries.Count);
        long totalBudgetUsed = 0;

        foreach (var entry in _archive.Entries)
        {
            totalBudgetUsed += ExtractEntrySafely(entry, safeRoot, limits, totalBudgetUsed);
        }
    }

    public Task ExtractToAsync(string targetDirectory, CancellationToken ct = default)
        => ExtractToAsync(targetDirectory, limits: null, ct);

    /// <summary>Async equivalent of <see cref="ExtractTo(string, ZipExtractionLimits?)"/>.</summary>
    public async Task ExtractToAsync(string targetDirectory, ZipExtractionLimits? limits, CancellationToken ct = default)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(targetDirectory))
            throw new ArgumentException("Target directory required.", nameof(targetDirectory));

        Directory.CreateDirectory(targetDirectory);
        var safeRoot = Path.GetFullPath(targetDirectory);

        limits?.ValidateEntryCount(_archive.Entries.Count);
        long totalBudgetUsed = 0;

        foreach (var entry in _archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            totalBudgetUsed += await ExtractEntrySafelyAsync(entry, safeRoot, limits, totalBudgetUsed, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the per-entry decompression cap implied by <paramref name="limits"/>, or null
    /// when unlimited. The cap is the smaller of MaxEntryBytes and the REMAINING total budget,
    /// so the running total can never be exceeded mid-entry. Counting actual decompressed
    /// bytes (rather than trusting the entry's declared Length) is deliberate — a hostile
    /// archive can lie in its central directory.
    /// </summary>
    private static long? EffectiveEntryCap(ZipExtractionLimits? limits, long totalUsed)
    {
        if (limits is null) return null;
        long? cap = limits.MaxEntryBytes;
        if (limits.MaxTotalBytes is long total)
        {
            long remaining = Math.Max(0, total - totalUsed);
            cap = cap is long c ? Math.Min(c, remaining) : remaining;
        }
        return cap;
    }

    // ---- Internal: per-entry extract with traversal protection --------------------

    private static long ExtractEntrySafely(ZipArchiveEntry entry, string safeRoot, ZipExtractionLimits? limits, long totalUsed)
    {
        var fullPath = ResolveAndCheck(entry, safeRoot);

        // Directory entry (name ends with /) — just create the directory.
        if (string.IsNullOrEmpty(entry.Name))
        {
            Directory.CreateDirectory(fullPath);
            return 0;
        }

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var raw = entry.Open();
        using var src = WrapWithCap(raw, entry, limits, totalUsed, out var counter);
        using var dest = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        try
        {
            src.CopyTo(dest);
        }
        catch (InvalidDataException ex)
        {
            throw new ZipFormatException(ex.Message);
        }

        // Preserve timestamp where possible.
        try { File.SetLastWriteTimeUtc(fullPath, entry.LastWriteTime.UtcDateTime); }
        catch { /* advisory */ }

        return counter?.BytesRead ?? dest.Length;
    }

    /// <summary>Wraps the entry stream in a decompression counter when limits apply.</summary>
    private static Stream WrapWithCap(
        Stream raw, ZipArchiveEntry entry, ZipExtractionLimits? limits, long totalUsed,
        out Chuvadi.Internal.LimitedReadStream? counter)
    {
        counter = null;
        if (EffectiveEntryCap(limits, totalUsed) is long cap)
        {
            counter = new Chuvadi.Internal.LimitedReadStream(raw, cap, $"zip entry '{entry.FullName}'");
            return counter;
        }
        return raw;
    }

    private static async Task<long> ExtractEntrySafelyAsync(
        ZipArchiveEntry entry, string safeRoot, ZipExtractionLimits? limits, long totalUsed, CancellationToken ct)
    {
        var fullPath = ResolveAndCheck(entry, safeRoot);

        if (string.IsNullOrEmpty(entry.Name))
        {
            Directory.CreateDirectory(fullPath);
            return 0;
        }

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var raw = entry.Open();
        await using var src = WrapWithCap(raw, entry, limits, totalUsed, out var counter);
        await using var dest = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 64 * 1024, options: FileOptions.Asynchronous);
        try
        {
            await src.CopyToAsync(dest, ct).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new ZipFormatException(ex.Message);
        }

        try { File.SetLastWriteTimeUtc(fullPath, entry.LastWriteTime.UtcDateTime); }
        catch { }

        return counter?.BytesRead ?? dest.Length;
    }

    /// <summary>
    /// Resolves the entry's target path inside <paramref name="safeRoot"/> and throws
    /// <see cref="ZipFormatException"/> if the result would escape the root (zip-slip).
    /// </summary>
    private static string ResolveAndCheck(ZipArchiveEntry entry, string safeRoot)
    {
        // ZipArchiveEntry.FullName uses '/' regardless of OS. Combine and canonicalize.
        var combined = Path.Combine(safeRoot, entry.FullName);
        var full = Path.GetFullPath(combined);

        // Ensure full starts with safeRoot (plus a directory separator) — otherwise the
        // entry name contained '../' (or similar) that resolved outside safeRoot.
        var rootWithSep = safeRoot.EndsWith(Path.DirectorySeparatorChar)
            ? safeRoot
            : safeRoot + Path.DirectorySeparatorChar;

        if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, safeRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ZipFormatException(
                $"Refusing to extract entry '{entry.FullName}' because it resolves to a path outside the target directory ('{full}' vs root '{safeRoot}'). This is likely a malicious archive (zip-slip).");
        }

        return full;
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _archive.Dispose(); } catch { }
        if (_ownsInputStream)
        {
            try { _inputStream.Dispose(); } catch { }
        }
    }
}
