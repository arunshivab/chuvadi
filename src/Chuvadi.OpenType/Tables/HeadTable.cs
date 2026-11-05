using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>The 'head' (font header) table. Values are as stored; nothing is validated except unitsPerEm != 0.</summary>
public sealed class HeadTable
{
    // TRACE R-OT-0006
    internal HeadTable(ReadOnlySpan<byte> s)
    {
        MajorVersion = BigEndian.U16(s, 0);
        MinorVersion = BigEndian.U16(s, 2);
        FontRevisionRaw = BigEndian.I32(s, 4);
        ChecksumAdjustment = BigEndian.U32(s, 8);
        MagicNumber = BigEndian.U32(s, 12);
        Flags = BigEndian.U16(s, 16);
        UnitsPerEm = BigEndian.U16(s, 18);
        CreatedRaw = BigEndian.I64(s, 20);
        ModifiedRaw = BigEndian.I64(s, 28);
        XMin = BigEndian.I16(s, 36);
        YMin = BigEndian.I16(s, 38);
        XMax = BigEndian.I16(s, 40);
        YMax = BigEndian.I16(s, 42);
        MacStyle = BigEndian.U16(s, 44);
        LowestRecPpem = BigEndian.U16(s, 46);
        FontDirectionHint = BigEndian.I16(s, 48);
        IndexToLocFormat = BigEndian.I16(s, 50);
        GlyphDataFormat = BigEndian.I16(s, 52);
    }

    /// <summary>Gets the major version (1).</summary>
    public int MajorVersion { get; }

    /// <summary>Gets the minor version (0).</summary>
    public int MinorVersion { get; }

    /// <summary>Gets fontRevision as the raw 16.16 fixed value.</summary>
    public int FontRevisionRaw { get; }

    /// <summary>Gets fontRevision as a number.</summary>
    public double FontRevision => FontRevisionRaw / 65536.0;

    /// <summary>Gets checksumAdjustment (not verified).</summary>
    public uint ChecksumAdjustment { get; }

    /// <summary>Gets magicNumber (0x5F0F3CF5 in a valid font; not enforced).</summary>
    public uint MagicNumber { get; }

    /// <summary>Gets the flags bit field.</summary>
    public int Flags { get; }

    /// <summary>Gets unitsPerEm.</summary>
    public int UnitsPerEm { get; }

    /// <summary>Gets created: seconds since 1904-01-01 00:00 UTC.</summary>
    public long CreatedRaw { get; }

    /// <summary>Gets modified: seconds since 1904-01-01 00:00 UTC.</summary>
    public long ModifiedRaw { get; }

    /// <summary>Gets xMin of all glyph bounding boxes.</summary>
    public int XMin { get; }

    /// <summary>Gets yMin of all glyph bounding boxes.</summary>
    public int YMin { get; }

    /// <summary>Gets xMax of all glyph bounding boxes.</summary>
    public int XMax { get; }

    /// <summary>Gets yMax of all glyph bounding boxes.</summary>
    public int YMax { get; }

    /// <summary>Gets macStyle (bit 0 bold, bit 1 italic, ...).</summary>
    public int MacStyle { get; }

    /// <summary>Gets lowestRecPPEM.</summary>
    public int LowestRecPpem { get; }

    /// <summary>Gets fontDirectionHint (deprecated, usually 2).</summary>
    public int FontDirectionHint { get; }

    /// <summary>Gets indexToLocFormat: 0 for short 'loca' offsets, 1 for long.</summary>
    public int IndexToLocFormat { get; }

    /// <summary>Gets glyphDataFormat (0).</summary>
    public int GlyphDataFormat { get; }

    /// <summary>Converts a raw 'head' date (seconds since 1904-01-01 UTC) to a <see cref="DateTime"/>, or null when out of range.</summary>
    /// <param name="raw">The raw value, for example <see cref="CreatedRaw"/>.</param>
    /// <returns>The UTC date, or null.</returns>
    public static DateTime? ToDateTime(long raw)
    {
        DateTime epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        double maxSeconds = (DateTime.MaxValue - epoch).TotalSeconds;
        double minSeconds = (DateTime.MinValue - epoch).TotalSeconds;
        return raw > maxSeconds || raw < minSeconds ? null : epoch.AddSeconds(raw);
    }
}
