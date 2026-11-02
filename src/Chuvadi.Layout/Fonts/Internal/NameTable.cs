using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Layout.Fonts.Internal
{
    /// <summary>Decoded strings from the 'name' table. Windows Unicode records win over Macintosh Roman ones; US English wins within a platform.</summary>
    internal static class NameTable
    {
        public static Dictionary<int, string> Read(BigEndian? table)
        {
            Dictionary<int, string> result = new Dictionary<int, string>();
            if (table is not BigEndian t)
            {
                return result;
            }
            int count = t.U16(2);
            int storage = t.U16(4);
            Dictionary<int, int> rank = new Dictionary<int, int>();
            for (int i = 0; i < count; i++)
            {
                long rec = 6 + (12L * i);
                if (!t.Has(rec, 12))
                {
                    break;
                }
                int platform = t.U16(rec);
                int encoding = t.U16(rec + 2);
                int language = t.U16(rec + 4);
                int nameId = t.U16(rec + 6);
                int length = t.U16(rec + 8);
                int offset = t.U16(rec + 10);
                int r;
                if (platform == 3 && (encoding == 1 || encoding == 10 || encoding == 0))
                {
                    r = language == 0x409 ? 4 : 3;
                }
                else if (platform == 0)
                {
                    r = 2;
                }
                else if (platform == 1 && encoding == 0)
                {
                    r = language == 0 ? 1 : 0;
                }
                else
                {
                    continue;
                }
                if (rank.TryGetValue(nameId, out int existing) && existing >= r)
                {
                    continue;
                }
                long start = (long)storage + offset;
                if (!t.Has(start, length))
                {
                    continue;
                }
                ReadOnlySpan<byte> bytes = t.Bytes(start, length);
                string s = platform == 1 ? DecodeMacRoman(bytes) : Encoding.BigEndianUnicode.GetString(bytes);
                s = s.TrimEnd('\0');
                if (s.Length == 0)
                {
                    continue;
                }
                result[nameId] = s;
                rank[nameId] = r;
            }
            return result;
        }

        private static string DecodeMacRoman(ReadOnlySpan<byte> bytes)
        {
            // ASCII is all that is needed for family/style names; other bytes map to U+FFFD.
            StringBuilder sb = new StringBuilder(bytes.Length);
            foreach (byte b in bytes)
            {
                sb.Append(b < 0x80 ? (char)b : '\uFFFD');
            }
            return sb.ToString();
        }
    }
}
