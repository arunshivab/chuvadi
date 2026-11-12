using System;
using System.Collections.Generic;
using System.Threading;

namespace Chuvadi.Print.Ipp
{
    /// <summary>Where <see cref="PrinterListing.List"/> looks for printers.</summary>
    public sealed class PrinterListingOptions
    {
        /// <summary>
        /// List the local CUPS server's queues (<see cref="CupsQueues.LocalServer"/>). Default: true except on Windows,
        /// which has no CUPS.
        /// </summary>
        public bool IncludeLocalServer { get; set; } = !OperatingSystem.IsWindows();

        /// <summary>
        /// List the queues of the CUPS server this computer is set up to use (<see cref="CupsQueues.ConfiguredServer()"/>),
        /// when one is set up. Default: true.
        /// </summary>
        public bool IncludeConfiguredServer { get; set; } = true;

        /// <summary>
        /// More CUPS servers to list, as IPP URIs ("ipp://print-server:631/") or addresses ("print-server",
        /// "print-server:631"). Listed after the local and configured servers, in this order.
        /// </summary>
        public IList<string> CupsServers { get; } = new List<string>();

        /// <summary>Also find printers on the network with DNS-SD (<see cref="NetworkPrinterDiscovery.Browse"/>). Default: true.</summary>
        public bool BrowseNetwork { get; set; } = true;

        /// <summary>How the network is browsed (duration, service types). Null: the discovery defaults.</summary>
        public NetworkPrinterDiscoveryOptions? Discovery { get; set; }

        /// <summary>Client options (timeouts, TLS) for talking to the CUPS servers. Null: the defaults.</summary>
        public IppClientOptions? ClientOptions { get; set; }
    }

    /// <summary>One CUPS queue in a <see cref="ListedPrinter"/>, with the server it was listed from.</summary>
    public sealed class ListedQueue
    {
        internal ListedQueue(string server, CupsQueue queue)
        {
            Server = server;
            Queue = queue;
        }

        /// <summary>The CUPS server's IPP URI.</summary>
        public string Server { get; }

        /// <summary>The queue as the server described it.</summary>
        public CupsQueue Queue { get; }

        public override string ToString() => Queue.Name + " on " + Server;
    }

    /// <summary>
    /// One printer in a <see cref="PrinterListing"/>: every CUPS queue and every DNS-SD advertisement found for it.
    /// </summary>
    public sealed class ListedPrinter
    {
        internal ListedPrinter(IReadOnlyList<ListedQueue> queues, IReadOnlyList<DiscoveredPrinter> discovered)
        {
            Queues = queues;
            Discovered = discovered;
            if (queues.Count > 0)
            {
                CupsQueue q = queues[0].Queue;
                Name = q.Name;
                Uri = q.Uri;
            }
            else
            {
                DiscoveredPrinter d = discovered[0];
                Name = d.InstanceName;
                Uri = FirstUri(discovered);
            }
            Uuid = FirstUuid(queues, discovered);
            Location = FirstText(queues, discovered, q => q.Location, d => d.Location);
            MakeAndModel = FirstText(queues, discovered, q => q.MakeAndModel, d => d.MakeAndModel);
        }

        /// <summary>The first CUPS queue's name, or the DNS-SD instance name when no CUPS queue was found for it.</summary>
        public string Name { get; }

        /// <summary>
        /// Where to print: the first CUPS queue's URI (so CUPS converts and spools the job), or the URI of a DNS-SD
        /// advertisement when no CUPS queue was found for it. Null when none is known.
        /// </summary>
        public string? Uri { get; }

        /// <summary>The printer's UUID ("urn:uuid:..." from CUPS, or the DNS-SD UUID key), when one was reported.</summary>
        public string? Uuid { get; }

        public string? Location { get; }
        public string? MakeAndModel { get; }

        /// <summary>True when at least one CUPS queue was found for the printer.</summary>
        public bool IsCupsQueue => Queues.Count > 0;

        /// <summary>
        /// Every CUPS queue found for the printer, in the order the servers were listed (the local server first). One
        /// printer can have more than one queue, for example a queue added by hand and one CUPS created by itself.
        /// </summary>
        public IReadOnlyList<ListedQueue> Queues { get; }

        /// <summary>Every DNS-SD advertisement found for the printer (one per service type it offers).</summary>
        public IReadOnlyList<DiscoveredPrinter> Discovered { get; }

        public override string ToString() => Name + " (" + (Uri ?? "no address") + ")";

        private static string? FirstUri(IReadOnlyList<DiscoveredPrinter> discovered)
        {
            foreach (DiscoveredPrinter d in discovered) if (d.Uri is not null) return d.Uri;
            return null;
        }

        private static string? FirstUuid(IReadOnlyList<ListedQueue> queues, IReadOnlyList<DiscoveredPrinter> discovered)
        {
            foreach (ListedQueue q in queues) if (!string.IsNullOrEmpty(q.Queue.Uuid)) return q.Queue.Uuid;
            foreach (DiscoveredPrinter d in discovered)
                if (d.Txt.TryGetValue("uuid", out string? u) && !string.IsNullOrEmpty(u)) return u;
            return null;
        }

        private static string? FirstText(IReadOnlyList<ListedQueue> queues, IReadOnlyList<DiscoveredPrinter> discovered,
                                         Func<CupsQueue, string?> fromQueue, Func<DiscoveredPrinter, string?> fromDiscovered)
        {
            foreach (ListedQueue q in queues)
            {
                string? v = fromQueue(q.Queue);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            foreach (DiscoveredPrinter d in discovered)
            {
                string? v = fromDiscovered(d);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return null;
        }
    }

    /// <summary>A place <see cref="PrinterListing.List"/> could not list, and why.</summary>
    public sealed class PrinterListingProblem
    {
        internal PrinterListingProblem(string source, Exception error)
        {
            Source = source;
            Error = error;
        }

        /// <summary>The CUPS server's URI, or "dns-sd" for network browsing.</summary>
        public string Source { get; }

        /// <summary>What went wrong.</summary>
        public Exception Error { get; }

        public override string ToString() => Source + ": " + Error.Message;
    }

    /// <summary>What <see cref="PrinterListing.List"/> found.</summary>
    public sealed class PrinterListingResult
    {
        internal PrinterListingResult(IReadOnlyList<ListedPrinter> printers, IReadOnlyList<string> servers,
                                      IReadOnlyList<PrinterListingProblem> problems)
        {
            Printers = printers;
            Servers = servers;
            Problems = problems;
        }

        /// <summary>One entry per printer, CUPS printers first (in server order), then printers found only by DNS-SD.</summary>
        public IReadOnlyList<ListedPrinter> Printers { get; }

        /// <summary>The CUPS servers that were asked, in order.</summary>
        public IReadOnlyList<string> Servers { get; }

        /// <summary>The servers (or the network browse) that could not be listed. Empty when everything answered.</summary>
        public IReadOnlyList<PrinterListingProblem> Problems { get; }
    }

    /// <summary>
    /// One list of printers from the local CUPS server, the CUPS server this computer is set up to use, any other CUPS
    /// servers, and DNS-SD browsing, with each printer listed once.
    /// </summary>
    /// <remarks>
    /// <para>Entries are the same printer when they share a UUID (a queue's printer-uuid, the "uuid" in a CUPS
    /// "dnssd://" device URI, or the DNS-SD "UUID" key) or a URI (a queue's device URI or own URI, or a DNS-SD
    /// advertisement's URI; compared without case, a trailing slash or the default port).</para>
    /// <para>When a printer is found both as a CUPS queue and by DNS-SD, the CUPS queue comes first (it has the
    /// queue name, and CUPS converts the job for the printer); the DNS-SD details stay in
    /// <see cref="ListedPrinter.Discovered"/>.</para>
    /// <para>A server or browse that fails is reported in <see cref="PrinterListingResult.Problems"/> and the rest is
    /// still listed.</para>
    /// </remarks>
    public static class PrinterListing
    {
        /// <summary>The <see cref="PrinterListingProblem.Source"/> for network browsing.</summary>
        public const string DnsSdSource = "dns-sd";

        /// <summary>Lists the printers (see the class remarks).</summary>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public static PrinterListingResult List(PrinterListingOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new PrinterListingOptions();
            string? configured = options.IncludeConfiguredServer ? CupsQueues.ConfiguredServer() : null;
            return ListFrom(options, configured, cancellationToken);
        }

        /// <summary>The same as <see cref="List(PrinterListingOptions?, CancellationToken)"/>, with the configured server given (tests).</summary>
        internal static PrinterListingResult ListFrom(PrinterListingOptions options, string? configuredServer, CancellationToken cancellationToken)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            var servers = new List<string>();
            var seenServers = new HashSet<string>(StringComparer.Ordinal);
            void AddServer(string server)
            {
                string uri = CupsQueues.ToServerUri(server.Trim());
                if (seenServers.Add(UriKey(uri) ?? uri)) servers.Add(uri);
            }
            if (options.IncludeLocalServer) AddServer(CupsQueues.LocalServer);
            if (configuredServer is not null) AddServer(configuredServer);
            foreach (string server in options.CupsServers)
            {
                if (string.IsNullOrWhiteSpace(server)) throw new ArgumentException("A CUPS server is empty.", nameof(options));
                AddServer(server);
            }

            var problems = new List<PrinterListingProblem>();
            var queues = new List<(string Server, IReadOnlyList<CupsQueue> Queues)>();
            foreach (string server in servers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    queues.Add((server, CupsQueues.List(server, options.ClientOptions, cancellationToken)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    problems.Add(new PrinterListingProblem(server, ex));
                }
            }

            IReadOnlyList<DiscoveredPrinter> discovered = Array.Empty<DiscoveredPrinter>();
            if (options.BrowseNetwork)
            {
                try
                {
                    discovered = NetworkPrinterDiscovery.Browse(options.Discovery, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    problems.Add(new PrinterListingProblem(DnsSdSource, ex));
                }
            }

            return new PrinterListingResult(Merge(queues, discovered), servers, problems);
        }

        /// <summary>Groups the queues and advertisements into printers (see the class remarks).</summary>
        internal static IReadOnlyList<ListedPrinter> Merge(IReadOnlyList<(string Server, IReadOnlyList<CupsQueue> Queues)> queues,
                                                           IReadOnlyList<DiscoveredPrinter> discovered)
        {
            var queueItems = new List<ListedQueue>();
            foreach (var (server, list) in queues) foreach (CupsQueue q in list) queueItems.Add(new ListedQueue(server, q));
            int count = queueItems.Count + discovered.Count;

            // Union-find over every entry: entries that share any key are one printer.
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            int Find(int i)
            {
                while (parent[i] != i) i = parent[i] = parent[parent[i]];
                return i;
            }
            var owner = new Dictionary<string, int>(StringComparer.Ordinal);
            void Claim(string? key, int item)
            {
                if (key is null) return;
                if (owner.TryGetValue(key, out int other))
                {
                    int a = Find(other), b = Find(item);
                    if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); // the earlier entry stays the root
                }
                else
                {
                    owner[key] = item;
                }
            }

            for (int i = 0; i < queueItems.Count; i++)
            {
                CupsQueue q = queueItems[i].Queue;
                Claim(UuidKey(q.Uuid), i);
                Claim(UuidKey(DnsSdUuid(q.DeviceUri)), i);
                Claim(UriKey(q.DeviceUri), i);
                Claim(UriKey(q.Uri), i);
            }
            for (int j = 0; j < discovered.Count; j++)
            {
                DiscoveredPrinter d = discovered[j];
                int item = queueItems.Count + j;
                Claim(UuidKey(d.Txt.TryGetValue("uuid", out string? u) ? u : null), item);
                Claim(UriKey(d.Uri), item);
            }

            var groups = new Dictionary<int, (List<ListedQueue> Queues, List<DiscoveredPrinter> Discovered)>();
            var order = new List<int>();
            for (int i = 0; i < count; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var group))
                {
                    group = (new List<ListedQueue>(), new List<DiscoveredPrinter>());
                    groups[root] = group;
                    order.Add(root);
                }
                if (i < queueItems.Count) group.Queues.Add(queueItems[i]);
                else group.Discovered.Add(discovered[i - queueItems.Count]);
            }

            var printers = new List<ListedPrinter>(order.Count);
            foreach (int root in order)
            {
                var (q, d) = groups[root];
                printers.Add(new ListedPrinter(q, d));
            }
            return printers;
        }

        /// <summary>A UUID compared without its "urn:uuid:" prefix, braces or case; null for none.</summary>
        internal static string? UuidKey(string? uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return null;
            string v = uuid.Trim();
            if (v.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase)) v = v.Substring(9);
            v = v.Trim('{', '}').ToLowerInvariant();
            return v.Length == 0 ? null : "uuid:" + v;
        }

        /// <summary>The "uuid" query value of a CUPS "dnssd://" device URI; null for other URIs.</summary>
        internal static string? DnsSdUuid(string? deviceUri)
        {
            if (deviceUri is null || !deviceUri.StartsWith("dnssd://", StringComparison.OrdinalIgnoreCase)) return null;
            int query = deviceUri.IndexOf('?');
            if (query < 0) return null;
            foreach (string pair in deviceUri.Substring(query + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && string.Equals(pair.Substring(0, eq), "uuid", StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
            return null;
        }

        /// <summary>
        /// A URI compared without case, a trailing slash, the query or the default port (631 for ipp and ipps, 9100 for
        /// socket); null for none.
        /// </summary>
        internal static string? UriKey(string? uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return null;
            if (!System.Uri.TryCreate(uri.Trim(), UriKind.Absolute, out Uri? parsed)) return "uri:" + uri.Trim().ToLowerInvariant();
            string scheme = parsed.Scheme.ToLowerInvariant();
            int defaultPort = scheme switch
            {
                "ipp" or "ipps" => CupsQueues.DefaultPort,
                "socket" => 9100,
                "http" => 80,
                "https" => 443,
                _ => -1
            };
            string port = parsed.Port < 0 || parsed.Port == defaultPort ? string.Empty : ":" + parsed.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string path = Uri.UnescapeDataString(parsed.AbsolutePath).TrimEnd('/').ToLowerInvariant();
            return "uri:" + scheme + "://" + parsed.Host.ToLowerInvariant() + port + path;
        }
    }
}
