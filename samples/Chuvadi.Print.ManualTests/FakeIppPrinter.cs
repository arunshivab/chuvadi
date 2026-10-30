using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Chuvadi.Print.Ipp;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// An in-process IPP Everywhere printer for tests: answers Get-Printer-Attributes from a configurable
    /// attribute set, accepts Print-Job / Validate-Job / Get-Job-Attributes / Cancel-Job, and records every
    /// request with its document bytes.
    /// </summary>
    public sealed class FakeIppPrinter : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Thread _thread;
        private readonly object _gate = new();
        private readonly List<(IppMessage Request, byte[] Document)> _requests = new();
        private int _nextJobId = 100;

        public FakeIppPrinter(Action<IppAttributeGroup>? configure = null)
        {
            Port = FreePort();
            _listener.Prefixes.Add("http://localhost:" + Port + "/");
            _listener.Start();
            PrinterAttributes = DefaultAttributes(Uri);
            configure?.Invoke(PrinterAttributes);
            _thread = new Thread(Serve) { IsBackground = true, Name = "FakeIppPrinter" };
            _thread.Start();
        }

        public int Port { get; }
        public string Uri => "ipp://localhost:" + Port + "/ipp/print";
        public IppAttributeGroup PrinterAttributes { get; }

        /// <summary>When set, the next Print-Job is answered with this status (then cleared).</summary>
        public IppStatusCode? FailNextJobWith { get; set; }

        /// <summary>job-state reported by Get-Job-Attributes. Default completed.</summary>
        public IppJobState JobState { get; set; } = IppJobState.Completed;

        public IReadOnlyList<(IppMessage Request, byte[] Document)> Requests
        {
            get { lock (_gate) return _requests.ToArray(); }
        }

        /// <summary>The Print-Job requests received.</summary>
        public List<(IppMessage Request, byte[] Document)> PrintJobs
        {
            get
            {
                var list = new List<(IppMessage, byte[])>();
                foreach (var r in Requests) if (r.Request.Operation == IppOperation.PrintJob) list.Add(r);
                return list;
            }
        }

        public static IppAttributeGroup DefaultAttributes(string uri)
        {
            var g = new IppAttributeGroup(IppTag.PrinterAttributes);
            g.Add("printer-uri-supported", IppValue.Uri(uri));
            g.Add("printer-name", IppValue.Name("Chuvadi Fake IPP"));
            g.Add("printer-make-and-model", IppValue.Text("Chuvadi Test Printer"));
            g.Add("printer-state", IppValue.Enum((int)IppPrinterState.Idle));
            g.Add("printer-state-reasons", IppValue.Keyword("none"));
            g.Add("printer-is-accepting-jobs", IppValue.Boolean(true));
            g.Add("operations-supported", IppValue.Enum(0x0002), IppValue.Enum(0x0004), IppValue.Enum(0x0008),
                  IppValue.Enum(0x0009), IppValue.Enum(0x000A), IppValue.Enum(0x000B));
            g.Add("document-format-supported", IppValue.MimeMediaType("application/pdf"), IppValue.MimeMediaType("image/pwg-raster"),
                  IppValue.MimeMediaType("application/octet-stream"));
            g.Add("sides-supported", IppValue.Keyword("one-sided"), IppValue.Keyword("two-sided-long-edge"), IppValue.Keyword("two-sided-short-edge"));
            g.Add("media-supported", IppValue.Keyword("iso_a4_210x297mm"), IppValue.Keyword("na_letter_8.5x11in"),
                  IppValue.Keyword("iso_a5_148x210mm"), IppValue.Keyword("custom_min_76.2x127mm"), IppValue.Keyword("custom_max_215.9x355.6mm"));
            g.Add("media-source-supported", IppValue.Keyword("auto"), IppValue.Keyword("tray-1"), IppValue.Keyword("manual"));
            g.Add("media-type-supported", IppValue.Keyword("stationery"), IppValue.Keyword("labels"));
            g.Add("print-color-mode-supported", IppValue.Keyword("auto"), IppValue.Keyword("color"), IppValue.Keyword("monochrome"), IppValue.Keyword("bi-level"));
            g.Add("copies-supported", IppValue.Range(1, 99));
            g.Add("multiple-document-handling-supported", IppValue.Keyword("separate-documents-uncollated-copies"),
                  IppValue.Keyword("separate-documents-collated-copies"));
            g.Add("print-scaling-supported", IppValue.Keyword("auto"), IppValue.Keyword("auto-fit"), IppValue.Keyword("fill"),
                  IppValue.Keyword("fit"), IppValue.Keyword("none"));
            g.Add("print-quality-supported", IppValue.Enum(3), IppValue.Enum(4), IppValue.Enum(5));
            g.Add("orientation-requested-supported", IppValue.Enum(3), IppValue.Enum(4), IppValue.Enum(5), IppValue.Enum(6));
            g.Add("page-ranges-supported", IppValue.Boolean(true));
            g.Add("printer-resolution-supported", IppValue.Resolution(300, 300), IppValue.Resolution(600, 600));
            g.Add("pwg-raster-document-type-supported", IppValue.Keyword("black_1"), IppValue.Keyword("sgray_8"), IppValue.Keyword("srgb_8"));
            g.Add("pwg-raster-document-resolution-supported", IppValue.Resolution(150, 150), IppValue.Resolution(300, 300), IppValue.Resolution(600, 600));
            g.Add("pwg-raster-document-sheet-back", IppValue.Keyword("normal"));
            g.Add("media-left-margin-supported", IppValue.Integer(423), IppValue.Integer(0));
            g.Add("media-right-margin-supported", IppValue.Integer(423), IppValue.Integer(0));
            g.Add("media-top-margin-supported", IppValue.Integer(423));
            g.Add("media-bottom-margin-supported", IppValue.Integer(423));
            return g;
        }

        private void Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = _listener.GetContext(); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                try { Handle(context); }
                catch (Exception ex)
                {
                    try
                    {
                        context.Response.StatusCode = 500;
                        context.Response.Close(System.Text.Encoding.UTF8.GetBytes(ex.Message), false);
                    }
                    catch
                    {
                        // client went away
                    }
                }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            IppMessage request = IppMessage.ReadFrom(context.Request.InputStream, out byte[] document);
            lock (_gate) _requests.Add((request, document));

            var response = new IppMessage((short)IppStatusCode.SuccessfulOk, request.RequestId, request.VersionMajor, request.VersionMinor);
            IppAttributeGroup op = response.AddGroup(IppTag.OperationAttributes);
            op.Add("attributes-charset", IppValue.Charset("utf-8"));
            op.Add("attributes-natural-language", IppValue.NaturalLanguage("en"));

            switch (request.Operation)
            {
                case IppOperation.GetPrinterAttributes:
                {
                    IppAttributeGroup printer = response.AddGroup(IppTag.PrinterAttributes);
                    foreach (IppAttribute a in PrinterAttributes.Attributes) printer.Add(a);
                    break;
                }
                case IppOperation.PrintJob:
                    if (FailNextJobWith.HasValue)
                    {
                        response.Code = (short)FailNextJobWith.Value;
                        op.Add("status-message", IppValue.Text("simulated failure"));
                        FailNextJobWith = null;
                        break;
                    }
                    AddJob(response, Interlocked.Increment(ref _nextJobId), IppJobState.Pending);
                    break;
                case IppOperation.ValidateJob:
                    break;
                case IppOperation.GetJobAttributes:
                case IppOperation.CancelJob:
                {
                    int id = request.Group(IppTag.OperationAttributes)?["job-id"]?.Value.AsInteger() ?? 0;
                    if (request.Operation == IppOperation.GetJobAttributes) AddJob(response, id, JobState);
                    break;
                }
                default:
                    response.Code = (short)IppStatusCode.ServerErrorOperationNotSupported;
                    break;
            }

            byte[] body = response.ToArray();
            context.Response.ContentType = "application/ipp";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
        }

        private void AddJob(IppMessage response, int jobId, IppJobState state)
        {
            IppAttributeGroup job = response.AddGroup(IppTag.JobAttributes);
            job.Add("job-id", IppValue.Integer(jobId));
            job.Add("job-uri", IppValue.Uri(Uri + "/" + jobId));
            job.Add("job-state", IppValue.Enum((int)state));
            job.Add("job-state-reasons", IppValue.Keyword("none"));
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
            try { _listener.Stop(); } catch (ObjectDisposedException) { }
            _listener.Close();
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
