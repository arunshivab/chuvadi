using System;
using System.IO;
using System.Xml;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Since 2.0.0, every error caused by a malformed file reaches the caller as one type,
/// <see cref="XlsxFormatException"/>, with the original exception as its InnerException.
/// The public read entry points (XlsxReader.Open, row enumeration, the SheetReader part readers,
/// ReadChartSheet, Workbook.Load and the lazy image loaders) pass their errors through here.
/// Limits, passwords, cancellation and caller mistakes keep their own exception types.
/// </summary>
internal static class MalformedInput
{
    /// <summary>
    /// Set in <see cref="Exception.Data"/> on the InvalidDataException that reports
    /// XlsxReaderOptions.MaxPartBytes: that is a limit, not a malformed file, so it keeps its
    /// 1.x type.
    /// </summary>
    internal const string LimitMarker = "Chuvadi.Sheets.ReadLimit";

    /// <summary>True for the exceptions a malformed file produces.</summary>
    internal static bool Is(Exception ex) => ex switch
    {
        XlsxFormatException => false,                       // already the public type
        XmlException => true,                               // malformed XML in a part
        InvalidDataException ide => !ide.Data.Contains(LimitMarker),   // corrupt zip, missing part, corrupt encrypted package
        EndOfStreamException => true,                       // truncated data
        _ => false,
    };

    internal static XlsxFormatException Wrap(Exception ex)
        => new("The file is not a valid xlsx workbook: " + ex.Message, ex);

    /// <summary>Marks an InvalidDataException as a read limit (see <see cref="LimitMarker"/>).</summary>
    internal static InvalidDataException Limit(InvalidDataException ex)
    {
        ex.Data[LimitMarker] = true;
        return ex;
    }

    internal static T Run<T>(Func<T> body)
    {
        try { return body(); }
        catch (Exception ex) when (Is(ex)) { throw Wrap(ex); }
    }
}
