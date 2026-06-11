using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>Page size, orientation, and margins for the document's section.</summary>
public sealed class PageSetup
{
    public PageSize Size { get; set; } = PageSize.A4;
    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

    /// <summary>Margins in points (72pt = 1 inch). Defaults: 1-inch all around.</summary>
    public double TopMarginPt { get; set; } = 72;
    public double BottomMarginPt { get; set; } = 72;
    public double LeftMarginPt { get; set; } = 72;
    public double RightMarginPt { get; set; } = 72;

    /// <summary>(width, height) in twips (1/20 pt) for the PORTRAIT orientation of <see cref="Size"/>.</summary>
    internal (int W, int H) PortraitTwips => Size switch
    {
        PageSize.A4 => (11906, 16838),
        PageSize.Letter => (12240, 15840),
        PageSize.Legal => (12240, 20160),
        _ => (11906, 16838),
    };
}

/// <summary>
/// Content for a page header or footer: a list of paragraphs, which may include page-number
/// fields via <see cref="Paragraph.PageNumber"/>.
///
/// <code>
/// doc.Header.Add(new Paragraph("Quarterly Report") { Alignment = ParagraphAlignment.Center });
/// doc.Footer.Add(new Paragraph().PageNumber(includeTotal: true));
/// doc.Footer[0].Alignment = ParagraphAlignment.Right;
/// </code>
/// </summary>
public sealed class HeaderFooterContent : List<Paragraph>
{
    /// <summary>Convenience: adds a single centered text paragraph.</summary>
    public void SetText(string text, TextFormat? format = null)
    {
        Clear();
        Add(new Paragraph(text, format) { Alignment = ParagraphAlignment.Center });
    }

    internal bool HasContent => Count > 0;
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
}

/// <summary>Thrown when reading an encrypted docx without a password, or with the wrong one.</summary>
public sealed class DocxPasswordRequiredException : Exception
{
    public DocxPasswordRequiredException(string message) : base(message) { }
    public DocxPasswordRequiredException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when a file is not a structurally valid docx.</summary>
public sealed class DocxFormatException : Exception
{
    public DocxFormatException(string message) : base(message) { }
    public DocxFormatException(string message, Exception innerException) : base(message, innerException) { }
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
