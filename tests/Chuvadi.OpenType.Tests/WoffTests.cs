using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace Chuvadi.OpenType.Tests;

public class WoffTests
{
    private static readonly string[] KnownTags =
    {
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT",
    };

    private static (string Tag, byte[] Data)[] TablesOf(byte[] sfnt)
    {
        OpenTypeFont f = OpenTypeFont.Load(sfnt);
        return f.Tables.OrderBy(t => t.Offset).Select(t => (t.Tag.ToString(), f.GetTableData(t.Tag).ToArray())).ToArray();
    }

    private static byte[] Zlib(byte[] data)
    {
        using MemoryStream m = new MemoryStream();
        using (ZLibStream z = new ZLibStream(m, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            z.Write(data);
        }

        return m.ToArray();
    }

    /// <summary>Packs an sfnt as WOFF 1.0: entries sorted by tag, data in the font's table order, tables compressed when smaller.</summary>
    private static byte[] ToWoff1(byte[] sfnt, Action<byte[]>? tamper = null)
    {
        (string Tag, byte[] Data)[] tables = TablesOf(sfnt);
        List<byte[]> stored = tables.Select(t => { byte[] z = Zlib(t.Data); return z.Length < t.Data.Length ? z : t.Data; }).ToList();
        int dirEnd = 44 + (20 * tables.Length);
        List<byte> body = new List<byte>();
        int[] offsets = new int[tables.Length];
        for (int i = 0; i < tables.Length; i++)
        {
            offsets[i] = dirEnd + body.Count;
            body.AddRange(stored[i]);
            while (body.Count % 4 != 0)
            {
                body.Add(0);
            }
        }

        byte[] w = new byte[dirEnd + body.Count];
        BinaryPrimitives.WriteUInt32BigEndian(w, 0x774F4646u);
        BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(4), BinaryPrimitives.ReadUInt32BigEndian(sfnt));
        BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(8), (uint)w.Length);
        BinaryPrimitives.WriteUInt16BigEndian(w.AsSpan(12), (ushort)tables.Length);
        long total = 12 + (16L * tables.Length) + tables.Sum(t => (t.Data.Length + 3L) & ~3L);
        BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(16), (uint)total);
        int[] byTag = Enumerable.Range(0, tables.Length).OrderBy(i => tables[i].Tag, StringComparer.Ordinal).ToArray();
        for (int k = 0; k < byTag.Length; k++)
        {
            int i = byTag[k];
            int e = 44 + (20 * k);
            BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(e), Tag.Parse(tables[i].Tag).Value);
            BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(e + 4), (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(e + 8), (uint)stored[i].Length);
            BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(e + 12), (uint)tables[i].Data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(e + 16), 0x12345678u + (uint)k);
        }

        body.ToArray().CopyTo(w, dirEnd);
        tamper?.Invoke(w);
        return w;
    }

    private static IEnumerable<byte> Base128(uint v)
    {
        List<byte> b = new List<byte> { (byte)(v & 0x7F) };
        v >>= 7;
        while (v != 0)
        {
            b.Insert(0, (byte)(0x80 | (v & 0x7F)));
            v >>= 7;
        }

        return b;
    }

    /// <summary>Packs fonts as WOFF2 with null transforms only; two or more fonts make a WOFF2 collection with shared tables.</summary>
    private static byte[] ToWoff2(params byte[][] fonts)
    {
        List<(string Tag, byte[] Data)> unique = new List<(string, byte[])>();
        List<(uint Flavor, List<int> Indexes)> fontEntries = new List<(uint, List<int>)>();
        foreach (byte[] sfnt in fonts)
        {
            List<int> idx = new List<int>();
            foreach ((string tag, byte[] data) in TablesOf(sfnt))
            {
                int found = unique.FindIndex(u => u.Tag == tag && u.Data.AsSpan().SequenceEqual(data));
                if (found < 0)
                {
                    found = unique.Count;
                    unique.Add((tag, data));
                }

                idx.Add(found);
            }

            fontEntries.Add((BinaryPrimitives.ReadUInt32BigEndian(sfnt), idx));
        }

        List<byte> dir = new List<byte>();
        List<byte> stream = new List<byte>();
        foreach ((string tag, byte[] data) in unique)
        {
            int known = Array.IndexOf(KnownTags, tag);
            int transform = tag is "glyf" or "loca" ? 3 : 0;
            if (known >= 0)
            {
                dir.Add((byte)((transform << 6) | known));
            }
            else
            {
                dir.Add((byte)((transform << 6) | 63));
                FontBuilder.P32(dir, Tag.Parse(tag).Value);
            }

            dir.AddRange(Base128((uint)data.Length));
            stream.AddRange(data);
        }

        if (fonts.Length > 1)
        {
            FontBuilder.P32(dir, 0x00020000u);
            dir.Add((byte)fonts.Length);
            foreach ((uint flavor, List<int> idx) in fontEntries)
            {
                dir.Add((byte)idx.Count);
                FontBuilder.P32(dir, flavor);
                foreach (int i in idx)
                {
                    dir.Add((byte)i);
                }
            }
        }

        byte[] compressed = new byte[stream.Count + 1024];
        Assert.True(BrotliEncoder.TryCompress(stream.ToArray(), compressed, out int n));
        byte[] w = new byte[48 + dir.Count + n];
        BinaryPrimitives.WriteUInt32BigEndian(w, 0x774F4632u);
        BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(4), fonts.Length > 1 ? 0x74746366u : BinaryPrimitives.ReadUInt32BigEndian(fonts[0]));
        BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(8), (uint)w.Length);
        BinaryPrimitives.WriteUInt16BigEndian(w.AsSpan(12), (ushort)unique.Count);
        BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(20), (uint)n);
        dir.ToArray().CopyTo(w, 48);
        Array.Copy(compressed, 0, w, 48 + dir.Count, n);
        return w;
    }

    private static byte[] SampleFont(int glyphs, int unitsPerEm, int glyphForA)
    {
        (byte[] glyf, byte[] loca) = FontBuilder.GlyfLoca(Enumerable.Range(0, glyphs).Select(g => ((short, short, short, short)?)((short)g, (short)0, (short)(100 + g), (short)700)).ToArray(), 0);
        return FontBuilder.Basic(glyphs, unitsPerEm: unitsPerEm)
            .Table("glyf", glyf).Table("loca", loca)
            .Table("cmap", FontBuilder.Cmap((3, 1, FontBuilder.CmapFormat4(new Dictionary<int, int> { [0x41] = glyphForA }, false))))
            .Table("name", FontBuilder.Name((3, 1, 0x409, 1, System.Text.Encoding.BigEndianUnicode.GetBytes(new string('W', 300)))))
            .Build();
    }

    [Fact]
    public void Woff1RoundTripsTheFont()
    {
        byte[] sfnt = SampleFont(4, 2048, 3);
        byte[] woff = ToWoff1(sfnt);
        FontCollection c = FontCollection.Load(woff);
        Assert.Equal(FontContainerKind.Woff, c.Kind);
        Assert.False(c.IsCollection);
        OpenTypeFont f = c.GetFont(0);
        Assert.Equal(2048, f.UnitsPerEm);
        Assert.Equal(3, f.CharacterMap.GetGlyph(0x41));
        Assert.Equal(new GlyphBounds(2, 0, 102, 700), f.GetGlyphBounds(2));
        Assert.True(f.FaceProgram.Equals(f.Data));

        // Same table data in the same order; the stored checksums are kept.
        OpenTypeFont original = OpenTypeFont.Load(sfnt);
        Assert.Equal(original.Tables.Select(t => t.Tag), f.Tables.Select(t => t.Tag));
        Assert.Equal(original.Tables.Select(t => t.Offset), f.Tables.Select(t => t.Offset));
        foreach (TableRecord t in original.Tables)
        {
            Assert.True(original.GetTableData(t.Tag).Span.SequenceEqual(f.GetTableData(t.Tag).Span));
        }

        Assert.All(f.Tables.Select((t, k) => t.Checksum - 0x12345678u), d => Assert.InRange(d, 0u, 10u));
    }

    [Fact]
    public void Woff1RejectsDamagedFiles()
    {
        byte[] sfnt = SampleFont(4, 1000, 1);
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(ToWoff1(sfnt, w => w[14] = 1)));                    // reserved
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(ToWoff1(sfnt, w => FontBuilder.W32(w, 16, 12))));    // totalSfntSize
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(ToWoff1(sfnt, w => FontBuilder.W32(w, 44 + 4, (uint)w.Length)))); // past the end
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(ToWoff1(sfnt, w => FontBuilder.W32(w, 44 + 12, 1)))); // comp > orig
        byte[] corrupt = ToWoff1(sfnt);
        int nameEntry = Enumerable.Range(0, BinaryPrimitives.ReadUInt16BigEndian(corrupt.AsSpan(12))).Select(k => 44 + (20 * k)).First(e => BinaryPrimitives.ReadUInt32BigEndian(corrupt.AsSpan(e)) == Tag.Parse("name").Value);
        int nameAt = (int)BinaryPrimitives.ReadUInt32BigEndian(corrupt.AsSpan(nameEntry + 4));
        corrupt[nameAt + 5] ^= 0xFF;
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(corrupt));                                           // bad zlib data
        Assert.False(FontCollection.TryLoad(corrupt, out _));
    }

    [Fact]
    public void Woff2SingleFont()
    {
        byte[] sfnt = SampleFont(5, 1000, 4);
        FontCollection c = FontCollection.Load(ToWoff2(sfnt));
        Assert.Equal(FontContainerKind.Woff2, c.Kind);
        OpenTypeFont f = c.GetFont(0);
        Assert.Equal(4, f.CharacterMap.GetGlyph(0x41));
        Assert.Equal(new GlyphBounds(3, 0, 103, 700), f.GetGlyphBounds(3));
        OpenTypeFont original = OpenTypeFont.Load(sfnt);
        foreach (TableRecord t in original.Tables.Where(t => t.Tag != Tag.Parse("head")))
        {
            Assert.True(original.GetTableData(t.Tag).Span.SequenceEqual(f.GetTableData(t.Tag).Span), t.Tag.ToString());
        }
    }

    [Fact]
    public void Woff2CollectionSharesTablesAndGivesStandaloneFaces()
    {
        byte[] a = SampleFont(3, 1000, 1);
        byte[] b = SampleFont(3, 2048, 2);
        FontCollection c = FontCollection.Load(ToWoff2(a, b));
        Assert.Equal(FontContainerKind.Woff2, c.Kind);
        Assert.True(c.IsCollection);
        Assert.Equal(2, c.Count);
        Assert.Equal(0x00010000u, c.CollectionVersion);
        OpenTypeFont f0 = c.GetFont(0);
        OpenTypeFont f1 = c.GetFont(1);
        Assert.Equal(1000, f0.UnitsPerEm);
        Assert.Equal(2048, f1.UnitsPerEm);
        Assert.Equal(1, f0.CharacterMap.GetGlyph(0x41));
        Assert.Equal(2, f1.CharacterMap.GetGlyph(0x41));
        Assert.True(f0.TryGetTable(Tag.Parse("glyf"), out TableRecord g0));
        Assert.True(f1.TryGetTable(Tag.Parse("glyf"), out TableRecord g1));
        Assert.Equal(g0.Offset, g1.Offset); // identical glyf stored once
        OpenTypeFont standalone = OpenTypeFont.Load(f1.FaceProgram);
        Assert.Equal(2048, standalone.UnitsPerEm);
        Assert.Equal(2, standalone.CharacterMap.GetGlyph(0x41));
    }

    [Fact]
    public void Woff2RejectsDamagedFiles()
    {
        byte[] w = ToWoff2(SampleFont(3, 1000, 1));
        byte[] badStream = (byte[])w.Clone();
        badStream[^3] ^= 0x55;
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(badStream));
        byte[] leadingZero = (byte[])w.Clone();
        leadingZero[48 + 1] = 0x80; // first table's origLength as UIntBase128 starting with 0x80
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(leadingZero));
        Random r = new Random(99);
        for (int i = 0; i < 300; i++)
        {
            byte[] d = (byte[])w.Clone();
            d[r.Next(4, d.Length)] = (byte)r.Next(256);
            try
            {
                _ = OpenTypeFont.Load(d);
            }
            catch (OpenTypeFormatException)
            {
            }
        }
    }
}
