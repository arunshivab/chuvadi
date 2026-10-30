using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Conversion.Internal;

/// <summary>Inline formatting while importing (one entry per open element / emphasis).</summary>
internal record struct InlineState(bool Bold, bool Italic, bool Underline, bool Strike, bool Code, bool Sub, bool Sup,
    string? ColorHex, string? BackgroundHex, double SizePt, string? Font, bool SmallCaps, bool Caps,
    string? LinkUrl, string? LinkAnchor);

/// <summary>
/// Builds document content for the Markdown and HTML importers: paragraphs with styles, lists,
/// quotes, tables, links, pictures and bookmarks, in the body or in a table cell.
/// </summary>
internal sealed class DocBuilder
{
    private interface ISink
    {
        void Add(Paragraph p);
        DocTable AddTable(int columns);
    }

    private sealed class BodySink : ISink
    {
        private readonly Document _d;
        public BodySink(Document d) { _d = d; }
        public void Add(Paragraph p) => _d.AddParagraph(p);
        public DocTable AddTable(int columns) => _d.AddTable(columns);
    }

    private sealed class CellSink : ISink
    {
        public readonly TableCell Cell;
        public CellSink(TableCell c) { Cell = c; }
        public void Add(Paragraph p) => Cell.Paragraphs.Add(p);
        public DocTable AddTable(int columns) => Cell.AddTable(columns);
    }

    internal sealed class ListFrame
    {
        public ListInstance List = null!;
        public int Level;
        public bool ItemOpen;
        public bool ItemNumbered;   // the current item's first paragraph has been written
        public string? TaskMarker;
    }

    public readonly Document Doc;
    public readonly ImportOptions Options;
    public readonly List<string> Warnings = new();
    public readonly List<string> Blocked = new();

    private readonly Stack<ISink> _sinks = new();
    private readonly List<InlineState> _inline = new();
    private readonly List<ListFrame> _lists = new();
    private readonly HashSet<string> _takenNames = new(StringComparer.Ordinal);   // bookmark names in use (existing + assigned)
    private readonly HashSet<string> _placed = new(StringComparer.Ordinal);       // bookmarks written by this import
    private readonly Dictionary<string, string> _nameFor = new(StringComparer.Ordinal); // source id → bookmark name
    private readonly Dictionary<string, int> _nextSuffix = new(StringComparer.Ordinal);  // base name → next suffix to try (O(1) per collision)
    private readonly List<string> _pendingBookmarks = new();
    private ListInstance? _bullets;
    private Paragraph? _p;
    private bool _pendingSpace;
    private InlineState _pendingState;   // the formatting of the white space that made the pending space
    private int _quoteDepth;
    private int _steps;
    private readonly System.Threading.CancellationToken _ct;

    // Block context for the next paragraph.
    public string? NextStyleId;
    public ParagraphAlignment? NextAlignment;
    public double NextIndentPt;
    public bool NextPageBreakBefore;
    public int PreDepth;                 // inside <pre> / a code block: whitespace kept

    public DocBuilder(Document doc, ImportOptions options, System.Threading.CancellationToken ct)
    {
        Doc = doc;
        Options = options;
        _ct = ct;
        _sinks.Push(new BodySink(doc));
        _inline.Add(default);
        // Every story of the target: an appended import never reuses a name it already has.
        foreach (var b in doc.GetBookmarks()) _takenNames.Add(b.Name);
    }

    public void Tick()
    {
        if ((++_steps & 1023) == 0) _ct.ThrowIfCancellationRequested();
    }

    // ---------------- inline state ----------------

    public InlineState State => _inline[^1];

    public void PushInline(InlineState s) => _inline.Add(s);

    public void PopInline()
    {
        if (_inline.Count > 1) _inline.RemoveAt(_inline.Count - 1);
    }

    public int InlineDepth => _inline.Count;

    public void RestoreInline(int depth)
    {
        while (_inline.Count > depth && _inline.Count > 1) _inline.RemoveAt(_inline.Count - 1);
    }

    private TextFormat Format(InlineState s)
    {
        if (!s.Bold && !s.Italic && !s.Underline && !s.Strike && !s.Code && !s.Sub && !s.Sup && s.ColorHex is null
            && s.BackgroundHex is null && s.SizePt <= 0 && s.Font is null && !s.SmallCaps && !s.Caps)
            return TextFormat.None;
        string? font = s.Code ? Options.CodeFont : s.Font;
        return new TextFormat
        {
            Bold = s.Bold,
            Italic = s.Italic,
            Underline = s.Underline,
            Strikethrough = s.Strike,
            VerticalPosition = s.Sup ? VerticalTextPosition.Superscript : s.Sub ? VerticalTextPosition.Subscript : null,
            ColorHex = s.ColorHex,
            Highlight = HighlightName(s.BackgroundHex),
            Shading = s.BackgroundHex is { } bg && HighlightName(bg) is null ? Shading.Fill(bg) : null,
            SizePt = s.SizePt > 0 ? s.SizePt : 0,
            FontForAllScripts = font,
            SmallCaps = s.SmallCaps ? true : null,
            Caps = s.Caps ? true : null,
        };
    }

    private static string? HighlightName(string? hex) => hex?.ToUpperInvariant() switch
    {
        "FFFF00" => "yellow",
        "00FF00" => "green",
        "00FFFF" => "cyan",
        "FF00FF" => "magenta",
        _ => null,
    };

    // ---------------- paragraphs ----------------

    public bool InParagraph => _p is not null;

    /// <summary>Ends the current paragraph (if any); the next text starts a new one with the block context.</summary>
    public void EndParagraph()
    {
        if (_p is null) return;
        FlushBookmarks();
        _p = null;
        _pendingSpace = false;
    }

    /// <summary>Starts a paragraph now, even if nothing is written into it (an empty line, an empty cell).</summary>
    public Paragraph StartParagraph()
    {
        EndParagraph();
        return Ensure();
    }

    private Paragraph Ensure()
    {
        if (_p is not null) return _p;
        var p = new Paragraph();
        if (NextStyleId is { } sid) p.StyleId = sid;
        else if (_quoteDepth > 0) p.StyleId = EnsureQuoteStyle();
        if (NextAlignment is { } al) p.Alignment = al;
        double indent = NextIndentPt;
        if (_quoteDepth > 1) indent += Options.NestedQuoteIndentPt * (_quoteDepth - 1);
        if (_lists.Count > 0)
        {
            var f = _lists[^1];
            if (f.ItemOpen && !f.ItemNumbered)
            {
                p.SetList(f.List, Math.Min(f.Level, 8));
                f.ItemNumbered = true;
                if (f.TaskMarker is { } tm) { p.Text(tm, Format(State)); }
            }
            else if (f.ItemOpen)
            {
                // A further paragraph of the same item: aligned with the item's text, no number.
                indent += 36 * (Math.Min(f.Level, 8) + 1);
            }
        }
        if (indent > 0) p.Format.IndentLeftPt = (p.Format.IndentLeftPt ?? 0) + indent;
        if (NextPageBreakBefore) { p.PageBreakBefore = true; NextPageBreakBefore = false; }
        _sinks.Peek().Add(p);
        _p = p;
        FlushBookmarks();
        return p;
    }

    // ---------------- text ----------------

    /// <summary>Writes text. With <paramref name="collapse"/>, runs of white space become one space (HTML rendering).</summary>
    public void Text(string text, bool collapse)
    {
        if (string.IsNullOrEmpty(text)) return;
        Tick();
        if (!collapse || PreDepth > 0)
        {
            WritePreserved(text);
            return;
        }
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                if (!_pendingSpace) _pendingState = State;
                _pendingSpace = true;
                continue;
            }
            if (_pendingSpace)
            {
                // One space, carrying the formatting it had where it was written (so "a <b>b</b>"
                // keeps the space outside the bold run).
                if (sb.Length > 0) sb.Append(' ');
                else if (_p is not null && HasContent(_p)) WriteWith(" ", _pendingState);
                _pendingSpace = false;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) Write(sb.ToString());
    }

    private static bool HasContent(Paragraph p)
    {
        for (int i = p.Runs.Count - 1; i >= 0; i--)
        {
            var r = p.Runs[i];
            if (r.Bookmark is not null) continue;
            if (r.Break is not null) return false;   // no leading space after a line break
            return true;
        }
        return false;
    }

    private void WritePreserved(string text)
    {
        // Line breaks inside preformatted text become line breaks; tabs become tab runs.
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r') continue;
            if (c == '\n') { Flush(); LineBreak(); continue; }
            if (c == '\t') { Flush(); Ensure().Tab(Format(State)); continue; }
            sb.Append(c);
        }
        Flush();

        void Flush()
        {
            if (sb.Length == 0) return;
            Write(sb.ToString());
            sb.Clear();
        }
    }

    private void Write(string text) => WriteWith(text, State);

    private void WriteWith(string text, InlineState s)
    {
        text = TextSanitize.ValidXml(text);
        if (text.Length == 0) return;
        var p = Ensure();
        var f = Format(s);
        if (s.LinkUrl is { } url) p.Hyperlink(text, url, f);
        else if (s.LinkAnchor is { } anchor) p.InternalLink(text, NameFor(anchor), f);
        else p.Text(text, f);
    }

    public void LineBreak()
    {
        Ensure().Break(BreakType.Line, Format(State));
        _pendingSpace = false;
    }

    public void PageBreak()
    {
        Ensure().Break(BreakType.Page);
        _pendingSpace = false;
    }

    /// <summary>A pending collapsed space is written (between inline elements).</summary>
    public void Space()
    {
        if (_p is not null && HasContent(_p)) _pendingSpace = true;
    }

    // ---------------- pictures ----------------

    public void Image(string? src, string? alt)
    {
        byte[]? bytes = null;
        string? type = null;
        if (src is not null && DataUri.TryDecode(src, Options.MaxImageBytes, out var b, out var t)) { bytes = b; type = t; }
        else if (src is not null && Options.ImageResolver is { } resolve)
        {
            try { bytes = resolve(new ImageRequest(src, alt)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Warnings.Add("Picture resolver failed for " + src + ": " + ex.Message); }
            if (bytes is { Length: > 0 }) type = ImageTypes.Sniff(bytes);
        }
        if (bytes is null || bytes.Length == 0 || bytes.Length > Options.MaxImageBytes || type is null || !Options.AllowedImageTypes.Contains(type))
        {
            if (src is not null)
            {
                Warnings.Add("Picture not imported: " + Shorten(src));
                // A picture that would have to be fetched is listed for a "show remote content" prompt.
                if (bytes is null && !src.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase) && !Blocked.Contains(src)) Blocked.Add(src);
            }
            if (!string.IsNullOrEmpty(alt)) Text(alt, collapse: true);
            return;
        }
        ImageSpec spec;
        try { spec = ImageSpec.FromBytes(bytes, type); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or System.IO.InvalidDataException)
        {
            Warnings.Add("Picture not imported (" + ex.Message + "): " + Shorten(src ?? ""));
            if (!string.IsNullOrEmpty(alt)) Text(alt, collapse: true);
            return;
        }
        // A header can claim any size: only finite, positive, plausible sizes are kept (up to 200 inches).
        const double MaxSidePt = 14400;
        if (!(double.IsFinite(spec.WidthPt) && double.IsFinite(spec.HeightPt) && spec.WidthPt > 0 && spec.HeightPt > 0)
            || spec.WidthPt / spec.HeightPt > 1e4 || spec.HeightPt / spec.WidthPt > 1e4)
        {
            Warnings.Add("Picture not imported (implausible size): " + Shorten(src ?? ""));
            if (!string.IsNullOrEmpty(alt)) Text(alt, collapse: true);
            return;
        }
        if (Options.MaxImageWidthPt is double max && spec.WidthPt > max)
            spec = spec.ScaleToWidth(max);
        if (spec.WidthPt > MaxSidePt) spec = spec.ScaleToWidth(MaxSidePt);
        if (spec.HeightPt > MaxSidePt) spec = spec.ScaleToHeight(MaxSidePt);
        if (!string.IsNullOrEmpty(alt)) spec.AltText = alt;
        if (_pendingSpace && _p is not null && HasContent(_p)) WriteWith(" ", _pendingState);
        _pendingSpace = false;
        Ensure().Image(spec);
    }

    private static string Shorten(string s) => s.Length > 80 ? s.Substring(0, 80) + "…" : s;

    // ---------------- bookmarks ----------------

    /// <summary>A bookmark at the current position (start and end together; links to it jump here).</summary>
    public void Bookmark(string id)
    {
        if (!Options.CreateBookmarks || string.IsNullOrEmpty(id)) return;
        var name = NameFor(id);
        if (!_placed.Add(name)) return;
        _pendingBookmarks.Add(name);
        if (_p is not null) FlushBookmarks();
    }

    private void FlushBookmarks()
    {
        if (_p is null || _pendingBookmarks.Count == 0) return;
        foreach (var n in _pendingBookmarks)
        {
            var b = new Bookmark(n);
            _p.BookmarkStart(b).BookmarkEnd(b);
        }
        _pendingBookmarks.Clear();
    }

    /// <summary>
    /// The bookmark name for a source id, the same for links and targets (links may come first).
    /// Distinct ids that sanitize alike, and names the document already has, get a suffix.
    /// </summary>
    public string NameFor(string id)
    {
        if (_nameFor.TryGetValue(id, out var name)) return name;
        var core = BookmarkName(id);
        name = core;
        if (_takenNames.Contains(name))
        {
            int n = _nextSuffix.TryGetValue(core, out var next) ? next : 2;
            do
            {
                var suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
                name = (core.Length + suffix.Length > 40 ? core.Substring(0, 40 - suffix.Length) : core) + suffix;
                n++;
            }
            while (_takenNames.Contains(name));
            _nextSuffix[core] = n;
        }
        _takenNames.Add(name);
        _nameFor[id] = name;
        return name;
    }

    /// <summary>A valid Word bookmark name for an HTML id / Markdown anchor.</summary>
    public static string BookmarkName(string id)
    {
        var sb = new StringBuilder();
        foreach (char c in id)
        {
            if (sb.Length >= 40) break;
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
        }
        if (sb.Length == 0 || !char.IsLetter(sb[0])) sb.Insert(0, 'b');
        if (sb.Length > 40) sb.Length = 40;
        return sb.ToString();
    }

    // ---------------- blocks ----------------

    public void BeginQuote() { EndParagraph(); _quoteDepth++; }
    public void EndQuote() { EndParagraph(); if (_quoteDepth > 0) _quoteDepth--; }

    public void BeginList(bool ordered, int start)
    {
        NumberOpenItem();
        int level = _lists.Count;
        ListInstance list;
        if (!ordered)
        {
            // Bullets share one list (nesting is by level); each ordered list counts on its own.
            if (_lists.Count > 0 && _lists[^1].List.Definition.Levels[0].Format == NumberFormat.Bullet) list = _lists[^1].List;
            else list = _bullets ??= Doc.Numbering.AddBulletList();
        }
        else
        {
            list = Doc.Numbering.AddNumberedList();
            // Word counts nested levels inside the same list; a fresh list per ordered list keeps the
            // numbers of separate lists independent, and the level gives the nesting.
            if (start != 1) list.Override(Math.Min(level, 8)).StartOverride = Math.Max(0, start);
            else if (level > 0) list.Override(Math.Min(level, 8)).StartOverride = 1;
        }
        _lists.Add(new ListFrame { List = list, Level = level });
    }

    public void EndList()
    {
        EndParagraph();
        if (_lists.Count > 0) _lists.RemoveAt(_lists.Count - 1);
    }

    public void BeginItem(string? taskMarker = null)
    {
        EndParagraph();
        if (_lists.Count == 0) BeginList(false, 1);
        var f = _lists[^1];
        f.ItemOpen = true;
        f.ItemNumbered = false;
        f.TaskMarker = taskMarker;
    }

    public void EndItem()
    {
        EndParagraph();
        if (_lists.Count == 0) return;
        var f = _lists[^1];
        if (f.ItemOpen && !f.ItemNumbered)
        {
            // An empty item still shows its number.
            Ensure();
            EndParagraph();
        }
        f.ItemOpen = false;
    }

    public int ListDepth => _lists.Count;

    /// <summary>Word's limit on table columns.</summary>
    public const int MaxColumns = 63;

    private long _cells;
    private bool _cellWarning;

    /// <summary>Reserves cells for one table row against <see cref="ImportOptions.MaxTableCells"/>; false (with one warning) once the budget is spent.</summary>
    public bool ReserveCells(int count)
    {
        if (_cells + count > Options.MaxTableCells)
        {
            if (!_cellWarning) Warnings.Add($"More than {Options.MaxTableCells} table cells; further rows were left out.");
            _cellWarning = true;
            return false;
        }
        _cells += count;
        return true;
    }

    /// <summary>
    /// A list item whose first content is a block (a nested list, a table): its number goes on an
    /// empty paragraph before that block, where Word shows it.
    /// </summary>
    public void NumberOpenItem()
    {
        if (_lists.Count == 0) return;
        var f = _lists[^1];
        if (!f.ItemOpen || f.ItemNumbered) return;
        EndParagraph();
        Ensure();
        EndParagraph();
    }

    public DocTable BeginTable(int columns)
    {
        NumberOpenItem();
        return _sinks.Peek().AddTable(Math.Clamp(columns, 1, MaxColumns));
    }

    public void BeginCell(TableCell cell)
    {
        EndParagraph();
        cell.Paragraphs.Clear();
        _sinks.Push(new CellSink(cell));
    }

    public void EndCell()
    {
        EndParagraph();
        if (_sinks.Count > 1 && _sinks.Pop() is CellSink cs && cs.Cell.Paragraphs.Count == 0) cs.Cell.Paragraphs.Add(new Paragraph());
    }

    /// <summary>Saves the list/quote context (a table cell starts fresh) and restores it afterwards.</summary>
    public (List<ListFrame>, int) SuspendContext()
    {
        var saved = (new List<ListFrame>(_lists), _quoteDepth);
        _lists.Clear();
        _quoteDepth = 0;
        return saved;
    }

    public void ResumeContext((List<ListFrame> Lists, int Quote) saved)
    {
        _lists.Clear();
        _lists.AddRange(saved.Lists);
        _quoteDepth = saved.Quote;
    }

    public void HorizontalRule(bool asBorder)
    {
        EndParagraph();
        if (asBorder)
        {
            var p = StartParagraph();
            p.Format.Borders = new ParagraphBorders { Bottom = new BorderSpec { Style = BorderStyle.Single, WidthPt = 0.75, ColorHex = "A0A0A0" } };
            EndParagraph();
        }
        else NextPageBreakBefore = true;
    }

    /// <summary>The quote paragraph style, created (based on Quote, or indented) when the document does not have it.</summary>
    public string EnsureQuoteStyle()
    {
        var id = Options.QuoteStyleId;
        if (!Doc.Styles.Contains(id))
        {
            var s = new StyleDefinition(id, StyleType.Paragraph) { Name = id, IsCustom = true, QuickFormat = true };
            if (Doc.Styles.Contains("Quote")) s.BasedOn = "Quote";
            else
            {
                s.BasedOn = "Normal";
                s.ParagraphFormat.IndentLeftPt = 36;
                s.ParagraphFormat.IndentRightPt = 36;
                s.RunFormat = new TextFormat { Italic = true };
            }
            Doc.Styles.Add(s);
        }
        return id;
    }

    /// <summary>The code paragraph style, created when the document does not have it.</summary>
    public string EnsureCodeStyle()
    {
        var id = Options.CodeStyleId;
        if (!Doc.Styles.Contains(id))
        {
            var s = new StyleDefinition(id, StyleType.Paragraph)
            {
                Name = id == "Code" ? "Code" : id,
                BasedOn = "Normal",
                IsCustom = true,
                QuickFormat = true,
                RunFormat = new TextFormat { FontForAllScripts = Options.CodeFont, SizePt = 10 },
            };
            s.ParagraphFormat.SpaceBeforePt = 0;
            s.ParagraphFormat.SpaceAfterPt = 0;
            s.ParagraphFormat.LineSpacing = LineSpacing.Multiple(1);
            s.ParagraphFormat.Shading = Shading.Fill("F4F4F4");
            Doc.Styles.Add(s);
        }
        return id;
    }

    public void Finish()
    {
        EndParagraph();
        if (NextPageBreakBefore) StartParagraph(); // a final page break (thematic break as page break)
        EndParagraph();
        if (_pendingBookmarks.Count > 0)
        {
            StartParagraph();
            EndParagraph();
        }
    }
}

internal static class TextSanitize
{
    /// <summary>Removes characters XML 1.0 cannot hold (control characters, lone surrogates, U+FFFE/U+FFFF).</summary>
    public static string ValidXml(string s)
    {
        int i = 0;
        for (; i < s.Length; i++)
            if (!Ok(s, i)) break;
        if (i == s.Length) return s;
        var sb = new StringBuilder(s.Length);
        for (int k = 0; k < s.Length; k++)
        {
            char c = s[k];
            if (char.IsHighSurrogate(c) && k + 1 < s.Length && char.IsLowSurrogate(s[k + 1])) { sb.Append(c).Append(s[k + 1]); k++; continue; }
            if (Ok(s, k)) sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool Ok(string s, int i)
    {
        char c = s[i];
        if (c < 0x20) return c is '\t' or '\n' or '\r';
        if (c is '￾' or '￿') return false;
        if (char.IsHighSurrogate(c)) return i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
        if (char.IsLowSurrogate(c)) return i > 0 && char.IsHighSurrogate(s[i - 1]);
        return true;
    }
}

internal static class DataUri
{
    /// <summary>Decodes a base64 <c>data:</c> URI (or a percent-encoded one) to bytes and its media type.</summary>
    public static bool TryDecode(string uri, long maxBytes, out byte[] bytes, out string type)
    {
        bytes = Array.Empty<byte>();
        type = "";
        var u = uri.Trim();
        if (!u.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        int comma = u.IndexOf(',');
        if (comma < 0) return false;
        var meta = u.Substring(5, comma - 5);
        var parts = meta.Split(';');
        type = parts[0].Trim().ToLowerInvariant();
        if (type.Length == 0) type = "text/plain";
        bool b64 = false;
        for (int i = 1; i < parts.Length; i++) if (parts[i].Trim().Equals("base64", StringComparison.OrdinalIgnoreCase)) b64 = true;
        var data = u.Substring(comma + 1);
        if ((long)data.Length * 3 / 4 > maxBytes) return false;
        try
        {
            if (b64)
            {
                var clean = new StringBuilder(data.Length);
                foreach (char c in data) if (!char.IsWhiteSpace(c)) clean.Append(c);
                bytes = Convert.FromBase64String(clean.ToString());
            }
            else bytes = Encoding.Latin1.GetBytes(Uri.UnescapeDataString(data));
        }
        catch (FormatException) { return false; }
        if (bytes.Length > maxBytes) return false;
        if (type == "image/jpg") type = "image/jpeg";
        return true;
    }

    public static string Encode(byte[] bytes, string type) => "data:" + type + ";base64," + Convert.ToBase64String(bytes);
}

internal static class ImageTypes
{
    /// <summary>The media type from the first bytes (PNG, JPEG, GIF, BMP, TIFF, WebP), or null.</summary>
    public static string? Sniff(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return "image/gif";
        if (b.Length >= 2 && b[0] == 'B' && b[1] == 'M') return "image/bmp";
        if (b.Length >= 4 && ((b[0] == 'I' && b[1] == 'I' && b[2] == 42 && b[3] == 0) || (b[0] == 'M' && b[1] == 'M' && b[2] == 0 && b[3] == 42))) return "image/tiff";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }
}

internal static class Css
{
    /// <summary>A CSS colour (#rgb, #rrggbb, rgb(), a few names) as 6-digit hex, or null.</summary>
    public static string? Color(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v.StartsWith('#'))
        {
            var h = v.Substring(1);
            if (h.Length == 3 && IsHex(h)) return string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]).ToUpperInvariant();
            if (h.Length == 6 && IsHex(h)) return h.ToUpperInvariant();
            if (h.Length == 8 && IsHex(h)) return h.Substring(0, 6).ToUpperInvariant();
            return null;
        }
        if (v.StartsWith("rgb", StringComparison.Ordinal))
        {
            int open = v.IndexOf('('), close = v.IndexOf(')');
            if (open < 0 || close < open) return null;
            var parts = v.Substring(open + 1, close - open - 1).Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return null;
            var sb = new StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                var p = parts[i];
                double d;
                if (p.EndsWith('%') && double.TryParse(p.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) d = d * 255 / 100;
                else if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return null;
                sb.Append(((int)Math.Clamp(Math.Round(d), 0, 255)).ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
        return v switch
        {
            "black" => "000000", "white" => "FFFFFF", "red" => "FF0000", "green" => "008000", "blue" => "0000FF",
            "yellow" => "FFFF00", "gray" or "grey" => "808080", "silver" => "C0C0C0", "maroon" => "800000",
            "navy" => "000080", "purple" => "800080", "teal" => "008080", "olive" => "808000", "lime" => "00FF00",
            "aqua" or "cyan" => "00FFFF", "fuchsia" or "magenta" => "FF00FF", "orange" => "FFA500",
            _ => null,
        };
    }

    private static bool IsHex(string s)
    {
        foreach (char c in s) if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    /// <summary>A CSS length in points (pt, px, em relative to 12 pt, %, keywords), or null.</summary>
    public static double? Length(string value, double emBase = 12)
    {
        var v = value.Trim().ToLowerInvariant();
        switch (v)
        {
            case "xx-small": return 7;
            case "x-small": return 7.5;
            case "small": return 10;
            case "medium": return 12;
            case "large": return 13.5;
            case "x-large": return 18;
            case "xx-large": return 24;
        }
        string[] units = { "pt", "px", "em", "rem", "%", "cm", "mm", "in", "pc" };
        foreach (var u in units)
        {
            if (!v.EndsWith(u, StringComparison.Ordinal)) continue;
            if (!double.TryParse(v.Substring(0, v.Length - u.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)) return null;
            return u switch
            {
                "pt" => n,
                "px" => n * 0.75,
                "em" or "rem" => n * emBase,
                "%" => n * emBase / 100,
                "cm" => n * 72 / 2.54,
                "mm" => n * 72 / 25.4,
                "in" => n * 72,
                "pc" => n * 12,
                _ => null,
            };
        }
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) && double.IsFinite(plain)) return plain * 0.75;
        return null;
    }
}
