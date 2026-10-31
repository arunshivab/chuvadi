using System;
using System.Runtime.Versioning;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.Pdf;
using Chuvadi.Print.Windows;

namespace ChuvadiPdfPrinter
{
    /// <summary>Sends a finished PDF to a physical printer.</summary>
    public interface IForwarder
    {
        PrintResult Forward(string pdfPath, JobInfo job, ForwardTarget target, IPrintAuditSink audit);
    }

    /// <summary>Creates the forwarder for a target.</summary>
    public interface IForwarderFactory
    {
        IForwarder Create(ForwardTarget target);
    }

    /// <summary>
    /// The real forwarders. IPP printers get the PDF passed through unchanged. Windows printers get it rendered
    /// page by page by Chuvadi.Print.Pdf (<see cref="PdfPageRasterizer"/>) and printed through the GDI spooler
    /// (<see cref="WindowsPrinterTarget"/>), with the target's paper, scale, colour, copies and duplex.
    /// </summary>
    public sealed class DefaultForwarderFactory : IForwarderFactory
    {
        /// <summary>The PDF rasteriser for Windows printers. Default: a new <see cref="PdfPageRasterizer"/>; null disables Windows forwarding.</summary>
        public Func<IPageRasterizer?> RasterizerFactory { get; set; } = () => new PdfPageRasterizer();

        public IForwarder Create(ForwardTarget target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (target.Kind == ForwardKind.Ipp) return new IppForwarder();
            if (!OperatingSystem.IsWindows()) return new UnavailableForwarder("Windows printers can only be used on Windows.");
            IPageRasterizer? rasterizer = RasterizerFactory();
            return rasterizer is null
                ? new UnavailableForwarder("Forwarding to a Windows printer is switched off (no PDF rasteriser configured).")
                : new WindowsForwarder(rasterizer);
        }
    }

    /// <summary>Passes the PDF through to an IPP printer.</summary>
    public sealed class IppForwarder : IForwarder
    {
        public PrintResult Forward(string pdfPath, JobInfo job, ForwardTarget target, IPrintAuditSink audit)
        {
            using var printer = new IppDocumentTarget(target.Printer, new IppClientOptions
            {
                UserName = PrinterConfig.StripDomain(job.UserName),
                AcceptAnyServerCertificate = target.AcceptAnyServerCertificate
            });
            return new PrintJob(pdfPath)
                .WithTitle(job.Title).WithUser(job.UserName)
                .WithCopies(target.Copies).WithDuplex(target.Duplex)
                .WithAuditSink(audit)
                .SubmitTo(printer);
        }
    }

    /// <summary>
    /// Renders the PDF and prints it on any raster <see cref="IPrintTarget"/>; platform-neutral (the Windows
    /// forwarder is this with a <see cref="WindowsPrinterTarget"/>). Pages keep their size: no added margins
    /// (only the printer's unprintable edge), centred, shrunk only when too large (<see cref="ForwardTarget.Scale"/>),
    /// turned to fit the sheet. One audit event per forward.
    /// </summary>
    public sealed class RasterForwarder : IForwarder
    {
        private readonly IPageRasterizer _rasterizer;
        private readonly Func<ForwardTarget, IPrintTarget> _targetFactory;

        public RasterForwarder(IPageRasterizer rasterizer, Func<ForwardTarget, IPrintTarget> targetFactory)
        {
            _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
            _targetFactory = targetFactory ?? throw new ArgumentNullException(nameof(targetFactory));
        }

        public PrintResult Forward(string pdfPath, JobInfo job, ForwardTarget target, IPrintAuditSink audit)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            PrintJob print = new PrintJob(pdfPath)
                .WithTitle(job.Title).WithUser(job.UserName)
                .WithCopies(target.Copies).WithDuplex(target.Duplex)
                .WithColor(target.Color).WithScale(target.Scale)
                .WithMargins(Margins.None).WithMarginReference(MarginReference.SheetEdge)
                .WithAlignment(ContentAlignment.Center).WithAutoRotate()
                .WithRasterizer(_rasterizer).WithAuditSink(audit).WithSilent();
            print.WithPaper(target.ResolvePaper(() => FirstPageSize(pdfPath)));

            IPrintTarget printer = _targetFactory(target);
            try
            {
                return print.PrintTo(printer);
            }
            finally
            {
                (printer as IDisposable)?.Dispose();
            }
        }

        private PageDimensions? FirstPageSize(string pdfPath)
        {
            try
            {
                using IRasterDocument doc = _rasterizer.Open(System.IO.File.ReadAllBytes(pdfPath));
                return doc.PageCount > 0 ? doc.GetPageSize(1) : null;
            }
            catch (Exception)
            {
                return null; // PrintTo reports the real problem with one audit event
            }
        }
    }

    /// <summary>Renders the PDF and prints it on a Windows printer through the GDI spooler.</summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsForwarder : IForwarder
    {
        private readonly RasterForwarder _inner;

        public WindowsForwarder(IPageRasterizer rasterizer)
        {
            _inner = new RasterForwarder(rasterizer, t => new WindowsPrinterTarget(t.Printer).WithMaxRasterDpi(t.MaxRasterDpi));
        }

        public PrintResult Forward(string pdfPath, JobInfo job, ForwardTarget target, IPrintAuditSink audit)
            => _inner.Forward(pdfPath, job, target, audit);
    }

    /// <summary>A forwarder that fails every job with a fixed reason (and audits it).</summary>
    public sealed class UnavailableForwarder : IForwarder
    {
        public UnavailableForwarder(string reason) => Reason = reason ?? throw new ArgumentNullException(nameof(reason));

        public string Reason { get; }

        public PrintResult Forward(string pdfPath, JobInfo job, ForwardTarget target, IPrintAuditSink audit)
        {
            var result = PrintResult.Failed(Guid.NewGuid(), Reason);
            audit.Record(new PrintAuditEvent
            {
                JobId = result.JobId,
                DocumentTitle = job.Title,
                PrinterName = target.Printer,
                MachineName = Environment.MachineName,
                UserName = job.UserName,
                Copies = target.Copies,
                Status = PrintJobStatus.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = Reason
            });
            return result;
        }
    }
}
