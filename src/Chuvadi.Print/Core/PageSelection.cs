using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chuvadi.Print
{
    /// <summary>
    /// Which pages of a document to print. Every selection style is supported:
    /// All, None, a single Current page, an arbitrary Range expression ("1-4, 7"),
    /// an Explicit list, Odd, Even, First(n) and Last(n).
    /// Page numbers are 1-based throughout.
    /// </summary>
    public sealed class PageSelection
    {
        private enum Kind { All, None, Current, Range, Explicit, Odd, Even, First, Last }

        private readonly Kind _kind;
        private readonly int _n;                 // Current page, or count for First/Last
        private readonly string? _spec;          // Range expression
        private readonly (int Start, int End)[]? _ranges;   // Range expression, parsed: never expanded to pages
        private readonly int[]? _list;           // Explicit pages

        private PageSelection(Kind kind, int n = 0, string? spec = null, int[]? list = null, (int, int)[]? ranges = null)
        {
            _kind = kind;
            _n = n;
            _spec = spec;
            _list = list;
            _ranges = ranges;
        }

        public static PageSelection All { get; } = new(Kind.All);
        public static PageSelection None { get; } = new(Kind.None);
        public static PageSelection Odd { get; } = new(Kind.Odd);
        public static PageSelection Even { get; } = new(Kind.Even);

        public static PageSelection Current(int pageNumber)
        {
            if (pageNumber < 1) throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page numbers are 1-based.");
            return new PageSelection(Kind.Current, n: pageNumber);
        }

        public static PageSelection First(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            return new PageSelection(Kind.First, n: count);
        }

        public static PageSelection Last(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            return new PageSelection(Kind.Last, n: count);
        }

        public static PageSelection Explicit(params int[] pageNumbers)
        {
            if (pageNumbers is null) throw new ArgumentNullException(nameof(pageNumbers));
            foreach (int p in pageNumbers)
                if (p < 1) throw new ArgumentOutOfRangeException(nameof(pageNumbers), "Page numbers are 1-based.");
            return new PageSelection(Kind.Explicit, list: (int[])pageNumbers.Clone());
        }

        /// <summary>
        /// A range expression such as "1-4, 7". It is checked now, so a bad one fails fast, and kept as its intervals:
        /// a range such as "1-2147483647" costs nothing until <see cref="Resolve"/> lists the pages of a document.
        /// </summary>
        public static PageSelection Range(string spec)
        {
            if (spec is null) throw new ArgumentNullException(nameof(spec));
            return new PageSelection(Kind.Range, spec: spec, ranges: ParseRange(spec));
        }

        /// <summary>Resolves to sorted, distinct, 1-based page numbers within [1, pageCount].</summary>
        public IReadOnlyList<int> Resolve(int pageCount)
        {
            if (pageCount < 0) throw new ArgumentOutOfRangeException(nameof(pageCount));
            var set = new SortedSet<int>();
            switch (_kind)
            {
                case Kind.All:
                    for (int p = 1; p <= pageCount; p++) set.Add(p);
                    break;
                case Kind.None:
                    break;
                case Kind.Odd:
                    for (int p = 1; p <= pageCount; p += 2) set.Add(p);
                    break;
                case Kind.Even:
                    for (int p = 2; p <= pageCount; p += 2) set.Add(p);
                    break;
                case Kind.Current:
                    if (_n >= 1 && _n <= pageCount) set.Add(_n);
                    break;
                case Kind.First:
                    for (int p = 1; p <= Math.Min(_n, pageCount); p++) set.Add(p);
                    break;
                case Kind.Last:
                    for (int p = Math.Max(1, pageCount - _n + 1); p <= pageCount; p++) set.Add(p);
                    break;
                case Kind.Explicit:
                    if (_list is not null)
                        foreach (int p in _list) if (p >= 1 && p <= pageCount) set.Add(p);
                    break;
                case Kind.Range:
                    // Each interval is cut to the document first, so this lists at most pageCount pages.
                    foreach ((int start, int end) in _ranges!)
                        for (long p = start, last = Math.Min(end, pageCount); p <= last; p++) set.Add((int)p);
                    break;
            }
            var result = new int[set.Count];
            set.CopyTo(result);
            return result;
        }

        /// <summary>A stable textual form used by the spool envelope.</summary>
        public string ToCanonicalString()
        {
            switch (_kind)
            {
                case Kind.All: return "all";
                case Kind.None: return "none";
                case Kind.Odd: return "odd";
                case Kind.Even: return "even";
                case Kind.Current: return "current:" + _n.ToString(CultureInfo.InvariantCulture);
                case Kind.First: return "first:" + _n.ToString(CultureInfo.InvariantCulture);
                case Kind.Last: return "last:" + _n.ToString(CultureInfo.InvariantCulture);
                case Kind.Range: return "range:" + _spec;
                case Kind.Explicit:
                    var sb = new StringBuilder("list:");
                    for (int i = 0; i < _list!.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(_list[i].ToString(CultureInfo.InvariantCulture));
                    }
                    return sb.ToString();
                default: return "all";
            }
        }

        /// <summary>Parses the canonical form produced by <see cref="ToCanonicalString"/>.</summary>
        public static PageSelection Parse(string canonical)
        {
            if (canonical is null) throw new ArgumentNullException(nameof(canonical));
            switch (canonical)
            {
                case "all": return All;
                case "none": return None;
                case "odd": return Odd;
                case "even": return Even;
            }
            int colon = canonical.IndexOf(':');
            if (colon < 0) throw new FormatException("Unrecognised page selection: " + canonical);
            string key = canonical.Substring(0, colon);
            string value = canonical.Substring(colon + 1);
            switch (key)
            {
                case "current": return Current(int.Parse(value, CultureInfo.InvariantCulture));
                case "first": return First(int.Parse(value, CultureInfo.InvariantCulture));
                case "last": return Last(int.Parse(value, CultureInfo.InvariantCulture));
                case "range": return Range(value);
                case "list":
                    if (value.Length == 0) return Explicit();
                    string[] parts = value.Split(',');
                    var nums = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++) nums[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
                    return Explicit(nums);
                default: throw new FormatException("Unrecognised page selection: " + canonical);
            }
        }

        private static (int Start, int End)[] ParseRange(string spec)
        {
            var ranges = new List<(int, int)>();
            foreach (string rawToken in spec.Split(','))
            {
                string token = rawToken.Trim();
                if (token.Length == 0) continue;
                int dash = token.IndexOf('-');
                if (dash < 0)
                {
                    int page = ParsePositive(token);
                    ranges.Add((page, page));
                }
                else
                {
                    int start = ParsePositive(token.Substring(0, dash).Trim());
                    int end = ParsePositive(token.Substring(dash + 1).Trim());
                    if (end < start) throw new FormatException("Range end before start: " + token);
                    ranges.Add((start, end));
                }
            }
            return ranges.ToArray();
        }

        private static int ParsePositive(string s)
        {
            if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1)
                throw new FormatException("Invalid page number: '" + s + "'");
            return value;
        }
    }
}
