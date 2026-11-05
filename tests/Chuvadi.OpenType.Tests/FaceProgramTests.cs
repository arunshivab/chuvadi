using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Chuvadi.OpenType.Tests;

public class FaceProgramTests
{
    private static uint Sum(ReadOnlySpan<byte> s)
    {
        uint sum = 0;
        for (int i = 0; i + 3 < s.Length; i += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(s.Slice(i)));
        }

        return sum;
    }

    [Fact]
    public void PlainFontReturnsTheLoadedBytes()
    {
        byte[] data = FontBuilder.Basic(3).Build();
        OpenTypeFont f = OpenTypeFont.Load(data);
        Assert.True(f.FaceProgram.Equals(f.Data));
    }

    [Fact]
    public void CollectionFaceBecomesAStandaloneFontWithoutDsig()
    {
        FontBuilder first = FontBuilder.Basic(2, unitsPerEm: 1000);
        FontBuilder second = FontBuilder.Basic(5, unitsPerEm: 2048)
            .Table("cmap", FontBuilder.Cmap((3, 1, FontBuilder.CmapFormat4(new Dictionary<int, int> { [0x41] = 3 }, false))))
            .Table("DSIG", new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 })
            .Table("zzzz", new byte[] { 1, 2, 3 });
        byte[] collection = FontBuilder.Collection(first, second);
        OpenTypeFont face = OpenTypeFont.Load(collection, 1);

        ReadOnlyMemory<byte> program = face.FaceProgram;
        Assert.Same(program.ToArray().GetType(), typeof(byte[]));
        Assert.True(face.FaceProgram.Equals(program)); // built once, kept

        OpenTypeFont standalone = OpenTypeFont.Load(program);
        Assert.Equal(FontContainerKind.Sfnt, FontCollection.Load(program).Kind);
        Assert.Equal(2048, standalone.UnitsPerEm);
        Assert.Equal(5, standalone.GlyphCount);
        Assert.Equal(3, standalone.CharacterMap.GetGlyph(0x41));
        Assert.False(standalone.HasTable(Tag.Parse("DSIG")));
        Assert.True(standalone.FaceProgram.Equals(standalone.Data));

        // Same tables, byte for byte, except head's checkSumAdjustment.
        foreach (TableRecord t in face.Tables.Where(t => t.Tag != Tag.Parse("DSIG") && t.Tag != Tag.Parse("head")))
        {
            Assert.True(face.GetTableData(t.Tag).Span.SequenceEqual(standalone.GetTableData(t.Tag).Span), t.Tag.ToString());
        }

        // Directory: sorted tags, search fields, table checksums, whole-font checksum.
        ReadOnlySpan<byte> p = program.Span;
        int n = BinaryPrimitives.ReadUInt16BigEndian(p.Slice(4));
        Assert.Equal(face.Tables.Count - 1, n);
        Assert.Equal(64, BinaryPrimitives.ReadUInt16BigEndian(p.Slice(6))); // 6 tables: searchRange 4 x 16
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(p.Slice(8)));
        Assert.Equal((n * 16) - 64, BinaryPrimitives.ReadUInt16BigEndian(p.Slice(10)));
        uint previous = 0;
        for (int i = 0; i < n; i++)
        {
            int r = 12 + (16 * i);
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(p.Slice(r));
            Assert.True(tag > previous);
            previous = tag;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(p.Slice(r + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(p.Slice(r + 12));
            Assert.Equal(0, offset % 4);
            byte[] padded = new byte[(length + 3) & ~3];
            p.Slice(offset, length).CopyTo(padded);
            if (tag == 0x68656164u)
            {
                BinaryPrimitives.WriteUInt32BigEndian(padded.AsSpan(8), 0);
            }

            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(p.Slice(r + 4)), Sum(padded));
        }

        Assert.Equal(0xB1B0AFBAu, Sum(p));
    }
}
