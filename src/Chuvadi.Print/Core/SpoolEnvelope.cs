using System;
using System.IO;
using System.Text;

namespace Chuvadi.Print
{
    /// <summary>
    /// The wire/spool payload: the document's PDF bytes plus its <see cref="PrintSettings"/>,
    /// in a self-describing, checksummed, hand-rolled binary format (no third-party serializer).
    /// This is what a web host hands to a print agent; the agent rasterises and prints.
    /// </summary>
    public sealed class SpoolEnvelope
    {
        private static readonly byte[] MagicBytes = { (byte)'C', (byte)'V', (byte)'P', (byte)'R' };

        /// <summary>Envelope format version.</summary>
        public const byte CurrentVersion = 1;

        public byte[] PdfBytes { get; }
        public PrintSettings Settings { get; }

        public SpoolEnvelope(byte[] pdfBytes, PrintSettings settings)
        {
            PdfBytes = pdfBytes ?? throw new ArgumentNullException(nameof(pdfBytes));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void Serialize(Stream destination)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            byte[] body = BuildBody();
            using var w = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
            w.Write(MagicBytes);
            w.Write(CurrentVersion);
            w.Write((byte)0);            // flags (reserved: bit0 = compressed)
            w.Write(Crc32(body));
            w.Write(body.Length);
            w.Write(body);
        }

        public byte[] ToArray()
        {
            using var ms = new MemoryStream();
            Serialize(ms);
            return ms.ToArray();
        }

        public static SpoolEnvelope Deserialize(Stream source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            using var r = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
            byte[] magic = r.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != MagicBytes[0] || magic[1] != MagicBytes[1]
                || magic[2] != MagicBytes[2] || magic[3] != MagicBytes[3])
                throw new InvalidDataException("Not a Chuvadi spool envelope.");
            byte version = r.ReadByte();
            if (version != CurrentVersion)
                throw new InvalidDataException("Unsupported spool envelope version: " + version + ".");
            _ = r.ReadByte();            // flags (reserved)
            uint expectedCrc = r.ReadUInt32();
            int bodyLength = r.ReadInt32();
            if (bodyLength < 0) throw new InvalidDataException("Corrupt spool envelope (negative length).");
            byte[] body = r.ReadBytes(bodyLength);
            if (body.Length != bodyLength) throw new InvalidDataException("Truncated spool envelope.");
            if (Crc32(body) != expectedCrc) throw new InvalidDataException("Checksum mismatch (corrupt spool).");
            return ParseBody(body);
        }

        public static SpoolEnvelope FromArray(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            using var ms = new MemoryStream(data, writable: false);
            return Deserialize(ms);
        }

        private byte[] BuildBody()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(Settings.Copies);
                w.Write(Settings.Collate);
                w.Write((byte)Settings.Duplex);
                w.Write((byte)Settings.Color);
                w.Write((byte)Settings.Orientation);
                w.Write((byte)Settings.Scale);
                w.Write(Settings.CustomScalePercent);
                w.Write(Settings.Paper.WidthPoints);
                w.Write(Settings.Paper.HeightPoints);
                WriteNullableString(w, Settings.Paper.Name);
                w.Write(Settings.Margins.Left);
                w.Write(Settings.Margins.Top);
                w.Write(Settings.Margins.Right);
                w.Write(Settings.Margins.Bottom);
                w.Write((byte)Settings.Alignment.Horizontal);
                w.Write((byte)Settings.Alignment.Vertical);
                w.Write(Settings.Silent);
                w.Write(Settings.Pages.ToCanonicalString());
                w.Write((long)PdfBytes.LongLength);
                w.Write(PdfBytes);
            }
            return ms.ToArray();
        }

        private static SpoolEnvelope ParseBody(byte[] body)
        {
            using var ms = new MemoryStream(body, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            var settings = new PrintSettings
            {
                Copies = r.ReadInt32(),
                Collate = r.ReadBoolean()
            };
            settings.Duplex = (Duplex)r.ReadByte();
            settings.Color = (ColorMode)r.ReadByte();
            settings.Orientation = (PageOrientation)r.ReadByte();
            settings.Scale = (ScaleMode)r.ReadByte();
            settings.CustomScalePercent = r.ReadDouble();
            double paperWidth = r.ReadDouble();
            double paperHeight = r.ReadDouble();
            string? paperName = ReadNullableString(r);
            settings.Paper = new PaperSize(paperWidth, paperHeight, paperName);
            double ml = r.ReadDouble(), mt = r.ReadDouble(), mr = r.ReadDouble(), mb = r.ReadDouble();
            settings.Margins = new Margins(ml, mt, mr, mb);
            var ha = (HorizontalAlignment)r.ReadByte();
            var va = (VerticalAlignment)r.ReadByte();
            settings.Alignment = new ContentAlignment(ha, va);
            settings.Silent = r.ReadBoolean();
            settings.Pages = PageSelection.Parse(r.ReadString());
            long pdfLength = r.ReadInt64();
            if (pdfLength < 0 || pdfLength > int.MaxValue)
                throw new InvalidDataException("Invalid PDF length in spool envelope.");
            byte[] pdf = r.ReadBytes((int)pdfLength);
            if (pdf.Length != pdfLength) throw new InvalidDataException("Truncated PDF payload.");
            return new SpoolEnvelope(pdf, settings);
        }

        private static void WriteNullableString(BinaryWriter w, string? value)
        {
            w.Write(value is not null);
            if (value is not null) w.Write(value);
        }

        private static string? ReadNullableString(BinaryReader r)
            => r.ReadBoolean() ? r.ReadString() : null;

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
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

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
                crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
