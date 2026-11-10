using System;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.Docs.Model;

/// <summary>
/// Page size, orientation and margins of a section (w:pgSz, w:pgMar, w:paperSrc). The preset
/// sizes give portrait dimensions and flip for landscape; <see cref="PageSize.Custom"/> uses
/// <see cref="CustomWidthPt"/> × <see cref="CustomHeightPt"/> (also portrait, also flipped).
/// </summary>
public sealed class PageSetup
{
    private PageSize _size = PageSize.A4;

    /// <summary>Preset size or <see cref="PageSize.Custom"/>. Changing it clears <see cref="PaperCode"/>. Setting it makes <see cref="SizeStated"/> true.</summary>
    public PageSize Size
    {
        get => _size;
        set { if (value != _size) PaperCode = null; _size = value; SizeStated = true; }
    }

    /// <summary>
    /// True when the page size is stated: always for documents built in code; for a loaded
    /// document, true only when its w:sectPr has a w:pgSz with a valid width and height. When
    /// false, <see cref="Size"/> still holds A4 (so nothing breaks), but the document itself said
    /// nothing: Word then uses its own default size (Letter in the US, A4 elsewhere), and a
    /// paginator should do the same. Setting <see cref="Size"/> or a custom size makes it true;
    /// while it is false and the orientation is portrait, saving writes no w:pgSz (as loaded).
    /// </summary>
    public bool SizeStated { get; set; } = true;
    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

    private double? _customWidth, _customHeight;

    /// <summary>Portrait width in points for <see cref="PageSize.Custom"/> (0–1584pt).</summary>
    public double? CustomWidthPt
    {
        get => _customWidth;
        set { CheckDimension(value); if (value != _customWidth) PaperCode = null; _customWidth = value; SizeStated = true; }
    }

    /// <summary>Portrait height in points for <see cref="PageSize.Custom"/> (0–1584pt).</summary>
    public double? CustomHeightPt
    {
        get => _customHeight;
        set { CheckDimension(value); if (value != _customHeight) PaperCode = null; _customHeight = value; SizeStated = true; }
    }

    private static void CheckDimension(double? v)
    {
        if (v is double d && (!(d > 0) || d > 1584))
            throw new ArgumentOutOfRangeException(nameof(v), d, "Page dimensions must be between 0 and 1584pt (22 inches), Word's maximum.");
    }

    /// <summary>Sets a custom size: <see cref="Size"/> becomes <see cref="PageSize.Custom"/>.</summary>
    public PageSetup SetCustomSize(double widthPt, double heightPt)
    {
        if (!(widthPt > 0) || !(heightPt > 0) || widthPt > 1584 || heightPt > 1584)
            throw new ArgumentOutOfRangeException(nameof(widthPt), "Page dimensions must be between 0 and 1584pt (22 inches), Word's maximum.");
        Size = PageSize.Custom;
        CustomWidthPt = widthPt;
        CustomHeightPt = heightPt;
        return this;
    }

    /// <summary>Margins in points (72pt = 1 inch). Defaults: 1-inch all around. A negative top/bottom margin is "exactly" (text never pushes it).</summary>
    public double TopMarginPt { get; set; } = 72;
    public double BottomMarginPt { get; set; } = 72;
    public double LeftMarginPt { get; set; } = 72;
    public double RightMarginPt { get; set; } = 72;

    /// <summary>Distance from the page top to the header (w:pgMar/@w:header). Default 35.4pt (Word's 1.25 cm).</summary>
    public double HeaderDistancePt { get; set; } = 35.4;

    /// <summary>Distance from the page bottom to the footer (w:pgMar/@w:footer). Default 35.4pt.</summary>
    public double FooterDistancePt { get; set; } = 35.4;

    /// <summary>Binding gutter added to the inside (or top, see <see cref="Document.GutterAtTop"/>) margin (w:pgMar/@w:gutter).</summary>
    public double GutterPt { get; set; }

    /// <summary>Printer paper code (w:pgSz/@w:code), carried as given.</summary>
    public int? PaperCode { get; set; }

    /// <summary>Printer tray for the first page (w:paperSrc/@w:first).</summary>
    public int? PaperSourceFirstPage { get; set; }

    /// <summary>Printer tray for the other pages (w:paperSrc/@w:other).</summary>
    public int? PaperSourceOtherPages { get; set; }

    /// <summary>The page width in points as laid out (orientation applied).</summary>
    public double WidthPt => Orientation == PageOrientation.Landscape ? PortraitTwips.H / 20.0 : PortraitTwips.W / 20.0;

    /// <summary>The page height in points as laid out (orientation applied).</summary>
    public double HeightPt => Orientation == PageOrientation.Landscape ? PortraitTwips.W / 20.0 : PortraitTwips.H / 20.0;

    /// <summary>(width, height) in twips (1/20 pt) for the PORTRAIT orientation of <see cref="Size"/>.</summary>
    internal (int W, int H) PortraitTwips => Size switch
    {
        PageSize.A4 => (11906, 16838),
        PageSize.Letter => (12240, 15840),
        PageSize.Legal => (12240, 20160),
        PageSize.A3 => (16838, 23811),
        PageSize.A5 => (8391, 11906),
        PageSize.B5 => (10319, 14571),
        PageSize.Executive => (10440, 15120),
        PageSize.Tabloid => (15840, 24480),
        PageSize.Custom => ((int)Math.Round((CustomWidthPt ?? 595.3) * 20), (int)Math.Round((CustomHeightPt ?? 841.9) * 20)),
        _ => (11906, 16838),
    };

    /// <summary>A copy.</summary>
    public PageSetup Clone() => (PageSetup)MemberwiseClone();
}

/// <summary>
/// Content for a page header or footer: paragraphs (which may include page-number fields via
/// <see cref="Paragraph.PageNumber"/>) and tables (<see cref="AddTable"/>).
///
/// <code>
/// doc.Header.Add(new Paragraph("Quarterly Report") { Alignment = ParagraphAlignment.Center });
/// doc.Footer.Add(new Paragraph().PageNumber(includeTotal: true));
/// doc.Footer[0].Alignment = ParagraphAlignment.Right;
/// </code>
/// An empty header or footer of a later section is "linked to previous" — it shows the
/// previous section's — unless <see cref="LinkToPrevious"/> is false (then it is blank).
/// </summary>
public sealed class HeaderFooterContent : List<Paragraph>
{
    private readonly List<(Paragraph? After, DocTable Table)> _tables = new();

    /// <summary>Convenience: replaces the content with a single centered text paragraph.</summary>
    public void SetText(string text, TextFormat? format = null)
    {
        Clear();
        Add(new Paragraph(text, format) { Alignment = ParagraphAlignment.Center });
    }

    /// <summary>Removes all paragraphs and tables.</summary>
    public new void Clear()
    {
        base.Clear();
        _tables.Clear();
    }

    /// <summary>Removes a table.</summary>
    public bool RemoveTable(DocTable table)
    {
        int i = _tables.FindIndex(e => ReferenceEquals(e.Table, table));
        if (i < 0) return false;
        _tables.RemoveAt(i);
        return true;
    }

    /// <summary>
    /// When empty: true (default) shows the previous section's header/footer of this kind;
    /// false writes a blank one. Ignored when there is content.
    /// </summary>
    public bool LinkToPrevious { get; set; } = true;

    /// <summary>Adds a table after the current last paragraph (a paragraph is written after a trailing table, as Word requires).</summary>
    public DocTable AddTable(int columns)
    {
        var t = new DocTable(columns);
        _tables.Add((Count > 0 ? this[^1] : null, t));
        return t;
    }

    /// <summary>Tables in this header/footer, in content order.</summary>
    public IReadOnlyList<DocTable> Tables => Blocks.OfType<DocTable>().ToList();

    /// <summary>Paragraphs and tables in content order (a table follows the paragraph that was last when it was added).</summary>
    public IReadOnlyList<object> Blocks
    {
        get
        {
            var list = new List<object>(Count + _tables.Count);
            if (_tables.Count == 0) { foreach (var p in this) list.Add(p); return list; }
            var after = new Dictionary<Paragraph, List<DocTable>>(ReferenceEqualityComparer.Instance);
            foreach (var (a, t) in _tables)
            {
                if (a is null) { list.Add(t); continue; }
                if (!after.TryGetValue(a, out var l)) after[a] = l = new List<DocTable>();
                l.Add(t);
            }
            foreach (var p in this)
            {
                list.Add(p);
                if (after.Remove(p, out var ts)) list.AddRange(ts); // first occurrence only
            }
            foreach (var ts in after.Values) list.AddRange(ts); // anchors no longer present
            return list;
        }
    }

    /// <summary>Loader: appends a table after the paragraphs read so far.</summary>
    internal void AppendTable(DocTable t) => _tables.Add((Count > 0 ? this[^1] : null, t));

    /// <summary>Copies another header/footer's content (paragraph and table objects are shared, not cloned).</summary>
    internal void CopyFrom(HeaderFooterContent other)
    {
        Clear();
        _tables.Clear();
        AddRange(other);
        _tables.AddRange(other._tables);
        LinkToPrevious = other.LinkToPrevious;
    }


    internal bool HasContent => Count > 0 || _tables.Count > 0;
}

/// <summary>
/// Options for encrypting a docx when saving. Pass to
/// <see cref="Document.SaveTo(string, EncryptionOptions)"/>. Identical scheme to
/// Chuvadi.Sheets: OOXML Agile Encryption, AES-256-CBC, iterated-SHA-512 key derivation,
/// HMAC-SHA512 integrity (verified on read).
/// </summary>
public sealed class EncryptionOptions
{
    /// <summary>The password required to open the file. Cannot be null or empty.</summary>
    public required string Password { get; init; }

    /// <summary>
    /// Key-derivation iteration count ("spin count"). Default 100,000 matches modern Word.
    /// Higher = slower brute force; lower = faster save/open. Don't go below 50,000.
    /// </summary>
    public int SpinCount { get; init; } = 100_000;

    /// <summary>
    /// Folder for the temporary file that <see cref="Document.SaveTo(string, EncryptionOptions)"/>
    /// and <see cref="Document.SaveTo(System.IO.Stream, EncryptionOptions)"/> write while they build
    /// the package (D-217). Null, the default, uses <see cref="System.IO.Path.GetTempPath"/>.
    /// <para>The file is named <c>chuvadi_docs_*.tmp</c>. It holds the package encrypted with a
    /// one-time key kept only in memory, is opened exclusively, is owner-only on Unix (mode 600), and
    /// is deleted on close. A relative path is resolved against the current directory at save time,
    /// and an empty string means the current directory. If the folder is missing or cannot be written
    /// to, the save throws the system's exception (it names the temporary file) and the output is not
    /// changed. The same setting as Chuvadi.Sheets' <c>XlsxWriterOptions.TempDirectory</c>.</para>
    /// <para>Not used by <see cref="DocxTemplate"/> with <see cref="TemplateOptions.OutputEncryption"/>,
    /// which encrypts in memory. A path save's own temporary file always goes beside the target.</para>
    /// </summary>
    public string? TempDirectory { get; init; }

    private readonly TempStorage _tempStorage = TempStorage.File;

    /// <summary>
    /// Where the encrypted saves build the package before encrypting it (D-217, D-296).
    /// <see cref="Model.TempStorage.File"/>, the default, is the temporary file described under
    /// <see cref="TempDirectory"/>. <see cref="Model.TempStorage.Memory"/> builds it in process memory
    /// instead: no file is created anywhere and <see cref="TempDirectory"/> is ignored, so the save works
    /// on a read-only file system.
    /// <para>With <see cref="Model.TempStorage.Memory"/> the in-memory copy is not encrypted: it holds the
    /// same text the document already holds in memory, and it is gone when the save returns. Memory use
    /// grows by about two and a half times the package size at the peak, and a package over about 2 GB
    /// fails with the system's <see cref="System.IO.IOException"/>. The decrypted package is the same
    /// either way. The same setting, with the same values and meaning, as Chuvadi.Sheets'
    /// <c>XlsxWriterOptions.TempStorage</c>.</para>
    /// <para>Not used by <see cref="DocxTemplate"/> with <see cref="TemplateOptions.OutputEncryption"/>,
    /// which always encrypts in memory.</para>
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">The value is not a defined <see cref="Model.TempStorage"/>.</exception>
    public TempStorage TempStorage
    {
        get => _tempStorage;
        init
        {
            if (!System.Enum.IsDefined(value))
                throw new System.ArgumentOutOfRangeException(nameof(value), value, "Not a defined TempStorage value.");
            _tempStorage = value;
        }
    }
}

/// <summary>
/// Where a save keeps its temporary working copy (<see cref="EncryptionOptions.TempStorage"/>).
/// The same type, values and meaning as Chuvadi.Sheets' <c>TempStorage</c>.
/// </summary>
public enum TempStorage
{
    /// <summary>A temporary file, encrypted with a one-time key, owner-only and deleted on close (the default).</summary>
    File = 0,

    /// <summary>Process memory: no temporary file anywhere.</summary>
    Memory = 1,
}

/// <summary>Thrown when reading an encrypted docx without a password, or with the wrong one.</summary>
public sealed class DocxPasswordRequiredException : Exception
{
    public DocxPasswordRequiredException(string message) : base(message) { }
    public DocxPasswordRequiredException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when a file is not a structurally valid docx. <see cref="DocxSecurityLimitException"/>
/// derives from it, so one handler covers both malformed and resource-limit-breaching input.
/// </summary>
public class DocxFormatException : Exception
{
    public DocxFormatException(string message) : base(message) { }
    public DocxFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when input to an importer (the Markdown, HTML and text importers of
/// Chuvadi.Docs.Conversion) exceeds a configured limit. The call produces no partial result.
/// Follows the family pattern of Chuvadi.Sheets' <c>XlsxLimitExceededException</c>: the same
/// property names and types, the same message style, and it derives from
/// <see cref="System.IO.IOException"/>. It is never wrapped. When a lower-level library detected
/// the limit (Chuvadi.Markup), its exception is the <see cref="Exception.InnerException"/>.
/// </summary>
[System.Serializable]
public sealed class DocxLimitExceededException : System.IO.IOException
{
    /// <summary>The option that was exceeded, e.g. "MaxInputChars".</summary>
    public string LimitName { get; }

    /// <summary>The configured limit.</summary>
    public double Limit { get; }

    /// <summary>The value that exceeded it (as far as known when the check failed).</summary>
    public double Actual { get; }

    public DocxLimitExceededException(string limitName, double limit, double actual, string detail)
        : base($"{limitName} exceeded ({actual} > {limit}): {detail}")
    {
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }

    /// <summary>As the four-argument constructor, keeping the lower-level exception that detected the limit.</summary>
    public DocxLimitExceededException(string limitName, double limit, double actual, string detail, Exception innerException)
        : base($"{limitName} exceeded ({actual} > {limit}): {detail}", innerException)
    {
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }
}

/// <summary>
/// "Restrict editing" protection written into settings.xml. Like sheet protection in Excel,
/// this is COOPERATIVE locking — the content remains readable by anyone and a determined
/// user can strip it. For confidentiality, use <see cref="EncryptionOptions"/> instead.
/// </summary>
public enum DocumentProtectionMode
{
    /// <summary>No editing allowed (read-only).</summary>
    ReadOnly,
    /// <summary>Only comments may be added.</summary>
    Comments,
    /// <summary>Only tracked changes may be made.</summary>
    TrackedChanges,
    /// <summary>Only form fields may be filled.</summary>
    Forms,
}
