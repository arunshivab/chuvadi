using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Graphics.Printing.Workflow;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ChuvadiPdfPrinter.Psa
{
    /// <summary>
    /// Background task Windows runs when someone prints to one of the package's virtual printers.
    /// <list type="bullet">
    /// <item>"Chuvadi PDF" (PrinterUri chuvadi-pdf:save-as): Windows shows Save As; the PDF is written there, and a
    /// copy goes to the outbox so the agent can audit it and send it to the configured physical printers.</item>
    /// <item>"Chuvadi PDF (Auto-save)" (chuvadi-pdf:auto-save): the PDF goes to the outbox and the agent saves it in
    /// the configured folder with a unique name.</item>
    /// </list>
    /// Applications that print PDF directly (Edge) send PDF, which is passed through; everything else arrives as
    /// XPS and is converted with Windows' own XPS-to-PDF converter.
    /// </summary>
    public sealed class VirtualPrinterTask : IBackgroundTask
    {
        internal const string SaveAsPath = "save-as";
        internal const string AutoSavePath = "auto-save";

        private BackgroundTaskDeferral? _deferral;
        private string _printerPath = string.Empty;

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            if (taskInstance.TriggerDetails is not PrintWorkflowVirtualPrinterTriggerDetails details) return;
            _deferral = taskInstance.GetDeferral();
            taskInstance.Canceled += (_, _) => _deferral?.Complete();

            PrintWorkflowVirtualPrinterSession session = details.VirtualPrinterSession;
            _printerPath = session.Printer?.PrinterUri?.AbsolutePath ?? string.Empty;
            session.VirtualPrinterDataAvailable += OnDataAvailable;
            session.Start();
        }

        private async void OnDataAvailable(PrintWorkflowVirtualPrinterSession sender, PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
        {
            PrintWorkflowSubmittedStatus status = PrintWorkflowSubmittedStatus.Failed;
            try
            {
                status = await ProcessAsync(args).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TaskLog.Write("Job failed on " + _printerPath + ": " + ex);
            }
            finally
            {
                args.CompleteJob(status);
                _deferral?.Complete();
            }
        }

        private async Task<PrintWorkflowSubmittedStatus> ProcessAsync(PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
        {
            PrintWorkflowPdlSourceContent source = args.SourceContent;
            string title = args.Configuration?.JobTitle ?? "Untitled";
            string outbox = Path.Combine(ApplicationData.Current.LocalFolder.Path, "outbox");
            var entry = new OutboxEntry
            {
                Title = title,
                UserName = Environment.UserDomainName + "\\" + Environment.UserName,
                MachineName = Environment.MachineName,
                Submitted = DateTimeOffset.Now,
                Printer = _printerPath
            };

            if (_printerPath == SaveAsPath)
            {
                StorageFile? target = await args.GetTargetFileAsync();
                if (target is null) return PrintWorkflowSubmittedStatus.Canceled;
                using (IRandomAccessStream output = await target.OpenAsync(FileAccessMode.ReadWrite))
                {
                    output.Size = 0;
                    await WritePdfAsync(args, source, output.GetOutputStreamAt(0));
                }
                entry.SavedTo = target.Path;
                using Stream saved = await target.OpenStreamForReadAsync();
                OutboxWriter.Write(outbox, saved, entry);
                return PrintWorkflowSubmittedStatus.Succeeded;
            }

            if (_printerPath == AutoSavePath)
            {
                Directory.CreateDirectory(outbox);
                entry.PdfFile = entry.Id + ".pdf";
                string partial = Path.Combine(outbox, entry.Id + ".pdf.partial");
                using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite))
                {
                    await WritePdfAsync(args, source, file.AsOutputStream());
                    await file.FlushAsync();
                }
                File.Move(partial, Path.Combine(outbox, entry.PdfFile));
                OutboxWriter.Commit(outbox, entry);
                return PrintWorkflowSubmittedStatus.Succeeded;
            }

            TaskLog.Write("Unknown virtual printer path '" + _printerPath + "'.");
            return PrintWorkflowSubmittedStatus.Failed;
        }

        /// <summary>PDF input is copied as-is; XPS input goes through Windows' XPS-to-PDF converter.</summary>
        private static async Task WritePdfAsync(PrintWorkflowVirtualPrinterDataAvailableEventArgs args,
                                                PrintWorkflowPdlSourceContent source, IOutputStream output)
        {
            if (string.Equals(source.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                await RandomAccessStream.CopyAsync(source.GetInputStream(), output);
                await output.FlushAsync();
                return;
            }
            PrintWorkflowPdlConverter converter = args.GetPdlConverter(PrintWorkflowPdlConversionType.XpsToPdf);
            await converter.ConvertPdlAsync(args.GetJobPrintTicket(), source.GetInputStream(), output);
            await output.FlushAsync();
        }
    }

    /// <summary>Appends task errors to LocalState\task.log (the task has no console).</summary>
    internal static class TaskLog
    {
        public static void Write(string message)
        {
            try
            {
                string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "task.log");
                File.AppendAllText(path, DateTimeOffset.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
