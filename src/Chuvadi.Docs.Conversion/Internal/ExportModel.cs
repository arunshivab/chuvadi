using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Conversion.Internal;

// A neutral, format-independent view of a document for the text, Markdown and HTML writers:
// paragraphs classified (heading, quote, code, list item), runs reduced to the formatting that
// differs from the paragraph's own (so a bold heading style does not make every word "bold"),
// links merged across runs and HYPERLINK fields, notes collected with the labels Word shows.

internal enum XParagraphKind { Normal, Heading, Quote, Code }

internal sealed class XListInfo
{
    public bool Ordered;
    public int Number;
    public int Level;
    public string Label = "";
    public ListInstance? List;
    public NumberFormat Format;
}

internal abstract class XBlock { }

internal sealed class XParagraph : XBlock
{
    public XParagraphKind Kind;
    public int HeadingLevel;          // 1..9 for headings
    public XListInfo? List;
    public ParagraphAlignment? Alignment;
    public double IndentLeftPt;
    public bool PageBreakBefore;
    public string StyleId = "Normal";
    public readonly List<XInline> Inlines = new();
    public string? BookmarkId;       // first bookmark started in the paragraph (an anchor target)
    public List<string>? MoreBookmarks; // further bookmarks started in the paragraph

    public bool IsEmpty
    {
        get
        {
            foreach (var i in Inlines)
                if (i is not XText t || t.Text.Length > 0) return false;
            return true;
        }
    }
}

internal sealed class XTable : XBlock
{
    public readonly List<XRow> Rows = new();
    public int Columns;
}

internal sealed class XRow
{
    public bool IsHeader;
    public readonly List<XCell> Cells = new();
}

internal sealed class XCell
{
    public int ColumnSpan = 1;
    public int RowSpan = 1;          // > 1 on the first cell of a vertical merge
    public bool Covered;             // a continuation of a vertical merge (not written in HTML)
    public bool Placeholder;         // grid columns skipped before / after the row's cells (w:gridBefore / w:gridAfter)
    public readonly List<XBlock> Blocks = new();
}

internal abstract class XInline { }

internal record struct XStyle(bool Bold, bool Italic, bool Underline, bool Strike, bool Code, bool Sub, bool Sup,
    string? ColorHex, string? BackgroundHex, double SizePt, string? Font, bool Inserted, bool Deleted, bool SmallCaps, bool Caps);

internal sealed class XText : XInline
{
    public XText(string text, XStyle style) { Text = text; Style = style; }
    public string Text;
    public XStyle Style;
}

internal sealed class XBreak : XInline
{
    public XBreak(BreakType type) { Type = type; }
    public BreakType Type;
}

internal sealed class XTab : XInline { public XStyle Style; }

internal sealed class XLink : XInline
{
    public string? Url;
    public string? Anchor;
    public string? Title;
    public readonly List<XInline> Inlines = new();
}

internal sealed class XImage : XInline
{
    public XImage(ImageSpec image) { Image = image; }
    public ImageSpec Image;
}

internal sealed class XNoteRef : XInline
{
    public XNoteRef(XNote note, XStyle style) { Note = note; Style = style; }
    public XNote Note;
    public XStyle Style;
}

internal sealed class XNote
{
    public string Label = "";
    public NoteKind Kind;
    public int Index;                 // 1-based order of appearance per kind
    public readonly List<XBlock> Blocks = new();
}

internal sealed class XDocument
{
    public readonly List<XBlock> Headers = new();
    public readonly List<XBlock> Body = new();
    public readonly List<XBlock> Footers = new();
    public readonly List<XNote> Footnotes = new();
    public readonly List<XNote> Endnotes = new();
    public string? Title;
    public string? Language;
}

/// <summary>Options shared by every exporter's walk over the document.</summary>
internal sealed class WalkOptions
{
    public bool IncludeHeadersFooters;
    public bool IncludeNotes = true;
    public bool IncludeTextBoxes = true;
    public bool IncludeHiddenText;
    public TrackedChangesView TrackedChanges = TrackedChangesView.Final;
    public ICollection<string> CodeStyleIds = ConversionDefaults.CodeStyleIds();
    public ICollection<string> QuoteStyleIds = ConversionDefaults.QuoteStyleIds();
    public ICollection<string> MonospaceFonts = ConversionDefaults.MonospaceFonts();
    public System.Threading.CancellationToken Cancellation;
}

internal sealed class ExportWalker
{
    private readonly Document _doc;
    private readonly WalkOptions _o;
    private readonly IReadOnlyDictionary<Paragraph, ListLabel> _labels;
    private readonly Dictionary<(Paragraph, int), NoteReferenceInfo> _noteRefs = new();
    private readonly Dictionary<Note, XNote> _notes = new(ReferenceEqualityComparer.Instance);
    private readonly XDocument _x = new();
    private readonly HashSet<TextBox> _boxes = new(ReferenceEqualityComparer.Instance);
    private int _steps;

    private ExportWalker(Document doc, WalkOptions o)
    {
        _doc = doc;
        _o = o;
        _labels = doc.GetListLabels();
        if (o.IncludeNotes)
            foreach (var r in doc.GetNoteReferences()) _noteRefs[(r.Paragraph, r.RunIndex)] = r;
    }

    public static XDocument Walk(Document doc, WalkOptions o)
    {
        var w = new ExportWalker(doc, o);
        w._x.Title = string.IsNullOrWhiteSpace(doc.Properties.Title) ? null : doc.Properties.Title;
        w._x.Language = doc.Properties.Language;
        if (o.IncludeHeadersFooters)
        {
            var seen = new HashSet<HeaderFooterContent>(ReferenceEqualityComparer.Instance);
            foreach (var s in doc.Sections)
                foreach (var h in new[] { s.FirstPageHeader, s.Header, s.EvenPageHeader })
                    if (h.Count > 0 && seen.Add(h)) w.Blocks(h.Blocks, w._x.Headers, null);
            foreach (var s in doc.Sections)
                foreach (var f in new[] { s.FirstPageFooter, s.Footer, s.EvenPageFooter })
                    if (f.Count > 0 && seen.Add(f)) w.Blocks(f.Blocks, w._x.Footers, null);
        }
        w.Blocks(doc.Blocks, w._x.Body, null);
        // Notes are written in reference order; their content may itself reference nothing further.
        return w._x;
    }

    private void Tick()
    {
        if ((++_steps & 1023) == 0) _o.Cancellation.ThrowIfCancellationRequested();
    }

    private void Blocks(IReadOnlyList<object> blocks, List<XBlock> into, DocTable? _)
    {
        foreach (var b in blocks)
        {
            Tick();
            if (b is Paragraph p) Paragraph(p, into, null);
            else if (b is DocTable t) into.Add(Table(t));
        }
    }

    private XTable Table(DocTable t)
    {
        var xt = new XTable { Columns = t.Columns };
        var rows = t.Rows;
        // Vertical merges: the restart cell spans the continuation cells below it.
        var restartAt = new Dictionary<int, XCell>();
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Revision is { Type: RevisionType.Delete } && _o.TrackedChanges == TrackedChangesView.Final) continue;
            if (row.Revision is { Type: RevisionType.Insert } && _o.TrackedChanges == TrackedChangesView.Original) continue;
            var xr = new XRow { IsHeader = row.IsHeader };
            int before = Math.Clamp(row.GridBefore, 0, row.Cells.Count);
            if (before > 0) xr.Cells.Add(new XCell { ColumnSpan = before, Placeholder = true });
            int end = Math.Max(before, row.Cells.Count - Math.Clamp(row.GridAfter, 0, row.Cells.Count));
            for (int c = before; c < end; c++)
            {
                var cell = row.Cells[c];
                var xc = new XCell { ColumnSpan = Math.Max(1, cell.ColumnSpan) };
                if (cell.VerticalMerge == VerticalMerge.Continue && restartAt.TryGetValue(c, out var start))
                {
                    start.RowSpan++;
                    xc.Covered = true;
                }
                else
                {
                    if (cell.VerticalMerge == VerticalMerge.Restart) restartAt[c] = xc;
                    else restartAt.Remove(c);
                    foreach (var b in cell.Blocks)
                    {
                        Tick();
                        if (b is Paragraph p) Paragraph(p, xc.Blocks, new TableCellRef(t, r, c));
                        else if (b is DocTable nt) xc.Blocks.Add(Table(nt));
                    }
                }
                xr.Cells.Add(xc);
                c += xc.ColumnSpan - 1;
            }
            if (end < row.Cells.Count) xr.Cells.Add(new XCell { ColumnSpan = row.Cells.Count - end, Placeholder = true });
            xt.Rows.Add(xr);
        }
        return xt;
    }

    private void Paragraph(Paragraph p, List<XBlock> into, TableCellRef? cell)
    {
        var x = new XParagraph { StyleId = p.StyleId, PageBreakBefore = p.PageBreakBefore };
        var pf = cell is { } cr ? _doc.GetEffectiveParagraphFormat(p, cr) : _doc.GetEffectiveParagraphFormat(p);
        x.Alignment = pf.Alignment;
        x.IndentLeftPt = pf.IndentLeftPt ?? 0;
        if (pf.StartOnNewPage == true) x.PageBreakBefore = true;

        int heading = HeadingLevel(p.StyleId, pf);
        if (heading > 0) { x.Kind = XParagraphKind.Heading; x.HeadingLevel = heading; }
        else if (_o.QuoteStyleIds.Contains(p.StyleId)) x.Kind = XParagraphKind.Quote;
        else if (_o.CodeStyleIds.Contains(p.StyleId)) x.Kind = XParagraphKind.Code;

        if (_labels.TryGetValue(p, out var label))
        {
            x.List = new XListInfo
            {
                Ordered = label.LevelDefinition.Format != NumberFormat.Bullet,
                Number = label.Number,
                Level = label.Level,
                Label = label.Text,
                List = label.List,
                Format = label.LevelDefinition.Format ?? NumberFormat.Decimal,
            };
        }

        var baseline = cell is { } c0
            ? _doc.GetEffectiveRunFormat(p, new Run("", TextFormat.None), c0)
            : _doc.GetEffectiveRunFormat(p, new Run("", TextFormat.None));
        Inlines(p, x, baseline, cell);

        // A paragraph whose text is all in a monospace font reads as code.
        if (x.Kind == XParagraphKind.Normal && x.List is null && AllCode(x.Inlines)) x.Kind = XParagraphKind.Code;
        into.Add(x);

        if (_o.IncludeTextBoxes)
            foreach (var r in p.Runs)
                if (r.TextBox is { } box && _boxes.Add(box)) Blocks(box.Blocks, into, null);
    }

    private static bool AllCode(List<XInline> inlines)
    {
        bool any = false;
        foreach (var i in inlines)
        {
            if (i is XText t)
            {
                if (t.Text.Length == 0) continue;
                if (!t.Style.Code) return false;
                any = true;
            }
            else if (i is XBreak or XTab) continue;
            else return false;
        }
        return any;
    }

    private static int HeadingLevel(string styleId, ParagraphFormat pf)
    {
        if (styleId == "Title") return 1;
        if (styleId.Length == 8 && styleId.StartsWith("Heading", StringComparison.Ordinal) && styleId[7] is >= '1' and <= '9')
            return styleId[7] - '0';
        if (styleId != "Normal" && pf.OutlineLevel is int ol and >= 0 and <= 8) return ol + 1;
        return 0;
    }

    private void Inlines(Paragraph p, XParagraph x, TextFormat baseline, TableCellRef? cell)
    {
        var target = x.Inlines;
        XLink? link = null;
        string? linkKey = null;
        // HYPERLINK fields: their result runs form a link. Each entry carries the innermost
        // HYPERLINK link in effect, so the lookup per run is O(1).
        var fields = new Stack<XLink?>();
        var runs = p.Runs;
        for (int i = 0; i < runs.Count; i++)
        {
            Tick();
            var run = runs[i];
            if (run.Bookmark is { IsStart: true } bm)
            {
                if (x.BookmarkId is null) x.BookmarkId = bm.Bookmark.Name;
                else (x.MoreBookmarks ??= new List<string>()).Add(bm.Bookmark.Name);
            }
            if (run.FieldMark is { } fm)
            {
                if (fm.Type == FieldMarkType.Begin)
                {
                    XLink? fl = fields.Count > 0 ? fields.Peek() : null;
                    // A HYPERLINK inside another stays part of the outer link (links do not nest).
                    if (fl is null && string.Equals(fm.Field.FieldType, "HYPERLINK", StringComparison.OrdinalIgnoreCase) && HyperlinkField(fm.Field) is { } hl)
                    {
                        CloseLink();
                        target.Add(hl);
                        fl = hl;
                    }
                    fields.Push(fl);
                }
                else if (fm.Type == FieldMarkType.End && fields.Count > 0)
                {
                    fields.Pop();
                }
                continue;
            }
            if (run.Bookmark is not null || run.CommentRange is not null || run.CommentReference is not null || run.IsAnnotationMark) continue;
            if (!Visible(run)) continue;
            // Hidden text and left-out notes are dropped before anything (a link) is created for them.
            var eff = cell is { } c ? _doc.GetEffectiveRunFormat(p, run, c) : _doc.GetEffectiveRunFormat(p, run);
            if (eff.Hidden == true && !_o.IncludeHiddenText) continue;
            if (run.NoteReference is not null && !_o.IncludeNotes) continue;
            if (run.IsNoteMark || run.TextBox is not null) continue;

            var into = target;
            XLink? fieldLink = fields.Count > 0 ? fields.Peek() : null;
            if (fieldLink is not null) { CloseLink(); into = fieldLink.Inlines; }
            else if (run.IsLink)
            {
                var key = (run.HyperlinkUrl ?? "") + "\u0001" + (run.HyperlinkAnchor ?? "");
                if (link is null || key != linkKey)
                {
                    link = new XLink { Url = run.HyperlinkUrl, Anchor = run.HyperlinkAnchor, Title = run.HyperlinkTooltip };
                    linkKey = key;
                    target.Add(link);
                }
                into = link.Inlines;
            }
            else CloseLink();

            var style = Style(eff, baseline, run);

            if (run.NoteReference is { } note)
            {
                into.Add(new XNoteRef(NoteFor(note, p, i), style));
                continue;
            }
            if (run.Image is { } img) { into.Add(new XImage(img)); continue; }
            if (run.Break is BreakType bt) { into.Add(new XBreak(bt)); continue; }
            if (run.IsTab || run.PositionalTab is not null) { into.Add(new XTab { Style = style }); continue; }
            if (run.Symbol is { } sym) { into.Add(new XText(sym.Code.ToString(), style)); continue; }
            if (run.TextContent.Length > 0) AddText(into, run.TextContent, style);
        }

        Tidy(target);

        void CloseLink() { link = null; linkKey = null; }
    }

    /// <summary>Merges neighbouring text of the same style and drops links left without content (their URL is never shown as text).</summary>
    private static void Tidy(List<XInline> list)
    {
        int w = 0;
        for (int r = 0; r < list.Count; r++)
        {
            var item = list[r];
            if (item is XLink l)
            {
                Tidy(l.Inlines);
                if (l.Inlines.Count == 0) continue;
            }
            if (item is XText t && w > 0 && list[w - 1] is XText prev && prev.Style == t.Style)
            {
                prev.Text += t.Text;
                continue;
            }
            list[w++] = item;
        }
        list.RemoveRange(w, list.Count - w);
    }

    /// <summary>Text, with line breaks and tabs inside it (loaded documents carry w:br and w:tab that way) as their own inlines.</summary>
    private static void AddText(List<XInline> into, string text, XStyle style)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is not ('\n' or '\t' or '\r')) continue;
            if (i > start) into.Add(new XText(text.Substring(start, i - start), style));
            if (c == '\n') into.Add(new XBreak(BreakType.Line));
            else if (c == '\t') into.Add(new XTab { Style = style });
            start = i + 1;
        }
        if (start < text.Length) into.Add(new XText(start == 0 ? text : text.Substring(start), style));
    }

    private bool Visible(Run run) => _o.TrackedChanges switch
    {
        TrackedChangesView.Final => !run.IsDeleted,
        TrackedChangesView.Original => run.Revision is not { Type: RevisionType.Insert } && run.InsertedRevision is null,
        _ => true,
    };

    private static XLink? HyperlinkField(Field f)
    {
        // HYPERLINK "url" [\l "anchor"] [\o "tooltip"]
        var parts = FieldArgs(f.Instruction);
        var l = new XLink();
        for (int i = 1; i < parts.Count; i++)
        {
            var a = parts[i];
            if (a == "\\l" && i + 1 < parts.Count) l.Anchor = parts[++i];
            else if (a == "\\o" && i + 1 < parts.Count) l.Title = parts[++i];
            else if (a.StartsWith('\\')) { if (a is "\\t" && i + 1 < parts.Count) i++; }
            else if (l.Url is null) l.Url = a;
        }
        return l.Url is null && l.Anchor is null ? null : l;
    }

    private static List<string> FieldArgs(string instruction)
    {
        var list = new List<string>();
        int i = 0;
        while (i < instruction.Length)
        {
            while (i < instruction.Length && char.IsWhiteSpace(instruction[i])) i++;
            if (i >= instruction.Length) break;
            var sb = new System.Text.StringBuilder();
            if (instruction[i] == '"')
            {
                i++;
                while (i < instruction.Length && instruction[i] != '"')
                {
                    if (instruction[i] == '\\' && i + 1 < instruction.Length && instruction[i + 1] is '"' or '\\') i++;
                    sb.Append(instruction[i++]);
                }
                i++;
            }
            else while (i < instruction.Length && !char.IsWhiteSpace(instruction[i])) sb.Append(instruction[i++]);
            list.Add(sb.ToString());
        }
        return list;
    }

    private XNote NoteFor(Note note, Paragraph p, int runIndex)
    {
        if (_notes.TryGetValue(note, out var xn)) return xn;
        var list = note.Kind == NoteKind.Footnote ? _x.Footnotes : _x.Endnotes;
        xn = new XNote { Kind = note.Kind, Index = list.Count + 1 };
        xn.Label = _noteRefs.TryGetValue((p, runIndex), out var info) ? info.Label : xn.Index.ToString(CultureInfo.InvariantCulture);
        _notes[note] = xn;
        list.Add(xn);
        Blocks(note.Blocks, xn.Blocks, null);
        // Word puts a space after the note's own number; the exporters write their own separator.
        if (xn.Blocks.Count > 0 && xn.Blocks[0] is XParagraph { Inlines.Count: > 0 } first && first.Inlines[0] is XText lead)
        {
            lead.Text = lead.Text.TrimStart();
            if (lead.Text.Length == 0) first.Inlines.RemoveAt(0);
        }
        return xn;
    }

    private XStyle Style(TextFormat eff, TextFormat baseline, Run run)
    {
        bool code = IsMono(eff) && !IsMono(baseline);
        bool underline = (eff.Underline || eff.UnderlineStyle is { } us && us != UnderlineStyle.None) && !run.IsLink
                         && !(baseline.Underline || baseline.UnderlineStyle is { } bs && bs != UnderlineStyle.None);
        string? color = eff.ColorHex is { } ch && !string.Equals(ch, "auto", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(ch, baseline.ColorHex, StringComparison.OrdinalIgnoreCase) && !run.IsLink ? ch : null;
        string? bg = HighlightHex(eff.Highlight) ?? (eff.Shading?.FillHex ?? eff.ShadingHex);
        if (bg is not null && string.Equals(bg, "auto", StringComparison.OrdinalIgnoreCase)) bg = null;
        if (bg is not null && string.Equals(bg, HighlightHex(baseline.Highlight) ?? baseline.Shading?.FillHex ?? baseline.ShadingHex, StringComparison.OrdinalIgnoreCase)) bg = null;
        double size = eff.SizePt > 0 && Math.Abs(eff.SizePt - baseline.SizePt) > 0.01 ? eff.SizePt : 0;
        string? font = eff.Font is { } f && !string.Equals(f, baseline.Font, StringComparison.OrdinalIgnoreCase) && !code ? f : null;
        return new XStyle(
            Bold: eff.Bold && !baseline.Bold,
            Italic: eff.Italic && !baseline.Italic,
            Underline: underline,
            Strike: (eff.Strikethrough || eff.DoubleStrikethrough == true) && !(baseline.Strikethrough || baseline.DoubleStrikethrough == true),
            Code: code,
            Sub: eff.VerticalPosition == VerticalTextPosition.Subscript && run.NoteReference is null,
            Sup: eff.VerticalPosition == VerticalTextPosition.Superscript && run.NoteReference is null,
            ColorHex: color,
            BackgroundHex: bg,
            SizePt: size,
            Font: font,
            Inserted: _o.TrackedChanges == TrackedChangesView.Markup && (run.Revision is { Type: RevisionType.Insert }),
            Deleted: _o.TrackedChanges == TrackedChangesView.Markup && run.IsDeleted,
            SmallCaps: eff.SmallCaps == true && baseline.SmallCaps != true,
            Caps: eff.Caps == true && baseline.Caps != true);
    }

    private bool IsMono(TextFormat f) => f.Font is { } name && _o.MonospaceFonts.Contains(name);

    /// <summary>Word's 16 highlight colours as hex.</summary>
    internal static string? HighlightHex(string? highlight) => highlight?.ToLowerInvariant() switch
    {
        null or "" or "none" => null,
        "yellow" => "FFFF00",
        "green" => "00FF00",
        "cyan" => "00FFFF",
        "magenta" => "FF00FF",
        "blue" => "0000FF",
        "red" => "FF0000",
        "darkblue" => "000080",
        "darkcyan" => "008080",
        "darkgreen" => "008000",
        "darkmagenta" => "800080",
        "darkred" => "800000",
        "darkyellow" => "808000",
        "darkgray" => "808080",
        "lightgray" => "C0C0C0",
        "black" => "000000",
        "white" => "FFFFFF",
        _ => null,
    };
}
