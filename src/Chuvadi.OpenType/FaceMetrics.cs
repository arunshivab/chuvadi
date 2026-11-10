using Chuvadi.OpenType.Tables;

namespace Chuvadi.OpenType;

/// <summary>
/// Derived face values (names, metrics, style) worked out from the raw tables once per font (D-201, D-247).
/// The member names match the published FontFace of Chuvadi.Pdf.Fonts.Rendering, so code using it can move to
/// Chuvadi.OpenType by renaming. All lengths are in font design units.
/// </summary>
/// <remarks>
/// PDF-specific policy (a font descriptor's StemV and flags) is not here; it stays with the PDF writer, which reads
/// the raw inputs: <see cref="WeightClass"/>, <see cref="HeadTable.MacStyle"/>, <see cref="PostTable.ItalicAngle"/>,
/// <see cref="PostTable.IsFixedPitch"/>, <see cref="Os2Table.FamilyClassId"/> and
/// <see cref="Os2Table.FamilySubclassId"/>, <see cref="Os2Table.Panose"/> and <see cref="CharacterMap.HasSymbolSubtable"/>.
/// </remarks>
public sealed class FaceMetrics
{
    internal FaceMetrics(OpenTypeFont font)
    {
        // TRACE R-OT-0054
        NameTable? names = font.Names;
        FamilyName = names?.GetName(16) ?? names?.GetName(1);
        SubfamilyName = names?.GetName(17) ?? names?.GetName(2);
        FullName = names?.GetName(4);
        PostScriptName = names?.GetName(6);

        Os2Table? os2 = font.Os2;
        HeadTable head = font.Head;
        PostTable? post = font.Post;
        HheaTable? hhea = font.Hhea;

        HasOs2Table = os2 is not null;
        WeightClass = os2?.UsWeightClass ?? 0;
        IsBold = os2 is not null ? (os2.FsSelection & 0x0020) != 0 : (head.MacStyle & 0x0001) != 0;
        IsItalic = os2 is not null ? (os2.FsSelection & 0x0001) != 0 : (head.MacStyle & 0x0002) != 0;
        IsFixedPitch = post?.IsFixedPitch ?? false;
        FontBounds = new GlyphBounds(head.XMin, head.YMin, head.XMax, head.YMax);

        HheaAscender = hhea?.Ascender ?? 0;
        HheaDescender = hhea?.Descender ?? 0;
        HheaLineGap = hhea?.LineGap ?? 0;
        TypoAscender = os2?.STypoAscender ?? 0;
        TypoDescender = os2?.STypoDescender ?? 0;
        TypoLineGap = os2?.STypoLineGap ?? 0;
        WinAscent = os2?.UsWinAscent ?? 0;
        WinDescent = os2?.UsWinDescent ?? 0;

        // TRACE R-OT-0055
        UseTypoMetrics = os2 is not null && (os2.FsSelection & 0x0080) != 0;
        Ascender = UseTypoMetrics ? TypoAscender : HheaAscender;
        Descender = UseTypoMetrics ? TypoDescender : HheaDescender;
        LineGap = UseTypoMetrics ? TypoLineGap : HheaLineGap;

        // TRACE R-OT-0056
        CapHeight = os2?.SCapHeight is int cap ? cap : GlyphTop(font, 'H');
        XHeight = os2?.SxHeight is int xh ? xh : GlyphTop(font, 'x');

        UnderlinePosition = post?.UnderlinePosition ?? 0;
        UnderlineThickness = post?.UnderlineThickness ?? 0;
        StrikeoutPosition = os2?.YStrikeoutPosition ?? 0;
        StrikeoutSize = os2?.YStrikeoutSize ?? 0;
        ItalicAngle = post?.ItalicAngle ?? 0;
    }

    /// <summary>The typographic family name (name ID 16, else 1), or null.</summary>
    public string? FamilyName { get; }

    /// <summary>The typographic subfamily (style) name (name ID 17, else 2), or null.</summary>
    public string? SubfamilyName { get; }

    /// <summary>The full font name (name ID 4), or null.</summary>
    public string? FullName { get; }

    /// <summary>The PostScript name (name ID 6), or null.</summary>
    public string? PostScriptName { get; }

    /// <summary>The OS/2 weight class (400 regular, 700 bold); 0 without an OS/2 table.</summary>
    public int WeightClass { get; }

    /// <summary>Bold: OS/2 fsSelection bit 5, or head macStyle bit 0 without an OS/2 table.</summary>
    public bool IsBold { get; }

    /// <summary>Italic: OS/2 fsSelection bit 0, or head macStyle bit 1 without an OS/2 table.</summary>
    public bool IsItalic { get; }

    /// <summary>Every glyph has the same advance (post isFixedPitch).</summary>
    public bool IsFixedPitch { get; }

    /// <summary>The bounding box of all glyphs (head).</summary>
    public GlyphBounds FontBounds { get; }

    /// <summary>The hhea ascender.</summary>
    public int HheaAscender { get; }

    /// <summary>The hhea descender (negative below the baseline).</summary>
    public int HheaDescender { get; }

    /// <summary>The hhea line gap.</summary>
    public int HheaLineGap { get; }

    /// <summary>True when the font has an OS/2 table; the OS/2 values are 0 otherwise.</summary>
    public bool HasOs2Table { get; }

    /// <summary>The OS/2 typographic ascender.</summary>
    public int TypoAscender { get; }

    /// <summary>The OS/2 typographic descender (negative below the baseline).</summary>
    public int TypoDescender { get; }

    /// <summary>The OS/2 typographic line gap.</summary>
    public int TypoLineGap { get; }

    /// <summary>The OS/2 Windows ascent.</summary>
    public int WinAscent { get; }

    /// <summary>The OS/2 Windows descent, as a positive distance below the baseline.</summary>
    public int WinDescent { get; }

    /// <summary>OS/2 fsSelection bit 7 (USE_TYPO_METRICS).</summary>
    public bool UseTypoMetrics { get; }

    /// <summary>The ascender the font asks layout to use: typographic when <see cref="UseTypoMetrics"/>, else hhea.</summary>
    public int Ascender { get; }

    /// <summary>The descender chosen like <see cref="Ascender"/> (negative below the baseline).</summary>
    public int Descender { get; }

    /// <summary>The line gap chosen like <see cref="Ascender"/>.</summary>
    public int LineGap { get; }

    /// <summary>The cap height: OS/2 version 2 or later, else the top of the glyph for "H" (0 when none).</summary>
    public int CapHeight { get; }

    /// <summary>The x-height: OS/2 version 2 or later, else the top of the glyph for "x" (0 when none).</summary>
    public int XHeight { get; }

    /// <summary>The underline position (post; negative below the baseline).</summary>
    public int UnderlinePosition { get; }

    /// <summary>The underline thickness (post).</summary>
    public int UnderlineThickness { get; }

    /// <summary>The strikeout position above the baseline (OS/2).</summary>
    public int StrikeoutPosition { get; }

    /// <summary>The strikeout thickness (OS/2).</summary>
    public int StrikeoutSize { get; }

    /// <summary>The italic angle in degrees counter-clockwise from vertical (post).</summary>
    public double ItalicAngle { get; }

    private static int GlyphTop(OpenTypeFont font, char c)
    {
        int glyph = font.CharacterMap.GetGlyph(c);
        return glyph != 0 && font.TryGetGlyphBounds(glyph, out GlyphBounds b) ? b.YMax : 0;
    }
}
