using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Docs.Conversion.Internal;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Conversion;

/// <summary>
/// Plain text to and from a <see cref="Document"/>.
/// <code>
/// var doc = TextConversion.Import(File.ReadAllText("notes.txt"));
/// string text = TextConversion.Export(Document.Load("report.docx"));
/// </code>
/// </summary>
public static class TextConversion
{
    /// <summary>Builds a new document from plain text.</summary>
    public static Document Import(string text, TextImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        var doc = new Document();
        Import(text, doc, options, cancellationToken);
        return doc;
    }

    /// <summary>Imports plain text (into <paramref name="target"/>, or a new document when null), with the same result shape as the other importers.</summary>
    public static ConversionResult ImportDetailed(string text, Document? target = null, TextImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        var doc = target ?? new Document();
        Import(text, doc, options, cancellationToken);
        return new ConversionResult(doc, Array.Empty<string>(), Array.Empty<string>());
    }

    /// <summary>Reads all text from <paramref name="reader"/> into a new document.</summary>
    public static Document Import(TextReader reader, TextImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        var o = options ?? new TextImportOptions();
        var buffer = new char[81920];
        var sb = new StringBuilder();
        int n;
        while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sb.Length + n > o.MaxInputChars) throw new InvalidDataException($"Text input exceeds {o.MaxInputChars} characters.");
            sb.Append(buffer, 0, n);
        }
        return Import(sb.ToString(), o, cancellationToken);
    }

    /// <summary>Appends plain text to <paramref name="target"/> as paragraphs of its body.</summary>
    public static void Import(string text, Document target, TextImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (target is null) throw new ArgumentNullException(nameof(target));
        var o = options ?? new TextImportOptions();
        if (text.Length > o.MaxInputChars) throw new InvalidDataException($"Text input exceeds {o.MaxInputChars} characters.");
        if (text.Length > 0 && text[0] == '\ufeff') text = text.Substring(1);

        var lines = SplitLines(text);
        // A final line break ends the last line; it does not start an empty paragraph.
        if (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        if (o.ParagraphMode == TextParagraphMode.LinePerParagraph)
        {
            int k = 0;
            foreach (var line in lines)
            {
                if ((++k & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                AddLines(target, new[] { line }, o, reflow: false);
            }
            return;
        }
        var group = new List<string>();
        int step = 0;
        foreach (var line in lines)
        {
            if ((++step & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (line.Trim().Length == 0)
            {
                if (group.Count > 0) { AddLines(target, group, o, o.ParagraphMode == TextParagraphMode.BlankLineSeparatedReflow); group.Clear(); }
                continue;
            }
            group.Add(line);
        }
        if (group.Count > 0) AddLines(target, group, o, o.ParagraphMode == TextParagraphMode.BlankLineSeparatedReflow);
    }

    private static List<string> SplitLines(string text)
    {
        var list = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\r' or '\n' or '\u2028' or '\u2029' or '\u0085')
            {
                list.Add(text.Substring(start, i - start));
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                start = i + 1;
            }
        }
        list.Add(text.Substring(start));
        return list;
    }

    private static void AddLines(Document doc, IReadOnlyList<string> lines, TextImportOptions o, bool reflow)
    {
        var p = new Paragraph { StyleId = string.IsNullOrEmpty(o.StyleId) ? "Normal" : o.StyleId };
        var fmt = o.Format ?? TextFormat.None;
        for (int li = 0; li < lines.Count; li++)
        {
            if (li > 0)
            {
                if (reflow) p.Text(" ", fmt);
                else p.Break(BreakType.Line, fmt);
            }
            var line = reflow ? lines[li].Trim() : lines[li];
            var sb = new StringBuilder();
            foreach (char c in line)
            {
                if (c == '\t' && o.TabsAsTabRuns) { Flush(); p.Tab(fmt); continue; }
                if (c == '\f' && o.FormFeedAsPageBreak) { Flush(); p.Break(BreakType.Page, fmt); continue; }
                if (c != '\t' && (c < 0x20 || c is '￾' or '￿' || (c >= 0x7F && c <= 0x9F)))
                {
                    if (!o.RemoveControlCharacters) sb.Append('�');
                    continue;
                }
                sb.Append(c);
            }
            Flush();

            void Flush()
            {
                if (sb.Length == 0) return;
                p.Text(TextSanitize.ValidXml(sb.ToString()), fmt);
                sb.Clear();
            }
        }
        doc.AddParagraph(p);
    }

    /// <summary>The document as plain text.</summary>
    public static string Export(Document document, TextExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        var sw = new StringWriter(CultureInfo.InvariantCulture);
        Export(document, sw, options, cancellationToken);
        return sw.ToString();
    }

    /// <summary>Writes the document as plain text to <paramref name="writer"/>.</summary>
    public static void Export(Document document, TextWriter writer, TextExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        var o = options ?? new TextExportOptions();
        cancellationToken.ThrowIfCancellationRequested();
        var x = ExportWalker.Walk(document, o.Walk(cancellationToken));
        var w = new TextWriterState(writer, o);
        w.Blocks(x.Headers);
        w.Blocks(x.Body);
        w.Blocks(x.Footers);
        if (x.Footnotes.Count + x.Endnotes.Count > 0)
        {
            if (o.NotesSeparator is not null) w.Line(o.NotesSeparator);
            foreach (var n in x.Footnotes) w.Note(n);
            foreach (var n in x.Endnotes) w.Note(n);
        }
    }

    private sealed class TextWriterState
    {
        private readonly TextWriter _w;
        private readonly TextExportOptions _o;
        private bool _first = true;

        public TextWriterState(TextWriter w, TextExportOptions o) { _w = w; _o = o; }

        public void Line(string s)
        {
            if (!_first && _o.BlankLineBetweenParagraphs) _w.Write(_o.NewLine);
            _first = false;
            _w.Write(s);
            _w.Write(_o.NewLine);
        }

        public void Blocks(List<XBlock> blocks)
        {
            foreach (var b in blocks)
            {
                if (b is XParagraph p) Paragraph(p, prefix: "");
                else if (b is XTable t) Table(t);
            }
        }

        public void Note(XNote n)
        {
            bool first = true;
            foreach (var b in n.Blocks)
            {
                if (b is XParagraph p) Paragraph(p, first ? string.Format(CultureInfo.InvariantCulture, _o.NoteReferenceFormat, n.Label) + " " : "");
                else if (b is XTable t) Table(t);
                first = false;
            }
        }

        private void Paragraph(XParagraph p, string prefix)
        {
            if (p.PageBreakBefore && _o.PageBreak is not null && !_first) _w.Write(_o.PageBreak);
            var sb = new StringBuilder(prefix);
            if (p.List is { } li && _o.ListLabels)
            {
                for (int i = 0; i < li.Level; i++) sb.Append(_o.ListIndent);
                if (li.Label.Length > 0) sb.Append(li.Label).Append(_o.ListLabelSeparator);
            }
            Inlines(p.Inlines, sb);
            Line(sb.ToString());
        }

        public string ParagraphText(XParagraph p)
        {
            var sb = new StringBuilder();
            if (p.List is { } li && _o.ListLabels && li.Label.Length > 0) sb.Append(li.Label).Append(_o.ListLabelSeparator);
            Inlines(p.Inlines, sb);
            return sb.ToString().Replace(_o.NewLine, " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        }

        private void Inlines(List<XInline> inlines, StringBuilder sb)
        {
            foreach (var i in inlines)
            {
                switch (i)
                {
                    case XText t: sb.Append(t.Text); break;
                    case XTab: sb.Append(_o.Tab); break;
                    case XBreak br:
                        if (br.Type == BreakType.Page && _o.PageBreak is not null) sb.Append(_o.PageBreak);
                        else sb.Append(_o.NewLine);
                        break;
                    case XLink l: Inlines(l.Inlines, sb); break;
                    case XNoteRef nr: sb.Append(string.Format(CultureInfo.InvariantCulture, _o.NoteReferenceFormat, nr.Note.Label)); break;
                    case XImage img:
                        if (!string.IsNullOrEmpty(img.Image.AltText)) sb.Append('[').Append(img.Image.AltText).Append(']');
                        break;
                }
            }
        }

        private void Table(XTable t)
        {
            foreach (var row in t.Rows)
            {
                var cells = new List<string>();
                foreach (var c in row.Cells)
                {
                    if (c.Covered) { cells.Add(""); continue; }
                    var parts = new List<string>();
                    CellText(c.Blocks, parts);
                    cells.Add(string.Join(_o.TableCellParagraphSeparator, parts));
                    for (int k = 1; k < c.ColumnSpan; k++) cells.Add("");
                }
                Line(string.Join(_o.TableCellSeparator, cells));
            }
        }

        private void CellText(List<XBlock> blocks, List<string> parts)
        {
            foreach (var b in blocks)
            {
                if (b is XParagraph p) parts.Add(ParagraphText(p));
                else if (b is XTable nt)
                    foreach (var r in nt.Rows)
                        foreach (var c in r.Cells) CellText(c.Blocks, parts);
            }
        }
    }
}
