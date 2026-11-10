using System.Buffers.Binary;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.Tests;

/// <summary>Font helpers for tests: pack faces by file name (LiPi from Chuvadi.Fonts, the rest from Layout.Fonts) and a TrueType-collection builder.</summary>
internal static class TestFonts
{
    public static byte[] Bytes(string file) => file.StartsWith("LiPi-", StringComparison.Ordinal) ? Chuvadi.Fonts.FontPack.ReadAllBytes(file) : SubstituteFontPack.ReadAllBytes(file);

    public static FontFace Face(string file) => FontFace.Load(Bytes(file), 0, FontOrigin.Bundled, file);

    public static FontResolver BundledResolver() => new FontResolver(new IFontSource[] { FontPackSource.For(SubstituteFontPack.Instance), FontPackSource.For(Chuvadi.Fonts.FontPack.Instance) });

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

    /// <summary>
    /// A "subset" of a TrueType font for tests: the same font with a cmap (3,10 format 12) that maps only
    /// <paramref name="keep"/>, as a document's embedded subset font does. Glyphs and other tables are unchanged.
    /// </summary>
    public static byte[] SubsetCmap(byte[] font, IEnumerable<int> keep)
    {
        FontFace full = FontFace.Load(font);
        List<(int Cp, int Gid)> map = keep.Distinct().OrderBy(c => c).Select(c => (c, full.GetGlyphId(c))).Where(m => m.Item2 != 0).ToList();
        byte[] cmap = new byte[4 + 8 + 16 + (12 * map.Count)];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);          // one encoding record
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3);          // Windows
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 10);         // Unicode full repertoire
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);         // subtable offset
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(12), 12);        // format 12
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(16), (uint)(16 + (12 * map.Count)));
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(24), (uint)map.Count);
        for (int g = 0; g < map.Count; g++)
        {
            int o = 28 + (12 * g);
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(o), (uint)map[g].Cp);
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(o + 4), (uint)map[g].Cp);
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(o + 8), (uint)map[g].Gid);
        }
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        List<(uint Tag, byte[] Data)> tables = new List<(uint, byte[])>();
        for (int t = 0; t < numTables; t++)
        {
            int rec = 12 + (16 * t);
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec));
            int off = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 8));
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 12));
            tables.Add((tag, tag == 0x636D6170 ? cmap : font.AsSpan(off, len).ToArray()));
        }
        List<byte> output = new List<byte>(font.AsSpan(0, 12 + (16 * numTables)).ToArray());
        for (int t = 0; t < tables.Count; t++)
        {
            while (output.Count % 4 != 0)
            {
                output.Add(0);
            }
            byte[] rec = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(rec, (uint)output.Count);
            BinaryPrimitives.WriteUInt32BigEndian(rec.AsSpan(4), (uint)tables[t].Data.Length);
            for (int k = 0; k < 8; k++)
            {
                output[12 + (16 * t) + 8 + k] = rec[k];
            }
            output.AddRange(tables[t].Data);
        }
        return output.ToArray();
    }
}
