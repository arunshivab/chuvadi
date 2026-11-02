using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Flow
{
    /// <summary>Why a page starts where it does.</summary>
    internal enum PageStartReason
    {
        DocumentStart,
        Flow,
        PageBreak,
        SectionBreak,
    }

    /// <summary>A position in a flow document: the next line (or table row) to place.</summary>
    internal readonly record struct FlowPosition(int Section, int Block, int Line, TableContinuation? Table = null, int? TextStart = null);

    /// <summary>Checkpoint of one page: where it starts, why, and how it is numbered.</summary>
    internal sealed record PageRecord(FlowPosition Start, PageStartReason Reason, int Section, int PageNumber, bool Blank, int SectionPageIndex);

    /// <summary>Something placed on a page.</summary>
    internal abstract class PlanItem
    {
    }

    /// <summary>A paragraph line placed at absolute coordinates.</summary>
    internal sealed class PlacedLine : PlanItem
    {
        public required MeasuredParagraph Paragraph { get; init; }
        public required FittedLine Line { get; init; }
        public required int LineIndex { get; init; }

        /// <summary>Left edge of the text area (indents are inside the line).</summary>
        public required double AreaLeft { get; init; }

        public required double AreaWidth { get; init; }

        /// <summary>Top of the line box.</summary>
        public required double Top { get; init; }

        public required string Path { get; init; }
    }

    /// <summary>The part of a paragraph on a page: decoration (shading, borders) and its box.</summary>
    internal sealed class PlacedParagraphSlice : PlanItem
    {
        public required FlowParagraph Paragraph { get; init; }
        public required LayoutRect Rect { get; init; }
        public required bool IsFirstSlice { get; init; }
        public required bool IsLastSlice { get; init; }
        public required string Path { get; init; }
    }

    /// <summary>A floating image placed on a page.</summary>
    internal sealed class PlacedFloatingImage : PlanItem
    {
        /// <summary>The picture; null for a text box.</summary>
        public LayoutImage? Image { get; init; }

        /// <summary>The text box; null for a picture.</summary>
        public FlowTextBox? TextBox { get; init; }

        public ImageCrop? Crop { get; init; }
        public required LayoutRect Rect { get; init; }
        public required bool Behind { get; init; }
        public required string Path { get; init; }
    }

    /// <summary>The result of laying out one page.</summary>
    internal sealed class PagePlan
    {
        public List<PlanItem> Items { get; } = new List<PlanItem>();
        public FlowPosition End { get; set; }
        public PageStartReason EndReason { get; set; } = PageStartReason.Flow;
        public bool Finished { get; set; }
        public int Section { get; set; }
        public PageGeometry Geometry { get; set; } = PageGeometry.A4;

        /// <summary>Footnotes whose references are on the page, in reference order.</summary>
        public List<FlowNote> Footnotes { get; } = new List<FlowNote>();

        /// <summary>Bottom of the body area before footnotes took their room (the footnotes end here).</summary>
        public double FootnoteBottom { get; set; }
    }

    /// <summary>
    /// Paginates a <see cref="FlowDocument"/>. A forward pass records one <see cref="PageRecord"/> per page; a page's
    /// display list is produced by re-running pagination from its checkpoint and emitting ops.
    /// </summary>
    internal sealed partial class FlowEngine : IPageSource, IFieldValues
    {
        private const double Epsilon = 0.01;
        private const int MeasureCacheCapacity = 4096;

        private readonly FlowDocument _doc;
        private readonly LayoutContext _ctx;
        private readonly List<PageRecord> _pages = new List<PageRecord>();
        private readonly Dictionary<(int, int, long), LinkedListNode<CacheEntry>> _cache = new Dictionary<(int, int, long), LinkedListNode<CacheEntry>>();
        private readonly LinkedList<CacheEntry> _cacheOrder = new LinkedList<CacheEntry>();
        private readonly long[] _sectionBlockOffset;
        private readonly long _totalBlocks;
        private FlowPosition _next = new FlowPosition(0, 0, 0);
        private PageStartReason _nextReason = PageStartReason.DocumentStart;
        private bool _done;
        private long _cacheBytes;

        private sealed record CacheEntry((int, int, long) Key, MeasuredParagraph Measured, List<FittedLine> Lines, long Bytes);

        public FlowEngine(FlowDocument doc, LayoutContext ctx, LayoutSourceInfo source)
        {
            _doc = doc;
            _ctx = ctx;
            Source = source;
            LayoutLimits.Check(nameof(LayoutLimits.MaxBlocks), ctx.Limits.MaxBlocks, doc.CountBlocks());
            _sectionBlockOffset = new long[doc.Sections.Count + 1];
            for (int s = 0; s < doc.Sections.Count; s++)
            {
                _sectionBlockOffset[s + 1] = _sectionBlockOffset[s] + doc.Sections[s].Blocks.Count;
                if (doc.Sections[s].ColumnCount > 1)
                {
                    ctx.Warn(LayoutWarningCodes.MultiColumnNotSupported, LayoutWarningSeverity.Fidelity,
                        $"Section {s + 1} has {doc.Sections[s].ColumnCount} columns; it is laid out as one column.", $"section {s + 1}");
                }
            }
            _totalBlocks = _sectionBlockOffset[doc.Sections.Count];
            if (doc.Sections.Count == 0)
            {
                _done = true;
            }
        }

        public LayoutSourceInfo Source { get; }

        public bool IsPageCountFinal => _done;

        public int KnownPageCount => _done && _pages.Count == 0 ? 0 : _pages.Count;

        public int EstimatedPageCount
        {
            get
            {
                if (_done)
                {
                    return _pages.Count;
                }
                if (_pages.Count == 0)
                {
                    return Math.Max(1, _doc.PageCountHint ?? 1);
                }
                long consumed = Math.Max(1, _sectionBlockOffset[Math.Min(_next.Section, _doc.Sections.Count)] + _next.Block);
                long estimate = (long)Math.Ceiling((double)_pages.Count * _totalBlocks / consumed);
                if (_doc.PageCountHint is int hint && hint >= _pages.Count)
                {
                    return hint;
                }
                return (int)Math.Clamp(estimate, _pages.Count, int.MaxValue);
            }
        }

        public void EnsurePages(int count, CancellationToken ct)
        {
            while (!_done && _pages.Count < count)
            {
                ct.ThrowIfCancellationRequested();
                AddNextPage(ct);
            }
        }

        private void AddNextPage(CancellationToken ct)
        {
            int section = Math.Min(_next.Section, _doc.Sections.Count - 1);
            // Odd/even section starts insert a blank page when the parity is wrong.
            if (_pages.Count > 0 && _nextReason == PageStartReason.SectionBreak && _next.Block == 0 && _next.Line == 0 && _next.TextStart is null)
            {
                SectionStart start = _doc.Sections[section].Start;
                int nextNumber = NextPageNumber(section, true);
                bool wantOdd = start == SectionStart.OddPage;
                if ((start == SectionStart.OddPage || start == SectionStart.EvenPage) && (nextNumber % 2 == 1) != wantOdd)
                {
                    PageRecord prev = _pages[^1];
                    AddRecord(new PageRecord(_next, PageStartReason.SectionBreak, prev.Section, prev.PageNumber + 1, true, prev.SectionPageIndex + 1));
                    return;
                }
            }
            bool sectionStarts = _pages.Count == 0 || _pages[^1].Section != section;
            int number = NextPageNumber(section, sectionStarts);
            int sectionPageIndex = sectionStarts ? 0 : _pages[^1].SectionPageIndex + 1;
            PageRecord record = new PageRecord(_next, _nextReason, section, number, false, sectionPageIndex);
            PagePlan plan = Paginate(record, ct);
            AddRecord(record);
            _next = plan.End;
            _nextReason = plan.EndReason;
            if (plan.Finished)
            {
                _done = true;
            }
        }

        private void AddRecord(PageRecord record)
        {
            LayoutLimits.Check(nameof(LayoutLimits.MaxPages), _ctx.Limits.MaxPages, _pages.Count + 1);
            _pages.Add(record);
        }

        private int NextPageNumber(int section, bool sectionStarts)
        {
            if (sectionStarts && _doc.Sections[section].PageNumberStart is int start)
            {
                return start;
            }
            return _pages.Count == 0 ? 1 : _pages[^1].PageNumber + 1;
        }

        public LaidOutPage BuildPage(int index, CancellationToken ct)
        {
            PageRecord record = _pages[index];
            PagePlan plan;
            if (record.Blank)
            {
                plan = new PagePlan { Section = record.Section, Geometry = _doc.Sections[record.Section].Page };
            }
            else
            {
                plan = Paginate(record, ct);
            }
            return FlowEmitter.Emit(this, _ctx, plan, record, index, ct);
        }

        // ---- Field values (IFieldValues) for body text: page-dependent fields use cached results. ----

        public string Evaluate(FieldInline field) => EvaluateField(field, null);

        internal string EvaluateField(FieldInline field, PageRecord? page)
        {
            CultureInfo culture = _ctx.Options.Culture;
            switch (field.Kind)
            {
                case FieldKind.Page:
                    return page is null ? field.CachedResult ?? "1" : FormatPageNumber(page.PageNumber, _doc.Sections[page.Section].PageNumberFormat);
                case FieldKind.NumPages:
                    if (page is null)
                    {
                        return field.CachedResult ?? "1";
                    }
                    return (IsPageCountFinal ? _pages.Count : EstimatedPageCount).ToString(culture);
                case FieldKind.SectionPages:
                    if (page is null)
                    {
                        return field.CachedResult ?? "1";
                    }
                    return SectionPageCount(page.Section).ToString(culture);
                case FieldKind.Date:
                case FieldKind.Time:
                    DateTimeOffset now = _ctx.Options.Now ?? DateTimeOffset.Now;
                    string fmt = field.FormatString ?? (field.Kind == FieldKind.Date ? "d" : "t");
                    return now.ToString(fmt, culture);
                case FieldKind.FileName:
                    return _ctx.Options.DocumentName ?? field.CachedResult ?? string.Empty;
                default:
                    return field.CachedResult ?? string.Empty;
            }
        }

        private int SectionPageCount(int section)
        {
            int n = 0;
            foreach (PageRecord p in _pages)
            {
                if (p.Section == section)
                {
                    n++;
                }
            }
            return Math.Max(1, n);
        }

        internal static string FormatPageNumber(int n, PageNumberFormat format)
        {
            switch (format)
            {
                case PageNumberFormat.UpperRoman:
                    return ToRoman(n);
                case PageNumberFormat.LowerRoman:
                    return ToRoman(n).ToLowerInvariant();
                case PageNumberFormat.UpperLetter:
                case PageNumberFormat.LowerLetter:
                    if (n <= 0)
                    {
                        return n.ToString(CultureInfo.InvariantCulture);
                    }
                    // Word: A..Z, then AA..ZZ, AAA.. (letter repeated).
                    char c = (char)('A' + ((n - 1) % 26));
                    string s = new string(c, ((n - 1) / 26) + 1);
                    return format == PageNumberFormat.LowerLetter ? s.ToLowerInvariant() : s;
                default:
                    return n.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string ToRoman(int n)
        {
            if (n <= 0 || n >= 4000)
            {
                return n.ToString(CultureInfo.InvariantCulture);
            }
            (int Value, string Symbol)[] map =
            {
                (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
                (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
            };
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach ((int v, string sym) in map)
            {
                while (n >= v)
                {
                    sb.Append(sym);
                    n -= v;
                }
            }
            return sb.ToString();
        }

        // ---- Measurement cache ----

        internal (MeasuredParagraph Measured, List<FittedLine> Lines) Measure(int section, int block, FlowParagraph p, double width, string? pathPrefix = null)
        {
            long wkey = (long)Math.Round(width * 100);
            (int, int, long) key = pathPrefix is null ? (section, block, wkey) : (section, RuntimeHelpersHash(p), wkey);
            if (_cache.TryGetValue(key, out LinkedListNode<CacheEntry>? node) && ReferenceEquals(node.Value.Measured.Source, p))
            {
                _cacheOrder.Remove(node);
                _cacheOrder.AddFirst(node);
                return (node.Value.Measured, node.Value.Lines);
            }
            MeasuredParagraph m = ParagraphMeasurer.Measure(p, _ctx, this);
            List<FittedLine> lines = LineFitter.Fit(m, width, _doc.DefaultTabStop);
            long bytes = m.ApproximateBytes + (lines.Count * 200L);
            if (node is not null)
            {
                _cacheOrder.Remove(node);
                _cacheBytes -= node.Value.Bytes;
            }
            LinkedListNode<CacheEntry> added = _cacheOrder.AddFirst(new CacheEntry(key, m, lines, bytes));
            _cache[key] = added;
            _cacheBytes += bytes;
            while (_cacheOrder.Count > MeasureCacheCapacity || (_cacheBytes > _ctx.Limits.MaxLayoutBytes && _cacheOrder.Count > 1))
            {
                CacheEntry last = _cacheOrder.Last!.Value;
                _cacheOrder.RemoveLast();
                _cache.Remove(last.Key);
                _cacheBytes -= last.Bytes;
            }
            if (bytes > _ctx.Limits.MaxLayoutBytes)
            {
                throw new LayoutLimitExceededException(nameof(LayoutLimits.MaxLayoutBytes), _ctx.Limits.MaxLayoutBytes, bytes);
            }
            return (m, lines);
        }

        private static int RuntimeHelpersHash(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o) | int.MinValue;
    }
}
