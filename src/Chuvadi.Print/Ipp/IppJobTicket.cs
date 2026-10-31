using System;
using System.Collections.Generic;

namespace Chuvadi.Print.Ipp
{
    /// <summary>Printer-specific choices that are not part of the device-independent <see cref="PrintSettings"/>.</summary>
    public sealed class IppJobOptions
    {
        /// <summary>media-source keyword (tray), e.g. "tray-1", "manual". Null = printer default.</summary>
        public string? MediaSource { get; set; }

        /// <summary>media-type keyword, e.g. "stationery", "labels", "photographic-glossy". Null = printer default.</summary>
        public string? MediaType { get; set; }

        /// <summary>print-quality. Null = printer default.</summary>
        public IppPrintQuality? Quality { get; set; }

        /// <summary>
        /// When true, settings the printer cannot honour fail the job. When false (default) the closest
        /// supported value is used and the substitution is listed in the target's LastApproximations.
        /// </summary>
        public bool Strict { get; set; }

        /// <summary>Extra job template attributes, added after the mapped ones (e.g. vendor extensions).</summary>
        public IList<IppAttribute> AdditionalJobAttributes { get; } = new List<IppAttribute>();
    }

    /// <summary>Maps device-independent <see cref="PrintSettings"/> to IPP job template attributes.</summary>
    public static class IppJobTicket
    {
        /// <summary>
        /// Builds the job attribute group.
        /// </summary>
        /// <param name="settings">What to print.</param>
        /// <param name="capabilities">What the printer supports.</param>
        /// <param name="options">Tray, media type, quality, strictness.</param>
        /// <param name="documentMode">
        /// True when the printer receives the original document and lays it out itself (orientation, page
        /// ranges and scaling are requested); false when it receives finished sheet images (raster).
        /// </param>
        /// <param name="printerMakesCopies">Whether copies and collation are requested from the printer.</param>
        /// <param name="pageCount">Document page count, needed to express some page selections as page-ranges.</param>
        /// <param name="rasterResolution">printer-resolution to request in raster mode.</param>
        /// <param name="approximations">Receives a description of every setting that could not be expressed exactly.</param>
        public static IppAttributeGroup Build(PrintSettings settings, IppPrinterCapabilities capabilities, IppJobOptions? options,
                                              bool documentMode, bool printerMakesCopies, int? pageCount,
                                              IppResolution? rasterResolution, IList<string> approximations)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
            if (approximations is null) throw new ArgumentNullException(nameof(approximations));
            options ??= new IppJobOptions();
            var job = new IppAttributeGroup(IppTag.JobAttributes);

            // copies + collation
            if (printerMakesCopies && settings.Copies > 1)
            {
                int copies = settings.Copies;
                if (copies > capabilities.MaxCopies)
                {
                    approximations.Add("copies " + settings.Copies + " exceeds the printer maximum " + capabilities.MaxCopies);
                    copies = capabilities.MaxCopies;
                }
                job.Add("copies", IppValue.Integer(copies));
                string handling = settings.Collate ? "separate-documents-collated-copies" : "separate-documents-uncollated-copies";
                if (IppPrinterCapabilities.Contains(capabilities.MultipleDocumentHandling, handling))
                    job.Add("multiple-document-handling", IppValue.Keyword(handling));
                else
                    approximations.Add("printer cannot choose collation (" + handling + " not supported)");
            }

            // sides
            string sides = settings.Duplex switch
            {
                Duplex.Vertical => "two-sided-long-edge",
                Duplex.Horizontal => "two-sided-short-edge",
                _ => "one-sided"
            };
            if (capabilities.Sides.Count > 0 && !IppPrinterCapabilities.Contains(capabilities.Sides, sides))
            {
                approximations.Add("sides " + sides + " not supported; printing one-sided");
                sides = "one-sided";
            }
            job.Add("sides", IppValue.Keyword(sides));

            // media / media-col
            string media = PwgMediaNames.NameFor(settings.Paper, capabilities.Media);
            if (capabilities.Media.Count > 0 && !IppPrinterCapabilities.Contains(capabilities.Media, media)
                && !media.StartsWith("custom_", StringComparison.Ordinal))
                approximations.Add("media " + media + " is not listed by the printer");
            if (options.MediaSource is not null || options.MediaType is not null)
            {
                var size = new IppCollection()
                    .Add("x-dimension", IppValue.Integer(HundredthsMm(Math.Min(settings.Paper.WidthPoints, settings.Paper.HeightPoints))))
                    .Add("y-dimension", IppValue.Integer(HundredthsMm(Math.Max(settings.Paper.WidthPoints, settings.Paper.HeightPoints))));
                var col = new IppCollection().Add("media-size", IppValue.Collection(size)).Add("media-size-name", IppValue.Keyword(media));
                if (options.MediaSource is not null) col.Add("media-source", IppValue.Keyword(options.MediaSource));
                if (options.MediaType is not null) col.Add("media-type", IppValue.Keyword(options.MediaType));
                job.Add("media-col", IppValue.Collection(col));
            }
            else
            {
                job.Add("media", IppValue.Keyword(media));
            }

            // colour
            string colorMode = settings.Color switch
            {
                ColorMode.Color => "color",
                ColorMode.Monochrome when IppPrinterCapabilities.Contains(capabilities.ColorModes, "bi-level") => "bi-level",
                _ => "monochrome"
            };
            if (capabilities.ColorModes.Count > 0 && !IppPrinterCapabilities.Contains(capabilities.ColorModes, colorMode))
            {
                approximations.Add("print-color-mode " + colorMode + " not supported; using monochrome");
                colorMode = "monochrome";
            }
            job.Add("print-color-mode", IppValue.Keyword(colorMode));

            if (options.Quality.HasValue) job.Add("print-quality", IppValue.Enum((int)options.Quality.Value));
            if (!documentMode && rasterResolution.HasValue) job.Add("printer-resolution", IppValue.Resolution(rasterResolution.Value));

            if (documentMode) AddDocumentLayout(settings, capabilities, job, pageCount, approximations);

            foreach (IppAttribute extra in options.AdditionalJobAttributes) job.Add(extra);

            if (options.Strict && approximations.Count > 0)
                throw new NotSupportedException("The printer cannot honour the print settings: " + string.Join("; ", approximations));
            return job;
        }

        private static void AddDocumentLayout(PrintSettings settings, IppPrinterCapabilities capabilities, IppAttributeGroup job,
                                              int? pageCount, IList<string> approximations)
        {
            job.Add("orientation-requested", IppValue.Enum(settings.Orientation switch
            {
                PageOrientation.Landscape => 4,
                PageOrientation.ReverseLandscape => 5,
                PageOrientation.ReversePortrait => 6,
                _ => 3
            }));

            string canonical = settings.Pages.ToCanonicalString();
            if (canonical != "all")
            {
                IReadOnlyList<int>? pages = ResolvePages(settings.Pages, canonical, pageCount);
                if (pages is null)
                {
                    approximations.Add("page selection '" + canonical + "' needs the page count; all pages sent");
                }
                else if (pages.Count == 0)
                {
                    throw new InvalidOperationException("The page selection selects no pages.");
                }
                else
                {
                    var ranges = new List<IppValue>();
                    int start = pages[0], previous = pages[0];
                    for (int i = 1; i <= pages.Count; i++)
                    {
                        if (i < pages.Count && pages[i] == previous + 1)
                        {
                            previous = pages[i];
                            continue;
                        }
                        ranges.Add(IppValue.Range(start, previous));
                        if (i < pages.Count) start = previous = pages[i];
                    }
                    if (!capabilities.PageRangesSupported) approximations.Add("printer does not report page-ranges support");
                    job.Add(new IppAttribute("page-ranges", ranges));
                }
            }

            string? scaling = settings.Scale switch
            {
                ScaleMode.ActualSize => "none",
                ScaleMode.FitToPage => "fit",
                ScaleMode.ShrinkToFit => "auto-fit",
                _ => null
            };
            if (scaling is null)
            {
                approximations.Add("scale " + settings.Scale + " has no IPP equivalent; using fit");
                scaling = "fit";
            }
            if (capabilities.PrintScaling.Count > 0 && !IppPrinterCapabilities.Contains(capabilities.PrintScaling, scaling))
                approximations.Add("print-scaling " + scaling + " not supported by the printer");
            else
                job.Add("print-scaling", IppValue.Keyword(scaling));

            if (settings.Alignment != ContentAlignment.Center) approximations.Add("alignment " + settings.Alignment + " is chosen by the printer");
            if (settings.Margins != Margins.Default) approximations.Add("margins are chosen by the printer");
            if (settings.AutoRotate) approximations.Add("auto-rotate is chosen by the printer");
        }

        private static IReadOnlyList<int>? ResolvePages(PageSelection selection, string canonical, int? pageCount)
        {
            if (pageCount.HasValue) return selection.Resolve(pageCount.Value);
            // Without a page count only selections with explicit page numbers can be expressed.
            if (canonical.StartsWith("range:", StringComparison.Ordinal) || canonical.StartsWith("list:", StringComparison.Ordinal)
                || canonical.StartsWith("current:", StringComparison.Ordinal) || canonical.StartsWith("first:", StringComparison.Ordinal))
            {
                int max = 1;
                foreach (int p in selection.Resolve(MaxExplicitPage(canonical))) max = Math.Max(max, p);
                return selection.Resolve(max);
            }
            return null;
        }

        private static int MaxExplicitPage(string canonical)
        {
            int max = 1, number = 0;
            foreach (char c in canonical)
            {
                if (c >= '0' && c <= '9')
                {
                    number = Math.Min(1_000_000, number * 10 + (c - '0'));
                    max = Math.Max(max, number);
                }
                else
                {
                    number = 0;
                }
            }
            return max;
        }

        private static int HundredthsMm(double points) => (int)Math.Round(points / 72.0 * 2540.0);
    }
}
