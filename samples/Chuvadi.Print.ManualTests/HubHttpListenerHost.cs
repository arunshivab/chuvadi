using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Print.Remote;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// Serves a <see cref="PrintHub"/> over real HTTP with <see cref="HttpListener"/> on localhost (a free
    /// port), so the agent is tested across an actual socket. It is also a minimal example of hosting the hub
    /// without ASP.NET Core. Records each request line and can cut a payload download short to test resume.
    /// </summary>
    public sealed class HubHttpListenerHost : IDisposable
    {
        private readonly PrintHub _hub;
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly object _gate = new();
        private readonly List<string> _log = new();

        public HubHttpListenerHost(PrintHub hub)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            int port = FreePort();
            BaseUri = new Uri("http://localhost:" + port + "/print-hub/");
            _listener.Prefixes.Add(BaseUri.AbsoluteUri);
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        /// <summary>The hub's base address for agents.</summary>
        public Uri BaseUri { get; }

        /// <summary>
        /// When set, the next payload response stops after this many body bytes and the connection is aborted
        /// (then the setting clears itself).
        /// </summary>
        public long? CutNextPayloadAfter { get; set; }

        /// <summary>Request lines seen ("GET jobs/.../payload Range=bytes=100-").</summary>
        public IReadOnlyList<string> Requests
        {
            get { lock (_gate) return _log.ToArray(); }
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) when (_stop.IsCancellationRequested) { return; }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            HttpListenerRequest req = context.Request;
            HttpListenerResponse res = context.Response;
            try
            {
                string path = req.Url!.AbsolutePath;
                string relative = path.StartsWith(BaseUri.AbsolutePath, StringComparison.Ordinal)
                    ? path.Substring(BaseUri.AbsolutePath.Length) : path;
                var hubRequest = new HubRequest(req.HttpMethod, relative);
                foreach (string? name in req.Headers.AllKeys)
                    if (name is not null) hubRequest.Headers[name] = req.Headers[name] ?? string.Empty;
                if (req.HasEntityBody)
                {
                    using var ms = new MemoryStream();
                    await req.InputStream.CopyToAsync(ms).ConfigureAwait(false);
                    hubRequest.Body = ms.ToArray();
                }
                lock (_gate)
                    _log.Add(req.HttpMethod + " " + relative + (req.Headers["Range"] is string r ? " Range=" + r : ""));

                using HubResponse hub = await _hub.HandleAsync(hubRequest, _stop.Token).ConfigureAwait(false);
                res.StatusCode = hub.StatusCode;
                foreach (KeyValuePair<string, string> h in hub.Headers) res.Headers[h.Key] = h.Value;
                if (hub.ContentType is not null) res.ContentType = hub.ContentType;
                res.ContentLength64 = hub.ContentLength;

                long? cut = null;
                if (hub.BodyStream is not null && relative.EndsWith("/payload", StringComparison.Ordinal))
                {
                    lock (_gate)
                    {
                        cut = CutNextPayloadAfter;
                        CutNextPayloadAfter = null;
                    }
                }
                if (cut is long limit && limit < hub.ContentLength)
                {
                    byte[] buffer = new byte[limit];
                    int got = 0;
                    while (got < limit)
                    {
                        int n = hub.BodyStream!.Read(buffer, got, (int)limit - got);
                        if (n == 0) break;
                        got += n;
                    }
                    await res.OutputStream.WriteAsync(buffer.AsMemory(0, got)).ConfigureAwait(false);
                    await res.OutputStream.FlushAsync().ConfigureAwait(false);
                    res.Abort();
                    return;
                }
                await hub.CopyBodyToAsync(res.OutputStream).ConfigureAwait(false);
                res.Close();
            }
            catch (OperationCanceledException)
            {
                try { res.Abort(); } catch (Exception) { }
            }
            catch (Exception)
            {
                try
                {
                    res.StatusCode = 500;
                    res.Close();
                }
                catch (Exception) { try { res.Abort(); } catch (Exception) { } }
            }
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            _listener.Close();
            _stop.Dispose();
        }
    }
}
