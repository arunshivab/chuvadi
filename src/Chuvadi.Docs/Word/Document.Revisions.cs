using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Docs.Internal;

namespace Chuvadi.Docs.Word;

/// <summary>One tracked change in the document.</summary>
/// <param name="Paragraph">The paragraph the change is in.</param>
/// <param name="Run">The run for run-level changes; null for a paragraph-level change (mark or formatting).</param>
/// <param name="Mark">Who and when, and the kind.</param>
public sealed record RevisionInfo(Paragraph Paragraph, Run? Run, RevisionMark Mark)
{
    /// <summary>For an inserted or deleted table row: the row (<see cref="Paragraph"/> is its first cell's first paragraph).</summary>
    public TableRow? Row { get; init; }

    /// <summary>For a paragraph-level entry: true when it is the paragraph mark's formatting change (<see cref="Word.Paragraph.MarkFormatChange"/>).</summary>
    public bool IsMarkFormatChange { get; init; }
}

public sealed partial class Document
{
    private CommentCollection? _comments;

    /// <summary>The comments (and replies), in the order they are written.</summary>
    public CommentCollection Comments => _comments ??= new CommentCollection(this);

    /// <summary>Word records changes as tracked changes while editing (settings w:trackRevisions).</summary>
    public bool TrackRevisions { get; set; }

    /// <summary>Adds the built-in comment styles when missing.</summary>
    internal void EnsureCommentStyles()
    {
        if (!Styles.Contains("CommentText"))
            Styles.Add(new StyleDefinition("CommentText", StyleType.Paragraph)
            {
                Name = "annotation text", BasedOn = "Normal", UiPriority = 99, UnhideWhenUsed = true,
                RunFormat = new TextFormat { SizePt = 10 },
            });
        if (!Styles.Contains("CommentReference"))
            Styles.Add(new StyleDefinition("CommentReference", StyleType.Character)
            {
                Name = "annotation reference", UiPriority = 99, SemiHidden = true, UnhideWhenUsed = true,
                RunFormat = new TextFormat { SizePt = 8 },
            });
    }

    /// <summary>
    /// Every tracked change in all stories: inserted / deleted runs (a deletion inside an insertion
    /// gives both), run and paragraph formatting changes, paragraph marks and their formatting, and
    /// inserted / deleted table rows (listed before the row's content).
    /// </summary>
    public IReadOnlyList<RevisionInfo> GetRevisions()
    {
        var list = new List<RevisionInfo>();
        var rowOf = new Dictionary<Paragraph, TableRow>(ReferenceEqualityComparer.Instance);
        foreach (var (t, _) in CollectTables())
            foreach (var row in t.Rows)
                if (row.Revision is not null && row.Cells.Count > 0 && row.Cells[0].Paragraphs.Count > 0)
                    rowOf[row.Cells[0].Paragraphs[0]] = row;
        foreach (var p in DocumentSerializer.AllParagraphs(this))
        {
            if (rowOf.Remove(p, out var tr)) list.Add(new RevisionInfo(p, null, tr.Revision!) { Row = tr });
            if (p.FormatChange is { } pf) list.Add(new RevisionInfo(p, null, pf.Mark));
            foreach (var r in p.Runs)
            {
                if (r.InsertedRevision is { } ir) list.Add(new RevisionInfo(p, r, ir));
                if (r.Revision is { } rv) list.Add(new RevisionInfo(p, r, rv));
                if (r.FormatChange is { } fc) list.Add(new RevisionInfo(p, r, fc.Mark));
            }
            if (p.MarkFormatChange is { } mf) list.Add(new RevisionInfo(p, null, mf.Mark) { IsMarkFormatChange = true });
            if (p.MarkRevision is { } mr) list.Add(new RevisionInfo(p, null, mr));
        }
        return list;
    }

    private List<(DocTable, Func<DocTable, bool>)> CollectTables()
    {
        var tables = new List<(DocTable, Func<DocTable, bool>)>();
        ParagraphContainers(tables);
        return tables;
    }

    /// <summary>
    /// Accepts every tracked change: insertions stay, deletions go, formatting changes keep the new
    /// formatting, and a deleted paragraph mark joins its paragraph with the next one.
    /// </summary>
    public void AcceptAllRevisions() => ResolveRevisions(accept: true);

    /// <summary>
    /// Rejects every tracked change: insertions go, deletions stay, formatting returns to the old
    /// formatting, and an inserted paragraph mark joins its paragraph with the next one.
    /// </summary>
    public void RejectAllRevisions() => ResolveRevisions(accept: false);

    private void ResolveRevisions(bool accept)
    {
        var tables = new List<(DocTable Table, Func<DocTable, bool> Remove)>();
        var containers = ParagraphContainers(tables);
        var removedComments = new HashSet<Comment>(ReferenceEqualityComparer.Instance);
        // Whole-row insertions / deletions first; a table left without rows is removed, as Word does.
        foreach (var (t, remove) in tables)
        {
            var gone = new HashSet<TableRow>(ReferenceEqualityComparer.Instance);
            foreach (var row in t.Rows)
            {
                if (row.Revision is not { } rv) continue;
                if (accept ? rv.Type == RevisionType.Delete : rv.Type == RevisionType.Insert) gone.Add(row);
                else row.Revision = null;
            }
            if (gone.Count == 0) continue;
            foreach (var row in gone)
                foreach (var cell in row.Cells)
                    foreach (var p in AllIn(cell.Blocks))
                        foreach (var r in p.Runs)
                            if (r.CommentReference is { } c) removedComments.Add(c);
            PromoteMerges(t, gone);
            t.RemoveRows(gone);
            if (t.Rows.Count == 0) remove(t);
        }
        // Content order is read after the row pass, so a removed table no longer separates paragraphs.
        foreach (var c in containers) ResolveIn(c.List, c.Blocks(), accept, removedComments);
        foreach (var c in removedComments) Comments.Remove(c);
    }

    /// <summary>A removed row that starts a vertical merge hands the start to the next remaining row of the region.</summary>
    private static void PromoteMerges(DocTable t, HashSet<TableRow> gone)
    {
        var rows = t.Rows;
        for (int c = 0; c < t.Columns; c++)
        {
            bool pendingRestart = false;
            foreach (var row in rows)
            {
                if (c >= row.Cells.Count) continue;
                var cell = row.Cells[c];
                if (gone.Contains(row))
                {
                    if (cell.VerticalMerge == VerticalMerge.Restart) pendingRestart = true;
                    else if (cell.VerticalMerge is null) pendingRestart = false;
                    continue;
                }
                if (pendingRestart && cell.VerticalMerge == VerticalMerge.Continue) cell.VerticalMerge = VerticalMerge.Restart;
                pendingRestart = false;
            }
        }
    }

    private static IEnumerable<Paragraph> AllIn(IReadOnlyList<object> blocks)
    {
        foreach (var b in blocks)
        {
            if (b is Paragraph p) yield return p;
            else if (b is DocTable t)
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var q in AllIn(cell.Blocks)) yield return q;
        }
    }

    /// <summary>
    /// Every list that holds paragraphs (the body, cells, headers/footers, notes, comments and text
    /// boxes) with its content order, and every table with a way to remove it from its container.
    /// </summary>
    private List<(IList List, Func<IReadOnlyList<object>> Blocks)> ParagraphContainers(List<(DocTable, Func<DocTable, bool>)> tables)
    {
        var result = new List<(IList, Func<IReadOnlyList<object>>)>();
        var seenBoxes = new HashSet<TextBox>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(object Block, Func<DocTable, bool> Remove)>();
        void Push(IReadOnlyList<object> blocks, Func<DocTable, bool> remove) { foreach (var b in blocks) pending.Push((b, remove)); }

        result.Add((_blocks, () => _blocks));
        Push(_blocks, t => _blocks.Remove(t));
        foreach (var hf in DocumentSerializer.HeaderFooterStories(this))
        {
            result.Add((hf, () => hf.Blocks));
            // A header/footer emptied by removing its table stays blank rather than showing the previous section's.
            Push(hf.Blocks, t => { bool ok = hf.RemoveTable(t); if (ok && hf.Blocks.Count == 0) hf.LinkToPrevious = false; return ok; });
        }
        foreach (var n in Footnotes.Concat(Endnotes)) { result.Add((n.Paragraphs, () => n.Blocks)); Push(n.Blocks, n.RemoveTableInternal); }
        foreach (var c in Comments) { result.Add((c.Paragraphs, () => c.Blocks)); Push(c.Blocks, c.RemoveTableInternal); }
        int guard = 0;
        while (pending.Count > 0 && guard++ < 10_000_000)
        {
            var (item, remove) = pending.Pop();
            if (item is DocTable t)
            {
                tables.Add((t, remove));
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                    {
                        result.Add((cell.Paragraphs, () => cell.Blocks));
                        Push(cell.Blocks, cell.RemoveTableInternal);
                    }
            }
            else if (item is Paragraph p)
            {
                foreach (var r in p.Runs)
                    if (r.TextBox is { } box && seenBoxes.Add(box))
                    {
                        result.Add((box.Paragraphs, () => box.Blocks));
                        Push(box.Blocks, box.RemoveTableInternal);
                    }
            }
        }
        return result;
    }

    /// <summary>
    /// Resolves the changes of one container in a single pass. A paragraph whose mark goes joins
    /// the next block when that is a paragraph (never across a table); chains of joins collect
    /// their runs and are prepended once.
    /// </summary>
    private static void ResolveIn(IList list, IReadOnlyList<object> blocks, bool accept, HashSet<Comment> removedComments)
    {
        // Which paragraphs are directly followed by a paragraph in content order.
        var followedByParagraph = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i + 1 < blocks.Count; i++)
            if (blocks[i] is Paragraph a && blocks[i + 1] is Paragraph) followedByParagraph.Add(a);

        var kept = new List<object>(list.Count);
        List<Run>? carried = null;
        bool changed = false;
        foreach (var item in list)
        {
            if (item is not Paragraph p) { kept.Add(item!); continue; }
            for (int r = p.Runs.Count - 1; r >= 0; r--)
            {
                var run = p.Runs[r];
                bool remove = run.Revision is { } rv && (accept
                    ? rv.Type == RevisionType.Delete
                    : rv.Type == RevisionType.Insert || run.InsertedRevision is not null);
                if (remove)
                {
                    if (run.CommentReference is { } cref) removedComments.Add(cref);
                    p.RemoveRunAt(r);
                    continue;
                }
                if (run.FormatChange is { } fc && !accept) p.ReplaceRun(r, run.WithFormat(fc.OldFormat, fc.OldCharacterStyleId).WithRevision(null, null));
                else if (run.Revision is not null || run.FormatChange is not null) p.ReplaceRun(r, run.WithRevision(null, null));
            }
            if (p.FormatChange is { } pf)
            {
                if (!accept)
                {
                    p.ReplaceFormat(pf.OldFormat);
                    p.StyleId = pf.OldStyleId ?? "Normal";
                }
                p.FormatChange = null;
            }
            if (p.MarkFormatChange is { } mf)
            {
                if (!accept) p.Format.MarkFormat = mf.OldFormat.IsDefault ? null : mf.OldFormat;
                p.MarkFormatChange = null;
            }
            bool joins = false;
            if (p.MarkRevision is { } mr)
            {
                p.MarkRevision = null;
                // The content joins the next paragraph, which keeps its own properties. A section
                // break on the joined mark goes with it: the sections merge and the next section's
                // properties apply, as in Word.
                joins = (accept ? mr.Type == RevisionType.Delete : mr.Type == RevisionType.Insert) && followedByParagraph.Contains(p);
            }
            if (joins)
            {
                // Chains collect their runs once (linear) and go into the paragraph that stays.
                (carried ??= new List<Run>()).AddRange(p.Runs);
                changed = true;
                continue;
            }
            if (carried is not null) { p.PrependRuns(carried); carried = null; }
            kept.Add(p);
        }
        if (carried is not null) // defensive: followedByParagraph guarantees a successor
        {
            var tail = new Paragraph();
            tail.PrependRuns(carried);
            kept.Add(tail);
        }
        if (!changed) return;
        list.Clear();
        foreach (var k in kept) list.Add(k);
    }
}
