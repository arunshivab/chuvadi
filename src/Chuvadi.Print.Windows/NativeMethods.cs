using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// Win32 spooler and GDI entry points. Interop with operating-system DLLs only; no native code ships
    /// with the library. The marshalling code is generated at build time (<see cref="LibraryImportAttribute"/>,
    /// D-217), so it is trimming- and NativeAOT-safe; strings are UTF-16 and BOOL returns are mapped explicitly.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static partial class NativeMethods
    {
        private const string Winspool = "winspool.drv";
        private const string Gdi32 = "gdi32.dll";

        internal const uint PRINTER_ENUM_LOCAL = 0x00000002;
        internal const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;

        internal const int DM_OUT_BUFFER = 2;
        internal const int DM_IN_BUFFER = 8;
        internal const int IDOK = 1;

        internal const ushort DC_PAPERS = 2;
        internal const ushort DC_PAPERSIZE = 3;
        internal const ushort DC_MINEXTENT = 4;
        internal const ushort DC_MAXEXTENT = 5;
        internal const ushort DC_BINS = 6;
        internal const ushort DC_DUPLEX = 7;
        internal const ushort DC_BINNAMES = 12;
        internal const ushort DC_ENUMRESOLUTIONS = 13;
        internal const ushort DC_PAPERNAMES = 16;
        internal const ushort DC_ORIENTATION = 17;
        internal const ushort DC_COPIES = 18;
        internal const ushort DC_COLLATE = 22;
        internal const ushort DC_COLORDEVICE = 32;
        internal const ushort DC_MEDIATYPENAMES = 34;
        internal const ushort DC_MEDIATYPES = 35;

        internal const int HORZRES = 8;
        internal const int VERTRES = 10;
        internal const int LOGPIXELSX = 88;
        internal const int LOGPIXELSY = 90;
        internal const int PHYSICALWIDTH = 110;
        internal const int PHYSICALHEIGHT = 111;
        internal const int PHYSICALOFFSETX = 112;
        internal const int PHYSICALOFFSETY = 113;

        internal const int COLORONCOLOR = 3;
        internal const int HALFTONE = 4;
        internal const uint DIB_RGB_COLORS = 0;
        internal const uint SRCCOPY = 0x00CC0020;

        internal const int ERROR_FILE_NOT_FOUND = 2;
        internal const int ERROR_INSUFFICIENT_BUFFER = 122;
        internal const int ERROR_INVALID_PRINTER_NAME = 1801;
        internal const int ERROR_CANCELLED = 1223;
        internal const int ERROR_INVALID_DATATYPE = 1804;

        [StructLayout(LayoutKind.Sequential)]
        internal struct PRINTER_INFO_2
        {
            public IntPtr pServerName;
            public IntPtr pPrinterName;
            public IntPtr pShareName;
            public IntPtr pPortName;
            public IntPtr pDriverName;
            public IntPtr pComment;
            public IntPtr pLocation;
            public IntPtr pDevMode;
            public IntPtr pSepFile;
            public IntPtr pPrintProcessor;
            public IntPtr pDatatype;
            public IntPtr pParameters;
            public IntPtr pSecurityDescriptor;
            public uint Attributes;
            public uint Priority;
            public uint DefaultPriority;
            public uint StartTime;
            public uint UntilTime;
            public uint Status;
            public uint cJobs;
            public uint AveragePPM;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DOCINFO
        {
            public int cbSize;
            public IntPtr lpszDocName;
            public IntPtr lpszOutput;
            public IntPtr lpszDatatype;
            public uint fwType;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DOC_INFO_1
        {
            public IntPtr pDocName;
            public IntPtr pOutputFile;
            public IntPtr pDatatype;
        }

        [LibraryImport(Winspool, SetLastError = true)]
        internal static partial int StartDocPrinterW(IntPtr hPrinter, uint level, ref DOC_INFO_1 pDocInfo);

        [LibraryImport(Winspool, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool StartPagePrinter(IntPtr hPrinter);

        [LibraryImport(Winspool, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool WritePrinter(IntPtr hPrinter, IntPtr pBuf, uint cbBuf, out uint pcWritten);

        [LibraryImport(Winspool, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EndPagePrinter(IntPtr hPrinter);

        [LibraryImport(Winspool, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EndDocPrinter(IntPtr hPrinter);

        [LibraryImport(Winspool, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AbortPrinter(IntPtr hPrinter);

        [LibraryImport(Winspool, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EnumPrintersW(uint flags, string? name, uint level, IntPtr pPrinterEnum,
                                                  uint cbBuf, out uint pcbNeeded, out uint pcReturned);

        [LibraryImport(Winspool, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool OpenPrinterW(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [LibraryImport(Winspool, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ClosePrinter(IntPtr hPrinter);

        [LibraryImport(Winspool, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetPrinterW(IntPtr hPrinter, uint level, IntPtr pPrinter, uint cbBuf, out uint pcbNeeded);

        [LibraryImport(Winspool, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int DocumentPropertiesW(IntPtr hWnd, IntPtr hPrinter, string pDeviceName,
                                                       IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

        [LibraryImport(Winspool, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int DeviceCapabilitiesW(string pDevice, string? pPort, ushort fwCapability,
                                                       IntPtr pOutput, IntPtr pDevMode);

        [LibraryImport(Winspool, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetDefaultPrinterW([In, Out] char[]? pszBuffer, ref int pcchBuffer);

        [LibraryImport(Winspool, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetDefaultPrinterW(string pszPrinter);

        [LibraryImport(Gdi32, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr CreateDCW(string? pwszDriver, string pwszDevice, string? pszPort, IntPtr pdm);

        [LibraryImport(Gdi32, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr CreateICW(string? pszDriver, string pszDevice, string? pszPort, IntPtr pdm);

        [LibraryImport(Gdi32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteDC(IntPtr hdc);

        [LibraryImport(Gdi32, SetLastError = true)]
        internal static partial int StartDocW(IntPtr hdc, ref DOCINFO lpdi);

        [LibraryImport(Gdi32, SetLastError = true)]
        internal static partial int EndDoc(IntPtr hdc);

        [LibraryImport(Gdi32, SetLastError = true)]
        internal static partial int AbortDoc(IntPtr hdc);

        [LibraryImport(Gdi32, SetLastError = true)]
        internal static partial int StartPage(IntPtr hdc);

        [LibraryImport(Gdi32, SetLastError = true)]
        internal static partial int EndPage(IntPtr hdc);

        [LibraryImport(Gdi32)]
        internal static partial int GetDeviceCaps(IntPtr hdc, int index);

        [LibraryImport(Gdi32, SetLastError = true)]
        internal static partial int SetStretchBltMode(IntPtr hdc, int mode);

        [LibraryImport(Gdi32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr lppt);

        [LibraryImport(Gdi32, SetLastError = true)]
        internal static partial int StretchDIBits(IntPtr hdc, int xDest, int yDest, int destWidth, int destHeight,
                                                 int xSrc, int ySrc, int srcWidth, int srcHeight,
                                                 IntPtr lpBits, IntPtr lpbmi, uint iUsage, uint rop);
    }
}
