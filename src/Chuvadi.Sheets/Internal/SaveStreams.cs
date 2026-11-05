using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Internal.Crypto;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// A pass-through stream that leaves the inner stream open (Sheets 2.0.1, B1): disposing it
/// flushes the inner stream instead of closing it. Every capability is passed through, so the
/// package writer produces the same bytes it would write to the inner stream directly.
/// </summary>
internal sealed class LeaveOpenStream : Stream
{
    private readonly Stream _inner;
    private bool _disposed;

    public LeaveOpenStream(Stream inner) { _inner = inner; }

    public override bool CanRead => !_disposed && _inner.CanRead;
    public override bool CanSeek => !_disposed && _inner.CanSeek;
    public override bool CanWrite => !_disposed && _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
    public override void WriteByte(byte value) => _inner.WriteByte(value);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.WriteAsync(buffer, offset, count, cancellationToken);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _inner.Flush();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Temporary files of the writers (Sheets 2.0.1, D-183, D-187):
/// <list type="bullet">
/// <item>spools are owner-only on Unix, opened exclusively and deleted when closed; for an
/// encrypted save they are <see cref="EncryptedSpool"/>s, so no plaintext reaches the disk;</item>
/// <item>a path save writes a temporary file beside the target and replaces the target only when
/// the save succeeds, so a failed save keeps the existing file.</item>
/// </list>
/// </summary>
internal static class TempFiles
{
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// A read-write spool in <paramref name="directory"/>: encrypted (AES-256-CTR, one-time key in
    /// memory) when <paramref name="encrypted"/>, otherwise a plain owner-only file. Either way it
    /// is deleted when disposed.
    /// </summary>
    public static Stream CreateSpool(string directory, string fileNamePrefix, bool encrypted)
    {
        if (encrypted) return new EncryptedSpool(directory, fileNamePrefix);

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose | FileOptions.Asynchronous,
            BufferSize = BufferSize,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(Path.Combine(directory, $"{fileNamePrefix}{Guid.NewGuid():N}.tmp"), options);
    }

    /// <summary>
    /// The file a path save really writes: <paramref name="fullPath"/>, or, when that is a symbolic link to a file,
    /// the file it finally points to, so the link itself is kept (before 2.0.1 the save wrote through the link).
    /// </summary>
    public static string ResolveTarget(string fullPath)
    {
        var info = new FileInfo(fullPath);
        if (info.Exists && info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is FileInfo final)
            return final.FullName;
        return fullPath;
    }

    /// <summary>
    /// Fails exactly where and how a path save failed before 2.0.1, which opened the target for writing at once:
    /// an existing target that cannot be opened for writing (read-only, locked by another process, a folder) throws
    /// the same exception now, at Create, instead of at Save. The file is opened without truncating and not
    /// changed. A missing folder throws DirectoryNotFoundException naming the target, as before.
    /// </summary>
    public static void ProbeWritable(string target)
    {
        string? folder = Path.GetDirectoryName(target);
        bool folderMissing = folder is not null && !Directory.Exists(folder);
        if (!folderMissing && !File.Exists(target) && !Directory.Exists(target)) return;
        using var probe = new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.None, bufferSize: 1);
    }

    /// <summary>
    /// Replaces <paramref name="target"/> with the finished <paramref name="temp"/> file. An existing target keeps
    /// what the old in-place write kept: its permission bits on Unix, and on Windows its attributes and security
    /// settings (File.Replace). A new target is created by a rename.
    /// </summary>
    public static void Commit(string temp, string target)
    {
        if (File.Exists(target))
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temp, File.GetUnixFileMode(target));
                File.Replace(temp, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }
            catch (FileNotFoundException) when (File.Exists(temp) && !File.Exists(target))
            {
                // The target was removed meanwhile: create it below.
            }
        }
        File.Move(temp, target, overwrite: true);
    }

    /// <summary>The temporary file a path save writes before replacing <paramref name="fullPath"/>: ".&lt;name&gt;.&lt;8 hex&gt;.tmp" beside it.</summary>
    public static string BesideTarget(string fullPath)
        => Path.Combine(Path.GetDirectoryName(fullPath) ?? ".",
            "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");

    /// <summary>Deletes a file if it exists, ignoring failures (clean-up only).</summary>
    public static void TryDelete(string? path)
    {
        if (path is null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
