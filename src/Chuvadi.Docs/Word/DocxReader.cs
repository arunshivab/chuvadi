using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Chuvadi.Docs.Internal;
using Chuvadi.Internal;

namespace Chuvadi.Docs.Word;

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
///
/// <para><b>Untrusted input.</b> Opening applies the resource limits in
/// <see cref="DocxReaderOptions"/> (zip-bomb, nesting-depth and spin-count guards) and
/// throws <see cref="DocxSecurityLimitException"/> on a breach. <see cref="HasMacros"/> and
/// <see cref="GetActiveContent()"/> report macros, ActiveX, embedded objects, DDE fields and
/// remote references — detected only, never executed or fetched.</para>
///
/// <para><b>Cancellation.</b> Every reading entry point has an overload taking a
/// <see cref="CancellationToken"/>. The token is observed on every XML node and every
/// decompression buffer fill, so cancellation takes effect within milliseconds and surfaces
/// as <see cref="OperationCanceledException"/>. The one uninterruptible step is password key
/// derivation for encrypted files, which <see cref="DocxReaderOptions.MaxSpinCount"/> bounds.</para>
/// </summary>
public sealed partial class DocxReader : IDisposable
{
    private const string RelTypeVba = "http://schemas.microsoft.com/office/2006/relationships/vbaProject";

    private readonly OoxmlPackage _package;
    private readonly string _documentUri;
    private readonly List<string> _headerUris;
    private readonly List<string> _footerUris;
    private readonly List<string> _noteUris;
    private readonly DocxReaderOptions _options;
    private readonly MemoryStream? _ownedBuffer;
    private readonly PackageScanResult _scan;
    private readonly string _mainContentType;
    private bool _disposed;

    private DocxReader(OoxmlPackage package, string documentUri,
        List<string> headerUris, List<string> footerUris, List<string> noteUris,
        DocxReaderOptions options, MemoryStream? ownedBuffer,
        PackageScanResult scan, string mainContentType)
    {
        _package = package;
        _documentUri = documentUri;
        _headerUris = headerUris;
        _footerUris = footerUris;
        _noteUris = noteUris;
        _options = options;
        _ownedBuffer = ownedBuffer;
        _scan = scan;
        _mainContentType = mainContentType;
    }

    // ---- Open -----------------------------------------------------------------------

    /// <summary>Opens a .docx file (encrypted when <paramref name="password"/> is given).</summary>
    public static DocxReader Open(string path, string? password = null, DocxReaderOptions? options = null)
        => Open(path, password, options, CancellationToken.None);

    /// <summary>Opens a .docx file, observing <paramref name="cancellationToken"/>.</summary>
    public static DocxReader Open(string path, string? password, DocxReaderOptions? options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return OpenCore(fs, password, options, cancellationToken);
    }

    /// <summary>
    /// Opens a .docx from a stream. The reader TAKES OWNERSHIP of the stream: it is disposed
    /// when the reader is disposed, or before this method returns when opening fails (including
    /// on a limit breach or cancellation). Non-seekable streams are buffered into memory first.
    /// </summary>
    public static DocxReader Open(Stream input, string? password = null, DocxReaderOptions? options = null)
        => OpenCore(input, password, options, CancellationToken.None);

    /// <summary>Opens a .docx from a stream, observing <paramref name="cancellationToken"/>. Ownership as in <see cref="Open(Stream, string?, DocxReaderOptions?)"/>.</summary>
    public static DocxReader Open(Stream input, string? password, DocxReaderOptions? options, CancellationToken cancellationToken)
        => OpenCore(input, password, options, cancellationToken);

    private static DocxReader OpenCore(Stream input, string? password, DocxReaderOptions? options, CancellationToken ct)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        options ??= new DocxReaderOptions();

        MemoryStream? buffered = null;
        MemoryStream? decrypted = null;
        OoxmlPackage? pkg = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            Stream source = PackageGuard.EnsureSeekable(input, options, ct, out buffered);
            Stream packageStream = source;

            if (Chuvadi.Internal.Crypto.EncryptedPackageReader.IsEncryptedPackage(source))
            {
                byte[] plaintext = PackageGuard.Decrypt(source, password, options, ct);
                ct.ThrowIfCancellationRequested();
                decrypted = new MemoryStream(plaintext);
                packageStream = decrypted;
            }

            var scan = PackageGuard.Scan(packageStream, options, ct);
            ct.ThrowIfCancellationRequested();

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

            string mainContentType = string.Empty;
            foreach (var part in pkg.Parts)
            {
                if (part.Uri == documentUri) { mainContentType = part.ContentType; break; }
            }

            // Header/footer/footnote/endnote part URIs from the document's own relationships.
            var headerUris = new List<string>();
            var footerUris = new List<string>();
            var noteUris = new List<string>();
            foreach (var rel in pkg.GetRelationships(documentUri))
            {
                if (rel.IsExternal) continue;
                if (rel.Type.EndsWith("/header", StringComparison.Ordinal))
                    headerUris.Add(ResolveUri(documentUri, rel.Target));
                else if (rel.Type.EndsWith("/footer", StringComparison.Ordinal))
                    footerUris.Add(ResolveUri(documentUri, rel.Target));
                else if (rel.Type.EndsWith("/footnotes", StringComparison.Ordinal)
                      || rel.Type.EndsWith("/endnotes", StringComparison.Ordinal))
                    noteUris.Add(ResolveUri(documentUri, rel.Target));
            }

            // The package owns (and disposes) whichever stream backs it. When that is an
            // in-memory copy, the original input is no longer needed.
            MemoryStream? owned = decrypted ?? buffered;
            if (decrypted is not null && buffered is not null) { buffered.Dispose(); buffered = null; }
            if (owned is not null) input.Dispose();

            return new DocxReader(pkg, documentUri, headerUris, footerUris, noteUris,
                options, owned, scan, mainContentType);
        }
        catch (Exception ex)
        {
            pkg?.Dispose();
            decrypted?.Dispose();
            buffered?.Dispose();
            input.Dispose();
            if (ex is DocxFormatException or DocxSecurityLimitException or DocxPasswordRequiredException
                or OperationCanceledException)
                throw;
            throw new DocxFormatException("Could not open the file as a docx package.", ex);
        }
    }

    /// <summary>Resolves a relationship target relative to the source part URI.</summary>
    private static string ResolveUri(string sourceUri, string target)
    {
        // Targets are relative to the source part's directory (e.g. /word/).
        if (target.StartsWith('/')) return target;
        var dir = sourceUri.Contains('/') ? sourceUri[..(sourceUri.LastIndexOf('/') + 1)] : "/";
        var combined = dir + target;
        if (!combined.Contains("/../", StringComparison.Ordinal) && !combined.Contains("/./", StringComparison.Ordinal))
            return combined;
        // Normalise "a/b/../c" segments.
        var stack = new List<string>();
        foreach (var seg in combined.Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
            stack.Add(seg);
        }
        return "/" + string.Join("/", stack);
    }

    // ---- Active content (detect, never execute) ---------------------------------------

    /// <summary>
    /// True when the document carries macros: a macro-enabled main content type (.docm/.dotm)
    /// or a VBA project part. Determined at open from the package structure — no part is
    /// parsed and nothing is executed.
    /// </summary>
    public bool HasMacros => IsMacroEnabledPackage || ContainsVbaProject;

    /// <summary>True when the package contains a VBA project part (<c>vbaProject.bin</c>).</summary>
    public bool ContainsVbaProject
    {
        get
        {
            EnsureNotDisposed();
            if (_scan.ContainsVbaProject) return true;
            foreach (var rel in _package.GetRelationships(_documentUri))
                if (rel.Type == RelTypeVba) return true;
            return false;
        }
    }

    /// <summary>True when the main part's content type is macro-enabled (a .docm or .dotm package).</summary>
    public bool IsMacroEnabledPackage
        => _mainContentType.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase);

    /// <summary>The main document part's content type exactly as declared in the package.</summary>
    public string MainPartContentType => _mainContentType;

    /// <summary>Full active/remote-content report. See <see cref="DocxActiveContent"/>.</summary>
    public DocxActiveContent GetActiveContent() => GetActiveContent(CancellationToken.None);

    /// <summary>Full active/remote-content report, observing <paramref name="cancellationToken"/>.</summary>
    public DocxActiveContent GetActiveContent(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        bool activeX = _scan.HasActiveXParts;
        bool embedded = _scan.HasEmbeddedObjects;
        bool altChunks = false, remoteTemplate = false, externalResources = false;
        var external = new List<DocxExternalReference>();

        var sources = new List<string> { "/" };
        foreach (var part in _package.Parts) sources.Add(part.Uri);

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var rel in _package.GetRelationships(source))
            {
                var type = rel.Type;
                if (type.EndsWith("/control", StringComparison.Ordinal)) activeX = true;
                else if (!rel.IsExternal && (type.EndsWith("/oleObject", StringComparison.Ordinal)
                      || type.EndsWith("/package", StringComparison.Ordinal))) embedded = true;
                else if (type.EndsWith("/aFChunk", StringComparison.Ordinal)) altChunks = true;

                if (!rel.IsExternal) continue;
                bool isHyperlink = type.EndsWith("/hyperlink", StringComparison.Ordinal);
                external.Add(new DocxExternalReference(source, type, rel.Target, isHyperlink));
                if (!isHyperlink) externalResources = true;
                if (type.EndsWith("/attachedTemplate", StringComparison.Ordinal)) remoteTemplate = true;
            }
        }

        // DDE can hide in any WordprocessingML part that holds runs: body, headers, footers,
        // notes, comments, the glossary document… Scan every such part (styles, settings,
        // numbering and font tables simply contain no fields).
        bool dde = false;
        var scanParts = new List<string>();
        foreach (var part in _package.Parts)
        {
            var ctype = part.ContentType;
            if (ctype.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
                || ctype.Contains("ms-word", StringComparison.OrdinalIgnoreCase))
                scanParts.Add(part.Uri);
        }
        if (!scanParts.Contains(_documentUri)) scanParts.Insert(0, _documentUri);
        foreach (var uri in scanParts)
        {
            if (ScanPartForDde(uri, cancellationToken)) { dde = true; break; }
        }

        return new DocxActiveContent
        {
            IsMacroEnabledPackage = IsMacroEnabledPackage,
            ContainsVbaProject = ContainsVbaProject,
            HasActiveXControls = activeX,
            HasEmbeddedObjects = embedded,
            HasAltChunks = altChunks,
            HasDdeFields = dde,
            HasRemoteTemplate = remoteTemplate,
            HasExternalResources = externalResources,
            ExternalReferences = external,
        };
    }

    /// <summary>Scans one part's field instructions (complex and simple fields) for DDE/DDEAUTO.</summary>
    private bool ScanPartForDde(string uri, CancellationToken ct)
    {
        Stream? s = TryOpenPart(uri, ct);
        if (s is null) return false;
        using var _ = s;
        using var r = CreateXml(s, uri, ct);

        // One entry per open complex field; null once its instruction is complete.
        var fieldStack = new List<StringBuilder?>();
        if (!r.Read()) return false;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == DocumentSerializer.W)
            {
                switch (r.LocalName)
                {
                    case "fldSimple":
                        if (IsDdeInstruction(r.GetAttribute("instr", DocumentSerializer.W))) return true;
                        break;
                    case "fldChar":
                        switch (r.GetAttribute("fldCharType", DocumentSerializer.W))
                        {
                            case "begin":
                                fieldStack.Add(new StringBuilder());
                                break;
                            case "separate":
                                if (fieldStack.Count > 0)
                                {
                                    if (IsDdeInstruction(fieldStack[^1]?.ToString())) return true;
                                    fieldStack[^1] = null;
                                }
                                break;
                            case "end":
                                if (fieldStack.Count > 0)
                                {
                                    if (IsDdeInstruction(fieldStack[^1]?.ToString())) return true;
                                    fieldStack.RemoveAt(fieldStack.Count - 1);
                                }
                                break;
                        }
                        break;
                    case "instrText":
                        var text = r.ReadElementContentAsString();
                        if (fieldStack.Count > 0) fieldStack[^1]?.Append(text);
                        else if (IsDdeInstruction(text)) return true;
                        continue; // cursor already advanced
                }
            }
            if (!r.Read()) break;
        }
        return false;
    }

    /// <summary>True when the field type (first token, quotes stripped) is DDE or DDEAUTO.</summary>
    internal static bool IsDdeInstruction(string? instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction)) return false;
        var span = instruction.AsSpan().TrimStart();
        int end = 0;
        while (end < span.Length && !char.IsWhiteSpace(span[end])) end++;
        var token = span[..end].Trim('"');
        return token.Equals("DDE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("DDEAUTO", StringComparison.OrdinalIgnoreCase);
    }

    // ---- Reading --------------------------------------------------------------------

    /// <summary>One body/header/footer paragraph: concatenated run text + paragraph style id.</summary>
    public readonly record struct ParagraphInfo(string Text, string StyleId);

    /// <summary>
    /// Streams paragraphs in document order: body first, then headers, then footers
    /// (when <see cref="DocxReaderOptions.IncludeHeadersAndFooters"/> is true).
    /// Table-cell paragraphs are not included here — use <see cref="Tables()"/> for tables.
    /// </summary>
    public IEnumerable<ParagraphInfo> Paragraphs() => Paragraphs(CancellationToken.None);

    /// <summary>As <see cref="Paragraphs()"/>, observing <paramref name="cancellationToken"/> while enumerating.</summary>
    public IEnumerable<ParagraphInfo> Paragraphs(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        return ParagraphsIterator(cancellationToken);
    }

    private IEnumerable<ParagraphInfo> ParagraphsIterator(CancellationToken ct)
    {
        foreach (var uri in ReadOrder())
            foreach (var info in ReadParagraphsFromPart(uri, ct))
                yield return info;
    }

    /// <summary>
    /// Streams tables in document order: body first, then headers, then footers
    /// (when <see cref="DocxReaderOptions.IncludeHeadersAndFooters"/> is true).
    /// </summary>
    public IEnumerable<string[][]> Tables() => Tables(CancellationToken.None);

    /// <summary>As <see cref="Tables()"/>, observing <paramref name="cancellationToken"/> while enumerating.</summary>
    public IEnumerable<string[][]> Tables(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        return TablesIterator(cancellationToken);
    }

    private IEnumerable<string[][]> TablesIterator(CancellationToken ct)
    {
        foreach (var uri in ReadOrder())
            foreach (var grid in ReadTablesFromPart(uri, ct))
                yield return grid;
    }

    /// <summary>
    /// All text — paragraphs and table cells from body, headers, and footers
    /// (when <see cref="DocxReaderOptions.IncludeHeadersAndFooters"/> is true),
    /// newline-separated.
    /// </summary>
    public string ExtractText() => ExtractText(CancellationToken.None);

    /// <summary>As <see cref="ExtractText()"/>, observing <paramref name="cancellationToken"/>.</summary>
    public string ExtractText(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        var sb = new StringBuilder();
        foreach (var uri in ReadOrder()) AppendTextFromPart(uri, sb, cancellationToken);
        return sb.ToString();
    }

    /// <summary>Body, then headers and footers when enabled.</summary>
    private IEnumerable<string> ReadOrder()
    {
        yield return _documentUri;
        if (!_options.IncludeHeadersAndFooters) yield break;
        foreach (var uri in _headerUris) yield return uri;
        foreach (var uri in _footerUris) yield return uri;
    }

    // ---- Per-part scanning ----------------------------------------------------------

    private IEnumerable<ParagraphInfo> ReadParagraphsFromPart(string uri, CancellationToken ct)
    {
        Stream? s = TryOpenPart(uri, ct);
        if (s is null) yield break;
        using var _ = s;
        using var r = CreateXml(s, uri, ct);

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

    private IEnumerable<string[][]> ReadTablesFromPart(string uri, CancellationToken ct)
    {
        Stream? s = TryOpenPart(uri, ct);
        if (s is null) yield break;
        using var _ = s;
        using var r = CreateXml(s, uri, ct);

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

    private void AppendTextFromPart(string uri, StringBuilder sb, CancellationToken ct)
    {
        Stream? s = TryOpenPart(uri, ct);
        if (s is null) return;
        using var _ = s;
        using var r = CreateXml(s, uri, ct);

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
            if (DocumentLoader.IsAlternateRepresentation(sub) || IsRemovedContent(sub))
            {
                sub.Skip(); // duplicate (mc:Fallback) or moved-away/deleted content
                continue;
            }
            if (sub.NodeType == XmlNodeType.Element && sub.NamespaceURI == DocumentSerializer.W)
            {
                switch (sub.LocalName)
                {
                    case "pPr":
                        // Paragraph properties hold no text — notably w:tabs/w:tab are tab-stop
                        // DEFINITIONS, not tab characters. Take the style id (not the one inside
                        // w:pPrChange history) and skip the rest.
                        using (var pPr = sub.ReadSubtree())
                        {
                            pPr.Read();
                            int childDepth = pPr.Depth + 1;
                            while (pPr.Read())
                            {
                                if (pPr.NodeType == XmlNodeType.Element && pPr.Depth == childDepth
                                    && pPr.LocalName == "pStyle" && pPr.NamespaceURI == DocumentSerializer.W)
                                    style = pPr.GetAttribute("val", DocumentSerializer.W) ?? style;
                            }
                        }
                        break; // cursor on </w:pPr>; the Read below moves past it
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
                    case "noBreakHyphen":
                        sb.Append('\u2011');
                        break;
                    case "softHyphen":
                        sb.Append('\u00AD');
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
            if (DocumentLoader.IsAlternateRepresentation(sub) || IsRemovedContent(sub))
            {
                sub.Skip();
                continue;
            }
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

    /// <summary><c>w:moveFrom</c> / <c>w:del</c>: content that is no longer in the current text.</summary>
    private static bool IsRemovedContent(XmlReader r)
        => r.NodeType == XmlNodeType.Element && r.NamespaceURI == DocumentSerializer.W
           && (r.LocalName == "moveFrom" || r.LocalName == "del");

    /// <summary>
    /// Opens a part with the per-part size cap and the cancellation check applied. Returns
    /// null when the part is referenced but missing (skipped gracefully).
    /// </summary>
    private Stream? TryOpenPart(string uri, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream raw;
        try
        {
            raw = _package.OpenPart(uri);
        }
        catch
        {
            return null;
        }
        return GuardedPartStream.Wrap(raw, _options.MaxPartBytes, ct, uri);
    }

    private XmlReader CreateXml(Stream s, string partUri, CancellationToken ct)
        => GuardedXmlReader.Create(s, _options.MaxXmlDepth, ct, partUri);

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _package.Dispose(); } catch { }
        try { _ownedBuffer?.Dispose(); } catch { }
    }
}
