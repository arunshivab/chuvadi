using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace Chuvadi.Print.Ipp
{
    /// <summary>Thrown when an IPP request fails (transport error or a non-successful status).</summary>
    public sealed class IppException : Exception
    {
        public IppException(string message, short statusCode = -1, IppMessage? response = null, Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            Response = response;
        }

        /// <summary>The IPP status code, or -1 when no IPP response was received.</summary>
        public short StatusCode { get; }

        /// <summary>The printer's response, when there was one.</summary>
        public IppMessage? Response { get; }
    }

    /// <summary>Connection options for <see cref="IppClient"/>.</summary>
    public sealed class IppClientOptions
    {
        /// <summary>requesting-user-name sent with every request. Default: the OS user name.</summary>
        public string? UserName { get; set; } = Environment.UserName;

        /// <summary>attributes-natural-language. Default "en".</summary>
        public string NaturalLanguage { get; set; } = "en";

        /// <summary>IPP version sent. Default 2.0 (IPP Everywhere); some older printers need 1.1.</summary>
        public Version IppVersion { get; set; } = new(2, 0);

        /// <summary>HTTP timeout per request. Default 5 minutes (large documents on slow printers).</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>HTTP Basic/Digest credentials, when the printer requires authentication.</summary>
        public ICredentials? Credentials { get; set; }

        /// <summary>
        /// Accept any TLS certificate for ipps:// and https:// printers. Printers commonly use self-signed
        /// certificates; enable only on trusted networks. Default false.
        /// </summary>
        public bool AcceptAnyServerCertificate { get; set; }
    }

    /// <summary>
    /// A minimal, complete IPP/1.1-2.x client over HTTP (System.Net.Http only). Accepts ipp://, ipps://,
    /// http:// and https:// printer URIs (ipp and ipps default to port 631).
    /// </summary>
    public sealed class IppClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private int _requestId;

        public IppClient(string printerUri, IppClientOptions? options = null)
            : this(printerUri, options ?? new IppClientOptions(), null)
        {
        }

        /// <summary>Uses a caller-supplied HttpClient (not disposed by this client).</summary>
        public IppClient(string printerUri, HttpClient httpClient, IppClientOptions? options = null)
            : this(printerUri, options ?? new IppClientOptions(), httpClient ?? throw new ArgumentNullException(nameof(httpClient)))
        {
        }

        private IppClient(string printerUri, IppClientOptions options, HttpClient? httpClient)
        {
            if (printerUri is null) throw new ArgumentNullException(nameof(printerUri));
            Options = options;
            PrinterUri = printerUri;
            HttpUri = ToHttpUri(printerUri);
            if (httpClient is null)
            {
                var handler = new SocketsHttpHandler { Credentials = options.Credentials, PreAuthenticate = options.Credentials is not null };
                if (options.AcceptAnyServerCertificate)
                    handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                _http = new HttpClient(handler, disposeHandler: true) { Timeout = options.Timeout };
                _ownsHttp = true;
            }
            else
            {
                _http = httpClient;
            }
        }

        /// <summary>The printer URI as given (sent as printer-uri).</summary>
        public string PrinterUri { get; }

        /// <summary>The HTTP endpoint requests are posted to.</summary>
        public Uri HttpUri { get; }

        public IppClientOptions Options { get; }

        /// <summary>Maps ipp://host/path to http://host:631/path and ipps:// to https://host:631/path.</summary>
        public static Uri ToHttpUri(string printerUri)
        {
            if (!Uri.TryCreate(printerUri, UriKind.Absolute, out Uri? uri))
                throw new ArgumentException("Not an absolute URI: " + printerUri, nameof(printerUri));
            string scheme = uri.Scheme.ToLowerInvariant();
            switch (scheme)
            {
                case "http":
                case "https":
                    return uri;
                case "ipp":
                case "ipps":
                {
                    var builder = new UriBuilder(uri)
                    {
                        Scheme = scheme == "ipp" ? "http" : "https",
                        Port = uri.IsDefaultPort || uri.Port < 0 ? 631 : uri.Port
                    };
                    return builder.Uri;
                }
                default:
                    throw new ArgumentException("Unsupported printer URI scheme: " + uri.Scheme, nameof(printerUri));
            }
        }

        /// <summary>A request with the standard operation attributes filled in.</summary>
        public IppMessage CreateRequest(IppOperation operation)
        {
            IppMessage request = IppMessage.CreateRequest(operation, Interlocked.Increment(ref _requestId), PrinterUri,
                                                          Options.UserName, Options.NaturalLanguage);
            request.VersionMajor = (byte)Options.IppVersion.Major;
            request.VersionMinor = (byte)Math.Max(0, Options.IppVersion.Minor);
            return request;
        }

        /// <summary>Sends a request (and optional document data) and returns the response, throwing on a non-successful status.</summary>
        public IppMessage Send(IppMessage request, Stream? document = null, CancellationToken cancellationToken = default)
        {
            IppMessage response = SendRaw(request, document, cancellationToken);
            if (!response.IsSuccess)
            {
                string? detail = response.Group(IppTag.OperationAttributes)?["status-message"]?.Value.AsString();
                throw new IppException("IPP " + request.Operation + " failed: " + response.StatusCode
                                       + " (0x" + ((ushort)response.Code).ToString("X4", CultureInfo.InvariantCulture) + ")"
                                       + (detail is null ? string.Empty : " - " + detail), response.Code, response);
            }
            return response;
        }

        /// <summary>Sends a request and returns the response whatever its status.</summary>
        public IppMessage SendRaw(IppMessage request, Stream? document = null, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            byte[] head = request.ToArray();
            var body = new IppRequestContent(head, document);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, HttpUri) { Content = body };
            try
            {
                using HttpResponseMessage httpResponse = _http.Send(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
                if (!httpResponse.IsSuccessStatusCode)
                    throw new IppException("HTTP " + (int)httpResponse.StatusCode + " " + httpResponse.ReasonPhrase + " from " + HttpUri);
                using Stream stream = httpResponse.Content.ReadAsStream(cancellationToken);
                return IppMessage.ReadFrom(stream, out _);
            }
            catch (HttpRequestException ex)
            {
                throw new IppException("Could not reach the printer at " + HttpUri + ": " + ex.Message, innerException: ex);
            }
            catch (System.Threading.Tasks.TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IppException("Timed out waiting for " + HttpUri, innerException: ex);
            }
            catch (IppFormatException ex)
            {
                throw new IppException("The printer sent an invalid IPP response: " + ex.Message, innerException: ex);
            }
        }

        // ------------------------------------------------------------------ operations

        /// <summary>Get-Printer-Attributes. <paramref name="requested"/> null = "all" plus "media-col-database".</summary>
        public IppMessage GetPrinterAttributes(IEnumerable<string>? requested = null, CancellationToken cancellationToken = default)
        {
            IppMessage request = CreateRequest(IppOperation.GetPrinterAttributes);
            var values = new List<IppValue>();
            foreach (string name in requested ?? new[] { "all", "media-col-database" }) values.Add(IppValue.Keyword(name));
            request.Group(IppTag.OperationAttributes)!.Add(new IppAttribute("requested-attributes", values));
            return Send(request, null, cancellationToken);
        }

        /// <summary>Print-Job: sends one document with job template attributes; returns the job id.</summary>
        public int PrintJob(Stream document, string documentFormat, IppAttributeGroup? jobAttributes = null,
                            string? jobName = null, CancellationToken cancellationToken = default)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            IppMessage request = CreateRequest(IppOperation.PrintJob);
            AddDocumentOperationAttributes(request, documentFormat, jobName);
            AddJobGroup(request, jobAttributes);
            return JobId(Send(request, document, cancellationToken));
        }

        /// <summary>Validate-Job: asks whether the printer would accept these attributes, without printing.</summary>
        public IppMessage ValidateJob(string documentFormat, IppAttributeGroup? jobAttributes = null, CancellationToken cancellationToken = default)
        {
            IppMessage request = CreateRequest(IppOperation.ValidateJob);
            AddDocumentOperationAttributes(request, documentFormat, null);
            AddJobGroup(request, jobAttributes);
            return SendRaw(request, null, cancellationToken);
        }

        /// <summary>Create-Job: creates an empty job for later Send-Document calls; returns the job id.</summary>
        public int CreateJob(IppAttributeGroup? jobAttributes = null, string? jobName = null, CancellationToken cancellationToken = default)
        {
            IppMessage request = CreateRequest(IppOperation.CreateJob);
            if (!string.IsNullOrEmpty(jobName)) request.Group(IppTag.OperationAttributes)!.Add("job-name", IppValue.Name(jobName));
            AddJobGroup(request, jobAttributes);
            return JobId(Send(request, null, cancellationToken));
        }

        /// <summary>Send-Document: adds a document to a job created with <see cref="CreateJob"/>.</summary>
        public IppMessage SendDocument(int jobId, Stream document, string documentFormat, bool lastDocument,
                                       CancellationToken cancellationToken = default)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            IppMessage request = CreateRequest(IppOperation.SendDocument);
            IppAttributeGroup op = request.Group(IppTag.OperationAttributes)!;
            op.Add("job-id", IppValue.Integer(jobId));
            op.Add("document-format", IppValue.MimeMediaType(documentFormat));
            op.Add("last-document", IppValue.Boolean(lastDocument));
            return Send(request, document, cancellationToken);
        }

        public IppMessage GetJobAttributes(int jobId, IEnumerable<string>? requested = null, CancellationToken cancellationToken = default)
        {
            IppMessage request = CreateRequest(IppOperation.GetJobAttributes);
            IppAttributeGroup op = request.Group(IppTag.OperationAttributes)!;
            op.Add("job-id", IppValue.Integer(jobId));
            if (requested is not null)
            {
                var values = new List<IppValue>();
                foreach (string name in requested) values.Add(IppValue.Keyword(name));
                if (values.Count > 0) op.Add(new IppAttribute("requested-attributes", values));
            }
            return Send(request, null, cancellationToken);
        }

        /// <summary>The job's current state (job-state).</summary>
        public IppJobState GetJobState(int jobId, CancellationToken cancellationToken = default)
        {
            IppMessage response = GetJobAttributes(jobId, new[] { "job-state" }, cancellationToken);
            IppAttribute state = response.Group(IppTag.JobAttributes)?["job-state"]
                                 ?? throw new IppException("The printer did not report job-state.", response.Code, response);
            return (IppJobState)state.Value.AsInteger();
        }

        public IppMessage CancelJob(int jobId, CancellationToken cancellationToken = default)
        {
            IppMessage request = CreateRequest(IppOperation.CancelJob);
            request.Group(IppTag.OperationAttributes)!.Add("job-id", IppValue.Integer(jobId));
            return Send(request, null, cancellationToken);
        }

        /// <summary>Get-Jobs: "not-completed" (default) or "completed".</summary>
        public IppMessage GetJobs(string whichJobs = "not-completed", bool myJobs = false, CancellationToken cancellationToken = default)
        {
            IppMessage request = CreateRequest(IppOperation.GetJobs);
            IppAttributeGroup op = request.Group(IppTag.OperationAttributes)!;
            op.Add("which-jobs", IppValue.Keyword(whichJobs));
            op.Add("my-jobs", IppValue.Boolean(myJobs));
            return Send(request, null, cancellationToken);
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        // ------------------------------------------------------------------ helpers

        private void AddDocumentOperationAttributes(IppMessage request, string documentFormat, string? jobName)
        {
            if (string.IsNullOrEmpty(documentFormat)) throw new ArgumentException("A document format is required.", nameof(documentFormat));
            IppAttributeGroup op = request.Group(IppTag.OperationAttributes)!;
            if (!string.IsNullOrEmpty(jobName)) op.Add("job-name", IppValue.Name(jobName));
            op.Add("document-format", IppValue.MimeMediaType(documentFormat));
        }

        private static void AddJobGroup(IppMessage request, IppAttributeGroup? jobAttributes)
        {
            if (jobAttributes is null || jobAttributes.Attributes.Count == 0) return;
            IppAttributeGroup job = request.AddGroup(IppTag.JobAttributes);
            foreach (IppAttribute a in jobAttributes.Attributes) job.Add(a);
        }

        private static int JobId(IppMessage response)
        {
            IppAttribute? id = response.Group(IppTag.JobAttributes)?["job-id"];
            if (id is null) throw new IppException("The printer accepted the job but returned no job-id.", response.Code, response);
            return id.Value.AsInteger();
        }

        /// <summary>Request body: the encoded IPP message followed by the document, streamed.</summary>
        private sealed class IppRequestContent : HttpContent
        {
            private readonly byte[] _head;
            private readonly Stream? _document;

            public IppRequestContent(byte[] head, Stream? document)
            {
                _head = head;
                _document = document;
                Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
            }

            protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            {
                stream.Write(_head, 0, _head.Length);
                _document?.CopyTo(stream);
            }

            protected override System.Threading.Tasks.Task SerializeToStreamAsync(Stream stream, TransportContext? context)
                => SerializeToStreamAsync(stream, context, CancellationToken.None);

            protected override async System.Threading.Tasks.Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            {
                await stream.WriteAsync(_head, cancellationToken).ConfigureAwait(false);
                if (_document is not null) await _document.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            protected override bool TryComputeLength(out long length)
            {
                if (_document is null)
                {
                    length = _head.Length;
                    return true;
                }
                if (_document.CanSeek)
                {
                    length = _head.Length + (_document.Length - _document.Position);
                    return true;
                }
                length = 0;
                return false;
            }
        }

    }
}
