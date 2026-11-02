using System.Buffers.Binary;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.Tests;

/// <summary>Font helpers for tests: pack faces by file name (LiPi from Chuvadi.Fonts, the rest from Layout.Fonts) and a TrueType-collection builder.</summary>
internal static class TestFonts
{
    public static byte[] Bytes(string file) => file.StartsWith("LiPi-", StringComparison.Ordinal) ? Chuvadi.Fonts.FontPack.ReadAllBytes(file) : BundledFontSource.ReadResource(file);

    public static FontFace Face(string file) => FontFace.Load(Bytes(file), 0, FontOrigin.Bundled, file);

    public static FontResolver BundledResolver() => new FontResolver(new IFontSource[] { BundledFontSource.Instance, BuiltInFontSource.Instance });

    /// <summary>Builds a .ttc from single-face fonts by copying each font and rebasing its table offsets.</summary>
    public static byte[] BuildCollection(params byte[][] fonts)
    {
        int headerSize = 12 + (4 * fonts.Length);
        List<byte> output = new List<byte>(new byte[headerSize]);
        byte[] header = new byte[headerSize];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), 0x74746366);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), (uint)fonts.Length);
        for (int f = 0; f < fonts.Length; f++)
        {
            while (output.Count % 4 != 0)
            {
                output.Add(0);
            }
            int baseOffset = output.Count;
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12 + (4 * f)), (uint)baseOffset);
            byte[] copy = (byte[])fonts[f].Clone();
            int numTables = BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(4));
            for (int t = 0; t < numTables; t++)
            {
                int rec = 12 + (16 * t) + 8;
                uint off = BinaryPrimitives.ReadUInt32BigEndian(copy.AsSpan(rec));
                BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(rec), off + (uint)baseOffset);
            }
            output.AddRange(copy);
        }
        byte[] result = output.ToArray();
        header.CopyTo(result, 0);
        return result;
    }
}
