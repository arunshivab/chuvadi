using System;
using System.IO;
using System.Text;

namespace Chuvadi.Print
{
    /// <summary>
    /// The wire/spool payload: the document's PDF bytes plus its <see cref="PrintSettings"/> and job
    /// metadata, in a self-describing, checksummed, hand-rolled binary format (no third-party serializer).
    /// This is what a web host hands to a print agent; the agent rasterises and prints.
    /// </summary>
    /// <remarks>
    /// Version 2 (current) adds <see cref="PrintSettings.AutoRotate"/> and the job metadata
    /// (<see cref="JobId"/>, <see cref="DocumentTitle"/>, <see cref="UserName"/>, <see cref="TenantId"/>).
    /// Version 1 envelopes are still read; their metadata is null and AutoRotate is false.
    /// </remarks>
    public sealed class SpoolEnvelope
    {
        private static readonly byte[] MagicBytes = { (byte)'C', (byte)'V', (byte)'P', (byte)'R' };

        /// <summary>Envelope format version written by <see cref="Serialize(Stream)"/>.</summary>
        public const byte CurrentVersion = 2;

        /// <summary>Oldest envelope format version <see cref="Deserialize"/> accepts.</summary>
        public const byte MinimumVersion = 1;

        public byte[] PdfBytes { get; }
        public PrintSettings Settings { get; }

        /// <summary>Identifier of the print job, if known.</summary>
        public Guid? JobId { get; init; }

        /// <summary>Human-readable document title for the audit trail.</summary>
        public string? DocumentTitle { get; init; }

        /// <summary>The user on whose behalf the job is printed.</summary>
        public string? UserName { get; init; }

        /// <summary>The tenant (organisation, site) the job belongs to.</summary>
        public string? TenantId { get; init; }

        public SpoolEnvelope(byte[] pdfBytes, PrintSettings settings)
        {
            PdfBytes = pdfBytes ?? throw new ArgumentNullException(nameof(pdfBytes));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void Serialize(Stream destination) => Serialize(destination, CurrentVersion);

        /// <summary>Writes a specific format version (used to verify backward compatibility).</summary>
        internal void Serialize(Stream destination, byte version)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            if (version < MinimumVersion || version > CurrentVersion)
                throw new ArgumentOutOfRangeException(nameof(version));
            byte[] body = BuildBody(version);
            using var w = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
            w.Write(MagicBytes);
            w.Write(version);
            w.Write((byte)0);            // flags (reserved: bit0 = compressed)
            w.Write(Crc32.Compute(body));
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
            byte version;
            uint expectedCrc;
            int bodyLength;
            try
            {
                version = r.ReadByte();
                if (version < MinimumVersion || version > CurrentVersion)
                    throw new InvalidDataException("Unsupported spool envelope version: " + version + ".");
                _ = r.ReadByte();            // flags (reserved)
                expectedCrc = r.ReadUInt32();
                bodyLength = r.ReadInt32();
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("Truncated spool envelope.", ex);
            }
            if (bodyLength < 0) throw new InvalidDataException("Corrupt spool envelope (negative length).");
            byte[] body = r.ReadBytes(bodyLength);
            if (body.Length != bodyLength) throw new InvalidDataException("Truncated spool envelope.");
            if (Crc32.Compute(body) != expectedCrc) throw new InvalidDataException("Checksum mismatch (corrupt spool).");
            try
            {
                return ParseBody(body, version);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("Corrupt spool envelope body.", ex);
            }
        }

        public static SpoolEnvelope FromArray(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            using var ms = new MemoryStream(data, writable: false);
            return Deserialize(ms);
        }

        private byte[] BuildBody(byte version)
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
                if (version >= 2)
                {
                    w.Write(Settings.AutoRotate);
                    w.Write(JobId.HasValue);
                    if (JobId.HasValue) w.Write(JobId.Value.ToByteArray());
                    WriteNullableString(w, DocumentTitle);
                    WriteNullableString(w, UserName);
                    WriteNullableString(w, TenantId);
                }
                w.Write((long)PdfBytes.LongLength);
                w.Write(PdfBytes);
            }
            return ms.ToArray();
        }

        private static SpoolEnvelope ParseBody(byte[] body, byte version)
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

            Guid? jobId = null;
            string? title = null, user = null, tenant = null;
            if (version >= 2)
            {
                settings.AutoRotate = r.ReadBoolean();
                if (r.ReadBoolean())
                {
                    byte[] guid = r.ReadBytes(16);
                    if (guid.Length != 16) throw new InvalidDataException("Truncated job id.");
                    jobId = new Guid(guid);
                }
                title = ReadNullableString(r);
                user = ReadNullableString(r);
                tenant = ReadNullableString(r);
            }

            long pdfLength = r.ReadInt64();
            if (pdfLength < 0 || pdfLength > int.MaxValue)
                throw new InvalidDataException("Invalid PDF length in spool envelope.");
            byte[] pdf = r.ReadBytes((int)pdfLength);
            if (pdf.Length != pdfLength) throw new InvalidDataException("Truncated PDF payload.");
            return new SpoolEnvelope(pdf, settings)
            {
                JobId = jobId,
                DocumentTitle = title,
                UserName = user,
                TenantId = tenant
            };
        }

        private static void WriteNullableString(BinaryWriter w, string? value)
        {
            w.Write(value is not null);
            if (value is not null) w.Write(value);
        }

        private static string? ReadNullableString(BinaryReader r)
            => r.ReadBoolean() ? r.ReadString() : null;
    }
}
