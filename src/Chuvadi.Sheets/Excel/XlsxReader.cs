using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Chuvadi.Sheets.Internal;
using Chuvadi.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Streaming xlsx reader. Open with <see cref="Open(string, XlsxReaderOptions?)"/>, enumerate
/// rows via <see cref="Sheet(string)"/> or <see cref="Sheet(int)"/>, then dispose.
///
/// <code>
/// using var reader = XlsxReader.Open("file.xlsx");
/// var sheet = reader.Sheet("Data");
/// foreach (var row in sheet.Rows)
/// {
///     var id = row.GetInt32("Id");
///     var name = row.GetString("Name");
/// }
/// </code>
///
/// The shared strings table and styles are loaded fully into memory at open time (small
/// relative to sheet data; referenced by integer index). Sheet content streams via XmlReader,
/// with each <see cref="RowReader"/> reusing a buffer — DO NOT retain RowReader instances
/// across iterations.
/// </summary>
public sealed class XlsxReader : IDisposable
{
    private readonly OoxmlPackage _package;
    private readonly XlsxReaderOptions _options;
    private readonly ReadBudget _budget;
    private readonly Stream? _ownedStream;   // decrypted / buffered copy we must dispose

    /// <summary>Shared strings, indexed by SST id. May be empty if the file uses only inline strings.</summary>
    internal string[] SharedStrings { get; }

    /// <summary>Style entries, indexed by cellXf id. May be empty for files with no styles.xml.</summary>
    internal StyleEntry[] Styles { get; }

    /// <summary>
    /// The workbook's complete style table (styles.xml plus theme), exactly as stored. A cell's
    /// s= attribute indexes <see cref="CellStyleTable.CellFormats"/>.
    /// <see cref="CellStyleTable.Empty"/> when the file has no styles part.
    /// </summary>
    public CellStyleTable StyleTable { get; }

    /// <summary>The sheets in the workbook, in tab order.</summary>
    public IReadOnlyList<SheetInfo> Sheets { get; }

    /// <summary>
    /// True if the workbook uses the 1904 date system (&lt;workbookPr date1904="1"/&gt;; common in
    /// files from old Mac Excel). Date serials are converted accordingly.
    /// </summary>
    public bool Date1904 { get; }

    /// <summary>
    /// True if the package contains macros: a VBA project (vbaProject.bin, typical of .xlsm /
    /// .xltm / .xlam) or Excel 4.0 macro sheets. Detection only; macros are never run, and
    /// <see cref="Workbook"/> load → save does not carry them over.
    /// </summary>
    public bool HasMacros { get; }

    /// <summary>
    /// Every defined name in workbook.xml, as stored, including built-in ones such as
    /// _xlnm.Print_Area and _xlnm.Print_Titles.
    /// </summary>
    public IReadOnlyList<DefinedNameInfo> DefinedNames { get; }

    internal XlsxReaderOptions Options => _options;

    private string?[]? _displayCodes;
    private CellStyle?[]? _resolvedStyles;
    private bool[]? _resolvedSet;

    /// <summary>The resolved style for a cellXf index (null for -1, 0 and out-of-range), cached per index.</summary>
    internal CellStyle? ResolveStyle(int styleIndex)
    {
        var xfs = StyleTable.CellFormats;
        if (styleIndex <= 0 || styleIndex >= xfs.Count) return null;
        _resolvedStyles ??= new CellStyle?[xfs.Count];
        _resolvedSet ??= new bool[xfs.Count];
        if (!_resolvedSet[styleIndex])
        {
            _resolvedStyles[styleIndex] = StyleTable.ResolveCellFormat(styleIndex);
            _resolvedSet[styleIndex] = true;
        }
        return _resolvedStyles[styleIndex];
    }
    private NumberFormatOptions? _displayOptions;

    /// <summary>Format options for display text: the configured culture and this file's date system.</summary>
    internal NumberFormatOptions DisplayOptions => _displayOptions ??= new NumberFormatOptions
    {
        Culture = _options.DisplayCulture ?? System.Globalization.CultureInfo.InvariantCulture,
        Date1904 = Date1904,
    };

    /// <summary>The format code a cell with this style index displays with (null = General).</summary>
    internal string? DisplayCode(int styleIndex)
    {
        var xfs = StyleTable.CellFormats;
        if (styleIndex < 0 || styleIndex >= xfs.Count) return null;
        _displayCodes ??= new string?[xfs.Count];
        return _displayCodes[styleIndex] ??= SerialDate.DisplayCode(StyleTable, xfs[styleIndex].NumberFormatId) ?? string.Empty;
    }

    private XlsxReader(OoxmlPackage package, string[] sst, CellStyleTable styleTable, WorkbookPartInfo wb,
                       bool hasMacros, XlsxReaderOptions options, ReadBudget budget, Stream? ownedStream)
    {
        _package = package;
        _options = options;
        _budget = budget;
        _ownedStream = ownedStream;
        SharedStrings = sst;
        StyleTable = styleTable;
        Styles = BuildStyleEntries(styleTable);
        Sheets = wb.Sheets;
        Date1904 = wb.Date1904;
        DefinedNames = wb.DefinedNames;
        HasMacros = hasMacros;
    }

    /// <summary>Per-cellXf date detection data, derived from the parsed style table.</summary>
    private static StyleEntry[] BuildStyleEntries(CellStyleTable table)
    {
        var xfs = table.CellFormats;
        var entries = new StyleEntry[xfs.Count];
        for (int i = 0; i < xfs.Count; i++)
        {
            var id = xfs[i].NumberFormatId;
            table.CustomNumberFormats.TryGetValue(id, out var code);
            entries[i] = new StyleEntry
            {
                NumFmtId = id,
                FormatCode = code,
                IsDateFormat = DateFormatDetector.IsDateFormat(id, code),
            };
        }
        return entries;
    }

    // ---- Open ---------------------------------------------------------------------

    /// <summary>
    /// Opens an xlsx file. An encrypted (password-protected) file throws
    /// <see cref="XlsxPasswordRequiredException"/>; use <see cref="Open(string, string?, XlsxReaderOptions?)"/>.
    /// </summary>
    public static XlsxReader Open(string path, XlsxReaderOptions? options = null)
        => Open(path, null, options);

    /// <summary>
    /// Opens an xlsx stream. The stream is not closed. An encrypted stream throws
    /// <see cref="XlsxPasswordRequiredException"/>; use <see cref="Open(Stream, string?, XlsxReaderOptions?)"/>.
    /// </summary>
    public static XlsxReader Open(Stream input, XlsxReaderOptions? options = null)
        => Open(input, null, options);

    /// <summary>
    /// Opens a possibly encrypted xlsx file. If the file is encrypted the password is required
    /// (missing or wrong → <see cref="XlsxPasswordRequiredException"/>); for an unencrypted file
    /// the password is ignored. Decryption happens in memory.
    /// </summary>
    public static XlsxReader Open(string path, string? password, XlsxReaderOptions? options)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return OpenCore(stream, password, options ?? new XlsxReaderOptions(), ownsStream: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a possibly encrypted xlsx stream. The stream is not closed. If the stream is
    /// encrypted the password is required (missing or wrong →
    /// <see cref="XlsxPasswordRequiredException"/>); otherwise it is ignored.
    /// </summary>
    public static XlsxReader Open(Stream input, string? password, XlsxReaderOptions? options)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        return OpenCore(input, password, options ?? new XlsxReaderOptions(), ownsStream: false);
    }

    private static XlsxReader OpenCore(Stream input, string? password, XlsxReaderOptions options, bool ownsStream)
    {
        OoxmlPackage? pkg = null;
        Stream? owned = null;
        var ct = options.CancellationToken;
        try
        {
            ct.ThrowIfCancellationRequested();
            Stream source = input;

            // Encrypted (CFB) packages: decrypt to an in-memory plaintext zip first.
            if (input.CanSeek && Chuvadi.Internal.Crypto.EncryptedPackageReader.IsEncryptedPackage(input))
            {
                if (options.MaxTotalUncompressedBytes is long cap && input.Length - input.Position > cap)
                    throw new XlsxLimitExceededException("MaxTotalUncompressedBytes", cap, input.Length - input.Position,
                        "the encrypted file is larger than the limit.");
                byte[] plaintext;
                // The container is read through a stream that checks the token on every read, so a
                // cancel lands while the file is being read; the in-memory decryption after that
                // (key derivation, AES, HMAC) runs to completion.
                try
                {
                    using var cancellable = new CancellableReadStream(input, ct);
                    plaintext = Chuvadi.Internal.Crypto.EncryptedPackageReader.DecryptToPlaintextPackage(cancellable, password);
                }
                catch (Chuvadi.Internal.Crypto.PackagePasswordException) when (ct.IsCancellationRequested)
                {
                    // The container reader wraps read errors; a cancel is not a password problem.
                    ct.ThrowIfCancellationRequested();
                    throw;
                }
                catch (Chuvadi.Internal.Crypto.PackagePasswordException ex) { throw new XlsxPasswordRequiredException(ex.Message, ex); }
                owned = source = new MemoryStream(plaintext, writable: false);
                ct.ThrowIfCancellationRequested();
            }

            var budget = new ReadBudget(options.MaxTotalUncompressedBytes);
            if (options.HasPackageLimits)
            {
                var checkedStream = PackageGuard.Check(source, options, budget);
                if (!ReferenceEquals(checkedStream, source)) owned = source = checkedStream;
            }

            // The package layer closes the stream it is given: shield a caller-owned stream.
            pkg = OoxmlPackage.Open(owned is null && !ownsStream ? new NonClosingStream(source) : source);

            // Find the workbook part via root relationships.
            string? workbookUri = null;
            foreach (var rel in pkg.GetRelationships("/"))
            {
                if (rel.Type.EndsWith("/officeDocument", StringComparison.Ordinal))
                {
                    workbookUri = "/" + rel.Target.TrimStart('/');
                    break;
                }
            }
            if (workbookUri is null)
                throw new XlsxFormatException("Package has no officeDocument relationship; not a valid xlsx.");

            var sst = TryLoadSharedStrings(pkg, options, budget);
            var styleTable = TryLoadStyleTable(pkg, workbookUri, options, budget);
            var wb = LoadWorkbookPart(pkg, workbookUri, options, budget);
            bool macros = DetectMacros(pkg);

            return new XlsxReader(pkg, sst, styleTable, wb, macros, options, budget, owned);
        }
        catch
        {
            pkg?.Dispose();
            owned?.Dispose();
            if (ownsStream) input.Dispose();
            throw;
        }
    }

    /// <summary>VBA project parts, Excel 4.0 macro sheets, or a macro-enabled main part.</summary>
    private static bool DetectMacros(OoxmlPackage pkg)
    {
        foreach (var part in pkg.Parts)
        {
            var ct = part.ContentType ?? string.Empty;
            var uri = part.Uri;
            if (ct.IndexOf("vbaProject", StringComparison.OrdinalIgnoreCase) >= 0
                || ct.IndexOf("macrosheet", StringComparison.OrdinalIgnoreCase) >= 0
                || uri.EndsWith("/vbaProject.bin", StringComparison.OrdinalIgnoreCase)
                || uri.IndexOf("/macrosheets/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    // ---- Sheet access -------------------------------------------------------------

    /// <summary>Returns a sheet reader by 1-based tab order.</summary>
    public SheetReader Sheet(int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > Sheets.Count)
            throw new ArgumentOutOfRangeException(nameof(oneBasedIndex));
        return new SheetReader(this, Sheets[oneBasedIndex - 1]);
    }

    /// <summary>Returns a sheet reader by name (case-insensitive).</summary>
    public SheetReader Sheet(string name)
    {
        foreach (var s in Sheets)
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                return new SheetReader(this, s);
        throw new KeyNotFoundException($"No sheet named '{name}'.");
    }

    /// <summary>Opens the sheet part's XML stream, counted against the configured limits. Caller disposes.</summary>
    internal Stream OpenSheetPart(SheetInfo info)
        => Limit(_package.OpenPart(info.PartUri), info.PartUri, _options, _budget);

    /// <summary>Wraps a part stream in the decompression guards when limits are configured.</summary>
    private static Stream Limit(Stream s, string partUri, XlsxReaderOptions options, ReadBudget budget)
        => budget.Wrap(s, partUri, options.MaxPartBytes);

    // ---- Loading -----------------------------------------------------------------

    private static string[] TryLoadSharedStrings(OoxmlPackage pkg, XlsxReaderOptions options, ReadBudget budget)
    {
        foreach (var part in pkg.Parts)
        {
            if (part.Uri.EndsWith("/sharedStrings.xml", StringComparison.Ordinal))
            {
                using var s = Limit(pkg.OpenPart(part.Uri), part.Uri, options, budget);
                return SharedStringReader.Read(s, options.CancellationToken);
            }
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// Loads styles.xml (located through the workbook's styles relationship, falling back to
    /// any part named styles.xml) and the theme part, into one <see cref="CellStyleTable"/>.
    /// A malformed styles part throws, as before. A malformed theme is ignored unless
    /// <see cref="XlsxReaderOptions.StrictXml"/> is set, since only colours depend on it.
    /// </summary>
    private static CellStyleTable TryLoadStyleTable(OoxmlPackage pkg, string workbookUri, XlsxReaderOptions options, ReadBudget budget)
    {
        string? stylesUri = FindWorkbookRelatedPart(pkg, workbookUri, "/styles");
        if (stylesUri is null)
        {
            foreach (var part in pkg.Parts)
            {
                if (part.Uri.EndsWith("/styles.xml", StringComparison.Ordinal)) { stylesUri = part.Uri; break; }
            }
        }

        CellStyleTable table;
        if (stylesUri is null)
        {
            table = new CellStyleTable();
        }
        else
        {
            using var s = Limit(pkg.OpenPart(stylesUri), stylesUri, options, budget);
            table = StyleTableParser.ParseStyles(s);
        }

        var themeUri = FindWorkbookRelatedPart(pkg, workbookUri, "/theme");
        if (themeUri is not null)
        {
            try
            {
                using var s = Limit(pkg.OpenPart(themeUri), themeUri, options, budget);
                table.Theme = StyleTableParser.ParseTheme(s);
            }
            catch (Exception ex) when (!options.StrictXml && ex is XmlException or XlsxFormatException)
            {
                // Leave Theme.IsPresent = false; theme colours then resolve to null.
            }
        }
        return table;
    }

    /// <summary>The absolute URI of the first existing part the workbook relates to with the given type suffix.</summary>
    private static string? FindWorkbookRelatedPart(OoxmlPackage pkg, string workbookUri, string typeSuffix)
    {
        foreach (var rel in pkg.GetRelationships(workbookUri))
        {
            if (!rel.Type.EndsWith(typeSuffix, StringComparison.Ordinal)) continue;
            var uri = ResolveWorkbookTarget(workbookUri, rel.Target);
            foreach (var part in pkg.Parts)
                if (string.Equals(part.Uri, uri, StringComparison.Ordinal)) return uri;
        }
        return null;
    }

    /// <summary>
    /// Resolves a relationship target from workbook.xml's .rels to an absolute part URI.
    /// Relative targets are relative to the workbook part's directory ("worksheets/sheet1.xml"
    /// → "/xl/worksheets/sheet1.xml"); "../" climbs one level; a leading '/' is already absolute.
    /// </summary>
    private static string ResolveWorkbookTarget(string workbookUri, string target)
    {
        if (target.StartsWith("/", StringComparison.Ordinal)) return target;
        var workbookDir = workbookUri.Substring(0, workbookUri.LastIndexOf('/'));  // "/xl"
        if (target.StartsWith("../", StringComparison.Ordinal))
        {
            var parentDir = workbookDir.Substring(0, workbookDir.LastIndexOf('/'));
            return parentDir + "/" + target.Substring(3);
        }
        return workbookDir + "/" + target;
    }

    private sealed class WorkbookPartInfo
    {
        public readonly List<SheetInfo> Sheets = new();
        public readonly List<DefinedNameInfo> DefinedNames = new();
        public bool Date1904;
    }

    /// <summary>Reads workbook.xml: the sheet list (with visibility), workbookPr and definedNames.</summary>
    private static WorkbookPartInfo LoadWorkbookPart(OoxmlPackage pkg, string workbookUri, XlsxReaderOptions options, ReadBudget budget)
    {
        var rels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in pkg.GetRelationships(workbookUri))
        {
            if (rel.Type.EndsWith("/worksheet", StringComparison.Ordinal))
                rels[rel.Id] = ResolveWorkbookTarget(workbookUri, rel.Target);
        }

        var info = new WorkbookPartInfo();
        int sheetPosition = 0;   // position among ALL <sheet> elements (localSheetId refers to this)
        using (var s = Limit(pkg.OpenPart(workbookUri), workbookUri, options, budget))
        using (var r = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true, CloseInput = false, DtdProcessing = DtdProcessing.Prohibit }))
        {
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                switch (r.LocalName)
                {
                    case "workbookPr":
                        info.Date1904 = r.GetAttribute("date1904") is "1" or "true";
                        break;
                    case "sheet":
                    {
                        int position = sheetPosition++;
                        var name = r.GetAttribute("name");
                        var sheetId = r.GetAttribute("sheetId");
                        var rId = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                        var state = r.GetAttribute("state") switch
                        {
                            "hidden" => SheetVisibility.Hidden,
                            "veryHidden" => SheetVisibility.VeryHidden,
                            _ => SheetVisibility.Visible,
                        };
                        if (name is null || rId is null) break;
                        if (!rels.TryGetValue(rId, out var partUri)) break;   // chart sheets, macro sheets
                        info.Sheets.Add(new SheetInfo(name, sheetId ?? "1", partUri, state, position));
                        break;
                    }
                    case "definedName":
                    {
                        var name = r.GetAttribute("name") ?? string.Empty;
                        int? local = int.TryParse(r.GetAttribute("localSheetId"), System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : null;
                        bool hidden = r.GetAttribute("hidden") is "1" or "true";
                        var comment = r.GetAttribute("comment");
                        // Collect the text up to </definedName>, leaving the reader ON the end tag
                        // (ReadElementContentAsString would land on the next element, which the
                        // loop's Read() would then skip).
                        var sb = new System.Text.StringBuilder();
                        if (!r.IsEmptyElement)
                        {
                            int depth = r.Depth;
                            while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
                                if (r.NodeType is XmlNodeType.Text or XmlNodeType.CDATA) sb.Append(r.Value);
                        }
                        var formula = sb.ToString();
                        info.DefinedNames.Add(new DefinedNameInfo(name, formula, local, hidden, comment));
                        break;
                    }
                }
            }
        }
        return info;
    }

    // ---- Disposal -----------------------------------------------------------------

    public void Dispose()
    {
        _package.Dispose();
        _ownedStream?.Dispose();
    }
}

/// <summary>Sheet visibility from workbook.xml (&lt;sheet state&gt;).</summary>
public enum SheetVisibility
{
    Visible = 0,
    /// <summary>Hidden; the user can unhide it from Excel's UI.</summary>
    Hidden,
    /// <summary>Hidden and not listed in Excel's Unhide dialog (only VBA can show it).</summary>
    VeryHidden,
}

/// <summary>A defined name from workbook.xml, exactly as stored.</summary>
/// <param name="Name">The name, e.g. "TaxRate" or "_xlnm.Print_Area".</param>
/// <param name="Formula">The formula or reference text, e.g. "Sheet1!$A$1:$D$20".</param>
/// <param name="LocalSheetIndex">0-based position of the sheet the name is scoped to (among all
/// sheets in workbook.xml, including chart sheets), or null for a workbook-level name.</param>
/// <param name="Hidden">True if the name is hidden from Excel's Name Manager.</param>
/// <param name="Comment">The name's comment (shown in Excel's Name Manager), or null.</param>
public sealed record DefinedNameInfo(string Name, string Formula, int? LocalSheetIndex, bool Hidden, string? Comment = null);

/// <summary>
/// Lightweight descriptor for a sheet — name + part URI. Available before opening the sheet's content.
/// </summary>
public sealed class SheetInfo
{
    public string Name { get; }
    public string SheetId { get; }
    internal string PartUri { get; }

    /// <summary>Visible, hidden or very hidden.</summary>
    public SheetVisibility Visibility { get; }

    /// <summary>
    /// 0-based position among all &lt;sheet&gt; entries in workbook.xml (including chart and
    /// macro sheets that this reader skips). Matches <see cref="DefinedNameInfo.LocalSheetIndex"/>.
    /// </summary>
    public int WorkbookIndex { get; }

    internal SheetInfo(string name, string sheetId, string partUri, SheetVisibility visibility = SheetVisibility.Visible, int workbookIndex = 0)
    {
        Name = name; SheetId = sheetId; PartUri = partUri; Visibility = visibility; WorkbookIndex = workbookIndex;
    }
}

/// <summary>A pass-through stream whose Dispose leaves the inner stream open.</summary>
internal sealed class NonClosingStream : Stream
{
    private readonly Stream _inner;
    public NonClosingStream(Stream inner) { _inner = inner; }
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A pass-through read stream that throws once the token is cancelled. Leaves the inner stream open.</summary>
internal sealed class CancellableReadStream : Stream
{
    private readonly Stream _inner;
    private readonly System.Threading.CancellationToken _ct;
    public CancellableReadStream(Stream inner, System.Threading.CancellationToken ct) { _inner = inner; _ct = ct; }
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set { _ct.ThrowIfCancellationRequested(); _inner.Position = value; } }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) { _ct.ThrowIfCancellationRequested(); return _inner.Read(buffer, offset, count); }
    public override int Read(Span<byte> buffer) { _ct.ThrowIfCancellationRequested(); return _inner.Read(buffer); }
    public override long Seek(long offset, SeekOrigin origin) { _ct.ThrowIfCancellationRequested(); return _inner.Seek(offset, origin); }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Per-sheet reader. Iterate <see cref="Rows"/> to stream rows in order.
/// </summary>
public sealed class SheetReader
{
    private readonly XlsxReader _book;
    private readonly SheetInfo _info;

    /// <summary>Maps header text → 0-based column index (built from row 1 if enabled).</summary>
    private Dictionary<string, int>? _headerIndex;

    internal SheetReader(XlsxReader book, SheetInfo info)
    {
        _book = book;
        _info = info;
    }

    public string Name => _info.Name;

    /// <summary>
    /// Streaming row enumeration. Each <see cref="RowReader"/> yielded is a transient struct
    /// over a reused buffer — do NOT retain across iterations.
    /// </summary>
    public IEnumerable<RowReader> Rows => new RowEnumerable(this, null);

    /// <summary>
    /// Same row stream as <see cref="Rows"/>, but also feeds sheet layout metadata (merges,
    /// dimensions) into <paramref name="layout"/> during the same single pass. The collector
    /// is complete only once enumeration has run to the end.
    /// </summary>
    internal IEnumerable<RowReader> RowsWithLayout(SheetLayoutCollector layout)
        => new RowEnumerable(this, layout);

    private SheetLayoutInfo? _layoutInfo;

    /// <summary>
    /// The sheet's layout and print settings: <see cref="SheetLayoutInfo.PageSetup"/> (with print
    /// areas and titles), sheet format defaults, column spans (widths, hidden, outline, styles),
    /// merged ranges, the stored used range and the sheet view. These elements sit before and
    /// after the cell data in the file, so the first call makes one pass over the sheet part,
    /// skipping the cell data without parsing it; the result is cached. Call it before or
    /// between row enumerations (it opens its own stream). Read limits and the cancellation
    /// token apply. Per-row settings come with each row: <see cref="RowReader.Attributes"/>.
    /// </summary>
    public SheetLayoutInfo ReadLayout()
    {
        if (_layoutInfo is not null) return _layoutInfo;
        var collector = new SheetLayoutCollector();
        var ct = _book.Options.CancellationToken;
        ct.ThrowIfCancellationRequested();
        using (var stream = _book.OpenSheetPart(_info))
        using (var xml = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = false, IgnoreComments = true, CloseInput = false, DtdProcessing = DtdProcessing.Prohibit }))
        {
            int n = 0;
            while (xml.Read())
            {
                if ((++n & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
                switch (xml.NodeType)
                {
                    case XmlNodeType.Element:
                        if (xml.LocalName == "sheetData" && SheetLayoutCollector.IsMainNamespace(xml.NamespaceURI))
                        {
                            if (!xml.IsEmptyElement) SkipSubtree(xml, ct);
                            continue;
                        }
                        collector.OnElement(xml);
                        break;
                    case XmlNodeType.Text: case XmlNodeType.CDATA: case XmlNodeType.Whitespace: case XmlNodeType.SignificantWhitespace:
                        collector.OnText(xml.Value);
                        break;
                    case XmlNodeType.EndElement:
                        collector.OnEndElement(xml.LocalName);
                        break;
                }
            }
        }
        var info = collector.ToLayoutInfo(_book.StyleTable);
        PrintNames.Apply(info.PageSetup, _book.DefinedNames, _info.WorkbookIndex);
        return _layoutInfo = info;
    }

    /// <summary>Skips the current element's subtree, checking the token as it goes (sheetData can be huge).</summary>
    private static void SkipSubtree(XmlReader xml, System.Threading.CancellationToken ct)
    {
        int depth = xml.Depth, n = 0;
        while (xml.Read())
        {
            if ((++n & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth) return;
        }
    }

    internal Dictionary<string, int>? HeaderIndex => _headerIndex;

    internal void SetHeaderIndex(Dictionary<string, int> headers) => _headerIndex = headers;

    internal XlsxReader Book => _book;
    internal SheetInfo Info => _info;
}

/// <summary>
/// Internal enumerable that opens the sheet XML on iteration and yields rows. Implemented as
/// a class (not iterator method) so we can manage the disposal of the XmlReader cleanly.
/// </summary>
internal sealed class RowEnumerable : IEnumerable<RowReader>
{
    private readonly SheetReader _sheet;
    private readonly SheetLayoutCollector? _layout;
    public RowEnumerable(SheetReader sheet, SheetLayoutCollector? layout) { _sheet = sheet; _layout = layout; }
    public IEnumerator<RowReader> GetEnumerator() => new RowEnumerator(_sheet, _layout);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Pulls rows from the sheet's XML stream one at a time. Reuses internal buffers so each
/// row yielded has zero allocations in the steady state.
/// </summary>
internal sealed class RowEnumerator : IEnumerator<RowReader>
{
    private readonly SheetReader _sheet;
    private readonly Stream _stream;
    private readonly XmlReader _xml;
    private readonly bool _autoDetectDates;
    private readonly System.Threading.CancellationToken _ct;
    private readonly long _maxRows;
    private long _rowsRead;
    private readonly XlsxReader _book;
    private readonly SheetLayoutCollector? _layout;   // null on the public streaming path

    // Row buffer — reused across all rows. _cellValues[col-1] holds the parsed value.
    private object?[] _cellValues = new object?[16];
    private int[] _cellStyles = NewStyleBuffer(16);   // parallel to _cellValues; -1 = no s= attribute
    private bool[] _cellPresent = new bool[16];        // parallel: a <c> element exists for the column
    private FormulaSlot[] _formulas = new FormulaSlot[16];   // parallel: formula of the cell, if any
    private bool _rowHasFormula;
    private RowData? _rowData;                          // the current <row>'s attributes (null = none)
    private readonly Dictionary<int, SharedFormulaMaster> _sharedMasters = new();
    private int _maxColumnSeen;
    private int _currentRowNumber;
    private bool _hasCurrent;
    private bool _headersConsumed;

    public RowEnumerator(SheetReader sheet, SheetLayoutCollector? layout)
    {
        _sheet = sheet;
        _layout = layout;
        _book = sheet.Book;
        _stream = _book.OpenSheetPart(sheet.Info);
        _xml = XmlReader.Create(_stream, new XmlReaderSettings
        {
            IgnoreWhitespace = false,
            IgnoreComments = true,
            CloseInput = false,
        });
        _autoDetectDates = _book.Options.AutoDetectDates;
        _ct = _book.Options.CancellationToken;
        _maxRows = _book.Options.MaxRowsPerSheet ?? long.MaxValue;
    }

    public RowReader Current
        => _hasCurrent
            ? new RowReader(_cellValues, _cellStyles, _maxColumnSeen, _currentRowNumber, _sheet, new RowExtras(_cellPresent, _rowHasFormula ? _formulas : null, _rowData, _sharedMasters))
            : throw new InvalidOperationException("Enumeration has not started or already ended.");

    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        // Loop in case we need to skip past the header row internally.
        while (true)
        {
            if (!ReadNextRow()) return false;

            // Build header index from the first row if enabled and not yet built.
            if (_book.Options.TreatFirstRowAsHeaders && !_headersConsumed)
            {
                BuildHeaderIndex();
                _headersConsumed = true;
                // Don't yield the header row; advance to the next.
                continue;
            }

            return true;
        }
    }

    private bool ReadNextRow()
    {
        ClearRowBuffer();

        // Advance to the next <row> element.
        while (_xml.Read())
        {
            if (_xml.NodeType != XmlNodeType.Element)
            {
                if (_layout is not null)
                {
                    if (_xml.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                        _layout.OnText(_xml.Value);
                    else if (_xml.NodeType == XmlNodeType.EndElement)
                        _layout.OnEndElement(_xml.LocalName);
                }
                continue;
            }
            if (_xml.LocalName != "row")
            {
                _layout?.OnElement(_xml);
                continue;
            }

            _ct.ThrowIfCancellationRequested();
            if (++_rowsRead > _maxRows)
                throw new XlsxLimitExceededException("MaxRowsPerSheet", _maxRows, _rowsRead,
                    $"sheet '{_sheet.Name}' has more rows than allowed.");

            var rAttr = _xml.GetAttribute("r");
            _currentRowNumber = int.TryParse(rAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rn)
                ? rn
                : _currentRowNumber + 1;

            _rowData = SheetLayoutCollector.ParseRow(_xml);
            if (_rowData is not null) _layout?.SetRow(_currentRowNumber, _rowData);

            // Read cells within this row.
            ReadCellsInRow();
            _hasCurrent = true;
            return true;
        }

        _hasCurrent = false;
        return false;
    }

    private void ReadCellsInRow()
    {
        if (_xml.IsEmptyElement) return;

        int rowDepth = _xml.Depth;
        int impliedCol = 0;  // for cells without r= attribute

        while (_xml.Read())
        {
            if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == rowDepth)
                return;

            if (_xml.NodeType != XmlNodeType.Element) continue;
            if (_xml.LocalName != "c") continue;

            // Parse the cell.
            var rAttr = _xml.GetAttribute("r");
            int col;
            if (rAttr is not null)
            {
                col = ParseCellColumn(rAttr);
            }
            else
            {
                col = ++impliedCol;
            }
            impliedCol = col;

            var t = _xml.GetAttribute("t");
            var s = _xml.GetAttribute("s");
            int styleIndex = -1;
            if (s is not null && !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out styleIndex))
                styleIndex = -1;   // malformed s=: treat as absent rather than as xf 0

            // Read the cell's contents.
            _pending = default;
            var value = ReadCellValue(t, styleIndex);

            // Store in buffer; grow if needed.
            EnsureCapacity(col);
            _cellValues[col - 1] = value;
            _cellStyles[col - 1] = styleIndex;
            _cellPresent[col - 1] = true;
            if (_pending.Present)
            {
                var slot = _pending;
                slot.Row = _currentRowNumber;
                slot.Column = col;
                if (slot.Kind == FormulaKind.Shared && slot.Text is not null && slot.Text.Length > 0 && slot.SharedIndex >= 0)
                    _sharedMasters[slot.SharedIndex] = new SharedFormulaMaster(slot.Text, _currentRowNumber, col);
                _formulas[col - 1] = slot;
                _rowHasFormula = true;
            }
            if (col > _maxColumnSeen) _maxColumnSeen = col;
        }
    }

    private FormulaSlot _pending;   // formula of the cell being read

    private object? ReadCellValue(string? cellType, int styleIndex)
    {
        if (_xml.IsEmptyElement) return null;

        int cellDepth = _xml.Depth;
        string? rawValue = null;
        bool isFormula = false;
        System.Text.StringBuilder? inlineSb = null;   // allocated lazily only for inline strings

        while (_xml.Read())
        {
            if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == cellDepth)
                break;

            if (_xml.NodeType == XmlNodeType.Element)
            {
                if (_xml.LocalName == "v")
                {
                    rawValue = ReadElementText();
                }
                else if (_xml.LocalName == "f")
                {
                    isFormula = true;
                    var ft = _xml.GetAttribute("t");
                    _pending.Present = true;
                    _pending.Kind = ft switch
                    {
                        "shared" => FormulaKind.Shared,
                        "array" => FormulaKind.Array,
                        "dataTable" => FormulaKind.DataTable,
                        _ => FormulaKind.Normal,
                    };
                    _pending.Range = _xml.GetAttribute("ref");
                    _pending.SharedIndex = int.TryParse(_xml.GetAttribute("si"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var si) ? si : -1;
                    // Read the text to </f> (not XmlReader.Skip(): Skip lands ON the next sibling,
                    // and this loop's Read() would then step past <v>, losing the cached value).
                    _pending.Text = ReadElementText();
                }
                else if (_xml.LocalName == "is")
                {
                    // Inline string container — extract its text content.
                    inlineSb ??= new System.Text.StringBuilder();
                    ExtractStringFromInlineStrContainer(inlineSb);
                }
            }
        }

        if (inlineSb is not null) return inlineSb.ToString();
        if (rawValue is null) return null;

        return InterpretRawValue(cellType, rawValue, styleIndex, isFormula);
    }

    private string ReadElementText()
    {
        // Read everything inside the current element until its close.
        if (_xml.IsEmptyElement) return string.Empty;
        int depth = _xml.Depth;
        var sb = new System.Text.StringBuilder();
        while (_xml.Read())
        {
            if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == depth)
                return sb.ToString();
            if (_xml.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
                sb.Append(_xml.Value);
        }
        return sb.ToString();
    }

    private void ExtractStringFromInlineStrContainer(System.Text.StringBuilder sb)
    {
        // <is> may contain a simple <t> or rich-text <r>/<t> structure.
        if (_xml.IsEmptyElement) return;
        int depth = _xml.Depth;
        while (_xml.Read())
        {
            if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == depth) return;
            if (_xml.NodeType == XmlNodeType.Element && _xml.LocalName == "t")
            {
                if (!_xml.IsEmptyElement)
                {
                    int td = _xml.Depth;
                    while (_xml.Read())
                    {
                        if (_xml.NodeType == XmlNodeType.EndElement && _xml.Depth == td) break;
                        if (_xml.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
                            sb.Append(_xml.Value);
                    }
                }
            }
        }
    }

    private object? InterpretRawValue(string? cellType, string rawValue, int styleIndex, bool isFormula)
    {
        // OOXML cell type semantics:
        //   t="s"        — value is an SST id
        //   t="str"      — value is an inline string (typically formula result)
        //   t="inlineStr"— actually handled above via <is>
        //   t="b"        — "1"/"0"
        //   t="e"        — error code
        //   t="d"        — ISO date string
        //   t=null/"n"   — numeric (default)
        //
        // For numerics, apply date auto-detect from the style if enabled.

        switch (cellType)
        {
            case "s":
            {
                if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                {
                    var arr = _book.SharedStrings;
                    if (idx >= 0 && idx < arr.Length) return arr[idx];
                }
                return rawValue;  // malformed — return raw as fallback
            }
            case "str":
                return rawValue;
            case "b":
                return rawValue == "1";
            case "e":
                return rawValue;
            case "d":
                return DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? dt
                    : (object)rawValue;
            default:
                // Numeric.
                if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    return rawValue;

                if (_autoDetectDates && styleIndex >= 0 && styleIndex < _book.Styles.Length
                    && _book.Styles[styleIndex].IsDateFormat)
                {
                    return SerialDate.ToDateTime(num, _book.Date1904) is DateTime dt1 ? dt1 : num;   // unrepresentable → double
                }
                return num;
        }
    }

    /// <summary>
    /// Parses the column-letter portion of an A1 address: "A1" → 1, "B23" → 2, "AA1" → 27.
    /// </summary>
    private static int ParseCellColumn(string a1)
    {
        int col = 0;
        for (int i = 0; i < a1.Length; i++)
        {
            var c = a1[i];
            if (c >= 'A' && c <= 'Z') col = col * 26 + (c - 'A' + 1);
            else break;
        }
        return col;
    }

    private void ClearRowBuffer()
    {
        if (_maxColumnSeen > 0)
        {
            Array.Clear(_cellValues, 0, _maxColumnSeen);
            Array.Fill(_cellStyles, -1, 0, _maxColumnSeen);
            Array.Clear(_cellPresent, 0, _maxColumnSeen);
            if (_rowHasFormula) Array.Clear(_formulas, 0, _maxColumnSeen);
        }
        _rowHasFormula = false;
        _rowData = null;
        _maxColumnSeen = 0;
    }

    private void EnsureCapacity(int column)
    {
        if (column <= _cellValues.Length) return;
        int oldSize = _cellValues.Length;
        int newSize = oldSize;
        while (newSize < column) newSize *= 2;
        Array.Resize(ref _cellValues, newSize);
        Array.Resize(ref _cellStyles, newSize);
        Array.Resize(ref _cellPresent, newSize);
        Array.Resize(ref _formulas, newSize);
        Array.Fill(_cellStyles, -1, oldSize, newSize - oldSize);
    }

    private static int[] NewStyleBuffer(int size)
    {
        var a = new int[size];
        Array.Fill(a, -1);
        return a;
    }

    private void BuildHeaderIndex()
    {
        if (!_hasCurrent) return;
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _maxColumnSeen; i++)
        {
            if (_cellValues[i] is string s && !string.IsNullOrEmpty(s))
                map.TryAdd(s, i);   // first occurrence wins
        }
        _sheet.SetHeaderIndex(map);
    }

    public void Reset() => throw new NotSupportedException("Sheet rows are forward-only.");

    public void Dispose()
    {
        _xml.Dispose();
        _stream.Dispose();
    }
}

/// <summary>
/// One row's cells. <strong>This is a transient struct — do not retain it across enumeration
/// steps. The underlying buffer is reused on each <c>MoveNext</c>.</strong>
///
/// To use a row's data later, copy out the specific values you need:
/// <code>
/// var copied = new List&lt;(int Id, string Name)&gt;();
/// foreach (var row in sheet.Rows)
/// {
///     copied.Add((row.GetInt32("Id"), row.GetString("Name")));
/// }
/// </code>
///
/// (We use <c>readonly struct</c> rather than <c>ref struct</c> so that the type can be
/// used with standard <see cref="System.Collections.Generic.IEnumerable{T}"/> patterns.
/// The "don't retain" rule is enforced by convention, not the compiler.)
/// </summary>
public readonly struct RowReader
{
    private readonly object?[] _buffer;
    private readonly int[] _styles;
    private readonly int _count;
    public readonly int RowNumber;
    private readonly SheetReader _sheet;
    private readonly RowExtras _extras;

    internal RowReader(object?[] buffer, int[] styles, int count, int rowNumber, SheetReader sheet, RowExtras extras)
    {
        _buffer = buffer;
        _styles = styles;
        _count = count;
        RowNumber = rowNumber;
        _sheet = sheet;
        _extras = extras;
    }

    /// <summary>
    /// The row's stored settings (&lt;row&gt; attributes): height, custom height, hidden, outline
    /// level, collapsed and row style. A row with no stored attributes reports defaults
    /// (<see cref="Row.IsDefined"/> false; its height is the sheet's default row height).
    /// </summary>
    public Row Attributes => new(RowNumber, _extras.RowData, _sheet.Book.StyleTable);

    /// <summary>
    /// True if the file has a &lt;c&gt; element for this 0-based column (with or without a value).
    /// A styled empty cell is present; a gap between cells is not.
    /// </summary>
    public bool HasCell(int column0Based)
        => column0Based >= 0 && column0Based < _count && _extras.Present[column0Based];

    /// <summary>
    /// The resolved style of the cell's s= index (as <see cref="CellStyleTable.ResolveCellFormat"/>),
    /// cached per index; null when the cell has no s= attribute, uses xf 0 (the workbook default,
    /// see <see cref="CellStyleTable.DefaultCellStyle"/>), or is missing.
    /// </summary>
    public CellStyle? GetStyle(int column0Based) => _sheet.Book.ResolveStyle(GetStyleIndex(column0Based));

    /// <summary>
    /// The cellXf index Excel renders this cell with: the cell's own s= when the file has a
    /// &lt;c&gt; element for it (0 when it has none); otherwise the row style when the row has
    /// customFormat; otherwise the column style from <paramref name="layout"/>; otherwise 0.
    /// Pass the result of <see cref="SheetReader.ReadLayout"/> for this sheet.
    /// </summary>
    public int GetEffectiveStyleIndex(int column0Based, SheetLayoutInfo layout)
    {
        if (layout is null) throw new ArgumentNullException(nameof(layout));
        if (HasCell(column0Based)) return Math.Max(0, _styles[column0Based]);
        var rd = _extras.RowData;
        if (rd is not null && rd.CustomFormat && rd.StyleIndex >= 0) return rd.StyleIndex;
        return layout.ColumnStyleIndex(column0Based + 1);
    }

    /// <summary>
    /// The cell's formula as written in Excel without the leading '=', or null when the cell
    /// has none. For a shared formula's child cell this is the child's own formula: the master's
    /// relative references moved by the child's offset (absolute $ parts kept; a reference moved
    /// off the sheet becomes #REF!). For an array formula only the top-left cell has the text;
    /// a data-table cell (TABLE()) returns null. The indexer returns the cached result.
    /// </summary>
    public string? GetFormula(int column0Based)
    {
        var slot = Slot(column0Based);
        if (slot is null) return null;
        return Text(slot.Value);
    }

    /// <summary>Formula by header name (requires TreatFirstRowAsHeaders). See <see cref="GetFormula(int)"/>.</summary>
    public string? GetFormula(string columnName) => GetFormula(HeaderColumn(columnName));

    /// <summary>
    /// The cell's formula with its kind and range (the shared or array range on the cell that
    /// stores it, the data-table range for TABLE() cells), or null when the cell has no formula.
    /// </summary>
    public CellFormula? GetFormulaInfo(int column0Based)
    {
        var slot = Slot(column0Based);
        if (slot is null) return null;
        var f = slot.Value;
        return new CellFormula(Text(f) ?? string.Empty, f.Kind, f.Range);
    }

    /// <summary>
    /// True if the cell has a formula and the file stores its last computed result (a &lt;v&gt;
    /// element). Files written by tools that do not calculate often have formulas without results.
    /// </summary>
    public bool HasCachedValue(int column0Based) => Slot(column0Based) is not null && this[column0Based] is not null;

    private FormulaSlot? Slot(int column0Based)
    {
        var f = _extras.Formulas;
        if (f is null || column0Based < 0 || column0Based >= _count || !f[column0Based].Present) return null;
        return f[column0Based];
    }

    private string? Text(FormulaSlot f)
    {
        if (f.Kind == FormulaKind.DataTable) return null;
        if (f.Kind == FormulaKind.Shared && string.IsNullOrEmpty(f.Text))
        {
            if (f.SharedIndex < 0 || !_extras.SharedMasters.TryGetValue(f.SharedIndex, out var m)) return null;
            return FormulaText.Shift(m.Text, f.Row - m.Row, f.Column - m.Column);
        }
        return f.Text;
    }

    private int HeaderColumn(string columnName)
    {
        if (_sheet.HeaderIndex is null)
            throw new InvalidOperationException("Header index not built; enable TreatFirstRowAsHeaders.");
        return _sheet.HeaderIndex.TryGetValue(columnName, out var idx) ? idx : -1;
    }

    /// <summary>Number of cells in this row (the highest column index seen).</summary>
    public int Count => _count;

    /// <summary>
    /// The cell's style index (its s= attribute) by 0-based column, or -1 when the cell has no
    /// s= attribute or is missing. Index into <see cref="XlsxReader.StyleTable"/>'s
    /// <see cref="CellStyleTable.CellFormats"/>; resolve it with
    /// <see cref="CellStyleTable.ResolveCellFormat"/>.
    /// </summary>
    public int GetStyleIndex(int column0Based)
        => column0Based >= 0 && column0Based < _count ? _styles[column0Based] : -1;

    /// <summary>
    /// The cell's text as Excel displays it: the value rendered with the cell's number format
    /// (<see cref="NumberFormatter"/>), using <see cref="XlsxReaderOptions.DisplayCulture"/> and
    /// the file's date system. Empty for a missing or empty cell; formula cells show their
    /// cached result. Locale-dependent built-in formats (currency/accounting ids 5-8, 41-44)
    /// render as Excel en-US does. Fill characters (*) are not expanded.
    /// </summary>
    public string GetDisplayString(int column0Based)
    {
        var v = this[column0Based];
        if (v is null) return string.Empty;
        var book = _sheet.Book;
        var code = book.DisplayCode(GetStyleIndex(column0Based));
        return NumberFormatter.Format(v, string.IsNullOrEmpty(code) ? null : code, book.DisplayOptions);
    }

    /// <summary>Display text by header name (requires TreatFirstRowAsHeaders). See <see cref="GetDisplayString(int)"/>.</summary>
    public string GetDisplayString(string columnName)
    {
        if (_sheet.HeaderIndex is null)
            throw new InvalidOperationException("Header index not built; enable TreatFirstRowAsHeaders.");
        return _sheet.HeaderIndex.TryGetValue(columnName, out var idx) ? GetDisplayString(idx) : string.Empty;
    }

    /// <summary>Raw value access by 0-based column index. Returns null for missing cells.</summary>
    public object? this[int column0Based]
        => column0Based >= 0 && column0Based < _count ? _buffer[column0Based] : null;

    /// <summary>Raw value access by header name (requires TreatFirstRowAsHeaders).</summary>
    public object? this[string columnName]
    {
        get
        {
            if (_sheet.HeaderIndex is null)
                throw new InvalidOperationException("Header index not built; enable TreatFirstRowAsHeaders.");
            return _sheet.HeaderIndex.TryGetValue(columnName, out var idx) ? this[idx] : null;
        }
    }

    // ---- Typed accessors — throw on missing/wrong type --------------------------

    public string GetString(int col) => Get<string>(col);
    public string GetString(string name) => Get<string>(name);

    public int GetInt32(int col) => GetNumeric<int>(col, Convert.ToInt32);
    public int GetInt32(string name) => GetNumeric<int>(name, Convert.ToInt32);

    public long GetInt64(int col) => GetNumeric<long>(col, Convert.ToInt64);
    public long GetInt64(string name) => GetNumeric<long>(name, Convert.ToInt64);

    public double GetDouble(int col) => GetNumeric<double>(col, Convert.ToDouble);
    public double GetDouble(string name) => GetNumeric<double>(name, Convert.ToDouble);

    public decimal GetDecimal(int col) => GetNumeric<decimal>(col, Convert.ToDecimal);
    public decimal GetDecimal(string name) => GetNumeric<decimal>(name, Convert.ToDecimal);

    public bool GetBool(int col) => Get<bool>(col);
    public bool GetBool(string name) => Get<bool>(name);

    public DateTime GetDateTime(int col)
    {
        var v = this[col];
        if (v is DateTime dt) return dt;
        if (v is double d && SerialDate.ToDateTime(d, _sheet.Book.Date1904) is DateTime fromSerial) return fromSerial;
        throw new InvalidCastException($"Cell at column {col + 1} is not a date.");
    }
    public DateTime GetDateTime(string name)
    {
        var v = this[name];
        if (v is DateTime dt) return dt;
        if (v is double d && SerialDate.ToDateTime(d, _sheet.Book.Date1904) is DateTime fromSerial) return fromSerial;
        throw new InvalidCastException($"Cell '{name}' is not a date.");
    }

    public T Get<T>(int col)
    {
        var v = this[col] ?? throw new InvalidOperationException($"Cell at column {col + 1} is null.");
        if (v is T t) return t;
        try { return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); }
        catch { throw new InvalidCastException($"Cell at column {col + 1} ({v.GetType().Name}) cannot be converted to {typeof(T).Name}."); }
    }

    public T Get<T>(string name)
    {
        var v = this[name] ?? throw new InvalidOperationException($"Cell '{name}' is null or missing.");
        if (v is T t) return t;
        try { return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); }
        catch { throw new InvalidCastException($"Cell '{name}' ({v.GetType().Name}) cannot be converted to {typeof(T).Name}."); }
    }

    // ---- TryGet pattern --------------------------------------------------------

    public bool TryGet<T>(int col, out T value)
    {
        var v = this[col];
        if (v is T t) { value = t; return true; }
        if (v is not null)
        {
            try { value = (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); return true; }
            catch { }
        }
        value = default!;
        return false;
    }

    public bool TryGet<T>(string name, out T value)
    {
        var v = this[name];
        if (v is T t) { value = t; return true; }
        if (v is not null)
        {
            try { value = (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); return true; }
            catch { }
        }
        value = default!;
        return false;
    }

    // ---- Helpers ---------------------------------------------------------------

    private T GetNumeric<T>(int col, Func<object, T> convert)
    {
        var v = this[col] ?? throw new InvalidOperationException($"Cell at column {col + 1} is null.");
        try { return convert(v); }
        catch (Exception ex) { throw new InvalidCastException($"Cell at column {col + 1} ({v.GetType().Name}) cannot be converted to {typeof(T).Name}.", ex); }
    }

    private T GetNumeric<T>(string name, Func<object, T> convert)
    {
        var v = this[name] ?? throw new InvalidOperationException($"Cell '{name}' is null or missing.");
        try { return convert(v); }
        catch (Exception ex) { throw new InvalidCastException($"Cell '{name}' ({v.GetType().Name}) cannot be converted to {typeof(T).Name}.", ex); }
    }
}

/// <summary>Per-row side buffers handed to <see cref="RowReader"/> (reused across rows).</summary>
internal readonly struct RowExtras
{
    public readonly bool[] Present;
    public readonly FormulaSlot[]? Formulas;
    public readonly RowData? RowData;
    public readonly Dictionary<int, SharedFormulaMaster> SharedMasters;

    public RowExtras(bool[] present, FormulaSlot[]? formulas, RowData? rowData, Dictionary<int, SharedFormulaMaster> masters)
    {
        Present = present; Formulas = formulas; RowData = rowData; SharedMasters = masters;
    }
}

/// <summary>A cell's &lt;f&gt; element as read.</summary>
internal struct FormulaSlot
{
    public bool Present;
    public FormulaKind Kind;
    public string? Text;
    public string? Range;
    public int SharedIndex;
    public int Row;
    public int Column;
}

/// <summary>The cell that stores a shared formula's text (si), with its position.</summary>
internal readonly record struct SharedFormulaMaster(string Text, int Row, int Column);
