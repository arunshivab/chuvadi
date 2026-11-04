using System;
using System.Collections.Generic;
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
            "printer-state", "printer-is-accepting-jobs", "printer-is-shared", "document-format-supported"
        };

        /// <summary>Every queue on the server (default: the local CUPS server), in the order the server lists them.</summary>
        /// <exception cref="IppException">The server could not be reached or refused the request.</exception>
        public static IReadOnlyList<CupsQueue> List(string serverUri = LocalServer, IppClientOptions? clientOptions = null,
                                                    CancellationToken cancellationToken = default)
        {
            if (serverUri is null) throw new ArgumentNullException(nameof(serverUri));
            using var client = new IppClient(serverUri, clientOptions);
            return List(client, cancellationToken);
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
