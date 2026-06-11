using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Chuvadi.Sheets.Internal;
using Chuvadi.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Streaming xlsx writer. Construct with <see cref="Create(string, XlsxWriterOptions?)"/>,
/// add one or more sheets via <see cref="AddSheet"/>, write rows through each sheet, then
/// call <see cref="Save"/> (or <see cref="SaveAsync"/>) to assemble the package.
///
/// Cell content streams to per-sheet temp files during writing — memory stays flat regardless
/// of row count. At save time, the temp files are streamed into the final zip package with
/// shared-string placeholders replaced by their final indices in a single byte-level pass.
///
/// Dispose always cleans up temp files. Calling Save is REQUIRED to produce a valid output
/// file; Dispose alone will not finalize the package.
/// </summary>
public sealed class XlsxWriter : IDisposable, IAsyncDisposable
{
    // ---- OOXML constants -----------------------------------------------------------

    private const string SsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string RelOfficeDocument =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string RelWorksheet =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private const string RelStyles =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string RelSharedStrings =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";
    private const string RelHyperlink =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string RelTable =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
    private const string RelComments =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string RelVmlDrawing =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";
    private const string RelTheme =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string RelCoreProps =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    private const string RelExtendedProps =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";

    private const string CtWorkbook =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string CtWorksheet =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string CtStyles =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";
    private const string CtSharedStrings =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
    private const string CtTable =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml";
    private const string CtComments =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml";
    private const string CtVmlDrawing =
        "application/vnd.openxmlformats-officedocument.vmlDrawing";
    private const string CtTheme =
        "application/vnd.openxmlformats-officedocument.theme+xml";
    private const string CtCoreProps =
        "application/vnd.openxmlformats-package.core-properties+xml";
    private const string CtExtendedProps =
        "application/vnd.openxmlformats-officedocument.extended-properties+xml";

    // ---- State ---------------------------------------------------------------------

    private readonly Stream _outputStream;
    private readonly bool _ownsOutputStream;
    private readonly XlsxWriterOptions _options;
    private readonly WorkbookWriteState _state;
    private readonly List<SheetWriter> _sheets = new();
    private readonly string _tempDir;
    private bool _saved;
    private bool _disposed;

    // ---- Construction --------------------------------------------------------------

    // Encryption mode: the package is assembled into _outputStream (a temp file) and then
    // encrypted into _finalOutputStream during Save. Null when no encryption is requested.
    private readonly Stream? _finalOutputStream;
    private readonly bool _ownsFinalOutputStream;
    private readonly string? _packageTempPath;

    private XlsxWriter(Stream outputStream, bool ownsStream, XlsxWriterOptions options)
    {
        _options = options;
        _state = new WorkbookWriteState(new SharedStringTable(), new StyleRegistry(), options);
        _tempDir = options.TempDirectory ?? Path.GetTempPath();

        if (options.Encryption is not null)
        {
            if (string.IsNullOrEmpty(options.Encryption.Password))
                throw new ArgumentException("Encryption password cannot be null or empty.", nameof(options));

            // Assemble the plaintext package into a temp file; encrypt it into the real
            // output at Save time. The package writer closes the temp stream at assembly
            // end, so the file is reopened by name for the encryption pass.
            _finalOutputStream = outputStream;
            _ownsFinalOutputStream = ownsStream;
            _packageTempPath = Path.Combine(_tempDir, $"chuvadi_sheets_pkg_{Guid.NewGuid():N}.tmp");
            _outputStream = new FileStream(
                _packageTempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 64 * 1024);
            _ownsOutputStream = true;
        }
        else
        {
            _outputStream = outputStream;
            _ownsOutputStream = ownsStream;
        }
    }

    /// <summary>Creates a writer that streams to the given file path. The file is created or overwritten.</summary>
    public static XlsxWriter Create(string path, XlsxWriterOptions? options = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous);
        return new XlsxWriter(stream, ownsStream: true, options ?? new XlsxWriterOptions());
    }

    /// <summary>Creates a writer that streams to the given stream. The stream is NOT closed by the writer.</summary>
    public static XlsxWriter Create(Stream output, XlsxWriterOptions? options = null)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        return new XlsxWriter(output, ownsStream: false, options ?? new XlsxWriterOptions());
    }

    // ---- Sheet management ----------------------------------------------------------

    /// <summary>
    /// Adds a new sheet and returns a writer for it. The sheet's content streams to a temp file
    /// until <see cref="Save"/> is called. Sheets must be disposed (use <c>using</c>) before Save.
    /// </summary>
    public SheetWriter AddSheet(string name)
    {
        EnsureNotDisposed();
        EnsureNotSaved();
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Sheet name required.", nameof(name));
        ValidateSheetName(name);

        foreach (var existing in _sheets)
            if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"A sheet named '{name}' already exists.");

        var sheetIndex = _sheets.Count + 1;
        var tempPath = Path.Combine(
            _tempDir,
            $"chuvadi_sheets_{Guid.NewGuid():N}_sheet{sheetIndex}.tmp");

        var sheet = new SheetWriter(_state, name, sheetIndex, tempPath);
        _sheets.Add(sheet);
        return sheet;
    }

    /// <summary>Number of sheets added so far.</summary>
    public int SheetCount => _sheets.Count;

    private Dictionary<string, string>? _definedNames;

    private string? _workbookProtectionHashB64;
    private string? _workbookProtectionSaltB64;
    private int _workbookProtectionSpinCount;
    private bool _lockStructure;
    private bool _lockWindows;

    /// <summary>
    /// Registers workbook structure protection. Hashes the password and emits
    /// &lt;workbookProtection&gt; in workbook.xml. Locks sheet add/remove/rename if
    /// <paramref name="lockStructure"/> is true.
    /// </summary>
    public void ProtectWorkbook(string password, bool lockStructure, bool lockWindows)
    {
        EnsureNotDisposed();
        EnsureNotSaved();
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password required.", nameof(password));

        var salt = Chuvadi.Internal.Crypto.PasswordHasher.GenerateSalt();
        _workbookProtectionHashB64 = Chuvadi.Internal.Crypto.PasswordHasher.ComputeHashBase64(
            password, salt, Chuvadi.Internal.Crypto.PasswordHasher.DefaultSpinCount);
        _workbookProtectionSaltB64 = Convert.ToBase64String(salt);
        _workbookProtectionSpinCount = Chuvadi.Internal.Crypto.PasswordHasher.DefaultSpinCount;
        _lockStructure = lockStructure;
        _lockWindows = lockWindows;
    }

    /// <summary>
    /// Registers workbook-level defined names that will be emitted into <c>workbook.xml</c>.
    /// Names are workbook-scoped. Call before <see cref="Save"/>.
    /// </summary>
    public void SetDefinedNames(IEnumerable<KeyValuePair<string, string>> names)
    {
        EnsureNotDisposed();
        EnsureNotSaved();
        if (names is null) throw new ArgumentNullException(nameof(names));
        _definedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in names)
            _definedNames[kv.Key] = kv.Value;
    }

    // ---- Save (sync) ---------------------------------------------------------------

    /// <summary>
    /// Finalizes all sheets, assembles the xlsx package, and writes everything to the output
    /// stream. After Save returns, the writer is closed for further mutation.
    /// </summary>
    public void Save()
    {
        BeginSave();
        AssemblePackage(useAsyncCopy: false, cancellation: CancellationToken.None)
            .GetAwaiter().GetResult();
        EncryptPackageIfRequested();
        _saved = true;
    }

    /// <summary>Async equivalent of <see cref="Save"/>.</summary>
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        BeginSave();
        return SaveAsyncImpl(cancellationToken);
    }

    private async Task SaveAsyncImpl(CancellationToken ct)
    {
        await AssemblePackage(useAsyncCopy: true, cancellation: ct).ConfigureAwait(false);
        EncryptPackageIfRequested();
        _saved = true;
    }

    /// <summary>
    /// Encryption mode post-step: the plaintext package now sits in the temp file (its
    /// stream was closed when assembly finished). Re-open it, stream-encrypt it into the
    /// real output, and delete the plaintext temp. No-op when encryption wasn't requested.
    /// </summary>
    private void EncryptPackageIfRequested()
    {
        if (_options.Encryption is null) return;

        var enc = _options.Encryption;
        try
        {
            using var plaintext = new FileStream(
                _packageTempPath!, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024);
            Chuvadi.Internal.Crypto.EncryptedPackageWriter.WriteEncrypted(
                _finalOutputStream!, plaintext, plaintext.Length, enc.Password, enc.SpinCount);
            _finalOutputStream!.Flush();
        }
        finally
        {
            try { if (File.Exists(_packageTempPath)) File.Delete(_packageTempPath!); } catch { }
        }

        // Match unencrypted Save semantics (where the package writer closes the output at
        // assembly end): close an owned final output now so the file is complete and
        // readable immediately after Save returns.
        if (_ownsFinalOutputStream)
        {
            try { _finalOutputStream!.Dispose(); } catch { }
        }
    }

    private void BeginSave()
    {
        EnsureNotDisposed();
        EnsureNotSaved();
        if (_sheets.Count == 0)
            throw new InvalidOperationException("Cannot save a workbook with zero sheets.");
        foreach (var sheet in _sheets) sheet.FinalizeSheet();
    }

    // ---- Save (assembly) -----------------------------------------------------------

    /// <summary>
    /// The actual package-assembly work. Used by both sync and async Save paths. Async-ness is
    /// confined to the placeholder-replacement copy from temp files into the zip — that's where
    /// I/O dominates.
    /// </summary>
    private async Task AssemblePackage(bool useAsyncCopy, CancellationToken cancellation)
    {
        using var pkg = OoxmlPackage.Create(_outputStream);

        WriteWorkbookPart(pkg);

        // Per-sheet IDs we'll need for tables / comments.
        int nextTableGlobalId = 1;
        int commentPartCounter = 1;

        // Write each sheet + its sheet-level parts (tables, comments, VML).
        foreach (var sheet in _sheets)
        {
            // Sheet XML body (with placeholder replacement).
            using (var dest = pkg.CreatePart(sheet.PartUri, CtWorksheet))
            using (var src = new FileStream(
                sheet.TempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024,
                options: FileOptions.SequentialScan | (useAsyncCopy ? FileOptions.Asynchronous : FileOptions.None)))
            {
                if (_options.UseInlineStrings)
                {
                    if (useAsyncCopy)
                        await src.CopyToAsync(dest, cancellation).ConfigureAwait(false);
                    else
                        src.CopyTo(dest);
                }
                else
                {
                    if (useAsyncCopy)
                        await CopyWithPlaceholderReplacementAsync(src, dest, cancellation).ConfigureAwait(false);
                    else
                        CopyWithPlaceholderReplacement(src, dest);
                }
            }

            // Sheet-level relationships are collected here as we go.
            int sheetRelCounter = 0;

            // Hyperlinks (external).
            int hlIdx = 1;
            foreach (var hl in sheet.Hyperlinks)
            {
                pkg.AddExternalRelationship(sheet.PartUri, hl.Url, RelHyperlink, $"rIdHL{hlIdx}");
                hlIdx++;
                sheetRelCounter++;
            }

            // Tables.
            int tblIdx = 1;
            foreach (var table in sheet.Tables)
            {
                var tableUri = $"/xl/tables/table{nextTableGlobalId}.xml";
                using (var tableStream = pkg.CreatePart(tableUri, CtTable))
                    FeaturePartWriters.WriteTable(tableStream, nextTableGlobalId, table);
                pkg.AddRelationship(sheet.PartUri, tableUri, RelTable, $"rIdTbl{tblIdx}");
                nextTableGlobalId++;
                tblIdx++;
                sheetRelCounter++;
            }

            // Comments.
            if (sheet.Comments.Count > 0)
            {
                var commentsUri = $"/xl/comments{commentPartCounter}.xml";
                using (var cs = pkg.CreatePart(commentsUri, CtComments))
                    FeaturePartWriters.WriteComments(cs, sheet.Comments);
                pkg.AddRelationship(sheet.PartUri, commentsUri, RelComments, "rIdComments");

                // VML drawing for the comment shapes.
                var vmlUri = $"/xl/drawings/vmlDrawing{commentPartCounter}.vml";
                using (var vs = pkg.CreatePart(vmlUri, CtVmlDrawing))
                    FeaturePartWriters.WriteVmlDrawing(vs, sheet.Comments);
                pkg.AddRelationship(sheet.PartUri, vmlUri, RelVmlDrawing, "rIdVml");

                commentPartCounter++;
            }
        }

        // Workbook-shared parts.
        using (var s = pkg.CreatePart("/xl/styles.xml", CtStyles))
            _state.Styles.WriteTo(s);

        if (!_options.UseInlineStrings && _state.SharedStrings.Count > 0)
        {
            using var s = pkg.CreatePart("/xl/sharedStrings.xml", CtSharedStrings);
            _state.SharedStrings.WriteTo(s);
        }

        // Theme (xl/theme/theme1.xml). Excel expects this part to exist in any production xlsx,
        // and validates its presence strictly when content arrives via encryption.
        using (var s = pkg.CreatePart("/xl/theme/theme1.xml", CtTheme))
            WriteMinimalThemeXml(s);

        // docProps/core.xml — Dublin Core metadata. Excel's strict post-decryption validation
        // requires this; without it, Excel reports "file is corrupt" even when the xlsx is otherwise valid.
        using (var s = pkg.CreatePart("/docProps/core.xml", CtCoreProps))
            WriteCorePropsXml(s);

        // docProps/app.xml — application metadata.
        using (var s = pkg.CreatePart("/docProps/app.xml", CtExtendedProps))
            WriteAppPropsXml(s, _sheets.Count, _sheets.Select(sh => sh.Name).ToList());

        // Workbook-level relationships.
        pkg.AddRelationship("/", "/xl/workbook.xml", RelOfficeDocument, "rId1");
        pkg.AddRelationship("/", "/docProps/core.xml", RelCoreProps, "rId2");
        pkg.AddRelationship("/", "/docProps/app.xml", RelExtendedProps, "rId3");

        int relId = 1;
        foreach (var sheet in _sheets)
            pkg.AddRelationship("/xl/workbook.xml", sheet.PartUri, RelWorksheet, $"rId{relId++}");

        pkg.AddRelationship("/xl/workbook.xml", "/xl/styles.xml", RelStyles, $"rId{relId++}");

        if (!_options.UseInlineStrings && _state.SharedStrings.Count > 0)
            pkg.AddRelationship("/xl/workbook.xml", "/xl/sharedStrings.xml", RelSharedStrings, $"rId{relId++}");

        // Theme relationship from the workbook.
        pkg.AddRelationship("/xl/workbook.xml", "/xl/theme/theme1.xml", RelTheme, $"rId{relId++}");

        pkg.Close();
    }

    // ---- Workbook part -------------------------------------------------------------

    private void WriteWorkbookPart(OoxmlPackage pkg)
    {
        using var s = pkg.CreatePart("/xl/workbook.xml", CtWorkbook);
        using var w = XmlWriter.Create(s, MakeSettings());

        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("workbook", SsNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);

        // workbookProtection (must come before <sheets>).
        if (_workbookProtectionHashB64 is not null)
        {
            w.WriteStartElement("workbookProtection", SsNs);
            w.WriteAttributeString("workbookAlgorithmName", "SHA-512");
            w.WriteAttributeString("workbookHashValue", _workbookProtectionHashB64);
            w.WriteAttributeString("workbookSaltValue", _workbookProtectionSaltB64);
            w.WriteAttributeString("workbookSpinCount",
                _workbookProtectionSpinCount.ToString(CultureInfo.InvariantCulture));
            if (_lockStructure) w.WriteAttributeString("lockStructure", "1");
            if (_lockWindows) w.WriteAttributeString("lockWindows", "1");
            w.WriteEndElement();
        }

        w.WriteStartElement("sheets", SsNs);
        for (int i = 0; i < _sheets.Count; i++)
        {
            var sheet = _sheets[i];
            w.WriteStartElement("sheet", SsNs);
            w.WriteAttributeString("name", sheet.Name);
            w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("id", RelNs, $"rId{i + 1}");
            w.WriteEndElement();
        }
        w.WriteEndElement();

        // <definedNames> — workbook-scoped named ranges/values.
        if (_definedNames is not null && _definedNames.Count > 0)
        {
            w.WriteStartElement("definedNames", SsNs);
            foreach (var kv in _definedNames)
            {
                w.WriteStartElement("definedName", SsNs);
                w.WriteAttributeString("name", kv.Key);
                w.WriteString(kv.Value);
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteEndElement(); // </workbook>
        w.WriteEndDocument();
    }

    // ---- Required-by-Excel package parts (theme, docProps) ------------------------

    /// <summary>
    /// Writes a minimal but spec-valid Office theme. Excel demands this part exists in the
    /// package; when content arrives via decryption, Excel's stricter validator will reject
    /// the file as corrupt if theme1.xml is missing. The actual theme contents don't matter
    /// much for our purposes — any valid theme XML will satisfy Excel.
    /// </summary>
    private static void WriteMinimalThemeXml(Stream output)
    {
        // The smallest workable Office theme. Contains the minimum required scheme elements
        // (clrScheme, fontScheme, fmtScheme) with default Office values. Generated to satisfy
        // Excel's structural validator; users producing styled output should rely on cell-level
        // formatting since theme references aren't part of our public API.
        const string xml =
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<a:theme xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" name=""Office Theme""><a:themeElements><a:clrScheme name=""Office""><a:dk1><a:sysClr val=""windowText"" lastClr=""000000""/></a:dk1><a:lt1><a:sysClr val=""window"" lastClr=""FFFFFF""/></a:lt1><a:dk2><a:srgbClr val=""44546A""/></a:dk2><a:lt2><a:srgbClr val=""E7E6E6""/></a:lt2><a:accent1><a:srgbClr val=""4472C4""/></a:accent1><a:accent2><a:srgbClr val=""ED7D31""/></a:accent2><a:accent3><a:srgbClr val=""A5A5A5""/></a:accent3><a:accent4><a:srgbClr val=""FFC000""/></a:accent4><a:accent5><a:srgbClr val=""5B9BD5""/></a:accent5><a:accent6><a:srgbClr val=""70AD47""/></a:accent6><a:hlink><a:srgbClr val=""0563C1""/></a:hlink><a:folHlink><a:srgbClr val=""954F72""/></a:folHlink></a:clrScheme><a:fontScheme name=""Office""><a:majorFont><a:latin typeface=""Calibri Light""/><a:ea typeface=""""/><a:cs typeface=""""/></a:majorFont><a:minorFont><a:latin typeface=""Calibri""/><a:ea typeface=""""/><a:cs typeface=""""/></a:minorFont></a:fontScheme><a:fmtScheme name=""Office""><a:fillStyleLst><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:gradFill rotWithShape=""1""><a:gsLst><a:gs pos=""0""><a:schemeClr val=""phClr""><a:lumMod val=""110000""/><a:satMod val=""105000""/><a:tint val=""67000""/></a:schemeClr></a:gs><a:gs pos=""50000""><a:schemeClr val=""phClr""><a:lumMod val=""105000""/><a:satMod val=""103000""/><a:tint val=""73000""/></a:schemeClr></a:gs><a:gs pos=""100000""><a:schemeClr val=""phClr""><a:lumMod val=""105000""/><a:satMod val=""109000""/><a:tint val=""81000""/></a:schemeClr></a:gs></a:gsLst><a:lin ang=""5400000"" scaled=""0""/></a:gradFill><a:gradFill rotWithShape=""1""><a:gsLst><a:gs pos=""0""><a:schemeClr val=""phClr""><a:satMod val=""103000""/><a:lumMod val=""102000""/><a:tint val=""94000""/></a:schemeClr></a:gs><a:gs pos=""50000""><a:schemeClr val=""phClr""><a:satMod val=""110000""/><a:lumMod val=""100000""/><a:shade val=""100000""/></a:schemeClr></a:gs><a:gs pos=""100000""><a:schemeClr val=""phClr""><a:lumMod val=""99000""/><a:satMod val=""120000""/><a:shade val=""78000""/></a:schemeClr></a:gs></a:gsLst><a:lin ang=""5400000"" scaled=""0""/></a:gradFill></a:fillStyleLst><a:lnStyleLst><a:ln w=""6350"" cap=""flat"" cmpd=""sng"" algn=""ctr""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:prstDash val=""solid""/><a:miter lim=""800000""/></a:ln><a:ln w=""12700"" cap=""flat"" cmpd=""sng"" algn=""ctr""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:prstDash val=""solid""/><a:miter lim=""800000""/></a:ln><a:ln w=""19050"" cap=""flat"" cmpd=""sng"" algn=""ctr""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:prstDash val=""solid""/><a:miter lim=""800000""/></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst><a:outerShdw blurRad=""57150"" dist=""19050"" dir=""5400000"" algn=""ctr"" rotWithShape=""0""><a:srgbClr val=""000000""><a:alpha val=""63000""/></a:srgbClr></a:outerShdw></a:effectLst></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:solidFill><a:schemeClr val=""phClr""><a:tint val=""95000""/><a:satMod val=""170000""/></a:schemeClr></a:solidFill><a:gradFill rotWithShape=""1""><a:gsLst><a:gs pos=""0""><a:schemeClr val=""phClr""><a:tint val=""93000""/><a:satMod val=""150000""/><a:shade val=""98000""/><a:lumMod val=""102000""/></a:schemeClr></a:gs><a:gs pos=""50000""><a:schemeClr val=""phClr""><a:tint val=""98000""/><a:satMod val=""130000""/><a:shade val=""90000""/><a:lumMod val=""103000""/></a:schemeClr></a:gs><a:gs pos=""100000""><a:schemeClr val=""phClr""><a:shade val=""63000""/><a:satMod val=""120000""/></a:schemeClr></a:gs></a:gsLst><a:lin ang=""5400000"" scaled=""0""/></a:gradFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Writes the Dublin Core metadata part (core.xml). Excel reads creator, lastModifiedBy,
    /// created, and modified for the file properties dialog. We use sensible defaults.
    /// </summary>
    private static void WriteCorePropsXml(Stream output)
    {
        var nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var xml =
$@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<cp:coreProperties xmlns:cp=""http://schemas.openxmlformats.org/package/2006/metadata/core-properties"" xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:dcterms=""http://purl.org/dc/terms/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""><dc:creator>Chuvadi.Sheets</dc:creator><cp:lastModifiedBy>Chuvadi.Sheets</cp:lastModifiedBy><dcterms:created xsi:type=""dcterms:W3CDTF"">{nowIso}</dcterms:created><dcterms:modified xsi:type=""dcterms:W3CDTF"">{nowIso}</dcterms:modified></cp:coreProperties>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Writes the extended (application) properties part (app.xml). Contains the application
    /// name, document type, and the list of sheet names. Excel uses this for its "Properties"
    /// dialog and demands the part exist when reading decrypted content.
    /// </summary>
    private static void WriteAppPropsXml(Stream output, int sheetCount, System.Collections.Generic.List<string> sheetNames)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
        sb.Append(@"<Properties xmlns=""http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"" xmlns:vt=""http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"">");
        sb.Append("<Application>Chuvadi.Sheets</Application>");
        sb.Append("<DocSecurity>0</DocSecurity>");
        sb.Append("<ScaleCrop>false</ScaleCrop>");
        sb.Append("<HeadingPairs><vt:vector size=\"2\" baseType=\"variant\">");
        sb.Append("<vt:variant><vt:lpstr>Worksheets</vt:lpstr></vt:variant>");
        sb.Append($"<vt:variant><vt:i4>{sheetCount}</vt:i4></vt:variant>");
        sb.Append("</vt:vector></HeadingPairs>");
        sb.Append($"<TitlesOfParts><vt:vector size=\"{sheetCount}\" baseType=\"lpstr\">");
        foreach (var name in sheetNames)
        {
            sb.Append("<vt:lpstr>");
            // XML-escape the sheet name.
            sb.Append(System.Security.SecurityElement.Escape(name));
            sb.Append("</vt:lpstr>");
        }
        sb.Append("</vt:vector></TitlesOfParts>");
        sb.Append("<LinksUpToDate>false</LinksUpToDate>");
        sb.Append("<SharedDoc>false</SharedDoc>");
        sb.Append("<HyperlinksChanged>false</HyperlinksChanged>");
        sb.Append("<AppVersion>16.0300</AppVersion>");
        sb.Append("</Properties>");
        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    // ---- Placeholder replacement (sync and async) ----------------------------------

    private static void CopyWithPlaceholderReplacement(Stream src, Stream dest)
    {
        var buffer = new byte[64 * 1024];
        var carry = new byte[128];
        int carryLen = 0;

        while (true)
        {
            if (carryLen > 0) Array.Copy(carry, 0, buffer, 0, carryLen);
            // CA2022 suppression: partial reads are handled by the surrounding loop.
            // We treat read==0 as EOF and any positive count as "process and continue".
#pragma warning disable CA2022
            int read = src.Read(buffer, carryLen, buffer.Length - carryLen);
#pragma warning restore CA2022
            int total = carryLen + read;
            if (total == 0) break;

            int i = ScanAndWrite(buffer, total, dest);

            if (read == 0)
            {
                // EOF: anything still pending (between i and total) is real content, not a
                // partial placeholder. Flush it and stop.
                if (i < total) dest.Write(buffer, i, total - i);
                break;
            }

            // Mid-stream: bytes from i onward might be the start of a placeholder we haven't
            // seen the end of yet. Carry them into the next read.
            carryLen = total - i;
            if (carryLen > carry.Length)
                throw new InvalidOperationException($"Placeholder carry exceeded {carry.Length} bytes.");
            if (carryLen > 0) Array.Copy(buffer, i, carry, 0, carryLen);
        }
    }

    private static async Task CopyWithPlaceholderReplacementAsync(Stream src, Stream dest, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var carry = new byte[128];
        int carryLen = 0;

        while (true)
        {
            if (carryLen > 0) Array.Copy(carry, 0, buffer, 0, carryLen);
            // CA2022 suppression: partial reads handled by the loop (see sync version above).
#pragma warning disable CA2022
            int read = await src.ReadAsync(buffer.AsMemory(carryLen, buffer.Length - carryLen), ct).ConfigureAwait(false);
#pragma warning restore CA2022
            int total = carryLen + read;
            if (total == 0) break;

            int i = await ScanAndWriteAsync(buffer, total, dest, ct).ConfigureAwait(false);

            if (read == 0)
            {
                if (i < total)
                    await dest.WriteAsync(buffer.AsMemory(i, total - i), ct).ConfigureAwait(false);
                break;
            }

            carryLen = total - i;
            if (carryLen > carry.Length)
                throw new InvalidOperationException($"Placeholder carry exceeded {carry.Length} bytes.");
            if (carryLen > 0) Array.Copy(buffer, i, carry, 0, carryLen);
        }
    }

    /// <summary>
    /// Synchronously scans the buffer for placeholders, writing safe bytes and replacements
    /// to <paramref name="dest"/>. Returns the index of the first unexamined byte (the boundary
    /// to carry into the next read).
    /// </summary>
    private static int ScanAndWrite(byte[] buffer, int total, Stream dest)
    {
        ReadOnlySpan<byte> prefix = "__CHUVADI_SS_"u8;
        ReadOnlySpan<byte> suffix = "_END__"u8;

        int i = 0;
        int writeFrom = 0;

        while (i <= total - prefix.Length)
        {
            if (buffer[i] != prefix[0] || !buffer.AsSpan(i, prefix.Length).SequenceEqual(prefix))
            {
                i++;
                continue;
            }

            int idStart = i + prefix.Length;
            int idEnd = idStart;
            while (idEnd < total && buffer[idEnd] >= (byte)'0' && buffer[idEnd] <= (byte)'9')
                idEnd++;

            if (idEnd == total || idEnd + suffix.Length > total)
                break;  // Insufficient bytes; carry to next read.

            if (idEnd == idStart)
            {
                i++;
                continue;  // Malformed.
            }

            if (!buffer.AsSpan(idEnd, suffix.Length).SequenceEqual(suffix))
            {
                i++;
                continue;
            }

            if (i > writeFrom) dest.Write(buffer, writeFrom, i - writeFrom);
            dest.Write(buffer, idStart, idEnd - idStart);

            int placeholderEnd = idEnd + suffix.Length;
            writeFrom = placeholderEnd;
            i = placeholderEnd;
        }

        // Determine the final flush.
        int safeEnd = i;
        if (safeEnd < writeFrom) safeEnd = writeFrom;
        if (safeEnd > writeFrom) dest.Write(buffer, writeFrom, safeEnd - writeFrom);
        return safeEnd;
    }

    private static async Task<int> ScanAndWriteAsync(byte[] buffer, int total, Stream dest, CancellationToken ct)
    {
        // Identical logic to ScanAndWrite, but uses WriteAsync. We don't share the scan logic
        // because async lambdas would explode this in source; the duplication is acceptable for
        // a hot path.
        const string prefixStr = "__CHUVADI_SS_";
        const string suffixStr = "_END__";
        byte[] prefix = Encoding.ASCII.GetBytes(prefixStr);
        byte[] suffix = Encoding.ASCII.GetBytes(suffixStr);

        int i = 0;
        int writeFrom = 0;

        while (i <= total - prefix.Length)
        {
            if (buffer[i] != prefix[0] || !buffer.AsSpan(i, prefix.Length).SequenceEqual(prefix))
            {
                i++;
                continue;
            }

            int idStart = i + prefix.Length;
            int idEnd = idStart;
            while (idEnd < total && buffer[idEnd] >= (byte)'0' && buffer[idEnd] <= (byte)'9')
                idEnd++;

            if (idEnd == total || idEnd + suffix.Length > total) break;
            if (idEnd == idStart) { i++; continue; }
            if (!buffer.AsSpan(idEnd, suffix.Length).SequenceEqual(suffix)) { i++; continue; }

            if (i > writeFrom)
                await dest.WriteAsync(buffer.AsMemory(writeFrom, i - writeFrom), ct).ConfigureAwait(false);
            await dest.WriteAsync(buffer.AsMemory(idStart, idEnd - idStart), ct).ConfigureAwait(false);

            int placeholderEnd = idEnd + suffix.Length;
            writeFrom = placeholderEnd;
            i = placeholderEnd;
        }

        int safeEnd = i;
        if (safeEnd < writeFrom) safeEnd = writeFrom;
        if (safeEnd > writeFrom)
            await dest.WriteAsync(buffer.AsMemory(writeFrom, safeEnd - writeFrom), ct).ConfigureAwait(false);
        return safeEnd;
    }

    // ---- Helpers -------------------------------------------------------------------

    private static XmlWriterSettings MakeSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };

    private static void ValidateSheetName(string name)
    {
        if (name.Length == 0 || name.Length > 31)
            throw new ArgumentException($"Sheet name must be 1-31 characters (got {name.Length}).", nameof(name));
        if (name[0] == '\'' || name[name.Length - 1] == '\'')
            throw new ArgumentException("Sheet name cannot begin or end with a single quote.", nameof(name));
        foreach (var c in name)
            if (c is ':' or '\\' or '/' or '?' or '*' or '[' or ']')
                throw new ArgumentException($"Sheet name cannot contain '{c}'.", nameof(name));
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void EnsureNotSaved()
    {
        if (_saved) throw new InvalidOperationException("Writer has already been saved; no further mutations are permitted.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var sheet in _sheets)
        {
            try { sheet.Dispose(); } catch { }
            try { if (File.Exists(sheet.TempPath)) File.Delete(sheet.TempPath); } catch { }
        }

        if (_ownsOutputStream)
        {
            try { _outputStream.Dispose(); } catch { }
        }
        if (_ownsFinalOutputStream && _finalOutputStream is not null)
        {
            try { _finalOutputStream.Dispose(); } catch { }
        }
        if (_packageTempPath is not null)
        {
            try { if (File.Exists(_packageTempPath)) File.Delete(_packageTempPath); } catch { }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
