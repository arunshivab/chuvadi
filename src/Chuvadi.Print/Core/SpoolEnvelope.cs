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
    /// <para>Version 2 adds <see cref="PrintSettings.AutoRotate"/> and the job metadata
    /// (<see cref="JobId"/>, <see cref="DocumentTitle"/>, <see cref="UserName"/>, <see cref="TenantId"/>).
    /// Version 1 envelopes are still read; their metadata is null and AutoRotate is false.</para>
    /// <para>Version 3 (1.0.0-rc.4) adds <see cref="PrintSettings.PrinterDefaults"/>. It is written only when that
    /// setting is not <see cref="PrinterDefault.None"/>; otherwise <see cref="Serialize(Stream)"/> still writes
    /// version 2, byte for byte as before, so agents on 1.0.0-rc.3 and earlier keep reading those jobs.</para>
    /// <para>Version 4 (1.0.0-rc.5) adds <see cref="PrintSettings.Private"/>. It is written only for a private job;
    /// agents older than 1.0.0-rc.5 refuse it, so a private job is never printed by an agent that does not keep its
    /// title out of its records.</para>
    /// </remarks>
    public sealed class SpoolEnvelope
    {
        private static readonly byte[] MagicBytes = { (byte)'C', (byte)'V', (byte)'P', (byte)'R' };

        /// <summary>
        /// The newest envelope format version this library writes and reads. <see cref="Serialize(Stream)"/> writes the
        /// oldest version that holds the settings (see the remarks): 2, 3 or 4.
        /// </summary>
        public const byte CurrentVersion = 4;

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

        /// <summary>
        /// Writes the envelope: version 4 for a <see cref="PrintSettings.Private"/> job, else version 3 when
        /// <see cref="PrintSettings.PrinterDefaults"/> is set, otherwise version 2.
        /// </summary>
        public void Serialize(Stream destination) => Serialize(destination, VersionFor(Settings));

        /// <summary>The oldest envelope version that holds <paramref name="settings"/>.</summary>
        internal static byte VersionFor(PrintSettings settings)
            => settings.Private ? (byte)4 : settings.PrinterDefaults != PrinterDefault.None ? (byte)3 : (byte)2;

        /// <summary>Writes a specific format version (used to verify backward compatibility).</summary>
        /// <exception cref="InvalidOperationException">
        /// The version cannot hold these settings (<see cref="PrintSettings.PrinterDefaults"/> needs version 3).
        /// </exception>
        internal void Serialize(Stream destination, byte version)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            if (version < MinimumVersion || version > CurrentVersion)
                throw new ArgumentOutOfRangeException(nameof(version));
            byte needed = VersionFor(Settings);
            if (needed > 2 && version < needed)
                throw new InvalidOperationException("Spool envelope version " + version + " cannot hold these settings ("
                    + (Settings.Private ? "PrintSettings.Private" : "PrintSettings.PrinterDefaults") + ").");
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
                if (version >= 3) w.Write((int)Settings.PrinterDefaults);
                if (version >= 4) w.Write((byte)(Settings.Private ? 1 : 0));
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
            if (version >= 3)
            {
                int flags = r.ReadInt32();
                const int known = (int)(PrinterDefault.Paper | PrinterDefault.Duplex | PrinterDefault.Color | PrinterDefault.Copies);
                if ((flags & ~known) != 0) throw new InvalidDataException("Unknown printer default flags in spool envelope: " + flags + ".");
                settings.PrinterDefaults = (PrinterDefault)flags;
            }
            if (version >= 4)
            {
                byte options = r.ReadByte();
                if ((options & ~1) != 0) throw new InvalidDataException("Unknown job options in spool envelope: " + options + ".");
                settings.Private = (options & 1) != 0;
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
