using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// The operating-system entry points the XPS submission uses: COM start-up and object creation, memory streams (ole32)
    /// and the PrintTicket provider (prntvpt). All return an HRESULT and do not set the Win32 last error. Interop with
    /// operating-system DLLs only; the marshalling is source-generated (D-217). Signatures checked against Microsoft's
    /// documentation (PROVENANCE-XPS-COM.md).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static partial class XpsNativeMethods
    {
        private const string Ole32 = "ole32.dll";
        private const string Prntvpt = "prntvpt.dll";

        internal const uint COINIT_MULTITHREADED = 0x0;
        internal const uint CLSCTX_INPROC_SERVER = 0x1;
        internal const uint CLSCTX_LOCAL_SERVER = 0x4;
        internal const int S_OK = 0;
        internal const int S_FALSE = 1;
        internal const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

        /// <summary>EPrintTicketScope.kPTJobScope: job, document and page settings.</summary>
        internal const int kPTJobScope = 2;

        /// <summary>The Print Schema version (1, Windows Vista and later).</summary>
        internal const uint PrintSchemaVersion = 1;

        internal const uint STREAM_SEEK_SET = 0;
        internal const uint STREAM_SEEK_END = 2;

        [LibraryImport(Ole32)]
        internal static partial int CoInitializeEx(nint reserved, uint coInit);

        [LibraryImport(Ole32)]
        internal static partial void CoUninitialize();

        [LibraryImport(Ole32)]
        internal static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

        [LibraryImport(Ole32)]
        internal static partial int CreateStreamOnHGlobal(nint hGlobal, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out nint stream);

        [LibraryImport(Prntvpt, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int PTOpenProvider(string printerName, uint version, out nint provider);

        [LibraryImport(Prntvpt)]
        internal static partial int PTConvertDevModeToPrintTicket(nint provider, uint devModeBytes, nint devMode, int scope, nint printTicket);

        [LibraryImport(Prntvpt)]
        internal static partial int PTCloseProvider(nint provider);
    }
}
