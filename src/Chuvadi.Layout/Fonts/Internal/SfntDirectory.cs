using System;
using System.Collections.Generic;

namespace Chuvadi.Layout.Fonts.Internal
{
    /// <summary>The table directory of one face in an sfnt file (TrueType, OpenType/CFF or a TrueType collection).</summary>
    internal sealed class SfntDirectory
    {
        private const uint TagTrueType = 0x00010000;
        private const uint TagTrue = 0x74727565; // 'true'
        private const uint TagOtto = 0x4F54544F; // 'OTTO'
        private const uint TagTtcf = 0x74746366; // 'ttcf'
        private const uint TagWoff = 0x774F4646; // 'wOFF'
        private const uint TagWoff2 = 0x774F4632; // 'wOF2'
        private const int MaxTables = 512;
        private const int MaxFacesInCollection = 4096;

        private readonly Dictionary<uint, (int Offset, int Length)> _tables;

        private SfntDirectory(Dictionary<uint, (int Offset, int Length)> tables, FontOutlineFormat format)
        {
            _tables = tables;
            OutlineFormat = format;
        }

        public FontOutlineFormat OutlineFormat { get; }

        public IEnumerable<uint> Tags => _tables.Keys;

        /// <summary>Number of faces in the file: 1 for a plain sfnt, n for a collection.</summary>
        public static int CountFaces(ReadOnlyMemory<byte> data)
        {
            BigEndian r = new BigEndian(data);
            if (r.Length < 12)
            {
                throw new FontFormatException("Font data is too short to be an sfnt file.");
            }
            uint tag = r.U32(0);
            if (tag == TagTtcf)
            {
                uint n = r.U32(8);
                if (n == 0 || n > MaxFacesInCollection)
                {
                    throw new FontFormatException($"TrueType collection declares {n} faces.");
                }
                return (int)n;
            }
            CheckSignature(tag);
            return 1;
        }

        public static SfntDirectory Read(ReadOnlyMemory<byte> data, int faceIndex)
        {
            BigEndian r = new BigEndian(data);
            if (r.Length < 12)
            {
                throw new FontFormatException("Font data is too short to be an sfnt file.");
            }
            long dirOffset = 0;
            uint tag = r.U32(0);
            if (tag == TagTtcf)
            {
                int n = CountFaces(data);
                if (faceIndex < 0 || faceIndex >= n)
                {
                    throw new FontFormatException($"Face index {faceIndex} is out of range; the collection has {n} face(s).");
                }
                dirOffset = r.U32(12 + (4L * faceIndex));
                tag = r.U32(dirOffset);
            }
            else if (faceIndex != 0)
            {
                throw new FontFormatException($"Face index {faceIndex} requested from a single-face font.");
            }

            CheckSignature(tag);
            FontOutlineFormat format = tag == TagOtto ? FontOutlineFormat.Cff : FontOutlineFormat.TrueType;
            int numTables = r.U16(dirOffset + 4);
            if (numTables == 0 || numTables > MaxTables)
            {
                throw new FontFormatException($"Font declares {numTables} tables.");
            }

            Dictionary<uint, (int, int)> tables = new Dictionary<uint, (int, int)>(numTables);
            for (int i = 0; i < numTables; i++)
            {
                long rec = dirOffset + 12 + (16L * i);
                uint t = r.U32(rec);
                uint off = r.U32(rec + 8);
                uint len = r.U32(rec + 12);
                if (!r.Has(off, len))
                {
                    // Lenient like mainstream rasterisers: a table whose declared length overruns
                    // the file by padding is clipped; one that starts outside is dropped.
                    if (off >= (uint)r.Length)
                    {
                        continue;
                    }
                    len = (uint)r.Length - off;
                }
                tables[t] = ((int)off, (int)len);
            }
            return new SfntDirectory(tables, format);
        }

        private static void CheckSignature(uint tag)
        {
            if (tag == TagWoff || tag == TagWoff2)
            {
                throw new FontFormatException("WOFF/WOFF2 font containers are not supported by the font core; supply the sfnt (TTF/OTF) font.");
            }
            if (tag != TagTrueType && tag != TagTrue && tag != TagOtto)
            {
                throw new FontFormatException($"Not an sfnt font (signature 0x{tag:X8}).");
            }
        }

        public bool Has(string tag) => _tables.ContainsKey(BigEndian.MakeTag(tag));

        public BigEndian? TryTable(ReadOnlyMemory<byte> data, string tag)
        {
            if (_tables.TryGetValue(BigEndian.MakeTag(tag), out (int Offset, int Length) e))
            {
                return new BigEndian(data.Slice(e.Offset, e.Length));
            }
            return null;
        }

        public BigEndian Table(ReadOnlyMemory<byte> data, string tag)
            => TryTable(data, tag) ?? throw new FontFormatException($"Required font table '{tag}' is missing.");
    }
}
