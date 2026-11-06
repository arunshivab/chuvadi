using System;
using System.Runtime.Versioning;
using System.Threading;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// Prints a job to a Windows print queue as XPS through a <see cref="WindowsXpsPrinterTarget"/>:
    /// <see cref="PrintRouteKind.WindowsQueue"/>, D-96 position 4. Used only where the application adds it to a
    /// <see cref="PrintRouter"/>; existing code keeps printing through the GDI raster target.
    /// </summary>
    /// <remarks>
    /// <see cref="Check"/> passes the job to the next route (and the reason goes into the audit trail) when the target has
    /// no vector page source, the queue cannot be opened, or a selected page holds content XPS cannot express (a knockout group, a blend mode other than Normal, a font that may not be embedded, and
    /// the rest <see cref="XpsWriter.CanWrite(VectorPage, out string?)"/> lists). The check converts the selected pages;
    /// printing converts them again. A failure while printing is reported as a Failed result; the router does not retry
    /// on another route.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsQueueRoute : IPrintRoute
    {
        /// <param name="target">The queue, with its vector page source.</param>
        public WindowsQueueRoute(WindowsXpsPrinterTarget target)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public WindowsXpsPrinterTarget Target { get; }

        /// <inheritdoc />
        public PrintRouteKind Kind => PrintRouteKind.WindowsQueue;

        /// <inheritdoc />
        public string Name => Target.Name;

        /// <inheritdoc />
        public PrintRouteCheck Check(PrintRouteContext context, CancellationToken cancellationToken)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));
            if (Target.Source is null) return PrintRouteCheck.Skip("no vector page source is set");
            if (!WindowsPrinters.Exists(Target.PrinterName)) return PrintRouteCheck.Skip("the queue '" + Target.PrinterName + "' cannot be opened");
            return Target.CanPrint(context.Job.PdfBytes, context.Job.Settings, out string? reason, cancellationToken)
                ? PrintRouteCheck.Usable
                : PrintRouteCheck.Skip(reason ?? "the pages cannot be printed as XPS");
        }

        /// <inheritdoc />
        public PrintResult Print(PrintJob job, CancellationToken cancellationToken)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            return job.SubmitTo(Target, cancellationToken);
        }
    }
}
