using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Fonts.ManualTests
{
    /// <summary>Reads the few table fields the checks need (name IDs, OS/2 weight, width, italic and fsType) from a TrueType file.</summary>
    internal sealed class SfntInfo
    {
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();

        public uint Version { get; private set; }

        public int Weight { get; private set; }

        public int WidthClass { get; private set; }

        public bool Italic { get; private set; }

        public int FsType { get; private set; }

        public string? Name(int id) => _names.TryGetValue(id, out string? s) ? s : null;

        public static SfntInfo Read(byte[] font)
        {
            ReadOnlySpan<byte> b = font;
            SfntInfo info = new SfntInfo { Version = BinaryPrimitives.ReadUInt32BigEndian(b) };
            int tables = BinaryPrimitives.ReadUInt16BigEndian(b[4..]);
            for (int i = 0; i < tables; i++)
            {
                ReadOnlySpan<byte> rec = b.Slice(12 + (16 * i), 16);
                string tag = Encoding.ASCII.GetString(rec[..4]);
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(rec[8..]);
                if (tag == "OS/2")
                {
                    info.Weight = BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 4)..]);
                    info.WidthClass = BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 6)..]);
                    info.FsType = BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 8)..]);
                    info.Italic = (BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 62)..]) & 1) != 0;
                }
                else if (tag == "name")
                {
                    int count = BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 2)..]);
                    int storage = offset + BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 4)..]);
                    for (int n = 0; n < count; n++)
                    {
                        ReadOnlySpan<byte> r = b.Slice(offset + 6 + (12 * n), 12);
                        int platform = BinaryPrimitives.ReadUInt16BigEndian(r);
                        int encoding = BinaryPrimitives.ReadUInt16BigEndian(r[2..]);
                        int language = BinaryPrimitives.ReadUInt16BigEndian(r[4..]);
                        int id = BinaryPrimitives.ReadUInt16BigEndian(r[6..]);
                        int length = BinaryPrimitives.ReadUInt16BigEndian(r[8..]);
                        int start = BinaryPrimitives.ReadUInt16BigEndian(r[10..]);
                        if (platform == 3 && encoding == 1 && language == 0x409)
                        {
                            info._names[id] = Encoding.BigEndianUnicode.GetString(font, storage + start, length);
                        }
                    }
                }
            }
            return info;
        }
    }
}
