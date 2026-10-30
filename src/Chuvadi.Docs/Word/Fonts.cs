using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>Which face of a font an embedded font file is.</summary>
public enum EmbeddedFontStyle
{
    Regular,
    Bold,
    Italic,
    BoldItalic,
}

/// <summary>
/// A font file embedded in the document (w:embedRegular … under word/fonts/). <see cref="Data"/>
/// is the plain font (TrueType / OpenType): the obfuscation Word applies in the package
/// (.odttf, keyed by <see cref="FontKey"/>) is removed on load and applied again on save.
/// </summary>
public sealed class EmbeddedFont
{
    public EmbeddedFont(EmbeddedFontStyle style, byte[] data)
    {
        if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style));
        Style = style;
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>Regular, bold, italic or bold italic.</summary>
    public EmbeddedFontStyle Style { get; }

    /// <summary>The plain font file bytes.</summary>
    public byte[] Data { get; set; }

    /// <summary>The obfuscation key (w:fontKey) the file used; null = a new key is made on save.</summary>
    public Guid? FontKey { get; set; }

    /// <summary>Only the characters used are embedded (w:subsetted).</summary>
    public bool Subsetted { get; set; }
}

/// <summary>
/// One entry of the document's font table (word/fontTable.xml): the font's name and the hints
/// Word uses to find a substitute (alternate name, PANOSE, character set, family, pitch,
/// Unicode / code-page signature), plus any embedded font files.
/// </summary>
public sealed class FontInfo
{
    public FontInfo(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Font name required.", nameof(name));
        Name = name;
    }

    /// <summary>The font name runs refer to.</summary>
    public string Name { get; set; }

    /// <summary>An alternate name (w:altName), e.g. the font's name in another language.</summary>
    public string? AltName { get; set; }

    /// <summary>PANOSE-1 classification as 20 hex digits (w:panose1).</summary>
    public string? Panose { get; set; }

    /// <summary>Character set as 2 hex digits (w:charset), e.g. "00" ANSI, "02" symbol.</summary>
    public string? Charset { get; set; }

    /// <summary>Font family (w:family): roman, swiss, modern, script, decorative or auto.</summary>
    public string? Family { get; set; }

    /// <summary>Pitch (w:pitch): fixed, variable or default.</summary>
    public string? Pitch { get; set; }

    /// <summary>The font is not TrueType (w:notTrueType).</summary>
    public bool NotTrueType { get; set; }

    /// <summary>Unicode and code-page signature (w:sig): usb0–usb3, csb0, csb1 as 8 hex digits each; null when absent.</summary>
    public FontSignature? Signature { get; set; }

    /// <summary>Embedded font files for this font (one per style at most).</summary>
    public List<EmbeddedFont> Embedded { get; } = new();
}

/// <summary>The w:sig values of a font table entry (hex strings as written).</summary>
public sealed record FontSignature(string Usb0, string Usb1, string Usb2, string Usb3, string Csb0, string Csb1);
