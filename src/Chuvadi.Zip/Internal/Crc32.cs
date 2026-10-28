using System;
using System.Buffers.Binary;

namespace Chuvadi.Zip.Internal;

/// <summary>CRC-32 (IEEE 802.3, as used by zip) using slicing-by-8 tables. Pure managed code.</summary>
internal static class Crc32
{
    private static readonly uint[] Tables = BuildTables();

    private static uint[] BuildTables()
    {
        var t = new uint[8 * 256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        for (int n = 0; n < 256; n++)
        {
            uint c = t[n];
            for (int k = 1; k < 8; k++)
            {
                c = t[c & 0xFF] ^ (c >> 8);
                t[k * 256 + n] = c;
            }
        }
        return t;
    }

    /// <summary>Single-byte raw table step (no pre/post inversion). Used by ZipCrypto key updates.</summary>
    public static uint UpdateRaw(uint crc, byte b) => Tables[(crc ^ b) & 0xFF] ^ (crc >> 8);

    /// <summary>Continues a CRC-32 over <paramref name="data"/>. Start with 0.</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = ~crc;
        var t = Tables;
        int i = 0;
        while (data.Length - i >= 8)
        {
            uint a = c ^ BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i));
            uint b = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i + 4));
            c = t[7 * 256 + (a & 0xFF)] ^ t[6 * 256 + ((a >> 8) & 0xFF)]
              ^ t[5 * 256 + ((a >> 16) & 0xFF)] ^ t[4 * 256 + (a >> 24)]
              ^ t[3 * 256 + (b & 0xFF)] ^ t[2 * 256 + ((b >> 8) & 0xFF)]
              ^ t[1 * 256 + ((b >> 16) & 0xFF)] ^ t[b >> 24];
            i += 8;
        }
        for (; i < data.Length; i++)
            c = t[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return ~c;
    }
}
