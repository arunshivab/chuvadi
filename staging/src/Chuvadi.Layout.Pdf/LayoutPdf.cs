using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Svg;

namespace Chuvadi.Layout.Pdf
{
    /// <summary>Options for PDF, SVG and PNG output.</summary>
    public sealed class LayoutPdfOptions
    {
        public string? Title { get; set; }

        public string? Author { get; set; }

        public string? Subject { get; set; }

        /// <summary>
        /// Settings for <see cref="LayoutPdf.ToSvg(LaidOutPage, LayoutPdfOptions?, CancellationToken)"/>, which uses
        /// Chuvadi.Layout's own SVG writer (glyph outlines plus a selectable text layer). Its Warning callback, when
        /// null, falls back to <see cref="Warning"/>.
        /// </summary>
        public LayoutSvgOptions LayoutSvg { get; set; } = new LayoutSvgOptions();

        /// <summary>Settings for <see cref="LayoutPdf.ToSvgViaPdf(LaidOutPage, LayoutPdfOptions?, CancellationToken)"/> (Chuvadi.Pdf.Svg).</summary>
        public SvgExportOptions Svg { get; set; } = new SvgExportOptions();

        /// <summary>Anti-aliasing for PNG output. Default true.</summary>
        public bool AntiAlias { get; set; } = true;

        /// <summary>Receives each output limitation once per call (see the Pdf* codes in README.md).</summary>
        public Action<LayoutWarning>? Warning { get; set; }
    }

    /// <summary>
    /// Output for Chuvadi.Layout pages through chuvadi-pdf. Everything happens in memory: nothing is written to
    /// disk except to the stream the caller passes. SVG and PNG are produced per page from a one-page PDF built
    /// in memory (so lazy per-page viewing stays lazy); <see cref="WritePdf(LaidOutDocument, Stream, LayoutPdfOptions?, CancellationToken)"/>
    /// writes the whole document. Fonts are embedded as subsets.
    /// </summary>
    public static class LayoutPdf
    {
        /// <summary>Writes every page of the document to a PDF.</summary>
        public static void WritePdf(LaidOutDocument document, Stream output, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            WritePdf(document.GetPages(cancellationToken), output, options, cancellationToken);
        }

        /// <summary>Writes the given pages, in order, to a PDF.</summary>
        public static void WritePdf(IEnumerable<LaidOutPage> pages, Stream output, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(pages);
            ArgumentNullException.ThrowIfNull(output);
            options ??= new LayoutPdfOptions();
            PdfDocumentBuilder builder = NewBuilder(options);
            PageTranslator translator = new PageTranslator(builder, new Dictionary<FontFace, string?>(ReferenceEqualityComparer.Instance), new HashSet<string>(), options.Warning);
            int count = 0;
            foreach (LaidOutPage page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PageBuilder pb = builder.AddPage(new PageSize(page.WidthPt, page.HeightPt));
                translator.Draw(page.DisplayList, pb);
                count++;
            }
            if (count == 0)
            {
                throw new ArgumentException("There are no pages to write.", nameof(pages));
            }
            cancellationToken.ThrowIfCancellationRequested();
            builder.Save(output);
        }

        /// <summary>A one-page PDF of <paramref name="page"/>.</summary>
        public static byte[] ToPdf(LaidOutPage page, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(page);
            using MemoryStream ms = new MemoryStream();
            WritePdf(new[] { page }, ms, options, cancellationToken);
            return ms.ToArray();
        }

        /// <summary>
        /// SVG of one page, written by Chuvadi.Layout's <see cref="Chuvadi.Layout.Drawing.LayoutSvg"/>: glyphs drawn from
        /// their outlines, an invisible selectable text layer, links, crops, clips and translucency. This is the path
        /// for on-screen viewing. It needs no PDF round trip.
        /// </summary>
        public static string ToSvg(LaidOutPage page, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(page);
            options ??= new LayoutPdfOptions();
            LayoutSvgOptions svg = options.LayoutSvg;
            if (svg.Warning is null && options.Warning is not null)
            {
                Action<LayoutWarning> warn = options.Warning;
                svg = Copy(svg);
                svg.Warning = warn;
            }
            if (svg.Title is null && options.Title is not null)
            {
                svg = ReferenceEquals(svg, options.LayoutSvg) ? Copy(svg) : svg;
                svg.Title = options.Title;
            }
            return Chuvadi.Layout.Drawing.LayoutSvg.ToSvg(page, svg, cancellationToken);
        }

        /// <summary>
        /// SVG of one page rendered by Chuvadi.Pdf.Svg from a one-page PDF, kept for comparison; on-screen viewers use
        /// <see cref="ToSvg(LaidOutPage, LayoutPdfOptions?, CancellationToken)"/>.
        /// </summary>
        public static string ToSvgViaPdf(LaidOutPage page, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new LayoutPdfOptions();
            byte[] pdf = ToPdf(page, options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using MemoryStream ms = new MemoryStream(pdf, writable: false);
            using PdfDocument doc = PdfDocument.Open(ms);
            return new SvgRenderer(options.Svg).RenderPage(doc, 0);
        }

        private static LayoutSvgOptions Copy(LayoutSvgOptions o)
        {
            LayoutSvgOptions c = new LayoutSvgOptions
            {
                IdPrefix = o.IdPrefix,
                SelectableText = o.SelectableText,
                Background = o.Background,
                Size = o.Size,
                Precision = o.Precision,
                Title = o.Title,
                ImageHref = o.ImageHref,
                IncludeLinks = o.IncludeLinks,
                PageLinkHref = o.PageLinkHref,
                AnchorHref = o.AnchorHref,
                ExternalLinkTarget = o.ExternalLinkTarget,
                Warning = o.Warning,
            };
            c.AllowedUriSchemes.Clear();
            foreach (string scheme in o.AllowedUriSchemes)
            {
                c.AllowedUriSchemes.Add(scheme);
            }
            return c;
        }

        /// <summary>PNG of one page at <paramref name="dpi"/> (rendered by Chuvadi.Pdf.Rendering from a one-page PDF).</summary>
        public static byte[] ToPng(LaidOutPage page, double dpi = 150, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (dpi <= 0 || double.IsNaN(dpi) || dpi > 2400)
            {
                throw new ArgumentOutOfRangeException(nameof(dpi), "DPI must be in (0, 2400].");
            }
            options ??= new LayoutPdfOptions();
            byte[] pdf = ToPdf(page, options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using MemoryStream ms = new MemoryStream(pdf, writable: false);
            using PdfDocument doc = PdfDocument.Open(ms);
            PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = dpi, AntiAlias = options.AntiAlias });
            return rasterizer.RasterizeToPng(doc.Pages[0]);
        }

        private static PdfDocumentBuilder NewBuilder(LayoutPdfOptions options)
        {
            PdfDocumentBuilder b = PdfDocumentBuilder.Create();
            if (options.Title is not null)
            {
                b.SetTitle(options.Title);
            }
            if (options.Author is not null)
            {
                b.SetAuthor(options.Author);
            }
            if (options.Subject is not null)
            {
                b.SetSubject(options.Subject);
            }
            return b;
        }
    }

    /// <summary>Extension methods for the consumer API: ToPdf, ToSvg and ToPng on documents and pages.</summary>
    public static class LayoutPdfExtensions
    {
        public static void ToPdf(this LaidOutDocument document, Stream output, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
            => LayoutPdf.WritePdf(document, output, options, cancellationToken);

        public static byte[] ToPdf(this LaidOutPage page, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
            => LayoutPdf.ToPdf(page, options, cancellationToken);

        public static string ToSvg(this LaidOutPage page, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
            => LayoutPdf.ToSvg(page, options, cancellationToken);

        public static byte[] ToPng(this LaidOutPage page, double dpi = 150, LayoutPdfOptions? options = null, CancellationToken cancellationToken = default)
            => LayoutPdf.ToPng(page, dpi, options, cancellationToken);
    }
}
