using System;

namespace Chuvadi.Print
{
    /// <summary>
    /// Places vector pages on sheets with the same maths the raster routes use (<see cref="PageLayout"/>), so a page lands
    /// in the same place, at the same scale and turned the same way on every route.
    /// </summary>
    public static class VectorLayout
    {
        /// <summary>
        /// A sheet-sized page with <paramref name="page"/> drawn where <paramref name="placement"/> puts it: turned by
        /// <see cref="PagePlacement.Rotation"/>, scaled by <see cref="PagePlacement.ScaleFactor"/> and moved to
        /// <see cref="PagePlacement.ContentBounds"/>. The content is clipped to the page's own rectangle, as a rendered
        /// page is; parts of it outside the sheet are cut off by the sheet edge.
        /// </summary>
        /// <remarks>
        /// The page's elements are shared, not copied: the sheet holds one <see cref="VectorGroup"/> whose children are the
        /// page's elements. Do not change the page while the sheet is in use.
        /// </remarks>
        public static VectorPage Place(VectorPage page, PagePlacement placement)
        {
            if (page is null) throw new ArgumentNullException(nameof(page));
            if (placement is null) throw new ArgumentNullException(nameof(placement));

            var sheet = new VectorPage(placement.SheetSize.WidthPoints, placement.SheetSize.HeightPoints);
            if (page.Elements.Count == 0) return sheet;

            var group = new VectorGroup
            {
                Transform = PageToSheet(page.Width, page.Height, placement),
                Clip = VectorGeometry.FromRectangle(0, 0, page.Width, page.Height)
            };
            foreach (VectorElement element in page.Elements) group.Children.Add(element);
            sheet.Elements.Add(group);
            return sheet;
        }

        /// <summary>
        /// The transform from page coordinates to sheet coordinates for a page of the given size: the rotation (about the
        /// page, so the turned page starts at the origin), then the scale, then the move to the content bounds.
        /// </summary>
        public static VectorMatrix PageToSheet(double pageWidth, double pageHeight, PagePlacement placement)
        {
            if (placement is null) throw new ArgumentNullException(nameof(placement));
            if (!(pageWidth > 0) || double.IsInfinity(pageWidth)) throw new ArgumentOutOfRangeException(nameof(pageWidth));
            if (!(pageHeight > 0) || double.IsInfinity(pageHeight)) throw new ArgumentOutOfRangeException(nameof(pageHeight));

            // x' = M11 x + M21 y + OffsetX, y' = M12 x + M22 y + OffsetY; y runs down.
            VectorMatrix turn = placement.Rotation switch
            {
                PageRotation.Clockwise90 => new VectorMatrix(0, 1, -1, 0, pageHeight, 0),   // (x, y) -> (H - y, x)
                PageRotation.Rotate180 => new VectorMatrix(-1, 0, 0, -1, pageWidth, pageHeight), // (x, y) -> (W - x, H - y)
                PageRotation.Clockwise270 => new VectorMatrix(0, -1, 1, 0, 0, pageWidth),  // (x, y) -> (y, W - x)
                _ => VectorMatrix.Identity
            };
            double scale = placement.ScaleFactor;
            RectanglePoints bounds = placement.ContentBounds;
            return VectorMatrix.Multiply(VectorMatrix.Multiply(turn, VectorMatrix.Scaling(scale, scale)),
                                         VectorMatrix.Translation(bounds.X, bounds.Y));
        }
    }
}
