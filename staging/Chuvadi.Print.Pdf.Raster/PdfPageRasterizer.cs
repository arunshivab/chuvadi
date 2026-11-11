using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;

namespace Chuvadi.Print.Pdf.Raster
{
    /// <summary>Which of a PDF page's boxes is printed (ISO 32000-1 §14.11.2).</summary>
    public enum PdfPageBox
    {
        /// <summary>The visible region of the page, as viewers show it and as ISO 32000 says to print it. The default.</summary>
        CropBox = 0,

        /// <summary>The whole physical medium the page was laid out on, including anything outside the crop box.</summary>
        MediaBox = 1,

        /// <summary>The bleed box (production printing). Defaults to the crop box when the page has none.</summary>
        BleedBox = 2,

        /// <summary>The trim box (the finished page). Defaults to the crop box when the page has none.</summary>
        TrimBox = 3,

        /// <summary>The art box (the meaningful content). Defaults to the crop box when the page has none.</summary>
        ArtBox = 4
    }

    /// <summary>
    /// The <see cref="IPageRasterizer"/> for PDF documents, backed by the Chuvadi PDF renderer
    /// (Chuvadi.Pdf.Rendering). Give it to a <see cref="PrintJob"/> with <c>WithRasterizer</c> to print
    /// PDFs on any Chuvadi.Print target: Windows printers, IPP printers, PNG preview.
    /// </summary>
    /// <remarks>
    /// <para>Pages render one at a time at the resolution the print job asks for (P05), with the page's own
    /// <c>/Rotate</c> applied, so <see cref="IRasterDocument.GetPageSize"/> is the upright size. The printed area is
    /// <see cref="PageBox"/> (the crop box by default, as ISO 32000 says), clipped to the media box.</para>
    /// <para>Annotations and form-field appearances are printed by flattening them into a copy of the
    /// document first (Chuvadi.Pdf <c>AnnotationFlattener</c>); hidden and no-view annotations are skipped.
    /// <see cref="RasterRequest.IncludeAnnotations"/> and <see cref="RasterRequest.IncludeFormFields"/> pick
    /// what is flattened. Documents without annotations are not copied.</para>
    /// <para>Anisotropic resolutions (DpiX different from DpiY) render at the larger one and are resampled.
    /// <see cref="RasterPixelFormat.BlackWhite1"/> requests are answered with Gray8; the print job applies
    /// its monochrome conversion (threshold, ordered dither or error diffusion).</para>
    /// <para>Thread-safe for separate documents; an opened document renders on one thread at a time.</para>
    /// </remarks>
    public sealed class PdfPageRasterizer : IPageRasterizer
    {
        /// <summary>Password for encrypted documents (user or owner password). Default none.</summary>
        public string? Password { get; set; }

        /// <summary>Supersampling factor (1 = off). Renders at this multiple and box-filters down. Default 1.</summary>
        public int SuperSample { get; set; } = 1;

        /// <summary>Blend anti-aliased edges in linear light. Default true.</summary>
        public bool GammaCorrect { get; set; } = true;

        /// <summary>Glyph hinting. Default <see cref="HintingMode.Light"/>.</summary>
        public HintingMode Hinting { get; set; } = HintingMode.Light;

        /// <summary>Autohint fonts that carry no hinting programs. Default true.</summary>
        public bool AutohintUnhintedFonts { get; set; } = true;

        /// <summary>Curve flattening tolerance in device pixels. Default 0.25.</summary>
        public double FlatnessTolerance { get; set; } = 0.25;

        /// <summary>Draw every layer whatever its state. Default false (the document's default layer visibility).</summary>
        public bool IgnoreOptionalContent { get; set; }

        /// <summary>Per-layer visibility overrides (layer name to visible). Default none.</summary>
        public IReadOnlyDictionary<string, bool>? LayerVisibility { get; set; }

        /// <summary>
        /// Upper bound on the resolution actually rendered, in dots per inch; requests above it render at this
        /// resolution and are scaled up. Default 1200. Protects memory on very high-resolution devices.
        /// </summary>
        public double MaxRenderDpi { get; set; } = 1200;

        /// <summary>
        /// Which page box is printed, clipped to the media box. Default <see cref="PdfPageBox.CropBox"/>: the page as
        /// a viewer shows it. <see cref="PdfPageBox.MediaBox"/> prints the whole medium, as 0.1.0 previews did.
        /// </summary>
        public PdfPageBox PageBox { get; set; } = PdfPageBox.CropBox;

        /// <summary>Opens a PDF for rendering. Throws when the PDF cannot be read (or the password is wrong).</summary>
        public IRasterDocument Open(ReadOnlyMemory<byte> document)
        {
            if (SuperSample < 1 || SuperSample > 8)
            {
                throw new InvalidOperationException("SuperSample must be 1..8.");
            }

            if (!(MaxRenderDpi > 0))
            {
                throw new InvalidOperationException("MaxRenderDpi must be positive.");
            }

            if (!Enum.IsDefined(PageBox))
            {
                throw new InvalidOperationException("PageBox is not a PdfPageBox value.");
            }

            return new Document(this, new PdfSource(document, Password));
        }

        private sealed class Document : IRasterDocument
        {
            private readonly PdfPageRasterizer _owner;
            private readonly PdfSource _source;
            private readonly PdfPageBox _box;

            public Document(PdfPageRasterizer owner, PdfSource source)
            {
                _owner = owner;
                _source = source;
                _box = owner.PageBox;
            }

            public int PageCount => _source.PageCount;

            public PageDimensions GetPageSize(int pageNumber)
            {
                PdfPage page = PageOf(_source.Original, pageNumber);
                return Upright(page, _box);
            }

            public RasterPage Render(int pageNumber, RasterRequest request, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(request);
                cancellationToken.ThrowIfCancellationRequested();

                PdfDocument doc = _source.For(request.IncludeAnnotations, request.IncludeFormFields);
                PdfPage page = PageOf(doc, pageNumber);

                PageDimensions upright = Upright(page, _box);
                int extra = Degrees(request.Rotation);
                PageDimensions rotated = extra is 90 or 270 ? upright.Swap() : upright;
                int targetWidth = Math.Max(1, (int)Math.Round(rotated.WidthPoints / 72.0 * request.DpiX));
                int targetHeight = Math.Max(1, (int)Math.Round(rotated.HeightPoints / 72.0 * request.DpiY));

                double renderDpi = Math.Min(Math.Max(request.DpiX, request.DpiY), _owner.MaxRenderDpi);
                RenderOptions options = new RenderOptions
                {
                    Dpi = renderDpi,
                    Background = ColorF.White,
                    FlatnessTolerance = _owner.FlatnessTolerance,
                    SuperSample = _owner.SuperSample,
                    AntiAlias = request.AntiAlias,
                    GammaCorrect = _owner.GammaCorrect,
                    Hinting = _owner.Hinting,
                    AutohintUnhintedFonts = _owner.AutohintUnhintedFonts,
                    Rotation = extra,
                    ApplyPageRotation = true,
                    IgnoreOptionalContent = _owner.IgnoreOptionalContent,
                    LayerVisibility = _owner.LayerVisibility,
                    // Chuvadi.Pdf 3.32.0 renders the crop box by default; this adapter renders the whole media box
                    // and cuts out the box chosen by PageBox itself (crop, bleed, trim or art box).
                    DisplayBox = PdfDisplayBox.MediaBox,
                };

                // The renderer draws the whole media box; the printed box is cut out of it afterwards.
                PixelBuffer pixels = new PageRasterizer(doc.Objects, options).Rasterize(page);
                cancellationToken.ThrowIfCancellationRequested();
                PixelRegion region = Region(page, _box, extra, pixels.Width, pixels.Height);

                RasterPixelFormat format = request.PixelFormat == RasterPixelFormat.BlackWhite1
                    ? RasterPixelFormat.Gray8
                    : request.PixelFormat;
                RasterPage result = RasterPage.Rent(targetWidth, targetHeight, format, request.DpiX, request.DpiY);
                try
                {
                    PixelCopy.Copy(pixels, region, result);
                }
                catch
                {
                    result.Dispose();
                    throw;
                }

                return result;
            }

            public void Dispose() => _source.Dispose();
        }

        internal static PdfPage PageOf(PdfDocument document, int pageNumber)
        {
            if (pageNumber < 1 || pageNumber > document.PageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber,
                    "Page " + pageNumber + " is outside 1.." + document.PageCount + ".");
            }

            return document.Pages[pageNumber - 1];
        }

        /// <summary>The printed area of a page in PDF user space: <paramref name="box"/> clipped to the media box.</summary>
        internal static (double X1, double Y1, double X2, double Y2) PrintedArea(PdfPage page, PdfPageBox box)
        {
            (double mx1, double my1, double mx2, double my2) = Normal(page.MediaBox);
            if (!(mx2 - mx1 > 0) || !(my2 - my1 > 0))
            {
                throw new InvalidOperationException("Page " + page.PageNumber + " has an empty media box.");
            }

            if (box == PdfPageBox.MediaBox)
            {
                return (mx1, my1, mx2, my2);
            }

            PdfRectangle chosen = box switch
            {
                PdfPageBox.BleedBox => page.BleedBox,
                PdfPageBox.TrimBox => page.TrimBox,
                PdfPageBox.ArtBox => page.ArtBox,
                _ => page.CropBox
            };
            (double cx1, double cy1, double cx2, double cy2) = Normal(chosen);
            double x1 = Math.Max(mx1, cx1), y1 = Math.Max(my1, cy1), x2 = Math.Min(mx2, cx2), y2 = Math.Min(my2, cy2);

            // A box that does not overlap the media box (malformed) falls back to the media box, as viewers do.
            return x2 - x1 > 0 && y2 - y1 > 0 ? (x1, y1, x2, y2) : (mx1, my1, mx2, my2);
        }

        internal static PageDimensions Upright(PdfPage page, PdfPageBox box)
        {
            (double x1, double y1, double x2, double y2) = PrintedArea(page, box);
            double width = x2 - x1;
            double height = y2 - y1;
            int rotate = PageRotate(page);
            return rotate is 90 or 270 ? new PageDimensions(height, width) : new PageDimensions(width, height);
        }

        /// <summary>
        /// Where the printed area lies in the rendered (rotated) media-box buffer, in pixels. The renderer turns the
        /// page clockwise by its <c>/Rotate</c> (to a multiple of 90) plus <paramref name="extraRotation"/>.
        /// </summary>
        internal static PixelRegion Region(PdfPage page, PdfPageBox box, int extraRotation, int bufferWidth, int bufferHeight)
        {
            (double mx1, double my1, double mx2, double my2) = Normal(page.MediaBox);
            (double x1, double y1, double x2, double y2) = PrintedArea(page, box);
            int rotation = (PageRotate(page) + extraRotation) % 360;
            bool swap = rotation is 90 or 270;

            // Unrotated buffer: the media box, y down, at the buffer's own scale on each axis.
            int w = swap ? bufferHeight : bufferWidth;
            int h = swap ? bufferWidth : bufferHeight;
            double sx = w / (mx2 - mx1);
            double sy = h / (my2 - my1);
            int left = Clamp((int)Math.Round((x1 - mx1) * sx), 0, w - 1);
            int top = Clamp((int)Math.Round((my2 - y2) * sy), 0, h - 1);
            int right = Clamp((int)Math.Round((x2 - mx1) * sx), left + 1, w);
            int bottom = Clamp((int)Math.Round((my2 - y1) * sy), top + 1, h);
            int rw = right - left, rh = bottom - top;

            return rotation switch
            {
                90 => new PixelRegion(h - (top + rh), left, rh, rw),
                180 => new PixelRegion(w - (left + rw), h - (top + rh), rw, rh),
                270 => new PixelRegion(top, w - (left + rw), rh, rw),
                _ => new PixelRegion(left, top, rw, rh)
            };
        }

        /// <summary>The page's <c>/Rotate</c> as the renderer applies it: the nearest lower multiple of 90, in 0..270.</summary>
        internal static int PageRotate(PdfPage page)
        {
            int rotate = page.Rotate - (page.Rotate % 90);
            return ((rotate % 360) + 360) % 360;
        }

        internal static int Degrees(PageRotation rotation) => rotation switch
        {
            PageRotation.Clockwise90 => 90,
            PageRotation.Rotate180 => 180,
            PageRotation.Clockwise270 => 270,
            _ => 0,
        };

        private static (double X1, double Y1, double X2, double Y2) Normal(PdfRectangle r)
            => (Math.Min(r.X1, r.X2), Math.Min(r.Y1, r.Y2), Math.Max(r.X1, r.X2), Math.Max(r.Y1, r.Y2));

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }

    /// <summary>A rectangle of pixels in a rendered buffer.</summary>
    internal readonly record struct PixelRegion(int X, int Y, int Width, int Height);
}
