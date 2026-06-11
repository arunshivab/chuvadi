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
}

/// <summary>
/// Read-only access to a .docx — body paragraphs (text + style name) and tables (text
/// cells), without building an editable model. Handles encrypted files when a password is
/// supplied. Content in headers/footers, footnotes, comments, and text boxes is not
/// traversed in v1.
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
    private readonly DocxReaderOptions _options;
    private readonly MemoryStream? _decryptedBuffer;
    private bool _disposed;

    private DocxReader(OoxmlPackage package, string documentUri, DocxReaderOptions options, MemoryStream? decryptedBuffer)
    {
        _package = package;
        _documentUri = documentUri;
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

            return new DocxReader(pkg, documentUri, options, decrypted);
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

    // ---- Reading --------------------------------------------------------------------

    /// <summary>One body paragraph: concatenated run text + the paragraph style id ("Normal", "Heading1", ...).</summary>
    public readonly record struct ParagraphInfo(string Text, string StyleId);

    /// <summary>Streams body paragraphs in document order. Table-cell paragraphs are not
    /// included here — use <see cref="Tables"/> for tables.</summary>
    public IEnumerable<ParagraphInfo> Paragraphs()
    {
        EnsureNotDisposed();
        using var s = OpenDocumentPart();
        using var r = CreateXml(s);

        // Manual cursor control: ReadSubtree/Skip already advance the reader, so the loop
        // must not unconditionally Read() after them (the classic skipped-node bug).
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
                    // Outer reader now sits on the subtree's end node; step past it.
                    r.Read();
                    yield return info;
                    continue;
                }
            }
            if (!r.Read()) break;
        }
    }

    /// <summary>Streams body tables in document order as text grids (rows × cells; a
    /// cell's paragraphs are joined with newlines).</summary>
    public IEnumerable<string[][]> Tables()
    {
        EnsureNotDisposed();
        using var s = OpenDocumentPart();
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

    /// <summary>All body text — paragraphs and table cells in document order, newline-separated.</summary>
    public string ExtractText()
    {
        EnsureNotDisposed();
        var sb = new StringBuilder();
        using var s = OpenDocumentPart();
        using var r = CreateXml(s);

        if (!r.Read()) return string.Empty;
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
        return sb.ToString();
    }

    // ---- Internal parsing (over an isolated subtree reader) ---------------------------

    internal static ParagraphInfo ParseParagraph(XmlReader sub)
    {
        var sb = new StringBuilder();
        string style = "Normal";
        // Manual advance: ReadElementContentAsString moves the cursor itself, so the loop
        // must not Read() again after it (would silently skip the following node).
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

        if (!sub.Read()) return Array.Empty<string[]>();
        while (!sub.EOF)
        {
            if (sub.NodeType == XmlNodeType.Element && sub.NamespaceURI == DocumentSerializer.W)
            {
                switch (sub.LocalName)
                {
                    case "tr":
                        currentRow = new List<string>();
                        break;
                    case "tc":
                        cellText = new StringBuilder();
                        firstCellParagraph = true;
                        break;
                    case "p" when cellText is not null:
                        if (!firstCellParagraph) cellText.Append('\n');
                        firstCellParagraph = false;
                        break;
                    case "t" when cellText is not null:
                        cellText.Append(sub.ReadElementContentAsString());
                        continue; // cursor already advanced past </t>
                }
            }
            else if (sub.NodeType == XmlNodeType.EndElement && sub.NamespaceURI == DocumentSerializer.W)
            {
                if (sub.LocalName == "tc" && currentRow is not null && cellText is not null)
                {
                    currentRow.Add(cellText.ToString());
                    cellText = null;
                }
                else if (sub.LocalName == "tr" && currentRow is not null)
                {
                    rows.Add(currentRow.ToArray());
                    currentRow = null;
                }
            }
            if (!sub.Read()) break;
        }
        return rows.ToArray();
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
