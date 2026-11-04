using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>
/// Font data that may hold one font (a plain sfnt) or several (a 'ttcf' collection).
/// Use <see cref="OpenTypeFont.Load(ReadOnlyMemory{byte}, int)"/> directly when the face index is known.
/// </summary>
public sealed class FontCollection
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int[] _faceOffsets;

    private FontCollection(ReadOnlyMemory<byte> data, FontContainerKind kind, int[] faceOffsets, uint version)
    {
        _data = data;
        Kind = kind;
        _faceOffsets = faceOffsets;
        CollectionVersion = version;
    }

    /// <summary>Gets the container kind.</summary>
    public FontContainerKind Kind { get; }

    /// <summary>Gets the number of fonts in the data (1 for a plain sfnt).</summary>
    public int Count => _faceOffsets.Length;

    /// <summary>Gets the 'ttcf' header version (0x00010000 or 0x00020000), or 0 for a plain sfnt.</summary>
    public uint CollectionVersion { get; }

    /// <summary>Reads the container header of font data.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <returns>The collection.</returns>
    /// <exception cref="OpenTypeFormatException">The data is not an sfnt font or a font collection.</exception>
    public static FontCollection Load(ReadOnlyMemory<byte> data)
    {
        if (!TryLoad(data, out FontCollection? result, out string? error))
        {
            throw new OpenTypeFormatException(error!);
        }

        return result!;
    }

    /// <summary>Tries to read the container header of font data.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <param name="result">The collection, or null on failure.</param>
    /// <returns>True when the data is an sfnt font or a font collection.</returns>
    public static bool TryLoad(ReadOnlyMemory<byte> data, out FontCollection? result) =>
        TryLoad(data, out result, out _);

    /// <summary>Loads one font of the collection.</summary>
    /// <param name="faceIndex">Zero-based index of the font.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is outside 0..Count-1.</exception>
    /// <exception cref="OpenTypeFormatException">The font's table directory or required tables are unusable.</exception>
    public OpenTypeFont GetFont(int faceIndex)
    {
        if ((uint)faceIndex >= (uint)_faceOffsets.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(faceIndex), faceIndex, "The face index is outside the collection.");
        }

        return OpenTypeFont.Create(_data, faceIndex, _faceOffsets[faceIndex], Kind == FontContainerKind.Collection);
    }

    internal static bool TryLoad(ReadOnlyMemory<byte> data, out FontCollection? result, out string? error)
    {
        result = null;
        ReadOnlySpan<byte> s = data.Span;
        if (s.Length < 12)
        {
            error = "The data is too short to be a font.";
            return false;
        }

        // TRACE R-OT-0003
        uint signature = BigEndian.U32(s, 0);
        switch (signature)
        {
            case 0x00010000u:
            case 0x4F54544Fu: // 'OTTO'
            case 0x74727565u: // 'true'
                result = new FontCollection(data, FontContainerKind.Sfnt, new[] { 0 }, 0);
                error = null;
                return true;

            case 0x74746366u: // 'ttcf'
                {
                    // TRACE R-OT-0005
                    uint version = BigEndian.U32(s, 4);
                    uint numFonts = BigEndian.U32(s, 8);
                    if (numFonts == 0 || numFonts > (uint)((s.Length - 12) / 4))
                    {
                        error = "The font collection header has an invalid font count.";
                        return false;
                    }

                    int[] offsets = new int[numFonts];
                    for (int i = 0; i < offsets.Length; i++)
                    {
                        int off = BigEndian.Offset32(s, 12 + (4 * i));
                        if (off < 0 || off > s.Length - 12)
                        {
                            error = "A font offset in the collection header is outside the data.";
                            return false;
                        }

                        offsets[i] = off;
                    }

                    result = new FontCollection(data, FontContainerKind.Collection, offsets, version);
                    error = null;
                    return true;
                }

            case 0x774F4646u: // 'wOFF'
            case 0x774F4632u: // 'wOF2'
                error = "WOFF and WOFF2 data are not read by this version of Chuvadi.OpenType; decompress to sfnt first.";
                return false;

            default:
                error = "The data does not start with a known sfnt or collection signature.";
                return false;
        }
    }
}
