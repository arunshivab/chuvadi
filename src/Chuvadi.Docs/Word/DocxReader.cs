using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Chuvadi.Docs.Internal;
using Chuvadi.Internal;

namespace Chuvadi.Docs.Word;

/// <summary>Options for <see cref="DocxReader"/>.</summary>
public sealed class DocxReaderOptions
{
    /// <summary>
    /// Maximum DECOMPRESSED size, in bytes, permitted for any single package part.
    /// Protects against decompression bombs when opening untrusted files. Exceeding the
    /// limit throws <see cref="InvalidDataException"/>. Null (default) = unlimited.
    /// </summary>
    public long? MaxPartBytes { get; set; }

    /// <summary>
    /// When true (default), <see cref="DocxReader.Paragraphs"/>, <see cref="DocxReader.Tables"/>
    /// and <see cref="DocxReader.ExtractText"/> include content from headers and footers
    /// in addition to the document body. Set to false to read only the body.
    /// </summary>
    public bool IncludeHeadersAndFooters { get; set; } = true;
}

/// <summary>
/// Read-only access to a .docx — body paragraphs (text + style name) and tables (text
/// cells), without building an editable model. Handles encrypted files when a password is
/// supplied. By default, headers and footers are included in all read operations.
///
/// <code>
/// using var r = DocxReader.Open("contract.docx", password: "secret");
/// foreach (var p in r.Paragraphs())
///     Console.WriteLine($"[{p.StyleId}] {p.Text}");
/// string everything = r.ExtractText();
/// </code>
/// </summary>
public sealed class DocxReader : IDisposable
{
    private readonly OoxmlPackage _package;
    private readonly string _documentUri;
    private readonly List<string> _headerUris;
    private readonly List<string> _footerUris;
    private readonly DocxReaderOptions _options;
    private readonly MemoryStream? _decryptedBuffer;
    private bool _disposed;

    private DocxReader(OoxmlPackage package, string documentUri,
        List<string> headerUris, List<string> footerUris,
        DocxReaderOptions options, MemoryStream? decryptedBuffer)
    {
        _package = package;
        _documentUri = documentUri;
        _headerUris = headerUris;
        _footerUris = footerUris;
        _options = options;
        _decryptedBuffer = decryptedBuffer;
    }

    // ---- Open -----------------------------------------------------------------------

    public static DocxReader Open(string path, string? password = null, DocxReaderOptions? options = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return Open(fs, password, options, ownsStream: true);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    public static DocxReader Open(Stream input, string? password = null, DocxReaderOptions? options = null)
        => Open(input, password, options, ownsStream: false);

    private static DocxReader Open(Stream input, string? password, DocxReaderOptions? options, bool ownsStream)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        options ??= new DocxReaderOptions();

        MemoryStream? decrypted = null;
        Stream packageStream = input;
        if (Chuvadi.Internal.Crypto.EncryptedPackageReader.IsEncryptedPackage(input))
        {
            byte[] plaintext;
            try
            {
                plaintext = Chuvadi.Internal.Crypto.EncryptedPackageReader.DecryptToPlaintextPackage(input, password);
            }
            catch (Chuvadi.Internal.Crypto.PackagePasswordException ex)
            {
                if (ownsStream) input.Dispose();
                throw new DocxPasswordRequiredException(ex.Message, ex);
            }
            decrypted = new MemoryStream(plaintext);
            packageStream = decrypted;
        }

        OoxmlPackage? pkg = null;
        try
        {
            pkg = OoxmlPackage.Open(packageStream);

            // Locate the main document part via the root relationship.
            string? documentUri = null;
            foreach (var rel in pkg.GetRelationships("/"))
            {
                if (rel.Type.EndsWith("/officeDocument", StringComparison.Ordinal))
                {
                    documentUri = "/" + rel.Target.TrimStart('/');
                    break;
                }
            }
            if (documentUri is null)
                throw new DocxFormatException("Package has no officeDocument relationship; not a valid docx.");

            // Collect header and footer part URIs from the document's own relationships.
            // These are the parts that contain header/footer content including tables.
            var headerUris = new List<string>();
            var footerUris = new List<string>();
            foreach (var rel in pkg.GetRelationships(documentUri))
            {
                if (rel.Type.EndsWith("/header", StringComparison.Ordinal))
                    headerUris.Add(ResolveUri(documentUri, rel.Target));
                else if (rel.Type.EndsWith("/footer", StringComparison.Ordinal))
                    footerUris.Add(ResolveUri(documentUri, rel.Target));
            }

            return new DocxReader(pkg, documentUri, headerUris, footerUris, options, decrypted);
        }
        catch (DocxFormatException)
        {
            pkg?.Dispose();
            decrypted?.Dispose();
            if (ownsStream) input.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            pkg?.Dispose();
            decrypted?.Dispose();
            if (ownsStream) input.Dispose();
            throw new DocxFormatException("Could not open the file as a docx package.", ex);
        }
    }

    /// <summary>Resolves a relationship target relative to the source part URI.</summary>
    private static string ResolveUri(string sourceUri, string target)
    {
        // Targets are relative to the source part's directory (e.g. /word/).
        if (target.StartsWith('/')) return target;
        var dir = sourceUri.Contains('/') ? sourceUri[..(sourceUri.LastIndexOf('/') + 1)] : "/";
        return dir + target;
    }

    // ---- Reading --------------------------------------------------------------------

    /// <summary>One body/header/footer paragraph: concatenated run text + paragraph style id.</summary>
    public readonly record struct ParagraphInfo(string Text, string StyleId);

    /// <summary>
    /// Streams paragraphs in document order: body first, then headers, then footers
    /// (when <see cref="DocxReaderOptions.IncludeHeadersAndFooters"/> is true).
    /// Table-cell paragraphs are not included here — use <see cref="Tables"/> for tables.
    /// </summary>
    public IEnumerable<ParagraphInfo> Paragraphs()
    {
        EnsureNotDisposed();
        foreach (var info in ReadParagraphsFromPart(_documentUri))
            yield return info;

        if (!_options.IncludeHeadersAndFooters) yield break;
        foreach (var uri in _headerUris)
            foreach (var info in ReadParagraphsFromPart(uri))
                yield return info;
        foreach (var uri in _footerUris)
            foreach (var info in ReadParagraphsFromPart(uri))
                yield return info;
    }

    /// <summary>
    /// Streams tables in document order: body first, then headers, then footers
    /// (when <see cref="DocxReaderOptions.IncludeHeadersAndFooters"/> is true).
    /// </summary>
    public IEnumerable<string[][]> Tables()
    {
        EnsureNotDisposed();
        foreach (var grid in ReadTablesFromPart(_documentUri))
            yield return grid;

        if (!_options.IncludeHeadersAndFooters) yield break;
        foreach (var uri in _headerUris)
            foreach (var grid in ReadTablesFromPart(uri))
                yield return grid;
        foreach (var uri in _footerUris)
            foreach (var grid in ReadTablesFromPart(uri))
                yield return grid;
    }

    /// <summary>
    /// All text — paragraphs and table cells from body, headers, and footers
    /// (when <see cref="DocxReaderOptions.IncludeHeadersAndFooters"/> is true),
    /// newline-separated.
    /// </summary>
    public string ExtractText()
    {
        EnsureNotDisposed();
        var sb = new StringBuilder();
        AppendTextFromPart(_documentUri, sb);

        if (!_options.IncludeHeadersAndFooters) return sb.ToString();
        foreach (var uri in _headerUris) AppendTextFromPart(uri, sb);
        foreach (var uri in _footerUris) AppendTextFromPart(uri, sb);
        return sb.ToString();
    }

    // ---- Per-part scanning ----------------------------------------------------------

    private IEnumerable<ParagraphInfo> ReadParagraphsFromPart(string uri)
    {
        Stream? s = TryOpenPart(uri);
        if (s is null) yield break;
        using var _ = s;
        using var r = CreateXml(s);

        if (!r.Read()) yield break;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == DocumentSerializer.W)
            {
                if (r.LocalName == "tbl") { r.Skip(); continue; }
                if (r.LocalName == "p")
                {
                    ParagraphInfo info;
                    using (var sub = r.ReadSubtree()) info = ParseParagraph(sub);
                    r.Read();
                    yield return info;
                    continue;
                }
            }
            if (!r.Read()) break;
        }
    }

    private IEnumerable<string[][]> ReadTablesFromPart(string uri)
    {
        Stream? s = TryOpenPart(uri);
        if (s is null) yield break;
        using var _ = s;
        using var r = CreateXml(s);

        if (!r.Read()) yield break;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "tbl" && r.NamespaceURI == DocumentSerializer.W)
            {
                string[][] grid;
                using (var sub = r.ReadSubtree()) grid = ParseTable(sub);
                r.Read();
                yield return grid;
                continue;
            }
            if (!r.Read()) break;
        }
    }

    private void AppendTextFromPart(string uri, StringBuilder sb)
    {
        Stream? s = TryOpenPart(uri);
        if (s is null) return;
        using var _ = s;
        using var r = CreateXml(s);

        if (!r.Read()) return;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == DocumentSerializer.W)
            {
                if (r.LocalName == "tbl")
                {
                    string[][] grid;
                    using (var sub = r.ReadSubtree()) grid = ParseTable(sub);
                    r.Read();
                    foreach (var row in grid)
                        foreach (var cell in row)
                            sb.AppendLine(cell);
                    continue;
                }
                if (r.LocalName == "p")
                {
                    ParagraphInfo info;
                    using (var sub = r.ReadSubtree()) info = ParseParagraph(sub);
                    r.Read();
                    sb.AppendLine(info.Text);
                    continue;
                }
            }
            if (!r.Read()) break;
        }
    }

    // ---- Internal parsing (over an isolated subtree reader) ---------------------------

    internal static ParagraphInfo ParseParagraph(XmlReader sub)
    {
        var sb = new StringBuilder();
        string style = "Normal";
        if (!sub.Read()) return new ParagraphInfo(string.Empty, style);
        while (!sub.EOF)
        {
            if (sub.NodeType == XmlNodeType.Element && sub.NamespaceURI == DocumentSerializer.W)
            {
                switch (sub.LocalName)
                {
                    case "pStyle":
                        style = sub.GetAttribute("val", DocumentSerializer.W) ?? style;
                        break;
                    case "t":
                        sb.Append(sub.ReadElementContentAsString());
                        continue; // cursor already advanced past </t>
                    case "br":
                    case "cr":
                        sb.Append('\n');
                        break;
                    case "tab":
                        sb.Append('\t');
                        break;
                }
            }
            if (!sub.Read()) break;
        }
        return new ParagraphInfo(sb.ToString(), style);
    }

    internal static string[][] ParseTable(XmlReader sub)
    {
        var rows = new List<string[]>();
        List<string>? currentRow = null;
        StringBuilder? cellText = null;
        bool firstCellParagraph = true;
        // Track nesting depth relative to the root <w:tbl> we were handed.
        // depth 0 = the root tbl element itself; depth 1 = direct tr/tc children.
        // Any w:tbl encountered at depth >= 1 is a NESTED table — we collect its
        // text inline (so the cell is not empty) but do not let its tr/tc/t nodes
        // corrupt the outer row/cell state machine.
        int tblDepth = 0;

        if (!sub.Read()) return Array.Empty<string[]>();
        while (!sub.EOF)
        {
            if (sub.NodeType == XmlNodeType.Element && sub.NamespaceURI == DocumentSerializer.W)
            {
                bool isEmpty = sub.IsEmptyElement;
                switch (sub.LocalName)
                {
                    case "tbl":
                        tblDepth++;
                        break;
                    case "tr" when tblDepth <= 1:
                        currentRow = new List<string>();
                        break;
                    case "tc" when tblDepth <= 1:
                        cellText = new StringBuilder();
                        firstCellParagraph = true;
                        break;
                    case "p" when cellText is not null && tblDepth <= 1:
                        if (!firstCellParagraph) cellText.Append('\n');
                        firstCellParagraph = false;
                        break;
                    case "t" when cellText is not null:
                        // Collect text regardless of nesting depth — nested table
                        // text belongs in the containing cell.
                        cellText.Append(sub.ReadElementContentAsString());
                        continue; // cursor already advanced past </t>
                }
                // Empty elements have no matching EndElement — adjust depth now.
                if (isEmpty && sub.LocalName == "tbl") tblDepth--;
            }
            else if (sub.NodeType == XmlNodeType.EndElement && sub.NamespaceURI == DocumentSerializer.W)
            {
                switch (sub.LocalName)
                {
                    case "tbl":
                        tblDepth--;
                        break;
                    case "tc" when tblDepth <= 1 && currentRow is not null && cellText is not null:
                        currentRow.Add(cellText.ToString());
                        cellText = null;
                        break;
                    case "tr" when tblDepth <= 1 && currentRow is not null:
                        rows.Add(currentRow.ToArray());
                        currentRow = null;
                        break;
                }
            }
            if (!sub.Read()) break;
        }
        return rows.ToArray();
    }

    // ---- Helpers --------------------------------------------------------------------

    private Stream? TryOpenPart(string uri)
    {
        try
        {
            var raw = _package.OpenPart(uri);
            return _options.MaxPartBytes is long max
                ? new LimitedReadStream(raw, max, $"package part '{uri}'")
                : raw;
        }
        catch
        {
            // Part listed in relationships but not present in zip — skip gracefully.
            return null;
        }
    }

    private Stream OpenDocumentPart()
    {
        var raw = _package.OpenPart(_documentUri);
        return _options.MaxPartBytes is long max
            ? new LimitedReadStream(raw, max, $"package part '{_documentUri}'")
            : raw;
    }

    private static XmlReader CreateXml(Stream s)
        => XmlReader.Create(s, new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            CloseInput = false,
        });

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _package.Dispose(); } catch { }
        try { _decryptedBuffer?.Dispose(); } catch { }
    }
}
