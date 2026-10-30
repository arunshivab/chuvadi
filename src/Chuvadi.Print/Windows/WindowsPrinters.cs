using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Chuvadi.Print.Windows
{
    /// <summary>Discovers the printers installed on this Windows machine and what they can do.</summary>
    [SupportedOSPlatform("windows")]
    public static class WindowsPrinters
    {
        /// <summary>
        /// Every printer queue visible to the current user: local queues and, when
        /// <paramref name="includeConnections"/> is true, connections to shared network printers.
        /// </summary>
        public static IReadOnlyList<PrinterInfo> List(bool includeConnections = true)
        {
            EnsureWindows();
            uint flags = NativeMethods.PRINTER_ENUM_LOCAL | (includeConnections ? NativeMethods.PRINTER_ENUM_CONNECTIONS : 0);
            NativeMethods.EnumPrintersW(flags, null, 2, IntPtr.Zero, 0, out uint needed, out _);
            if (needed == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 0 || error == NativeMethods.ERROR_INSUFFICIENT_BUFFER) return Array.Empty<PrinterInfo>();
                throw new WindowsPrintException("Could not enumerate printers", error);
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!NativeMethods.EnumPrintersW(flags, null, 2, buffer, needed, out _, out uint returned))
                    throw new WindowsPrintException("Could not enumerate printers", Marshal.GetLastWin32Error());
                string? defaultName = DefaultPrinterName;
                int size = Marshal.SizeOf<NativeMethods.PRINTER_INFO_2>();
                var printers = new List<PrinterInfo>((int)returned);
                for (int i = 0; i < returned; i++)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.PRINTER_INFO_2>(buffer + i * size);
                    printers.Add(PrinterSession.FromInfo2(info, defaultName));
                }
                return printers;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Details of one printer.</summary>
        /// <exception cref="PrinterNotFoundException">No such printer, or access denied.</exception>
        public static PrinterInfo Get(string printerName)
        {
            using PrinterSession session = PrinterSession.Open(printerName);
            return session.Info;
        }

        /// <summary>True when a printer with this name can be opened.</summary>
        public static bool Exists(string printerName)
        {
            if (string.IsNullOrEmpty(printerName)) return false;
            EnsureWindows();
            if (!NativeMethods.OpenPrinterW(printerName, out IntPtr handle, IntPtr.Zero)) return false;
            NativeMethods.ClosePrinter(handle);
            return true;
        }

        /// <summary>The current user's default printer, or null when none is set.</summary>
        public static string? DefaultPrinterName
        {
            get
            {
                EnsureWindows();
                int size = 0;
                NativeMethods.GetDefaultPrinterW(null, ref size);
                if (size <= 0) return null;
                var buffer = new char[size];
                if (!NativeMethods.GetDefaultPrinterW(buffer, ref size)) return null;
                int length = Array.IndexOf(buffer, '\0');
                return new string(buffer, 0, length < 0 ? buffer.Length : length);
            }
        }

        /// <summary>Makes <paramref name="printerName"/> the current user's default printer.</summary>
        public static void SetDefault(string printerName)
        {
            if (printerName is null) throw new ArgumentNullException(nameof(printerName));
            EnsureWindows();
            if (!NativeMethods.SetDefaultPrinterW(printerName))
                throw new WindowsPrintException("Could not set the default printer to '" + printerName + "'", Marshal.GetLastWin32Error());
        }

        /// <summary>
        /// What the printer offers (papers, trays, media, resolutions, copies, collation, duplex, colour) and
        /// its geometry (resolution, hard margins, sheet) for <paramref name="settings"/>, or for the
        /// driver's defaults when <paramref name="settings"/> is null.
        /// </summary>
        public static WindowsPrinterCapabilities GetCapabilities(string printerName, PrintSettings? settings = null)
            => GetCapabilities(printerName, settings, PaperMatching.ExactOrCustom, null, null, null, null);

        internal static WindowsPrinterCapabilities GetCapabilities(string printerName, PrintSettings? settings,
                                                                    PaperMatching matching, PrinterBin? bin,
                                                                    PrinterMediaType? mediaType, PrinterResolution? resolution,
                                                                    PrintQuality? quality)
        {
            using PrinterSession session = PrinterSession.Open(printerName);
            IReadOnlyList<PrinterPaper> papers = session.ReadPapers();
            DevModeBuffer devMode = session.GetDefaultDevMode();
            if (settings is not null)
            {
                DevModeRequest request = DevModeMapper.Map(settings, papers, matching, targetHandlesCopies: false,
                                                           bin, mediaType, resolution, quality);
                DevModeMapper.Apply(request, devMode);
                devMode = session.Merge(devMode);
            }

            IntPtr ic = session.CreateContext(devMode, informationOnly: true);
            DeviceGeometry geometry;
            try
            {
                geometry = PrinterSession.ReadGeometry(ic);
            }
            finally
            {
                NativeMethods.DeleteDC(ic);
            }

            return new WindowsPrinterCapabilities(
                session.Name,
                papers,
                session.ReadBins(),
                session.ReadMediaTypes(),
                session.ReadResolutions(),
                Math.Max(1, session.Capability(NativeMethods.DC_COPIES)),
                session.Capability(NativeMethods.DC_COLLATE) == 1,
                session.Capability(NativeMethods.DC_DUPLEX) == 1,
                session.Capability(NativeMethods.DC_COLORDEVICE) == 1,
                Math.Max(0, session.Capability(NativeMethods.DC_ORIENTATION)),
                session.ReadExtent(NativeMethods.DC_MINEXTENT),
                session.ReadExtent(NativeMethods.DC_MAXEXTENT),
                geometry);
        }

        internal static void EnsureWindows()
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Chuvadi.Print.Windows requires Windows.");
        }
    }
}
