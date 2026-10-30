using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;

namespace Chuvadi.Print.Pdf
{
    /// <summary>
    /// The <see cref="IPageRasterizer"/> for PDF documents, backed by the Chuvadi PDF renderer
    /// (Chuvadi.Pdf.Rendering). Give it to a <see cref="PrintJob"/> with <c>WithRasterizer</c> to print
    /// PDFs on any Chuvadi.Print target: Windows printers, IPP printers, PNG preview.
    /// </summary>
    /// <remarks>
    /// <para>Pages render one at a time at the resolution the print job asks for (P05), with the page's own
    /// <c>/Rotate</c> applied, so <see cref="IRasterDocument.GetPageSize"/> is the upright size. The renderer
    /// draws the page's media box.</para>
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

            return new Document(this, new PdfSource(document, Password));
        }

        private sealed class Document : IRasterDocument
        {
            private readonly PdfPageRasterizer _owner;
            private readonly PdfSource _source;

            public Document(PdfPageRasterizer owner, PdfSource source)
            {
                _owner = owner;
                _source = source;
            }

            public int PageCount => _source.PageCount;

            public PageDimensions GetPageSize(int pageNumber)
            {
                PdfPage page = PageOf(_source.Original, pageNumber);
                return Upright(page);
            }

            public RasterPage Render(int pageNumber, RasterRequest request, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(request);
                cancellationToken.ThrowIfCancellationRequested();

                PdfDocument doc = _source.For(request.IncludeAnnotations, request.IncludeFormFields);
                PdfPage page = PageOf(doc, pageNumber);

                PageDimensions upright = Upright(page);
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
                };

                PixelBuffer pixels = new PageRasterizer(doc.Objects, options).Rasterize(page);
                cancellationToken.ThrowIfCancellationRequested();

                RasterPixelFormat format = request.PixelFormat == RasterPixelFormat.BlackWhite1
                    ? RasterPixelFormat.Gray8
                    : request.PixelFormat;
                RasterPage result = RasterPage.Rent(targetWidth, targetHeight, format, request.DpiX, request.DpiY);
                try
                {
                    PixelCopy.Copy(pixels, result);
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

        /// <summary>The media box (what the renderer draws) with the page's /Rotate applied.</summary>
        internal static PageDimensions Upright(PdfPage page)
        {
            double width = page.Width;
            double height = page.Height;
            if (!(width > 0) || !(height > 0))
            {
                throw new InvalidOperationException("Page " + page.PageNumber + " has an empty media box.");
            }

            int rotate = ((page.Rotate % 360) + 360) % 360;
            return rotate is 90 or 270 ? new PageDimensions(height, width) : new PageDimensions(width, height);
        }

        internal static int Degrees(PageRotation rotation) => rotation switch
        {
            PageRotation.Clockwise90 => 90,
            PageRotation.Rotate180 => 180,
            PageRotation.Clockwise270 => 270,
            _ => 0,
        };
    }
}
