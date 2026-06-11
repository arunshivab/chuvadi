using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>Adds a heading (level 1–3).</summary>
    public Paragraph AddHeading(string text, int level = 1)
    {
        var style = level switch
        {
            1 => ParagraphStyle.Heading1,
            2 => ParagraphStyle.Heading2,
            3 => ParagraphStyle.Heading3,
            _ => throw new ArgumentOutOfRangeException(nameof(level), "Heading level must be 1–3."),
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

    /// <summary>Saves the docx package to a stream.</summary>
    public void SaveTo(Stream output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        DocumentSerializer.Write(output, this);
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
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        if (Chuvadi.Internal.Crypto.EncryptedPackageReader.IsEncryptedPackage(input))
        {
            byte[] plaintext;
            try
            {
                plaintext = Chuvadi.Internal.Crypto.EncryptedPackageReader.DecryptToPlaintextPackage(input, password);
            }
            catch (Chuvadi.Internal.Crypto.PackagePasswordException ex)
            {
                throw new DocxPasswordRequiredException(ex.Message, ex);
            }
            using var ms = new MemoryStream(plaintext);
            return DocumentLoader.Load(ms);
        }
        return DocumentLoader.Load(input);
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
