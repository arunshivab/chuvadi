using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Chuvadi.Internal;

/// <summary>
/// Represents an OOXML (Open Office XML) package. An OOXML package is a ZIP archive
/// containing a tree of XML parts plus a manifest ([Content_Types].xml) and relationship
/// files (.rels) describing how the parts connect.
///
/// This class is the foundation that xlsx (and future docx) writers and readers sit on.
/// It does NOT know anything about Excel, sheets, or styles — it only knows about parts,
/// content types, and relationships.
///
/// Usage (write):
///   using var pkg = OoxmlPackage.Create(stream);
///   using (var s = pkg.CreatePart("/xl/workbook.xml", "application/...+xml")) { ... }
///   pkg.AddRelationship("/", "/xl/workbook.xml", "...officeDocument", "rId1");
///   pkg.Close();
///
/// Usage (read):
///   using var pkg = OoxmlPackage.Open(stream);
///   foreach (var part in pkg.Parts) { ... }
///   using (var s = pkg.OpenPart("/xl/workbook.xml")) { ... }
/// </summary>
internal sealed class OoxmlPackage : IDisposable
{
    // ---- Constants -----------------------------------------------------------------

    /// <summary>Path of the content types manifest within the package.</summary>
    private const string ContentTypesPath = "[Content_Types].xml";

    /// <summary>Namespace for the content types manifest.</summary>
    private const string ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>Namespace for relationship files.</summary>
    private const string RelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    // ---- State ---------------------------------------------------------------------

    private readonly ZipArchive _zip;
    private readonly bool _writeMode;
    private bool _closed;
    private bool _disposed;

    /// <summary>Parts written to the package (write mode) or discovered in it (read mode).</summary>
    private readonly Dictionary<string, PackagePart> _parts = new(StringComparer.Ordinal);

    /// <summary>Relationships keyed by their source part URI. The package root uses "/" as its source.</summary>
    private readonly Dictionary<string, List<PackageRelationship>> _relationships = new(StringComparer.Ordinal);

    // ---- Construction --------------------------------------------------------------

    private OoxmlPackage(ZipArchive zip, bool writeMode)
    {
        _zip = zip;
        _writeMode = writeMode;
    }

    /// <summary>
    /// Creates a new, empty OOXML package writing to the given stream. The stream must be
    /// writable and seekable; the caller is responsible for disposing it after the package
    /// is disposed (or for using a FileStream that the package's ZipArchive will close).
    /// </summary>
    public static OoxmlPackage Create(Stream output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
        return new OoxmlPackage(zip, writeMode: true);
    }

    /// <summary>
    /// Opens an existing OOXML package from the given stream. Reads [Content_Types].xml
    /// and all .rels files immediately to populate the parts and relationships dictionaries.
    /// </summary>
    public static OoxmlPackage Open(Stream input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var pkg = new OoxmlPackage(zip, writeMode: false);
        pkg.LoadContentTypes();
        pkg.LoadAllRelationships();
        return pkg;
    }

    // ---- Write API -----------------------------------------------------------------

    /// <summary>
    /// Creates a new part in the package and returns a writable stream for its content.
    /// Caller MUST dispose the returned stream before creating another part. The part's
    /// content type is recorded for the [Content_Types].xml manifest written at Finalize.
    /// </summary>
    /// <param name="uri">Part URI, must begin with '/'. Example: "/xl/workbook.xml".</param>
    /// <param name="contentType">MIME type for [Content_Types].xml.</param>
    public Stream CreatePart(string uri, string contentType)
    {
        EnsureWriteMode();
        EnsureNotClosed();
        if (_parts.ContainsKey(uri))
            throw new InvalidOperationException($"Part '{uri}' already exists in this package.");

        var part = new PackagePart(uri, contentType);
        _parts[uri] = part;

        var entry = _zip.CreateEntry(part.ToZipEntryName(), CompressionLevel.Optimal);
        return entry.Open();
    }

    /// <summary>
    /// Adds a relationship from a source part (or the package root, indicated by sourceUri "/")
    /// to a target part. The target URI is converted to the appropriate relative form before
    /// writing the .rels file. Relationship IDs must be unique within a source's relationship set.
    /// </summary>
    /// <param name="sourceUri">Source part URI, or "/" for package-root relationships.</param>
    /// <param name="targetUri">Target part URI (always begins with '/').</param>
    /// <param name="relationshipType">OOXML relationship type URI.</param>
    /// <param name="id">Relationship ID, e.g. "rId1". Must be unique within the source's relationships.</param>
    public void AddRelationship(string sourceUri, string targetUri, string relationshipType, string id)
    {
        EnsureWriteMode();
        EnsureNotClosed();
        if (string.IsNullOrEmpty(sourceUri)) throw new ArgumentException("Source URI required.", nameof(sourceUri));
        if (string.IsNullOrEmpty(targetUri)) throw new ArgumentException("Target URI required.", nameof(targetUri));

        var target = MakeRelativeTarget(sourceUri, targetUri);
        AddRelationshipRecord(sourceUri, new PackageRelationship(id, relationshipType, target, isExternal: false));
    }

    /// <summary>
    /// Adds an EXTERNAL relationship — the target is an absolute URL (http, https, mailto, file)
    /// rather than a part path inside the package. The target string is preserved verbatim and
    /// the resulting Relationship element gets <c>TargetMode="External"</c>.
    /// </summary>
    public void AddExternalRelationship(string sourceUri, string targetUrl, string relationshipType, string id)
    {
        EnsureWriteMode();
        EnsureNotClosed();
        if (string.IsNullOrEmpty(sourceUri)) throw new ArgumentException("Source URI required.", nameof(sourceUri));
        if (string.IsNullOrEmpty(targetUrl)) throw new ArgumentException("Target URL required.", nameof(targetUrl));

        AddRelationshipRecord(sourceUri, new PackageRelationship(id, relationshipType, targetUrl, isExternal: true));
    }

    private void AddRelationshipRecord(string sourceUri, PackageRelationship rel)
    {
        if (!_relationships.TryGetValue(sourceUri, out var list))
        {
            list = new List<PackageRelationship>();
            _relationships[sourceUri] = list;
        }
        foreach (var existing in list)
        {
            if (existing.Id == rel.Id)
                throw new InvalidOperationException(
                    $"Relationship ID '{rel.Id}' is already used for source '{sourceUri}'.");
        }
        list.Add(rel);
    }

    /// <summary>
    /// Writes [Content_Types].xml and all .rels files, then closes the underlying zip.
    /// After Close, no further writes are permitted. Calling Dispose without Close
    /// is tolerated — Dispose will auto-close — but explicit Close is preferred so
    /// any I/O errors surface to the caller cleanly.
    /// </summary>
    public void Close()
    {
        EnsureWriteMode();
        if (_closed) return;

        WriteContentTypes();
        WriteAllRelationships();

        _closed = true;
    }

    // ---- Read API ------------------------------------------------------------------

    /// <summary>
    /// All parts discovered in the package, in the order they appeared in [Content_Types].xml.
    /// </summary>
    public IEnumerable<PackagePart> Parts => _parts.Values;

    /// <summary>
    /// Opens a part for reading. Returns a stream; the caller must dispose it.
    /// </summary>
    public Stream OpenPart(string uri)
    {
        EnsureReadMode();
        if (!_parts.ContainsKey(uri))
            throw new FileNotFoundException($"Part '{uri}' not found in package.");

        var entryName = uri.TrimStart('/');
        var entry = _zip.GetEntry(entryName)
            ?? throw new InvalidDataException($"Part '{uri}' is declared in [Content_Types].xml but missing from the zip.");
        return entry.Open();
    }

    /// <summary>
    /// Returns the relationships originating at the given source URI. Use "/" for the
    /// package root. Returns an empty enumerable if the source has no relationships.
    /// </summary>
    public IEnumerable<PackageRelationship> GetRelationships(string sourceUri)
    {
        EnsureReadMode();
        return _relationships.TryGetValue(sourceUri, out var list)
            ? (IEnumerable<PackageRelationship>)list
            : Array.Empty<PackageRelationship>();
    }

    // ---- Internal: write helpers ---------------------------------------------------

    /// <summary>
    /// Writes [Content_Types].xml at the root of the package. The standard requires this
    /// to declare every content type used. We emit one &lt;Override&gt; per part for simplicity
    /// (rather than &lt;Default&gt; entries keyed on extension), which is what Excel itself does
    /// for most parts. Default entries are added only for the universal "rels" and "xml" types.
    /// </summary>
    private void WriteContentTypes()
    {
        var entry = _zip.CreateEntry(ContentTypesPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = false,
        });

        writer.WriteStartDocument(standalone: true);
        writer.WriteStartElement("Types", ContentTypesNs);

        // Default content types every OOXML package needs.
        writer.WriteStartElement("Default", ContentTypesNs);
        writer.WriteAttributeString("Extension", "rels");
        writer.WriteAttributeString("ContentType",
            "application/vnd.openxmlformats-package.relationships+xml");
        writer.WriteEndElement();

        writer.WriteStartElement("Default", ContentTypesNs);
        writer.WriteAttributeString("Extension", "xml");
        writer.WriteAttributeString("ContentType", "application/xml");
        writer.WriteEndElement();

        // Per-part overrides.
        foreach (var part in _parts.Values)
        {
            writer.WriteStartElement("Override", ContentTypesNs);
            writer.WriteAttributeString("PartName", part.Uri);
            writer.WriteAttributeString("ContentType", part.ContentType);
            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // </Types>
        writer.WriteEndDocument();
        writer.Flush();
    }

    /// <summary>
    /// Writes one .rels file per source part that has relationships. The package root's
    /// relationships go to "/_rels/.rels"; a part at "/xl/workbook.xml" gets its relationships
    /// written to "/xl/_rels/workbook.xml.rels".
    /// </summary>
    private void WriteAllRelationships()
    {
        foreach (var kv in _relationships)
        {
            var sourceUri = kv.Key;
            var rels = kv.Value;
            if (rels.Count == 0) continue;

            var relsPath = GetRelsPathFor(sourceUri);
            var entry = _zip.CreateEntry(relsPath, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = false,
                CloseOutput = false,
            });

            writer.WriteStartDocument(standalone: true);
            writer.WriteStartElement("Relationships", RelationshipsNs);

            foreach (var rel in rels)
            {
                writer.WriteStartElement("Relationship", RelationshipsNs);
                writer.WriteAttributeString("Id", rel.Id);
                writer.WriteAttributeString("Type", rel.Type);
                writer.WriteAttributeString("Target", rel.Target);
                if (rel.IsExternal)
                    writer.WriteAttributeString("TargetMode", "External");
                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // </Relationships>
            writer.WriteEndDocument();
            writer.Flush();
        }
    }

    /// <summary>
    /// Returns the zip-entry name where a given source's .rels file is stored.
    /// Package root ("/") → "_rels/.rels". Otherwise the rels file lives in a "_rels"
    /// folder beside the part, with ".rels" appended to the part's filename.
    /// </summary>
    private static string GetRelsPathFor(string sourceUri)
    {
        if (sourceUri == "/" || sourceUri == "")
            return "_rels/.rels";

        // Split the source URI into directory and filename.
        // "/xl/workbook.xml"  →  directory: "xl",  filename: "workbook.xml"
        // ".rels" path:        →  "xl/_rels/workbook.xml.rels"
        var trimmed = sourceUri.TrimStart('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash < 0)
            return $"_rels/{trimmed}.rels";

        var dir = trimmed.Substring(0, lastSlash);
        var file = trimmed.Substring(lastSlash + 1);
        return $"{dir}/_rels/{file}.rels";
    }

    /// <summary>
    /// Computes the relationship Target attribute, given absolute source and target URIs.
    /// The Target is stored as a path relative to the source part's directory.
    /// For the package root, the target is stored without a leading slash but otherwise as-is.
    /// </summary>
    private static string MakeRelativeTarget(string sourceUri, string targetUri)
    {
        if (sourceUri == "/" || sourceUri == "")
        {
            // Root relationships use relative paths without leading slash.
            // E.g. target "/xl/workbook.xml" → "xl/workbook.xml".
            return targetUri.TrimStart('/');
        }

        // For non-root sources, compute a path relative to the source's directory.
        var sourceDir = GetDirectory(sourceUri);
        var targetTrimmed = targetUri.TrimStart('/');

        if (string.IsNullOrEmpty(sourceDir))
            return targetTrimmed;

        if (targetTrimmed.StartsWith(sourceDir + "/", StringComparison.Ordinal))
            return targetTrimmed.Substring(sourceDir.Length + 1);

        // Target is outside source's directory tree. Build a relative path with "../" segments.
        var sourceDirSegments = sourceDir.Split('/');
        return string.Concat(string.Join("/", new string[sourceDirSegments.Length].Fill("..")),
                             "/", targetTrimmed);
    }

    /// <summary>
    /// Returns the directory portion of a part URI, without leading slash.
    /// "/xl/workbook.xml" → "xl". "/sheet.xml" → "". "/xl/worksheets/sheet1.xml" → "xl/worksheets".
    /// </summary>
    private static string GetDirectory(string uri)
    {
        var trimmed = uri.TrimStart('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash < 0 ? "" : trimmed.Substring(0, lastSlash);
    }

    // ---- Internal: read helpers ----------------------------------------------------

    /// <summary>
    /// Parses [Content_Types].xml from the zip and populates _parts. Throws if the manifest
    /// is missing or malformed — without it, the package is not a valid OOXML file.
    /// </summary>
    private void LoadContentTypes()
    {
        var entry = _zip.GetEntry(ContentTypesPath)
            ?? throw new InvalidDataException("Package is missing [Content_Types].xml; not a valid OOXML package.");

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            CloseInput = false,
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.LocalName != "Override") continue;

            var partName = reader.GetAttribute("PartName");
            var contentType = reader.GetAttribute("ContentType");
            if (partName is null || contentType is null) continue;

            _parts[partName] = new PackagePart(partName, contentType);
        }
    }

    /// <summary>
    /// Discovers and parses every .rels file in the zip, populating _relationships.
    /// The package root's .rels lives at "_rels/.rels"; other parts' .rels files
    /// live at "&lt;dir&gt;/_rels/&lt;filename&gt;.rels". We map each .rels file back to its
    /// source URI by reversing this convention.
    /// </summary>
    private void LoadAllRelationships()
    {
        foreach (var entry in _zip.Entries)
        {
            var name = entry.FullName;
            if (!name.EndsWith(".rels", StringComparison.Ordinal)) continue;

            var sourceUri = RelsPathToSourceUri(name);
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                CloseInput = false,
            });

            var list = new List<PackageRelationship>();
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.LocalName != "Relationship") continue;

                var id = reader.GetAttribute("Id");
                var type = reader.GetAttribute("Type");
                var target = reader.GetAttribute("Target");
                var targetMode = reader.GetAttribute("TargetMode");
                if (id is null || type is null || target is null) continue;

                var isExternal = string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase);
                list.Add(new PackageRelationship(id, type, target, isExternal));
            }

            if (list.Count > 0)
                _relationships[sourceUri] = list;
        }
    }

    /// <summary>
    /// Reverses GetRelsPathFor: given a .rels entry name in the zip, returns the source URI
    /// it belongs to. "_rels/.rels" → "/". "xl/_rels/workbook.xml.rels" → "/xl/workbook.xml".
    /// </summary>
    private static string RelsPathToSourceUri(string relsZipEntry)
    {
        if (relsZipEntry == "_rels/.rels")
            return "/";

        // "xl/_rels/workbook.xml.rels"  →  "/xl/workbook.xml"
        // "xl/worksheets/_rels/sheet1.xml.rels"  →  "/xl/worksheets/sheet1.xml"
        var lastRels = relsZipEntry.LastIndexOf("/_rels/", StringComparison.Ordinal);
        if (lastRels < 0)
        {
            // "_rels/foo.xml.rels" at top level → "/foo.xml"
            const string prefix = "_rels/";
            if (relsZipEntry.StartsWith(prefix, StringComparison.Ordinal))
            {
                var fileName = relsZipEntry.Substring(prefix.Length);
                if (fileName.EndsWith(".rels", StringComparison.Ordinal))
                    fileName = fileName.Substring(0, fileName.Length - 5);
                return "/" + fileName;
            }
            return "/" + relsZipEntry; // best effort
        }

        var dir = relsZipEntry.Substring(0, lastRels);
        var file = relsZipEntry.Substring(lastRels + "/_rels/".Length);
        if (file.EndsWith(".rels", StringComparison.Ordinal))
            file = file.Substring(0, file.Length - 5);
        return "/" + dir + "/" + file;
    }

    // ---- Guards --------------------------------------------------------------------

    private void EnsureWriteMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_writeMode) throw new InvalidOperationException("Package was opened for read; write operations are not permitted.");
    }

    private void EnsureReadMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_writeMode) throw new InvalidOperationException("Package was created for write; read operations are not permitted until reopened.");
    }

    private void EnsureNotClosed()
    {
        if (_closed) throw new InvalidOperationException("Package has been closed; no further writes are permitted.");
    }

    // ---- Disposal ------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_writeMode && !_closed)
        {
            // Defensive: auto-close on dispose so callers don't leave packages half-written.
            // We swallow exceptions here only to avoid masking the original exception that may
            // have prevented an explicit Close() call. Callers should still prefer to call
            // Close() explicitly so any errors surface cleanly.
            try { Close(); } catch { /* surface nothing during Dispose */ }
        }

        _zip.Dispose();
    }
}

/// <summary>
/// Small array helper used internally. Lives here to keep step 1 a single file plus its two types.
/// </summary>
internal static class ArrayFillExtensions
{
    public static string[] Fill(this string[] arr, string value)
    {
        for (int i = 0; i < arr.Length; i++) arr[i] = value;
        return arr;
    }
}
