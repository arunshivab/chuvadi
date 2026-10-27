using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Chuvadi.Print;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>A2 verification groups: layout, plan, PrintJob orchestration, conversion, spool v2, PNG target.</summary>
    public static partial class VerificationGroups
    {
        public static IReadOnlyList<Action> All()
        {
            var all = new List<Action>(A1Groups());
            all.AddRange(A2Groups());
            return all;
        }

        private static Action[] A2Groups() => new Action[]
        {
            LayoutActualSizeHonoursAllNineAlignments,
            LayoutScaleModesComputeExpectedFactors,
            LayoutOrientationShapesSheetAndRotation,
            LayoutAutoRotateTurnsMismatchedPages,
            LayoutMarginReferenceModesDiffer,
            LayoutFlagsClippingAndRejectsImpossibleMargins,
            PlanExpandsCollatedAndUncollatedCopies,
            PlanDuplexPairsSidesAndInsertsBlankBacks,
            PlanDefersCopiesToCapableTarget,
            PrintToEmitsPlannedImpressionsInOrder,
            PrintToRendersAtDeviceResolution,
            PrintToKeepsMemoryBounded,
            PrintToDeliversRequestedColourMode,
            PrintToRequiresRasterizer,
            PrintToAuditsExactlyOnceOnSuccess,
            PrintToReportsFailureAndAuditsOnce,
            PrintToReportsCancellationAndAuditsOnce,
            PrintToRethrowsWhenAsked,
            PrintToReportsProgress,
            FluentSettersCoverEverySetting,
            SpoolRoundTripsThroughPrintJob,
            SpoolEnvelopeStillReadsVersion1,
            MonochromeConversionsProduceDistinctOutput,
            ImageFileTargetWritesValidPngs
        };

        private static readonly PageDimensions A4Page = PageDimensions.FromPaper(PaperSize.A4);

        private static bool Near(double a, double b, double tolerance = 1e-6) => Math.Abs(a - b) <= tolerance;

        private static PrintJob FakeJob(byte[] doc, FakeRasterizer rasterizer, PrintSettings? settings = null)
            => new PrintJob(doc, settings ?? new PrintSettings()).WithRasterizer(rasterizer);

        // ------------------------------------------------------------------ layout

        public static void LayoutActualSizeHonoursAllNineAlignments()
        {
            var page = new PageDimensions(200, 100);
            var caps = PrintTargetCapabilities.Default;
            double sw = PaperSize.A4.WidthPoints, sh = PaperSize.A4.HeightPoints;
            var expect = new (ContentAlignment Align, double X, double Y)[]
            {
                (ContentAlignment.TopLeft, 0, 0), (ContentAlignment.TopCenter, (sw - 200) / 2, 0), (ContentAlignment.TopRight, sw - 200, 0),
                (ContentAlignment.CenterLeft, 0, (sh - 100) / 2), (ContentAlignment.Center, (sw - 200) / 2, (sh - 100) / 2), (ContentAlignment.CenterRight, sw - 200, (sh - 100) / 2),
                (ContentAlignment.BottomLeft, 0, sh - 100), (ContentAlignment.BottomCenter, (sw - 200) / 2, sh - 100), (ContentAlignment.BottomRight, sw - 200, sh - 100)
            };
            foreach (var e in expect)
            {
                var s = new PrintSettings { Scale = ScaleMode.ActualSize, Margins = Margins.None, Alignment = e.Align };
                PagePlacement p = PageLayout.Compute(page, s, caps);
                Require(Near(p.ContentBounds.X, e.X) && Near(p.ContentBounds.Y, e.Y), "alignment " + e.Align + " -> " + p.ContentBounds);
                Require(Near(p.ContentBounds.Width, 200) && Near(p.ContentBounds.Height, 100), "actual size keeps dimensions");
                Require(Near(p.ScaleFactor, 1) && !p.IsClipped, "actual size scale 1, not clipped");
            }
        }

        public static void LayoutScaleModesComputeExpectedFactors()
        {
            var caps = PrintTargetCapabilities.Default;
            double aw = PaperSize.A4.WidthPoints - 72, ah = PaperSize.A4.HeightPoints - 72; // Margins.Default = 36pt each
            var letter = PageDimensions.FromPaper(PaperSize.Letter);
            var small = new PageDimensions(100, 50);

            double Scale(PageDimensions page, ScaleMode mode, double pct = 100)
                => PageLayout.Compute(page, new PrintSettings { Scale = mode, CustomScalePercent = pct }, caps).ScaleFactor;

            double fitLetter = Math.Min(aw / letter.WidthPoints, ah / letter.HeightPoints);
            Require(Near(Scale(letter, ScaleMode.FitToPage), fitLetter), "fit to page");
            Require(Near(Scale(letter, ScaleMode.ShrinkToFit), fitLetter), "shrink to fit shrinks large page");
            Require(Near(Scale(small, ScaleMode.ShrinkToFit), 1.0), "shrink to fit never enlarges");
            Require(Near(Scale(small, ScaleMode.FitToPage), Math.Min(aw / 100, ah / 50)), "fit to page enlarges small page");
            Require(Near(Scale(letter, ScaleMode.FitToWidth), aw / letter.WidthPoints), "fit to width");
            Require(Near(Scale(letter, ScaleMode.FitToHeight), ah / letter.HeightPoints), "fit to height");
            Require(Near(Scale(letter, ScaleMode.ActualSize), 1.0), "actual size");
            Require(Near(Scale(letter, ScaleMode.Custom, 42.5), 0.425), "custom percent");

            bool threw = false;
            try { Scale(letter, ScaleMode.Custom, 0); } catch (InvalidOperationException) { threw = true; }
            Require(threw, "custom percent must be positive");
        }

        public static void LayoutOrientationShapesSheetAndRotation()
        {
            var caps = PrintTargetCapabilities.Default;
            var expected = new (PageOrientation Orientation, bool Landscape, PageRotation Rotation)[]
            {
                (PageOrientation.Portrait, false, PageRotation.None),
                (PageOrientation.Landscape, true, PageRotation.None),
                (PageOrientation.ReversePortrait, false, PageRotation.Rotate180),
                (PageOrientation.ReverseLandscape, true, PageRotation.Rotate180)
            };
            foreach (var e in expected)
            {
                PagePlacement p = PageLayout.Compute(A4Page, new PrintSettings { Orientation = e.Orientation }, caps);
                Require(p.SheetSize.IsLandscape == e.Landscape, e.Orientation + " sheet shape");
                Require(p.Rotation == e.Rotation, e.Orientation + " rotation");
            }
            // Paper defined landscape (Ledger 17x11) still prints portrait when asked.
            PagePlacement ledger = PageLayout.Compute(A4Page, new PrintSettings { Paper = PaperSize.Ledger }, caps);
            Require(ledger.SheetSize.IsPortrait && Near(ledger.SheetSize.WidthPoints, 11 * 72), "portrait normalises paper");
        }

        public static void LayoutAutoRotateTurnsMismatchedPages()
        {
            var caps = PrintTargetCapabilities.Default;
            var landscapePage = A4Page.Swap();
            PagePlacement off = PageLayout.Compute(landscapePage, new PrintSettings(), caps);
            PagePlacement on = PageLayout.Compute(landscapePage, new PrintSettings { AutoRotate = true }, caps);
            Require(off.Rotation == PageRotation.None, "auto-rotate off leaves page");
            Require(on.Rotation == PageRotation.Clockwise90, "auto-rotate turns landscape page on portrait sheet");
            Require(on.ScaleFactor > off.ScaleFactor, "auto-rotate gives a larger fit");
            Require(Near(on.ContentBounds.Width / on.ContentBounds.Height, A4Page.WidthPoints / A4Page.HeightPoints, 1e-9),
                    "rotated content has portrait aspect");

            PagePlacement reverse = PageLayout.Compute(landscapePage,
                new PrintSettings { AutoRotate = true, Orientation = PageOrientation.ReversePortrait }, caps);
            Require(reverse.Rotation == PageRotation.Clockwise270, "reverse portrait + auto-rotate = 270");

            PagePlacement matching = PageLayout.Compute(A4Page, new PrintSettings { AutoRotate = true }, caps);
            Require(matching.Rotation == PageRotation.None, "matching orientation not rotated");
        }

        public static void LayoutMarginReferenceModesDiffer()
        {
            var caps = new PrintTargetCapabilities { HardMargins = new Margins(10, 20, 30, 40) };
            var settings = new PrintSettings { Margins = Margins.Uniform(25) };
            PagePlacement edge = PageLayout.Compute(A4Page, settings, caps, MarginReference.SheetEdge);
            PagePlacement area = PageLayout.Compute(A4Page, settings, caps, MarginReference.PrintableArea);
            Require(edge.EffectiveMargins == new Margins(25, 25, 30, 40), "sheet edge uses larger per edge: " + edge.EffectiveMargins);
            Require(area.EffectiveMargins == new Margins(35, 45, 55, 65), "printable area adds: " + area.EffectiveMargins);
            Require(Near(edge.PrintableArea.X, 25) && Near(edge.PrintableArea.Y, 25), "printable area origin");
            Require(Near(edge.PrintableArea.Width, PaperSize.A4.WidthPoints - 55), "printable width");
        }

        public static void LayoutFlagsClippingAndRejectsImpossibleMargins()
        {
            var caps = PrintTargetCapabilities.Default;
            PagePlacement big = PageLayout.Compute(PageDimensions.FromPaper(PaperSize.A3),
                new PrintSettings { Scale = ScaleMode.ActualSize }, caps);
            Require(big.IsClipped, "A3 at actual size on A4 is clipped");
            PagePlacement fit = PageLayout.Compute(PageDimensions.FromPaper(PaperSize.A3), new PrintSettings(), caps);
            Require(!fit.IsClipped, "fit to page is never clipped");

            bool threw = false;
            try { PageLayout.Compute(A4Page, new PrintSettings { Margins = Margins.Uniform(400) }, caps); }
            catch (InvalidOperationException) { threw = true; }
            Require(threw, "margins wider than the sheet are rejected");
        }

        // ------------------------------------------------------------------ plan

        public static void PlanExpandsCollatedAndUncollatedCopies()
        {
            PrintPlan collated = PrintPlan.Create(3, new PrintSettings { Copies = 2, Collate = true }, false);
            Require(Pages(collated) == "1,2,3,1,2,3", "collated order " + Pages(collated));
            Require(collated.SheetCount == 6 && collated.Impressions[3].CopyNumber == 2, "collated sheets and copy numbers");

            PrintPlan uncollated = PrintPlan.Create(3, new PrintSettings { Copies = 2, Collate = false }, false);
            Require(Pages(uncollated) == "1,1,2,2,3,3", "uncollated order " + Pages(uncollated));

            PrintPlan selected = PrintPlan.Create(10, new PrintSettings { Pages = PageSelection.Range("2-3, 9") }, false);
            Require(Pages(selected) == "2,3,9" && selected.DocumentPageCount == 10, "selection respected");

            PrintPlan none = PrintPlan.Create(5, new PrintSettings { Pages = PageSelection.None }, false);
            Require(none.Impressions.Count == 0 && none.SheetCount == 0, "empty selection");
        }

        public static void PlanDuplexPairsSidesAndInsertsBlankBacks()
        {
            PrintPlan single = PrintPlan.Create(3, new PrintSettings { Duplex = Duplex.Vertical }, false);
            Require(Pages(single) == "1,2,3" && single.SheetCount == 2, "duplex single copy: no trailing blank");
            Require(single.Impressions[1].IsBackSide && single.Impressions[1].SheetIndex == 0, "page 2 is back of sheet 0");

            PrintPlan collated = PrintPlan.Create(3, new PrintSettings { Duplex = Duplex.Horizontal, Copies = 2 }, false);
            Require(Pages(collated) == "1,2,3,_,1,2,3", "blank keeps copy 2 starting on a fresh sheet: " + Pages(collated));
            Require(collated.SheetCount == 4 && collated.PageImpressionCount == 6, "collated duplex counts");

            PrintPlan uncollated = PrintPlan.Create(3, new PrintSettings { Duplex = Duplex.Vertical, Copies = 2, Collate = false }, false);
            Require(Pages(uncollated) == "1,2,1,2,3,_,3", "uncollated duplex repeats whole sheets: " + Pages(uncollated));
        }

        public static void PlanDefersCopiesToCapableTarget()
        {
            var s = new PrintSettings { Copies = 3, Collate = true };
            var copier = new PrintTargetCapabilities { SupportsCopies = true, SupportsCollate = true };
            var noCollate = new PrintTargetCapabilities { SupportsCopies = true, SupportsCollate = false };
            Require(PrintJob.TargetMakesCopies(s, copier, CopyHandling.Auto), "capable target makes copies");
            Require(!PrintJob.TargetMakesCopies(s, noCollate, CopyHandling.Auto), "collation needs collating target");
            Require(PrintJob.TargetMakesCopies(new PrintSettings { Copies = 3, Collate = false }, noCollate, CopyHandling.Auto), "uncollated ok");
            Require(!PrintJob.TargetMakesCopies(s, copier, CopyHandling.Software), "software forced");
            Require(PrintJob.TargetMakesCopies(s, PrintTargetCapabilities.Default, CopyHandling.Target), "target forced");
            Require(!PrintJob.TargetMakesCopies(new PrintSettings(), copier, CopyHandling.Target), "single copy never deferred");

            PrintPlan plan = PrintPlan.Create(2, s, targetHandlesCopies: true);
            Require(Pages(plan) == "1,2" && plan.TargetHandlesCopies && plan.Copies == 3, "deferred plan sends one copy");
        }

        // ------------------------------------------------------------------ PrintJob

        public static void PrintToEmitsPlannedImpressionsInOrder()
        {
            var rasterizer = new FakeRasterizer();
            var target = new RecordingTarget();
            PrintResult result = FakeJob(FakeDocument.Uniform(3, A4Page), rasterizer)
                .WithCopies(2, collate: false).WithDuplex(Duplex.Vertical)
                .PrintTo(target);

            Require(result.Status == PrintJobStatus.Printed, "printed: " + result.Error);
            Require(string.Join(",", target.Events) == "caps,start,page1,page2,page1,page2,page3,blank,page3,end",
                    "event order " + string.Join(",", target.Events));
            Require(result.PagesPrinted == 3 && result.ImpressionsPrinted == 6 && result.SheetsPrinted == 4, "result counts");
            Require(rasterizer.OpenCount == 1 && rasterizer.DocumentsDisposed == 1, "document opened once and closed");
            Require(target.Context!.Plan.Impressions.Count == 7, "context carries plan");
        }

        public static void PrintToRendersAtDeviceResolution()
        {
            var rasterizer = new FakeRasterizer();
            var target = new RecordingTarget { Capabilities = new PrintTargetCapabilities { DpiX = 150, DpiY = 100 } };
            FakeJob(FakeDocument.Create(new PageDimensions(300, 200)), rasterizer)
                .WithScale(ScaleMode.ActualSize).WithMargins(Margins.None)
                .PrintTo(target);
            var record = target.Pages[0];
            Require(record.PixelWidth == (int)Math.Round(300 / 72.0 * 150), "width at device dpi: " + record.PixelWidth);
            Require(record.PixelHeight == (int)Math.Round(200 / 72.0 * 100), "height at device dpi: " + record.PixelHeight);

            var scaled = new RecordingTarget { Capabilities = new PrintTargetCapabilities { DpiX = 72, DpiY = 72 } };
            var r2 = new FakeRasterizer();
            FakeJob(FakeDocument.Create(new PageDimensions(300, 200)), r2).WithCustomScale(50).WithRenderDpi(144).PrintTo(scaled);
            Require(Near(r2.Renders[0].Request.DpiX, 72), "render dpi = override x scale");
            Require(scaled.Pages[0].PixelWidth == 300, "raster covers content bounds at render dpi");

            var rotated = new RecordingTarget();
            var r3 = new FakeRasterizer();
            FakeJob(FakeDocument.Create(A4Page.Swap()), r3).WithAutoRotate().PrintTo(rotated);
            Require(r3.Renders[0].Request.Rotation == PageRotation.Clockwise90, "rotation passed to rasteriser");
            Require(rotated.Pages[0].PixelHeight > rotated.Pages[0].PixelWidth, "rotated raster is portrait");
        }

        public static void PrintToKeepsMemoryBounded()
        {
            foreach (ColorMode mode in new[] { ColorMode.Color, ColorMode.Grayscale, ColorMode.Monochrome })
            {
                var rasterizer = new FakeRasterizer();
                var target = new RecordingTarget { LivePageProbe = () => rasterizer.LivePages };
                FakeJob(FakeDocument.Uniform(12, A4Page), rasterizer)
                    .WithColor(mode).WithCopies(2, collate: true)
                    .PrintTo(target);
                Require(target.Pages.Count == 24, mode + ": all impressions");
                foreach (var r in target.Pages)
                    Require(r.LiveRasterizerPages <= 1, mode + ": at most one rasteriser page alive, saw " + r.LiveRasterizerPages);
                Require(rasterizer.LivePages == 0, mode + ": every page released after the job");
            }
        }

        public static void PrintToDeliversRequestedColourMode()
        {
            var cases = new (ColorMode Mode, PrintTargetCapabilities Caps, RasterPixelFormat Expected)[]
            {
                (ColorMode.Color, new PrintTargetCapabilities(), RasterPixelFormat.Bgra32),
                (ColorMode.Color, new PrintTargetCapabilities { PreferredPixelFormat = RasterPixelFormat.Rgb24 }, RasterPixelFormat.Rgb24),
                (ColorMode.Color, new PrintTargetCapabilities { SupportsColor = false }, RasterPixelFormat.Gray8),
                (ColorMode.Grayscale, new PrintTargetCapabilities(), RasterPixelFormat.Gray8),
                (ColorMode.Monochrome, new PrintTargetCapabilities(), RasterPixelFormat.BlackWhite1)
            };
            foreach (var c in cases)
            {
                foreach (RasterPixelFormat? forced in new RasterPixelFormat?[] { null, RasterPixelFormat.Bgra32, RasterPixelFormat.Rgb24, RasterPixelFormat.Gray8 })
                {
                    var rasterizer = new FakeRasterizer { ForcedFormat = forced };
                    var target = new RecordingTarget { Capabilities = WithDpi(c.Caps, 36) };
                    PrintResult result = FakeJob(FakeDocument.Uniform(1, A4Page), rasterizer).WithColor(c.Mode).PrintTo(target);
                    Require(result.Status == PrintJobStatus.Printed, "printed " + c.Mode + " " + forced + ": " + result.Error);
                    Require(target.Pages[0].Format == c.Expected,
                            c.Mode + " from " + (forced?.ToString() ?? "requested") + " -> " + target.Pages[0].Format + ", expected " + c.Expected);
                    Require(target.Context!.OutputPixelFormat == c.Expected, "context output format");
                    Require(target.Pages[0].BlackPixels > 0, "border survives conversion");
                }
            }
        }

        public static void PrintToRequiresRasterizer()
        {
            bool threw = false;
            try { new PrintJob(FakeDocument.Uniform(1, A4Page)).PrintTo(new RecordingTarget()); }
            catch (InvalidOperationException) { threw = true; }
            Require(threw, "printing without a rasteriser throws");

            threw = false;
            try { new PrintJob(FakeDocument.Uniform(1, A4Page)).WithRasterizer(new FakeRasterizer()).PrintTo(null!); }
            catch (ArgumentNullException) { threw = true; }
            Require(threw, "null target throws");
        }

        public static void PrintToAuditsExactlyOnceOnSuccess()
        {
            var sink = new MemoryAuditSink();
            var job = FakeJob(FakeDocument.Uniform(4, A4Page), new FakeRasterizer())
                .WithPages(PageSelection.Odd).WithCopies(3).WithTitle("Discharge summary")
                .WithUser("arun").WithTenant("apulki").WithAuditSink(sink);
            PrintResult result = job.PrintTo(new RecordingTarget { Name = "Ward-3 LaserJet" });

            Require(sink.Events.Count == 1, "one audit event, saw " + sink.Events.Count);
            PrintAuditEvent e = sink.Events[0];
            Require(e.JobId == job.JobId && result.JobId == job.JobId, "job id flows to audit and result");
            Require(e.Status == PrintJobStatus.Printed && e.Error is null, "status printed");
            Require(e.PageCount == 2 && e.Copies == 3, "pages and copies");
            Require(e.DocumentTitle == "Discharge summary" && e.UserName == "arun" && e.TenantId == "apulki", "metadata");
            Require(e.PrinterName == "Ward-3 LaserJet" && e.MachineName == Environment.MachineName, "printer and machine");
            Require(e.CompletedAt.HasValue && e.CompletedAt >= e.StartedAt, "timestamps");
        }

        public static void PrintToReportsFailureAndAuditsOnce()
        {
            var sink = new MemoryAuditSink();
            var rasterizer = new FakeRasterizer { FailOnPage = 2 };
            var target = new RecordingTarget();
            PrintResult result = FakeJob(FakeDocument.Uniform(3, A4Page), rasterizer).WithAuditSink(sink).PrintTo(target);
            Require(result.Status == PrintJobStatus.Failed && result.Error!.Contains("page 2"), "failed result: " + result.Error);
            Require(result.ImpressionsPrinted == 1 && result.PagesPrinted == 1, "partial counts");
            Require(target.Events[target.Events.Count - 1] == "abort" && target.AbortReason is not null, "target aborted with reason");
            Require(sink.Events.Count == 1 && sink.Events[0].Status == PrintJobStatus.Failed && sink.Events[0].Error is not null, "failure audited once");
            Require(rasterizer.LivePages == 0 && rasterizer.DocumentsDisposed == 1, "resources released on failure");

            var sink2 = new MemoryAuditSink();
            var device = new RecordingTarget { FailOnImpression = 0 };
            PrintResult r2 = FakeJob(FakeDocument.Uniform(2, A4Page), new FakeRasterizer()).WithAuditSink(sink2).PrintTo(device);
            Require(r2.Status == PrintJobStatus.Failed && sink2.Events.Count == 1, "device failure reported and audited");
        }

        public static void PrintToReportsCancellationAndAuditsOnce()
        {
            var sink = new MemoryAuditSink();
            using var cts = new CancellationTokenSource();
            var target = new RecordingTarget { AfterImpression = i => { if (i == 1) cts.Cancel(); } };
            var rasterizer = new FakeRasterizer();
            PrintResult result = FakeJob(FakeDocument.Uniform(5, A4Page), rasterizer).WithAuditSink(sink).PrintTo(target, cts.Token);
            Require(result.Status == PrintJobStatus.Cancelled && result.Error is null, "cancelled result");
            Require(result.ImpressionsPrinted == 2, "stopped after two impressions");
            Require(target.Events[target.Events.Count - 1] == "abort" && target.AbortReason is null, "abort without reason on cancel");
            Require(sink.Events.Count == 1 && sink.Events[0].Status == PrintJobStatus.Cancelled, "cancellation audited once");
            Require(rasterizer.LivePages == 0, "pages released on cancel");

            using var early = new CancellationTokenSource();
            early.Cancel();
            var untouched = new RecordingTarget();
            PrintResult r2 = FakeJob(FakeDocument.Uniform(1, A4Page), new FakeRasterizer()).PrintTo(untouched, early.Token);
            Require(r2.Status == PrintJobStatus.Cancelled && untouched.Events.Count == 0, "pre-cancelled job never reaches target");
        }

        public static void PrintToRethrowsWhenAsked()
        {
            var sink = new MemoryAuditSink();
            bool threw = false;
            try
            {
                FakeJob(FakeDocument.Uniform(2, A4Page), new FakeRasterizer { FailOnPage = 1 })
                    .WithAuditSink(sink).WithThrowOnFailure().PrintTo(new RecordingTarget());
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Simulated")) { threw = true; }
            Require(threw, "original exception rethrown");
            Require(sink.Events.Count == 1 && sink.Events[0].Status == PrintJobStatus.Failed, "audited before rethrow");
        }

        public static void PrintToReportsProgress()
        {
            var progress = new ListProgress();
            FakeJob(FakeDocument.Uniform(3, A4Page), new FakeRasterizer()).WithProgress(progress).PrintTo(new RecordingTarget());
            Require(progress.Reports.Count == 3, "one report per impression");
            Require(progress.Reports[2].ImpressionsCompleted == 3 && progress.Reports[2].ImpressionsTotal == 3
                    && Near(progress.Reports[2].Fraction, 1.0), "final report complete");
            Require(progress.Reports[0].CurrentPageNumber == 1, "page number reported");
        }

        public static void FluentSettersCoverEverySetting()
        {
            var raster = new FakeRasterizer();
            var sink = new MemoryAuditSink();
            var progress = new ListProgress();
            var id = Guid.NewGuid();
            PrintJob job = new PrintJob(FakeDocument.Uniform(1, A4Page))
                .WithPages(PageSelection.Even).WithCopies(4).WithCollate(false).WithDuplex(Duplex.Horizontal)
                .WithColor(ColorMode.Grayscale).WithPaper(PaperSize.Legal).WithOrientation(PageOrientation.ReverseLandscape)
                .WithCustomScale(80).WithAlignment(ContentAlignment.BottomLeft).WithMargins(Margins.Narrow)
                .WithAutoRotate().WithSilent()
                .WithJobId(id).WithTitle("t").WithUser("u").WithTenant("x")
                .WithRasterizer(raster).WithAuditSink(sink).WithRenderDpi(200, 100)
                .WithCopyHandling(CopyHandling.Software).WithMonochrome(MonochromeConversion.ErrorDiffusion)
                .WithMarginReference(MarginReference.PrintableArea).WithAnnotations(false).WithFormFields(false)
                .WithAntiAlias(false).WithThrowOnFailure().WithProgress(progress);

            PrintSettings s = job.Settings;
            Require(s.Pages.ToCanonicalString() == "even" && s.Copies == 4 && !s.Collate && s.Duplex == Duplex.Horizontal, "pages/copies/collate/duplex");
            Require(s.Color == ColorMode.Grayscale && s.Paper == PaperSize.Legal && s.Orientation == PageOrientation.ReverseLandscape, "colour/paper/orientation");
            Require(s.Scale == ScaleMode.Custom && Near(s.CustomScalePercent, 80) && s.Alignment == ContentAlignment.BottomLeft, "scale/alignment");
            Require(s.Margins == Margins.Narrow && s.AutoRotate && s.Silent, "margins/auto-rotate/silent");
            Require(job.JobId == id && job.DocumentTitle == "t" && job.UserName == "u" && job.TenantId == "x", "metadata");
            PrintJobOptions o = job.Options;
            Require(ReferenceEquals(o.Rasterizer, raster) && ReferenceEquals(o.AuditSink, sink) && ReferenceEquals(o.Progress, progress), "references");
            Require(o.RenderDpiX == 200 && o.RenderDpiY == 100 && o.CopyHandling == CopyHandling.Software, "dpi/copy handling");
            Require(o.Monochrome == MonochromeConversion.ErrorDiffusion && o.MarginReference == MarginReference.PrintableArea, "mono/margin reference");
            Require(!o.IncludeAnnotations && !o.IncludeFormFields && !o.AntiAlias && o.ThrowOnFailure, "render flags");

            job.WithMonochromeThreshold(90);
            Require(o.Monochrome == MonochromeConversion.Threshold && o.MonochromeThreshold == 90, "threshold setter");
            job.Configure(x => x.Copies = 7);
            Require(job.Settings.Copies == 7, "configure");

            bool threw = false;
            try { job.WithCopies(0); } catch (ArgumentOutOfRangeException) { threw = true; }
            Require(threw, "zero copies rejected");
        }

        // ------------------------------------------------------------------ spool

        public static void SpoolRoundTripsThroughPrintJob()
        {
            byte[] doc = FakeDocument.Uniform(3, A4Page);
            var id = Guid.NewGuid();
            PrintJob original = new PrintJob(doc)
                .WithJobId(id).WithTitle("Radiotherapy plan").WithUser("physicist").WithTenant("apulki-pune")
                .WithPages(PageSelection.Range("1-2")).WithCopies(2).WithAutoRotate().WithColor(ColorMode.Monochrome);

            string path = Path.Combine(Path.GetTempPath(), "chuvadi-print-" + Guid.NewGuid().ToString("N") + ".cvpr");
            try
            {
                original.SaveSpoolTo(path);
                PrintJob loaded = PrintJob.LoadSpool(path);
                Require(loaded.JobId == id && loaded.DocumentTitle == "Radiotherapy plan", "id/title");
                Require(loaded.UserName == "physicist" && loaded.TenantId == "apulki-pune", "user/tenant");
                Require(loaded.Settings.AutoRotate && loaded.Settings.Copies == 2 && loaded.Settings.Color == ColorMode.Monochrome, "settings");
                Require(SeqEqualBytes(loaded.PdfBytes, doc), "document bytes");

                var target = new RecordingTarget();
                PrintResult result = loaded.WithRasterizer(new FakeRasterizer()).PrintTo(target);
                Require(result.Status == PrintJobStatus.Printed && result.ImpressionsPrinted == 4, "loaded job prints");
            }
            finally
            {
                File.Delete(path);
            }

            using var ms = new MemoryStream();
            original.SaveSpoolTo(ms);
            ms.Position = 0;
            Require(PrintJob.LoadSpool(ms).JobId == id, "stream round-trip");
            Require(PrintJob.LoadSpool(original.ToSpoolArray()).JobId == id, "array round-trip");
        }

        public static void SpoolEnvelopeStillReadsVersion1()
        {
            var settings = new PrintSettings { Copies = 2, AutoRotate = true, Pages = PageSelection.Odd };
            var envelope = new SpoolEnvelope(new byte[] { 1, 2, 3 }, settings) { JobId = Guid.NewGuid(), DocumentTitle = "x" };
            using var ms = new MemoryStream();
            envelope.Serialize(ms, 1);
            byte[] v1 = ms.ToArray();
            Require(v1[4] == 1, "version byte 1 written");
            SpoolEnvelope back = SpoolEnvelope.FromArray(v1);
            Require(back.Settings.Copies == 2 && back.Settings.Pages.ToCanonicalString() == "odd", "v1 settings read");
            Require(!back.Settings.AutoRotate && back.JobId is null && back.DocumentTitle is null, "v1 carries no v2 fields");
            Require(SpoolEnvelope.FromArray(envelope.ToArray()).DocumentTitle == "x", "v2 carries metadata");
            Require(envelope.ToArray()[4] == SpoolEnvelope.CurrentVersion, "current version written");
        }

        // ------------------------------------------------------------------ conversion + PNG

        public static void MonochromeConversionsProduceDistinctOutput()
        {
            var counts = new Dictionary<MonochromeConversion, long>();
            foreach (MonochromeConversion m in new[] { MonochromeConversion.Threshold, MonochromeConversion.OrderedDither, MonochromeConversion.ErrorDiffusion })
            {
                var target = new RecordingTarget();
                FakeJob(FakeDocument.Uniform(1, A4Page), new FakeRasterizer()).WithColor(ColorMode.Monochrome).WithMonochrome(m).PrintTo(target);
                counts[m] = target.Pages[0].BlackPixels;
            }
            Require(counts[MonochromeConversion.Threshold] != counts[MonochromeConversion.OrderedDither], "dither differs from threshold");
            Require(counts[MonochromeConversion.OrderedDither] != counts[MonochromeConversion.ErrorDiffusion], "diffusion differs from dither");

            var dark = new RecordingTarget();
            var light = new RecordingTarget();
            FakeJob(FakeDocument.Uniform(1, A4Page), new FakeRasterizer()).WithColor(ColorMode.Monochrome).WithMonochromeThreshold(250).PrintTo(dark);
            FakeJob(FakeDocument.Uniform(1, A4Page), new FakeRasterizer()).WithColor(ColorMode.Monochrome).WithMonochromeThreshold(10).PrintTo(light);
            Require(dark.Pages[0].BlackPixels > light.Pages[0].BlackPixels, "higher threshold gives more black");
        }

        public static void ImageFileTargetWritesValidPngs()
        {
            string folder = Path.Combine(Path.GetTempPath(), "chuvadi-print-png-" + Guid.NewGuid().ToString("N"));
            try
            {
                var target = new ImageFileTarget(folder).WithDpi(36).WithGuides();
                PrintResult result = FakeJob(FakeDocument.Uniform(3, A4Page), new FakeRasterizer())
                    .WithDuplex(Duplex.Vertical).WithCopies(2).PrintTo(target);
                Require(result.Status == PrintJobStatus.Printed, "printed: " + result.Error);
                Require(target.WrittenFiles.Count == 7, "one file per impression incl. blank, saw " + target.WrittenFiles.Count);

                int expectedW = (int)Math.Round(PaperSize.A4.WidthPoints / 72.0 * 36);
                int expectedH = (int)Math.Round(PaperSize.A4.HeightPoints / 72.0 * 36);
                foreach (string file in target.WrittenFiles)
                {
                    (int w, int h, byte colorType, byte[] pixels) = DecodePng(File.ReadAllBytes(file));
                    Require(w == expectedW && h == expectedH, "sheet size " + w + "x" + h);
                    Require(colorType == 2 && pixels.Length == (w * 3 + 1) * h, "RGB scanlines");
                }

                var gray = new ImageFileTarget(folder).WithDpi(24).WithFileNamePattern("p{1}-c{2}-{4}.png");
                FakeJob(FakeDocument.Uniform(1, A4Page), new FakeRasterizer()).WithColor(ColorMode.Monochrome).PrintTo(gray);
                Require(Path.GetFileName(gray.WrittenFiles[0]) == "p1-c1-front.png", "file name pattern");
                Require(DecodePng(File.ReadAllBytes(gray.WrittenFiles[0])).ColorType == 0, "monochrome writes grayscale PNG");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static string Pages(PrintPlan plan)
        {
            var parts = new List<string>();
            foreach (PlannedImpression i in plan.Impressions) parts.Add(i.IsBlank ? "_" : i.SourcePageNumber.ToString());
            return string.Join(",", parts);
        }

        private static PrintTargetCapabilities WithDpi(PrintTargetCapabilities c, double dpi) => new()
        {
            DpiX = dpi, DpiY = dpi, HardMargins = c.HardMargins, SupportsCopies = c.SupportsCopies,
            SupportsCollate = c.SupportsCollate, SupportsDuplex = c.SupportsDuplex, SupportsColor = c.SupportsColor,
            PreferredPixelFormat = c.PreferredPixelFormat
        };

        /// <summary>Independent PNG check: signature, chunk CRCs, IHDR, and inflated scanline data.</summary>
        internal static (int Width, int Height, byte ColorType, byte[] Pixels) DecodePng(byte[] png)
        {
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int i = 0; i < 8; i++) Require(png[i] == sig[i], "PNG signature");
            int pos = 8, width = 0, height = 0;
            byte colorType = 255;
            bool sawEnd = false;
            using var idat = new MemoryStream();
            while (pos < png.Length)
            {
                int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                uint crc = (uint)((png[pos + 8 + len] << 24) | (png[pos + 9 + len] << 16) | (png[pos + 10 + len] << 8) | png[pos + 11 + len]);
                Require(crc == ReferenceCrc(png, pos + 4, len + 4), "CRC of " + type);
                if (type == "IHDR")
                {
                    width = (png[pos + 8] << 24) | (png[pos + 9] << 16) | (png[pos + 10] << 8) | png[pos + 11];
                    height = (png[pos + 12] << 24) | (png[pos + 13] << 16) | (png[pos + 14] << 8) | png[pos + 15];
                    Require(png[pos + 16] == 8, "bit depth 8");
                    colorType = png[pos + 17];
                }
                else if (type == "IDAT") idat.Write(png, pos + 8, len);
                else if (type == "IEND") sawEnd = true;
                pos += 12 + len;
            }
            Require(sawEnd, "IEND present");
            idat.Position = 0;
            using var z = new ZLibStream(idat, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            z.CopyTo(raw);
            return (width, height, colorType, raw.ToArray());
        }

        // Bit-by-bit CRC-32, deliberately independent of the library's table implementation.
        private static uint ReferenceCrc(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= data[i];
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
