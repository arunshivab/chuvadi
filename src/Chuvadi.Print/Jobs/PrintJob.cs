using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Chuvadi.Print
{
    /// <summary>
    /// A document (PDF bytes) plus everything needed to print it. Create one from a PDF or from a
    /// spool envelope, adjust it with property setters or the fluent <c>With*</c> methods, then
    /// either print it with <see cref="PrintTo"/> or hand it to another machine with <see cref="SaveSpoolTo(string)"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var result = new PrintJob("report.pdf")
    ///     .WithPages(PageSelection.Range("1-4"))
    ///     .WithCopies(2)
    ///     .WithDuplex(Duplex.Vertical)
    ///     .WithRasterizer(rasterizer)
    ///     .PrintTo(new ImageFileTarget("preview"));
    /// </code>
    /// </example>
    /// <remarks>
    /// Printing renders one page at a time and releases it before the next, so memory does not grow with
    /// document length. Exactly one audit event is recorded per <see cref="PrintTo"/> call.
    /// </remarks>
    public sealed class PrintJob
    {
        private readonly byte[] _pdf;
        private PrintSettings _settings;
        private PrintJobOptions _options = new();

        // ----------------------------------------------------------------- construction

        /// <summary>Creates a job for PDF bytes with default settings. The array is used as-is, not copied.</summary>
        public PrintJob(byte[] pdfBytes) : this(pdfBytes, new PrintSettings())
        {
        }

        /// <summary>Creates a job for PDF bytes with the given settings. Neither argument is copied.</summary>
        public PrintJob(byte[] pdfBytes, PrintSettings settings)
        {
            _pdf = pdfBytes ?? throw new ArgumentNullException(nameof(pdfBytes));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Creates a job from a PDF file. <see cref="DocumentTitle"/> defaults to the file name.</summary>
        public PrintJob(string pdfPath)
            : this(File.ReadAllBytes(pdfPath ?? throw new ArgumentNullException(nameof(pdfPath))))
        {
            DocumentTitle = Path.GetFileName(pdfPath);
        }

        /// <summary>Creates a job by reading a PDF stream to its end. The stream is not closed.</summary>
        public PrintJob(Stream pdf) : this(ReadAll(pdf))
        {
        }

        /// <summary>Recreates a job from a spool file written by <see cref="SaveSpoolTo(string)"/>.</summary>
        public static PrintJob LoadSpool(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return FromEnvelope(SpoolEnvelope.Deserialize(stream));
        }

        /// <summary>Recreates a job from a spool stream. The stream is not closed.</summary>
        public static PrintJob LoadSpool(Stream input) => FromEnvelope(SpoolEnvelope.Deserialize(input));

        /// <summary>Recreates a job from spool bytes.</summary>
        public static PrintJob LoadSpool(byte[] data) => FromEnvelope(SpoolEnvelope.FromArray(data));

        /// <summary>Creates a job from an envelope: its PDF, settings and metadata.</summary>
        public static PrintJob FromEnvelope(SpoolEnvelope envelope)
        {
            if (envelope is null) throw new ArgumentNullException(nameof(envelope));
            var job = new PrintJob(envelope.PdfBytes, envelope.Settings.Clone())
            {
                DocumentTitle = envelope.DocumentTitle,
                UserName = envelope.UserName,
                TenantId = envelope.TenantId
            };
            if (envelope.JobId.HasValue) job.JobId = envelope.JobId.Value;
            return job;
        }

        // ----------------------------------------------------------------- state

        /// <summary>Identifies the job in results, audit events and spool envelopes. Defaults to a new GUID.</summary>
        public Guid JobId { get; set; } = Guid.NewGuid();

        /// <summary>The document bytes.</summary>
        public byte[] PdfBytes => _pdf;

        /// <summary>What to print. Never null.</summary>
        public PrintSettings Settings
        {
            get => _settings;
            set => _settings = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>How to print on this machine. Never null.</summary>
        public PrintJobOptions Options
        {
            get => _options;
            set => _options = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string? DocumentTitle { get; set; }
        public string? UserName { get; set; }
        public string? TenantId { get; set; }

        // ----------------------------------------------------------------- fluent: settings

        public PrintJob WithSettings(PrintSettings settings) { Settings = settings; return this; }

        public PrintJob Configure(Action<PrintSettings> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));
            configure(_settings);
            return this;
        }

        public PrintJob WithPages(PageSelection pages)
        {
            _settings.Pages = pages ?? throw new ArgumentNullException(nameof(pages));
            return this;
        }

        public PrintJob WithCopies(int copies)
        {
            if (copies < 1) throw new ArgumentOutOfRangeException(nameof(copies), "At least one copy is required.");
            _settings.Copies = copies;
            return this;
        }

        public PrintJob WithCopies(int copies, bool collate)
        {
            WithCopies(copies);
            _settings.Collate = collate;
            return this;
        }

        public PrintJob WithCollate(bool collate = true) { _settings.Collate = collate; return this; }
        public PrintJob WithDuplex(Duplex duplex) { _settings.Duplex = duplex; return this; }
        public PrintJob WithColor(ColorMode color) { _settings.Color = color; return this; }
        public PrintJob WithPaper(PaperSize paper) { _settings.Paper = paper; return this; }
        public PrintJob WithOrientation(PageOrientation orientation) { _settings.Orientation = orientation; return this; }
        public PrintJob WithScale(ScaleMode scale) { _settings.Scale = scale; return this; }

        /// <summary>Sets <see cref="ScaleMode.Custom"/> with the given percentage.</summary>
        public PrintJob WithCustomScale(double percent)
        {
            if (!(percent > 0) || double.IsInfinity(percent))
                throw new ArgumentOutOfRangeException(nameof(percent), "Scale must be a positive finite percentage.");
            _settings.Scale = ScaleMode.Custom;
            _settings.CustomScalePercent = percent;
            return this;
        }

        public PrintJob WithAlignment(ContentAlignment alignment) { _settings.Alignment = alignment; return this; }
        public PrintJob WithMargins(Margins margins) { _settings.Margins = margins; return this; }
        public PrintJob WithAutoRotate(bool autoRotate = true) { _settings.AutoRotate = autoRotate; return this; }
        public PrintJob WithSilent(bool silent = true) { _settings.Silent = silent; return this; }

        // ----------------------------------------------------------------- fluent: metadata

        public PrintJob WithJobId(Guid jobId) { JobId = jobId; return this; }
        public PrintJob WithTitle(string? title) { DocumentTitle = title; return this; }
        public PrintJob WithUser(string? userName) { UserName = userName; return this; }
        public PrintJob WithTenant(string? tenantId) { TenantId = tenantId; return this; }

        // ----------------------------------------------------------------- fluent: options

        public PrintJob WithOptions(PrintJobOptions options) { Options = options; return this; }

        public PrintJob WithOptions(Action<PrintJobOptions> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));
            configure(_options);
            return this;
        }

        public PrintJob WithRasterizer(IPageRasterizer rasterizer)
        {
            _options.Rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
            return this;
        }

        public PrintJob WithAuditSink(IPrintAuditSink sink)
        {
            _options.AuditSink = sink ?? throw new ArgumentNullException(nameof(sink));
            return this;
        }

        /// <summary>Renders at <paramref name="dpi"/> on both axes instead of the target's resolution.</summary>
        public PrintJob WithRenderDpi(double dpi) => WithRenderDpi(dpi, dpi);

        public PrintJob WithRenderDpi(double dpiX, double dpiY)
        {
            _options.RenderDpiX = dpiX;
            _options.RenderDpiY = dpiY;
            return this;
        }

        public PrintJob WithCopyHandling(CopyHandling handling) { _options.CopyHandling = handling; return this; }

        public PrintJob WithMonochrome(MonochromeConversion conversion)
        {
            _options.Monochrome = conversion;
            return this;
        }

        public PrintJob WithMonochromeThreshold(byte threshold)
        {
            _options.Monochrome = MonochromeConversion.Threshold;
            _options.MonochromeThreshold = threshold;
            return this;
        }

        public PrintJob WithMarginReference(MarginReference reference) { _options.MarginReference = reference; return this; }
        public PrintJob WithAnnotations(bool include = true) { _options.IncludeAnnotations = include; return this; }
        public PrintJob WithFormFields(bool include = true) { _options.IncludeFormFields = include; return this; }
        public PrintJob WithAntiAlias(bool antiAlias = true) { _options.AntiAlias = antiAlias; return this; }
        public PrintJob WithThrowOnFailure(bool throwOnFailure = true) { _options.ThrowOnFailure = throwOnFailure; return this; }
        public PrintJob WithProgress(IProgress<PrintProgress>? progress) { _options.Progress = progress; return this; }

        // ----------------------------------------------------------------- planning

        /// <summary>
        /// Works out what will be printed without rendering anything. Opens the document with the
        /// configured rasteriser to learn its page count.
        /// </summary>
        /// <param name="capabilities">The device to plan for (decides who makes copies). Default: <see cref="PrintTargetCapabilities.Default"/>.</param>
        public PrintPlan Plan(PrintTargetCapabilities? capabilities = null)
        {
            IPageRasterizer rasterizer = RequireRasterizer(_options);
            int pageCount;
            using (IRasterDocument document = rasterizer.Open(_pdf))
                pageCount = document.PageCount;
            return PrintPlan.Create(pageCount, _settings,
                TargetMakesCopies(_settings, capabilities ?? PrintTargetCapabilities.Default, _options.CopyHandling));
        }

        /// <summary>Decides whether the device, rather than the job, should make the copies.</summary>
        public static bool TargetMakesCopies(PrintSettings settings, PrintTargetCapabilities capabilities, CopyHandling handling)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
            if (settings.Copies <= 1) return false;
            return handling switch
            {
                CopyHandling.Target => true,
                CopyHandling.Software => false,
                CopyHandling.Auto => capabilities.SupportsCopies && (!settings.Collate || capabilities.SupportsCollate),
                _ => throw new ArgumentOutOfRangeException(nameof(handling))
            };
        }

        /// <summary>The pixel format pages are delivered in, for these settings and this device.</summary>
        public static RasterPixelFormat OutputPixelFormatFor(PrintSettings settings, PrintTargetCapabilities capabilities)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
            return settings.Color switch
            {
                ColorMode.Monochrome => RasterPixelFormat.BlackWhite1,
                ColorMode.Grayscale => RasterPixelFormat.Gray8,
                _ when !capabilities.SupportsColor => RasterPixelFormat.Gray8,
                _ => capabilities.PreferredPixelFormat == RasterPixelFormat.Rgb24 ? RasterPixelFormat.Rgb24 : RasterPixelFormat.Bgra32
            };
        }

        // ----------------------------------------------------------------- printing

        /// <summary>
        /// Prints the job to <paramref name="target"/>: plans it, renders each needed page once at a time,
        /// converts pixels to the output format, and sends every impression in order. Records exactly one
        /// audit event.
        /// </summary>
        /// <returns>
        /// The outcome. Failures and cancellation are reported here (status Failed / Cancelled) unless
        /// <see cref="PrintJobOptions.ThrowOnFailure"/> is set, in which case the exception is rethrown
        /// after the job is aborted and audited.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="InvalidOperationException">No rasteriser is configured.</exception>
        public PrintResult PrintTo(IPrintTarget target, CancellationToken cancellationToken = default)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            PrintJobOptions options = _options.Clone();
            IPageRasterizer rasterizer = RequireRasterizer(options);
            PrintSettings settings = _settings.Clone();
            Guid jobId = JobId;

            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            PrintPlan? plan = null;
            bool jobStarted = false;
            int impressionsPrinted = 0;
            int sheetsPrinted = 0;
            var pagesPrinted = new HashSet<int>();
            PrintJobStatus status;
            Exception? failure = null;

            IRasterDocument? document = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                PrintTargetCapabilities capabilities = target.GetCapabilities(settings)
                    ?? throw new InvalidOperationException("The print target returned no capabilities.");

                document = rasterizer.Open(_pdf)
                    ?? throw new InvalidOperationException("The rasteriser returned no document.");
                plan = PrintPlan.Create(document.PageCount, settings,
                    TargetMakesCopies(settings, capabilities, options.CopyHandling));
                RasterPixelFormat outputFormat = OutputPixelFormatFor(settings, capabilities);
                var context = new PrintJobContext(jobId, DocumentTitle, settings, plan, capabilities, outputFormat);

                target.StartJob(context);
                jobStarted = true;

                RasterPage? current = null;
                PagePlacement? currentPlacement = null;
                int currentPage = 0;
                try
                {
                    IReadOnlyList<PlannedImpression> impressions = plan.Impressions;
                    for (int i = 0; i < impressions.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        PlannedImpression impression = impressions[i];

                        if (impression.IsBlank)
                        {
                            target.PrintPage(new PrintedPage(impression, i, null, null));
                        }
                        else
                        {
                            if (current is null || currentPage != impression.SourcePageNumber)
                            {
                                current?.Dispose();
                                current = null;
                                currentPage = impression.SourcePageNumber;
                                currentPlacement = PageLayout.Compute(document.GetPageSize(currentPage), settings,
                                                                      capabilities, options.MarginReference);
                                current = RenderPage(document, currentPage, currentPlacement, capabilities,
                                                     outputFormat, options, cancellationToken);
                            }
                            target.PrintPage(new PrintedPage(impression, i, currentPlacement, current));
                            impressionsPrinted++;
                            pagesPrinted.Add(impression.SourcePageNumber);
                        }

                        sheetsPrinted = impression.SheetIndex + 1;
                        options.Progress?.Report(new PrintProgress(i + 1, impressions.Count, impression.SourcePageNumber));
                    }
                }
                finally
                {
                    current?.Dispose();
                }

                target.EndJob();
                status = PrintJobStatus.Printed;
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                status = PrintJobStatus.Cancelled;
                failure = ex;
                if (jobStarted) TryAbort(target, null);
            }
            catch (Exception ex)
            {
                status = PrintJobStatus.Failed;
                failure = ex;
                if (jobStarted) TryAbort(target, ex);
            }
            finally
            {
                document?.Dispose();
            }

            string? error = status == PrintJobStatus.Failed ? failure!.Message : null;
            options.AuditSink.Record(new PrintAuditEvent
            {
                JobId = jobId,
                DocumentTitle = DocumentTitle,
                PageCount = plan?.SelectedPages.Count ?? 0,
                Copies = settings.Copies,
                PrinterName = SafeName(target),
                MachineName = Environment.MachineName,
                UserName = UserName,
                Status = status,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                TenantId = TenantId,
                Error = error
            });

            if (failure is not null && options.ThrowOnFailure)
                ExceptionDispatchInfo.Capture(failure).Throw();

            return new PrintResult
            {
                JobId = jobId,
                Status = status,
                PagesPrinted = pagesPrinted.Count,
                ImpressionsPrinted = impressionsPrinted,
                SheetsPrinted = sheetsPrinted,
                Error = error
            };
        }

        // ----------------------------------------------------------------- spool

        /// <summary>The spool envelope for this job: PDF, a snapshot of the settings, and the metadata.</summary>
        public SpoolEnvelope ToEnvelope() => new(_pdf, _settings.Clone())
        {
            JobId = JobId,
            DocumentTitle = DocumentTitle,
            UserName = UserName,
            TenantId = TenantId
        };

        public void SaveSpoolTo(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            ToEnvelope().Serialize(stream);
        }

        /// <summary>Writes the spool envelope to a stream. The stream is not closed.</summary>
        public void SaveSpoolTo(Stream output) => ToEnvelope().Serialize(output);

        public byte[] ToSpoolArray() => ToEnvelope().ToArray();

        // ----------------------------------------------------------------- helpers

        private static RasterPage RenderPage(IRasterDocument document, int pageNumber, PagePlacement placement,
                                             PrintTargetCapabilities capabilities, RasterPixelFormat outputFormat,
                                             PrintJobOptions options, CancellationToken cancellationToken)
        {
            double deviceDpiX = options.RenderDpiX ?? capabilities.DpiX;
            double deviceDpiY = options.RenderDpiY ?? capabilities.DpiY;
            var request = new RasterRequest
            {
                DpiX = deviceDpiX * placement.ScaleFactor,
                DpiY = deviceDpiY * placement.ScaleFactor,
                PixelFormat = outputFormat == RasterPixelFormat.BlackWhite1 ? RasterPixelFormat.Gray8 : outputFormat,
                Rotation = placement.Rotation,
                IncludeAnnotations = options.IncludeAnnotations,
                IncludeFormFields = options.IncludeFormFields,
                AntiAlias = options.AntiAlias
            };

            RasterPage raw = document.Render(pageNumber, request, cancellationToken)
                ?? throw new InvalidOperationException("The rasteriser returned no page for page " + pageNumber + ".");
            if (raw.PixelFormat == outputFormat) return raw;
            try
            {
                return PixelConverter.Convert(raw, outputFormat, options.Monochrome, options.MonochromeThreshold);
            }
            finally
            {
                raw.Dispose();
            }
        }

        private static IPageRasterizer RequireRasterizer(PrintJobOptions options)
            => options.Rasterizer ?? throw new InvalidOperationException(
                "No rasteriser is configured. Set PrintJob.Options.Rasterizer or call WithRasterizer(...) " +
                "(for PDF documents, use the Chuvadi.Print.Pdf adapter).");

        private static void TryAbort(IPrintTarget target, Exception? reason)
        {
            try
            {
                target.AbortJob(reason);
            }
            catch
            {
                // The original failure is what matters; an abort failure must not mask it.
            }
        }

        private static string SafeName(IPrintTarget target)
        {
            try
            {
                return target.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static byte[] ReadAll(Stream stream)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
