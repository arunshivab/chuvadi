using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// Win32 spooler and GDI entry points. Interop with operating-system DLLs only; no native code ships
    /// with the library.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class NativeMethods
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

        [DllImport(Winspool, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern bool EnumPrintersW(uint flags, string? name, uint level, IntPtr pPrinterEnum,
                                                  uint cbBuf, out uint pcbNeeded, out uint pcReturned);

        [DllImport(Winspool, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern bool OpenPrinterW(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport(Winspool, ExactSpelling = true, SetLastError = true)]
        internal static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport(Winspool, ExactSpelling = true, SetLastError = true)]
        internal static extern bool GetPrinterW(IntPtr hPrinter, uint level, IntPtr pPrinter, uint cbBuf, out uint pcbNeeded);

        [DllImport(Winspool, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern int DocumentPropertiesW(IntPtr hWnd, IntPtr hPrinter, string pDeviceName,
                                                       IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

        [DllImport(Winspool, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern int DeviceCapabilitiesW(string pDevice, string? pPort, ushort fwCapability,
                                                       IntPtr pOutput, IntPtr pDevMode);

        [DllImport(Winspool, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern bool GetDefaultPrinterW(char[]? pszBuffer, ref int pcchBuffer);

        [DllImport(Winspool, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern bool SetDefaultPrinterW(string pszPrinter);

        [DllImport(Gdi32, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern IntPtr CreateDCW(string? pwszDriver, string pwszDevice, string? pszPort, IntPtr pdm);

        [DllImport(Gdi32, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern IntPtr CreateICW(string? pszDriver, string pszDevice, string? pszPort, IntPtr pdm);

        [DllImport(Gdi32, ExactSpelling = true, SetLastError = true)]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport(Gdi32, ExactSpelling = true, SetLastError = true)]
        internal static extern int StartDocW(IntPtr hdc, ref DOCINFO lpdi);

        [DllImport(Gdi32, ExactSpelling = true, SetLastError = true)]
        internal static extern int EndDoc(IntPtr hdc);

        [DllImport(Gdi32, ExactSpelling = true, SetLastError = true)]
        internal static extern int AbortDoc(IntPtr hdc);

        [DllImport(Gdi32, ExactSpelling = true, SetLastError = true)]
        internal static extern int StartPage(IntPtr hdc);

        [DllImport(Gdi32, ExactSpelling = true, SetLastError = true)]
        internal static extern int EndPage(IntPtr hdc);

        [DllImport(Gdi32, ExactSpelling = true)]
        internal static extern int GetDeviceCaps(IntPtr hdc, int index);

        [DllImport(Gdi32, ExactSpelling = true, SetLastError = true)]
        internal static extern int SetStretchBltMode(IntPtr hdc, int mode);

        [DllImport(Gdi32, ExactSpelling = true, SetLastError = true)]
        internal static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr lppt);

        [DllImport(Gdi32, ExactSpelling = true, SetLastError = true)]
        internal static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int destWidth, int destHeight,
                                                 int xSrc, int ySrc, int srcWidth, int srcHeight,
                                                 IntPtr lpBits, IntPtr lpbmi, uint iUsage, uint rop);
    }
}
