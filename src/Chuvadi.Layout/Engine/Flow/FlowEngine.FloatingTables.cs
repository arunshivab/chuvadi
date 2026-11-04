using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Flow
{
    /// <summary>What placing a floating table did.</summary>
    /// <param name="Placed">Some of the table is on the page.</param>
    /// <param name="Bottom">Bottom of the part placed.</param>
    /// <param name="ColumnEnds">The table continues on the next column or page (a table anchored to its paragraph that is
    /// taller than the space left); the flow stops here.</param>
    /// <param name="Next">Where the flow continues: the block after the table, or the table's remaining rows.</param>
    /// <param name="Late">A table positioned on the page or margin that reaches above its paragraph: lay the page out again
    /// with it placed first.</param>
    internal readonly record struct FloatingTableResult(bool Placed, double Bottom, bool ColumnEnds, FlowPosition Next, object? Late);

    internal sealed partial class FlowEngine
    {
        /// <summary>
        /// Places a floating table (w:tblpPr) at the position the file states (ECMA-376 17.4.57) and makes text flow around
        /// it on both sides, at least the stated distances away. The flow position does not move: the text that follows
        /// starts beside the table. Where the standard is silent, Word 2021 as measured with calib-floating.docx:
        /// <list type="bullet">
        /// <item>a table positioned relative to its paragraph that is taller than the space left continues at the top of
        /// the next page (or column), at the same horizontal position, with the text beside it there (T5);</item>
        /// <item>a table positioned on the page or margin is placed exactly there, whole (Arun's decision A: not moved and
        /// not split, even past the bottom margin); when it reaches above its paragraph the page is laid out again with
        /// it placed first, so the text above flows around it too (T3);</item>
        /// <item>when either of two tables does not allow overlapping, the later one moves down until it is clear
        /// (decision B; the standard requires an adjustment but not which).</item>
        /// </list>
        /// </summary>
        private FloatingTableResult FloatTable(PageFill f, FlowTable table, FloatingTablePosition fp, FlowPosition pos, (double Left, double Width) column,
            bool single, double y, double columnTop, double bodyBottom, bool canForce, string path, CancellationToken ct)
        {
            PageRecord record = f.Record;
            FlowSection section = _doc.Sections[pos.Section];
            PageGeometry page = section.Page;
            (double marginLeft, double marginWidth) = TextArea(page, record.PageNumber);
            (double colLeft, double colWidth) = single ? (marginLeft, marginWidth) : column;
            TableLayout layout = LayoutTable(table, colWidth, path, 1, ct);
            layout.Token = ct;
            double width = layout.Width;
            FlowPosition after = new FlowPosition(pos.Section, pos.Block + 1, 0);
            if (layout.RowCount == 0)
            {
                return new FloatingTableResult(false, y, false, after, null);
            }

            (double baseX, double spanX) = fp.HorizontalAnchor switch
            {
                TableHorizontalAnchor.Margin => (marginLeft, marginWidth),
                TableHorizontalAnchor.Column => (colLeft, colWidth),
                _ => (0.0, page.Width),
            };
            bool oddPage = record.PageNumber % 2 == 1;
            double x = fp.HorizontalAlignment switch
            {
                TableHorizontalAlignment.Left => baseX,
                TableHorizontalAlignment.Center => baseX + ((spanX - width) / 2),
                TableHorizontalAlignment.Right => baseX + spanX - width,
                TableHorizontalAlignment.Inside => oddPage ? baseX : baseX + spanX - width,
                TableHorizontalAlignment.Outside => oddPage ? baseX + spanX - width : baseX,
                _ => baseX + fp.OffsetX,
            };

            if (pos.Table is not null)
            {
                // The rest of a table that did not fit: from the top of this column, at the same horizontal position.
                TablePlacement rest = PlaceFloatingTable(f, table, fp, pos, x, width, colWidth, columnTop, columnTop, bodyBottom, canForce, path, ct);
                return new FloatingTableResult(rest.Placed, rest.Y, rest.PageEnds && rest.Next.Block == pos.Block, rest.Next, null);
            }

            double height = 0;
            for (int r = 0; r < layout.RowCount; r++)
            {
                height += layout.Row(r).Height;
            }
            double yTop;
            if (fp.VerticalAnchor == TableVerticalAnchor.Paragraph)
            {
                yTop = y + fp.OffsetY; // a relative vertical position is ignored for the paragraph anchor (17.4.57)
            }
            else
            {
                (double baseY, double spanY) = fp.VerticalAnchor == TableVerticalAnchor.Margin
                    ? (page.MarginTop, page.Height - page.MarginTop - page.MarginBottom)
                    : (0.0, page.Height);
                yTop = fp.VerticalAlignment switch
                {
                    TableVerticalAlignment.Inline or TableVerticalAlignment.Top or TableVerticalAlignment.Inside => baseY,
                    TableVerticalAlignment.Center => baseY + ((spanY - height) / 2),
                    TableVerticalAlignment.Bottom or TableVerticalAlignment.Outside => baseY + spanY - height,
                    _ => baseY + fp.OffsetY,
                };
            }
            if (fp.VerticalAnchor != TableVerticalAnchor.Paragraph)
            {
                // Positioned on the page or margin: above its paragraph, the page is laid out again with it placed first.
                object? late = null;
                if (yTop - fp.DistanceTop < y - Epsilon && yTop + height + fp.DistanceBottom > f.BodyTop + Epsilon)
                {
                    late = new Preseed(table, new LayoutRect(x, yTop, width, height), path, pos, colWidth);
                }
                yTop = ClearOfFloatingTables(f, fp, x, width, yTop, height);
                TablePlacement whole = PlaceFloatingTable(f, table, fp, pos, x, width, colWidth, yTop, f.BodyTop, double.MaxValue, false, path, ct);
                return new FloatingTableResult(whole.Placed, whole.Y, false, after, late);
            }
            yTop = ClearOfFloatingTables(f, fp, x, width, yTop, height);

            TablePlacement tp = PlaceFloatingTable(f, table, fp, pos, x, width, colWidth, yTop, columnTop, bodyBottom, canForce && yTop <= columnTop + Epsilon, path, ct);
            bool continues = tp.PageEnds && tp.Next.Block == pos.Block;
            return new FloatingTableResult(tp.Placed, tp.Y, continues, continues ? tp.Next : after, null);
        }

        /// <summary>
        /// Width of the table's right border. Word keeps text the distance away from the outside of that border (its text
        /// starts at x + width + border + rightFromText: T1, T4 of calib-floating), so the wrap edge includes it.
        /// </summary>
        private static double RightBorderWidth(FlowTable table)
        {
            double w = table.Format.BorderRight.IsVisible ? table.Format.BorderRight.Width : 0;
            foreach (FlowTableRow row in table.Rows)
            {
                if (row.Cells.Count > 0 && row.Cells[^1].BorderRight is BorderLine b && b.IsVisible)
                {
                    w = Math.Max(w, b.Width);
                }
            }
            return w;
        }

        /// <summary>Moves a table down until it overlaps no floating table it may not overlap (either one not allowing it).</summary>
        private static double ClearOfFloatingTables(PageFill f, FloatingTablePosition fp, double x, double width, double yTop, double height)
        {
            for (int pass = 0; pass <= f.FloatingTables.Count; pass++)
            {
                bool moved = false;
                foreach ((LayoutRect r, bool allow) in f.FloatingTables)
                {
                    if (fp.AllowOverlap && allow)
                    {
                        continue;
                    }
                    bool overlaps = x < r.Right - Epsilon && x + width > r.X + Epsilon && yTop < r.Bottom - Epsilon && yTop + height > r.Y + Epsilon;
                    if (overlaps)
                    {
                        yTop = r.Bottom;
                        moved = true;
                    }
                }
                if (!moved)
                {
                    break;
                }
            }
            return yTop;
        }

        /// <summary>Lays the table's rows out from (x, yTop) and keeps text away from the part placed.</summary>
        private TablePlacement PlaceFloatingTable(PageFill f, FlowTable table, FloatingTablePosition fp, FlowPosition pos, double x, double width,
            double areaWidth, double yTop, double bodyTop, double bottom, bool first, string path, CancellationToken ct)
        {
            TablePlacement tp = PlaceTable(table, pos, f.Plan, x, areaWidth, yTop, bodyTop, bottom, first, path, f.Record, ct);
            if (tp.Placed)
            {
                f.Exclusions.Add(new WrapExclusion
                {
                    Top = yTop - fp.DistanceTop,
                    Bottom = tp.Y + fp.DistanceBottom,
                    Left = x,
                    Right = x + width + RightBorderWidth(table),
                    DistanceLeft = fp.DistanceLeft,
                    DistanceRight = fp.DistanceRight,
                    Side = FloatWrapSide.BothSides,
                });
                f.FloatingTables.Add((new LayoutRect(x, yTop, width, tp.Y - yTop), fp.AllowOverlap));
            }
            return tp;
        }
    }
}
