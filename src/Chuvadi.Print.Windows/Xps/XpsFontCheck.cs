using System;
using System.Buffers.Binary;

namespace Chuvadi.Print.Windows
{
    /// <summary>What the XPS writer needs to know about a font file before embedding it.</summary>
    internal readonly struct XpsFontInfo
    {
        public XpsFontInfo(int glyphCount, ushort fsType)
        {
            GlyphCount = glyphCount;
            FsType = fsType;
        }

        /// <summary>From 'maxp'; 0 when the font has no maxp table.</summary>
        public int GlyphCount { get; }

        /// <summary>OS/2 fsType; 0 (installable) when the font has no OS/2 table.</summary>
        public ushort FsType { get; }
    }

    /// <summary>
    /// Reads the sfnt table directory (OpenType specification, "Font file" and the OS/2 and maxp tables) to decide
    /// whether a font can be embedded in an XPS package as TrueType.
    /// </summary>
    internal static class XpsFontCheck
    {
        /// <summary>OS/2 fsType: Restricted License embedding (the low four bits equal 2).</summary>
        public const ushort RestrictedLicense = 0x0002;

        /// <summary>OS/2 fsType bit 9: Bitmap embedding only.</summary>
        public const ushort BitmapOnly = 0x0200;

        public static bool TryRead(byte[] data, out XpsFontInfo info, out string? reason)
        {
            info = default;
            if (data.Length < 12)
            {
                reason = "is not a font file";
                return false;
            }
            uint version = BinaryPrimitives.ReadUInt32BigEndian(data);
            if (version == 0x4F54544F) // 'OTTO'
            {
                reason = "has CFF outlines (only TrueType outlines are embedded; draw it as outlines instead)";
                return false;
            }
            if (version == 0x74746366) // 'ttcf'
            {
                reason = "is a font collection (embed the single font)";
                return false;
            }
            if (version != 0x00010000 && version != 0x74727565) // 'true'
            {
                reason = "is not a TrueType font file";
                return false;
            }
            int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
            if (12L + numTables * 16L > data.Length)
            {
                reason = "has a damaged table directory";
                return false;
            }
            bool glyf = false, loca = false;
            int glyphCount = 0;
            ushort fsType = 0;
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + i * 16;
                uint tag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec));
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8));
                uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 12));
                if (offset + (ulong)length > (ulong)data.Length)
                {
                    reason = "has a table outside the file";
                    return false;
                }
                switch (tag)
                {
                    case 0x676C7966: glyf = true; break;                              // 'glyf'
                    case 0x6C6F6361: loca = true; break;                              // 'loca'
                    case 0x6D617870 when length >= 6:                                 // 'maxp'
                        glyphCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan((int)offset + 4));
                        break;
                    case 0x4F532F32 when length >= 10:                                // 'OS/2'
                        fsType = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan((int)offset + 8));
                        break;
                }
            }
            if (!glyf || !loca)
            {
                reason = "has no TrueType outlines (glyf and loca tables)";
                return false;
            }
            if ((fsType & 0x000F) == RestrictedLicense)
            {
                reason = "does not allow embedding (OS/2 fsType Restricted License)";
                return false;
            }
            if ((fsType & BitmapOnly) != 0)
            {
                reason = "allows bitmap embedding only (OS/2 fsType bit 9)";
                return false;
            }
            info = new XpsFontInfo(glyphCount, fsType);
            reason = null;
            return true;
        }
    }
}
