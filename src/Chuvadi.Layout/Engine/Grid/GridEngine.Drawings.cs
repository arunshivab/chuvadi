using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine.Flow;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Grid
{
    internal sealed partial class GridEngine
    {
        /// <summary>Insets of a shape's text, as Excel's default text box body (0.1" left/right, 0.05" top/bottom).</summary>
        private const double ShapeInsetX = 7.2;
        private const double ShapeInsetY = 3.6;

        /// <summary>A drawing's rectangle in unscaled sheet points.</summary>
        internal static LayoutRect? SheetBounds(GridDrawing d, GridGeometry geo)
        {
            double x0, y0;
            if (d.From is GridAnchorPoint f)
            {
                x0 = geo.ColumnStart(f.Column) + f.OffsetX;
                y0 = geo.RowStart(f.Row) + f.OffsetY;
            }
            else if (d.X is double x && d.Y is double y)
            {
                x0 = x;
                y0 = y;
            }
            else
            {
                return null;
            }
            double x1, y1;
            if (d.To is GridAnchorPoint t)
            {
                x1 = geo.ColumnStart(t.Column) + t.OffsetX;
                y1 = geo.RowStart(t.Row) + t.OffsetY;
            }
            else if (d.Width is double w && d.Height is double h)
            {
                x1 = x0 + w;
                y1 = y0 + h;
            }
            else
            {
                return null;
            }
            if (x1 < x0 || y1 < y0)
            {
                return null;
            }
            return new LayoutRect(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>The last row and column a drawing reaches (so that the printed used range includes it).</summary>
        internal static (int Row, int Column)? LastCell(GridDrawing d, GridGeometry geo)
        {
            if (d.To is GridAnchorPoint t)
            {
                return (t.OffsetY > 0 ? t.Row : Math.Max(0, t.Row - 1), t.OffsetX > 0 ? t.Column : Math.Max(0, t.Column - 1));
            }
            if (SheetBounds(d, geo) is not LayoutRect r)
            {
                return null;
            }
            int c = 0;
            while (c < 16383 && geo.ColumnStart(c + 1) < r.Right)
            {
                c++;
            }
            int row = 0;
            while (row < 1048575 && geo.RowStart(row + 1) < r.Bottom)
            {
                row++;
            }
            return (row, c);
        }

        /// <summary>Draws the sheet's drawings over the page's band (print titles excluded), clipped to it.</summary>
        private void EmitDrawings(GridSheet sheet, GridGeometry geo, GridPageRecord rec, List<Track> cols, List<Track> rows, double gx, double gy, double s,
            List<LayoutOp> ops, List<LayoutBox> boxes)
        {
            if (sheet.Drawings.Count == 0)
            {
                return;
            }
            int bandCols = rec.ColumnLast - rec.ColumnFirst + 1;
            int bandRows = rec.RowLast - rec.RowFirst + 1;
            if (bandCols <= 0 || bandRows <= 0 || cols.Count < bandCols || rows.Count < bandRows)
            {
                return;
            }
            Track c0 = cols[cols.Count - bandCols];
            Track r0 = rows[rows.Count - bandRows];
            Track cl = cols[^1];
            Track rl = rows[^1];
            LayoutRect band = new LayoutRect(gx + c0.Start, gy + r0.Start, cl.Start + cl.Size - c0.Start, rl.Start + rl.Size - r0.Start);
            double originX = geo.ColumnStart(rec.ColumnFirst);
            double originY = geo.RowStart(rec.RowFirst);
            bool pushed = false;
            foreach (GridDrawing d in sheet.Drawings)
            {
                if (SheetBounds(d, geo) is not LayoutRect sr)
                {
                    continue;
                }
                LayoutRect r = new LayoutRect(band.X + ((sr.X - originX) * s), band.Y + ((sr.Y - originY) * s), sr.Width * s, sr.Height * s);
                if (r.Right <= band.X || r.X >= band.Right || r.Bottom <= band.Y || r.Y >= band.Bottom)
                {
                    continue;
                }
                if (!pushed)
                {
                    ops.Add(new PushClipOp(band));
                    pushed = true;
                }
                string path = sheet.Name + "!" + (d.Name ?? d.Kind.ToString());
                DrawItem(sheet, d, r, s, ops, path);
                boxes.Add(new LayoutBox(LayoutBoxKind.Image, r, path));
            }
            if (pushed)
            {
                ops.Add(PopClipOp.Instance);
            }
        }

        private void DrawItem(GridSheet sheet, GridDrawing d, LayoutRect r, double s, List<LayoutOp> ops, string path)
        {
            if (d.Rotation != 0)
            {
                _ctx.Warn(LayoutWarningCodes.DrawingApproximated, LayoutWarningSeverity.Fidelity,
                    "A rotated picture or shape is drawn unrotated.", path);
            }
            if (d.Kind == GridDrawingKind.Picture && (d.FlipHorizontal || d.FlipVertical))
            {
                _ctx.Warn(LayoutWarningCodes.DrawingApproximated, LayoutWarningSeverity.Fidelity,
                    "A flipped picture is drawn unflipped.", path);
            }
            switch (d.Kind)
            {
                case GridDrawingKind.Picture:
                    if (d.Image is LayoutImage img)
                    {
                        if (d.Crop is (double cl, double ct, double cr, double cb) && (cl != 0 || ct != 0 || cr != 0 || cb != 0)
                            && img.Format is LayoutImageFormat.Png or LayoutImageFormat.Jpeg or LayoutImageFormat.Gif or LayoutImageFormat.Bmp)
                        {
                            LayoutRect crop = new LayoutRect(cl * img.PixelWidth, ct * img.PixelHeight,
                                Math.Max(1, (1 - cl - cr) * img.PixelWidth), Math.Max(1, (1 - ct - cb) * img.PixelHeight));
                            ops.Add(new ImageOp(img, r, crop));
                        }
                        else
                        {
                            FlowEmitter.EmitImageOrPlaceholder(img, r, _ctx, ops, path);
                        }
                    }
                    else
                    {
                        Placeholder(r, d.Description ?? d.Name, ops);
                        _ctx.Warn(LayoutWarningCodes.ImagePlaceholder, LayoutWarningSeverity.ContentLoss,
                            "A picture without image data (linked or not loaded) is drawn as a placeholder.", path);
                    }
                    break;
                case GridDrawingKind.Shape:
                    DrawShape(sheet, d, r, s, ops);
                    break;
                case GridDrawingKind.Chart:
                    _ctx.Warn(LayoutWarningCodes.ChartPlaceholder, LayoutWarningSeverity.ContentLoss,
                        "Charts are not rendered in this version; a placeholder with the chart's title is drawn.", path);
                    Placeholder(r, "Chart" + (string.IsNullOrWhiteSpace(d.Text) ? string.Empty : ": " + d.Text), ops);
                    break;
                case GridDrawingKind.Group:
                    foreach (GridDrawing child in d.Children)
                    {
                        LayoutRect rb = child.RelativeBounds ?? new LayoutRect(0, 0, 1, 1);
                        LayoutRect cr2 = new LayoutRect(r.X + (rb.X * r.Width), r.Y + (rb.Y * r.Height), rb.Width * r.Width, rb.Height * r.Height);
                        DrawItem(sheet, child, cr2, s, ops, path);
                    }
                    break;
            }
        }

        private void DrawShape(GridSheet sheet, GridDrawing d, LayoutRect r, double s, List<LayoutOp> ops)
        {
            string geom = d.Geometry ?? "rect";
            double lw = Math.Max(0.25, d.LineWidth * s);
            switch (geom)
            {
                case "line":
                case "straightConnector1":
                    if (d.Line is LayoutColor lc)
                    {
                        // A line runs from the top-left to the bottom-right of its box; flips mirror the ends.
                        double x1 = d.FlipHorizontal ? r.Right : r.X, x2 = d.FlipHorizontal ? r.X : r.Right;
                        double y1 = d.FlipVertical ? r.Bottom : r.Y, y2 = d.FlipVertical ? r.Y : r.Bottom;
                        ops.Add(new StrokeLineOp(x1, y1, x2, y2, lw, lc));
                    }
                    return;
                case "ellipse":
                    ops.Add(new PathOp { Segments = Ellipse(r), Fill = d.Fill, Stroke = d.Line, StrokeWidth = lw });
                    break;
                case "roundRect":
                    ops.Add(new PathOp { Segments = RoundRect(r, Math.Min(r.Width, r.Height) * 0.16667), Fill = d.Fill, Stroke = d.Line, StrokeWidth = lw });
                    break;
                default:
                    if (geom != "rect")
                    {
                        _ctx.Warn(LayoutWarningCodes.DrawingApproximated, LayoutWarningSeverity.Fidelity,
                            $"Shape geometry '{geom}' is drawn as its bounding rectangle.", sheet.Name);
                    }
                    ops.Add(new PathOp { Segments = RoundRect(r, 0), Fill = d.Fill, Stroke = d.Line, StrokeWidth = lw });
                    break;
            }
            if (!string.IsNullOrEmpty(d.Text))
            {
                ShapeText(sheet, d.Text, r, s, ops);
            }
        }

        private void ShapeText(GridSheet sheet, string text, LayoutRect r, double s, List<LayoutOp> ops)
        {
            double insetX = ShapeInsetX * s, insetY = ShapeInsetY * s;
            double width = Math.Max(1, r.Width - (2 * insetX));
            CharacterFormat f = sheet.NormalFont with { FontSize = sheet.NormalFont.FontSize * s };
            ops.Add(new PushClipOp(r));
            double y = r.Y + insetY;
            foreach (string para in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                FlowParagraph p = FlowParagraph.Of(para, f, new ParagraphFormat { WidowControl = false });
                (MeasuredParagraph m, List<FittedLine> lines) = MeasureCell(p, width);
                for (int i = 0; i < lines.Count; i++)
                {
                    FlowEmitter.EmitLine(new PlacedLine { Paragraph = m, Line = lines[i], LineIndex = i, AreaLeft = r.X + insetX, AreaWidth = width, Top = y, Path = string.Empty },
                        _ctx, ops, new List<LayoutBox>(), new StringBuilder());
                    y += lines[i].Height;
                }
            }
            ops.Add(PopClipOp.Instance);
        }

        private void Placeholder(LayoutRect r, string? label, List<LayoutOp> ops)
        {
            LayoutColor grey = new LayoutColor(0xA0, 0xA0, 0xA0);
            ops.Add(new FillRectOp(r, new LayoutColor(0xF2, 0xF2, 0xF2)));
            ops.Add(new PathOp { Segments = RoundRect(r, 0), Stroke = grey, StrokeWidth = 0.5 });
            if (!string.IsNullOrWhiteSpace(label) && r.Width > 20 && r.Height > 12)
            {
                CenteredText(label, new CharacterFormat { FontFamily = "Calibri", FontSize = Math.Min(11, r.Height * 0.4), Color = new LayoutColor(0x59, 0x59, 0x59) }, r, ops);
            }
        }

        private static List<PathSegment> RoundRect(LayoutRect r, double radius)
        {
            List<PathSegment> p = new List<PathSegment>();
            if (radius <= 0)
            {
                p.Add(new PathSegment(PathSegmentKind.MoveTo, r.X, r.Y));
                p.Add(new PathSegment(PathSegmentKind.LineTo, r.Right, r.Y));
                p.Add(new PathSegment(PathSegmentKind.LineTo, r.Right, r.Bottom));
                p.Add(new PathSegment(PathSegmentKind.LineTo, r.X, r.Bottom));
                p.Add(new PathSegment(PathSegmentKind.Close));
                return p;
            }
            const double K = 0.5523;
            double k = radius * K;
            p.Add(new PathSegment(PathSegmentKind.MoveTo, r.X + radius, r.Y));
            p.Add(new PathSegment(PathSegmentKind.LineTo, r.Right - radius, r.Y));
            p.Add(new PathSegment(PathSegmentKind.CubicTo, r.Right, r.Y + radius, r.Right - radius + k, r.Y, r.Right, r.Y + radius - k));
            p.Add(new PathSegment(PathSegmentKind.LineTo, r.Right, r.Bottom - radius));
            p.Add(new PathSegment(PathSegmentKind.CubicTo, r.Right - radius, r.Bottom, r.Right, r.Bottom - radius + k, r.Right - radius + k, r.Bottom));
            p.Add(new PathSegment(PathSegmentKind.LineTo, r.X + radius, r.Bottom));
            p.Add(new PathSegment(PathSegmentKind.CubicTo, r.X, r.Bottom - radius, r.X + radius - k, r.Bottom, r.X, r.Bottom - radius + k));
            p.Add(new PathSegment(PathSegmentKind.LineTo, r.X, r.Y + radius));
            p.Add(new PathSegment(PathSegmentKind.CubicTo, r.X + radius, r.Y, r.X, r.Y + radius - k, r.X + radius - k, r.Y));
            p.Add(new PathSegment(PathSegmentKind.Close));
            return p;
        }

        private static List<PathSegment> Ellipse(LayoutRect r)
        {
            const double K = 0.5523;
            double cx = r.X + (r.Width / 2), cy = r.Y + (r.Height / 2), rx = r.Width / 2, ry = r.Height / 2;
            double kx = rx * K, ky = ry * K;
            return new List<PathSegment>
            {
                new PathSegment(PathSegmentKind.MoveTo, cx + rx, cy),
                new PathSegment(PathSegmentKind.CubicTo, cx, cy + ry, cx + rx, cy + ky, cx + kx, cy + ry),
                new PathSegment(PathSegmentKind.CubicTo, cx - rx, cy, cx - kx, cy + ry, cx - rx, cy + ky),
                new PathSegment(PathSegmentKind.CubicTo, cx, cy - ry, cx - rx, cy - ky, cx - kx, cy - ry),
                new PathSegment(PathSegmentKind.CubicTo, cx + rx, cy, cx + kx, cy - ry, cx + rx, cy - ky),
                new PathSegment(PathSegmentKind.Close),
            };
        }
    }
}
