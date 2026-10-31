using System;
using System.Runtime.Versioning;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
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
    /// The real forwarders: IPP printers get the PDF passed through (works now); Windows printers need the PDF
    /// rendered, which needs a PDF rasteriser (Chuvadi.Print.Pdf, layer A5). Until one is supplied through
    /// <see cref="RasterizerFactory"/>, Windows forwarding fails each job with a clear message.
    /// </summary>
    public sealed class DefaultForwarderFactory : IForwarderFactory
    {
        /// <summary>Supplies a PDF rasteriser for Windows printers; null until Chuvadi.Print.Pdf is available.</summary>
        public Func<IPageRasterizer?> RasterizerFactory { get; set; } = () => null;

        public IForwarder Create(ForwardTarget target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (target.Kind == ForwardKind.Ipp) return new IppForwarder();
            if (!OperatingSystem.IsWindows()) return new UnavailableForwarder("Windows printers can only be used on Windows.");
            IPageRasterizer? rasterizer = RasterizerFactory();
            return rasterizer is null
                ? new UnavailableForwarder("Forwarding to a Windows printer needs a PDF rasteriser (Chuvadi.Print.Pdf), which is not installed yet. Use an IPP printer URI meanwhile.")
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

    /// <summary>Renders the PDF and prints it on a Windows printer (A3) - needs a rasteriser.</summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsForwarder : IForwarder
    {
        private readonly IPageRasterizer _rasterizer;

        public WindowsForwarder(IPageRasterizer rasterizer) => _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));

        public PrintResult Forward(string pdfPath, JobInfo job, ForwardTarget target, IPrintAuditSink audit)
            => new PrintJob(pdfPath)
                .WithTitle(job.Title).WithUser(job.UserName)
                .WithCopies(target.Copies).WithDuplex(target.Duplex)
                .WithRasterizer(_rasterizer).WithAuditSink(audit).WithSilent()
                .PrintTo(new WindowsPrinterTarget(target.Printer));
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
