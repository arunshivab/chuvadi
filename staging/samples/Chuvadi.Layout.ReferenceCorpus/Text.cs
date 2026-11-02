using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Chuvadi.Layout.ReferenceCorpus
{
    /// <summary>Deterministic sample text and images, so the corpus is the same on every run.</summary>
    internal static class Sample
    {
        private static readonly string[] Words =
        {
            "patient", "report", "ward", "admission", "the", "of", "and", "radiation", "dose", "planning", "schedule",
            "review", "clinical", "summary", "treatment", "for", "with", "daily", "follow-up", "imaging", "results",
            "is", "was", "a", "to", "in", "on", "department", "quality", "assurance", "machine", "beam", "calibration",
            "nursing", "pharmacy", "invoice", "insurance", "approval", "discharge", "consultant", "theatre", "laboratory",
        };

        public const string Tamil1 = "\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD \u0B89\u0BB2\u0B95\u0BBF\u0BA9\u0BCD \u0BAE\u0BBF\u0B95\u0BAA\u0BCD \u0BAA\u0BB4\u0BAE\u0BC8\u0BAF\u0BBE\u0BA9 \u0BAE\u0BCA\u0BB4\u0BBF\u0B95\u0BB3\u0BBF\u0BB2\u0BCD \u0B92\u0BA9\u0BCD\u0BB1\u0BC1.";
        public const string Tamil2 = "\u0BA8\u0BCB\u0BAF\u0BBE\u0BB3\u0BBF\u0BAF\u0BBF\u0BA9\u0BCD \u0B89\u0B9F\u0BB2\u0BCD\u0BA8\u0BBF\u0BB2\u0BC8 \u0B9A\u0BC0\u0BB0\u0BBE\u0B95 \u0B89\u0BB3\u0BCD\u0BB3\u0BA4\u0BC1; \u0BAE\u0BB0\u0BC1\u0BA4\u0BCD\u0BA4\u0BC1\u0BB5\u0BB0\u0BCD \u0BA8\u0BBE\u0BB3\u0BC8 \u0BAE\u0BC0\u0BA3\u0BCD\u0B9F\u0BC1\u0BAE\u0BCD \u0BAA\u0BBE\u0BB0\u0BCD\u0BB5\u0BC8\u0BAF\u0BBF\u0B9F\u0BC1\u0BB5\u0BBE\u0BB0\u0BCD.";
        public const string Tamil3 = "\u0B95\u0BA4\u0BBF\u0BB0\u0BCD\u0BB5\u0BC0\u0B9A\u0BCD\u0B9A\u0BC1 \u0B9A\u0BBF\u0B95\u0BBF\u0B9A\u0BCD\u0B9A\u0BC8 \u0BA4\u0BBF\u0B9F\u0BCD\u0B9F\u0BAE\u0BBF\u0B9F\u0BB2\u0BCD \u0BAE\u0BC1\u0B9F\u0BBF\u0BA8\u0BCD\u0BA4\u0BA4\u0BC1. \u0B85\u0B9F\u0BC1\u0BA4\u0BCD\u0BA4 \u0B85\u0BAE\u0BB0\u0BCD\u0BB5\u0BC1 \u0BA4\u0BBF\u0B99\u0BCD\u0B95\u0B9F\u0BCD\u0B95\u0BBF\u0BB4\u0BAE\u0BC8 \u0B95\u0BBE\u0BB2\u0BC8 \u0BAA\u0BA4\u0BCD\u0BA4\u0BC1 \u0BAE\u0BA3\u0BBF\u0B95\u0BCD\u0B95\u0BC1.";

        /// <summary>A sentence of <paramref name="words"/> words; the same seed gives the same text.</summary>
        public static string Sentence(int seed, int words)
        {
            uint s = (uint)(seed * 2654435761u + 12345);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < words; i++)
            {
                s = (s * 1103515245u) + 12345u;
                string w = Words[(int)((s >> 8) % (uint)Words.Length)];
                if (i == 0)
                {
                    w = char.ToUpperInvariant(w[0]) + w.Substring(1);
                }
                else
                {
                    sb.Append(' ');
                }
                sb.Append(w);
            }
            sb.Append('.');
            return sb.ToString();
        }

        /// <summary>A paragraph of <paramref name="sentences"/> sentences.</summary>
        public static string Paragraph(int seed, int sentences = 5)
        {
            List<string> list = new List<string>();
            for (int i = 0; i < sentences; i++)
            {
                list.Add(Sentence((seed * 31) + i, 8 + ((seed + i) % 9)));
            }
            return string.Join(" ", list);
        }

        /// <summary>DejaVu Serif (Fonts/DejaVuSerif.ttf, DejaVu licence in Fonts/DejaVu-LICENSE.txt), for the embedded-font case.</summary>
        public static byte[] EmbeddedFont()
        {
            using Stream s = typeof(Sample).Assembly.GetManifestResourceStream("Chuvadi.Layout.ReferenceCorpus.Fonts.DejaVuSerif.ttf")
                ?? throw new InvalidOperationException("The DejaVuSerif.ttf resource is missing.");
            using MemoryStream ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>An RGB PNG of <paramref name="w"/> x <paramref name="h"/> pixels: a colour field with a diagonal band.</summary>
        public static byte[] Png(int w, int h, byte r, byte g, byte b)
        {
            using MemoryStream raw = new MemoryStream();
            for (int y = 0; y < h; y++)
            {
                raw.WriteByte(0);
                for (int x = 0; x < w; x++)
                {
                    bool band = Math.Abs((x * h / Math.Max(1, w)) - y) < Math.Max(2, h / 10);
                    raw.WriteByte(band ? (byte)255 : r);
                    raw.WriteByte(band ? (byte)255 : g);
                    raw.WriteByte(band ? (byte)255 : b);
                }
            }
            using MemoryStream z = new MemoryStream();
            using (ZLibStream zs = new ZLibStream(z, CompressionLevel.Optimal, true))
            {
                raw.Position = 0;
                raw.CopyTo(zs);
            }
            byte[] ihdr = new byte[13];
            BigEndian(ihdr, 0, (uint)w);
            BigEndian(ihdr, 4, (uint)h);
            ihdr[8] = 8;
            ihdr[9] = 2;
            using MemoryStream png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            Chunk(png, "IHDR", ihdr);
            Chunk(png, "IDAT", z.ToArray());
            Chunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            BigEndian(len, 0, (uint)data.Length);
            s.Write(len);
            byte[] td = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type).CopyTo(td, 0);
            data.CopyTo(td, 4);
            s.Write(td);
            byte[] crc = new byte[4];
            BigEndian(crc, 0, Crc32(td));
            s.Write(crc);
        }

        private static void BigEndian(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24);
            b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8);
            b[at + 3] = (byte)v;
        }

        private static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte x in data)
            {
                c ^= x;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
                }
            }
            return ~c;
        }
    }
}
