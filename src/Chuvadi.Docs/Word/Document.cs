using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Docs.Internal;

namespace Chuvadi.Docs.Word;

/// <summary>
/// An in-memory Word document: an ordered list of blocks (paragraphs and tables) plus page
/// setup, header/footer, optional restrict-editing protection, and optional encryption at
/// save time.
///
/// <code>
/// var doc = new Document();
/// doc.AddParagraph("Quarterly Report", ParagraphStyle.Title);
/// doc.AddParagraph("Revenue grew 18% year over year.");
/// var t = doc.AddTable(2);
/// t.HeaderRow("Metric", "Value");
/// t.AddRow("Revenue", "₹1.2 Cr");
/// doc.Footer.Add(new Paragraph().PageNumber(includeTotal: true));
/// doc.SaveTo("report.docx");
/// // or password-protected:
/// doc.SaveTo("report.docx", new EncryptionOptions { Password = "secret" });
/// </code>
///
/// LOADING: <see cref="Load(string)"/> reads text content — paragraphs with basic run
/// formatting (bold/italic/underline/strike, font, size, color) and paragraph style/
/// alignment, plus tables as text cells. Images, fields, comments, tracked changes, content
/// controls, and section variations beyond the first are NOT preserved through a
/// load → save round-trip. For filling a designed template while preserving everything in
/// it, use <see cref="DocxTemplate"/> instead.
/// </summary>
public sealed class Document
{
    private readonly List<object> _blocks = new();  // Paragraph | DocTable

    /// <summary>Creates an empty document with the built-in styles and Calibri 11pt defaults.</summary>
    public Document()
    {
        StylesXml.AddBuiltIns(Styles);
        Numbering = new NumberingCollection(this);
    }

    /// <summary>
    /// The document's styles (paragraph, character, table, numbering). Starts with the
    /// built-in set; <see cref="Load(string)"/> replaces it with the file's styles.
    /// </summary>
    public StyleCollection Styles { get; } = new();

    /// <summary>Document-wide default run and paragraph formatting (w:docDefaults).</summary>
    public DocumentDefaults Defaults { get; } = new();

    /// <summary>
    /// The document's DrawingML theme part (word/theme/theme1.xml) as raw bytes: the theme
    /// fonts and colours that <see cref="ThemeFont"/> / <see cref="ThemeColor"/> references
    /// resolve against. Carried through unchanged from a loaded document so theme-based
    /// formatting survives load → save; null for new documents (none is written).
    /// </summary>
    public byte[]? ThemeXml { get; set; }

    /// <summary>
    /// The document's lists (numbering.xml): definitions and the list instances paragraphs use.
    /// <see cref="Load(string)"/> fills it from the file. Paragraphs added with
    /// <see cref="AddBullet"/> / <see cref="AddNumbered"/> use two built-in default lists that
    /// are written automatically and do not appear here.
    /// </summary>
    public NumberingCollection Numbering { get; }

    /// <summary>
    /// The built-in default lists behind <see cref="ListKind"/> (not in <see cref="Numbering"/>;
    /// ids -1 and -2, real numIds are assigned at save). Created eagerly so concurrent readers
    /// share one pair; the saved numbering is built from these definitions.
    /// </summary>
    internal (ListInstance Bullets, ListInstance Numbers) LegacyDefaults { get; } = (
        new ListInstance(-1, LegacyDefinition(NumberingDefinition.Bulleted())),
        new ListInstance(-2, LegacyDefinition(NumberingDefinition.Numbered())));

    private static NumberingDefinition LegacyDefinition(NumberingDefinition d)
    {
        d.MultiLevelType = null; // the pre-1.4 output has no multiLevelType element
        return d;
    }

    /// <summary>
    /// The label Word shows for every numbered or bulleted paragraph — body (tables and nested
    /// tables included) and each header/footer, each story counted separately — e.g. "1.",
    /// "2.3.", "iv)", "•". Counting follows Word: instances of one definition continue each
    /// other unless restarted with a start override, deeper levels restart after a shallower
    /// item (or as <see cref="NumberingLevel.RestartAfterLevel"/> says), legal numbering shows
    /// decimals, and numbering applied through paragraph styles (and numStyleLink) is included.
    /// Paragraphs that are not list items are absent.
    /// </summary>
    public IReadOnlyDictionary<Paragraph, ListLabel> GetListLabels() => ListNumbering.Compute(this);

    /// <summary>The label of one paragraph (see <see cref="GetListLabels"/>), or null when it is not a list item. Computes the whole story; call <see cref="GetListLabels"/> once for many paragraphs.</summary>
    public ListLabel? GetListLabel(Paragraph paragraph)
    {
        if (paragraph is null) throw new ArgumentNullException(nameof(paragraph));
        return ListNumbering.Compute(this).TryGetValue(paragraph, out var l) ? l : null;
    }

    /// <summary>Adds a paragraph that is an item of <paramref name="list"/> at <paramref name="level"/> (List Paragraph style).</summary>
    public Paragraph AddListItem(string text, ListInstance list, int level = 0)
    {
        if (list is null) throw new ArgumentNullException(nameof(list));
        if (!Numbering.Contains(list))
            throw new ArgumentException("The list is not in this document's Numbering (another document's list, a removed list, or a built-in default list).", nameof(list));
        var p = new Paragraph(text) { Style = ParagraphStyle.ListParagraph }.SetList(list, level);
        _blocks.Add(p);
        return p;
    }

    /// <summary>
    /// The EFFECTIVE formatting of <paramref name="run"/> in <paramref name="paragraph"/>:
    /// document defaults, then the paragraph style chain, then the character style chain, then
    /// the run's direct formatting — with toggle properties (bold, italic, caps…) set by both
    /// the paragraph and character styles combining by exclusive-or, as Word does. The result
    /// states complex-script properties explicitly (<see cref="TextFormat.MirrorToComplexScript"/>
    /// is false). Use it to render or measure text exactly as Word would style it.
    /// </summary>
    public TextFormat GetEffectiveRunFormat(Paragraph paragraph, Run run)
    {
        if (paragraph is null) throw new ArgumentNullException(nameof(paragraph));
        if (run is null) throw new ArgumentNullException(nameof(run));
        return FormatResolver.ResolveRun(this, paragraph, run);
    }

    /// <summary>
    /// As <see cref="GetEffectiveRunFormat(Paragraph, Run)"/> for a run inside a table cell:
    /// the table style's run formatting — whole table, then the conditional regions that apply
    /// to <paramref name="cell"/> (header row, banding, first column…) — forms the lowest style
    /// level, below the paragraph and character styles, and takes part in the toggle rule.
    /// </summary>
    public TextFormat GetEffectiveRunFormat(Paragraph paragraph, Run run, TableCellRef cell)
    {
        if (paragraph is null) throw new ArgumentNullException(nameof(paragraph));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (cell.Table is null) throw new ArgumentNullException(nameof(cell));
        return FormatResolver.ResolveRun(this, paragraph, run, cell);
    }

    /// <summary>As <see cref="GetEffectiveParagraphFormat(Paragraph)"/> for a paragraph inside a table cell (table style level included).</summary>
    public ParagraphFormat GetEffectiveParagraphFormat(Paragraph paragraph, TableCellRef cell)
    {
        if (paragraph is null) throw new ArgumentNullException(nameof(paragraph));
        if (cell.Table is null) throw new ArgumentNullException(nameof(cell));
        return FormatResolver.ResolveParagraph(this, paragraph, cell);
    }

    /// <summary>
    /// The EFFECTIVE table format: the table style chain, then the table's direct formatting
    /// (borders and default cell margins merge per side). The legacy default grid
    /// (<see cref="DocTable.Borders"/>) counts as direct borders.
    /// </summary>
    public TableFormat GetEffectiveTableFormat(DocTable table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        return FormatResolver.ResolveTable(this, table);
    }

    /// <summary>
    /// The EFFECTIVE formatting of one cell — shading, margins, vertical alignment, text
    /// direction — with all four edges resolved: table borders mapped onto the cell (outer edge
    /// or inside line), then the table style's cell format and each applicable conditional
    /// region (header row, last row, banding, first/last column, corner cells, in Word's order),
    /// then the cell's direct formatting. A null edge means no border.
    /// </summary>
    public TableCellFormat GetEffectiveCellFormat(TableCellRef cell)
    {
        if (cell.Table is null) throw new ArgumentNullException(nameof(cell));
        return FormatResolver.ResolveCell(this, cell);
    }

    /// <summary>
    /// The EFFECTIVE paragraph formatting: document defaults, then the paragraph style chain,
    /// then the paragraph's direct formatting — with a list item's level formatting (indents,
    /// number tab) below the style when the numbering comes from the style, and above it when
    /// the numbering is applied to the paragraph directly, as Word renders it. Tab stops accumulate (a Clear stop removes an
    /// inherited one) and borders merge per side. Returns a new, independent object.
    /// </summary>
    public ParagraphFormat GetEffectiveParagraphFormat(Paragraph paragraph)
    {
        if (paragraph is null) throw new ArgumentNullException(nameof(paragraph));
        return FormatResolver.ResolveParagraph(this, paragraph);
    }

    /// <summary>Page size, orientation, margins.</summary>
    public PageSetup Page { get; } = new();

    /// <summary>Default page header (all pages, or pages 2+ when <see cref="DifferentFirstPage"/>).</summary>
    public HeaderFooterContent Header { get; } = new();

    /// <summary>Default page footer.</summary>
    public HeaderFooterContent Footer { get; } = new();

    /// <summary>Header used on page 1 only. Setting content here enables the title-page flag.</summary>
    public HeaderFooterContent FirstPageHeader { get; } = new();

    /// <summary>Footer used on page 1 only.</summary>
    public HeaderFooterContent FirstPageFooter { get; } = new();

    /// <summary>True when first-page header/footer differ from the rest.</summary>
    public bool DifferentFirstPage => FirstPageHeader.HasContent || FirstPageFooter.HasContent;

    internal bool IsProtected { get; private set; }
    internal string? ProtectionPassword { get; private set; }
    internal DocumentProtectionMode ProtectionMode { get; private set; }

    /// <summary>Document blocks in order. Items are <see cref="Paragraph"/> or <see cref="DocTable"/>.</summary>
    public IReadOnlyList<object> Blocks => _blocks;

    private readonly List<ImageInfo> _images = new();

    /// <summary>
    /// Images discovered when the document was loaded (body, headers, and footers), with
    /// their bytes, display size, placement, and table location. Empty for documents built
    /// in memory — images you add via <see cref="AddImage"/> are stored on their paragraphs,
    /// not here. This list is for inspecting LOADED documents (e.g. for the PDF pipeline).
    /// </summary>
    public IReadOnlyList<ImageInfo> Images => _images;

    internal void AddLoadedImage(ImageInfo image) => _images.Add(image);

    // ---- Building -------------------------------------------------------------------

    /// <summary>Adds an empty paragraph and returns it for fluent run building.</summary>
    public Paragraph AddParagraph()
    {
        var p = new Paragraph();
        _blocks.Add(p);
        return p;
    }

    /// <summary>Adds a paragraph of plain text with an optional style.</summary>
    public Paragraph AddParagraph(string text, ParagraphStyle style = ParagraphStyle.Normal, TextFormat? format = null)
    {
        var p = new Paragraph(text, format) { Style = style };
        _blocks.Add(p);
        return p;
    }

    /// <summary>Adds an existing paragraph object.</summary>
    public Paragraph AddParagraph(Paragraph paragraph)
    {
        _blocks.Add(paragraph ?? throw new ArgumentNullException(nameof(paragraph)));
        return paragraph;
    }

    /// <summary>Adds a heading (level 1–9, Word's built-in Heading 1 … Heading 9 styles).</summary>
    public Paragraph AddHeading(string text, int level = 1)
    {
        var style = level switch
        {
            1 => ParagraphStyle.Heading1,
            2 => ParagraphStyle.Heading2,
            3 => ParagraphStyle.Heading3,
            4 => ParagraphStyle.Heading4,
            5 => ParagraphStyle.Heading5,
            6 => ParagraphStyle.Heading6,
            7 => ParagraphStyle.Heading7,
            8 => ParagraphStyle.Heading8,
            9 => ParagraphStyle.Heading9,
            _ => throw new ArgumentOutOfRangeException(nameof(level), "Heading level must be 1–9."),
        };
        return AddParagraph(text, style);
    }

    /// <summary>Adds one bulleted list item. Call repeatedly for multiple items.</summary>
    public Paragraph AddBullet(string text, int level = 0)
    {
        var p = new Paragraph(text) { Style = ParagraphStyle.ListParagraph, List = ListKind.Bullet, ListLevel = level };
        _blocks.Add(p);
        return p;
    }

    /// <summary>Adds one numbered list item. Call repeatedly for multiple items.</summary>
    public Paragraph AddNumbered(string text, int level = 0)
    {
        var p = new Paragraph(text) { Style = ParagraphStyle.ListParagraph, List = ListKind.Number, ListLevel = level };
        _blocks.Add(p);
        return p;
    }

    /// <summary>Adds a table with the given number of columns.</summary>
    public DocTable AddTable(int columns)
    {
        var t = new DocTable(columns);
        _blocks.Add(t);
        return t;
    }

    /// <summary>Inserts a page break (an empty paragraph that starts a new page).</summary>
    public void AddPageBreak()
        => _blocks.Add(new Paragraph { PageBreakBefore = true });

    /// <summary>Adds a new paragraph containing the image (inline or floating).</summary>
    public Paragraph AddImage(ImageSpec image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        var p = new Paragraph().Image(image);
        _blocks.Add(p);
        return p;
    }

    /// <summary>
    /// Applies "restrict editing" protection. COOPERATIVE locking only — content stays
    /// readable; use <see cref="EncryptionOptions"/> for confidentiality. The password is
    /// hashed (iterated SHA-512), never stored in plaintext.
    /// </summary>
    public Document Protect(string password, DocumentProtectionMode mode = DocumentProtectionMode.ReadOnly)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password required.", nameof(password));
        IsProtected = true;
        ProtectionPassword = password;
        ProtectionMode = mode;
        return this;
    }

    // ---- Saving ---------------------------------------------------------------------

    /// <summary>Saves to a .docx file path (created or overwritten).</summary>
    public void SaveTo(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        SaveTo(stream);
    }

    /// <summary>Saves the docx package to a stream, which is flushed and left OPEN (the caller disposes it).</summary>
    public void SaveTo(Stream output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        // The caller owns the stream: it is flushed but left open (before 1.2 it was disposed).
        DocumentSerializer.Write(new NonClosingStream(output), this);
    }

    /// <summary>
    /// Saves to an ENCRYPTED .docx requiring <see cref="EncryptionOptions.Password"/> to
    /// open in Word. The plaintext package is spooled to a temp file (deleted afterwards),
    /// never held fully in memory, then encrypted with OOXML Agile Encryption.
    /// </summary>
    public void SaveTo(string path, EncryptionOptions encryption)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        if (encryption is null) throw new ArgumentNullException(nameof(encryption));
        if (string.IsNullOrEmpty(encryption.Password))
            throw new ArgumentException("Encryption password cannot be empty.", nameof(encryption));

        var tempPath = Path.Combine(Path.GetTempPath(), $"chuvadi_docs_pkg_{Guid.NewGuid():N}.tmp");
        try
        {
            using (var spool = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 64 * 1024))
            {
                SaveTo(spool);
            }
            using var spoolRead = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            Chuvadi.Internal.Crypto.EncryptedPackageWriter.WriteEncrypted(
                output, spoolRead, spoolRead.Length, encryption.Password, encryption.SpinCount);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ---- Loading --------------------------------------------------------------------

    /// <summary>Loads an unencrypted .docx (see class docs for what is preserved).</summary>
    public static Document Load(string path) => Load(path, password: null);

    /// <summary>
    /// Loads a possibly-encrypted .docx. If the file is encrypted, the password is required;
    /// if not encrypted, the password is ignored. Throws
    /// <see cref="DocxPasswordRequiredException"/> on a missing or wrong password.
    /// </summary>
    public static Document Load(string path, string? password)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Load(fs, password);
    }

    /// <summary>Loads from an open stream (not closed). Password as in <see cref="Load(string, string?)"/>.</summary>
    public static Document Load(Stream input, string? password = null)
        => Load(input, password, options: null, CancellationToken.None);

    /// <summary>
    /// Loads a possibly-encrypted .docx with explicit read options (resource limits for
    /// untrusted input) and cancellation. Limit breaches throw
    /// <see cref="DocxSecurityLimitException"/>; cancellation throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static Document Load(string path, string? password, DocxReaderOptions? options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Load(fs, password, options, cancellationToken);
    }

    /// <summary>
    /// Loads from an open stream (not closed) with explicit read options and cancellation.
    /// See <see cref="Load(string, string?, DocxReaderOptions?, CancellationToken)"/>.
    /// </summary>
    public static Document Load(Stream input, string? password, DocxReaderOptions? options,
        CancellationToken cancellationToken = default)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        options ??= new DocxReaderOptions();
        cancellationToken.ThrowIfCancellationRequested();

        var source = PackageGuard.EnsureSeekable(input, options, cancellationToken, out var buffered);
        using var _ = buffered;

        if (Chuvadi.Internal.Crypto.EncryptedPackageReader.IsEncryptedPackage(source))
        {
            byte[] plaintext = PackageGuard.Decrypt(source, password, options, cancellationToken);
            using var ms = new MemoryStream(plaintext);
            return DocumentLoader.Load(ms, options, cancellationToken);
        }
        return DocumentLoader.Load(source, options, cancellationToken);
    }

    /// <summary>All document text: paragraphs and table cells, newline-separated.</summary>
    public string GetText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in _blocks)
        {
            if (block is Paragraph p) sb.AppendLine(p.GetText());
            else if (block is DocTable t)
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        sb.AppendLine(cell.GetText());
        }
        return sb.ToString();
    }

    internal void AddBlock(object block) => _blocks.Add(block);
}
