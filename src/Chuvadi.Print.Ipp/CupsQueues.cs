using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Chuvadi.Print.Ipp
{
    /// <summary>One print queue on a CUPS server (Linux, macOS), as CUPS-Get-Printers reports it.</summary>
    public sealed class CupsQueue
    {
        internal CupsQueue(IppAttributeGroup attributes)
        {
            Attributes = attributes;
            Name = Text(attributes, "printer-name") ?? string.Empty;
            Uri = Text(attributes, "printer-uri-supported");
            Info = Text(attributes, "printer-info");
            Location = Text(attributes, "printer-location");
            MakeAndModel = Text(attributes, "printer-make-and-model");
            IppAttribute? state = attributes["printer-state"];
            State = state is not null && state.Value.Value is int s ? (IppPrinterState)s : null;
            IsAcceptingJobs = Bool(attributes, "printer-is-accepting-jobs") ?? false;
            IsShared = Bool(attributes, "printer-is-shared") ?? false;
            var formats = new List<string>();
            IppAttribute? df = attributes["document-format-supported"];
            if (df is not null) foreach (IppValue v in df.Values) if (v.Value is string f) formats.Add(f);
            DocumentFormats = formats;
            IppAttribute? type = attributes["printer-type"];
            PrinterType = type is not null && type.Value.Value is int t ? t : null;
            Uuid = Text(attributes, "printer-uuid");
            DeviceUri = Text(attributes, "device-uri");
        }

        /// <summary>Every attribute the server returned for the queue.</summary>
        public IppAttributeGroup Attributes { get; }

        /// <summary>The queue name (printer-name).</summary>
        public string Name { get; }

        /// <summary>The queue's IPP URI (the first printer-uri-supported value), for IppRoutes.CreateCupsRouter.</summary>
        public string? Uri { get; }

        public string? Info { get; }
        public string? Location { get; }
        public string? MakeAndModel { get; }
        public IppPrinterState? State { get; }
        public bool IsAcceptingJobs { get; }
        public bool IsShared { get; }
        public IReadOnlyList<string> DocumentFormats { get; }

        /// <summary>CUPS' printer-type bits for the queue; null when the server did not send them.</summary>
        public int? PrinterType { get; }

        /// <summary>
        /// True when the queue is a remote one, shared by another CUPS server (bit 0x2, CUPS_PRINTER_REMOTE, of
        /// <see cref="PrinterType"/>); false when it is local or the server did not say.
        /// </summary>
        public bool IsRemote => PrinterType is int t && (t & 0x2) != 0;

        /// <summary>The queue's printer-uuid ("urn:uuid:..."); null when the server did not send it.</summary>
        public string? Uuid { get; }

        /// <summary>
        /// Where CUPS sends the queue's jobs (device-uri), for example "ipp://192.0.2.5/ipp/print" or a "dnssd://" URI;
        /// null when the server did not send it (CUPS sends it only to an administrator on some systems).
        /// </summary>
        public string? DeviceUri { get; }

        private static string? Text(IppAttributeGroup g, string name)
        {
            IppAttribute? a = g[name];
            return a is null || a.Values.Count == 0 ? null : a.Value.Value as string;
        }

        private static bool? Bool(IppAttributeGroup g, string name)
        {
            IppAttribute? a = g[name];
            return a is not null && a.Value.Value is bool b ? b : null;
        }
    }

    /// <summary>
    /// Lists the print queues of a CUPS server over IPP with the CUPS-Get-Printers operation (D-94, Linux and macOS).
    /// The protocol is described in CUPS' own documentation of its IPP extensions; no CUPS code or library is used.
    /// </summary>
    public static class CupsQueues
    {
        /// <summary>The local CUPS server.</summary>
        public const string LocalServer = "ipp://localhost:631/";

        internal static readonly string[] DefaultAttributes =
        {
            "printer-name", "printer-uri-supported", "printer-info", "printer-location", "printer-make-and-model",
            "printer-state", "printer-is-accepting-jobs", "printer-is-shared", "document-format-supported",
            "printer-type", "printer-uuid", "device-uri"
        };

        /// <summary>The port a CUPS server listens on when its address names none.</summary>
        public const int DefaultPort = 631;

        /// <summary>Every queue on the server (default: the local CUPS server), in the order the server lists them.</summary>
        /// <exception cref="IppException">The server could not be reached or refused the request.</exception>
        public static IReadOnlyList<CupsQueue> List(string serverUri = LocalServer, IppClientOptions? clientOptions = null,
                                                    CancellationToken cancellationToken = default)
        {
            if (serverUri is null) throw new ArgumentNullException(nameof(serverUri));
            using var client = new IppClient(serverUri, clientOptions);
            return List(client, cancellationToken);
        }

        /// <summary>
        /// Every queue on the CUPS server this computer is set up to use (<see cref="ConfiguredServer"/>), or on the
        /// local CUPS server when none is set up.
        /// </summary>
        /// <exception cref="IppException">The server could not be reached or refused the request.</exception>
        public static IReadOnlyList<CupsQueue> ListConfigured(IppClientOptions? clientOptions = null, CancellationToken cancellationToken = default)
            => List(ConfiguredServer() ?? LocalServer, clientOptions, cancellationToken);

        /// <summary>
        /// The CUPS server this computer's CUPS clients are set up to use, as an IPP URI such as "ipp://print-server:631/":
        /// the <c>CUPS_SERVER</c> environment variable, else <c>ServerName</c> in <c>~/.cups/client.conf</c>, else
        /// <c>ServerName</c> in <c>/etc/cups/client.conf</c>. Null when none is set, and on Windows.
        /// </summary>
        /// <remarks>
        /// A server given as a local socket path (a value starting with "/") means the local CUPS server, so
        /// <see cref="LocalServer"/> is returned for it. A "/version=..." suffix is ignored. A server that names no port
        /// gets <see cref="DefaultPort"/>.
        /// </remarks>
        public static string? ConfiguredServer()
        {
            if (OperatingSystem.IsWindows()) return null;
            return ConfiguredServerFrom(Environment.GetEnvironmentVariable,
                                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ReadFileOrNull);
        }

        /// <summary>The same as <see cref="ConfiguredServer()"/>, with the environment, home folder and file reading given (tests).</summary>
        internal static string? ConfiguredServerFrom(Func<string, string?> environment, string? home, Func<string, string?> readFile)
        {
            string? value = environment("CUPS_SERVER");
            if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(home))
                value = ServerNameIn(readFile(Path.Combine(home, ".cups", "client.conf")));
            if (string.IsNullOrWhiteSpace(value))
                value = ServerNameIn(readFile("/etc/cups/client.conf"));
            return string.IsNullOrWhiteSpace(value) ? null : ToServerUri(value.Trim());
        }

        /// <summary>The ServerName value in a client.conf text; null when it has none.</summary>
        internal static string? ServerNameIn(string? clientConf)
        {
            if (clientConf is null) return null;
            string? found = null;
            foreach (string raw in clientConf.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int space = line.IndexOfAny(new[] { ' ', '\t' });
                if (space < 0) continue;
                if (!string.Equals(line.Substring(0, space), "ServerName", StringComparison.OrdinalIgnoreCase)) continue;
                string v = line.Substring(space + 1).Trim();
                if (v.Length > 0) found = v; // the last ServerName line wins, as in CUPS
            }
            return found;
        }

        /// <summary>
        /// The IPP URI for a CUPS server address: "host", "host:port", "[v6]:port", an "ipp://" or "ipps://" URI, or a
        /// local socket path.
        /// </summary>
        internal static string ToServerUri(string server)
        {
            if (server.StartsWith("/", StringComparison.Ordinal)) return LocalServer;
            if (server.StartsWith("ipp://", StringComparison.OrdinalIgnoreCase) || server.StartsWith("ipps://", StringComparison.OrdinalIgnoreCase))
                return server.EndsWith("/", StringComparison.Ordinal) ? server : server + "/";
            int version = server.IndexOf("/version=", StringComparison.OrdinalIgnoreCase);
            if (version >= 0) server = server.Substring(0, version);
            server = server.TrimEnd('/');

            string host = server;
            string port = DefaultPort.ToString(CultureInfo.InvariantCulture);
            if (server.StartsWith("[", StringComparison.Ordinal))
            {
                int close = server.IndexOf(']');
                if (close > 0)
                {
                    host = server.Substring(0, close + 1);
                    if (close + 2 < server.Length && server[close + 1] == ':') port = server.Substring(close + 2);
                }
            }
            else
            {
                int colon = server.LastIndexOf(':');
                // One colon means host:port; more than one is a bare IPv6 address.
                if (colon > 0 && server.IndexOf(':') == colon)
                {
                    host = server.Substring(0, colon);
                    port = server.Substring(colon + 1);
                }
                else if (colon > 0)
                {
                    host = "[" + server + "]";
                }
            }
            return "ipp://" + host + ":" + port + "/";
        }

        private static string? ReadFileOrNull(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Every queue on the server <paramref name="client"/> talks to.</summary>
        public static IReadOnlyList<CupsQueue> List(IppClient client, CancellationToken cancellationToken = default)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            var queues = new List<CupsQueue>();
            foreach (IppAttributeGroup g in client.CupsGetPrinters(null, null, cancellationToken)) queues.Add(new CupsQueue(g));
            return queues;
        }
    }
}
