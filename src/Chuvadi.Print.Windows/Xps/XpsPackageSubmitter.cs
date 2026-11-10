using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Chuvadi.Print.Windows
{
    /// <summary>What <see cref="XpsPackageSubmitter.Submit"/> sends.</summary>
    internal sealed class XpsSubmissionRequest
    {
        public XpsSubmissionRequest(string printerName, string jobName, byte[] xps, byte[] devMode, TimeSpan? statusTimeout)
        {
            PrinterName = printerName;
            JobName = jobName;
            Xps = xps;
            DevMode = devMode;
            StatusTimeout = statusTimeout;
        }

        public string PrinterName { get; }
        public string JobName { get; }

        /// <summary>The XPS package from <see cref="XpsWriter"/>.</summary>
        public byte[] Xps { get; }

        /// <summary>The driver-merged DEVMODE the job PrintTicket is made from.</summary>
        public byte[] DevMode { get; }

        /// <summary>How long to wait for the job's completion after the package is written; null = do not wait.</summary>
        public TimeSpan? StatusTimeout { get; }
    }

    /// <summary>What happened to a submission.</summary>
    internal sealed class XpsSubmissionResult
    {
        public XpsSubmissionResult(byte[] printTicket, bool statusEventsConnected, PrintDocumentPackageStatus? status, int statusUpdates)
        {
            PrintTicket = printTicket;
            StatusEventsConnected = statusEventsConnected;
            Status = status;
            StatusUpdates = statusUpdates;
        }

        /// <summary>The job PrintTicket (XML) sent with the job.</summary>
        public byte[] PrintTicket { get; }

        /// <summary>Whether the print system accepted the status sink.</summary>
        public bool StatusEventsConnected { get; }

        /// <summary>The last status received; null when none arrived.</summary>
        public PrintDocumentPackageStatus? Status { get; }

        public int StatusUpdates { get; }
    }

    /// <summary>
    /// Submits an XPS package to a Windows print queue through the Print Document Package API: the job PrintTicket is made
    /// from the DEVMODE (PTConvertDevModeToPrintTicket, job scope), the package is loaded into the XPS Object Model and
    /// written page by page to the queue's IXpsOMPackageWriter, and the job's status arrives through
    /// IPrintDocumentPackageStatusEvent. Everything runs on one thread of its own in the multithreaded apartment, so the
    /// caller's apartment (a UI thread, say) does not matter and status callbacks need no message loop.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class XpsPackageSubmitter
    {
        private static readonly Guid IidFactory = new(XpsComIds.IPrintDocumentPackageTargetFactory);
        private static readonly Guid IidXpsTarget = new(XpsComIds.IXpsDocumentPackageTarget);
        private static readonly Guid IidConnectionPointContainer = new(XpsComIds.IConnectionPointContainer);
        private static readonly Guid IidStatusEvent = new(XpsComIds.IPrintDocumentPackageStatusEvent);

        /// <exception cref="WindowsPrintException">A step failed, or the print system reported the job failed or cancelled.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled (the job is cancelled too).</exception>
        public static XpsSubmissionResult Submit(XpsSubmissionRequest request, CancellationToken cancellationToken)
            => RunInMultithreadedApartment(() => SubmitCore(request, cancellationToken));

        /// <summary>The job PrintTicket made from a DEVMODE, without printing.</summary>
        public static byte[] CreatePrintTicket(string printerName, byte[] devMode)
            => RunInMultithreadedApartment(() =>
            {
                using ComRef<IComStream> stream = NewStream(Array.Empty<byte>());
                return WritePrintTicket(printerName, devMode, stream);
            });

        // ------------------------------------------------------------------ the submission

        private static XpsSubmissionResult SubmitCore(XpsSubmissionRequest request, CancellationToken cancellationToken)
        {
            string step = "creating the job PrintTicket";
            ComRef<IPrintDocumentPackageTarget>? target = null;
            bool written = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using ComRef<IComStream> ticketStream = NewStream(Array.Empty<byte>());
                byte[] ticket = WritePrintTicket(request.PrinterName, request.DevMode, ticketStream);

                step = "creating the print document package target factory";
                using ComRef<IPrintDocumentPackageTargetFactory> factory = CreateFactory();

                step = "starting the job on the queue";
                factory.Object.CreateDocumentPackageTargetForPrintJob(request.PrinterName, request.JobName, 0, ticketStream.Pointer,
                                                                      out nint targetPointer);
                target = ComRef<IPrintDocumentPackageTarget>.Attach(targetPointer, "CreateDocumentPackageTargetForPrintJob");

                step = "choosing the XPS package target";
                RequireMsXps(target.Object);
                target.Object.GetPackageTarget(XpsComIds.TargetTypeMsXps, IidXpsTarget, out nint xpsTargetPointer);
                using ComRef<IXpsDocumentPackageTarget> xpsTarget = ComRef<IXpsDocumentPackageTarget>.Attach(xpsTargetPointer, "GetPackageTarget");

                step = "connecting the job status events";
                using var sink = new PackageStatusSink();
                using StatusConnection? connection = StatusConnection.TryConnect(target, sink);

                step = "loading the XPS package";
                xpsTarget.Object.GetXpsOMFactory(out nint omFactoryPointer);
                using ComRef<IXpsOMObjectFactory> omFactory = ComRef<IXpsOMObjectFactory>.Attach(omFactoryPointer, "GetXpsOMFactory");
                using ComRef<IComStream> xpsStream = NewStream(request.Xps);
                omFactory.Object.CreatePackageFromStream(xpsStream.Pointer, 0, out nint packagePointer);
                using ComRef<IXpsOMPackage> package = ComRef<IXpsOMPackage>.Attach(packagePointer, "CreatePackageFromStream");
                package.Object.GetDocumentSequence(out nint sequencePointer);
                using ComRef<IXpsOMDocumentSequence> sequence = ComRef<IXpsOMDocumentSequence>.Attach(sequencePointer, "GetDocumentSequence");

                step = "opening the queue's package writer";
                omFactory.Object.CreatePartUri(XpsWriter.FixedDocumentSequencePart, out nint sequenceNamePointer);
                using ComPointer sequenceName = ComPointer.Attach(sequenceNamePointer, "CreatePartUri");
                xpsTarget.Object.GetXpsOMPackageWriter(sequenceName.Pointer, 0, out nint writerPointer);
                using ComRef<IXpsOMPackageWriter> writer = ComRef<IXpsOMPackageWriter>.Attach(writerPointer, "GetXpsOMPackageWriter");

                sequence.Object.GetDocuments(out nint documentsPointer);
                using ComRef<IXpsOMDocumentCollection> documents = ComRef<IXpsOMDocumentCollection>.Attach(documentsPointer, "GetDocuments");
                documents.Object.GetCount(out uint documentCount);
                for (uint d = 0; d < documentCount; d++)
                {
                    step = "writing document " + (d + 1);
                    documents.Object.GetAt(d, out nint documentPointer);
                    using ComRef<IXpsOMDocument> document = ComRef<IXpsOMDocument>.Attach(documentPointer, "IXpsOMDocumentCollection.GetAt");
                    document.Object.GetPartName(out nint documentNamePointer);
                    using ComPointer documentName = ComPointer.Attach(documentNamePointer, "IXpsOMDocument.GetPartName");
                    writer.Object.StartNewDocument(documentName.Pointer, 0, 0, 0, 0);

                    document.Object.GetPageReferences(out nint referencesPointer);
                    using ComRef<IXpsOMPageReferenceCollection> references =
                        ComRef<IXpsOMPageReferenceCollection>.Attach(referencesPointer, "GetPageReferences");
                    references.Object.GetCount(out uint pageCount);
                    for (uint p = 0; p < pageCount; p++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        step = "writing page " + (p + 1) + " of document " + (d + 1);
                        references.Object.GetAt(p, out nint referencePointer);
                        using ComRef<IXpsOMPageReference> reference = ComRef<IXpsOMPageReference>.Attach(referencePointer, "IXpsOMPageReferenceCollection.GetAt");
                        reference.Object.GetAdvisoryPageDimensions(out XpsSize size);
                        reference.Object.GetPage(out nint pagePointer);
                        using (ComPointer page = ComPointer.Attach(pagePointer, "IXpsOMPageReference.GetPage"))
                            writer.Object.AddPage(page.Pointer, size, 0, 0, 0, 0);
                        reference.Object.DiscardPage();
                    }
                }

                step = "closing the package";
                cancellationToken.ThrowIfCancellationRequested();
                writer.Object.Close();
                written = true;

                step = "waiting for the job status";
                bool connected = connection is not null;
                if (connected && request.StatusTimeout is TimeSpan timeout)
                    WaitForCompletion(sink, timeout, cancellationToken);

                PrintDocumentPackageStatus? status = sink.Latest;
                if (status is PrintDocumentPackageStatus s) ThrowIfUnsuccessful(s, request.PrinterName);
                return new XpsSubmissionResult(ticket, connected, status, sink.Updates);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelQuietly(target);
                throw;
            }
            catch (WindowsPrintException)
            {
                if (!written) CancelQuietly(target);
                throw;
            }
            catch (Exception ex)
            {
                if (!written) CancelQuietly(target);
                throw new WindowsPrintException("Printing to '" + request.PrinterName + "' through XPS failed while " + step
                                                + ": " + Describe(ex), ex);
            }
            finally
            {
                target?.Dispose();
            }
        }

        private static void WaitForCompletion(PackageStatusSink sink, TimeSpan timeout, CancellationToken cancellationToken)
        {
            int index = WaitHandle.WaitAny(new[] { sink.Finished, cancellationToken.WaitHandle }, timeout);
            if (index == 1) cancellationToken.ThrowIfCancellationRequested();
            if (index == WaitHandle.WaitTimeout)
                throw new WindowsPrintException("The print queue did not report the job finished within " + timeout
                                                + " (" + sink.Updates + " status updates received). The job may still be printing; "
                                                + "check the queue.");
        }

        internal static void ThrowIfUnsuccessful(PrintDocumentPackageStatus status, string printerName)
        {
            switch (status.Completion)
            {
                case 2:
                    throw new WindowsPrintException("The job was cancelled in the print queue '" + printerName + "'.");
                case 3:
                    throw new WindowsPrintException("The print queue '" + printerName + "' reported the job failed (HRESULT 0x"
                                                    + status.PackageStatus.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ").");
            }
        }

        private static void RequireMsXps(IPrintDocumentPackageTarget target)
        {
            target.GetPackageTargetTypes(out uint count, out nint types);
            try
            {
                var names = new string[count];
                for (int i = 0; i < count; i++)
                {
                    Guid type = Marshal.PtrToStructure<Guid>(types + i * 16);
                    if (type == XpsComIds.TargetTypeMsXps) return;
                    names[i] = type == XpsComIds.TargetTypeOpenXps ? "OpenXPS" : type.ToString();
                }
                throw new WindowsPrintException("The queue does not take XPS 1.0 packages (it lists: "
                                                + (count == 0 ? "nothing" : string.Join(", ", names)) + ").");
            }
            finally
            {
                if (types != 0) Marshal.FreeCoTaskMem(types);
            }
        }

        private static void CancelQuietly(ComRef<IPrintDocumentPackageTarget>? target)
        {
            if (target is null) return;
            try { target.Object.Cancel(); }
            catch (Exception) { } // best effort: the job may already be gone
        }

        // ------------------------------------------------------------------ PrintTicket

        private static byte[] WritePrintTicket(string printerName, byte[] devMode, ComRef<IComStream> stream)
        {
            int hr = XpsNativeMethods.PTOpenProvider(printerName, XpsNativeMethods.PrintSchemaVersion, out nint provider);
            if (hr < 0) throw HResultException("PTOpenProvider", hr);
            try
            {
                var pin = GCHandle.Alloc(devMode, GCHandleType.Pinned);
                try
                {
                    hr = XpsNativeMethods.PTConvertDevModeToPrintTicket(provider, (uint)devMode.Length, pin.AddrOfPinnedObject(),
                                                                        XpsNativeMethods.kPTJobScope, stream.Pointer);
                }
                finally
                {
                    pin.Free();
                }
                if (hr < 0) throw HResultException("PTConvertDevModeToPrintTicket", hr);
            }
            finally
            {
                XpsNativeMethods.PTCloseProvider(provider);
            }
            return ReadAllAndRewind(stream.Object);
        }

        // ------------------------------------------------------------------ COM helpers

        private static ComRef<IPrintDocumentPackageTargetFactory> CreateFactory()
        {
            int hr = XpsNativeMethods.CoCreateInstance(XpsComIds.PrintDocumentPackageTargetFactoryClass, 0,
                                                       XpsNativeMethods.CLSCTX_INPROC_SERVER | XpsNativeMethods.CLSCTX_LOCAL_SERVER,
                                                       IidFactory, out nint factory);
            if (hr < 0) throw HResultException("CoCreateInstance(PrintDocumentPackageTargetFactory)", hr);
            return ComRef<IPrintDocumentPackageTargetFactory>.Attach(factory, "CoCreateInstance");
        }

        /// <summary>A memory stream (CreateStreamOnHGlobal) holding <paramref name="content"/>, positioned at its start.</summary>
        internal static ComRef<IComStream> NewStream(byte[] content)
        {
            int hr = XpsNativeMethods.CreateStreamOnHGlobal(0, true, out nint pointer);
            if (hr < 0) throw HResultException("CreateStreamOnHGlobal", hr);
            ComRef<IComStream> stream = ComRef<IComStream>.Attach(pointer, "CreateStreamOnHGlobal");
            try
            {
                if (content.Length > 0)
                {
                    var pin = GCHandle.Alloc(content, GCHandleType.Pinned);
                    try
                    {
                        Check(stream.Object.Write(pin.AddrOfPinnedObject(), (uint)content.Length, out uint written), "IStream.Write");
                        if (written != content.Length) throw new WindowsPrintException("IStream.Write wrote " + written + " of " + content.Length + " bytes.");
                    }
                    finally
                    {
                        pin.Free();
                    }
                }
                Check(stream.Object.Seek(0, XpsNativeMethods.STREAM_SEEK_SET, out _), "IStream.Seek");
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>Everything in the stream, then the position back at the start.</summary>
        internal static byte[] ReadAllAndRewind(IComStream stream)
        {
            Check(stream.Seek(0, XpsNativeMethods.STREAM_SEEK_END, out ulong length), "IStream.Seek");
            Check(stream.Seek(0, XpsNativeMethods.STREAM_SEEK_SET, out _), "IStream.Seek");
            if (length > int.MaxValue) throw new WindowsPrintException("The PrintTicket is too large (" + length + " bytes).");
            var bytes = new byte[(int)length];
            int total = 0;
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                while (total < bytes.Length)
                {
                    Check(stream.Read(pin.AddrOfPinnedObject() + total, (uint)(bytes.Length - total), out uint read), "IStream.Read");
                    if (read == 0) break;
                    total += (int)read;
                }
            }
            finally
            {
                pin.Free();
            }
            Check(stream.Seek(0, XpsNativeMethods.STREAM_SEEK_SET, out _), "IStream.Seek");
            return total == bytes.Length ? bytes : bytes.AsSpan(0, total).ToArray();
        }

        private static void Check(int hr, string call)
        {
            if (hr < 0) throw HResultException(call, hr);
        }

        internal static WindowsPrintException HResultException(string call, int hr)
            => new(call + " failed (HRESULT 0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ")");

        private static string Describe(Exception ex)
            => ex.Message + " (HRESULT 0x" + ex.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ")";

        /// <summary>Runs <paramref name="work"/> on a new thread in the multithreaded apartment and returns its result or rethrows its exception.</summary>
        internal static T RunInMultithreadedApartment<T>(Func<T> work)
        {
            T result = default!;
            ExceptionDispatchInfo? failure = null;
            var thread = new Thread(() =>
            {
                int hr = XpsNativeMethods.CoInitializeEx(0, XpsNativeMethods.COINIT_MULTITHREADED);
                bool initialized = hr == XpsNativeMethods.S_OK || hr == XpsNativeMethods.S_FALSE;
                try
                {
                    if (hr < 0 && hr != XpsNativeMethods.RPC_E_CHANGED_MODE) throw HResultException("CoInitializeEx", hr);
                    result = work();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    if (initialized) XpsNativeMethods.CoUninitialize();
                }
            })
            {
                IsBackground = true,
                Name = "Chuvadi.Print XPS submission"
            };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            thread.Join();
            failure?.Throw();
            return result;
        }

        // ------------------------------------------------------------------ status connection

        /// <summary>The status sink advised on the job's connection point; Dispose unadvises it.</summary>
        private sealed class StatusConnection : IDisposable
        {
            private static readonly System.Runtime.InteropServices.Marshalling.StrategyBasedComWrappers Wrappers = new();
            private ComRef<IConnectionPoint>? _point;
            private readonly uint _cookie;
            private nint _sinkPointer;

            private StatusConnection(ComRef<IConnectionPoint> point, uint cookie, nint sinkPointer)
            {
                _point = point;
                _cookie = cookie;
                _sinkPointer = sinkPointer;
            }

            /// <summary>Advises the sink; null when the job object has no status connection point.</summary>
            public static StatusConnection? TryConnect(ComRef<IPrintDocumentPackageTarget> target, PackageStatusSink sink)
            {
                using ComRef<IConnectionPointContainer>? container = target.As<IConnectionPointContainer>(IidConnectionPointContainer);
                if (container is null) return null;
                nint pointPointer;
                try
                {
                    container.Object.FindConnectionPoint(IidStatusEvent, out pointPointer);
                }
                catch (Exception)
                {
                    return null; // no status connection point: the job is printed without status
                }
                ComRef<IConnectionPoint> point = ComRef<IConnectionPoint>.Attach(pointPointer, "FindConnectionPoint");
                nint sinkPointer = Wrappers.GetOrCreateComInterfaceForObject(sink, System.Runtime.InteropServices.CreateComInterfaceFlags.None);
                try
                {
                    point.Object.Advise(sinkPointer, out uint cookie);
                    return new StatusConnection(point, cookie, sinkPointer);
                }
                catch (Exception)
                {
                    Marshal.Release(sinkPointer);
                    point.Dispose();
                    return null; // the print system refused the sink: the job is printed without status
                }
            }

            public void Dispose()
            {
                ComRef<IConnectionPoint>? point = _point;
                _point = null;
                if (point is null) return;
                try { point.Object.Unadvise(_cookie); }
                catch (Exception) { } // the job object may already be closed
                point.Dispose();
                if (_sinkPointer != 0) Marshal.Release(_sinkPointer);
                _sinkPointer = 0;
            }
        }
    }
}
