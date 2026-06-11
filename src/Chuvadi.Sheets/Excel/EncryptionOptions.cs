using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Options for encrypting an xlsx workbook when saving. Pass to <see cref="Workbook.SaveTo(string, EncryptionOptions)"/>.
/// </summary>
public sealed class EncryptionOptions
{
    /// <summary>The password required to open the file. Cannot be null or empty.</summary>
    public required string Password { get; init; }

    /// <summary>
    /// Key-derivation iteration count ("spin count" in [MS-OFFCRYPTO] terms). Default 100,000
    /// matches modern Excel. Higher = slower brute force; lower = faster save/open.
    /// Don't go below 50,000.
    /// </summary>
    public int SpinCount { get; init; } = 100_000;
}

/// <summary>
/// Thrown when attempting to read an encrypted xlsx file without a password, or with the
/// wrong password.
/// </summary>
public sealed class XlsxPasswordRequiredException : Exception
{
    public XlsxPasswordRequiredException(string message) : base(message) { }
    public XlsxPasswordRequiredException(string message, Exception innerException) : base(message, innerException) { }
}
