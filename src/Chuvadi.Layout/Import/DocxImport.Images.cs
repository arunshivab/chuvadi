using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using D = Chuvadi.Docs.Word;

namespace Chuvadi.Layout.Import
{
    internal sealed partial class DocxImport
    {
        // ---- Images ----

        /// <summary>
        /// A picture run (Chuvadi.Docs 6b: documents built in code and loaded ones alike). Inline pictures flow at their
        /// run; floating ones anchor to their paragraph. Size: the relative size when given (percent of the page or
        /// margins), else the stated size. Position precedence: alignment, then percentage offset, then points.
        /// </summary>
        private FlowInline? Image(D.ImageSpec img, CharacterFormat cf)
        {
            if (img.Hidden)
            {
                return null;
            }
            PageGeometry page = _page;
            double contentWidth = page.ContentWidth;
            double contentHeight = Math.Max(1, page.Height - page.MarginTop - page.MarginBottom);
            double Frame(D.RelativeSizeFrame f, bool horizontal) => f switch
            {
                D.RelativeSizeFrame.Page => horizontal ? page.Width : page.Height,
                D.RelativeSizeFrame.LeftMargin or D.RelativeSizeFrame.InsideMargin => page.MarginLeft,
                D.RelativeSizeFrame.RightMargin or D.RelativeSizeFrame.OutsideMargin => page.MarginRight,
                D.RelativeSizeFrame.TopMargin => page.MarginTop,
                D.RelativeSizeFrame.BottomMargin => page.MarginBottom,
                _ => horizontal ? contentWidth : contentHeight,
            };
            double width = img.RelativeWidth is D.RelativeSize rw && rw.Percent > 0 ? Frame(rw.RelativeTo, true) * rw.Percent / 100 : img.WidthPt;
            double height = img.RelativeHeight is D.RelativeSize rh && rh.Percent > 0 ? Frame(rh.RelativeTo, false) * rh.Percent / 100 : img.HeightPt;
            if (!(width > 0) || !(height > 0))
            {
                return null;
            }
            LayoutImage image;
            if (img.Bytes is { Length: > 0 } bytes)
            {
                LayoutLimits.Check(nameof(LayoutLimits.MaxResourceBytes), _lo.Limits.MaxResourceBytes, bytes.LongLength);
                image = LayoutImage.FromEncoded(bytes);
            }
            else
            {
                // A linked picture (no pixels): the engine draws a placeholder box of the right size.
                image = new LayoutImage(ReadOnlyMemory<byte>.Empty, LayoutImageFormat.Unknown, 0, 0);
            }
            if (img.RotationDegrees % 360 != 0 || img.FlipHorizontal || img.FlipVertical)
            {
                Warn(LayoutWarningCodes.DrawingApproximated, LayoutWarningSeverity.Fidelity, "Rotated or flipped pictures are drawn upright and unflipped.");
            }
            ImageCrop? crop = img.Crop is D.ImageCrop c ? new ImageCrop(c.Left, c.Top, c.Right, c.Bottom) : null;
            D.FloatingPosition? pos = img.Position;
            if (img.Placement == D.ImagePlacement.Inline || pos is null)
            {
                return new ImageInline(image, width, height, cf) { Description = img.AltText, Crop = crop };
            }

            Geometry g = Floating(pos, width, height, "Pictures");
            return new AnchoredImageInline(image, width, height, cf)
            {
                HorizontalRelativeTo = g.H,
                VerticalRelativeTo = g.V,
                OffsetX = g.X,
                OffsetY = g.Y,
                HorizontalAlignment = g.HAlign,
                VerticalAlignment = g.VAlign,
                Wrap = g.Wrap,
                WrapSide = g.Side,
                WrapPolygon = g.Polygon,
                DistanceTop = g.Top,
                DistanceBottom = g.Bottom,
                DistanceLeft = g.Left,
                DistanceRight = g.Right,
                Description = img.AltText,
                Crop = crop,
            };
        }

        /// <summary>Where a floating object goes and how text wraps around it, in the engine's terms.</summary>
        private sealed record Geometry(HorizontalAnchor H, VerticalAnchor V, double X, double Y, FloatAlignment? HAlign, FloatAlignment? VAlign,
            FloatWrap Wrap, FloatWrapSide Side, List<WrapPoint>? Polygon, double Top, double Bottom, double Left, double Right);

        /// <summary>
        /// A floating picture's or text box's position (Chuvadi.Docs FloatingPosition): anchors, then alignment, then
        /// percentage offset, then points; frames that start at the page edge become the page frame with an offset.
        /// </summary>
        private Geometry Floating(D.FloatingPosition pos, double width, double height, string what)
        {
            PageGeometry page = _page;
            double contentWidth = page.ContentWidth;
            double contentHeight = Math.Max(1, page.Height - page.MarginTop - page.MarginBottom);
            HorizontalAnchor h;
            VerticalAnchor v;
            double x, y;
            FloatAlignment? ha = pos.HAlign switch
            {
                null => null,
                D.HorizontalAlignment.Center => FloatAlignment.Center,
                D.HorizontalAlignment.Right or D.HorizontalAlignment.Outside => FloatAlignment.End,
                _ => FloatAlignment.Start,
            };
            FloatAlignment? va = pos.VAlign switch
            {
                null => null,
                D.VerticalAlignment.Center => FloatAlignment.Center,
                D.VerticalAlignment.Bottom or D.VerticalAlignment.Outside => FloatAlignment.End,
                _ => FloatAlignment.Start,
            };
            if (pos.UseSimplePosition)
            {
                (h, v, x, y, ha, va) = (HorizontalAnchor.Page, VerticalAnchor.Page, pos.SimplePositionXPt, pos.SimplePositionYPt, null, null);
            }
            else
            {
                // Frames that start at the page's left or top edge are the page frame with an offset.
                (h, double hBase, double hSize) = pos.HorizontalAnchor switch
                {
                    D.HorizontalAnchor.Page => (HorizontalAnchor.Page, 0.0, page.Width),
                    D.HorizontalAnchor.LeftMargin => (HorizontalAnchor.Page, 0.0, page.MarginLeft),
                    D.HorizontalAnchor.RightMargin => (HorizontalAnchor.Page, page.Width - page.MarginRight, page.MarginRight),
                    D.HorizontalAnchor.InsideMargin => (HorizontalAnchor.Page, 0.0, page.MarginLeft),
                    D.HorizontalAnchor.OutsideMargin => (HorizontalAnchor.Page, page.Width - page.MarginRight, page.MarginRight),
                    D.HorizontalAnchor.Character => (HorizontalAnchor.Character, 0.0, contentWidth),
                    D.HorizontalAnchor.Column => (HorizontalAnchor.Column, 0.0, contentWidth),
                    _ => (HorizontalAnchor.Margin, 0.0, contentWidth),
                };
                (v, double vBase, double vSize) = pos.VerticalAnchor switch
                {
                    D.VerticalAnchor.Page => (VerticalAnchor.Page, 0.0, page.Height),
                    D.VerticalAnchor.TopMargin => (VerticalAnchor.Page, 0.0, page.MarginTop),
                    D.VerticalAnchor.BottomMargin => (VerticalAnchor.Page, page.Height - page.MarginBottom, page.MarginBottom),
                    D.VerticalAnchor.Line => (VerticalAnchor.Line, 0.0, contentHeight),
                    D.VerticalAnchor.Paragraph => (VerticalAnchor.Paragraph, 0.0, contentHeight),
                    D.VerticalAnchor.InsideMargin or D.VerticalAnchor.OutsideMargin => (VerticalAnchor.Margin, 0.0, contentHeight),
                    _ => (VerticalAnchor.Margin, 0.0, contentHeight),
                };
                if (pos.HorizontalAnchor is D.HorizontalAnchor.InsideMargin or D.HorizontalAnchor.OutsideMargin && page.MirrorMargins)
                {
                    Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, what + " positioned from the inside or outside margin are placed as on odd pages.");
                }
                // An alignment within a margin frame becomes an offset in the page frame.
                if (ha is FloatAlignment a1 && h == HorizontalAnchor.Page && hSize != page.Width)
                {
                    x = hBase + (a1 switch { FloatAlignment.Center => (hSize - width) / 2, FloatAlignment.End => hSize - width, _ => 0 });
                    ha = null;
                }
                else
                {
                    x = hBase + (pos.HorizontalOffsetPercent is double hp ? hSize * hp / 100 : pos.HorizontalOffsetPt);
                }
                if (va is FloatAlignment a2 && v == VerticalAnchor.Page && vSize != page.Height)
                {
                    y = vBase + (a2 switch { FloatAlignment.Center => (vSize - height) / 2, FloatAlignment.End => vSize - height, _ => 0 });
                    va = null;
                }
                else
                {
                    y = vBase + (pos.VerticalOffsetPercent is double vp ? vSize * vp / 100 : pos.VerticalOffsetPt);
                }
            }
            (double dt, double db, double dl, double dr) = pos.EffectiveDistances;
            List<WrapPoint>? polygon = null;
            if (pos.WrapPolygon is D.WrapPolygon wp && wp.Points.Count >= 3 && pos.Wrap is D.TextWrap.Tight or D.TextWrap.Through)
            {
                polygon = new List<WrapPoint>(wp.Points.Count);
                foreach ((long px, long py) in wp.Points)
                {
                    polygon.Add(new WrapPoint(px / 21600.0 * width, py / 21600.0 * height));
                }
            }
            return new Geometry(h, v, x, y, ha, va,
                pos.Wrap switch
                {
                    D.TextWrap.None => pos.BehindText ? FloatWrap.BehindText : FloatWrap.InFrontOfText,
                    D.TextWrap.Tight => FloatWrap.Tight,
                    D.TextWrap.Through => FloatWrap.Through,
                    D.TextWrap.TopAndBottom => FloatWrap.TopAndBottom,
                    _ => FloatWrap.Square,
                },
                pos.WrapSide switch
                {
                    D.WrapSide.Left => FloatWrapSide.Left,
                    D.WrapSide.Right => FloatWrapSide.Right,
                    D.WrapSide.Largest => FloatWrapSide.Largest,
                    _ => FloatWrapSide.BothSides,
                },
                polygon, dt, db, dl, dr);
        }
    }
}
