using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Docs.Conversion.Internal;
using Chuvadi.Docs.Word;
using Chuvadi.Markup.Css;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Docs.Conversion;

/// <summary>
/// HTML to and from a <see cref="Document"/>.
/// <para><b>Import</b> parses the input like a browser (WHATWG) and passes it through Chuvadi.Markup's
/// allowlist sanitizer (<see cref="HtmlImportOptions.Policy"/>) before mapping it to the document:
/// scripts, event handlers, frames, forms and unsafe URLs never reach the model, and no file or
/// network access happens (pictures come from <c>data:</c> URIs or the host's
/// <see cref="ImportOptions.ImageResolver"/>).</para>
/// <para><b>Export</b> writes a complete page or a fragment; <see cref="ExportPreview(Document, CancellationToken)"/>
/// gives a self-contained preview (no script, no external reference) for mail clients such as Anjal.</para>
/// <code>
/// var doc = HtmlConversion.Import(html);
/// string page = HtmlConversion.Export(Document.Load("report.docx"));
/// string preview = HtmlConversion.ExportPreview(Document.Load(attachmentStream));
/// </code>
/// </summary>
public static class HtmlConversion
{
    // =====================================================================================
    // Import
    // =====================================================================================

    /// <summary>Builds a new document from HTML (a full page or a fragment).</summary>
    public static Document Import(string html, HtmlImportOptions? options = null, CancellationToken cancellationToken = default)
        => ImportDetailed(html, null, options, cancellationToken).Document;

    /// <summary>Builds a new document from HTML bytes; the encoding is sniffed as a browser does (BOM, meta charset, windows-1252) unless given.</summary>
    public static Document Import(byte[] html, Encoding? encoding = null, HtmlImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (html is null) throw new ArgumentNullException(nameof(html));
        var o = options ?? new HtmlImportOptions();
        var r = HtmlSanitizer.Sanitize(html, o.Policy, encoding, cancellationToken);
        return Build(r, null, o, cancellationToken).Document;
    }

    /// <summary>Imports HTML bytes (into <paramref name="target"/>, or a new document when null) and reports what was left out.</summary>
    public static ConversionResult ImportDetailed(byte[] html, Encoding? encoding = null, Document? target = null, HtmlImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (html is null) throw new ArgumentNullException(nameof(html));
        var o = options ?? new HtmlImportOptions();
        var r = HtmlSanitizer.Sanitize(html, o.Policy, encoding, cancellationToken);
        return Build(r, target, o, cancellationToken);
    }

    /// <summary>Reads HTML text from <paramref name="reader"/> into a new document.</summary>
    public static Document Import(TextReader reader, HtmlImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        var o = options ?? new HtmlImportOptions();
        int max = o.Policy.Limits.MaxInputChars;
        var sb = new StringBuilder();
        var buffer = new char[81920];
        int n;
        while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((long)sb.Length + n > max) throw new Chuvadi.Markup.MarkupLimitExceededException(nameof(Chuvadi.Markup.MarkupLimits.MaxInputChars), max, (long)sb.Length + n);
            sb.Append(buffer, 0, n);
        }
        return Import(sb.ToString(), o, cancellationToken);
    }

    /// <summary>Reads HTML bytes from <paramref name="stream"/> (encoding sniffed unless given) into a new document.</summary>
    public static Document Import(Stream stream, Encoding? encoding = null, HtmlImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var o = options ?? new HtmlImportOptions();
        // Up to four bytes per character: more input than that can never fit the character limit.
        long max = 4L * o.Policy.Limits.MaxInputChars;
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ms.Length + n > max) throw new Chuvadi.Markup.MarkupLimitExceededException(nameof(Chuvadi.Markup.MarkupLimits.MaxInputChars), max, ms.Length + n);
            ms.Write(buffer, 0, n);
        }
        return Import(ms.ToArray(), encoding, o, cancellationToken);
    }

    /// <summary>Appends HTML to the body of <paramref name="target"/>.</summary>
    public static void Import(string html, Document target, HtmlImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        ImportDetailed(html, target, options, cancellationToken);
    }

    /// <summary>Imports HTML (into <paramref name="target"/>, or a new document when null) and reports what was left out.</summary>
    public static ConversionResult ImportDetailed(string html, Document? target = null, HtmlImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (html is null) throw new ArgumentNullException(nameof(html));
        var o = options ?? new HtmlImportOptions();
        var r = HtmlSanitizer.Sanitize(html, o.Policy, cancellationToken);
        return Build(r, target, o, cancellationToken);
    }

    private static ConversionResult Build(SanitizeResult r, Document? target, HtmlImportOptions o, CancellationToken ct)
    {
        var doc = target ?? new Document();
        if (target is null && !string.IsNullOrWhiteSpace(r.Title)) doc.Properties.Title = r.Title.Trim();
        var b = new DocBuilder(doc, o, ct);
        foreach (var blocked in r.BlockedResources) b.Blocked.Add(blocked.Url);
        foreach (var w in r.Warnings) b.Warnings.Add(w);
        new HtmlImporter(b, o, ct).Nodes(r.Fragment);
        b.Finish();
        return new ConversionResult(doc, b.Warnings, b.Blocked);
    }

    internal sealed class HtmlImporter
    {
        private readonly DocBuilder _b;
        private readonly HtmlImportOptions _o;
        private readonly CancellationToken _ct;
        private int _depth;
        private int _tableDepth;

        public HtmlImporter(DocBuilder b, HtmlImportOptions o, CancellationToken ct) { _b = b; _o = o; _ct = ct; }

        public void Nodes(HtmlNode parent)
        {
            for (var n = parent.FirstChild; n is not null; n = n.NextSibling) Node(n);
        }

        private void Node(HtmlNode n)
        {
            _b.Tick();
            if (n is HtmlText t) { _b.Text(t.Data, collapse: true); return; }
            if (n is not HtmlElement e || e.Namespace != HtmlNamespace.Html) return;
            if (++_depth > 1000)
            {
                // Pathologically deep markup (only with raised parser limits): its text is kept, without structure, iteratively.
                _depth--;
                foreach (var d in e.Descendants()) if (d is HtmlText dt) _b.Text(dt.Data, collapse: true);
                return;
            }
            try { Element(e); }
            finally { _depth--; }
        }

        private void Element(HtmlElement e)
        {
            var name = e.LocalName;
            if (e.GetAttribute("id") is { Length: > 0 } id) _b.Bookmark(id);
            if (name == "a" && e.GetAttribute("name") is { Length: > 0 } an) _b.Bookmark(an);
            switch (name)
            {
                case "br": _b.LineBreak(); return;
                case "wbr": return;
                case "hr": _b.HorizontalRule(true); return;
                case "img": _b.Image(e.GetAttribute("src"), e.GetAttribute("alt")); return;
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    Block(e, "Heading" + name[1]);
                    return;
                case "p": case "div": case "section": case "article": case "header": case "footer": case "main":
                case "nav": case "aside": case "address": case "figure": case "figcaption": case "details": case "summary":
                case "center": case "fieldset": case "legend": case "hgroup": case "search": case "caption":
                    Block(e, null);
                    return;
                case "blockquote":
                    _b.BeginQuote();
                    WithBlockStyle(e, () => Nodes(e));
                    _b.EndQuote();
                    return;
                case "pre":
                {
                    _b.EndParagraph();
                    var saved = _b.NextStyleId;
                    _b.NextStyleId = _b.EnsureCodeStyle();
                    _b.StartParagraph();
                    _b.NextStyleId = saved;
                    _b.PreDepth++;
                    // A leading newline right after <pre> is not content (the parser already drops it).
                    Nodes(e);
                    _b.PreDepth--;
                    _b.EndParagraph();
                    return;
                }
                case "ul": case "ol": case "menu":
                {
                    int start = 1;
                    if (name == "ol" && int.TryParse(e.GetAttribute("start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var st)) start = st;
                    _b.BeginList(name == "ol", start);
                    for (var c = e.FirstChild; c is not null; c = c.NextSibling)
                    {
                        if (c is HtmlElement li && li.IsHtml("li"))
                        {
                            _b.BeginItem();
                            if (li.GetAttribute("id") is { Length: > 0 } lid) _b.Bookmark(lid);
                            WithInline(li, () => Nodes(li));
                            _b.EndItem();
                        }
                        else if (c is HtmlText tx && tx.Data.Trim().Length == 0) continue;
                        else
                        {
                            _b.BeginItem();
                            Node(c);
                            _b.EndItem();
                        }
                    }
                    _b.EndList();
                    return;
                }
                case "li":
                    // A stray li (outside a list) still becomes a list item.
                    _b.BeginItem();
                    Nodes(e);
                    _b.EndItem();
                    return;
                case "dl": Block(e, null); return;
                case "dt":
                    _b.EndParagraph();
                    _b.StartParagraph();
                    _b.PushInline(_b.State with { Bold = true });
                    Nodes(e);
                    _b.PopInline();
                    _b.EndParagraph();
                    return;
                case "dd":
                {
                    _b.EndParagraph();
                    double saved = _b.NextIndentPt;
                    _b.NextIndentPt = saved + 36;
                    Nodes(e);
                    _b.EndParagraph();
                    _b.NextIndentPt = saved;
                    return;
                }
                case "table": Table(e); return;
                case "q":
                    _b.Text("“", collapse: false);
                    WithInline(e, () => Nodes(e));
                    _b.Text("”", collapse: false);
                    return;
                case "a":
                {
                    var href = e.GetAttribute("href");
                    var s = InlineFor(e, _b.State);
                    if (!string.IsNullOrEmpty(href))
                    {
                        if (href.StartsWith('#')) s = s with { LinkUrl = null, LinkAnchor = href.Length > 1 ? Uri.UnescapeDataString(href.Substring(1)) : null };
                        else s = s with { LinkUrl = href, LinkAnchor = null };
                    }
                    _b.PushInline(s);
                    Nodes(e);
                    _b.PopInline();
                    return;
                }
                case "td": case "th": case "tr": case "thead": case "tbody": case "tfoot":
                    // Table parts outside a table (the sanitizer keeps the parser's tree, so rare).
                    Block(e, null);
                    return;
                default:
                    WithInline(e, () => Nodes(e));
                    return;
            }
        }

        private void Block(HtmlElement e, string? styleId)
        {
            _b.EndParagraph();
            var savedStyle = _b.NextStyleId;
            if (styleId is not null) _b.NextStyleId = styleId;
            WithBlockStyle(e, () =>
            {
                if (styleId is not null) { _b.StartParagraph(); _b.NextStyleId = savedStyle; }
                WithInline(e, () => Nodes(e));
            });
            _b.NextStyleId = savedStyle;
            _b.EndParagraph();
        }

        private void WithBlockStyle(HtmlElement e, Action body)
        {
            var savedAlign = _b.NextAlignment;
            var savedIndent = _b.NextIndentPt;
            var align = AlignOf(e);
            if (align is not null) _b.NextAlignment = align;
            if (e.LocalName == "center") _b.NextAlignment = ParagraphAlignment.Center;
            if (_o.ApplyInlineStyles && Declarations(e) is { } decls)
                foreach (var d in decls)
                    if (d.Name is "margin-left" or "padding-left" or "text-indent" && Internal.Css.Length(d.ValueText) is double pt && pt > 0 && pt < 400 && d.Name != "text-indent")
                        _b.NextIndentPt += pt;
            body();
            _b.EndParagraph();
            _b.NextAlignment = savedAlign;
            _b.NextIndentPt = savedIndent;
        }

        private ParagraphAlignment? AlignOf(HtmlElement e)
        {
            string? v = e.GetAttribute("align");
            if (_o.ApplyInlineStyles && Declarations(e) is { } decls)
                foreach (var d in decls) if (d.Name == "text-align") v = d.ValueText;
            return v?.Trim().ToLowerInvariant() switch
            {
                "center" => ParagraphAlignment.Center,
                "right" or "end" => ParagraphAlignment.Right,
                "justify" => ParagraphAlignment.Justify,
                "left" or "start" => ParagraphAlignment.Left,
                _ => null,
            };
        }

        private List<CssDeclaration>? Declarations(HtmlElement e)
        {
            var style = e.GetAttribute("style");
            if (string.IsNullOrWhiteSpace(style)) return null;
            try { return CssParser.ParseDeclarations(style, null, _ct); }
            catch (Chuvadi.Markup.MarkupLimitExceededException) { return null; }
        }

        private void WithInline(HtmlElement e, Action body)
        {
            var s = InlineFor(e, _b.State);
            if (s == _b.State) { body(); return; }
            _b.PushInline(s);
            body();
            _b.PopInline();
        }

        private InlineState InlineFor(HtmlElement e, InlineState s)
        {
            switch (e.LocalName)
            {
                case "b": case "strong": s = s with { Bold = true }; break;
                case "i": case "em": case "cite": case "dfn": case "var": s = s with { Italic = true }; break;
                case "u": case "ins": s = s with { Underline = true }; break;
                case "s": case "strike": case "del": s = s with { Strike = true }; break;
                case "sub": s = s with { Sub = true, Sup = false }; break;
                case "sup": s = s with { Sup = true, Sub = false }; break;
                case "code": case "kbd": case "samp": case "tt": s = s with { Code = true }; break;
                case "mark": s = s with { BackgroundHex = "FFFF00" }; break;
                case "small": s = s with { SizePt = 9 }; break;
                case "big": s = s with { SizePt = 14 }; break;
                case "th": s = s with { Bold = true }; break;
                case "font":
                    if (e.GetAttribute("color") is { } fc && Internal.Css.Color(fc) is { } hex) s = s with { ColorHex = hex };
                    if (e.GetAttribute("face") is { } face && FontName(face) is { } fn) s = s with { Font = fn };
                    if (int.TryParse(e.GetAttribute("size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fsz))
                        s = s with { SizePt = Math.Clamp(fsz, 1, 7) switch { 1 => 7.5, 2 => 10, 3 => 12, 4 => 13.5, 5 => 18, 6 => 24, _ => 36 } };
                    break;
            }
            if (!_o.ApplyInlineStyles || Declarations(e) is not { } decls) return s;
            foreach (var d in decls)
            {
                var v = d.ValueText;
                var lv = v.Trim().ToLowerInvariant();
                switch (d.Name)
                {
                    case "font-weight":
                        s = s with { Bold = lv is "bold" or "bolder" || (int.TryParse(lv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w >= 600) };
                        break;
                    case "font-style": s = s with { Italic = lv is "italic" or "oblique" }; break;
                    case "text-decoration":
                    case "text-decoration-line":
                        if (lv.Contains("underline", StringComparison.Ordinal)) s = s with { Underline = true };
                        if (lv.Contains("line-through", StringComparison.Ordinal)) s = s with { Strike = true };
                        if (lv == "none") s = s with { Underline = false, Strike = false };
                        break;
                    case "color": if (Internal.Css.Color(v) is { } c) s = s with { ColorHex = c }; break;
                    case "background-color":
                    case "background":
                        if (Internal.Css.Color(v) is { } bg && bg != "FFFFFF") s = s with { BackgroundHex = bg };
                        break;
                    case "font-size":
                        if (Internal.Css.Length(v) is double pt && pt >= 1 && pt <= 1638) s = s with { SizePt = Math.Round(pt * 2) / 2 };
                        break;
                    case "font-family": if (FontName(v) is { } f) s = s with { Font = f, Code = s.Code || IsMonospaceFamily(v) }; break;
                    case "vertical-align":
                        if (lv == "super") s = s with { Sup = true, Sub = false };
                        else if (lv == "sub") s = s with { Sub = true, Sup = false };
                        break;
                    case "font-variant": if (lv.Contains("small-caps", StringComparison.Ordinal)) s = s with { SmallCaps = true }; break;
                    case "text-transform": if (lv == "uppercase") s = s with { Caps = true }; break;
                }
            }
            return s;
        }

        private static bool IsMonospaceFamily(string v) => v.Contains("monospace", StringComparison.OrdinalIgnoreCase)
            || v.Contains("Courier", StringComparison.OrdinalIgnoreCase) || v.Contains("Consolas", StringComparison.OrdinalIgnoreCase);

        private static string? FontName(string value)
        {
            // The first family of a list, unquoted; generic families map to Word fonts.
            var first = value.Split(',')[0].Trim().Trim('"', '\'').Trim();
            if (first.Length == 0 || first.Length > 31) return null;
            foreach (char c in first) if (char.IsControl(c) || c is '<' or '>' or ';' or '\\') return null;
            return first.ToLowerInvariant() switch
            {
                "serif" => "Times New Roman",
                "sans-serif" or "system-ui" or "ui-sans-serif" => "Arial",
                "monospace" or "ui-monospace" => "Courier New",
                "cursive" or "fantasy" or "inherit" or "initial" or "unset" => null,
                _ => first,
            };
        }

        // ---- tables ----

        private sealed class GridCell
        {
            public HtmlElement Element = null!;
            public int Row, Column, ColSpan = 1, RowSpan = 1;
        }

        private void Table(HtmlElement table)
        {
            const int WordMaxColumns = 63;
            var rows = new List<(HtmlElement Tr, bool Header)>();
            HtmlElement? caption = null;
            for (var c = table.FirstChild; c is not null; c = c.NextSibling)
            {
                if (c is not HtmlElement ce) continue;
                if (ce.IsHtml("caption")) caption = ce;
                else if (ce.IsHtml("tr")) rows.Add((ce, false));
                else if (ce.IsHtml("thead") || ce.IsHtml("tbody") || ce.IsHtml("tfoot"))
                    for (var r = ce.FirstChild; r is not null; r = r.NextSibling)
                        if (r is HtmlElement re && re.IsHtml("tr")) rows.Add((re, ce.IsHtml("thead")));
            }
            if (caption is not null)
            {
                _b.EndParagraph();
                var saved = _b.NextAlignment;
                _b.NextAlignment = ParagraphAlignment.Center;
                _b.PushInline(_b.State with { Bold = true });
                Nodes(caption);
                _b.PopInline();
                _b.EndParagraph();
                _b.NextAlignment = saved;
            }
            if (rows.Count == 0) return;
            if (_tableDepth >= _o.MaxTableNesting)
            {
                // Too deep: the cells' content as paragraphs.
                foreach (var (tr, _) in rows)
                    for (var c = tr.FirstChild; c is not null; c = c.NextSibling)
                        if (c is HtmlElement cell) { _b.EndParagraph(); WithInline(cell, () => Nodes(cell)); _b.EndParagraph(); }
                return;
            }

            // Place cells on a grid (rowspan / colspan), as the HTML table model does.
            var occupied = new List<bool[]>();
            var cells = new List<GridCell>();
            int columns = 1;
            bool truncated = false;
            for (int r = 0; r < rows.Count; r++)
            {
                while (occupied.Count <= r) occupied.Add(new bool[WordMaxColumns]);
                int col = 0;
                for (var c = rows[r].Tr.FirstChild; c is not null; c = c.NextSibling)
                {
                    if (c is not HtmlElement td || !(td.IsHtml("td") || td.IsHtml("th"))) continue;
                    while (col < WordMaxColumns && occupied[r][col]) col++;
                    if (col >= WordMaxColumns) { truncated = true; break; }
                    int cs = Span(td.GetAttribute("colspan"), 1, 1000);
                    int rs = Span(td.GetAttribute("rowspan"), 1, 65534);
                    if (td.GetAttribute("rowspan") == "0") rs = rows.Count - r;
                    rs = Math.Min(rs, rows.Count - r);
                    cs = Math.Min(cs, WordMaxColumns - col);
                    var g = new GridCell { Element = td, Row = r, Column = col, ColSpan = cs, RowSpan = rs };
                    cells.Add(g);
                    for (int rr = r; rr < r + rs; rr++)
                    {
                        while (occupied.Count <= rr) occupied.Add(new bool[WordMaxColumns]);
                        for (int cc = col; cc < col + cs; cc++) occupied[rr][cc] = true;
                    }
                    col += cs;
                    columns = Math.Max(columns, col);
                }
            }
            if (truncated) _b.Warnings.Add("Table has more than 63 columns (Word's limit); the extra cells were left out.");

            // Rows without a cell of their own and outside any rowspan add nothing; they are skipped.
            var keep = new int[rows.Count];
            int kept = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                bool used = r < occupied.Count && Array.IndexOf(occupied[r], true) >= 0;
                keep[r] = used ? kept++ : -1;
            }
            if (kept == 0) return;

            // The item number comes before the table; a table goes in only if its first row fits the cell budget.
            _b.NumberOpenItem();
            if (!_b.ReserveCells(columns)) return;
            var context = _b.SuspendContext();
            _tableDepth++;
            var dt = _b.BeginTable(columns);
            var docRows = new List<TableRow?>();
            bool firstReserved = true;
            for (int r = 0; r < rows.Count; r++)
            {
                if (keep[r] < 0) { docRows.Add(null); continue; }
                if (!firstReserved && !_b.ReserveCells(columns)) { docRows.Add(null); continue; }
                firstReserved = false;
                var row = dt.AddRow();
                bool allTh = true, any = false;
                for (var c = rows[r].Tr.FirstChild; c is not null; c = c.NextSibling)
                    if (c is HtmlElement td && (td.IsHtml("td") || td.IsHtml("th"))) { any = true; if (!td.IsHtml("th")) allTh = false; }
                row.IsHeader = rows[r].Header || (any && allTh && r == 0);
                for (int i = 0; i < columns; i++) { row.Cell(i).Paragraphs.Clear(); row.Cell(i).Paragraphs.Add(new Paragraph()); }
                docRows.Add(row);
            }
            foreach (var g in cells)
            {
                if (docRows[g.Row] is not { } hostRow) continue;
                var cell = hostRow.Cell(g.Column);
                cell.ColumnSpan = g.ColSpan;
                if (g.RowSpan > 1)
                {
                    bool any = false;
                    for (int rr = g.Row + 1; rr < g.Row + g.RowSpan; rr++)
                    {
                        if (docRows[rr] is not { } contRow) continue;
                        var cont = contRow.Cell(g.Column);
                        cont.VerticalMerge = VerticalMerge.Continue;
                        cont.ColumnSpan = g.ColSpan;
                        any = true;
                    }
                    if (any) cell.VerticalMerge = VerticalMerge.Restart;
                }
                var bg = g.Element.GetAttribute("bgcolor") is { } bgc ? Internal.Css.Color(bgc) : null;
                if (_o.ApplyInlineStyles && Declarations(g.Element) is { } decls)
                    foreach (var d in decls)
                        if (d.Name is "background-color" or "background" && Internal.Css.Color(d.ValueText) is { } c) bg = c;
                if (bg is not null && bg != "FFFFFF") cell.ShadeHex = bg;
                var valign = g.Element.GetAttribute("valign")?.Trim().ToLowerInvariant();
                cell.VerticalAlignment = valign switch
                {
                    "middle" or "center" => CellVerticalAlignment.Center,
                    "bottom" => CellVerticalAlignment.Bottom,
                    "top" => CellVerticalAlignment.Top,
                    _ => cell.VerticalAlignment,
                };
                _b.BeginCell(cell);
                var savedAlign = _b.NextAlignment;
                _b.NextAlignment = AlignOf(g.Element) ?? AlignOf(rows[g.Row].Tr);
                WithInline(g.Element, () => Nodes(g.Element));
                _b.NextAlignment = savedAlign;
                _b.EndCell();
            }
            _tableDepth--;
            _b.ResumeContext(context);
        }

        private static int Span(string? v, int min, int max)
            => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, min, max) : min;
    }

    // =====================================================================================
    // Export
    // =====================================================================================

    /// <summary>The document as HTML (a complete page by default, see <see cref="HtmlExportOptions.FullDocument"/>).</summary>
    public static string Export(Document document, HtmlExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var o = options ?? new HtmlExportOptions();
        var x = ExportWalker.Walk(document, o.Walk(cancellationToken));
        return new HtmlWriter(o, cancellationToken).Write(x);
    }

    /// <summary>Writes the document as HTML to <paramref name="writer"/>.</summary>
    public static void Export(Document document, TextWriter writer, HtmlExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.Write(Export(document, options, cancellationToken));
    }

    /// <summary>A self-contained, script-free HTML fragment for previews (see <see cref="HtmlExportOptions.Preview"/>).</summary>
    public static string ExportPreview(Document document, CancellationToken cancellationToken = default)
        => Export(document, HtmlExportOptions.Preview(), cancellationToken);

    private sealed class HtmlWriter
    {
        private static readonly HashSet<string> s_wrappers = new(StringComparer.Ordinal) { "div", "section", "article", "main", "aside", "header", "footer", "span" };

        private readonly HtmlExportOptions _o;
        private readonly CancellationToken _ct;
        private readonly StringBuilder _sb = new();
        private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _idSuffix = new(StringComparer.Ordinal);
        private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);
        private readonly bool _inline;
        private int _images;
        private int _steps;

        public HtmlWriter(HtmlExportOptions o, CancellationToken ct)
        {
            _o = o;
            _ct = ct;
            _inline = o.Formatting == HtmlFormatting.Inline;
        }

        private string NL => _o.NewLine;

        public string Write(XDocument x)
        {
            var title = _o.Title ?? x.Title;
            var lang = _o.Language ?? x.Language;
            if (_o.FullDocument)
            {
                _sb.Append("<!DOCTYPE html>").Append(NL);
                _sb.Append("<html");
                if (!string.IsNullOrWhiteSpace(lang) && IsLangTag(lang)) _sb.Append(" lang=\"").Append(Attr(lang)).Append('"');
                _sb.Append('>').Append(NL).Append("<head>").Append(NL);
                _sb.Append("<meta charset=\"utf-8\">").Append(NL);
                _sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">").Append(NL);
                _sb.Append("<title>").Append(Text(title ?? "")).Append("</title>").Append(NL);
                if (_o.IncludeStyleSheet) _sb.Append("<style>").Append(NL).Append(StyleSheet).Append("</style>").Append(NL);
                _sb.Append("</head>").Append(NL).Append("<body>").Append(NL);
            }
            bool wrap = _o.WrapperElement is { } we && s_wrappers.Contains(we);
            if (wrap)
            {
                _sb.Append('<').Append(_o.WrapperElement);
                if (IsClassList(_o.WrapperClass)) _sb.Append(" class=\"").Append(Attr(_o.WrapperClass)).Append('"');
                if (!_o.FullDocument && !string.IsNullOrWhiteSpace(lang) && IsLangTag(lang)) _sb.Append(" lang=\"").Append(Attr(lang)).Append('"');
                _sb.Append('>').Append(NL);
            }

            if (x.Headers.Count > 0) { _sb.Append("<header>").Append(NL); Blocks(x.Headers, null); _sb.Append("</header>").Append(NL); }
            Blocks(x.Body, _o.MaxBlocks);
            if (x.Footers.Count > 0) { _sb.Append("<footer>").Append(NL); Blocks(x.Footers, null); _sb.Append("</footer>").Append(NL); }
            if (x.Footnotes.Count + x.Endnotes.Count > 0)
            {
                _sb.Append("<section class=\"notes\">").Append(NL).Append("<hr>").Append(NL);
                foreach (var n in x.Footnotes) Note(n);
                foreach (var n in x.Endnotes) Note(n);
                _sb.Append("</section>").Append(NL);
            }

            if (wrap) _sb.Append("</").Append(_o.WrapperElement).Append('>').Append(NL);
            if (_o.FullDocument) _sb.Append("</body>").Append(NL).Append("</html>").Append(NL);
            return _sb.ToString();
        }

        private const string StyleSheet =
            "body { font-family: Calibri, Arial, sans-serif; line-height: 1.4; max-width: 50em; margin: 2em auto; padding: 0 1em; }\n" +
            "table { border-collapse: collapse; margin: 0.5em 0; }\n" +
            "td, th { border: 1px solid #bbb; padding: 2pt 5pt; vertical-align: top; }\n" +
            "th { background: #f2f2f2; }\n" +
            "pre { background: #f4f4f4; padding: 0.5em; overflow: auto; }\n" +
            "code { font-family: Consolas, 'Courier New', monospace; }\n" +
            "blockquote { margin-left: 2em; color: #444; }\n" +
            ".notes { font-size: 0.9em; }\n";

        private void Tick()
        {
            if ((++_steps & 1023) == 0) _ct.ThrowIfCancellationRequested();
        }

        private string NoteId(XNote n) => _o.IdPrefix + (n.Kind == NoteKind.Footnote ? "fn-" : "en-") + n.Index.ToString(CultureInfo.InvariantCulture);
        private string NoteRefId(XNote n) => _o.IdPrefix + (n.Kind == NoteKind.Footnote ? "fnref-" : "enref-") + n.Index.ToString(CultureInfo.InvariantCulture);

        private void Note(XNote n)
        {
            _sb.Append("<div id=\"").Append(Attr(NoteId(n))).Append("\">").Append(NL);
            bool first = true;
            foreach (var b in n.Blocks)
            {
                if (b is XParagraph p)
                {
                    _sb.Append("<p>");
                    if (first) _sb.Append("<a href=\"#").Append(Attr(NoteRefId(n))).Append("\">").Append(Text(n.Label)).Append("</a> ");
                    Inlines(p.Inlines);
                    _sb.Append("</p>").Append(NL);
                }
                else if (b is XTable t) Table(t);
                first = false;
            }
            _sb.Append("</div>").Append(NL);
        }

        private void Blocks(List<XBlock> blocks, int? max)
        {
            int i = 0, written = 0;
            while (i < blocks.Count)
            {
                Tick();
                if (max is int m && written >= m)
                {
                    _sb.Append("<p>…</p>").Append(NL);
                    return;
                }
                var b = blocks[i];
                if (b is XParagraph { List: not null } && _o.Lists == HtmlListRendering.Semantic)
                {
                    int j = i;
                    while (j < blocks.Count && blocks[j] is XParagraph { List: not null }) j++;
                    List(blocks, i, j);
                    written += j - i;
                    i = j;
                    continue;
                }
                if (b is XParagraph { Kind: XParagraphKind.Quote })
                {
                    _sb.Append("<blockquote>").Append(NL);
                    while (i < blocks.Count && blocks[i] is XParagraph { Kind: XParagraphKind.Quote } qp) { Paragraph(qp); i++; written++; }
                    _sb.Append("</blockquote>").Append(NL);
                    continue;
                }
                if (b is XParagraph { Kind: XParagraphKind.Code })
                {
                    _sb.Append("<pre");
                    if (b is XParagraph { BookmarkId: { } preId }) _sb.Append(" id=\"").Append(Attr(Id(preId))).Append('"');
                    _sb.Append("><code>");
                    bool firstLine = true;
                    while (i < blocks.Count && blocks[i] is XParagraph { Kind: XParagraphKind.Code } cp)
                    {
                        if (!firstLine) _sb.Append('\n');
                        CodeText(cp.Inlines);
                        firstLine = false;
                        i++;
                        written++;
                    }
                    _sb.Append("</code></pre>").Append(NL);
                    continue;
                }
                if (b is XParagraph p) { Paragraph(p); written++; }
                else if (b is XTable t)
                {
                    // A table costs its rows, and stops at the remaining budget.
                    int? left = max is int mm ? Math.Max(1, mm - written) : null;
                    Table(t, left);
                    written += Math.Max(1, left is int l2 ? Math.Min(l2, t.Rows.Count) : t.Rows.Count);
                }
                i++;
            }
        }

        private void CodeText(List<XInline> inlines)
        {
            foreach (var i in inlines)
            {
                if (i is XText t) _sb.Append(Text(t.Text));
                else if (i is XTab) _sb.Append('\t');
                else if (i is XBreak) _sb.Append('\n');
                else if (i is XLink l) CodeText(l.Inlines);
                else if (i is XNoteRef nr) NoteRef(nr);
                else if (i is XImage img && !string.IsNullOrEmpty(img.Image.AltText)) _sb.Append('[').Append(Text(img.Image.AltText)).Append(']');
            }
        }

        private void List(List<XBlock> blocks, int from, int to)
        {
            // Open lists by level; each holds an open li until the next item at its level.
            var stack = new List<(bool Ordered, int Level, ListInstance? Source, int Number)>();
            for (int i = from; i < to; i++)
            {
                var p = (XParagraph)blocks[i];
                var li = p.List!;
                while (stack.Count > 0 && stack[^1].Level > li.Level) CloseList(stack);
                // Another list, or a restart of the numbering, at the same level is a new list.
                if (stack.Count > 0 && stack[^1].Level == li.Level
                    && (stack[^1].Ordered != li.Ordered || !ReferenceEquals(stack[^1].Source, li.List) || (li.Ordered && li.Number <= stack[^1].Number)))
                    CloseList(stack);
                if (stack.Count > 0 && stack[^1].Level == li.Level) _sb.Append("</li>").Append(NL);
                if (stack.Count == 0 || stack[^1].Level < li.Level)
                {
                    _sb.Append(li.Ordered ? "<ol" : "<ul");
                    if (li.Ordered && li.Number != 1) _sb.Append(" start=\"").Append(li.Number.ToString(CultureInfo.InvariantCulture)).Append('"');
                    if (li.Ordered && ListType(li.Format) is { } type) _sb.Append(" type=\"").Append(type).Append('"');
                    _sb.Append('>').Append(NL);
                    stack.Add((li.Ordered, li.Level, li.List, li.Number));
                }
                else stack[^1] = (stack[^1].Ordered, stack[^1].Level, stack[^1].Source, li.Number);
                _sb.Append("<li");
                if (p.BookmarkId is { } bid) _sb.Append(" id=\"").Append(Attr(Id(bid))).Append('"');
                _sb.Append('>');
                ExtraAnchors(p);
                Inlines(p.Inlines);
            }
            while (stack.Count > 0) CloseList(stack);

            void CloseList(List<(bool Ordered, int Level, ListInstance? Source, int Number)> s)
            {
                _sb.Append("</li>").Append(NL).Append(s[^1].Ordered ? "</ol>" : "</ul>").Append(NL);
                s.RemoveAt(s.Count - 1);
            }
        }

        private static string? ListType(NumberFormat format) => format switch
        {
            NumberFormat.LowerLetter => "a",
            NumberFormat.UpperLetter => "A",
            NumberFormat.LowerRoman => "i",
            NumberFormat.UpperRoman => "I",
            _ => null,
        };

        private void Paragraph(XParagraph p)
        {
            string tag = p.Kind == XParagraphKind.Heading ? "h" + Math.Clamp(p.HeadingLevel, 1, 6).ToString(CultureInfo.InvariantCulture) : "p";
            _sb.Append('<').Append(tag);
            if (p.BookmarkId is { } bid) _sb.Append(" id=\"").Append(Attr(Id(bid))).Append('"');
            if (_inline)
            {
                var style = new StringBuilder();
                if (p.Alignment is { } al && al != ParagraphAlignment.Left)
                    style.Append("text-align:").Append(al switch
                    {
                        ParagraphAlignment.Center => "center",
                        ParagraphAlignment.Right => "right",
                        ParagraphAlignment.Justify => "justify",
                        _ => "left",
                    }).Append(';');
                if (p.IndentLeftPt > 0 && p.List is null && p.Kind != XParagraphKind.Quote)
                    style.Append("margin-left:").Append(Num(p.IndentLeftPt)).Append("pt;");
                if (p.PageBreakBefore) style.Append("break-before:page;");
                if (style.Length > 0) _sb.Append(" style=\"").Append(Attr(style.ToString().TrimEnd(';'))).Append('"');
            }
            _sb.Append('>');
            ExtraAnchors(p);
            if (p.List is { } li && _o.Lists == HtmlListRendering.Labels && li.Label.Length > 0)
                _sb.Append(Text(li.Label)).Append(' ');
            if (p.IsEmpty) _sb.Append("<br>");
            else Inlines(p.Inlines);
            _sb.Append("</").Append(tag).Append('>').Append(NL);
        }

        private void Table(XTable t, int? maxRows = null)
        {
            _sb.Append("<table");
            if (_inline) _sb.Append(" style=\"border-collapse:collapse\"");
            _sb.Append('>').Append(NL);
            int rowCount = 0;
            foreach (var r in t.Rows)
            {
                if (maxRows is int mr && rowCount++ >= mr) break;
                _sb.Append("<tr>");
                foreach (var c in r.Cells)
                {
                    if (c.Covered) continue;
                    var tag = r.IsHeader ? "th" : "td";
                    _sb.Append('<').Append(tag);
                    if (c.ColumnSpan > 1) _sb.Append(" colspan=\"").Append(c.ColumnSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
                    if (c.RowSpan > 1) _sb.Append(" rowspan=\"").Append(c.RowSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
                    var single = c.Blocks.Count == 1 && c.Blocks[0] is XParagraph { Kind: XParagraphKind.Normal, List: null } sp ? sp : null;
                    if (single?.BookmarkId is { } cid) _sb.Append(" id=\"").Append(Attr(Id(cid))).Append('"');
                    if (_inline) _sb.Append(" style=\"border:1px solid #bbbbbb;padding:2pt 5pt;vertical-align:top\"");
                    _sb.Append('>');
                    // A cell with one plain paragraph is written without a p element.
                    if (single is not null)
                    {
                        ExtraAnchors(single);
                        if (!single.IsEmpty) Inlines(single.Inlines);
                    }
                    else Blocks(c.Blocks, null);
                    _sb.Append("</").Append(tag).Append('>');
                }
                _sb.Append("</tr>").Append(NL);
            }
            _sb.Append("</table>").Append(NL);
        }

        private void Inlines(List<XInline> inlines)
        {
            foreach (var i in inlines)
            {
                Tick();
                switch (i)
                {
                    case XText t: Styled(t.Text, t.Style); break;
                    case XTab tab:
                        if (_inline) _sb.Append("<span style=\"white-space:pre\">\t</span>");
                        else _sb.Append(' ');
                        break;
                    case XBreak br:
                        if (br.Type == BreakType.Page && _inline) _sb.Append("<br style=\"break-after:page\">");
                        else _sb.Append("<br>");
                        break;
                    case XLink l: Link(l); break;
                    case XImage img: Image(img); break;
                    case XNoteRef nr: NoteRef(nr); break;
                }
            }
        }

        private void NoteRef(XNoteRef nr)
        {
            _sb.Append("<sup><a id=\"").Append(Attr(NoteRefId(nr.Note))).Append("\" href=\"#").Append(Attr(NoteId(nr.Note))).Append("\">")
               .Append(Text(nr.Note.Label)).Append("</a></sup>");
        }

        /// <summary>
        /// The HTML id for a bookmark: only safe characters, unique (two names that sanitize alike get
        /// a suffix), and never one of the generated note ids.
        /// </summary>
        private string Id(string bookmark)
        {
            if (_ids.TryGetValue(bookmark, out var id)) return id;
            // HTML5 ids may hold any letters: Unicode letters and digits are kept, everything else becomes '_'.
            var sb = new StringBuilder();
            foreach (char c in bookmark) sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
            var core = sb.Length == 0 ? "bm" : sb.ToString();
            if (core.StartsWith("fn-", StringComparison.Ordinal) || core.StartsWith("en-", StringComparison.Ordinal)
                || core.StartsWith("fnref-", StringComparison.Ordinal) || core.StartsWith("enref-", StringComparison.Ordinal))
                core = "bm-" + core;
            var candidate = _o.IdPrefix + core;
            if (!_usedIds.Add(candidate))
            {
                int n = _idSuffix.TryGetValue(core, out var next) ? next : 2;
                do candidate = _o.IdPrefix + core + "-" + (n++).ToString(CultureInfo.InvariantCulture);
                while (!_usedIds.Add(candidate));
                _idSuffix[core] = n;
            }
            _ids[bookmark] = candidate;
            return candidate;
        }

        private void ExtraAnchors(XParagraph p)
        {
            if (p.MoreBookmarks is null) return;
            foreach (var b in p.MoreBookmarks) _sb.Append("<span id=\"").Append(Attr(Id(b))).Append("\"></span>");
        }

        private void Link(XLink l)
        {
            string? href = null;
            if (l.Url is { } url && Urls.IsSafeExportLink(url)) href = url + (l.Anchor is { } a1 ? "#" + Uri.EscapeDataString(a1) : "");
            else if (l.Url is null && l.Anchor is { } a2) href = "#" + Id(a2);
            if (href is null) { Inlines(l.Inlines); return; }
            _sb.Append("<a href=\"").Append(Attr(href)).Append('"');
            if (!string.IsNullOrEmpty(l.Title)) _sb.Append(" title=\"").Append(Attr(l.Title)).Append('"');
            _sb.Append('>');
            Inlines(l.Inlines);
            _sb.Append("</a>");
        }

        private void Image(XImage img)
        {
            _images++;
            var spec = img.Image;
            string? src = _o.ImageUrlResolver?.Invoke(new ExportedImage(spec, _images));
            if (src is not null && !Urls.IsSafeExportLink(src) && !src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) src = null;
            bool tooBig = _o.MaxImageBytes is long max && spec.Bytes.LongLength > max;
            if (src is null && _o.Images == ImageExportMode.DataUri && spec.Bytes.Length > 0 && !tooBig && Urls.BrowserImageTypes.Contains(spec.ContentType))
                src = DataUri.Encode(spec.Bytes, spec.ContentType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : spec.ContentType);
            if (src is null)
            {
                if (!string.IsNullOrEmpty(spec.AltText)) _sb.Append("<span>[").Append(Text(spec.AltText)).Append("]</span>");
                return;
            }
            _sb.Append("<img src=\"").Append(Attr(src)).Append("\" alt=\"").Append(Attr(spec.AltText ?? "")).Append('"');
            if (spec.WidthPt > 0 && spec.HeightPt > 0)
            {
                int w = (int)Math.Clamp(Math.Round(spec.WidthPt * 4 / 3), 1, 20000), h = (int)Math.Clamp(Math.Round(spec.HeightPt * 4 / 3), 1, 20000);
                _sb.Append(" width=\"").Append(w.ToString(CultureInfo.InvariantCulture)).Append("\" height=\"").Append(h.ToString(CultureInfo.InvariantCulture)).Append('"');
                if (_inline) _sb.Append(" style=\"max-width:100%;height:auto\"");
            }
            _sb.Append('>');
        }

        private void Styled(string text, XStyle s)
        {
            var open = new List<string>();
            if (s.Deleted) open.Add("del");
            if (s.Inserted) open.Add("ins");
            if (s.Bold) open.Add("strong");
            if (s.Italic) open.Add("em");
            if (s.Underline) open.Add("u");
            if (s.Strike) open.Add("s");
            if (s.Sub) open.Add("sub");
            if (s.Sup) open.Add("sup");
            if (s.Code) open.Add("code");
            string? css = null;
            if (_inline)
            {
                var st = new StringBuilder();
                if (IsHex(s.ColorHex)) st.Append("color:#").Append(s.ColorHex).Append(';');
                if (IsHex(s.BackgroundHex)) st.Append("background-color:#").Append(s.BackgroundHex).Append(';');
                if (s.SizePt > 0 && s.SizePt < 1639) st.Append("font-size:").Append(Num(s.SizePt)).Append("pt;");
                if (s.Font is { } f && SafeFont(f)) st.Append("font-family:'").Append(f).Append("';");
                if (s.SmallCaps) st.Append("font-variant:small-caps;");
                if (s.Caps) st.Append("text-transform:uppercase;");
                if (st.Length > 0) css = st.ToString().TrimEnd(';');
            }
            foreach (var t in open) _sb.Append('<').Append(t).Append('>');
            if (css is not null) _sb.Append("<span style=\"").Append(Attr(css)).Append("\">");
            _sb.Append(Text(text));
            if (css is not null) _sb.Append("</span>");
            for (int i = open.Count - 1; i >= 0; i--) _sb.Append("</").Append(open[i]).Append('>');
        }

        private static bool IsHex(string? h)
        {
            if (h is null || h.Length != 6) return false;
            foreach (char c in h) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        private static bool SafeFont(string f)
        {
            if (f.Length == 0 || f.Length > 64) return false;
            foreach (char c in f) if (!(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.')) return false;
            return true;
        }

        private static bool IsClassList(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (char c in s) if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ' ')) return false;
            return true;
        }

        private static bool IsLangTag(string s)
        {
            if (s.Length > 35) return false;
            foreach (char c in s) if (!(char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
            return true;
        }

        private static string Num(double d) => Math.Round(d, 2).ToString("0.##", CultureInfo.InvariantCulture);

        private static string Text(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '\u00a0': sb.Append("&nbsp;"); break;
                    default:
                        if (c < 0x20 && c is not ('\t' or '\n' or '\r')) break;
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string Attr(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default:
                        if (c < 0x20) break;
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
