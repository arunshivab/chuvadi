using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// List resolution and label computation: which list and level number a paragraph (directly,
/// through the built-in default lists, or through its paragraph style), following
/// numStyleLink; and the labels Word shows, counted per story in document order.
/// </summary>
internal static class ListNumbering
{
    private const int MaxLinkDepth = 16;

    /// <summary>A paragraph's resolved list membership.</summary>
    internal readonly record struct ListRef(ListInstance List, int Level, NumberingDefinition Definition)
    {
        /// <summary>The level as shown: an instance override, else the (link-resolved) definition's level.</summary>
        public NumberingLevel LevelAt(int level)
            => List.Overrides.TryGetValue(level, out var o) && o.Level is { } l ? l : Definition.Levels[level];
    }

    /// <summary>
    /// The list a paragraph belongs to, or null. Direct membership first (a list of the
    /// document's <see cref="NumberingCollection"/>, or a built-in default list), then the
    /// paragraph style chain: the nearest numId and, independently, the nearest stated level
    /// (paragraph, then styles) — a style may set only the level (w:numPr with only w:ilvl).
    /// </summary>
    public static ListRef? Resolve(Document doc, Paragraph p)
    {
        if (p.ListInstance is { } li)
            return doc.Numbering.Contains(li) ? Make(doc, li, Math.Clamp(p.ListLevel, 0, 8)) : null;
        switch (p.LegacyListKind)
        {
            case ListKind.Bullet: return Make(doc, doc.LegacyDefaults.Bullets, Math.Clamp(p.ListLevel, 0, 8));
            case ListKind.Number: return Make(doc, doc.LegacyDefaults.Numbers, Math.Clamp(p.ListLevel, 0, 8));
        }
        if (p.NumberingSuppressed) return null;

        var styleId = FormatResolver.ParagraphStyleId(doc, p);
        var chain = FormatResolver.Chain(doc, styleId, StyleType.Paragraph);
        ListInstance? list = null;
        StyleDefinition? owner = null;
        for (int i = chain.Count - 1; i >= 0 && list is null; i--)
        {
            var s = chain[i];
            if (s.NumberingSuppressed) return null;
            if (s.ListInstance is { } sl)
            {
                if (!doc.Numbering.Contains(sl)) return null;
                list = sl;
                owner = s;
            }
        }
        if (list is null) return null;
        var resolved = ResolveDefinition(doc, list.Definition);
        int? level = p.StyleListLevel;
        for (int i = chain.Count - 1; i >= 0 && level is null; i--)
            if (chain[i].ListLevel is int sl) level = sl;
        level ??= LinkedLevel(resolved, styleId) ?? LinkedLevel(resolved, owner!.StyleId) ?? 0;
        return new ListRef(list, Math.Clamp(level.Value, 0, 8), resolved);
    }

    private static ListRef Make(Document doc, ListInstance list, int level) => new(list, level, ResolveDefinition(doc, list.Definition));

    private static int? LinkedLevel(NumberingDefinition d, string? styleId)
    {
        if (styleId is null) return null;
        for (int i = 0; i < 9; i++) if (d.Levels[i].ParagraphStyleId == styleId) return i;
        return null;
    }

    /// <summary>Follows w:numStyleLink (definition → numbering style → its list's definition), cycle- and depth-guarded.</summary>
    public static NumberingDefinition ResolveDefinition(Document doc, NumberingDefinition d)
    {
        var seen = new HashSet<NumberingDefinition>(ReferenceEqualityComparer.Instance);
        while (d.NumStyleLink is string link && seen.Add(d) && seen.Count <= MaxLinkDepth)
        {
            if (!doc.Styles.TryGet(link, out var st) || st.ListInstance is not { } next) break;
            if (ReferenceEquals(next.Definition, d)) break;
            d = next.Definition;
        }
        return d;
    }

    // =====================================================================================
    // Labels
    // =====================================================================================

    public static Dictionary<Paragraph, ListLabel> Compute(Document doc)
    {
        var result = new Dictionary<Paragraph, ListLabel>(ReferenceEqualityComparer.Instance);
        var body = new Counter(doc, result);
        foreach (var b in doc.Blocks)
        {
            if (b is Paragraph p) body.Item(p, null);
            else if (b is DocTable t) body.Table(t, 0);
        }
        foreach (var story in DocumentSerializer.HeaderFooterStories(doc))
        {
            var c = new Counter(doc, result);
            foreach (var b in story.Blocks)
            {
                if (b is Paragraph p) c.Item(p, null);
                else if (b is DocTable t) c.Table(t, 0);
            }
        }
        return result;
    }

    private sealed class Counter
    {
        private readonly Document _doc;
        private readonly Dictionary<Paragraph, ListLabel> _out;
        private readonly Dictionary<NumberingDefinition, int?[]> _counters = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<NumberingDefinition, int?[]> _pendingStart = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ListInstance> _seen = new(ReferenceEqualityComparer.Instance);

        public Counter(Document doc, Dictionary<Paragraph, ListLabel> output) { _doc = doc; _out = output; }

        public void Table(DocTable t, int depth)
        {
            if (depth > 512) return; // far beyond what MaxXmlDepth admits on load
            for (int r = 0; r < t.Rows.Count; r++)
            {
                var row = t.Rows[r];
                for (int c = 0; c < row.Cells.Count; c++)
                    foreach (var b in row.Cells[c].Blocks)
                    {
                        if (b is Paragraph p) Item(p, new TableCellRef(t, r, c));
                        else if (b is DocTable nested) Table(nested, depth + 1);
                    }
            }
        }

        public void Item(Paragraph p, TableCellRef? cell)
        {
            if (Resolve(_doc, p) is not ListRef lr) return;
            var key = lr.Definition;
            if (!_counters.TryGetValue(key, out var counters)) _counters[key] = counters = new int?[9];
            if (!_pendingStart.TryGetValue(key, out var pending)) _pendingStart[key] = pending = new int?[9];

            // First use of an instance: its start overrides restart those levels (Word's "Restart at").
            if (_seen.Add(lr.List))
                foreach (var (lvl, o) in lr.List.Overrides)
                    if (o.StartOverride is int so) { counters[lvl] = null; pending[lvl] = so; }

            int L = lr.Level;
            // A shallower level that has not occurred yet counts as having occurred once, at its
            // start (Word and LibreOffice: "1.a" then "2." when a list begins at level 2).
            for (int j = 0; j < L; j++)
                if (counters[j] is null) { counters[j] = pending[j] ?? lr.LevelAt(j).Start ?? 0; pending[j] = null; }
            if (counters[L] is int current) counters[L] = current == int.MaxValue ? current : current + 1;
            else { counters[L] = pending[L] ?? lr.LevelAt(L).Start ?? 0; pending[L] = null; }

            // Deeper levels restart after this item unless their restart rule says otherwise.
            for (int d = L + 1; d < 9; d++)
            {
                var restart = lr.LevelAt(d).RestartAfterLevel;
                if (restart == 0) continue;
                int threshold = restart ?? d; // 1-based: restart after items at levels 1..threshold
                if (L + 1 <= threshold) counters[d] = null;
            }

            var level = lr.LevelAt(L);
            var text = BuildText(lr, level, counters, pending);
            var format = FormatResolver.ResolveListLabelFormat(_doc, p, level.RunFormat, level.RunStyleId, cell);
            _out[p] = new ListLabel(text, counters[L] ?? 0, L, lr.List, level, level.Suffix ?? ListSuffix.Tab, format);
        }

        private static string BuildText(ListRef lr, NumberingLevel level, int?[] counters, int?[] pending)
        {
            var pattern = level.Text;
            if (string.IsNullOrEmpty(pattern)) return "";
            bool legal = level.IsLegal == true;
            var sb = new StringBuilder(pattern.Length + 8);
            for (int i = 0; i < pattern.Length; i++)
            {
                char ch = pattern[i];
                if (ch == '%' && i + 1 < pattern.Length && pattern[i + 1] is >= '1' and <= '9')
                {
                    int k = pattern[i + 1] - '1';
                    var kl = lr.LevelAt(k);
                    int value = counters[k] ?? pending[k] ?? kl.Start ?? 0;
                    var fmt = legal ? NumberFormat.Decimal : kl.Format ?? NumberFormat.Decimal;
                    if (kl.Format is null && kl.RawFormatToken is not null && !legal) fmt = NumberFormat.Decimal;
                    sb.Append(NumberFormatter.Format(value, fmt, kl.CustomFormat));
                    i++;
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}

/// <summary>Renders a counter value in a list number format.</summary>
internal static class NumberFormatter
{
    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    };
    private static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

    /// <summary>
    /// Formats <paramref name="n"/>. Formats without a rendering here (East Asian counting
    /// systems, Hebrew, Arabic, Hindi letters, Thai letters and counting, Russian letters,
    /// baht/dollar text…) fall back to decimal digits.
    /// </summary>
    public static string Format(int n, NumberFormat format, string? custom = null)
    {
        switch (format)
        {
            case NumberFormat.Bullet:
            case NumberFormat.None:
                return "";
            case NumberFormat.DecimalZero:
                return n is >= 0 and < 10 ? "0" + Dec(n) : Dec(n);
            case NumberFormat.UpperRoman:
                return Roman(n) ?? Dec(n);
            case NumberFormat.LowerRoman:
                return Roman(n)?.ToLowerInvariant() ?? Dec(n);
            case NumberFormat.UpperLetter:
                return Letters(n, 'A') ?? Dec(n);
            case NumberFormat.LowerLetter:
                return Letters(n, 'a') ?? Dec(n);
            case NumberFormat.Ordinal:
                return n < 0 ? Dec(n) : Dec(n) + OrdinalSuffix(n);
            case NumberFormat.CardinalText:
                return n < 0 ? Dec(n) : Capitalize(Words(n));
            case NumberFormat.OrdinalText:
                return n < 0 ? Dec(n) : Capitalize(OrdinalWords(n));
            case NumberFormat.Hex:
                return n < 0 ? Dec(n) : n.ToString("X", CultureInfo.InvariantCulture);
            case NumberFormat.Chicago:
                if (n <= 0) return Dec(n);
                return new string("*†‡§"[(n - 1) % 4], Math.Min((n - 1) / 4 + 1, 64));
            case NumberFormat.DecimalFullWidth:
            case NumberFormat.DecimalFullWidth2:
                return Digits(n, '０');
            case NumberFormat.DecimalEnclosedCircle:
                return n is >= 1 and <= 20 ? ((char)(0x2460 + n - 1)).ToString() : Dec(n);
            case NumberFormat.DecimalEnclosedFullstop:
                return n is >= 1 and <= 20 ? ((char)(0x2488 + n - 1)).ToString() : Dec(n);
            case NumberFormat.DecimalEnclosedParen:
                return n is >= 1 and <= 20 ? ((char)(0x2474 + n - 1)).ToString() : Dec(n);
            case NumberFormat.NumberInDash:
                return "- " + Dec(n) + " -";
            case NumberFormat.IdeographDigital:
            {
                if (n < 0) return Dec(n);
                const string ideo = "〇一二三四五六七八九";
                var sb = new StringBuilder();
                foreach (var c in Dec(n)) sb.Append(ideo[c - '0']);
                return sb.ToString();
            }
            case NumberFormat.HindiNumbers:
                return Digits(n, '०');
            case NumberFormat.ThaiNumbers:
                return Digits(n, '๐');
            case NumberFormat.Custom:
                return Custom(n, custom) ?? Dec(n);
            default:
                return Dec(n);
        }
    }

    private static string Dec(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Digits(int n, char zero)
    {
        if (n < 0) return Dec(n);
        var s = Dec(n).ToCharArray();
        for (int i = 0; i < s.Length; i++) s[i] = (char)(zero + (s[i] - '0'));
        return new string(s);
    }

    private static string? Roman(int n)
    {
        if (n <= 0 || n > 32767) return null;
        var sb = new StringBuilder();
        sb.Append('M', n / 1000);
        n %= 1000;
        string[] h = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
        string[] t = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
        string[] o = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        return sb.Append(h[n / 100]).Append(t[n / 10 % 10]).Append(o[n % 10]).ToString();
    }

    /// <summary>a … z, aa … zz, aaa … (Word repeats the letter).</summary>
    private static string? Letters(int n, char a)
    {
        if (n <= 0) return null;
        int repeat = (n - 1) / 26 + 1;
        if (repeat > 64) return null;
        return new string((char)(a + (n - 1) % 26), repeat);
    }

    private static string OrdinalSuffix(int n)
    {
        int h = n % 100;
        if (h is >= 11 and <= 13) return "th";
        return (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>English cardinal words: "one hundred twenty-three".</summary>
    internal static string Words(int n)
    {
        if (n < 20) return Ones[n];
        if (n < 100) return Tens[n / 10] + (n % 10 == 0 ? "" : "-" + Ones[n % 10]);
        if (n < 1000) return Ones[n / 100] + " hundred" + (n % 100 == 0 ? "" : " " + Words(n % 100));
        if (n < 1_000_000) return Words(n / 1000) + " thousand" + (n % 1000 == 0 ? "" : " " + Words(n % 1000));
        if (n < 1_000_000_000) return Words(n / 1_000_000) + " million" + (n % 1_000_000 == 0 ? "" : " " + Words(n % 1_000_000));
        return Words(n / 1_000_000_000) + " billion" + (n % 1_000_000_000 == 0 ? "" : " " + Words(n % 1_000_000_000));
    }

    /// <summary>English ordinal words: "twenty-first", "one hundredth".</summary>
    internal static string OrdinalWords(int n)
    {
        var w = Words(n);
        // Change the last word to its ordinal form.
        int cut = Math.Max(w.LastIndexOf(' '), w.LastIndexOf('-')) + 1;
        var head = w[..cut];
        var last = w[cut..];
        var ord = last switch
        {
            "zero" => "zeroth", "one" => "first", "two" => "second", "three" => "third", "five" => "fifth",
            "eight" => "eighth", "nine" => "ninth", "twelve" => "twelfth",
            _ when last.EndsWith('y') => last[..^1] + "ieth",
            _ => last + "th",
        };
        return head + ord;
    }

    /// <summary>
    /// A custom format from its sample sequence, e.g. "001, 002, 003, ..." (zero-padded to
    /// three digits) or "௧, ௨, ௩, ..." (Tamil digits): the first item's decimal digits give the
    /// digit set and the minimum width; text around them is kept.
    /// </summary>
    private static string? Custom(int n, string? sample)
    {
        if (n < 0 || string.IsNullOrEmpty(sample)) return null;
        var first = sample.Split(',')[0].Trim();
        int start = -1, end = -1;
        char zero = '0';
        for (int i = 0; i < first.Length; i++)
        {
            int v = CharUnicodeInfo.GetDecimalDigitValue(first[i]);
            if (v < 0) { if (start >= 0) break; continue; }
            if (start < 0) { start = i; zero = (char)(first[i] - v); }
            end = i;
        }
        if (start < 0) return null;
        int width = end - start + 1;
        var digits = Digits(n, zero);
        if (digits.Length < width) digits = new string(zero, width - digits.Length) + digits;
        return first[..start] + digits + first[(end + 1)..];
    }
}
