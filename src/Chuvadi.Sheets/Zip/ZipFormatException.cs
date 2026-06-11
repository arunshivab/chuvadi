using System;

namespace Chuvadi.Sheets.Zip;

/// <summary>
/// Thrown when a zip archive is malformed or violates safety constraints (e.g. a path-traversal
/// entry that would write outside the extraction target). Separate from <c>XlsxFormatException</c>
/// because the zip API is independent of the xlsx API.
/// </summary>
[Serializable]
public sealed class ZipFormatException : Exception
{
    public ZipFormatException(string message) : base(message) { }
    public ZipFormatException(string message, Exception innerException) : base(message, innerException) { }
}
