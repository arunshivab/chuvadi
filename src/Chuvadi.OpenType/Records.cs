namespace Chuvadi.OpenType;

/// <summary>One entry of a font's table directory.</summary>
/// <param name="Tag">The table tag.</param>
/// <param name="Checksum">The checksum recorded in the directory (not verified).</param>
/// <param name="Offset">Byte offset of the table from the start of the font data.</param>
/// <param name="Length">Length of the table in bytes.</param>
public readonly record struct TableRecord(Tag Tag, uint Checksum, int Offset, int Length);

/// <summary>A glyph bounding box in font units.</summary>
/// <param name="XMin">Minimum x.</param>
/// <param name="YMin">Minimum y.</param>
/// <param name="XMax">Maximum x.</param>
/// <param name="YMax">Maximum y.</param>
public readonly record struct GlyphBounds(int XMin, int YMin, int XMax, int YMax)
{
    /// <summary>Gets the width, XMax - XMin.</summary>
    public int Width => XMax - XMin;

    /// <summary>Gets the height, YMax - YMin.</summary>
    public int Height => YMax - YMin;
}

/// <summary>The kind of glyph outlines a font carries.</summary>
public enum OutlineFormat
{
    /// <summary>No outline table ('glyf', 'CFF ' or 'CFF2') was found.</summary>
    None,

    /// <summary>TrueType quadratic outlines ('glyf' and 'loca').</summary>
    TrueType,

    /// <summary>CFF version 1 outlines ('CFF ').</summary>
    Cff,

    /// <summary>CFF version 2 outlines ('CFF2'). Glyph bounds are not read for CFF2 in this version.</summary>
    Cff2,
}

/// <summary>The container format of font data.</summary>
public enum FontContainerKind
{
    /// <summary>A single sfnt font (TrueType or OpenType).</summary>
    Sfnt,

    /// <summary>A TrueType/OpenType collection ('ttcf').</summary>
    Collection,
}
