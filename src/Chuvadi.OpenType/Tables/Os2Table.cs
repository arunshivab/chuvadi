using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>
/// The 'OS/2' (OS/2 and Windows metrics) table, versions 0 to 5. Fields that the
/// table's version or length does not include are null.
/// </summary>
public sealed class Os2Table
{
    private readonly byte[] _panose;

    // TRACE R-OT-0011
    internal Os2Table(ReadOnlySpan<byte> s)
    {
        Version = BigEndian.U16(s, 0);
        XAvgCharWidth = BigEndian.I16(s, 2);
        UsWeightClass = BigEndian.U16(s, 4);
        UsWidthClass = BigEndian.U16(s, 6);
        FsType = BigEndian.U16(s, 8);
        YSubscriptXSize = BigEndian.I16(s, 10);
        YSubscriptYSize = BigEndian.I16(s, 12);
        YSubscriptXOffset = BigEndian.I16(s, 14);
        YSubscriptYOffset = BigEndian.I16(s, 16);
        YSuperscriptXSize = BigEndian.I16(s, 18);
        YSuperscriptYSize = BigEndian.I16(s, 20);
        YSuperscriptXOffset = BigEndian.I16(s, 22);
        YSuperscriptYOffset = BigEndian.I16(s, 24);
        YStrikeoutSize = BigEndian.I16(s, 26);
        YStrikeoutPosition = BigEndian.I16(s, 28);
        SFamilyClass = BigEndian.I16(s, 30);
        _panose = s.Slice(32, 10).ToArray();
        UlUnicodeRange1 = BigEndian.U32(s, 42);
        UlUnicodeRange2 = BigEndian.U32(s, 46);
        UlUnicodeRange3 = BigEndian.U32(s, 50);
        UlUnicodeRange4 = BigEndian.U32(s, 54);
        AchVendId = Tag.Read(s, 58);
        FsSelection = BigEndian.U16(s, 62);
        UsFirstCharIndex = BigEndian.U16(s, 64);
        UsLastCharIndex = BigEndian.U16(s, 66);

        // Some old fonts end the version 0 table after usLastCharIndex (68 bytes).
        if (s.Length >= 78)
        {
            STypoAscender = BigEndian.I16(s, 68);
            STypoDescender = BigEndian.I16(s, 70);
            STypoLineGap = BigEndian.I16(s, 72);
            UsWinAscent = BigEndian.U16(s, 74);
            UsWinDescent = BigEndian.U16(s, 76);
        }

        if (Version >= 1 && s.Length >= 86)
        {
            UlCodePageRange1 = BigEndian.U32(s, 78);
            UlCodePageRange2 = BigEndian.U32(s, 82);
        }

        if (Version >= 2 && s.Length >= 96)
        {
            SxHeight = BigEndian.I16(s, 86);
            SCapHeight = BigEndian.I16(s, 88);
            UsDefaultChar = BigEndian.U16(s, 90);
            UsBreakChar = BigEndian.U16(s, 92);
            UsMaxContext = BigEndian.U16(s, 94);
        }

        if (Version >= 5 && s.Length >= 100)
        {
            UsLowerOpticalPointSize = BigEndian.U16(s, 96);
            UsUpperOpticalPointSize = BigEndian.U16(s, 98);
        }
    }

    /// <summary>Gets the table version (0 to 5).</summary>
    public int Version { get; }

    /// <summary>Gets xAvgCharWidth.</summary>
    public int XAvgCharWidth { get; }

    /// <summary>Gets usWeightClass.</summary>
    public int UsWeightClass { get; }

    /// <summary>Gets usWidthClass.</summary>
    public int UsWidthClass { get; }

    /// <summary>Gets fsType (embedding permissions).</summary>
    public int FsType { get; }

    /// <summary>Gets ySubscriptXSize.</summary>
    public int YSubscriptXSize { get; }

    /// <summary>Gets ySubscriptYSize.</summary>
    public int YSubscriptYSize { get; }

    /// <summary>Gets ySubscriptXOffset.</summary>
    public int YSubscriptXOffset { get; }

    /// <summary>Gets ySubscriptYOffset.</summary>
    public int YSubscriptYOffset { get; }

    /// <summary>Gets ySuperscriptXSize.</summary>
    public int YSuperscriptXSize { get; }

    /// <summary>Gets ySuperscriptYSize.</summary>
    public int YSuperscriptYSize { get; }

    /// <summary>Gets ySuperscriptXOffset.</summary>
    public int YSuperscriptXOffset { get; }

    /// <summary>Gets ySuperscriptYOffset.</summary>
    public int YSuperscriptYOffset { get; }

    /// <summary>Gets yStrikeoutSize.</summary>
    public int YStrikeoutSize { get; }

    /// <summary>Gets yStrikeoutPosition.</summary>
    public int YStrikeoutPosition { get; }

    /// <summary>Gets sFamilyClass.</summary>
    public int SFamilyClass { get; }

    /// <summary>Gets the ten PANOSE classification bytes.</summary>
    public ReadOnlySpan<byte> Panose => _panose;

    /// <summary>Gets ulUnicodeRange1 (bits 0–31).</summary>
    public uint UlUnicodeRange1 { get; }

    /// <summary>Gets ulUnicodeRange2 (bits 32–63).</summary>
    public uint UlUnicodeRange2 { get; }

    /// <summary>Gets ulUnicodeRange3 (bits 64–95).</summary>
    public uint UlUnicodeRange3 { get; }

    /// <summary>Gets ulUnicodeRange4 (bits 96–127).</summary>
    public uint UlUnicodeRange4 { get; }

    /// <summary>Gets achVendID.</summary>
    public Tag AchVendId { get; }

    /// <summary>Gets fsSelection.</summary>
    public int FsSelection { get; }

    /// <summary>Gets usFirstCharIndex.</summary>
    public int UsFirstCharIndex { get; }

    /// <summary>Gets usLastCharIndex.</summary>
    public int UsLastCharIndex { get; }

    /// <summary>Gets sTypoAscender, or null in a short version 0 table.</summary>
    public int? STypoAscender { get; }

    /// <summary>Gets sTypoDescender, or null in a short version 0 table.</summary>
    public int? STypoDescender { get; }

    /// <summary>Gets sTypoLineGap, or null in a short version 0 table.</summary>
    public int? STypoLineGap { get; }

    /// <summary>Gets usWinAscent, or null in a short version 0 table.</summary>
    public int? UsWinAscent { get; }

    /// <summary>Gets usWinDescent, or null in a short version 0 table.</summary>
    public int? UsWinDescent { get; }

    /// <summary>Gets ulCodePageRange1 (version 1 and later).</summary>
    public uint? UlCodePageRange1 { get; }

    /// <summary>Gets ulCodePageRange2 (version 1 and later).</summary>
    public uint? UlCodePageRange2 { get; }

    /// <summary>Gets sxHeight (version 2 and later).</summary>
    public int? SxHeight { get; }

    /// <summary>Gets sCapHeight (version 2 and later).</summary>
    public int? SCapHeight { get; }

    /// <summary>Gets usDefaultChar (version 2 and later).</summary>
    public int? UsDefaultChar { get; }

    /// <summary>Gets usBreakChar (version 2 and later).</summary>
    public int? UsBreakChar { get; }

    /// <summary>Gets usMaxContext (version 2 and later).</summary>
    public int? UsMaxContext { get; }

    /// <summary>Gets usLowerOpticalPointSize (version 5).</summary>
    public int? UsLowerOpticalPointSize { get; }

    /// <summary>Gets usUpperOpticalPointSize (version 5).</summary>
    public int? UsUpperOpticalPointSize { get; }
}
