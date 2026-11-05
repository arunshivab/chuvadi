using System;
using System.IO;

namespace Chuvadi.Sheets.Model;

/// <summary>
/// Thrown when an xlsx file is structurally invalid: missing required parts, malformed XML,
/// or content that cannot be interpreted. The reader is liberal about minor deviations (unknown
/// attributes, missing optional elements, foreign-file conventions) but throws on issues that
/// would cause silent data loss.
/// Since 2.0.0 this is the only exception a malformed file produces: a damaged zip, malformed
/// XML, a damaged relationship entry, a truncated part, or a damaged encrypted container. The
/// original error (XmlException, InvalidDataException, ...) is the <see cref="Exception.InnerException"/>.
/// Read limits, passwords and cancellation keep their own types: see the README, "Errors".
/// </summary>
[Serializable]
public sealed class XlsxFormatException : Exception
{
    public XlsxFormatException(string message) : base(message) { }
    public XlsxFormatException(string message, Exception innerException) : base(message, innerException) { }
}
