using System;

namespace Chuvadi.Zip;

/// <summary>The archive is malformed or inconsistent. Base of all content-related zip exceptions.</summary>
public class ZipFormatException : Exception
{
    public ZipFormatException(string message) : base(message) { }
    public ZipFormatException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>A configured resource limit was exceeded (zip-bomb / size / count protection).</summary>
public sealed class ZipLimitExceededException : ZipFormatException
{
    public ZipLimit Limit { get; }
    /// <summary>The configured maximum.</summary>
    public long Maximum { get; }
    /// <summary>Entry the limit tripped on, if applicable.</summary>
    public string? EntryName { get; }

    public ZipLimitExceededException(ZipLimit limit, long maximum, string message, string? entryName = null)
        : base(message)
    {
        Limit = limit;
        Maximum = maximum;
        EntryName = entryName;
    }
}

/// <summary>An entry name or symlink would escape the extraction root, or is otherwise unsafe.</summary>
public sealed class ZipUnsafePathException : ZipFormatException
{
    public string EntryName { get; }
    public ZipUnsafePathException(string entryName, string message) : base(message) { EntryName = entryName; }
}

/// <summary>Data failed verification: bad CRC-32, bad AES HMAC, truncated data, or name mismatch.</summary>
public sealed class ZipIntegrityException : ZipFormatException
{
    public string? EntryName { get; }
    public ZipIntegrityException(string message, string? entryName = null) : base(message) { EntryName = entryName; }
}

/// <summary>A valid zip feature that this library does not implement (e.g. BZip2, split archives).</summary>
public sealed class ZipUnsupportedException : ZipFormatException
{
    public ZipUnsupportedException(string message) : base(message) { }
}

/// <summary>
/// An encrypted entry was opened without a password, or with a wrong one. For a ZipCrypto entry this is also thrown when
/// the password passed the format's one-byte check (about 1 wrong password in 256 does) but the data then failed its
/// CRC-32 or decompression check: the password is wrong or the entry is damaged, and <see cref="Exception.InnerException"/>
/// holds the <see cref="ZipIntegrityException"/> that says which check failed.
/// </summary>
public sealed class ZipPasswordRequiredException : InvalidOperationException
{
    /// <summary>
    /// True if a password was supplied but did not verify; false if none was supplied. For a ZipCrypto entry that passed
    /// the check byte and then failed its CRC-32 or decompression check this is also true, although the entry may instead be damaged.
    /// </summary>
    public bool WrongPassword { get; }
    public string? EntryName { get; }

    public ZipPasswordRequiredException(string message, bool wrongPassword, string? entryName = null) : base(message)
    {
        WrongPassword = wrongPassword;
        EntryName = entryName;
    }

    internal ZipPasswordRequiredException(string message, bool wrongPassword, string? entryName, Exception? innerException) : base(message, innerException)
    {
        WrongPassword = wrongPassword;
        EntryName = entryName;
    }
}
