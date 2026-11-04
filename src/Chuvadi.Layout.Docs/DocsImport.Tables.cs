using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using D = Chuvadi.Docs.Model;

namespace Chuvadi.Layout.Docs
{
    internal sealed partial class DocsImport
    {
        // Word's default cell margins (tblCellMar): 0.08 inch left and right, none top and bottom.
        private const double DefaultCellMarginSide = 5.4;

        /// <summary>
        /// A floating table (w:tblpPr) in the body: its position as the file states it (ECMA-376 17.4.57; omitted anchors
        /// are the page, omitted offsets and distances 0). The engine places it there and wraps text around it.
        /// </summary>
        private static FloatingTablePosition Floating(D.TableFloatingPosition fp, bool? overlap) => new FloatingTablePosition
        {
            HorizontalAnchor = fp.HorizontalAnchor switch
            {
                D.TableFloatAnchor.Margin => TableHorizontalAnchor.Margin,
                D.TableFloatAnchor.Text => TableHorizontalAnchor.Column,
                _ => TableHorizontalAnchor.Page,
            },
            VerticalAnchor = fp.VerticalAnchor switch
            {
                D.TableFloatAnchor.Margin => TableVerticalAnchor.Margin,
                D.TableFloatAnchor.Text => TableVerticalAnchor.Paragraph,
                _ => TableVerticalAnchor.Page,
            },
            HorizontalAlignment = fp.XAlign switch
            {
                D.TableFloatXAlign.Left => TableHorizontalAlignment.Left,
                D.TableFloatXAlign.Center => TableHorizontalAlignment.Center,
                D.TableFloatXAlign.Right => TableHorizontalAlignment.Right,
                D.TableFloatXAlign.Inside => TableHorizontalAlignment.Inside,
                D.TableFloatXAlign.Outside => TableHorizontalAlignment.Outside,
                _ => null,
            },
            OffsetX = fp.XPt ?? 0,
            VerticalAlignment = fp.YAlign switch
            {
                D.TableFloatYAlign.Inline => TableVerticalAlignment.Inline,
                D.TableFloatYAlign.Top => TableVerticalAlignment.Top,
                D.TableFloatYAlign.Center => TableVerticalAlignment.Center,
                D.TableFloatYAlign.Bottom => TableVerticalAlignment.Bottom,
                D.TableFloatYAlign.Inside => TableVerticalAlignment.Inside,
                D.TableFloatYAlign.Outside => TableVerticalAlignment.Outside,
                _ => null,
            },
            OffsetY = fp.YPt ?? 0,
            DistanceLeft = Math.Max(0, fp.LeftFromTextPt ?? 0),
            DistanceRight = Math.Max(0, fp.RightFromTextPt ?? 0),
            DistanceTop = Math.Max(0, fp.TopFromTextPt ?? 0),
            DistanceBottom = Math.Max(0, fp.BottomFromTextPt ?? 0),
            AllowOverlap = overlap != false,
        };

        /// <summary>
        /// A floating table inside a table cell, a header or footer, a note or a text box: its horizontal position becomes
        /// the table's alignment or indent; it is laid out in the flow of its container (a warning says so).
        /// </summary>
        private void FloatTable(FlowTable ft, D.TableFloatingPosition fp)
        {
            PageGeometry page = _page;
            bool fromPage = fp.HorizontalAnchor == D.TableFloatAnchor.Page;
            TableFormat f = ft.Format;
            if (fp.XAlign is D.TableFloatXAlign xa)
            {
                ParagraphAlignment a = xa switch
                {
                    D.TableFloatXAlign.Center => ParagraphAlignment.Center,
                    D.TableFloatXAlign.Right or D.TableFloatXAlign.Outside => ParagraphAlignment.Right,
                    _ => ParagraphAlignment.Left,
                };
                ft.Format = f with { Alignment = a, Indent = fromPage && a == ParagraphAlignment.Left ? -page.MarginLeft : 0 };
            }
            else if (fp.XPt is double x)
            {
                ft.Format = f with { Alignment = ParagraphAlignment.Left, Indent = fromPage ? x - page.MarginLeft : x };
            }
            Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity,
                "A floating table inside a table cell, header, footer, note or text box is placed at its horizontal position in the flow of its container; its vertical position is not applied and text does not wrap beside it.");
        }

        private FlowTable Table(D.DocTable t, int depth, double width)
        {
            Shared.Check(nameof(LayoutLimits.MaxNestingDepth), _lo.Limits.MaxNestingDepth, depth);
            D.TableFormat tf = _doc.GetEffectiveTableFormat(t);
            double indent = tf.IndentPt ?? 0;
            FlowTable ft = new FlowTable
            {
                Format = new TableFormat
                {
                    Indent = indent,
                    Alignment = tf.Alignment switch
                    {
                        D.TableAlignment.Center => ParagraphAlignment.Center,
                        D.TableAlignment.Right => ParagraphAlignment.Right,
                        _ => ParagraphAlignment.Left,
                    },
                    CellMarginLeft = tf.CellMargins?.LeftPt ?? DefaultCellMarginSide,
                    CellMarginRight = tf.CellMargins?.RightPt ?? DefaultCellMarginSide,
                    CellMarginTop = tf.CellMargins?.TopPt ?? 0,
                    CellMarginBottom = tf.CellMargins?.BottomPt ?? 0,
                    CellSpacing = tf.CellSpacingPt ?? 0,
                    // Every cell carries its resolved edges (GetEffectiveCellFormat), so the table draws none of its own.
                },
            };
            if (tf.FloatingPosition is D.TableFloatingPosition fp)
            {
                if (depth == 1 && _inBody && !_inStory && !_inTextBox && _noteMark is null)
                {
                    ft.Floating = Floating(fp, tf.AllowOverlap);
                    ft.Format = ft.Format with { Indent = 0, Alignment = ParagraphAlignment.Left };
                }
                else
                {
                    FloatTable(ft, fp);
                }
            }
            if (tf.RightToLeft == true)
            {
                Warn(LayoutWarningCodes.BidiNotSupported, LayoutWarningSeverity.Fidelity, "Right-to-left tables are laid out left-to-right.");
            }

            // Grid: the file's column widths; else the table width (points or percent of the available width), shared equally.
            if (t.ColumnWidthsPt is double[] given && given.Length == t.Columns && Array.TrueForAll(given, w => w > 0))
            {
                foreach (double w in given)
                {
                    ft.ColumnWidths.Add(w);
                }
            }
            else
            {
                double total = tf.Width switch
                {
                    { Type: D.TableWidthType.Points, Value: > 0 } tw => tw.Value,
                    { Type: D.TableWidthType.Percent, Value: > 0 } tw => width * tw.Value / 100,
                    _ => width - indent,
                };
                for (int c = 0; c < t.Columns; c++)
                {
                    ft.ColumnWidths.Add(Math.Max(1, total / t.Columns));
                }
                if (depth == 1 && tf.FloatingPosition is null && tf.Width is not { Type: D.TableWidthType.Points, Value: > 0 })
                {
                    // No column widths in the file: the grid follows the width of the column the table is placed in.
                    ft.Format = ft.Format with
                    {
                        GridFromLayoutWidth = true,
                        PreferredWidthPercent = tf.Width is { Type: D.TableWidthType.Percent, Value: > 0 } pw ? pw.Value : null,
                    };
                }
            }

            for (int r = 0; r < t.Rows.Count; r++)
            {
                _ct.ThrowIfCancellationRequested();
                D.TableRow row = t.Rows[r];
                if (row.Hidden == true || row.Revision is { Type: D.RevisionType.Delete })
                {
                    // Hidden rows, and rows tracked as deleted (the final view, as printed with changes accepted).
                    continue;
                }
                FlowTableRow fr = new FlowTableRow
                {
                    Height = row.HeightPt ?? 0,
                    HeightRule = row.HeightPt is null ? RowHeightRule.Auto : row.HeightRule switch
                    {
                        D.RowHeightRule.Exact => RowHeightRule.Exact,
                        D.RowHeightRule.Auto => RowHeightRule.Auto,
                        _ => RowHeightRule.AtLeast,
                    },
                    IsHeader = row.IsHeader,
                    CantSplit = row.CantSplit == true,
                };
                int last = t.Columns - Math.Max(0, row.GridAfter);
                int c = 0;
                while (c < t.Columns)
                {
                    if (c < row.GridBefore || c >= last)
                    {
                        // Grid columns skipped by a ragged row: nothing is drawn there.
                        FlowTableCell gap = new FlowTableCell { ColumnSpan = 1 };
                        gap.Blocks.Add(new FlowParagraph { MarkFormat = DefaultMark() });
                        fr.Cells.Add(gap);
                        c++;
                        continue;
                    }
                    D.TableCell cell = row.Cells[c];
                    int span = Math.Clamp(cell.ColumnSpan, 1, t.Columns - c);
                    if (cell.HorizontalMerge == D.HorizontalMerge.Restart)
                    {
                        // Legacy horizontal merge: the following "continue" cells join this one.
                        while (c + span < last && row.Cells[c + span].HorizontalMerge == D.HorizontalMerge.Continue)
                        {
                            span += Math.Max(1, row.Cells[c + span].ColumnSpan);
                        }
                        span = Math.Min(span, t.Columns - c);
                    }
                    fr.Cells.Add(Cell(t, r, c, cell, span, depth, ColumnSpanWidth(ft, c, span)));
                    c += span;
                }
                ft.Rows.Add(fr);
            }
            return ft;
        }

        private static double ColumnSpanWidth(FlowTable ft, int first, int span)
        {
            double w = 0;
            for (int i = first; i < first + span && i < ft.ColumnWidths.Count; i++)
            {
                w += ft.ColumnWidths[i];
            }
            return w;
        }

        private FlowTableCell Cell(D.DocTable t, int r, int c, D.TableCell cell, int span, int depth, double width)
        {
            D.TableCellRef cref = new D.TableCellRef(t, r, c);
            D.TableCellFormat cf = _doc.GetEffectiveCellFormat(cref);
            FlowTableCell fc = new FlowTableCell
            {
                ColumnSpan = span,
                VerticalMerge = cell.VerticalMerge switch
                {
                    D.VerticalMerge.Restart => VerticalMerge.Restart,
                    D.VerticalMerge.Continue => VerticalMerge.Continue,
                    _ => VerticalMerge.None,
                },
                VerticalAlignment = cf.VerticalAlignment switch
                {
                    D.CellVerticalAlignment.Center => CellVerticalAlignment.Center,
                    D.CellVerticalAlignment.Bottom => CellVerticalAlignment.Bottom,
                    _ => CellVerticalAlignment.Top,
                },
                Background = Shade(cf.Shading),
                BorderTop = Border(cf.Borders?.Top),
                BorderBottom = Border(cf.Borders?.Bottom),
                BorderLeft = Border(cf.Borders?.Left),
                BorderRight = Border(cf.Borders?.Right),
                MarginLeft = cf.Margins?.LeftPt,
                MarginRight = cf.Margins?.RightPt,
                MarginTop = cf.Margins?.TopPt,
                MarginBottom = cf.Margins?.BottomPt,
            };
            if (cf.Borders is { DiagonalDown: not null } or { DiagonalUp: not null })
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Diagonal cell borders are not drawn.");
            }
            if (cf.TextDirection is D.CellTextDirection td && td != D.CellTextDirection.LeftToRightTopToBottom)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Rotated cell text is laid out horizontally.");
            }
            if (cf.FitText == true)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Fit-text cells are laid out at their normal text width.");
            }
            double inner = Math.Max(1, width - (cf.Margins?.LeftPt ?? DefaultCellMarginSide) - (cf.Margins?.RightPt ?? DefaultCellMarginSide));
            if (cell.VerticalMerge != D.VerticalMerge.Continue)
            {
                LayoutColor? outer = _behind;
                _behind = fc.Background ?? outer;
                try
                {
                    foreach (object b in cell.Blocks)
                    {
                        if (Block(b, b is D.Paragraph ? cref : null, depth + 1, inner) is FlowBlock fb)
                        {
                            fc.Blocks.Add(fb);
                        }
                    }
                }
                finally
                {
                    _behind = outer;
                }
            }
            if (fc.Blocks.Count == 0)
            {
                // Every Word cell ends with a paragraph mark, which gives an empty cell its height.
                fc.Blocks.Add(new FlowParagraph { MarkFormat = DefaultMark() });
            }
            return fc;
        }
    }
}
