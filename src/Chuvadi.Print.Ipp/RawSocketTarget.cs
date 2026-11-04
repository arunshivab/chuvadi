using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Chuvadi.Print.Ipp
{
    /// <summary>
    /// Sends bytes unchanged to a network printer's raw port (TCP 9100, also called AppSocket or JetDirect), on any
    /// OS: PostScript, PCL, ZPL, ESC/POS, or a PDF for a printer that reads PDF on that port. One connection per
    /// job. The printer gets exactly the bytes given: print settings are not applied, because the raw port has no
    /// way to carry them (put them in the data itself, for example as PJL or ZPL commands).
    /// </summary>
    public sealed class RawSocketTarget : IRawPrintTarget, IDocumentTarget
    {
        /// <summary>The usual raw printing port.</summary>
        public const int DefaultPort = 9100;

        private const int ChunkSize = 64 * 1024;
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(30);
        private TimeSpan _writeTimeout = TimeSpan.FromMinutes(5);
        private TimeSpan _drainTimeout = TimeSpan.FromSeconds(2);

        public RawSocketTarget(string host, int port = DefaultPort)
        {
            if (host is null) throw new ArgumentNullException(nameof(host));
            if (host.Length == 0) throw new ArgumentException("A host is required.", nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            Host = host;
            Port = port;
        }

        /// <summary>Parses socket://host[:port] (port 9100 when omitted).</summary>
        public static RawSocketTarget FromUri(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || !string.Equals(parsed.Scheme, "socket", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expected socket://host[:port]: " + uri, nameof(uri));
            return new RawSocketTarget(parsed.IdnHost, parsed.Port > 0 ? parsed.Port : DefaultPort);
        }

        public string Host { get; }
        public int Port { get; }

        /// <summary>socket://host:port. Recorded in the audit trail.</summary>
        public string Name => "socket://" + (Host.Contains(':') ? "[" + Host + "]" : Host) + ":" + Port.ToString(CultureInfo.InvariantCulture);

        /// <summary>How long to wait for the printer to accept the connection. Default 30 seconds.</summary>
        public TimeSpan ConnectTimeout { get => _connectTimeout; set => _connectTimeout = Positive(value, nameof(ConnectTimeout)); }

        /// <summary>How long one write may block while the printer is busy. Default 5 minutes.</summary>
        public TimeSpan WriteTimeout { get => _writeTimeout; set => _writeTimeout = Positive(value, nameof(WriteTimeout)); }

        /// <summary>
        /// After the data is sent, how long to wait for the printer to close the connection (anything it sends back
        /// is read and discarded). Some printers drop the end of a job when the connection is closed abruptly.
        /// Zero closes at once. Default 2 seconds.
        /// </summary>
        public TimeSpan DrainTimeout
        {
            get => _drainTimeout;
            set => _drainTimeout = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(DrainTimeout));
        }

        /// <summary>Bytes sent in the most recent job.</summary>
        public long LastBytesSent { get; private set; }

        /// <inheritdoc />
        /// <exception cref="IOException">The printer could not be reached or closed the connection.</exception>
        public void Send(ReadOnlyMemory<byte> data, string? documentTitle, CancellationToken cancellationToken)
        {
            LastBytesSent = 0;
            cancellationToken.ThrowIfCancellationRequested();
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    connect.CancelAfter(ConnectTimeout);
                    try
                    {
                        socket.ConnectAsync(Host, Port, connect.Token).AsTask().GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new IOException("Timed out connecting to the printer at " + Name + ".");
                    }
                }

                socket.SendTimeout = (int)Math.Min(int.MaxValue, WriteTimeout.TotalMilliseconds);
                int offset = 0;
                while (offset < data.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(ChunkSize, data.Length - offset);
                    int sent = socket.Send(data.Span.Slice(offset, count));
                    offset += sent;
                    LastBytesSent = offset;
                }

                socket.Shutdown(SocketShutdown.Send);
                Drain(socket, cancellationToken);
            }
            catch (SocketException ex)
            {
                throw new IOException("Printing to " + Name + " failed: " + ex.Message, ex);
            }
        }

        /// <summary>Sends <see cref="DocumentSubmission.Document"/> unchanged (see the class remarks: settings are not applied).</summary>
        public void Submit(DocumentSubmission submission, CancellationToken cancellationToken)
        {
            if (submission is null) throw new ArgumentNullException(nameof(submission));
            Send(submission.Document, submission.DocumentTitle, cancellationToken);
        }

        private void Drain(Socket socket, CancellationToken cancellationToken)
        {
            if (DrainTimeout == TimeSpan.Zero) return;
            DateTime deadline = DateTime.UtcNow + DrainTimeout;
            var buffer = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return;
                if (!socket.Poll((int)Math.Min(int.MaxValue, left.TotalMilliseconds * 1000), SelectMode.SelectRead)) return;
                int read;
                try { read = socket.Receive(buffer); }
                catch (SocketException) { return; } // reset after our data was sent: the job is delivered
                if (read == 0) return;               // the printer closed the connection
            }
        }

        private static TimeSpan Positive(TimeSpan value, string name)
            => value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(name);
    }
}
