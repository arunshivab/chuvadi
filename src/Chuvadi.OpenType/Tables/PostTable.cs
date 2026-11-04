using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>
/// The 'post' (PostScript) table header. Glyph names are not read in this version.
/// </summary>
public sealed class PostTable
{
    // TRACE R-OT-0012
    internal PostTable(ReadOnlySpan<byte> s)
    {
        VersionRaw = BigEndian.U32(s, 0);
        ItalicAngleRaw = BigEndian.I32(s, 4);
        UnderlinePosition = BigEndian.I16(s, 8);
        UnderlineThickness = BigEndian.I16(s, 10);
        IsFixedPitchRaw = BigEndian.U32(s, 12);
        MinMemType42 = BigEndian.U32(s, 16);
        MaxMemType42 = BigEndian.U32(s, 20);
        MinMemType1 = BigEndian.U32(s, 24);
        MaxMemType1 = BigEndian.U32(s, 28);
    }

    /// <summary>Gets the version as stored: 0x00010000, 0x00020000, 0x00025000 or 0x00030000.</summary>
    public uint VersionRaw { get; }

    /// <summary>Gets italicAngle as the raw 16.16 fixed value.</summary>
    public int ItalicAngleRaw { get; }

    /// <summary>Gets italicAngle in degrees, counter-clockwise from vertical.</summary>
    public double ItalicAngle => ItalicAngleRaw / 65536.0;

    /// <summary>Gets underlinePosition (top of the underline).</summary>
    public int UnderlinePosition { get; }

    /// <summary>Gets underlineThickness.</summary>
    public int UnderlineThickness { get; }

    /// <summary>Gets isFixedPitch as stored.</summary>
    public uint IsFixedPitchRaw { get; }

    /// <summary>Gets whether the font is monospaced (isFixedPitch != 0).</summary>
    public bool IsFixedPitch => IsFixedPitchRaw != 0;

    /// <summary>Gets minMemType42.</summary>
    public uint MinMemType42 { get; }

    /// <summary>Gets maxMemType42.</summary>
    public uint MaxMemType42 { get; }

    /// <summary>Gets minMemType1.</summary>
    public uint MinMemType1 { get; }

    /// <summary>Gets maxMemType1.</summary>
    public uint MaxMemType1 { get; }
}
