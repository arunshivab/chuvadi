using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Chuvadi.Print.Ipp
{
    /// <summary>
    /// Sends the original PDF to an IPP printer that accepts it (driverless printing, any OS), with the
    /// print settings as IPP job attributes; the printer rasterises and lays out the pages. Use with
    /// <see cref="PrintJob.SubmitTo"/>. Settings IPP cannot express (alignment, custom margins, custom
    /// scale) are left to the printer and listed in <see cref="LastApproximations"/>.
    /// </summary>
    public sealed class IppDocumentTarget : IDocumentTarget, IDisposable
    {
        private readonly IppClient _client;
        private readonly bool _ownsClient;
        private readonly List<string> _approximations = new();

        public IppDocumentTarget(string printerUri, IppClientOptions? clientOptions = null)
        {
            _client = new IppClient(printerUri, clientOptions);
            _ownsClient = true;
        }

        public IppDocumentTarget(IppClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public IppClient Client => _client;

        /// <inheritdoc />
        public string Name => _client.PrinterUri;

        /// <summary>Tray, media type, quality, strictness and extra attributes.</summary>
        public IppJobOptions JobOptions { get; set; } = new();

        /// <summary>
        /// When set, <see cref="Submit"/> waits up to this long for the printer to finish the job and fails if
        /// the printer cancels or aborts it. Null (default) returns once the printer has accepted the job.
        /// </summary>
        public TimeSpan? WaitForCompletion { get; set; }

        /// <summary>How often job state is polled while waiting. Default 2 seconds.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>Printer job id of the most recent job (0 before the first).</summary>
        public int LastJobId { get; private set; }

        /// <summary>Settings that could not be expressed exactly in the most recent job.</summary>
        public IReadOnlyList<string> LastApproximations => _approximations;

        /// <summary>Printer capabilities read for the most recent job.</summary>
        public IppPrinterCapabilities? LastCapabilities { get; private set; }

        public IppDocumentTarget WithJobOptions(Action<IppJobOptions> configure)
        {
            (configure ?? throw new ArgumentNullException(nameof(configure)))(JobOptions);
            return this;
        }

        public IppDocumentTarget WithWaitForCompletion(TimeSpan? timeout) { WaitForCompletion = timeout; return this; }

        /// <summary>Reads the printer's capabilities.</summary>
        public IppPrinterCapabilities QueryCapabilities(CancellationToken cancellationToken = default)
            => new(_client.GetPrinterAttributes(null, cancellationToken).Group(IppTag.PrinterAttributes)
                   ?? throw new IppException("The printer returned no printer attributes."));

        /// <inheritdoc />
        public void Submit(DocumentSubmission submission, CancellationToken cancellationToken)
        {
            if (submission is null) throw new ArgumentNullException(nameof(submission));
            _approximations.Clear();
            IppPrinterCapabilities caps = QueryCapabilities(cancellationToken);
            LastCapabilities = caps;

            if (!caps.IsAcceptingJobs) throw new IppException("The printer is not accepting jobs.");
            string format = submission.ContentType;
            if (caps.DocumentFormats.Count > 0 && !caps.SupportsFormat(format))
            {
                if (caps.SupportsFormat("application/octet-stream")) format = "application/octet-stream";
                else throw new NotSupportedException("The printer does not accept " + submission.ContentType
                                                     + "; print rendered pages with IppPrinterTarget instead.");
            }

            IppAttributeGroup ticket = IppJobTicket.Build(submission.Settings, caps, JobOptions, documentMode: true,
                                                          printerMakesCopies: true, submission.PageCount, null, _approximations);
            using Stream document = AsStream(submission.Document);
            LastJobId = _client.PrintJob(document, format, ticket, submission.DocumentTitle ?? "Chuvadi.Print job", cancellationToken);
            if (WaitForCompletion.HasValue) IppJobWaiter.Wait(_client, LastJobId, WaitForCompletion.Value, PollInterval, cancellationToken);
        }

        public void Dispose()
        {
            if (_ownsClient) _client.Dispose();
        }

        private static Stream AsStream(ReadOnlyMemory<byte> memory)
            => MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment) && segment.Array is not null
                ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
                : new MemoryStream(memory.ToArray(), writable: false);
    }

    internal static class IppJobWaiter
    {
        public static void Wait(IppClient client, int jobId, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                IppJobState state = client.GetJobState(jobId, cancellationToken);
                switch (state)
                {
                    case IppJobState.Completed:
                        return;
                    case IppJobState.Canceled:
                    case IppJobState.Aborted:
                        throw new IppException("The printer " + (state == IppJobState.Canceled ? "cancelled" : "aborted") + " job " + jobId + ".");
                }
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("Job " + jobId + " did not finish within " + timeout + " (state " + state + ").");
                if (cancellationToken.WaitHandle.WaitOne(pollInterval)) cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
