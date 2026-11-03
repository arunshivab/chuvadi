using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Chuvadi.Print.Windows
{
    /// <summary>An open printer handle plus the queue's identity and default DEVMODE.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class PrinterSession : IDisposable
    {
        private IntPtr _handle;

        private PrinterSession(string name, IntPtr handle, PrinterInfo info)
        {
            Name = name;
            _handle = handle;
            Info = info;
        }

        public string Name { get; }
        public PrinterInfo Info { get; }

        public static PrinterSession Open(string printerName)
        {
            if (printerName is null) throw new ArgumentNullException(nameof(printerName));
            if (printerName.Length == 0) throw new ArgumentException("Printer name cannot be empty.", nameof(printerName));
            WindowsPrinters.EnsureWindows();
            if (!NativeMethods.OpenPrinterW(printerName, out IntPtr handle, IntPtr.Zero))
                throw new PrinterNotFoundException(printerName, Marshal.GetLastWin32Error());
            try
            {
                PrinterInfo info = ReadInfo(handle, WindowsPrinters.DefaultPrinterName);
                return new PrinterSession(printerName, handle, info);
            }
            catch
            {
                NativeMethods.ClosePrinter(handle);
                throw;
            }
        }

        /// <summary>The driver's current default DEVMODE (public part plus private data).</summary>
        public DevModeBuffer GetDefaultDevMode()
        {
            int size = NativeMethods.DocumentPropertiesW(IntPtr.Zero, _handle, Name, IntPtr.Zero, IntPtr.Zero, 0);
            if (size <= 0)
                throw new WindowsPrintException("DocumentProperties could not size the DEVMODE for '" + Name + "'", Marshal.GetLastWin32Error());
            var bytes = new byte[size];
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                int result = NativeMethods.DocumentPropertiesW(IntPtr.Zero, _handle, Name, pin.AddrOfPinnedObject(), IntPtr.Zero, NativeMethods.DM_OUT_BUFFER);
                if (result != NativeMethods.IDOK)
                    throw new WindowsPrintException("DocumentProperties could not read the DEVMODE for '" + Name + "'", Marshal.GetLastWin32Error());
            }
            finally
            {
                pin.Free();
            }
            return new DevModeBuffer(bytes);
        }

        /// <summary>Lets the driver validate and merge an edited DEVMODE (resolving conflicts, filling private data).</summary>
        public DevModeBuffer Merge(DevModeBuffer edited)
        {
            var output = new byte[edited.Bytes.Length];
            var pinIn = GCHandle.Alloc(edited.Bytes, GCHandleType.Pinned);
            var pinOut = GCHandle.Alloc(output, GCHandleType.Pinned);
            try
            {
                int result = NativeMethods.DocumentPropertiesW(IntPtr.Zero, _handle, Name, pinOut.AddrOfPinnedObject(),
                                                               pinIn.AddrOfPinnedObject(), NativeMethods.DM_IN_BUFFER | NativeMethods.DM_OUT_BUFFER);
                if (result != NativeMethods.IDOK)
                    throw new WindowsPrintException("The driver rejected the print settings for '" + Name + "'", Marshal.GetLastWin32Error());
            }
            finally
            {
                pinIn.Free();
                pinOut.Free();
            }
            return new DevModeBuffer(output);
        }

        // ------------------------------------------------------------------ DeviceCapabilities

        public int Capability(ushort capability)
            => NativeMethods.DeviceCapabilitiesW(Name, Info.PortName, capability, IntPtr.Zero, IntPtr.Zero);

        /// <summary>Calls DeviceCapabilities twice (count, then data) and returns the raw buffer and count.</summary>
        public (byte[] Data, int Count) CapabilityArray(ushort capability, int elementBytes)
        {
            int count = Capability(capability);
            if (count <= 0) return (Array.Empty<byte>(), 0);
            var data = new byte[count * elementBytes];
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                int again = NativeMethods.DeviceCapabilitiesW(Name, Info.PortName, capability, pin.AddrOfPinnedObject(), IntPtr.Zero);
                if (again <= 0) return (Array.Empty<byte>(), 0);
                return (data, Math.Min(count, again));
            }
            finally
            {
                pin.Free();
            }
        }

        public IReadOnlyList<PrinterPaper> ReadPapers()
        {
            (byte[] ids, int idCount) = CapabilityArray(NativeMethods.DC_PAPERS, 2);
            (byte[] sizes, int sizeCount) = CapabilityArray(NativeMethods.DC_PAPERSIZE, 8);
            (byte[] names, int nameCount) = CapabilityArray(NativeMethods.DC_PAPERNAMES, 128);
            int count = Math.Min(idCount, sizeCount);
            var papers = new List<PrinterPaper>(count);
            for (int i = 0; i < count; i++)
            {
                short id = BitConverter.ToInt16(ids, i * 2);
                int width = BitConverter.ToInt32(sizes, i * 8);
                int length = BitConverter.ToInt32(sizes, i * 8 + 4);
                if (width <= 0 || length <= 0) continue;
                string name = i < nameCount ? FixedString(names, i * 128, 64) : "Paper " + id;
                papers.Add(new PrinterPaper(id, name, new PaperSize(DevModeMapper.FromTenthsMm(width), DevModeMapper.FromTenthsMm(length), name)));
            }
            return papers;
        }

        public IReadOnlyList<PrinterBin> ReadBins()
        {
            (byte[] ids, int idCount) = CapabilityArray(NativeMethods.DC_BINS, 2);
            (byte[] names, int nameCount) = CapabilityArray(NativeMethods.DC_BINNAMES, 48);
            var bins = new List<PrinterBin>(idCount);
            for (int i = 0; i < idCount; i++)
            {
                short id = BitConverter.ToInt16(ids, i * 2);
                bins.Add(new PrinterBin(id, i < nameCount ? FixedString(names, i * 48, 24) : "Bin " + id));
            }
            return bins;
        }

        public IReadOnlyList<PrinterMediaType> ReadMediaTypes()
        {
            (byte[] ids, int idCount) = CapabilityArray(NativeMethods.DC_MEDIATYPES, 4);
            (byte[] names, int nameCount) = CapabilityArray(NativeMethods.DC_MEDIATYPENAMES, 128);
            var types = new List<PrinterMediaType>(idCount);
            for (int i = 0; i < idCount; i++)
            {
                uint id = BitConverter.ToUInt32(ids, i * 4);
                types.Add(new PrinterMediaType(id, i < nameCount ? FixedString(names, i * 128, 64) : "Media " + id));
            }
            return types;
        }

        public IReadOnlyList<PrinterResolution> ReadResolutions()
        {
            (byte[] data, int count) = CapabilityArray(NativeMethods.DC_ENUMRESOLUTIONS, 8);
            var list = new List<PrinterResolution>(count);
            for (int i = 0; i < count; i++)
            {
                int x = BitConverter.ToInt32(data, i * 8);
                int y = BitConverter.ToInt32(data, i * 8 + 4);
                if (x > 0 && y > 0) list.Add(new PrinterResolution(x, y));
            }
            return list;
        }

        /// <summary>DC_MINEXTENT / DC_MAXEXTENT pack width (low word) and length (high word) in tenths of a millimetre.</summary>
        public PaperSize? ReadExtent(ushort capability)
        {
            int packed = Capability(capability);
            if (packed <= 0) return null;
            int width = packed & 0xFFFF, length = (packed >> 16) & 0xFFFF;
            if (width <= 0 || length <= 0) return null;
            return new PaperSize(DevModeMapper.FromTenthsMm(width), DevModeMapper.FromTenthsMm(length));
        }

        // ------------------------------------------------------------------ device contexts

        /// <summary>Creates a printer DC (for output) or IC (information only) with the given DEVMODE.</summary>
        public IntPtr CreateContext(DevModeBuffer devMode, bool informationOnly)
        {
            var pin = GCHandle.Alloc(devMode.Bytes, GCHandleType.Pinned);
            try
            {
                IntPtr hdc = informationOnly
                    ? NativeMethods.CreateICW(null, Name, null, pin.AddrOfPinnedObject())
                    : NativeMethods.CreateDCW(null, Name, null, pin.AddrOfPinnedObject());
                if (hdc == IntPtr.Zero)
                    throw new WindowsPrintException("Could not create a device context for '" + Name + "'", Marshal.GetLastWin32Error());
                return hdc;
            }
            finally
            {
                pin.Free();
            }
        }

        public static DeviceGeometry ReadGeometry(IntPtr hdc)
            => new(
                NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSX),
                NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSY),
                NativeMethods.GetDeviceCaps(hdc, NativeMethods.PHYSICALWIDTH),
                NativeMethods.GetDeviceCaps(hdc, NativeMethods.PHYSICALHEIGHT),
                NativeMethods.GetDeviceCaps(hdc, NativeMethods.PHYSICALOFFSETX),
                NativeMethods.GetDeviceCaps(hdc, NativeMethods.PHYSICALOFFSETY),
                NativeMethods.GetDeviceCaps(hdc, NativeMethods.HORZRES),
                NativeMethods.GetDeviceCaps(hdc, NativeMethods.VERTRES));

        public void Dispose()
        {
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            if (handle != IntPtr.Zero) NativeMethods.ClosePrinter(handle);
        }

        // ------------------------------------------------------------------ helpers

        internal static PrinterInfo ReadInfo(IntPtr handle, string? defaultName)
        {
            NativeMethods.GetPrinterW(handle, 2, IntPtr.Zero, 0, out uint needed);
            if (needed == 0)
                throw new WindowsPrintException("GetPrinter failed", Marshal.GetLastWin32Error());
            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!NativeMethods.GetPrinterW(handle, 2, buffer, needed, out _))
                    throw new WindowsPrintException("GetPrinter failed", Marshal.GetLastWin32Error());
                return FromInfo2(Marshal.PtrToStructure<NativeMethods.PRINTER_INFO_2>(buffer), defaultName);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        internal static PrinterInfo FromInfo2(NativeMethods.PRINTER_INFO_2 info, string? defaultName)
        {
            string name = Marshal.PtrToStringUni(info.pPrinterName) ?? string.Empty;
            return new PrinterInfo(
                name,
                EmptyToNull(Marshal.PtrToStringUni(info.pServerName)),
                EmptyToNull(Marshal.PtrToStringUni(info.pShareName)),
                EmptyToNull(Marshal.PtrToStringUni(info.pPortName)),
                EmptyToNull(Marshal.PtrToStringUni(info.pDriverName)),
                EmptyToNull(Marshal.PtrToStringUni(info.pComment)),
                EmptyToNull(Marshal.PtrToStringUni(info.pLocation)),
                (PrinterAttributes)info.Attributes,
                (PrinterStatus)info.Status,
                (int)info.cJobs,
                defaultName is not null && string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase));
        }

        private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>Reads a NUL-padded fixed-length UTF-16 string exactly as the driver wrote it (not trimmed).</summary>
        internal static string FixedString(byte[] data, int offset, int maxChars)
        {
            var chars = new char[maxChars];
            int length = 0;
            for (; length < maxChars; length++)
            {
                char c = (char)BitConverter.ToUInt16(data, offset + length * 2);
                if (c == '\0') break;
                chars[length] = c;
            }
            return new string(chars, 0, length);
        }
    }
}
