using System;

namespace Chuvadi.Docs.Word;

/// <summary>
/// Character-level (run) formatting. All members are optional; unset members inherit from
/// the paragraph style / document defaults (Calibri 11pt). Immutable — build once, reuse.
/// </summary>
public sealed class TextFormat
{
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }

    /// <summary>Font family name (e.g. "Calibri", "Arial"). Null = inherit.</summary>
    public string? Font { get; init; }

    /// <summary>Font size in points (e.g. 11, 14). Null/0 = inherit.</summary>
    public double SizePt { get; init; }

    /// <summary>Text color as 6-digit hex WITHOUT '#', e.g. "C00000". Null = inherit.</summary>
    public string? ColorHex { get; init; }

    /// <summary>Highlight color name per OOXML (yellow, green, cyan, magenta, red, blue,
    /// darkGray, lightGray...). Null = none.</summary>
    public string? Highlight { get; init; }

    /// <summary>True when every member is at its default (nothing to emit).</summary>
    internal bool IsDefault =>
        !Bold && !Italic && !Underline && !Strikethrough
        && Font is null && SizePt == 0 && ColorHex is null && Highlight is null;

    public static readonly TextFormat None = new();
    public static readonly TextFormat BoldText = new() { Bold = true };
    public static readonly TextFormat ItalicText = new() { Italic = true };
}

/// <summary>Built-in paragraph styles emitted into styles.xml. Word renders these with
/// sensible defaults and lists them in its style gallery.</summary>
public enum ParagraphStyle
{
    Normal,
    Title,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    /// <summary>Used automatically for bulleted/numbered list items.</summary>
    ListParagraph,
}

/// <summary>Paragraph alignment.</summary>
public enum ParagraphAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

/// <summary>List kind for <see cref="Paragraph"/> items that are part of a list.</summary>
public enum ListKind
{
    None,
    Bullet,
    Number,
}

/// <summary>Page sizes (portrait dimensions; flipped automatically for landscape).</summary>
public enum PageSize
{
    A4,
    Letter,
    Legal,
}

/// <summary>Page orientation.</summary>
public enum PageOrientation
{
    Portrait,
    Landscape,
}
