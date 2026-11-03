using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Flow
{
    internal sealed partial class FlowEngine
    {
        /// <summary>Length of the footnote separator line (Word's default separator is two inches).</summary>
        internal const double FootnoteSeparatorLength = 144;

        /// <summary>Width of the footnote separator line.</summary>
        internal const double FootnoteSeparatorWidth = 0.5;

        private readonly ConditionalWeakTable<FlowNote, StrongBox<(double Width, double Height)>> _noteHeights = new ConditionalWeakTable<FlowNote, StrongBox<(double, double)>>();

        /// <summary>
        /// The room the footnotes referenced in <paramref name="line"/> add at the foot of the page: the notes not yet on
        /// the page (in <paramref name="plan"/> or <paramref name="pending"/>), plus the separator for the page's first
        /// note. Endnotes take no room here (they are laid out at the end of the document).
        /// </summary>
        private double NewNoteHeight(MeasuredParagraph m, FittedLine line, PagePlan plan, List<FlowNote>? pending, PageRecord record, double width,
            out List<FlowNote>? notes)
        {
            notes = null;
            if (m.Notes is null)
            {
                return 0;
            }
            double h = 0;
            foreach ((int index, FlowNote note) in m.Notes)
            {
                if (index < line.Start || index >= Math.Max(line.End, line.Start + 1) || note.Kind != FlowNoteKind.Footnote)
                {
                    continue;
                }
                if (plan.Footnotes.Contains(note) || (pending?.Contains(note) ?? false) || (notes?.Contains(note) ?? false))
                {
                    continue;
                }
                if (plan.Footnotes.Count == 0 && (pending is null || pending.Count == 0) && notes is null)
                {
                    h += SeparatorHeight(note, width, record);
                }
                (notes ??= new List<FlowNote>()).Add(note);
                h += NoteHeight(note, width, record);
            }
            return h;
        }

        private double NoteHeight(FlowNote note, double width, PageRecord record)
        {
            if (_noteHeights.TryGetValue(note, out StrongBox<(double Width, double Height)>? box) && Math.Abs(box.Value.Width - width) < Epsilon)
            {
                return box.Value.Height;
            }
            double h = LayoutHeaderFooter(note.Blocks, width, record, "footnote", CancellationToken.None).Height;
            _noteHeights.AddOrUpdate(note, new StrongBox<(double, double)>((width, h)));
            return h;
        }

        /// <summary>The separator above the footnotes: one empty line in the first note's paragraph-mark format.</summary>
        private double SeparatorHeight(FlowNote first, double width, PageRecord record)
        {
            FlowParagraph sep = Separator(first);
            return LayoutHeaderFooter(new FlowBlock[] { sep }, width, record, "footnote separator", CancellationToken.None).Height;
        }

        private static FlowParagraph Separator(FlowNote first)
        {
            CharacterFormat mark = first.Blocks.Count > 0 && first.Blocks[0] is FlowParagraph p ? p.MarkFormat : CharacterFormat.Default;
            return new FlowParagraph { MarkFormat = mark };
        }

        /// <summary>
        /// Draws the page's footnotes at the foot of the body, under a separator line. In a multi-column section each
        /// note is drawn in the column of its reference, at that column's width (Word's rule; the standard is silent).
        /// </summary>
        internal void EmitFootnotes(PagePlan plan, PageRecord record, List<LayoutOp> ops, List<LayoutBox> boxes, StringBuilder text, CancellationToken ct)
        {
            if (plan.Footnotes.Count == 0)
            {
                return;
            }
            (double pageLeft, double pageWidth) = TextArea(plan.Geometry, record.PageNumber);
            List<((double Left, double Width) Area, List<FlowNote> Notes)> groups = new List<((double, double), List<FlowNote>)>();
            for (int i = 0; i < plan.Footnotes.Count; i++)
            {
                (double Left, double Width) area = i < plan.FootnoteAreas.Count && plan.FootnoteAreas[i] is (double, double) a ? a : (pageLeft, pageWidth);
                int g = groups.FindIndex(x => Math.Abs(x.Area.Left - area.Left) < Epsilon && Math.Abs(x.Area.Width - area.Width) < Epsilon);
                if (g < 0)
                {
                    groups.Add((area, new List<FlowNote>()));
                    g = groups.Count - 1;
                }
                groups[g].Notes.Add(plan.Footnotes[i]);
            }
            foreach (((double left, double width), List<FlowNote> notes) in groups)
            {
                EmitFootnoteGroup(plan, record, notes, left, width, ops, boxes, text, ct);
            }
        }

        private void EmitFootnoteGroup(PagePlan plan, PageRecord record, List<FlowNote> notes, double left, double width, List<LayoutOp> ops,
            List<LayoutBox> boxes, StringBuilder text, CancellationToken ct)
        {
            double separator = SeparatorHeight(notes[0], width, record);
            List<HeaderFooterLayout> layouts = new List<HeaderFooterLayout>();
            double total = separator;
            foreach (FlowNote note in notes)
            {
                HeaderFooterLayout hf = LayoutHeaderFooter(note.Blocks, width, record, "footnote", ct);
                layouts.Add(hf);
                total += hf.Height;
            }
            double top = plan.FootnoteBottom - total;
            double lineY = top + (separator / 2);
            ops.Add(new StrokeLineOp(left, lineY, left + Math.Min(FootnoteSeparatorLength, width), lineY, FootnoteSeparatorWidth, LayoutColor.Black));
            double y = top + separator;
            text.Append('\n');
            foreach (HeaderFooterLayout hf in layouts)
            {
                EmitHeaderFooter(hf, left, width, y, ops, text, "footnote", ct);
                y += hf.Height;
            }
            boxes.Add(new LayoutBox(LayoutBoxKind.Footnotes, new LayoutRect(left, top, width, total), "footnotes"));
        }

        /// <summary>Height a text box needs for its content and insets.</summary>
        private double TextBoxNeeds(FlowTextBox box, double width, PageRecord record)
        {
            double inner = Math.Max(1, width - box.InsetLeft - box.InsetRight);
            return LayoutHeaderFooter(box.Blocks, inner, record, "text box", CancellationToken.None).Height + box.InsetTop + box.InsetBottom;
        }

        /// <summary>A floating text box: fill, content inside the insets (clipped to the box), outline.</summary>
        internal void EmitTextBox(FlowTextBox box, LayoutRect r, PageRecord record, List<LayoutOp> ops, StringBuilder text, string path, CancellationToken ct)
        {
            FlowEmitter.EmitTextBoxFrame(box, r, ops, outline: false);
            double inner = Math.Max(1, r.Width - box.InsetLeft - box.InsetRight);
            HeaderFooterLayout hf = LayoutHeaderFooter(box.Blocks, inner, record, path, ct);
            double top = FlowEmitter.TextBoxContentTop(box, r, hf.Height);
            ops.Add(new PushClipOp(r));
            EmitHeaderFooter(hf, r.X + box.InsetLeft, inner, top, ops, text, path, ct);
            ops.Add(PopClipOp.Instance);
            FlowEmitter.EmitTextBoxFrame(box, r, ops, outline: true);
        }
    }
}
