using System;
using System.Collections.Generic;
using System.Threading;

namespace Chuvadi.Print
{
    /// <summary>
    /// The ways a job can reach a printer, in the order a <see cref="PrintRouter"/> tries them (D-96). The
    /// numeric value is the position in that order.
    /// </summary>
    public enum PrintRouteKind
    {
        /// <summary>The PDF itself, sent unchanged to an IPP printer that lists application/pdf (vector, printer lays out).</summary>
        PdfAsIs = 1,

        /// <summary>A page description language made by Chuvadi (PostScript, PCL XL). Added in later phases.</summary>
        PageDescription = 2,

        /// <summary>The PDF sent to a CUPS queue (Linux, macOS), which converts it with its own filters.</summary>
        Cups = 3,

        /// <summary>A Windows print queue with vector output (XPS). Added in a later phase.</summary>
        WindowsQueue = 4,

        /// <summary>Pages rendered by Chuvadi to pixels (PWG Raster, the GDI spooler, PNG). Always the last resort.</summary>
        Raster = 5
    }

    /// <summary>Whether every font a PDF uses is embedded in it.</summary>
    public enum FontEmbedding
    {
        /// <summary>Not known. A router does not send the PDF as-is.</summary>
        Unknown = 0,

        /// <summary>Every font is embedded: the printer cannot substitute one.</summary>
        AllEmbedded = 1,

        /// <summary>At least one font is not embedded: the printer would substitute it, which breaks Tamil and other complex scripts.</summary>
        NotAllEmbedded = 2,

        /// <summary>
        /// Every font is embedded, but some form fields or comments have no stored appearance (or the form asks for
        /// appearances to be generated). A printer given the PDF as-is would draw them its own way, or not at all, so
        /// a router does not send the PDF as-is; our own rendering generates the appearances.
        /// </summary>
        NeedsGeneratedAppearances = 3
    }

    /// <summary>
    /// Reads a PDF and reports whether all its fonts are embedded. Supplied by a PDF package (a later Chuvadi.Pdf
    /// release); the core has no PDF parser.
    /// </summary>
    public interface IPdfFontInspector
    {
        /// <summary>Inspects the document. Returns <see cref="FontEmbedding.Unknown"/> when it cannot tell.</summary>
        FontEmbedding Inspect(ReadOnlyMemory<byte> pdf);
    }

    /// <summary>What a route is asked to judge: the job and what is known about its fonts.</summary>
    public sealed class PrintRouteContext
    {
        internal PrintRouteContext(PrintJob job, FontEmbedding fonts)
        {
            Job = job;
            Fonts = fonts;
        }

        /// <summary>The job to print. Routes must not modify it in <see cref="IPrintRoute.Check"/>.</summary>
        public PrintJob Job { get; }

        /// <summary>The job's <see cref="PrintJobOptions.DocumentFonts"/>, or the router's inspector's answer when that was unknown.</summary>
        public FontEmbedding Fonts { get; }
    }

    /// <summary>A route's answer to "can you print this job?".</summary>
    public sealed class PrintRouteCheck
    {
        private PrintRouteCheck(bool canPrint, string? reason)
        {
            CanPrint = canPrint;
            Reason = reason;
        }

        /// <summary>True when the route can print the job.</summary>
        public bool CanPrint { get; }

        /// <summary>Why the route cannot print the job; null when it can.</summary>
        public string? Reason { get; }

        /// <summary>The route can print the job.</summary>
        public static PrintRouteCheck Usable { get; } = new(true, null);

        /// <summary>The route cannot print the job, for this reason (written to the audit trail).</summary>
        public static PrintRouteCheck Skip(string reason)
            => new(false, string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("A reason is required.", nameof(reason)) : reason);
    }

    /// <summary>
    /// One way of printing a job on one printer. Implemented by the device packages (for example IppPdfRoute and
    /// IppRasterRoute in Chuvadi.Print.Ipp) and by <see cref="RasterRoute"/> for any <see cref="IPrintTarget"/>.
    /// </summary>
    public interface IPrintRoute
    {
        /// <summary>Which kind of route this is; decides its place in the router's order.</summary>
        PrintRouteKind Kind { get; }

        /// <summary>Display name of the destination (printer URI, queue name, ...).</summary>
        string Name { get; }

        /// <summary>
        /// Says whether this route can print the job, without printing anything. May ask the printer (for
        /// example for its capabilities). An exception is treated as <see cref="PrintRouteCheck.Skip"/> with its message.
        /// </summary>
        PrintRouteCheck Check(PrintRouteContext context, CancellationToken cancellationToken);

        /// <summary>Prints the job (through <see cref="PrintJob.PrintTo"/> or <see cref="PrintJob.SubmitTo"/>, which record the audit event).</summary>
        PrintResult Print(PrintJob job, CancellationToken cancellationToken);
    }

    /// <summary>A route the router did not use, and why.</summary>
    public sealed class PrintRouteSkip
    {
        internal PrintRouteSkip(PrintRouteKind kind, string name, string reason)
        {
            Kind = kind;
            Name = name;
            Reason = reason;
        }

        public PrintRouteKind Kind { get; }
        public string Name { get; }
        public string Reason { get; }

        public override string ToString() => Kind + ": " + Reason;
    }

    /// <summary>The route a router would use for a job, and the routes it passed over.</summary>
    public sealed class PrintRouteDecision
    {
        internal PrintRouteDecision(IPrintRoute? route, IReadOnlyList<PrintRouteSkip> skipped, FontEmbedding fonts)
        {
            Route = route;
            Skipped = skipped;
            Fonts = fonts;
        }

        /// <summary>The chosen route; null when no route can print the job.</summary>
        public IPrintRoute? Route { get; }

        /// <summary>Routes tried before the chosen one (or all of them, when none can print), in order.</summary>
        public IReadOnlyList<PrintRouteSkip> Skipped { get; }

        /// <summary>What was known about the document's fonts when deciding.</summary>
        public FontEmbedding Fonts { get; }

        /// <summary>True when an earlier route was passed over.</summary>
        public bool IsFallback => Route is not null && Skipped.Count > 0;

        /// <summary>
        /// The text written to <see cref="PrintAuditEvent.Route"/>: the route kind, then
        /// <c> (fallback: Kind: reason; Kind: reason)</c> when earlier routes were skipped; <c>None (...)</c>
        /// when no route can print.
        /// </summary>
        public string AuditText
        {
            get
            {
                string head = Route is null ? AuditRoutes.None : Route.Kind.ToString();
                if (Skipped.Count == 0) return head;
                var parts = new string[Skipped.Count];
                for (int i = 0; i < Skipped.Count; i++) parts[i] = Skipped[i].ToString();
                return head + (Route is null ? " (" : " (fallback: ") + string.Join("; ", parts) + ")";
            }
        }
    }

    /// <summary>The outcome of <see cref="PrintRouter.Print"/>.</summary>
    public sealed class PrintRouteResult
    {
        internal PrintRouteResult(PrintResult result, PrintRouteDecision decision)
        {
            Result = result;
            Decision = decision;
        }

        /// <summary>The job's result, from the chosen route (or Failed when no route could print).</summary>
        public PrintResult Result { get; }

        /// <summary>Which route was used and which were skipped.</summary>
        public PrintRouteDecision Decision { get; }

        /// <summary>The kind of the route used; null when none could print.</summary>
        public PrintRouteKind? Route => Decision.Route?.Kind;

        /// <summary>True when an earlier route was passed over (for example a raster fallback).</summary>
        public bool IsFallback => Decision.IsFallback;
    }

    /// <summary>
    /// Prints rendered pages to any <see cref="IPrintTarget"/> (PWG Raster, the Windows GDI spooler, PNG files):
    /// <see cref="PrintRouteKind.Raster"/>. Usable whenever the job has a rasteriser.
    /// </summary>
    public sealed class RasterRoute : IPrintRoute, IDisposable
    {
        private readonly bool _ownsTarget;

        /// <param name="target">Where the pages go.</param>
        /// <param name="ownsTarget">When true, <see cref="Dispose"/> disposes the target (if it is disposable).</param>
        public RasterRoute(IPrintTarget target, bool ownsTarget = false)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            _ownsTarget = ownsTarget;
        }

        public IPrintTarget Target { get; }

        /// <inheritdoc />
        public PrintRouteKind Kind => PrintRouteKind.Raster;

        /// <inheritdoc />
        public string Name => Target.Name;

        /// <inheritdoc />
        public PrintRouteCheck Check(PrintRouteContext context, CancellationToken cancellationToken)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));
            return context.Job.Options.Rasterizer is null ? PrintRouteCheck.Skip("no rasteriser is configured") : PrintRouteCheck.Usable;
        }

        /// <inheritdoc />
        public PrintResult Print(PrintJob job, CancellationToken cancellationToken)
            => (job ?? throw new ArgumentNullException(nameof(job))).PrintTo(Target, cancellationToken);

        public void Dispose()
        {
            if (_ownsTarget) (Target as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Chooses how to print each job (D-96): it asks each route in <see cref="PrintRouteKind"/> order whether it can
    /// print the job and uses the first that can. The chosen route and every skipped route, with the reason, are
    /// written to the job's audit event (<see cref="PrintAuditEvent.Route"/>), so a raster fallback is always on
    /// record. A route that fails while printing is not retried on another route: the printer may already have
    /// printed part of the job, and a second route would print it twice.
    /// </summary>
    /// <remarks>
    /// Overrides: <see cref="ForcedRoute"/> and <see cref="DisabledRoutes"/> apply to every job (site settings);
    /// the <c>forceRoute</c> argument of <see cref="Print"/> applies to one job and wins over both. Different jobs
    /// may be printed concurrently; one <see cref="PrintJob"/> must not be printed by two routers at once.
    /// </remarks>
    public sealed class PrintRouter : IDisposable
    {
        private readonly IPrintRoute[] _routes;
        private readonly bool _ownsRoutes;

        public PrintRouter(params IPrintRoute[] routes) : this((IEnumerable<IPrintRoute>)routes, false)
        {
        }

        /// <param name="routes">The routes for one printer, in any order.</param>
        /// <param name="ownsRoutes">When true, <see cref="Dispose"/> disposes the routes that are disposable.</param>
        public PrintRouter(IEnumerable<IPrintRoute> routes, bool ownsRoutes = false)
        {
            if (routes is null) throw new ArgumentNullException(nameof(routes));
            var list = new List<IPrintRoute>();
            foreach (IPrintRoute route in routes) list.Add(route ?? throw new ArgumentException("A route is null.", nameof(routes)));
            if (list.Count == 0) throw new ArgumentException("At least one route is required.", nameof(routes));
            // Stable sort by kind: routes of the same kind keep the order they were given in.
            var indexed = new List<(IPrintRoute Route, int Index)>();
            for (int i = 0; i < list.Count; i++) indexed.Add((list[i], i));
            indexed.Sort((a, b) => a.Route.Kind != b.Route.Kind ? ((int)a.Route.Kind).CompareTo((int)b.Route.Kind) : a.Index.CompareTo(b.Index));
            _routes = new IPrintRoute[indexed.Count];
            for (int i = 0; i < indexed.Count; i++) _routes[i] = indexed[i].Route;
            _ownsRoutes = ownsRoutes;
        }

        /// <summary>The routes, in the order they are tried.</summary>
        public IReadOnlyList<IPrintRoute> Routes => _routes;

        /// <summary>Display name used in the audit event when no route can print. Default: the first route's name.</summary>
        public string? Name { get; set; }

        /// <summary>When set, every job uses only routes of this kind (a site override). Default null.</summary>
        public PrintRouteKind? ForcedRoute { get; set; }

        /// <summary>Route kinds never used (a site override). Ignored for a job whose route is forced. Default empty.</summary>
        public ISet<PrintRouteKind> DisabledRoutes { get; } = new HashSet<PrintRouteKind>();

        /// <summary>
        /// Asked about the fonts of jobs whose <see cref="PrintJobOptions.DocumentFonts"/> is unknown. Default null:
        /// such jobs are not sent as-is (D-132).
        /// </summary>
        public IPdfFontInspector? FontInspector { get; set; }

        /// <summary>Decides which route would print the job, without printing.</summary>
        /// <param name="job">The job.</param>
        /// <param name="forceRoute">When set, only routes of this kind are considered (a per-job override).</param>
        /// <param name="cancellationToken">Cancels the checks.</param>
        public PrintRouteDecision Choose(PrintJob job, PrintRouteKind? forceRoute = null, CancellationToken cancellationToken = default)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            FontEmbedding fonts = job.Options.DocumentFonts;
            if (fonts == FontEmbedding.Unknown && FontInspector is not null)
            {
                try { fonts = FontInspector.Inspect(job.PdfBytes); }
                catch (Exception ex) when (ex is not OperationCanceledException) { fonts = FontEmbedding.Unknown; }
            }

            var context = new PrintRouteContext(job, fonts);
            PrintRouteKind? forced = forceRoute ?? ForcedRoute;
            bool perJob = forceRoute.HasValue;
            var skipped = new List<PrintRouteSkip>();
            foreach (IPrintRoute route in _routes)
            {
                if (forced.HasValue && route.Kind != forced.Value) continue;
                if (!perJob && DisabledRoutes.Contains(route.Kind)) continue;
                cancellationToken.ThrowIfCancellationRequested();
                PrintRouteCheck check;
                try
                {
                    check = route.Check(context, cancellationToken) ?? PrintRouteCheck.Skip("the route gave no answer");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    check = PrintRouteCheck.Skip(OneLine(ex.Message));
                }
                if (check.CanPrint) return new PrintRouteDecision(route, skipped, fonts);
                skipped.Add(new PrintRouteSkip(route.Kind, route.Name, check.Reason!));
            }
            if (skipped.Count == 0)
            {
                string why = forced.HasValue ? "no " + forced.Value + " route is configured" : "every route is disabled";
                skipped.Add(new PrintRouteSkip(forced ?? _routes[0].Kind, Name ?? _routes[0].Name, why));
            }
            return new PrintRouteDecision(null, skipped, fonts);
        }

        /// <summary>
        /// Prints the job on the first route that can print it. Records exactly one audit event, with the route.
        /// When no route can print, the result is Failed (or, with <see cref="PrintJobOptions.ThrowOnFailure"/>, an
        /// <see cref="InvalidOperationException"/> is thrown after the audit event).
        /// </summary>
        public PrintRouteResult Print(PrintJob job, PrintRouteKind? forceRoute = null, CancellationToken cancellationToken = default)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            PrintRouteDecision decision;
            try
            {
                decision = Choose(job, forceRoute, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Audit(job, startedAt, PrintJobStatus.Cancelled, null, AuditRoutes.None);
                if (job.Options.ThrowOnFailure) throw;
                var cancelled = new PrintRouteDecision(null, Array.Empty<PrintRouteSkip>(), job.Options.DocumentFonts);
                return new PrintRouteResult(PrintResult.Cancelled(job.JobId), cancelled);
            }

            if (decision.Route is null)
            {
                var reasons = new string[decision.Skipped.Count];
                for (int i = 0; i < reasons.Length; i++) reasons[i] = decision.Skipped[i].ToString();
                string error = "No print route can print this job: " + string.Join("; ", reasons) + ".";
                Audit(job, startedAt, PrintJobStatus.Failed, error, decision.AuditText);
                if (job.Options.ThrowOnFailure) throw new InvalidOperationException(error);
                return new PrintRouteResult(PrintResult.Failed(job.JobId, error), decision);
            }

            string? previous = job.RouteLabel;
            job.RouteLabel = decision.AuditText;
            try
            {
                PrintResult result = decision.Route.Print(job, cancellationToken)
                                     ?? throw new InvalidOperationException("The print route returned no result.");
                return new PrintRouteResult(result, decision);
            }
            finally
            {
                job.RouteLabel = previous;
            }
        }

        public void Dispose()
        {
            if (!_ownsRoutes) return;
            foreach (IPrintRoute route in _routes) (route as IDisposable)?.Dispose();
        }

        private void Audit(PrintJob job, DateTimeOffset startedAt, PrintJobStatus status, string? error, string route)
        {
            job.Options.AuditSink.Record(new PrintAuditEvent
            {
                JobId = job.JobId,
                DocumentTitle = job.DocumentTitle,
                PageCount = 0,
                Copies = job.Settings.Copies,
                PrinterName = Name ?? _routes[0].Name,
                MachineName = Environment.MachineName,
                UserName = job.UserName,
                Status = status,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                TenantId = job.TenantId,
                Error = error,
                Route = route
            });
        }

        private static string OneLine(string message) => message.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    /// <summary>Audit route names for jobs printed without a router.</summary>
    internal static class AuditRoutes
    {
        public const string Raster = "Raster";
        public const string Document = "Document";
        public const string None = "None";
    }

    /// <summary>
    /// A destination that takes bytes and passes them to the printer unchanged: PostScript, PCL, ZPL, ESC/POS or a
    /// PDF for a printer that reads PDF directly. Implemented by RawSocketTarget (TCP port 9100, Chuvadi.Print.Ipp)
    /// and WindowsRawPrinterTarget (a Windows queue with the RAW datatype, Chuvadi.Print.Windows).
    /// </summary>
    public interface IRawPrintTarget
    {
        /// <summary>Display name of the destination.</summary>
        string Name { get; }

        /// <summary>Sends the bytes as one job. Throws on failure; honours cancellation between writes.</summary>
        void Send(ReadOnlyMemory<byte> data, string? documentTitle, CancellationToken cancellationToken);
    }
}
