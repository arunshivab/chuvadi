using System;

namespace Chuvadi.Sheets.Zip;

/// <summary>
/// Thrown when a zip archive is malformed or violates safety constraints (e.g. a path-traversal
/// entry that would write outside the extraction target). Separate from <c>XlsxFormatException</c>
/// because the zip API is independent of the xlsx API.
/// </summary>
[Serializable]
// Not marked [Obsolete] in 2.0: Chuvadi.Layout.Sheets still names this type in a catch filter
// and builds with warnings as errors. It goes with the rest of Chuvadi.Sheets.Zip in the next
// major version; use Chuvadi.Zip's exceptions instead.
public sealed class ZipFormatException : Exception
{
    public ZipFormatException(string message) : base(message) { }
    public ZipFormatException(string message, Exception innerException) : base(message, innerException) { }
}
