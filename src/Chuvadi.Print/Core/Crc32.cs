using System;

namespace Chuvadi.Print
{
    /// <summary>
    /// CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320). Shared by the spool envelope
    /// checksum and the PNG chunk checksums.
    /// </summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            const uint polynomial = 0xEDB88320u;
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? polynomial ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        /// <summary>The running-state seed for <see cref="Append"/>.</summary>
        public const uint Seed = 0xFFFFFFFFu;

        /// <summary>Folds <paramref name="data"/> into a running CRC state.</summary>
        public static uint Append(uint state, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data)
                state = (state >> 8) ^ Table[(state ^ b) & 0xFF];
            return state;
        }

        /// <summary>Converts a running state into the final CRC value.</summary>
        public static uint Finish(uint state) => state ^ 0xFFFFFFFFu;

        /// <summary>CRC-32 of a whole buffer.</summary>
        public static uint Compute(ReadOnlySpan<byte> data) => Finish(Append(Seed, data));
    }
}
