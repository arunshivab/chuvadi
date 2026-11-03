using System;
using System.Buffers.Binary;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// A driver's DEVMODEW held as raw bytes (public part plus driver-private extra), edited by field
    /// offset. Working on bytes rather than a marshalled struct keeps the driver-private data intact and
    /// lets the editing logic run (and be tested) on any OS.
    /// </summary>
    internal sealed class DevModeBuffer
    {
        // DEVMODEW public-part offsets (Unicode). sizeof(DEVMODEW) = 220.
        internal const int OffsetDeviceName = 0;      // WCHAR[32]
        internal const int OffsetSpecVersion = 64;
        internal const int OffsetSize = 68;           // dmSize: size of the public part
        internal const int OffsetDriverExtra = 70;    // dmDriverExtra: bytes of private data after it
        internal const int OffsetFields = 72;
        internal const int OffsetOrientation = 76;
        internal const int OffsetPaperSize = 78;
        internal const int OffsetPaperLength = 80;    // tenths of a millimetre
        internal const int OffsetPaperWidth = 82;     // tenths of a millimetre
        internal const int OffsetScale = 84;
        internal const int OffsetCopies = 86;
        internal const int OffsetDefaultSource = 88;
        internal const int OffsetPrintQuality = 90;
        internal const int OffsetColor = 92;
        internal const int OffsetDuplex = 94;
        internal const int OffsetYResolution = 96;
        internal const int OffsetTTOption = 98;
        internal const int OffsetCollate = 100;
        internal const int OffsetFormName = 102;      // WCHAR[32]
        internal const int OffsetMediaType = 196;
        internal const int PublicSize = 220;

        // dmFields flags
        internal const uint DM_ORIENTATION = 0x00000001;
        internal const uint DM_PAPERSIZE = 0x00000002;
        internal const uint DM_PAPERLENGTH = 0x00000004;
        internal const uint DM_PAPERWIDTH = 0x00000008;
        internal const uint DM_SCALE = 0x00000010;
        internal const uint DM_COPIES = 0x00000100;
        internal const uint DM_DEFAULTSOURCE = 0x00000200;
        internal const uint DM_PRINTQUALITY = 0x00000400;
        internal const uint DM_COLOR = 0x00000800;
        internal const uint DM_DUPLEX = 0x00001000;
        internal const uint DM_YRESOLUTION = 0x00002000;
        internal const uint DM_COLLATE = 0x00008000;
        internal const uint DM_FORMNAME = 0x00010000;
        internal const uint DM_MEDIATYPE = 0x02000000;

        internal const short DMORIENT_PORTRAIT = 1;
        internal const short DMORIENT_LANDSCAPE = 2;
        internal const short DMCOLOR_MONOCHROME = 1;
        internal const short DMCOLOR_COLOR = 2;
        internal const short DMDUP_SIMPLEX = 1;
        internal const short DMDUP_VERTICAL = 2;
        internal const short DMDUP_HORIZONTAL = 3;
        internal const short DMCOLLATE_FALSE = 0;
        internal const short DMCOLLATE_TRUE = 1;

        public DevModeBuffer(byte[] bytes)
        {
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < OffsetFields + 4)
                throw new ArgumentException("Buffer is too small to be a DEVMODE.", nameof(bytes));
            int declared = DeclaredSize;
            if (declared < OffsetFields + 4 || declared + DriverExtra > bytes.Length)
                throw new ArgumentException("Buffer does not hold the DEVMODE its header declares.", nameof(bytes));
        }

        public byte[] Bytes { get; }

        /// <summary>dmSize: the size of the public part this driver uses (older drivers use less than 220).</summary>
        public int DeclaredSize => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(OffsetSize));

        public int DriverExtra => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(OffsetDriverExtra));

        public uint Fields
        {
            get => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(OffsetFields));
            set => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(OffsetFields), value);
        }

        /// <summary>True when this driver's public part contains the field at <paramref name="offset"/>.</summary>
        public bool Has(int offset, int length = 2) => offset + length <= DeclaredSize;

        public short ReadInt16(int offset) => BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(offset));

        public uint ReadUInt32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(offset));

        /// <summary>Writes a 16-bit field and sets its dmFields flag. Returns false if the driver's DEVMODE lacks the field.</summary>
        public bool SetInt16(int offset, uint flag, short value)
        {
            if (!Has(offset)) return false;
            BinaryPrimitives.WriteInt16LittleEndian(Bytes.AsSpan(offset), value);
            Fields |= flag;
            return true;
        }

        /// <summary>Writes a 32-bit field and sets its dmFields flag. Returns false if the driver's DEVMODE lacks the field.</summary>
        public bool SetUInt32(int offset, uint flag, uint value)
        {
            if (!Has(offset, 4)) return false;
            BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(offset), value);
            Fields |= flag;
            return true;
        }

        public void ClearFlag(uint flag) => Fields &= ~flag;

        public string DeviceName
        {
            get
            {
                ReadOnlySpan<byte> raw = Bytes.AsSpan(OffsetDeviceName, 64);
                var chars = new char[32];
                int length = 0;
                for (; length < 32; length++)
                {
                    char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(length * 2));
                    if (c == '\0') break;
                    chars[length] = c;
                }
                return new string(chars, 0, length);
            }
        }

        /// <summary>A blank DEVMODEW with the given public size and no driver extra (for tests).</summary>
        internal static DevModeBuffer CreateBlank(ushort publicSize = PublicSize, ushort driverExtra = 0)
        {
            var bytes = new byte[publicSize + driverExtra];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetSpecVersion), 0x0401);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetSize), publicSize);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetDriverExtra), driverExtra);
            return new DevModeBuffer(bytes);
        }
    }
}
