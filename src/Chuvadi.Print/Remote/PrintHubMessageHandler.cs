using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Print.Remote
{
    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that delivers requests straight to an in-process
    /// <see cref="PrintHub"/>: give it to a <see cref="PrintAgent"/> (or any HttpClient) to run hub and agent
    /// in one process without a network, for tests, demos and single-machine setups. It is also the reference
    /// for mapping HTTP onto <see cref="PrintHub.HandleAsync"/>.
    /// </summary>
    public sealed class PrintHubMessageHandler : HttpMessageHandler
    {
        private readonly PrintHub _hub;
        private readonly string _basePath;

        /// <param name="hub">The hub.</param>
        /// <param name="baseAddress">The hub address the agent uses (e.g. https://hub.local/print/); only its path matters.</param>
        public PrintHubMessageHandler(PrintHub hub, Uri baseAddress)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            if (baseAddress is null) throw new ArgumentNullException(nameof(baseAddress));
            string path = baseAddress.AbsolutePath;
            _basePath = path.EndsWith('/') ? path : path + "/";
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null) throw new ArgumentException("The request has no URI.", nameof(request));
            string path = request.RequestUri.AbsolutePath;
            if (!path.StartsWith(_basePath, StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };

            var hubRequest = new HubRequest(request.Method.Method, path.Substring(_basePath.Length));
            foreach (KeyValuePair<string, IEnumerable<string>> h in request.Headers)
                hubRequest.Headers[h.Key] = string.Join(",", h.Value);
            if (request.Content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> h in request.Content.Headers)
                    hubRequest.Headers[h.Key] = string.Join(",", h.Value);
                hubRequest.Body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }

            using HubResponse hubResponse = await _hub.HandleAsync(hubRequest, cancellationToken).ConfigureAwait(false);
            var response = new HttpResponseMessage((HttpStatusCode)hubResponse.StatusCode) { RequestMessage = request };
            var body = new MemoryStream();
            await hubResponse.CopyBodyToAsync(body, cancellationToken).ConfigureAwait(false);
            response.Content = new ByteArrayContent(body.ToArray());
            if (hubResponse.ContentType is not null)
                response.Content.Headers.TryAddWithoutValidation("Content-Type", hubResponse.ContentType);
            foreach (KeyValuePair<string, string> h in hubResponse.Headers)
            {
                if (!response.Headers.TryAddWithoutValidation(h.Key, h.Value))
                    response.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            return response;
        }
    }
}
