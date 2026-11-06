using System;
using System.Collections.Generic;
using System.Threading;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// Lays out a vector document on sheets for the XPS route, with the same plan and placement as the raster routes:
    /// <see cref="PrintPlan"/> selects the pages, expands software copies and pairs duplex sides (inserting blank backs),
    /// and <see cref="PageLayout"/> places each page. Pure: runs on any OS.
    /// </summary>
    internal static class XpsSheetBuilder
    {
        /// <param name="document">The pages.</param>
        /// <param name="settings">What to print.</param>
        /// <param name="capabilities">The queue's capabilities (hard margins).</param>
        /// <param name="marginReference">How requested margins combine with the hard margins.</param>
        /// <param name="targetMakesCopies">True when the copies go in the PrintTicket; false to repeat the sheets.</param>
        /// <param name="skipBlankSides">True when duplex was asked for but the queue prints one side: blank backs are left out.</param>
        /// <param name="cancellationToken">Checked before each page.</param>
        /// <returns>One sheet-sized page per side to print, in order.</returns>
        public static IReadOnlyList<VectorPage> Build(IVectorDocument document, PrintSettings settings, PrintTargetCapabilities capabilities,
                                                      MarginReference marginReference, bool targetMakesCopies, bool skipBlankSides,
                                                      CancellationToken cancellationToken)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));

            PrintPlan plan = PrintPlan.Create(document.PageCount, settings, targetMakesCopies);
            PageDimensions sheetSize = PageLayout.SheetFor(settings.Paper, settings.Orientation);
            var placed = new Dictionary<int, VectorPage>();
            VectorPage? blank = null;
            var sheets = new List<VectorPage>(plan.Impressions.Count);
            foreach (PlannedImpression impression in plan.Impressions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (impression.IsBlank)
                {
                    if (skipBlankSides) continue;
                    sheets.Add(blank ??= new VectorPage(sheetSize.WidthPoints, sheetSize.HeightPoints));
                    continue;
                }
                if (!placed.TryGetValue(impression.SourcePageNumber, out VectorPage? sheet))
                {
                    VectorPage page = document.GetPage(impression.SourcePageNumber, cancellationToken)
                        ?? throw new InvalidOperationException("The vector source returned no page " + impression.SourcePageNumber + ".");
                    PagePlacement placement = PageLayout.Compute(new PageDimensions(page.Width, page.Height), settings, capabilities, marginReference);
                    sheet = VectorLayout.Place(page, placement);
                    placed.Add(impression.SourcePageNumber, sheet);
                }
                sheets.Add(sheet);
            }
            if (sheets.Count == 0) throw new InvalidOperationException("The job has no pages to print.");
            return sheets;
        }
    }

    /// <summary>A vector document over pages already in memory.</summary>
    internal sealed class VectorPageList : IVectorDocument
    {
        private readonly IReadOnlyList<VectorPage> _pages;

        public VectorPageList(IReadOnlyList<VectorPage> pages)
        {
            _pages = pages ?? throw new ArgumentNullException(nameof(pages));
            for (int i = 0; i < pages.Count; i++)
                if (pages[i] is null) throw new ArgumentException("Page " + (i + 1) + " is null.", nameof(pages));
        }

        public int PageCount => _pages.Count;

        public PageDimensions GetPageSize(int pageNumber)
        {
            VectorPage page = Page(pageNumber);
            return new PageDimensions(page.Width, page.Height);
        }

        public VectorPage GetPage(int pageNumber, CancellationToken cancellationToken) => Page(pageNumber);

        private VectorPage Page(int pageNumber)
            => pageNumber >= 1 && pageNumber <= _pages.Count ? _pages[pageNumber - 1] : throw new ArgumentOutOfRangeException(nameof(pageNumber));

        public void Dispose() { }
    }
}
