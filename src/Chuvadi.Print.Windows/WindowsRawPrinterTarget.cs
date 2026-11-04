using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// Sends bytes unchanged to a Windows print queue with the RAW datatype (<c>StartDocPrinter</c> and
    /// <c>WritePrinter</c>): PostScript, PCL, ZPL or ESC/POS for a queue whose printer reads that language, for
    /// example a label printer on USB (D-95). The driver does not render anything and print settings are not
    /// applied: put them in the data itself. Queues with a v4 (XPS-only) driver, such as Microsoft Print to PDF,
    /// refuse RAW data.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsRawPrinterTarget : IRawPrintTarget, IDocumentTarget
    {
        /// <summary>The spooler datatype for bytes passed through unchanged.</summary>
        public const string RawDataType = "RAW";

        private const int ChunkSize = 64 * 1024;
        private string _dataType = RawDataType;

        public WindowsRawPrinterTarget(string printerName)
        {
            if (printerName is null) throw new ArgumentNullException(nameof(printerName));
            if (printerName.Length == 0) throw new ArgumentException("Printer name cannot be empty.", nameof(printerName));
            PrinterName = printerName;
        }

        public string PrinterName { get; }

        /// <inheritdoc />
        public string Name => PrinterName;

        /// <summary>
        /// The spooler datatype. Default <see cref="RawDataType"/>. Other values the queue's print processor lists
        /// (for example "RAW [FF appended]") are passed to the spooler as given.
        /// </summary>
        public string DataType
        {
            get => _dataType;
            set => _dataType = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A datatype is required.", nameof(DataType)) : value;
        }

        /// <summary>When set, the spooler writes the job to this file instead of the printer's port. Default null.</summary>
        public string? OutputFileName { get; set; }

        /// <summary>Spooler job id of the most recent job (0 before the first).</summary>
        public int LastSpoolJobId { get; private set; }

        /// <summary>Bytes written in the most recent job.</summary>
        public long LastBytesSent { get; private set; }

        public WindowsRawPrinterTarget WithDataType(string dataType) { DataType = dataType; return this; }
        public WindowsRawPrinterTarget WithOutputFile(string? outputFileName) { OutputFileName = outputFileName; return this; }

        /// <inheritdoc />
        /// <exception cref="PrinterNotFoundException">The queue does not exist or cannot be opened.</exception>
        /// <exception cref="WindowsPrintException">The spooler refused the job or a write.</exception>
        public void Send(ReadOnlyMemory<byte> data, string? documentTitle, CancellationToken cancellationToken)
        {
            WindowsPrinters.EnsureWindows();
            LastSpoolJobId = 0;
            LastBytesSent = 0;
            cancellationToken.ThrowIfCancellationRequested();
            if (!NativeMethods.OpenPrinterW(PrinterName, out IntPtr printer, IntPtr.Zero))
                throw new PrinterNotFoundException(PrinterName, Marshal.GetLastWin32Error());

            IntPtr docName = Marshal.StringToHGlobalUni(string.IsNullOrEmpty(documentTitle) ? "Chuvadi.Print job" : documentTitle);
            IntPtr output = OutputFileName is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(OutputFileName);
            IntPtr dataType = Marshal.StringToHGlobalUni(DataType);
            bool docStarted = false;
            bool completed = false;
            try
            {
                var info = new NativeMethods.DOC_INFO_1 { pDocName = docName, pOutputFile = output, pDatatype = dataType };
                int jobId = NativeMethods.StartDocPrinterW(printer, 1, ref info);
                if (jobId <= 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new WindowsPrintException(error == NativeMethods.ERROR_INVALID_DATATYPE
                        ? "Printer '" + PrinterName + "' does not accept the " + DataType + " datatype (its driver renders everything itself)"
                        : "StartDocPrinter failed on '" + PrinterName + "'", error);
                }
                docStarted = true;
                LastSpoolJobId = jobId;
                if (!NativeMethods.StartPagePrinter(printer))
                    throw new WindowsPrintException("StartPagePrinter failed on '" + PrinterName + "'", Marshal.GetLastWin32Error());

                Write(printer, data, cancellationToken);

                if (!NativeMethods.EndPagePrinter(printer))
                    throw new WindowsPrintException("EndPagePrinter failed on '" + PrinterName + "'", Marshal.GetLastWin32Error());
                if (!NativeMethods.EndDocPrinter(printer))
                    throw new WindowsPrintException("EndDocPrinter failed on '" + PrinterName + "'", Marshal.GetLastWin32Error());
                completed = true;
            }
            finally
            {
                if (docStarted && !completed) NativeMethods.AbortPrinter(printer);
                NativeMethods.ClosePrinter(printer);
                Marshal.FreeHGlobal(docName);
                Marshal.FreeHGlobal(dataType);
                if (output != IntPtr.Zero) Marshal.FreeHGlobal(output);
            }
        }

        /// <summary>Sends <see cref="DocumentSubmission.Document"/> unchanged; print settings are not applied.</summary>
        public void Submit(DocumentSubmission submission, CancellationToken cancellationToken)
        {
            if (submission is null) throw new ArgumentNullException(nameof(submission));
            Send(submission.Document, submission.DocumentTitle, cancellationToken);
        }

        private void Write(IntPtr printer, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            int size = Math.Min(ChunkSize, Math.Max(1, data.Length));
            var managed = new byte[size];
            IntPtr native = Marshal.AllocHGlobal(size);
            try
            {
                int offset = 0;
                while (offset < data.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(size, data.Length - offset);
                    data.Span.Slice(offset, count).CopyTo(managed);
                    Marshal.Copy(managed, 0, native, count);
                    int done = 0;
                    while (done < count)
                    {
                        if (!NativeMethods.WritePrinter(printer, native + done, (uint)(count - done), out uint written))
                            throw new WindowsPrintException("WritePrinter failed on '" + PrinterName + "'", Marshal.GetLastWin32Error());
                        if (written == 0)
                            throw new WindowsPrintException("WritePrinter wrote nothing on '" + PrinterName + "'");
                        done += (int)written;
                    }
                    offset += count;
                    LastBytesSent = offset;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(native);
            }
        }
    }
}
