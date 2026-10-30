using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Internal;

namespace Chuvadi.Docs.Word;

public sealed partial class Document
{
    /// <summary>The footnotes, in the order they are written (refer to one with <see cref="Paragraph.NoteReference(Note, TextFormat?)"/>).</summary>
    public NoteCollection Footnotes { get; }

    /// <summary>The endnotes, in the order they are written.</summary>
    public NoteCollection Endnotes { get; }

    /// <summary>Document-wide footnote numbering and placement (settings w:footnotePr); a section's own values win.</summary>
    public NoteProperties FootnoteProperties { get; } = new();

    /// <summary>Document-wide endnote numbering and placement (settings w:endnotePr).</summary>
    public NoteProperties EndnoteProperties { get; } = new();

    /// <summary>Title, author, company, custom properties… (docProps).</summary>
    public DocumentProperties Properties { get; } = new();

    /// <summary>
    /// Ask Word to update all fields when the document is opened (settings w:updateFields) — e.g.
    /// to build a table of contents with page numbers. Word asks the user first.
    /// </summary>
    public bool UpdateFieldsOnOpen { get; set; }

    /// <summary>Embed TrueType fonts when Word saves the file (settings w:embedTrueTypeFonts).</summary>
    public bool EmbedTrueTypeFonts { get; set; }

    /// <summary>Embed only the characters used (settings w:saveSubsetFonts).</summary>
    public bool SaveSubsetFonts { get; set; }

    /// <summary>
    /// The font table (word/fontTable.xml): fonts the document uses with their substitution hints
    /// and embedded font files. Empty for new documents (nothing is written); loaded documents
    /// keep the file's table.
    /// </summary>
    public List<FontInfo> Fonts { get; } = new();

    /// <summary>
    /// Embeds a font file (TrueType / OpenType bytes) for <paramref name="fontName"/> so the document
    /// shows it where it is not installed; turns on <see cref="EmbedTrueTypeFonts"/>. Check the font's
    /// licence allows embedding.
    /// </summary>
    public FontInfo EmbedFont(string fontName, byte[] fontData, EmbeddedFontStyle style = EmbeddedFontStyle.Regular)
    {
        if (fontData is null || fontData.Length < 32) throw new ArgumentException("Font data required (at least 32 bytes).", nameof(fontData));
        var info = Fonts.Find(f => string.Equals(f.Name, fontName, StringComparison.OrdinalIgnoreCase));
        if (info is null) Fonts.Add(info = new FontInfo(fontName));
        info.Embedded.RemoveAll(e => e.Style == style);
        info.Embedded.Add(new EmbeddedFont(style, fontData));
        EmbedTrueTypeFonts = true;
        return info;
    }

    /// <summary>Adds the built-in note text and reference styles when missing (the default look of Word's notes).</summary>
    internal void EnsureNoteStyles(NoteKind kind)
    {
        string text = kind == NoteKind.Footnote ? "FootnoteText" : "EndnoteText";
        string reference = kind == NoteKind.Footnote ? "FootnoteReference" : "EndnoteReference";
        string label = kind == NoteKind.Footnote ? "footnote" : "endnote";
        if (!Styles.Contains(text))
        {
            var s = new StyleDefinition(text, StyleType.Paragraph)
            {
                Name = label + " text", BasedOn = "Normal", UiPriority = 99, SemiHidden = true, UnhideWhenUsed = true,
                RunFormat = new TextFormat { SizePt = 10 },
            };
            s.ParagraphFormat.SpaceAfterPt = 0;
            s.ParagraphFormat.LineSpacing = LineSpacing.Multiple(1);
            Styles.Add(s);
        }
        if (!Styles.Contains(reference))
            Styles.Add(new StyleDefinition(reference, StyleType.Character)
            {
                Name = label + " reference", UiPriority = 99, SemiHidden = true, UnhideWhenUsed = true,
                RunFormat = new TextFormat { VerticalPosition = VerticalTextPosition.Superscript },
            });
    }

    /// <summary>Every bookmark in the document (body, headers/footers, notes), in order of their start.</summary>
    public IReadOnlyList<Bookmark> GetBookmarks()
    {
        var seen = new HashSet<Bookmark>(ReferenceEqualityComparer.Instance);
        var list = new List<Bookmark>();
        foreach (var p in DocumentSerializer.AllParagraphs(this))
            foreach (var r in p.Runs)
                if (r.Bookmark is { IsStart: true } m && seen.Add(m.Bookmark)) list.Add(m.Bookmark);
        return list;
    }

    /// <summary>
    /// Every footnote and endnote reference in the body (tables included), in document order,
    /// with the number Word shows: counting continues through the document or restarts per
    /// section as <see cref="NoteProperties.Restart"/> says (section values over document values),
    /// from <see cref="NoteProperties.StartAt"/>, in <see cref="NoteProperties.NumberFormat"/>
    /// (footnotes decimal, endnotes lower roman by default). A custom mark is shown as its text.
    /// When numbering restarts on each page, <see cref="NoteReferenceInfo.RestartsEachPage"/> is
    /// true and a paginator renumbers per page.
    /// </summary>
    public IReadOnlyList<NoteReferenceInfo> GetNoteReferences()
    {
        var result = new List<NoteReferenceInfo>();
        var sections = Sections;
        int sectionIndex = 0;
        var counters = new Dictionary<NoteKind, int>();
        Section? counted = null;
        void FromParagraph(Paragraph p, Section section)
        {
            if (!ReferenceEquals(section, counted))
            {
                foreach (var kind in new[] { NoteKind.Footnote, NoteKind.Endnote })
                    if (counted is null || EffectiveNoteProperty(section, kind, n => n.Restart) == NoteRestart.EachSection)
                        counters[kind] = (EffectiveNoteProperty(section, kind, n => n.StartAt) ?? 1) - 1;
                counted = section;
            }
            for (int i = 0; i < p.Runs.Count; i++)
            {
                if (p.Runs[i].NoteReference is not { } note) continue;
                var kind = note.Kind;
                string label;
                int number;
                if (p.Runs[i].CustomMarkFollows)
                {
                    number = counters[kind];
                    label = i + 1 < p.Runs.Count ? p.Runs[i + 1].TextContent : "";
                }
                else
                {
                    number = ++counters[kind];
                    var format = EffectiveNoteProperty(section, kind, n => n.NumberFormat)
                                 ?? (kind == NoteKind.Footnote ? NumberFormat.Decimal : NumberFormat.LowerRoman);
                    label = NumberFormatter.Format(number, format);
                }
                bool eachPage = kind == NoteKind.Footnote && EffectiveNoteProperty(section, kind, n => n.Restart) == NoteRestart.EachPage;
                result.Add(new NoteReferenceInfo(p, i, note, number, label, section, eachPage));
            }
        }
        foreach (var b in _blocks)
        {
            var section = sections[Math.Min(sectionIndex, sections.Count - 1)];
            if (b is Paragraph p)
            {
                FromParagraph(p, section);
                if (p.SectionProperties is not null) sectionIndex++;
            }
            else if (b is DocTable t)
                foreach (var tp in DocumentSerializer.TableParagraphs(t, 0)) FromParagraph(tp, section);
        }
        return result;
    }

    private T? EffectiveNoteProperty<T>(Section section, NoteKind kind, Func<NoteProperties, T?> get) where T : struct
        => get(kind == NoteKind.Footnote ? section.FootnoteProperties : section.EndnoteProperties)
           ?? get(kind == NoteKind.Footnote ? FootnoteProperties : EndnoteProperties);

    /// <summary>
    /// Adds a table of contents: an optional title paragraph ("TOC Heading" style) and a TOC
    /// field over heading levels <paramref name="fromLevel"/>–<paramref name="toLevel"/> with
    /// links. The field is marked dirty and <see cref="UpdateFieldsOnOpen"/> is switched on, so
    /// Word builds the entries with page numbers when the document is opened; until then
    /// <paramref name="placeholder"/> shows. Returns the field.
    /// </summary>
    public Field AddTableOfContents(int fromLevel = 1, int toLevel = 3, string? title = "Contents",
        string placeholder = "Right-click and choose Update Field to build the table of contents.")
    {
        if (fromLevel < 1 || toLevel > 9 || fromLevel > toLevel) throw new ArgumentOutOfRangeException(nameof(fromLevel), "Levels are 1–9, from ≤ to.");
        if (title is not null)
        {
            if (!Styles.Contains("TOCHeading"))
                Styles.Add(new StyleDefinition("TOCHeading", StyleType.Paragraph)
                {
                    Name = "TOC Heading", BasedOn = Styles.Contains("Heading1") ? "Heading1" : "Normal", Next = "Normal",
                    UiPriority = 39, UnhideWhenUsed = true, QuickFormat = true,
                });
            AddParagraph(new Paragraph(title) { StyleId = "TOCHeading" });
        }
        var field = new Field($" TOC \\o \"{fromLevel}-{toLevel}\" \\h \\z \\u ") { Dirty = true };
        var p = new Paragraph();
        p.Field(field, placeholder);
        AddParagraph(p);
        UpdateFieldsOnOpen = true;
        return field;
    }

    // =====================================================================================
    // Append
    // =====================================================================================

    /// <summary>
    /// Appends a copy of <paramref name="source"/>'s body to this document: paragraphs, tables,
    /// images, lists (with new ids, counting on their own), footnotes and endnotes, bookmarks
    /// (renamed when a name is already used here, with the links and REF / PAGEREF fields that
    /// point to them), and the styles the content uses that this document lacks. The source is
    /// not changed (it is copied through a save and load). See <see cref="AppendOptions"/> for
    /// section and style handling.
    /// </summary>
    public Document Append(Document source, AppendOptions? options = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        options ??= new AppendOptions();
        if (options.StartSection is SectionStart st && !Enum.IsDefined(st)) throw new ArgumentOutOfRangeException(nameof(options));
        Document copy;
        using (var ms = new MemoryStream())
        {
            source.SaveTo(ms);
            ms.Position = 0;
            copy = Load(ms, null, DocxReaderOptions.CreateUnrestricted(), System.Threading.CancellationToken.None);
        }
        var paragraphs = DocumentSerializer.AllParagraphs(copy).ToList();

        // ---- bookmarks: names unique in the combined document ----
        var names = new HashSet<string>(GetBookmarks().Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
        var renamedBookmarks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in copy.GetBookmarks())
        {
            if (!names.Contains(b.Name)) { names.Add(b.Name); continue; }
            var old = b.Name;
            int n = 2;
            string candidate;
            do
            {
                var suffix = "_" + n++;
                candidate = (old.Length + suffix.Length > 40 ? old[..(40 - suffix.Length)] : old) + suffix;
            }
            while (names.Contains(candidate));
            b.SetNameUnchecked(candidate);
            names.Add(candidate);
            renamedBookmarks[old] = candidate;
        }

        // ---- styles (computed on the copy; the destination changes only after every check) ----
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in paragraphs)
        {
            used.Add(p.StyleId);
            foreach (var r in p.Runs) if (r.CharacterStyleId is { } c) used.Add(c);
        }
        foreach (var t in AllTables(copy)) if (t.Format.StyleId is { } ts) used.Add(ts);
        foreach (var d in copy.Numbering.Definitions)
        {
            if (d.StyleLink is { } sl) used.Add(sl);
            if (d.NumStyleLink is { } nl) used.Add(nl);
            foreach (var l in d.Levels)
            {
                if (l.ParagraphStyleId is { } lp) used.Add(lp);
                if (l.RunStyleId is { } lr) used.Add(lr);
            }
        }
        var pending = new Queue<string>(used);
        var closure = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!closure.Add(id) || !copy.Styles.TryGet(id, out var s)) continue;
            if (s.BasedOn is not null) pending.Enqueue(s.BasedOn);
            if (s.Next is not null) pending.Enqueue(s.Next);
            if (s.Link is not null) pending.Enqueue(s.Link);
        }
        var rename = new Dictionary<string, string>(StringComparer.Ordinal);
        if (options.KeepSourceStyles)
            foreach (var id in closure)
                if (copy.Styles.Contains(id) && Styles.Contains(id) && id != "Normal")
                {
                    int n = 2;
                    string newId;
                    do newId = id + "_" + n++; while (Styles.Contains(newId) || copy.Styles.Contains(newId));
                    rename[id] = newId;
                }
        string Map(string id) => rename.TryGetValue(id, out var m) ? m : id;
        var stylesToAdd = new List<StyleDefinition>();
        foreach (var id in closure)
        {
            if (!copy.Styles.TryGet(id, out var s)) continue;
            if (rename.TryGetValue(id, out var newId))
            {
                var c = s.CloneAs(newId);
                if (c.BasedOn is not null) c.BasedOn = Map(c.BasedOn);
                if (c.Next is not null) c.Next = Map(c.Next);
                if (c.Link is not null) c.Link = Map(c.Link);
                c.Name = (s.Name ?? id) + " " + newId[(id.Length + 1)..];
                stylesToAdd.Add(c);
            }
            else if (!Styles.Contains(id)) stylesToAdd.Add(s);
        }

        // ---- remap paragraphs and runs ----
        foreach (var p in paragraphs)
        {
            if (rename.Count > 0) p.StyleId = Map(p.StyleId);
            for (int i = 0; i < p.Runs.Count; i++)
            {
                var r = p.Runs[i];
                var style = r.CharacterStyleId is { } cs && rename.Count > 0 ? Map(cs) : r.CharacterStyleId;
                var anchor = r.HyperlinkAnchor is { } a && renamedBookmarks.TryGetValue(a, out var na) ? na : r.HyperlinkAnchor;
                if (!ReferenceEquals(style, r.CharacterStyleId) || !ReferenceEquals(anchor, r.HyperlinkAnchor))
                    p.ReplaceRun(i, r.CloneWith(style, anchor));
                if (r.FieldMark is { Type: FieldMarkType.Begin } fm && renamedBookmarks.Count > 0 && fm.Field.InstructionParts.Count == 1)
                {
                    var renamedInstr = RenameFieldBookmark(fm.Field.Instruction, renamedBookmarks);
                    if (renamedInstr != fm.Field.Instruction) fm.Field.Instruction = renamedInstr;
                }
            }
        }
        if (rename.Count > 0)
        {
            foreach (var t in AllTables(copy)) if (t.Format.StyleId is { } ts) t.Format.StyleId = Map(ts);
            foreach (var d in copy.Numbering.Definitions)
            {
                if (d.StyleLink is { } sl) d.StyleLink = Map(sl);
                if (d.NumStyleLink is { } nl) d.NumStyleLink = Map(nl);
                foreach (var l in d.Levels)
                {
                    if (l.ParagraphStyleId is { } lp) l.ParagraphStyleId = Map(lp);
                    if (l.RunStyleId is { } lr) l.RunStyleId = Map(lr);
                }
            }
        }

        // ---- now change the destination: styles, lists, notes, fonts ----
        foreach (var st2 in stylesToAdd) if (!Styles.Contains(st2.StyleId)) Styles.Add(st2);
        Numbering.AdoptAll(copy.Numbering);
        foreach (var note in copy.Footnotes) Footnotes.AddMoved(note);
        foreach (var note in copy.Endnotes) Endnotes.AddMoved(note);
        foreach (var comment in copy.Comments) Comments.AddMoved(comment);
        if (copy.Comments.Count > 0) EnsureCommentStyles();
        if (copy.Footnotes.Count > 0) EnsureNoteStyles(NoteKind.Footnote);
        if (copy.Endnotes.Count > 0) EnsureNoteStyles(NoteKind.Endnote);
        foreach (var font in copy.Fonts)
            if (!Fonts.Exists(f => string.Equals(f.Name, font.Name, StringComparison.OrdinalIgnoreCase))) Fonts.Add(font);
        EmbedTrueTypeFonts |= copy.EmbedTrueTypeFonts;
        UpdateFieldsOnOpen |= copy.UpdateFieldsOnOpen;

        // ---- sections and blocks ----
        if (options.StartSection is SectionStart start)
        {
            AddSectionBreak(start);
            if (options.KeepSourceLayout)
            {
                copy.Section.Start = start;
                SetFinalSection(copy.Section);
            }
        }
        foreach (var block in copy.Blocks) AddBlock(block);
        return this;
    }

    private static IEnumerable<DocTable> AllTables(Document doc)
    {
        var stack = new Stack<DocTable>();
        void Push(IEnumerable<object> blocks) { foreach (var b in blocks) if (b is DocTable t) stack.Push(t); }
        Push(doc.Blocks);
        foreach (var hf in DocumentSerializer.HeaderFooterStories(doc)) Push(hf.Blocks);
        foreach (var n in doc.Footnotes.Concat(doc.Endnotes)) Push(n.Blocks);
        int guard = 0;
        while (stack.Count > 0 && guard++ < 1_000_000)
        {
            var t = stack.Pop();
            yield return t;
            foreach (var row in t.Rows)
                foreach (var cell in row.Cells)
                    Push(cell.Blocks);
        }
    }

    /// <summary>REF / PAGEREF / NOTEREF instructions and HYPERLINK \l whose bookmark was renamed get the new name.</summary>
    private static string RenameFieldBookmark(string instruction, Dictionary<string, string> renamed)
    {
        var f = new Field(instruction);
        if (f.FieldType == "HYPERLINK")
        {
            int l = instruction.IndexOf("\\l", StringComparison.Ordinal);
            if (l < 0) return instruction;
            int q1 = instruction.IndexOf('"', l), q2 = q1 < 0 ? -1 : instruction.IndexOf('"', q1 + 1);
            if (q2 < 0) return instruction;
            var target = instruction.Substring(q1 + 1, q2 - q1 - 1);
            return renamed.TryGetValue(target, out var nn) ? instruction[..(q1 + 1)] + nn + instruction[q2..] : instruction;
        }
        if (f.FieldType is not ("REF" or "PAGEREF" or "NOTEREF") || f.Argument is not { } arg || !renamed.TryGetValue(arg, out var newName))
            return instruction;
        int at = instruction.IndexOf(arg, instruction.IndexOf(f.FieldType == "REF" ? "REF" : f.FieldType, StringComparison.OrdinalIgnoreCase) + f.FieldType.Length, StringComparison.Ordinal);
        return at < 0 ? instruction : instruction[..at] + newName + instruction[(at + arg.Length)..];
    }
}
