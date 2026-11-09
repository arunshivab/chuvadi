using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout
{
    /// <summary>Kinds of <see cref="LayoutBox"/>.</summary>
    public enum LayoutBoxKind
    {
        Paragraph,
        Line,
        Table,
        TableCell,
        Image,
        Header,
        Footer,
        SheetCell,

        /// <summary>A text box (inline or floating).</summary>
        TextBox,

        /// <summary>The footnote area at the foot of a page.</summary>
        Footnotes,
    }

    /// <summary>
    /// A laid-out rectangle with a reference to its source, for hit-testing and acceptance tests.
    /// <see cref="Path"/> locates the source: for flow documents "section/block[/row/cell/block…][:line]",
    /// for sheets "Sheet!A1".
    /// </summary>
    public sealed record LayoutBox(LayoutBoxKind Kind, LayoutRect Rect, string Path);

    /// <summary>Where a document came from.</summary>
    public enum LayoutSourceFormat
    {
        Auto,
        Docx,
        Xlsx,
        Text,
        Markdown,
        FlowModel,
        GridModel,

        /// <summary>HTML, laid out by the Chuvadi.Layout.Markup package (HtmlLayout.Open).</summary>
        Html,
    }

    /// <summary>Information about the laid-out source.</summary>
    public sealed record LayoutSourceInfo(LayoutSourceFormat Format, int SectionOrSheetCount, string? Title);

    /// <summary>A fully laid-out page. Immutable.</summary>
    public sealed class LaidOutPage
    {
        internal LaidOutPage(int index, LayoutDisplayList displayList, IReadOnlyList<LayoutBox> boxes, string text, bool provisional,
            string pageLabel, string? sourceName)
        {
            Index = index;
            DisplayList = displayList;
            Boxes = boxes;
            _text = text;
            IsProvisional = provisional;
            PageLabel = pageLabel;
            SourceName = sourceName;
        }

        private readonly string _text;

        /// <summary>Zero-based page index in the document.</summary>
        public int Index { get; }

        public double WidthPt => DisplayList.WidthPt;

        public double HeightPt => DisplayList.HeightPt;

        public LayoutDisplayList DisplayList { get; }

        public IReadOnlyList<LayoutBox> Boxes { get; }

        /// <summary>True when the page shows a value (such as NUMPAGES) computed from a page count that was not yet final.</summary>
        public bool IsProvisional { get; }

        /// <summary>The page number as printed (section numbering and format applied), e.g. "iv" or "12".</summary>
        public string PageLabel { get; }

        /// <summary>Section or sheet the page belongs to, when it has a name (sheet name for workbooks).</summary>
        public string? SourceName { get; }

        /// <summary>The page's text in reading order; lines separated by '\n'.</summary>
        public string GetText() => _text;
    }

    /// <summary>The engine behind a <see cref="LaidOutDocument"/>.</summary>
    internal interface IPageSource
    {
        int EstimatedPageCount { get; }

        bool IsPageCountFinal { get; }

        int KnownPageCount { get; }

        /// <summary>Paginates forward until at least <paramref name="count"/> pages are known or the document ends.</summary>
        void EnsurePages(int count, CancellationToken ct);

        LaidOutPage BuildPage(int index, CancellationToken ct);

        LayoutSourceInfo Source { get; }
    }

    /// <summary>
    /// A document laid out into fixed pages, lazily. Opening is cheap; <see cref="GetPage"/> lays out only what
    /// is needed to produce the requested page and caches recent pages. Calls are serialised internally, so a
    /// viewer may prefetch from another thread. Dispose releases cached pages.
    /// </summary>
    public sealed class LaidOutDocument : IDisposable
    {
        private readonly object _gate = new object();
        private readonly IPageSource _source;
        private readonly LayoutContext _context;
        private readonly LinkedList<LaidOutPage> _lru = new LinkedList<LaidOutPage>();
        private readonly int _cacheSize;
        private bool _disposed;

        internal LaidOutDocument(IPageSource source, LayoutContext context)
        {
            _source = source;
            _context = context;
            _cacheSize = Math.Max(1, context.Options.PageCacheSize);
        }

        /// <summary>
        /// Number of pages: exact when <see cref="IsPageCountFinal"/>, otherwise an estimate (never below the pages
        /// already laid out). Use <see cref="ResolvePageCount"/> to make it final.
        /// </summary>
        public int PageCount
        {
            get
            {
                lock (_gate)
                {
                    return _source.IsPageCountFinal ? _source.KnownPageCount : Math.Max(_source.EstimatedPageCount, _source.KnownPageCount);
                }
            }
        }

        public bool IsPageCountFinal
        {
            get
            {
                lock (_gate)
                {
                    return _source.IsPageCountFinal;
                }
            }
        }

        /// <summary>
        /// Runs the (measurement-only) pagination pass to the end and returns the exact page count. The pass advances
        /// one page at a time and releases the document between pages, so a viewer can call it on a background thread
        /// while <see cref="GetPage"/> keeps serving pages on another.
        /// </summary>
        public int ResolvePageCount(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                lock (_gate)
                {
                    ThrowIfDisposed();
                    if (_source.IsPageCountFinal)
                    {
                        return _source.KnownPageCount;
                    }
                    int before = _source.KnownPageCount;
                    _source.EnsurePages(before + 1, cancellationToken);
                    if (_source.KnownPageCount == before && !_source.IsPageCountFinal)
                    {
                        // No progress is impossible for a correct engine; finish in one go rather than spin.
                        _source.EnsurePages(int.MaxValue, cancellationToken);
                    }
                }
            }
        }

        /// <summary>Lays out (or returns the cached) page <paramref name="index"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The document has fewer pages.</exception>
        public LaidOutPage GetPage(int index, CancellationToken cancellationToken = default)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            lock (_gate)
            {
                ThrowIfDisposed();
                for (LinkedListNode<LaidOutPage>? node = _lru.First; node is not null; node = node.Next)
                {
                    if (node.Value.Index == index)
                    {
                        _lru.Remove(node);
                        if (node.Value.IsProvisional && _source.IsPageCountFinal)
                        {
                            break; // rebuild now that page-count fields can be exact
                        }
                        _lru.AddFirst(node);
                        return node.Value;
                    }
                }
                _source.EnsurePages(index + 1, cancellationToken);
                if (index >= _source.KnownPageCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), $"The document has {_source.KnownPageCount} page(s).");
                }
                LaidOutPage page = _source.BuildPage(index, cancellationToken);
                _lru.AddFirst(page);
                while (_lru.Count > _cacheSize)
                {
                    _lru.RemoveLast();
                }
                return page;
            }
        }

        /// <summary>Lays out every page in order.</summary>
        public IEnumerable<LaidOutPage> GetPages(CancellationToken cancellationToken = default)
        {
            for (int i = 0; ; i++)
            {
                bool exists;
                lock (_gate)
                {
                    ThrowIfDisposed();
                    _source.EnsurePages(i + 1, cancellationToken);
                    exists = i < _source.KnownPageCount;
                }
                if (!exists)
                {
                    yield break;
                }
                yield return GetPage(i, cancellationToken);
            }
        }

        /// <summary>Every font substitution made so far (grows as pages are laid out).</summary>
        public IReadOnlyList<FontSubstitution> FontSubstitutions => _context.Fonts.ReportedSubstitutions;

        /// <summary>Warnings raised so far (grows as pages are laid out).</summary>
        public IReadOnlyList<LayoutWarning> Warnings => _context.Warnings;

        public LayoutSourceInfo Source => _source.Source;

        /// <summary>The engine behind the pages (tests and diagnostics).</summary>
        internal IPageSource PageSource => _source;

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _lru.Clear();
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
