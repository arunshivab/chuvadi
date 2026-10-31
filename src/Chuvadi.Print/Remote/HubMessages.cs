using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Chuvadi.Print.Remote
{
    /// <summary>
    /// A transport-neutral HTTP request for <see cref="PrintHub.HandleAsync"/>. The host's web stack (ASP.NET
    /// Core, HttpListener, anything) copies the method, the path below the hub's base address, the headers
    /// and the body into it.
    /// </summary>
    public sealed class HubRequest
    {
        public HubRequest(string method, string path)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>GET or POST.</summary>
        public string Method { get; }

        /// <summary>The path below the hub's base address, e.g. <c>agents/poll</c> or <c>/jobs/{id}/status</c>.</summary>
        public string Path { get; }

        /// <summary>Request headers (case-insensitive names). At least Authorization, X-Chuvadi-Agent, X-Chuvadi-Protocol and Range.</summary>
        public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The request body, or null.</summary>
        public byte[]? Body { get; set; }

        /// <summary>Adds a header and returns this request.</summary>
        public HubRequest WithHeader(string name, string value)
        {
            Headers[name] = value;
            return this;
        }

        /// <summary>Sets the body and returns this request.</summary>
        public HubRequest WithBody(byte[]? body)
        {
            Body = body;
            return this;
        }
    }

    /// <summary>
    /// What <see cref="PrintHub.HandleAsync"/> answers. The host copies the status code, headers, content type
    /// and body back to its web stack, then disposes the response (which disposes <see cref="BodyStream"/>).
    /// </summary>
    public sealed class HubResponse : IDisposable
    {
        public HubResponse(int statusCode)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }

        /// <summary>Response headers other than Content-Type and Content-Length.</summary>
        public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Content-Type of the body, or null when there is none.</summary>
        public string? ContentType { get; set; }

        /// <summary>A small body (JSON), or null. At most one of <see cref="Body"/> and <see cref="BodyStream"/> is set.</summary>
        public byte[]? Body { get; set; }

        /// <summary>A streamed body (the payload), or null. Read <see cref="ContentLength"/> bytes from its current position.</summary>
        public Stream? BodyStream { get; set; }

        /// <summary>Number of body bytes.</summary>
        public long ContentLength => Body?.LongLength ?? _streamLength;

        private long _streamLength;

        internal static HubResponse Json<T>(int status, T value)
        {
            return new HubResponse(status)
            {
                ContentType = RemoteProtocol.JsonContentType,
                Body = JsonSerializer.SerializeToUtf8Bytes(value, RemoteProtocol.Json)
            };
        }

        internal static HubResponse Error(int status, string message) => Json(status, new ErrorResponse { Error = message });

        internal static HubResponse Stream(int status, Stream body, long length, string contentType)
        {
            return new HubResponse(status) { BodyStream = body, ContentType = contentType, _streamLength = length };
        }

        /// <summary>Copies the body to <paramref name="destination"/> (either kind).</summary>
        public void CopyBodyTo(Stream destination)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            if (Body is not null) { destination.Write(Body, 0, Body.Length); return; }
            if (BodyStream is null) return;
            byte[] buffer = new byte[81920];
            long remaining = _streamLength;
            while (remaining > 0)
            {
                int read = BodyStream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) break;
                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        /// <summary>Copies the body to <paramref name="destination"/> asynchronously (either kind).</summary>
        public async System.Threading.Tasks.Task CopyBodyToAsync(Stream destination, System.Threading.CancellationToken cancellationToken = default)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            if (Body is not null) { await destination.WriteAsync(Body, cancellationToken).ConfigureAwait(false); return; }
            if (BodyStream is null) return;
            byte[] buffer = new byte[81920];
            long remaining = _streamLength;
            while (remaining > 0)
            {
                int read = await BodyStream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
        }

        public void Dispose() => BodyStream?.Dispose();
    }

    /// <summary>Raised by <see cref="PrintHub.JobFinished"/> once per job, when it reaches a final state.</summary>
    public sealed class HubJobFinishedEventArgs : EventArgs
    {
        public HubJobFinishedEventArgs(StoredJob job, PrintAuditEvent audit)
        {
            Job = job;
            Audit = audit;
        }

        /// <summary>The job in its final state (a copy).</summary>
        public StoredJob Job { get; }

        /// <summary>The job's audit event, as recorded in the hub's audit sink.</summary>
        public PrintAuditEvent Audit { get; }
    }
}
