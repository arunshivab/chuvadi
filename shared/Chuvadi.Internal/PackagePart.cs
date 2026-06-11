using System;

namespace Chuvadi.Internal;

/// <summary>
/// Represents a single part within an OOXML package. A part is identified by its
/// URI (its path inside the zip, beginning with '/'), has a MIME content type,
/// and corresponds to one entry in the underlying zip archive.
/// </summary>
internal sealed class PackagePart
{
    /// <summary>
    /// The part's URI within the package. Always begins with '/'.
    /// Examples: "/xl/workbook.xml", "/xl/worksheets/sheet1.xml".
    /// </summary>
    public string Uri { get; }

    /// <summary>
    /// The MIME content type as declared in [Content_Types].xml.
    /// Example: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml".
    /// </summary>
    public string ContentType { get; }

    public PackagePart(string uri, string contentType)
    {
        if (string.IsNullOrEmpty(uri))
            throw new ArgumentException("Part URI cannot be null or empty.", nameof(uri));
        if (uri[0] != '/')
            throw new ArgumentException("Part URI must begin with '/'.", nameof(uri));
        if (string.IsNullOrEmpty(contentType))
            throw new ArgumentException("Content type cannot be null or empty.", nameof(contentType));

        Uri = uri;
        ContentType = contentType;
    }

    /// <summary>
    /// Converts the part URI ('/xl/workbook.xml') to a zip entry name ('xl/workbook.xml').
    /// Zip entries do not include the leading slash.
    /// </summary>
    internal string ToZipEntryName() => Uri.TrimStart('/');
}
