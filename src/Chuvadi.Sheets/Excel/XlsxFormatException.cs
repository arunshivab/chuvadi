using System;
using System.IO;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Thrown when an xlsx file is structurally invalid: missing required parts, malformed XML,
/// or content that cannot be interpreted. The reader is liberal about minor deviations (unknown
/// attributes, missing optional elements, foreign-file conventions) but throws on issues that
/// would cause silent data loss.
/// </summary>
[Serializable]
public sealed class XlsxFormatException : Exception
{
    public XlsxFormatException(string message) : base(message) { }
    public XlsxFormatException(string message, Exception innerException) : base(message, innerException) { }
}
