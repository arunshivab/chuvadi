using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Docs.Conversion.Internal;
using Chuvadi.Docs.Word;
using Chuvadi.Markup.Html;
using Md = Chuvadi.Markup.Markdown;

namespace Chuvadi.Docs.Conversion;

/// <summary>
/// Markdown (CommonMark with GFM tables, task lists, strikethrough and autolinks) to and from a
/// <see cref="Document"/>. Parsing and writing use Chuvadi.Markup, so the Markdown is read exactly
/// as cmark-gfm reads it, and what is written parses back to the same structure.
/// <code>
/// var doc = MarkdownConversion.Import(File.ReadAllText("README.md"));
/// doc.SaveTo("README.docx");
/// string md = MarkdownConversion.Export(Document.Load("report.docx"));
/// </code>
/// Mapping: headings ↔ Heading 1–6, paragraphs, block quotes ↔ Quote, fenced and indented code ↔
/// the Code style, bullet and ordered lists (nested, start numbers) ↔ Word lists, task items ↔
/// ☐ / ☒ markers, tables ↔ tables (header row, column alignment), emphasis, strong, strikethrough,
/// inline code, links and autolinks ↔ hyperlinks, pictures ↔ pictures (data URIs, or through the
/// resolver), hard breaks ↔ line breaks, thematic breaks ↔ a bottom-bordered paragraph. Raw HTML is
/// sanitized and converted (or kept as text / dropped, see <see cref="MarkdownImportOptions.RawHtml"/>).
/// </summary>
public static class MarkdownConversion
{
    /// <summary>Builds a new document from Markdown.</summary>
    public static Document Import(string markdown, MarkdownImportOptions? options = null, CancellationToken cancellationToken = default)
        => ImportDetailed(markdown, null, options, cancellationToken).Document;

    /// <summary>Appends Markdown to the body of <paramref name="target"/>.</summary>
    public static void Import(string markdown, Document target, MarkdownImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        ImportDetailed(markdown, target, options, cancellationToken);
    }

    /// <summary>Imports Markdown (into <paramref name="target"/>, or a new document when null) and reports what could not be brought across.</summary>
    public static ConversionResult ImportDetailed(string markdown, Document? target = null, MarkdownImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (markdown is null) throw new ArgumentNullException(nameof(markdown));
        var o = options ?? new MarkdownImportOptions();
        var ast = Md.MarkdownParser.Parse(markdown, o.Parser, cancellationToken);
        var doc = target ?? new Document();
        var b = new DocBuilder(doc, o, cancellationToken);
        new MdImporter(b, o, cancellationToken).Blocks(ast);
        b.Finish();
        return new ConversionResult(doc, b.Warnings, b.Blocked);
    }

    /// <summary>Reads Markdown from <paramref name="reader"/> into a new document.</summary>
    public static Document Import(TextReader reader, MarkdownImportOptions? options = null, CancellationToken cancellationToken = default)
        => ImportDetailed(reader, null, options, cancellationToken).Document;

    /// <summary>Reads Markdown from <paramref name="reader"/> (into <paramref name="target"/>, or a new document when null) and reports what could not be brought across.</summary>
    public static ConversionResult ImportDetailed(TextReader reader, Document? target = null, MarkdownImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        var o = options ?? new MarkdownImportOptions();
        var ast = Md.MarkdownParser.Parse(reader, o.Parser, cancellationToken);
        var doc = target ?? new Document();
        var b = new DocBuilder(doc, o, cancellationToken);
        new MdImporter(b, o, cancellationToken).Blocks(ast);
        b.Finish();
        return new ConversionResult(doc, b.Warnings, b.Blocked);
    }

    private sealed class MdImporter
    {
        private readonly DocBuilder _b;
        private readonly MarkdownImportOptions _o;
        private readonly CancellationToken _ct;
        private readonly List<string> _dropping = new();     // open inline elements whose content is dropped (script, style…)
        private readonly List<(string Tag, int Depth)> _rawOpen = new(); // formatting opened by raw inline tags

        private static readonly HashSet<string> s_void = new(StringComparer.Ordinal)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "keygen", "link", "meta", "param", "source", "track", "wbr", "frame",
        };

        public MdImporter(DocBuilder b, MarkdownImportOptions o, CancellationToken ct) { _b = b; _o = o; _ct = ct; }

        public void Blocks(Md.MarkdownNode container)
        {
            foreach (var n in container.Children) Block(n);
        }

        private void Block(Md.MarkdownNode n)
        {
            _b.Tick();
            switch (n)
            {
                case Md.Paragraph p:
                    _b.EndParagraph();
                    _b.StartParagraph();
                    Inlines(p);
                    _b.EndParagraph();
                    break;
                case Md.Heading h:
                    _b.EndParagraph();
                    _b.NextStyleId = "Heading" + h.Level.ToString(CultureInfo.InvariantCulture);
                    _b.StartParagraph();
                    _b.NextStyleId = null;
                    Inlines(h);
                    _b.EndParagraph();
                    break;
                case Md.BlockQuote q:
                    _b.BeginQuote();
                    Blocks(q);
                    _b.EndQuote();
                    break;
                case Md.ListBlock l:
                    _b.BeginList(l.ListKind == Md.ListKind.Ordered, l.Start);
                    foreach (var item in l.Children)
                    {
                        if (item is not Md.ListItem li) continue;
                        string? marker = li.TaskState switch
                        {
                            Md.TaskState.Checked => _o.TaskCheckedMarker,
                            Md.TaskState.Unchecked => _o.TaskUncheckedMarker,
                            _ => null,
                        };
                        _b.BeginItem(marker);
                        Blocks(li);
                        _b.EndItem();
                    }
                    _b.EndList();
                    break;
                case Md.CodeBlock c:
                {
                    _b.EndParagraph();
                    _b.NextStyleId = _b.EnsureCodeStyle();
                    _b.StartParagraph();
                    _b.NextStyleId = null;
                    var code = c.Code;
                    if (code.EndsWith('\n')) code = code.Substring(0, code.Length - 1);
                    _b.PreDepth++;
                    _b.Text(code, collapse: false);
                    _b.PreDepth--;
                    _b.EndParagraph();
                    break;
                }
                case Md.HtmlBlock hb:
                    RawHtmlBlock(hb.Html);
                    break;
                case Md.ThematicBreak:
                    _b.HorizontalRule(_o.ThematicBreakAsBorder);
                    break;
                case Md.Table t:
                    Table(t);
                    break;
                default:
                    if (n.CanHaveChildren) Blocks(n);
                    break;
            }
        }

        private void Table(Md.Table t)
        {
            int cols = Math.Max(1, t.Alignments.Count);
            foreach (var r in t.Children) cols = Math.Max(cols, r.Children.Count);
            if (cols > DocBuilder.MaxColumns)
            {
                _b.Warnings.Add($"Table has more than {DocBuilder.MaxColumns} columns (Word's limit); the extra cells were left out.");
                cols = DocBuilder.MaxColumns;
            }
            // The item number comes before the table; a table goes in only if its first row fits the cell budget.
            _b.NumberOpenItem();
            if (!_b.ReserveCells(cols)) return;
            var saved = _b.SuspendContext();
            var table = _b.BeginTable(cols);
            bool firstReserved = true;
            foreach (var rn in t.Children)
            {
                if (rn is not Md.TableRow r) continue;
                if (!firstReserved && !_b.ReserveCells(cols)) break;
                firstReserved = false;
                var row = table.AddRow();
                row.IsHeader = r.IsHeader;
                for (int i = 0; i < cols; i++)
                {
                    var cell = row.Cell(i);
                    _b.BeginCell(cell);
                    var align = i < t.Alignments.Count ? t.Alignments[i] : Md.TableAlignment.None;
                    _b.NextAlignment = align switch
                    {
                        Md.TableAlignment.Center => ParagraphAlignment.Center,
                        Md.TableAlignment.Right => ParagraphAlignment.Right,
                        Md.TableAlignment.Left => ParagraphAlignment.Left,
                        _ => null,
                    };
                    _b.StartParagraph();
                    if (i < r.Children.Count)
                    {
                        int depth = _b.InlineDepth;
                        if (r.IsHeader) _b.PushInline(_b.State with { Bold = true });
                        Inlines(r.Children[i]);
                        _b.RestoreInline(depth);
                    }
                    _b.NextAlignment = null;
                    _b.EndCell();
                }
            }
            _b.ResumeContext(saved);
        }

        private void Inlines(Md.MarkdownNode parent)
        {
            int depthAtStart = _b.InlineDepth;
            foreach (var n in parent.Children) Inline(n);
            // Raw HTML state never outlives its paragraph (an unclosed <b> or <script> ends here).
            _b.RestoreInline(depthAtStart);
            _dropping.Clear();
            _rawOpen.Clear();
        }

        private void Inline(Md.MarkdownNode n)
        {
            _b.Tick();
            var s = _b.State;
            switch (n)
            {
                case Md.TextInline t: if (_dropping.Count == 0) _b.Text(t.Text, collapse: false); break;
                case Md.CodeSpan c:
                    _b.PushInline(s with { Code = true });
                    _b.Text(c.Code, collapse: false);
                    _b.PopInline();
                    break;
                case Md.Emphasis: Wrapped(n, s with { Italic = true }); break;
                case Md.Strong: Wrapped(n, s with { Bold = true }); break;
                case Md.Strikethrough: Wrapped(n, s with { Strike = true }); break;
                case Md.Link l:
                {
                    var dest = l.Destination;
                    if (dest.StartsWith('#') && dest.Length > 1) Wrapped(n, s with { LinkUrl = null, LinkAnchor = Uri.UnescapeDataString(dest.Substring(1)) });
                    else if (SafeLink(dest, out var url)) Wrapped(n, s with { LinkUrl = url, LinkAnchor = null });
                    else { _b.Blocked.Add(dest); Wrapped(n, s); }
                    break;
                }
                case Md.Image img:
                    _b.Image(img.Destination, img.ToPlainText());
                    break;
                case Md.HardBreak: _b.LineBreak(); break;
                case Md.SoftBreak:
                    if (_o.SoftBreaks == SoftBreakImport.LineBreak) _b.LineBreak();
                    else _b.Text(" ", collapse: false);
                    break;
                case Md.HtmlInline h: RawHtmlInline(h.Html); break;
                default:
                    if (n.CanHaveChildren) foreach (var c in n.Children) Inline(c);
                    break;
            }
        }

        private void Wrapped(Md.MarkdownNode n, InlineState state)
        {
            // Raw tags opened inside (an unclosed <b> in a link text) end with the Markdown construct.
            int depth = _b.InlineDepth;
            int raw = _rawOpen.Count;
            _b.PushInline(state);
            foreach (var c in n.Children) Inline(c);
            _b.RestoreInline(depth);
            if (_rawOpen.Count > raw) _rawOpen.RemoveRange(raw, _rawOpen.Count - raw);
        }

        private bool SafeLink(string dest, out string url)
        {
            url = dest.Trim();
            return Urls.IsSafeLink(url, _o.Html.Policy.AllowedLinkSchemes, _o.Html.Policy.AllowRelativeLinks);
        }

        // ---- raw HTML ----

        private void RawHtmlBlock(string html)
        {
            switch (_o.RawHtml)
            {
                case RawHtmlImport.Drop: return;
                case RawHtmlImport.Text:
                    _b.EndParagraph();
                    _b.StartParagraph();
                    _b.PreDepth++;
                    _b.Text(html.TrimEnd('\n'), collapse: false);
                    _b.PreDepth--;
                    _b.EndParagraph();
                    return;
            }
            _b.EndParagraph();
            var r = Markup.Sanitization.HtmlSanitizer.Sanitize(html, _o.Html.Policy, _ct);
            foreach (var blocked in r.BlockedResources) _b.Blocked.Add(blocked.Url);
            new HtmlConversion.HtmlImporter(_b, _o.Html, _ct).Nodes(r.Fragment);
            _b.EndParagraph();
        }

        // Inline raw HTML arrives tag by tag (<b>, text, </b>): a small state machine over the
        // formatting tags; anything else is dropped (its text, which is Markdown, still comes through).
        private void RawHtmlInline(string html)
        {
            if (_o.RawHtml == RawHtmlImport.Drop) return;
            if (_o.RawHtml == RawHtmlImport.Text) { _b.Text(html, collapse: false); return; }
            var tag = ParseTag(html, out bool closing, out bool selfClosing);
            if (tag is null) return; // a comment, declaration or processing instruction
            var policy = _o.Html.Policy;
            if (policy.DroppedElements.Contains(tag))
            {
                // Content of dropped elements (script, style, iframe…) is left out up to the closing
                // tag, within this paragraph. Void elements have no content.
                if (selfClosing || s_void.Contains(tag)) return;
                if (!closing) _dropping.Add(tag);
                else
                {
                    int at = _dropping.LastIndexOf(tag);
                    if (at >= 0) _dropping.RemoveRange(at, _dropping.Count - at);
                }
                return;
            }
            if (_dropping.Count > 0) return;
            if (!policy.AllowedElements.Contains(tag))
            {
                // Not an HTML element at all ("<angle>", "<T>"): the author meant the text.
                _b.Text(html, collapse: false);
                return;
            }
            if (tag == "br") { _b.LineBreak(); return; }
            if (tag == "img" && !closing)
            {
                // Sanitize the single element so only a safe data: picture survives.
                var r = Markup.Sanitization.HtmlSanitizer.Sanitize(html, _o.Html.Policy, _ct);
                foreach (var blocked in r.BlockedResources) _b.Blocked.Add(blocked.Url);
                foreach (var e in r.Fragment.DescendantElements())
                    if (e.IsHtml("img")) _b.Image(e.GetAttribute("src"), e.GetAttribute("alt"));
                return;
            }
            var s = _b.State;
            InlineState? open = tag switch
            {
                "b" or "strong" => s with { Bold = true },
                "i" or "em" or "cite" or "dfn" or "var" => s with { Italic = true },
                "u" or "ins" => s with { Underline = true },
                "s" or "strike" or "del" => s with { Strike = true },
                "sub" => s with { Sub = true, Sup = false },
                "sup" => s with { Sup = true, Sub = false },
                "code" or "kbd" or "samp" or "tt" => s with { Code = true },
                "mark" => s with { BackgroundHex = "FFFF00" },
                "small" => s with { SizePt = 9 },
                _ => null,
            };
            if (selfClosing) return;
            if (closing)
            {
                // Only a tag that raw HTML opened closes anything (never Markdown emphasis).
                for (int i = _rawOpen.Count - 1; i >= 0; i--)
                {
                    if (_rawOpen[i].Tag != tag) continue;
                    _b.RestoreInline(_rawOpen[i].Depth);
                    _rawOpen.RemoveRange(i, _rawOpen.Count - i);
                    break;
                }
                return;
            }
            if (open is null) return;
            _rawOpen.Add((tag, _b.InlineDepth));
            _b.PushInline(open.Value);
        }

        private static string? ParseTag(string html, out bool closing, out bool selfClosing)
        {
            closing = false;
            selfClosing = html.EndsWith("/>", StringComparison.Ordinal);
            if (html.Length < 3 || html[0] != '<' || html[1] is '!' or '?') return null;
            int i = 1;
            if (html[i] == '/') { closing = true; i++; }
            int start = i;
            while (i < html.Length && char.IsAsciiLetterOrDigit(html[i])) i++;
            if (i == start) return null;
            return html.Substring(start, i - start).ToLowerInvariant();
        }
    }

    // =====================================================================================
    // Export
    // =====================================================================================

    /// <summary>The document as Markdown.</summary>
    public static string Export(Document document, MarkdownExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var o = options ?? new MarkdownExportOptions();
        var ast = ToAst(document, o, cancellationToken);
        return Md.MarkdownWriter.Write(ast, o.Writer, cancellationToken);
    }

    /// <summary>Writes the document as Markdown to <paramref name="writer"/>.</summary>
    public static void Export(Document document, TextWriter writer, MarkdownExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.Write(Export(document, options, cancellationToken));
    }

    /// <summary>
    /// The document as a Chuvadi.Markup Markdown AST, for callers that want to edit it, render it
    /// with <c>MarkdownHtmlRenderer</c> or write it with their own <c>MarkdownWriterOptions</c>.
    /// </summary>
    public static Md.MarkdownDocument ToAst(Document document, MarkdownExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var o = options ?? new MarkdownExportOptions();
        var x = ExportWalker.Walk(document, o.Walk(cancellationToken));
        var e = new MdExporter(o);
        var root = new Md.MarkdownDocument();
        e.Blocks(x.Headers, root);
        e.Blocks(x.Body, root);
        e.Blocks(x.Footers, root);
        if (x.Footnotes.Count + x.Endnotes.Count > 0)
        {
            root.AppendChild(new Md.ThematicBreak());
            foreach (var n in x.Footnotes) e.Note(n, root);
            foreach (var n in x.Endnotes) e.Note(n, root);
        }
        return root;
    }

    private sealed class MdExporter
    {
        private readonly MarkdownExportOptions _o;
        private int _images;

        public MdExporter(MarkdownExportOptions o) { _o = o; }

        public void Blocks(List<XBlock> blocks, Md.MarkdownNode into)
        {
            // Consecutive list items become nested Markdown lists; quotes group consecutive quote paragraphs.
            int i = 0;
            while (i < blocks.Count)
            {
                var b = blocks[i];
                if (b is XParagraph { List: not null } && !_o.ListLabelsAsText)
                {
                    int j = i;
                    while (j < blocks.Count && blocks[j] is XParagraph { List: not null }) j++;
                    List(blocks, i, j, into);
                    i = j;
                    continue;
                }
                if (b is XParagraph { Kind: XParagraphKind.Quote })
                {
                    var q = new Md.BlockQuote();
                    while (i < blocks.Count && blocks[i] is XParagraph { Kind: XParagraphKind.Quote } qp)
                    {
                        Paragraph(qp, q);
                        i++;
                    }
                    into.AppendChild(q);
                    continue;
                }
                if (b is XParagraph { Kind: XParagraphKind.Code })
                {
                    var sb = new StringBuilder();
                    while (i < blocks.Count && blocks[i] is XParagraph { Kind: XParagraphKind.Code } cp)
                    {
                        sb.Append(PlainText(cp.Inlines)).Append('\n');
                        i++;
                    }
                    into.AppendChild(new Md.CodeBlock { IsFenced = true, FenceChar = _o.Writer.FenceChar, Code = sb.ToString() });
                    continue;
                }
                if (b is XParagraph p) Paragraph(p, into);
                else if (b is XTable t) Table(t, into);
                i++;
            }
        }

        private void List(List<XBlock> blocks, int from, int to, Md.MarkdownNode into)
        {
            // A stack of open lists by level; a change of kind at the same level starts a new list.
            // Each entry remembers its last item, which is where a deeper list goes.
            var stack = new List<(Md.ListBlock List, int Level, Md.ListItem? Last, ListInstance? Source, int Number)>();
            // Markdown joins adjacent lists that use the same marker: a separate list switches marker.
            var lastMarker = new Dictionary<int, (bool Ordered, char Marker)>();
            for (int i = from; i < to; i++)
            {
                var p = (XParagraph)blocks[i];
                var li = p.List!;
                while (stack.Count > 0 && stack[^1].Level > li.Level) stack.RemoveAt(stack.Count - 1);
                if (stack.Count > 0 && stack[^1].Level == li.Level
                    && ((stack[^1].List.ListKind == Md.ListKind.Ordered) != li.Ordered
                        || !ReferenceEquals(stack[^1].Source, li.List)
                        || (li.Ordered && li.Number <= stack[^1].Number)))
                    stack.RemoveAt(stack.Count - 1);
                if (stack.Count == 0 || stack[^1].Level < li.Level)
                {
                    char bullet = _o.Writer.BulletChar, delim = _o.Writer.OrderedDelimiter;
                    if (lastMarker.TryGetValue(li.Level, out var prev) && prev.Ordered == li.Ordered)
                    {
                        // A list of the same kind ended right here at this level: use the other marker.
                        if (!li.Ordered && prev.Marker == bullet) bullet = bullet == '-' ? '*' : '-';
                        if (li.Ordered && prev.Marker == delim) delim = delim == '.' ? ')' : '.';
                    }
                    var list = new Md.ListBlock
                    {
                        ListKind = li.Ordered ? Md.ListKind.Ordered : Md.ListKind.Bullet,
                        Start = li.Ordered ? Math.Max(0, li.Number) : 1,
                        BulletChar = bullet,
                        Delimiter = delim,
                        IsTight = true,
                    };
                    lastMarker[li.Level] = (li.Ordered, li.Ordered ? delim : bullet);
                    // Deeper levels start over under a new parent.
                    foreach (var k in new List<int>(lastMarker.Keys)) if (k > li.Level) lastMarker.Remove(k);
                    if (stack.Count == 0) into.AppendChild(list);
                    else
                    {
                        var parent = stack[^1];
                        var host = parent.Last ?? AddItem(parent.List);
                        stack[^1] = (parent.List, parent.Level, host, parent.Source, parent.Number);
                        host.AppendChild(list);
                    }
                    stack.Add((list, li.Level, null, li.List, li.Number));
                }
                var top = stack[^1];
                var item = AddItem(top.List);
                stack[^1] = (top.List, top.Level, item, top.Source, li.Number);
                foreach (var k in new List<int>(lastMarker.Keys)) if (k > li.Level) lastMarker.Remove(k);
                var para = new Md.Paragraph();
                Inlines(p.Inlines, para);
                item.AppendChild(para);
            }

            static Md.ListItem AddItem(Md.ListBlock list)
            {
                var item = new Md.ListItem();
                list.AppendChild(item);
                return item;
            }
        }

        public void Note(XNote n, Md.MarkdownNode into)
        {
            bool first = true;
            foreach (var b in n.Blocks)
            {
                if (b is XParagraph p)
                {
                    var para = new Md.Paragraph();
                    if (first) para.AppendChild(new Md.TextInline(string.Format(CultureInfo.InvariantCulture, _o.NoteReferenceFormat, n.Label) + " "));
                    Inlines(p.Inlines, para);
                    into.AppendChild(para);
                }
                else if (b is XTable t) Table(t, into);
                first = false;
            }
        }

        private void Paragraph(XParagraph p, Md.MarkdownNode into)
        {
            if (p.Kind == XParagraphKind.Heading)
            {
                var h = new Md.Heading { Level = Math.Clamp(p.HeadingLevel, 1, 6) };
                Inlines(p.Inlines, h);
                into.AppendChild(h);
                return;
            }
            if (p.List is { } li && _o.ListLabelsAsText)
            {
                var lp = new Md.Paragraph();
                lp.AppendChild(new Md.TextInline(new string(' ', 0) + li.Label + " "));
                Inlines(p.Inlines, lp);
                into.AppendChild(lp);
                return;
            }
            if (p.IsEmpty) return; // Markdown has no empty paragraphs
            var para = new Md.Paragraph();
            Inlines(p.Inlines, para);
            into.AppendChild(para);
        }

        private void Table(XTable t, Md.MarkdownNode into)
        {
            var mt = new Md.Table();
            int cols = 0;
            foreach (var r in t.Rows)
            {
                int n = 0;
                foreach (var c in r.Cells) n += c.ColumnSpan;
                cols = Math.Max(cols, n);
            }
            cols = Math.Max(1, cols);
            for (int i = 0; i < cols; i++) mt.Alignments.Add(Md.TableAlignment.None);
            bool headerDone = false;
            for (int ri = 0; ri < t.Rows.Count; ri++)
            {
                var r = t.Rows[ri];
                bool header = !headerDone && (r.IsHeader || (ri == 0 && _o.FirstRowAsHeader));
                if (!headerDone && !header)
                {
                    var empty = new Md.TableRow { IsHeader = true };
                    for (int i = 0; i < cols; i++) empty.AppendChild(new Md.TableCell());
                    mt.AppendChild(empty);
                    headerDone = true;
                }
                if (headerDone && r.IsHeader) header = false; // GFM has one header row
                var row = new Md.TableRow { IsHeader = header };
                int col = 0;
                foreach (var c in r.Cells)
                {
                    var cell = new Md.TableCell();
                    if (!c.Covered) CellInlines(c, cell, col, mt, header);
                    row.AppendChild(cell);
                    col++;
                    for (int k = 1; k < c.ColumnSpan && col < cols; k++, col++) row.AppendChild(new Md.TableCell());
                }
                while (col++ < cols) row.AppendChild(new Md.TableCell());
                mt.AppendChild(row);
                if (header) headerDone = true;
            }
            into.AppendChild(mt);
        }

        private void CellInlines(XCell c, Md.TableCell cell, int col, Md.Table table, bool header)
        {
            bool first = true;
            foreach (var p in CellParagraphs(c.Blocks))
            {
                if (header && first && p.Alignment is { } al && col < table.Alignments.Count)
                    table.Alignments[col] = al switch
                    {
                        ParagraphAlignment.Center => Md.TableAlignment.Center,
                        ParagraphAlignment.Right => Md.TableAlignment.Right,
                        _ => Md.TableAlignment.None,
                    };
                if (!first) CellBreak(cell);
                Inlines(p.Inlines, cell, inTable: true, header: header);
                first = false;
            }
        }

        private void CellBreak(Md.MarkdownNode cell)
        {
            if (_o.TableCellLineBreak is { } br) cell.AppendChild(new Md.HtmlInline(br));
            else cell.AppendChild(new Md.TextInline(" "));
        }

        private static IEnumerable<XParagraph> CellParagraphs(List<XBlock> blocks)
        {
            foreach (var b in blocks)
            {
                if (b is XParagraph p) yield return p;
                else if (b is XTable t)
                    foreach (var r in t.Rows)
                        foreach (var c in r.Cells)
                            foreach (var q in CellParagraphs(c.Blocks)) yield return q;
            }
        }

        private static string PlainText(List<XInline> inlines)
        {
            var sb = new StringBuilder();
            foreach (var i in inlines)
            {
                if (i is XText t) sb.Append(t.Text);
                else if (i is XTab) sb.Append('\t');
                else if (i is XBreak) sb.Append('\n');
                else if (i is XLink l) sb.Append(PlainText(l.Inlines));
                else if (i is XNoteRef nr) sb.Append('[').Append(nr.Note.Label).Append(']');
                else if (i is XImage img && !string.IsNullOrEmpty(img.Image.AltText)) sb.Append('[').Append(img.Image.AltText).Append(']');
            }
            return sb.ToString();
        }

        private void Inlines(List<XInline> inlines, Md.MarkdownNode into, bool inTable = false, bool header = false)
        {
            for (int k = 0; k < inlines.Count; k++)
            {
                var i = inlines[k];
                switch (i)
                {
                    // A header cell is bold already: no ** around its text.
                    case XText t: Text(t.Text, header ? t.Style with { Bold = false } : t.Style, into, NextChar(inlines, k)); break;
                    case XTab: into.AppendChild(new Md.TextInline("\t")); break;
                    case XBreak br:
                        if (inTable) CellBreak(into);
                        else into.AppendChild(new Md.HardBreak { IsBackslash = _o.Writer.BackslashHardBreaks });
                        break;
                    case XLink l:
                    {
                        // Links that could run or reach something unexpected are written as their text.
                        if (l.Url is { } u && !Urls.IsSafeExportLink(u)) { Inlines(l.Inlines, into, inTable, header); break; }
                        var dest = l.Url ?? "";
                        if (l.Anchor is { } a) dest = (l.Url ?? "") + "#" + Uri.EscapeDataString(a);
                        var link = new Md.Link { Destination = dest, Title = l.Title };
                        Inlines(l.Inlines, link, inTable, header);
                        if (link.Children.Count == 0) break;
                        into.AppendChild(link);
                        break;
                    }
                    case XImage img: Image(img, into); break;
                    case XNoteRef nr:
                        into.AppendChild(new Md.TextInline(string.Format(CultureInfo.InvariantCulture, _o.NoteReferenceFormat, nr.Note.Label)));
                        break;
                }
            }
        }

        private void Image(XImage img, Md.MarkdownNode into)
        {
            _images++;
            var spec = img.Image;
            string? url = _o.ImageUrlResolver?.Invoke(new ExportedImage(spec, _images));
            if (url is not null && !Urls.IsSafeExportLink(url) && !url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) url = null;
            bool tooBig = _o.MaxImageBytes is long max && spec.Bytes.LongLength > max;
            if (url is null && _o.Images == ImageExportMode.DataUri && spec.Bytes.Length > 0 && !tooBig && Urls.BrowserImageTypes.Contains(spec.ContentType))
                url = DataUri.Encode(spec.Bytes, spec.ContentType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : spec.ContentType);
            if (url is null)
            {
                if (!string.IsNullOrEmpty(spec.AltText)) into.AppendChild(new Md.TextInline(spec.AltText));
                return;
            }
            var mi = new Md.Image { Destination = url, Title = spec.Title };
            if (!string.IsNullOrEmpty(spec.AltText)) mi.AppendChild(new Md.TextInline(spec.AltText));
            into.AppendChild(mi);
        }

        /// <summary>The character that will follow inline <paramref name="k"/> in the output ('\0' = line end or white space).</summary>
        private static char NextChar(List<XInline> inlines, int k)
        {
            if (k + 1 >= inlines.Count) return '\0';
            return inlines[k + 1] switch
            {
                XText t when t.Text.Length > 0 => t.Text[0],
                XLink => '[',
                XImage => '!',
                XNoteRef => '[',
                _ => '\0',
            };
        }

        /// <summary>The character the output ends with so far in <paramref name="into"/> ('\0' = start).</summary>
        private static char PrevChar(Md.MarkdownNode into)
        {
            if (into.Children.Count == 0) return '\0';
            return into.Children[^1] switch
            {
                Md.TextInline t when t.Text.Length > 0 => t.Text[^1],
                Md.HardBreak or Md.SoftBreak => '\0',
                Md.CodeSpan => '`',
                Md.HtmlInline => '>',
                Md.Link or Md.Image => ')',
                _ => '*',
            };
        }

        private static bool IsWs(char c) => c == '\0' || char.IsWhiteSpace(c);
        private static bool IsPunct(char c) => char.IsPunctuation(c) || char.IsSymbol(c);
        private static bool LeftFlanking(char before, char after) => !IsWs(after) && (!IsPunct(after) || IsWs(before) || IsPunct(before));
        private static bool RightFlanking(char before, char after) => !IsWs(before) && (!IsPunct(before) || IsWs(after) || IsPunct(after));

        /// <summary>CommonMark's rules: can a delimiter run of <paramref name="d"/> open after <paramref name="before"/> and close before <paramref name="after"/> around text from <paramref name="first"/> to <paramref name="last"/>?</summary>
        private static bool CanDelimit(char d, char before, char first, char last, char after)
        {
            bool lf = LeftFlanking(before, first), rfOpen = RightFlanking(before, first);
            bool rf = RightFlanking(last, after), lfClose = LeftFlanking(last, after);
            if (d == '_') return lf && (!rfOpen || IsPunct(before)) && rf && (!lfClose || IsPunct(after));
            return lf && rf;
        }

        private void Text(string text, XStyle s, Md.MarkdownNode into, char nextOuter)
        {
            if (text.Length == 0) return;
            // Emphasis cannot start or end with white space: keep it outside the markers.
            int lead = 0, trail = 0;
            while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;
            while (trail < text.Length - lead && char.IsWhiteSpace(text[text.Length - 1 - trail])) trail++;
            bool styled = s.Bold || s.Italic || s.Strike || s.Code || s.Deleted || (s.Underline && _o.UnderlineAsHtml)
                          || ((s.Sub || s.Sup) && _o.SubSuperscriptAsHtml);
            if (!styled || lead == text.Length)
            {
                AppendText(into, text);
                return;
            }
            if (lead > 0) AppendText(into, text.Substring(0, lead));
            var core = text.Substring(lead, text.Length - lead - trail);
            // Delimiter emphasis only where CommonMark's flanking rules let it open and close given the
            // actual neighbouring characters (and never straight after another delimiter run, which
            // would merge with it); otherwise EmphasisFallback decides.
            char before = lead > 0 ? ' ' : PrevChar(into);
            char after = trail > 0 ? ' ' : nextOuter;
            bool fits = !(lead == 0 && into.Children.Count > 0 && into.Children[^1] is Md.Strong or Md.Emphasis or Md.Strikethrough);
            if (fits && (s.Strike || s.Deleted)) fits = CanDelimit('~', before, core[0], core[^1], after);
            if (fits && s.Italic) fits = CanDelimit(_o.Writer.EmphasisChar, before, core[0], core[^1], after);
            if (fits && s.Bold) fits = CanDelimit(_o.Writer.StrongChar, before, core[0], core[^1], after);
            bool html = !fits && _o.EmphasisFallback == MarkdownEmphasisFallback.Html;
            if (!fits && _o.EmphasisFallback == MarkdownEmphasisFallback.PlainText) s = s with { Bold = false, Italic = false, Strike = false, Deleted = false };
            bool u = s.Underline && _o.UnderlineAsHtml;
            string? sx = (s.Sub || s.Sup) && _o.SubSuperscriptAsHtml ? (s.Sub ? "sub" : "sup") : null;
            var tags = new List<string>();
            if (sx is not null) tags.Add(sx);
            if (u) tags.Add("u");
            Md.MarkdownNode node = s.Code ? new Md.CodeSpan(core) : new Md.TextInline(core);
            if (html)
            {
                if (s.Bold) tags.Add("strong");
                if (s.Italic) tags.Add("em");
                if (s.Strike || s.Deleted) tags.Add("del");
            }
            else
            {
                if (s.Strike || s.Deleted) node = Wrap(new Md.Strikethrough(), node);
                if (s.Italic) node = Wrap(new Md.Emphasis { Delimiter = _o.Writer.EmphasisChar }, node);
                if (s.Bold) node = Wrap(new Md.Strong { Delimiter = _o.Writer.StrongChar }, node);
            }
            foreach (var t in tags) into.AppendChild(new Md.HtmlInline("<" + t + ">"));
            into.AppendChild(node);
            for (int k = tags.Count - 1; k >= 0; k--) into.AppendChild(new Md.HtmlInline("</" + tags[k] + ">"));
            if (trail > 0) AppendText(into, text.Substring(text.Length - trail));
        }

        private static void AppendText(Md.MarkdownNode into, string text)
        {
            // Merge adjacent text so the writer sees whole words (it escapes per text node).
            if (into.Children.Count > 0 && into.Children[^1] is Md.TextInline prev) prev.Text += text;
            else into.AppendChild(new Md.TextInline(text));
        }

        private static Md.MarkdownNode Wrap(Md.MarkdownNode container, Md.MarkdownNode child)
        {
            container.AppendChild(child);
            return container;
        }
    }
}
