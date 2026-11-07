using System;
using System.Collections.Generic;
using System.Threading;

namespace Chuvadi.Print.Ipp
{
    /// <summary>
    /// Remembers each printer's capabilities (Get-Printer-Attributes) so a router does not ask the printer before
    /// every job. An entry is read again when it is older than <see cref="RefreshAfter"/> (default 10 minutes,
    /// D-132), after a failed read, and after a failed job (the routes call <see cref="Invalidate"/>). Thread-safe.
    /// </summary>
    public sealed class IppCapabilityCache
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, (IppPrinterCapabilities Caps, DateTimeOffset ReadAt)> _entries = new(StringComparer.Ordinal);
        private readonly TimeProvider _time;

        /// <param name="refreshAfter">Age after which an entry is read again. Default 10 minutes.</param>
        /// <param name="timeProvider">Clock. Default the system clock.</param>
        public IppCapabilityCache(TimeSpan? refreshAfter = null, TimeProvider? timeProvider = null)
        {
            TimeSpan refresh = refreshAfter ?? DefaultRefreshAfter;
            if (refresh < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(refreshAfter));
            RefreshAfter = refresh;
            _time = timeProvider ?? TimeProvider.System;
        }

        /// <summary>The default age after which capabilities are read again: 10 minutes.</summary>
        public static TimeSpan DefaultRefreshAfter { get; } = TimeSpan.FromMinutes(10);

        /// <summary>Age after which an entry is read again.</summary>
        public TimeSpan RefreshAfter { get; }

        /// <summary>
        /// The printer's capabilities: the cached entry when it is fresh, otherwise read now through
        /// <paramref name="client"/>. A failed read removes the entry and throws.
        /// </summary>
        public IppPrinterCapabilities Get(IppClient client, CancellationToken cancellationToken = default)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            string key = client.PrinterUri;
            DateTimeOffset now = _time.GetUtcNow();
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var entry) && now - entry.ReadAt < RefreshAfter) return entry.Caps;
            }

            IppPrinterCapabilities caps;
            try
            {
                caps = new IppPrinterCapabilities(client.GetPrinterAttributes(null, cancellationToken).Group(IppTag.PrinterAttributes)
                                                  ?? throw new IppException("The printer returned no printer attributes."));
            }
            catch
            {
                Invalidate(key);
                throw;
            }
            lock (_gate) _entries[key] = (caps, _time.GetUtcNow());
            return caps;
        }

        /// <summary>The cached capabilities and when they were read, without asking the printer (even when stale).</summary>
        public bool TryGetCached(string printerUri, out IppPrinterCapabilities? capabilities, out DateTimeOffset readAt)
        {
            if (printerUri is null) throw new ArgumentNullException(nameof(printerUri));
            lock (_gate)
            {
                if (_entries.TryGetValue(printerUri, out var entry))
                {
                    capabilities = entry.Caps;
                    readAt = entry.ReadAt;
                    return true;
                }
            }
            capabilities = null;
            readAt = default;
            return false;
        }

        /// <summary>
        /// Saves the cached capabilities of <paramref name="printer"/>'s address into it (encoded IPP printer
        /// attributes and the time they were read), for <see cref="PrinterRegistry"/>. Returns false when nothing is cached.
        /// </summary>
        public bool StoreIn(SavedPrinter printer)
        {
            if (printer is null) throw new ArgumentNullException(nameof(printer));
            if (!TryGetCached(printer.Address, out IppPrinterCapabilities? caps, out DateTimeOffset readAt) || caps is null) return false;
            var message = new IppMessage((short)IppStatusCode.SuccessfulOk, 0);
            IppAttributeGroup group = message.AddGroup(IppTag.PrinterAttributes);
            foreach (IppAttribute a in caps.Attributes.Attributes) group.Add(a);
            printer.Capabilities = message.ToArray();
            printer.CapabilitiesReadAt = readAt;
            return true;
        }

        /// <summary>
        /// Puts <paramref name="printer"/>'s saved capabilities into the cache with their original read time, so a
        /// fresh entry is used without asking the printer and a stale one is read again as usual. Returns false when
        /// the printer has none, or they cannot be decoded (they are then ignored).
        /// </summary>
        public bool LoadFrom(SavedPrinter printer)
        {
            if (printer is null) throw new ArgumentNullException(nameof(printer));
            if (printer.Capabilities is null || printer.CapabilitiesReadAt is null) return false;
            IppAttributeGroup? group;
            try { group = IppMessage.Parse(printer.Capabilities).Group(IppTag.PrinterAttributes); }
            catch (IppFormatException) { return false; }
            if (group is null) return false;
            lock (_gate) _entries[printer.Address] = (new IppPrinterCapabilities(group), printer.CapabilitiesReadAt.Value);
            return true;
        }

        /// <summary>Forgets one printer, so the next <see cref="Get"/> asks it again.</summary>
        public void Invalidate(string printerUri)
        {
            if (printerUri is null) throw new ArgumentNullException(nameof(printerUri));
            lock (_gate) _entries.Remove(printerUri);
        }

        /// <summary>Forgets every printer.</summary>
        public void Clear()
        {
            lock (_gate) _entries.Clear();
        }
    }

    /// <summary>
    /// Sends the PDF unchanged to an IPP printer (<see cref="PrintRouteKind.PdfAsIs"/>) or a CUPS queue
    /// (<see cref="PrintRouteKind.Cups"/>) through an <see cref="IppDocumentTarget"/>. Usable only when every font
    /// in the PDF is embedded (D-132 item 2), the printer is accepting jobs, and it lists application/pdf itself in
    /// document-format-supported (application/octet-stream does not count, D-132 item 3).
    /// </summary>
    public sealed class IppPdfRoute : IPrintRoute, IDisposable
    {
        public const string PdfFormat = "application/pdf";

        private readonly bool _ownsTarget;

        /// <param name="target">The printer.</param>
        /// <param name="cache">Shared capability cache. Null = a cache of this route's own.</param>
        /// <param name="ownsTarget">When true, <see cref="Dispose"/> disposes the target.</param>
        public IppPdfRoute(IppDocumentTarget target, IppCapabilityCache? cache = null, bool ownsTarget = false)
            : this(target, cache, ownsTarget, PrintRouteKind.PdfAsIs)
        {
        }

        private IppPdfRoute(IppDocumentTarget target, IppCapabilityCache? cache, bool ownsTarget, PrintRouteKind kind)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Cache = cache ?? new IppCapabilityCache();
            _ownsTarget = ownsTarget;
            Kind = kind;
        }

        /// <summary>The same route for a CUPS queue (its IPP URI, for example ipp://localhost:631/printers/name).</summary>
        public static IppPdfRoute ForCups(IppDocumentTarget target, IppCapabilityCache? cache = null, bool ownsTarget = false)
            => new(target, cache, ownsTarget, PrintRouteKind.Cups);

        public IppDocumentTarget Target { get; }
        public IppCapabilityCache Cache { get; }

        /// <inheritdoc />
        public PrintRouteKind Kind { get; }

        /// <inheritdoc />
        public string Name => Target.Name;

        /// <inheritdoc />
        public PrintRouteCheck Check(PrintRouteContext context, CancellationToken cancellationToken)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));
            if (context.Fonts == FontEmbedding.NotAllEmbedded) return PrintRouteCheck.Skip("not every font is embedded");
            if (context.Fonts == FontEmbedding.NeedsGeneratedAppearances)
                return PrintRouteCheck.Skip("form fields or comments need appearances generated");
            if (context.Fonts != FontEmbedding.AllEmbedded) return PrintRouteCheck.Skip("fonts not known to be embedded");
            IppPrinterCapabilities caps = Cache.Get(Target.Client, cancellationToken);
            if (!caps.IsAcceptingJobs) return PrintRouteCheck.Skip("the printer is not accepting jobs");
            if (!caps.SupportsFormat(PdfFormat)) return PrintRouteCheck.Skip("the printer does not list " + PdfFormat);
            return PrintRouteCheck.Usable;
        }

        /// <inheritdoc />
        public PrintResult Print(PrintJob job, CancellationToken cancellationToken)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            PrintResult result = job.SubmitTo(Target, cancellationToken);
            if (result.Status == PrintJobStatus.Failed) Cache.Invalidate(Target.Client.PrinterUri);
            return result;
        }

        public void Dispose()
        {
            if (_ownsTarget) Target.Dispose();
        }
    }

    /// <summary>
    /// Prints rendered pages as PWG Raster through an <see cref="IppPrinterTarget"/>
    /// (<see cref="PrintRouteKind.Raster"/>). Usable when the job has a rasteriser and the printer is accepting jobs
    /// and lists image/pwg-raster.
    /// </summary>
    public sealed class IppRasterRoute : IPrintRoute, IDisposable
    {
        private readonly bool _ownsTarget;

        public IppRasterRoute(IppPrinterTarget target, IppCapabilityCache? cache = null, bool ownsTarget = false)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Cache = cache ?? new IppCapabilityCache();
            _ownsTarget = ownsTarget;
        }

        public IppPrinterTarget Target { get; }
        public IppCapabilityCache Cache { get; }

        /// <inheritdoc />
        public PrintRouteKind Kind => PrintRouteKind.Raster;

        /// <inheritdoc />
        public string Name => Target.Name;

        /// <inheritdoc />
        public PrintRouteCheck Check(PrintRouteContext context, CancellationToken cancellationToken)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));
            if (context.Job.Options.Rasterizer is null) return PrintRouteCheck.Skip("no rasteriser is configured");
            IppPrinterCapabilities caps = Cache.Get(Target.Client, cancellationToken);
            if (!caps.IsAcceptingJobs) return PrintRouteCheck.Skip("the printer is not accepting jobs");
            if (!caps.SupportsFormat(IppPrinterTarget.PwgRasterFormat))
                return PrintRouteCheck.Skip("the printer does not list " + IppPrinterTarget.PwgRasterFormat);
            return PrintRouteCheck.Usable;
        }

        /// <inheritdoc />
        public PrintResult Print(PrintJob job, CancellationToken cancellationToken)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            PrintResult result = job.PrintTo(Target, cancellationToken);
            if (result.Status == PrintJobStatus.Failed) Cache.Invalidate(Target.Client.PrinterUri);
            return result;
        }

        public void Dispose()
        {
            if (_ownsTarget) Target.Dispose();
        }
    }

    /// <summary>Ready-made routers for IPP printers and CUPS queues.</summary>
    public static class IppRoutes
    {
        /// <summary>
        /// A router for an IPP printer: the PDF as-is when allowed, otherwise PWG Raster. The router owns the targets
        /// it creates; dispose it when done.
        /// </summary>
        public static PrintRouter CreateRouter(string printerUri, IppClientOptions? clientOptions = null, IppCapabilityCache? cache = null)
        {
            if (printerUri is null) throw new ArgumentNullException(nameof(printerUri));
            IppCapabilityCache shared = cache ?? new IppCapabilityCache();
            return new PrintRouter(new IPrintRoute[]
            {
                new IppPdfRoute(new IppDocumentTarget(printerUri, clientOptions), shared, ownsTarget: true),
                new IppRasterRoute(new IppPrinterTarget(printerUri, clientOptions), shared, ownsTarget: true)
            }, ownsRoutes: true);
        }

        /// <summary>
        /// A router for a saved IPP printer or CUPS queue: its address, its pinned certificate (added to a copy of
        /// <paramref name="clientOptions"/>), its forced route and its name. Saved capabilities are loaded into the
        /// cache (<see cref="IppCapabilityCache.LoadFrom"/>). The router owns the targets it creates.
        /// </summary>
        /// <exception cref="ArgumentException">The printer is not an <see cref="PrinterConnection.Ipp"/> or <see cref="PrinterConnection.Cups"/> printer.</exception>
        public static PrintRouter CreateRouter(SavedPrinter printer, IppClientOptions? clientOptions = null, IppCapabilityCache? cache = null)
        {
            if (printer is null) throw new ArgumentNullException(nameof(printer));
            if (printer.Connection != PrinterConnection.Ipp && printer.Connection != PrinterConnection.Cups)
                throw new ArgumentException("Saved printer '" + printer.Name + "' is a " + printer.Connection
                                            + " printer; IPP routes need an Ipp or Cups printer.", nameof(printer));
            IppClientOptions options = Copy(clientOptions ?? new IppClientOptions());
            if (!string.IsNullOrWhiteSpace(printer.PinnedCertificate)) options.PinnedCertificates.Add(printer.PinnedCertificate);
            IppCapabilityCache shared = cache ?? new IppCapabilityCache();
            shared.LoadFrom(printer);
            PrintRouter router = printer.Connection == PrinterConnection.Cups
                ? CreateCupsRouter(printer.Address, options, shared)
                : CreateRouter(printer.Address, options, shared);
            router.Name = printer.Name;
            router.ForcedRoute = printer.ForcedRoute;
            return router;
        }

        private static IppClientOptions Copy(IppClientOptions o)
        {
            var copy = new IppClientOptions
            {
                UserName = o.UserName,
                NaturalLanguage = o.NaturalLanguage,
                IppVersion = o.IppVersion,
                Timeout = o.Timeout,
                Credentials = o.Credentials,
                AcceptAnyServerCertificate = o.AcceptAnyServerCertificate
            };
            foreach (string pin in o.PinnedCertificates) copy.PinnedCertificates.Add(pin);
            return copy;
        }

        /// <summary>
        /// A router for a CUPS queue (D-94, Linux and macOS): the PDF to CUPS when allowed, otherwise PWG Raster to
        /// the same queue. The router owns the targets it creates; dispose it when done.
        /// </summary>
        public static PrintRouter CreateCupsRouter(string queueUri, IppClientOptions? clientOptions = null, IppCapabilityCache? cache = null)
        {
            if (queueUri is null) throw new ArgumentNullException(nameof(queueUri));
            IppCapabilityCache shared = cache ?? new IppCapabilityCache();
            return new PrintRouter(new IPrintRoute[]
            {
                IppPdfRoute.ForCups(new IppDocumentTarget(queueUri, clientOptions), shared, ownsTarget: true),
                new IppRasterRoute(new IppPrinterTarget(queueUri, clientOptions), shared, ownsTarget: true)
            }, ownsRoutes: true);
        }
    }
}
