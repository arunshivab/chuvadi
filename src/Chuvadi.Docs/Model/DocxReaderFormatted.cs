using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml;
using Chuvadi.Docs.Internal;

namespace Chuvadi.Docs.Model;

/// <summary>
/// Formatted reading on <see cref="DocxReader"/> (Docs 2.2.0, D-336): paragraphs with their runs and the effective run
/// and paragraph formatting, streamed one paragraph (or one top-level table) at a time, with the styles, numbering,
/// theme and settings read once per reader.
/// </summary>
public sealed partial class DocxReader
{
    private Document? _formatDoc;
    private readonly FormatResolver.RunStyleCache _runStyleCache = new();

    /// <summary>
    /// Streams every paragraph with its runs and their effective formatting: the body in document order, with the
    /// paragraphs of table cells (nested tables included) in place, then the headers, then the footers (when
    /// <see cref="DocxReaderOptions.IncludeHeadersAndFooters"/> is true). Each item is a <see cref="DocxFormattedParagraph"/>.
    /// <para>
    /// The runs and formats are the ones <see cref="Document.Load(Stream, string?)"/> and
    /// <see cref="Document.GetEffectiveRunFormat(Paragraph, Run)"/> give, from the same code. Only one paragraph, or one
    /// top-level table, is held at a time. The styles, numbering, theme and settings parts are read on the first call
    /// and kept for the reader's life; their elements count towards that call's <see cref="DocxReaderOptions.MaxElements"/>.
    /// Footnotes, endnotes and comments are not read, so their references are not in the runs.
    /// </para>
    /// <para>
    /// A styles, numbering, theme or settings part that is missing, cannot be opened or is not well-formed XML is listed
    /// in <see cref="SkippedParts"/> and the formatting falls back to the defaults without it (unlike <c>Document.Load</c>,
    /// which rejects malformed styles or numbering). Headers and footers read as in <see cref="Paragraphs()"/>: a broken
    /// one reads as blank and is listed. Observes the token given to
    /// <see cref="Open(Stream, string?, DocxReaderOptions?, CancellationToken)"/>; throws while being enumerated.
    /// </para>
    /// </summary>
    public IEnumerable<DocxFormattedParagraph> FormattedParagraphs() => FormattedParagraphs(CancellationToken.None);

    /// <summary>
    /// As <see cref="FormattedParagraphs()"/>, observing <paramref name="cancellationToken"/> and the token given to
    /// <see cref="Open(Stream, string?, DocxReaderOptions?, CancellationToken)"/> while enumerating.
    /// </summary>
    public IEnumerable<DocxFormattedParagraph> FormattedParagraphs(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        return DocxErrors.Guard(FormattedParagraphsIterator(cancellationToken));
    }

    /// <summary>
    /// The font name a theme font reference stands for, as <see cref="Document.GetThemeFontName(ThemeFont)"/>, from this
    /// file's theme and settings (read once, on first use). Null when the theme does not name one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="font"/> is not a defined value.</exception>
    public string? GetThemeFontName(ThemeFont font)
    {
        EnsureNotDisposed();
        if (!Enum.IsDefined(font)) throw new ArgumentOutOfRangeException(nameof(font), font, "Not a defined ThemeFont value.");
        return FormatDocument().GetThemeFontName(font);
    }

    /// <summary>
    /// The colour a theme colour reference stands for, as six-digit hex, as <see cref="Document.GetThemeColorHex(ThemeColor, byte?, byte?)"/>,
    /// from this file's theme and settings (read once, on first use).
    /// </summary>
    public string GetThemeColorHex(ThemeColor color, byte? tint = null, byte? shade = null)
    {
        EnsureNotDisposed();
        return FormatDocument().GetThemeColorHex(color, tint, shade);
    }

    /// <summary>The formatting document for the theme methods, read with the Open token and its own element count.</summary>
    private Document FormatDocument()
    {
        if (_formatDoc is { } ready) return ready;
        using var read = ReadToken.Combine(_openToken, CancellationToken.None);
        try
        {
            return FormatDocument(read.Token, _options.NewBudget());
        }
        catch (Exception ex) when (!DocxErrors.IsContract(ex))
        {
            throw DocxErrors.Wrap(ex, DocxErrors.ReadMessage);
        }
    }

    private IEnumerable<DocxFormattedParagraph> FormattedParagraphsIterator(CancellationToken callerToken)
    {
        // Each enumeration is one operation: its own combined token and element count.
        using var read = ReadToken.Combine(_openToken, callerToken);
        var ct = read.Token;
        var budget = _options.NewBudget();
        var styles = FormatDocument(ct, budget);
        foreach (var uri in ReadOrder())
        {
            var items = IsMainPart(uri) ? FormattedFromPart(uri, styles, ct, budget, story: false)
                : ReadOptionalPart(uri, u => FormattedFromPart(u, styles, ct, budget, story: true));
            foreach (var item in items)
                yield return item;
        }
    }

    /// <summary>
    /// The styles-only document the resolver works on: settings, theme, numbering and styles, read as
    /// <c>Document.Load</c> reads them (same readers, same linking), kept once complete.
    /// </summary>
    private Document FormatDocument(CancellationToken ct, ElementBudget? budget)
    {
        if (_formatDoc is { } ready) return ready;

        byte[]? theme = null;
        string? themeUri = RelatedPart("/theme");
        if (themeUri is not null && TryOpenPart(themeUri, ct) is { } ts)
        {
            using (ts)
            using (var ms = new MemoryStream())
            {
                ts.CopyTo(ms);
                theme = ms.ToArray();
            }
        }

        Document Fresh()
        {
            var d = new Document(DocumentPreset.Minimal); // the file's own paragraph defaults, as Load
            d.ClearCompatibilityForLoad();
            d.ThemeXml = theme;
            return d;
        }

        var doc = Fresh();
        string? numberingUri = RelatedPart("/numbering");
        string? stylesUri = RelatedPart("/styles");
        string? settingsUri = RelatedPart("/settings");

        // Numbering, then styles (Load's order). A part that is not well-formed is listed and left out: the
        // document is rebuilt without it, so nothing half-read stays.
        bool numberingOk = numberingUri is not null && ReadFormatPart(numberingUri, ct, budget, r => NumberingXml.Read(r, doc.Numbering));
        if (numberingUri is not null && !numberingOk) doc = Fresh();
        if (stylesUri is not null && !ReadFormatPart(stylesUri, ct, budget, r => StylesXml.Read(r, doc)))
        {
            doc = Fresh();
            if (numberingOk) ReadFormatPart(numberingUri!, ct, budget, r => NumberingXml.Read(r, doc.Numbering));
        }
        if (settingsUri is not null)
        {
            var target = doc;
            if (!ReadFormatPart(settingsUri, ct, budget, r => DocumentLoader.ReadSettings(r, target)))
            {
                // Settings carry only flags; a broken part leaves the defaults, as in Load. Rebuild what was read.
                doc = Fresh();
                if (numberingOk) ReadFormatPart(numberingUri!, ct, budget, r => NumberingXml.Read(r, doc.Numbering));
                if (stylesUri is not null) ReadFormatPart(stylesUri, ct, budget, r => StylesXml.Read(r, doc));
            }
        }
        DocumentLoader.LinkStyleNumbering(doc);
        _formatDoc = doc;
        return doc;
    }

    /// <summary>The first internal part of the main part's relationships whose type ends with <paramref name="typeSuffix"/>.</summary>
    private string? RelatedPart(string typeSuffix)
    {
        foreach (var rel in _package.GetRelationships(_documentUri))
            if (!rel.IsExternal && rel.Type.EndsWith(typeSuffix, StringComparison.Ordinal))
                return DocumentLoader.Resolve(_documentUri, rel.Target);
        return null;
    }

    /// <summary>Reads one formatting part; false (and the skip listed) when it is missing, unreadable or not well-formed.</summary>
    private bool ReadFormatPart(string uri, CancellationToken ct, ElementBudget? budget, Action<XmlReader> read)
    {
        var s = TryOpenPart(uri, ct);
        if (s is null) return false;
        using (s)
        using (var r = CreateXml(s, uri, ct, budget))
        {
            try
            {
                read(r);
                DocumentLoader.EnsureWellFormed(r, uri);
                return true;
            }
            catch (XmlException ex)
            {
                RecordSkip(uri, DocxSkippedPartReason.NotWellFormed, ex);
                return false;
            }
        }
    }

    /// <summary>
    /// One part's paragraphs, built with Load's own paragraph and table loaders. Each top-level block is resolved and
    /// yielded only after the next one is read (and after the part ends), because Load attaches bookmark marks that
    /// follow a block to its last paragraph; so every item is final when the caller sees it.
    /// </summary>
    private IEnumerable<DocxFormattedParagraph> FormattedFromPart(string uri, Document styles, CancellationToken ct,
        ElementBudget? budget, bool story)
    {
        Stream? s = TryOpenPart(uri, ct);
        if (s is null) yield break;
        using var _ = s;
        using var r = CreateXml(s, uri, ct, budget);

        // Pictures are read as Load reads them, but only kept while their paragraph is pending.
        var media = new Dictionary<string, (byte[] Bytes, string ContentType)?>(StringComparer.Ordinal);
        var images = new DocumentLoader.ImageSource(_package, uri, _options, ct, media, _skipped);
        var context = new DocumentLoader.StoryContext(new DocumentLoader.LoadContext(_skipped), images) { NoNoteReferences = story };
        var pending = new List<(Paragraph Paragraph, TableCellRef? Cell)>();

        if (!r.Read()) yield break;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == DocumentSerializer.W)
            {
                if (r.LocalName == "tbl")
                {
                    DocTable table;
                    using (var sub = r.LightSubtree()) table = DocumentLoader.LoadTable(sub, context);
                    r.Read();
                    foreach (var item in Flush(pending, styles, uri, _runStyleCache)) yield return item;
                    media.Clear();
                    CollectTable(table, pending, 0);
                    continue;
                }
                if (r.LocalName is "bookmarkStart" or "bookmarkEnd" or "commentRangeStart" or "commentRangeEnd")
                {
                    if (DocumentLoader.BookmarkMarker(r, context) is { } marker) context.Pending.Add(marker);
                    if (!r.Read()) break;
                    continue;
                }
                if (r.LocalName == "sectPr")
                {
                    r.Skip();
                    continue;
                }
                if (r.LocalName == "p")
                {
                    Paragraph p;
                    using (var sub = r.LightSubtree()) p = DocumentLoader.LoadParagraph(sub, context);
                    r.Read();
                    foreach (var item in Flush(pending, styles, uri, _runStyleCache)) yield return item;
                    media.Clear();
                    pending.Add((p, null));
                    continue;
                }
            }
            if (!r.Read()) break;
        }
        DocumentLoader.EnsureWellFormed(r, uri);
        context.Finish();
        foreach (var item in Flush(pending, styles, uri, _runStyleCache)) yield return item;
    }

    /// <summary>A table's paragraphs in reading order: rows, then the cells that start in each row, then each cell's blocks.</summary>
    private static void CollectTable(DocTable table, List<(Paragraph, TableCellRef?)> into, int depth)
    {
        if (depth > 512) return; // far beyond what MaxXmlDepth admits
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (int c = 0; c < row.Cells.Count; c++)
            {
                if (row.CellIfPresent(c) is not { } cell) continue; // a grid slot no cell starts in
                foreach (var b in cell.Blocks)
                {
                    if (b is Paragraph p) into.Add((p, new TableCellRef(table, r, c)));
                    else if (b is DocTable nested) CollectTable(nested, into, depth + 1);
                }
            }
        }
    }

    private static bool HasTextBox(Paragraph p)
    {
        foreach (var r in p.Runs) if (r.TextBox is not null) return true;
        return false;
    }

    /// <summary>Links numbering and resolves the effective formats of the pending paragraphs, in order, then clears them.</summary>
    private static List<DocxFormattedParagraph> Flush(List<(Paragraph Paragraph, TableCellRef? Cell)> pending, Document styles, string part,
        FormatResolver.RunStyleCache cache)
    {
        var result = new List<DocxFormattedParagraph>(pending.Count);
        foreach (var (p, cell) in pending)
        {
            DocumentLoader.LinkParagraphNumbering(styles, p);
            if (HasTextBox(p))
                foreach (var inner in DocumentSerializer.TextBoxParagraphs(p, 0, new HashSet<TextBox>()))
                    DocumentLoader.LinkParagraphNumbering(styles, inner);
            var formats = new TextFormat[p.Runs.Count];
            var key = formats.Length > 0 ? FormatResolver.ParagraphKey(styles, p, cell) : "";
            for (int i = 0; i < formats.Length; i++) formats[i] = FormatResolver.ResolveRun(styles, p, p.Runs[i], cell, cache, key);
            result.Add(new DocxFormattedParagraph(p, formats, FormatResolver.ResolveParagraph(styles, p, cell), part, cell));
        }
        pending.Clear();
        return result;
    }
}
