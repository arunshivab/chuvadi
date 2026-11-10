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

    private FontCollection(ReadOnlyMemory<byte> data, FontContainerKind kind, int[] faceOffsets, uint version, bool isCollection)
    {
        _data = data;
        Kind = kind;
        _faceOffsets = faceOffsets;
        CollectionVersion = version;
        IsCollection = isCollection;
    }

    /// <summary>Gets the container format the data came in: plain sfnt, 'ttcf' collection, WOFF or WOFF2.</summary>
    public FontContainerKind Kind { get; }

    /// <summary>Gets whether the (decoded) data is a 'ttcf' collection: true for <see cref="FontContainerKind.Collection"/> and for a WOFF2 collection.</summary>
    public bool IsCollection { get; }

    /// <summary>Gets the font data the faces read from: the bytes given, or for WOFF and WOFF2 the decoded sfnt or collection.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>Gets the number of fonts in the data (1 for a plain sfnt).</summary>
    public int Count => _faceOffsets.Length;

    /// <summary>Gets the 'ttcf' header version (0x00010000 or 0x00020000), or 0 for a plain sfnt.</summary>
    public uint CollectionVersion { get; }

    /// <summary>Reads the container header of font data.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <returns>The collection.</returns>
    /// <exception cref="OpenTypeFormatException">The data is not an sfnt font or a font collection.</exception>
    public static FontCollection Load(ReadOnlyMemory<byte> data) => Load(data, null);

    /// <summary>Reads the container header of font data, with load options.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <param name="options">The options; null for <see cref="FontLoadOptions.Default"/>.</param>
    /// <returns>The collection.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="FontLoadOptions.MaxDecodedBytes"/> is not greater than 0.</exception>
    /// <exception cref="OpenTypeFormatException">The data is not a font or font collection, or a WOFF/WOFF2 file declares more than the limit.</exception>
    public static FontCollection Load(ReadOnlyMemory<byte> data, FontLoadOptions? options)
    {
        if (!TryLoad(data, (options ?? FontLoadOptions.Default).Validate(), out FontCollection? result, out string? error))
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
        TryLoad(data, FontLoadOptions.Default.MaxDecodedBytes, out result, out _);

    /// <summary>Tries to read the container header of font data, with load options.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <param name="options">The options; null for <see cref="FontLoadOptions.Default"/>.</param>
    /// <param name="result">The collection, or null on failure.</param>
    /// <returns>True when the data is a usable font container.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="FontLoadOptions.MaxDecodedBytes"/> is not greater than 0.</exception>
    public static bool TryLoad(ReadOnlyMemory<byte> data, FontLoadOptions? options, out FontCollection? result) =>
        TryLoad(data, (options ?? FontLoadOptions.Default).Validate(), out result, out _);

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

        return OpenTypeFont.Create(_data, faceIndex, _faceOffsets[faceIndex], IsCollection);
    }

    internal static bool TryLoad(ReadOnlyMemory<byte> data, long maxDecodedBytes, out FontCollection? result, out string? error)
    {
        result = null;
        ReadOnlySpan<byte> s = data.Span;
        if (s.Length < 12)
        {
            error = "The data is too short to be a font.";
            return false;
        }

        // TRACE R-OT-0036
        uint signature = BigEndian.U32(s, 0);
        FontContainerKind kind = FontContainerKind.Sfnt;
        if (signature == 0x774F4646u || signature == 0x774F4632u)
        {
            try
            {
                data = signature == 0x774F4646u ? Woff.Woff1Decoder.Decode(s, maxDecodedBytes) : Woff.Woff2Decoder.Decode(s, maxDecodedBytes);
            }
            catch (OpenTypeFormatException e)
            {
                error = e.Message;
                return false;
            }

            kind = signature == 0x774F4646u ? FontContainerKind.Woff : FontContainerKind.Woff2;
            s = data.Span;
            signature = BigEndian.U32(s, 0);
        }

        return TryReadContainer(data, s, signature, kind, out result, out error);
    }

    private static bool TryReadContainer(ReadOnlyMemory<byte> data, ReadOnlySpan<byte> s, uint signature, FontContainerKind kind, out FontCollection? result, out string? error)
    {
        result = null;

        // TRACE R-OT-0003
        switch (signature)
        {
            case 0x00010000u:
            case 0x4F54544Fu: // 'OTTO'
            case 0x74727565u: // 'true'
                result = new FontCollection(data, kind, new[] { 0 }, 0, false);
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

                    result = new FontCollection(data, kind == FontContainerKind.Sfnt ? FontContainerKind.Collection : kind, offsets, version, true);
                    error = null;
                    return true;
                }

            default:
                error = "The data does not start with a known sfnt, collection, WOFF or WOFF2 signature.";
                return false;
        }
    }
}
