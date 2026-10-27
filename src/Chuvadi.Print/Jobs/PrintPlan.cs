using System;
using System.Collections.Generic;

namespace Chuvadi.Print
{
    /// <summary>
    /// One side of one sheet in the output stream: a document page (or a blank side) and the copy
    /// it belongs to.
    /// </summary>
    public readonly struct PlannedImpression : IEquatable<PlannedImpression>
    {
        internal PlannedImpression(int sourcePageNumber, int copyNumber, int sheetIndex, bool isBackSide, bool isBlank)
        {
            SourcePageNumber = sourcePageNumber;
            CopyNumber = copyNumber;
            SheetIndex = sheetIndex;
            IsBackSide = isBackSide;
            IsBlank = isBlank;
        }

        /// <summary>1-based document page; 0 for a blank side.</summary>
        public int SourcePageNumber { get; }

        /// <summary>1-based copy this impression belongs to.</summary>
        public int CopyNumber { get; }

        /// <summary>0-based physical sheet this impression is printed on.</summary>
        public int SheetIndex { get; }

        /// <summary>True for the second side of a duplex sheet.</summary>
        public bool IsBackSide { get; }

        /// <summary>
        /// True for an inserted blank back side. Blanks keep duplex output aligned when a copy (or a
        /// repeated sheet) ends on a front side and more output follows.
        /// </summary>
        public bool IsBlank { get; }

        public bool Equals(PlannedImpression other)
            => SourcePageNumber == other.SourcePageNumber && CopyNumber == other.CopyNumber
               && SheetIndex == other.SheetIndex && IsBackSide == other.IsBackSide && IsBlank == other.IsBlank;
        public override bool Equals(object? obj) => obj is PlannedImpression p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(SourcePageNumber, CopyNumber, SheetIndex, IsBackSide, IsBlank);
        public static bool operator ==(PlannedImpression left, PlannedImpression right) => left.Equals(right);
        public static bool operator !=(PlannedImpression left, PlannedImpression right) => !left.Equals(right);

        public override string ToString()
            => (IsBlank ? "blank" : "p" + SourcePageNumber) + " c" + CopyNumber + " s" + SheetIndex + (IsBackSide ? "b" : "f");
    }

    /// <summary>
    /// Exactly what a job will emit, worked out before anything is rendered: selected pages,
    /// copies and collation expanded, duplex sides paired onto sheets.
    /// </summary>
    public sealed class PrintPlan
    {
        private PrintPlan(int documentPageCount, int[] selectedPages, PlannedImpression[] impressions,
                          int sheetCount, int copies, bool targetHandlesCopies, bool isDuplex)
        {
            DocumentPageCount = documentPageCount;
            SelectedPages = selectedPages;
            Impressions = impressions;
            SheetCount = sheetCount;
            Copies = copies;
            TargetHandlesCopies = targetHandlesCopies;
            IsDuplex = isDuplex;
            int pageImpressions = 0;
            foreach (PlannedImpression i in impressions) if (!i.IsBlank) pageImpressions++;
            PageImpressionCount = pageImpressions;
        }

        /// <summary>Total pages in the document.</summary>
        public int DocumentPageCount { get; }

        /// <summary>The 1-based pages chosen by <see cref="PrintSettings.Pages"/>, in print order.</summary>
        public IReadOnlyList<int> SelectedPages { get; }

        /// <summary>Every side the job will send to the target, in order.</summary>
        public IReadOnlyList<PlannedImpression> Impressions { get; }

        /// <summary>Impressions that carry a document page (excludes inserted blanks).</summary>
        public int PageImpressionCount { get; }

        /// <summary>Physical sheets the job emits (copies made by the target are not counted).</summary>
        public int SheetCount { get; }

        /// <summary>Requested copy count.</summary>
        public int Copies { get; }

        /// <summary>When true the target makes the copies; the plan contains a single copy.</summary>
        public bool TargetHandlesCopies { get; }

        /// <summary>True when sides are paired two to a sheet.</summary>
        public bool IsDuplex { get; }

        /// <summary>Builds the plan for a document of <paramref name="documentPageCount"/> pages.</summary>
        public static PrintPlan Create(int documentPageCount, PrintSettings settings, bool targetHandlesCopies)
        {
            if (documentPageCount < 0) throw new ArgumentOutOfRangeException(nameof(documentPageCount));
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (settings.Pages is null) throw new InvalidOperationException("PrintSettings.Pages cannot be null.");
            if (settings.Copies < 1) throw new InvalidOperationException("PrintSettings.Copies must be at least 1.");

            IReadOnlyList<int> resolved = settings.Pages.Resolve(documentPageCount);
            var selected = new int[resolved.Count];
            for (int i = 0; i < selected.Length; i++) selected[i] = resolved[i];

            bool duplex = settings.Duplex != Duplex.Simplex;

            // Group the selected pages onto sheets: (front, back); back == 0 means no back side.
            var sheets = new List<(int Front, int Back)>();
            for (int i = 0; i < selected.Length; i += duplex ? 2 : 1)
            {
                int back = duplex && i + 1 < selected.Length ? selected[i + 1] : 0;
                sheets.Add((selected[i], back));
            }

            // Expand copies into a sequence of sheet occurrences.
            var occurrences = new List<(int Sheet, int Copy)>();
            int copies = settings.Copies;
            if (targetHandlesCopies || copies == 1)
            {
                for (int s = 0; s < sheets.Count; s++) occurrences.Add((s, 1));
            }
            else if (settings.Collate)
            {
                for (int c = 1; c <= copies; c++)
                    for (int s = 0; s < sheets.Count; s++) occurrences.Add((s, c));
            }
            else
            {
                for (int s = 0; s < sheets.Count; s++)
                    for (int c = 1; c <= copies; c++) occurrences.Add((s, c));
            }

            var impressions = new List<PlannedImpression>(occurrences.Count * (duplex ? 2 : 1));
            for (int k = 0; k < occurrences.Count; k++)
            {
                (int sheetNo, int copy) = occurrences[k];
                (int front, int back) = sheets[sheetNo];
                impressions.Add(new PlannedImpression(front, copy, k, isBackSide: false, isBlank: false));
                if (back > 0)
                    impressions.Add(new PlannedImpression(back, copy, k, isBackSide: true, isBlank: false));
                else if (duplex && k < occurrences.Count - 1)
                    impressions.Add(new PlannedImpression(0, copy, k, isBackSide: true, isBlank: true));
            }

            return new PrintPlan(documentPageCount, selected, impressions.ToArray(), occurrences.Count,
                                 copies, targetHandlesCopies, duplex);
        }
    }
}
