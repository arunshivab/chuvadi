using System;

namespace Chuvadi.Print
{
    /// <summary>
    /// Pure page-placement maths: given a page, the print settings and the device's capabilities,
    /// works out the sheet, the printable area, the rotation, the scale and the final position.
    /// Covers every orientation, scale mode, alignment and margin combination.
    /// </summary>
    public static class PageLayout
    {
        private const double Tolerance = 1e-6;

        /// <summary>
        /// Computes where <paramref name="page"/> lands on the sheet.
        /// </summary>
        /// <remarks>
        /// <para>Sheet: <see cref="PageOrientation.Portrait"/> and <see cref="PageOrientation.ReversePortrait"/>
        /// use the paper with its long edge vertical; <see cref="PageOrientation.Landscape"/> and
        /// <see cref="PageOrientation.ReverseLandscape"/> use it with its long edge horizontal.</para>
        /// <para>Rotation: the reverse orientations turn content 180 degrees. When
        /// <see cref="PrintSettings.AutoRotate"/> is set and the page's orientation differs from the
        /// printable area's, content is additionally turned 90 degrees clockwise.</para>
        /// <para><see cref="PrintTargetCapabilities.HardMargins"/> are expressed in the sheet's printed
        /// orientation (the frame of <see cref="PagePlacement.SheetSize"/>).</para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">The margins leave no printable area.</exception>
        public static PagePlacement Compute(PageDimensions page, PrintSettings settings,
                                            PrintTargetCapabilities capabilities,
                                            MarginReference marginReference = MarginReference.SheetEdge)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));

            PageDimensions sheet = SheetFor(settings.Paper, settings.Orientation);
            Margins margins = CombineMargins(settings.Margins, capabilities.HardMargins, marginReference);

            double areaWidth = sheet.WidthPoints - margins.Left - margins.Right;
            double areaHeight = sheet.HeightPoints - margins.Top - margins.Bottom;
            if (areaWidth <= Tolerance || areaHeight <= Tolerance)
                throw new InvalidOperationException(
                    "The margins (" + margins + ") leave no printable area on a " + sheet + " sheet.");
            var area = new RectanglePoints(margins.Left, margins.Top, areaWidth, areaHeight);

            PageRotation rotation = settings.Orientation is PageOrientation.ReversePortrait or PageOrientation.ReverseLandscape
                ? PageRotation.Rotate180
                : PageRotation.None;

            if (settings.AutoRotate)
            {
                bool areaLandscape = areaWidth > areaHeight;
                bool areaPortrait = areaHeight > areaWidth;
                if ((page.IsLandscape && areaPortrait) || (page.IsPortrait && areaLandscape))
                    rotation = Combine(rotation, PageRotation.Clockwise90);
            }

            PageDimensions content = IsQuarterTurn(rotation) ? page.Swap() : page;
            double scale = ScaleFor(settings, content, areaWidth, areaHeight);

            double width = content.WidthPoints * scale;
            double height = content.HeightPoints * scale;
            double x = settings.Alignment.Horizontal switch
            {
                HorizontalAlignment.Left => area.Left,
                HorizontalAlignment.Right => area.Right - width,
                _ => area.Left + (areaWidth - width) / 2.0
            };
            double y = settings.Alignment.Vertical switch
            {
                VerticalAlignment.Top => area.Top,
                VerticalAlignment.Bottom => area.Bottom - height,
                _ => area.Top + (areaHeight - height) / 2.0
            };
            var bounds = new RectanglePoints(x, y, width, height);
            bool clipped = !area.Contains(bounds, Tolerance * Math.Max(1.0, Math.Max(areaWidth, areaHeight)));

            return new PagePlacement(page, sheet, margins, area, bounds, scale, rotation, clipped);
        }

        /// <summary>The sheet as printed for a paper size and orientation.</summary>
        public static PageDimensions SheetFor(PaperSize paper, PageOrientation orientation)
        {
            double shortEdge = Math.Min(paper.WidthPoints, paper.HeightPoints);
            double longEdge = Math.Max(paper.WidthPoints, paper.HeightPoints);
            return orientation is PageOrientation.Landscape or PageOrientation.ReverseLandscape
                ? new PageDimensions(longEdge, shortEdge)
                : new PageDimensions(shortEdge, longEdge);
        }

        /// <summary>Combines requested margins with the device's hard margins.</summary>
        public static Margins CombineMargins(Margins requested, Margins hard, MarginReference reference)
            => reference switch
            {
                MarginReference.PrintableArea => new Margins(
                    requested.Left + hard.Left, requested.Top + hard.Top,
                    requested.Right + hard.Right, requested.Bottom + hard.Bottom),
                MarginReference.SheetEdge => new Margins(
                    Math.Max(requested.Left, hard.Left), Math.Max(requested.Top, hard.Top),
                    Math.Max(requested.Right, hard.Right), Math.Max(requested.Bottom, hard.Bottom)),
                _ => throw new ArgumentOutOfRangeException(nameof(reference))
            };

        /// <summary>Adds two clockwise rotations.</summary>
        public static PageRotation Combine(PageRotation first, PageRotation second)
            => (PageRotation)(((int)first + (int)second) % 4);

        /// <summary>True for 90 and 270 degree rotations, which swap width and height.</summary>
        public static bool IsQuarterTurn(PageRotation rotation)
            => rotation is PageRotation.Clockwise90 or PageRotation.Clockwise270;

        private static double ScaleFor(PrintSettings settings, PageDimensions content, double areaWidth, double areaHeight)
        {
            double fitWidth = areaWidth / content.WidthPoints;
            double fitHeight = areaHeight / content.HeightPoints;
            double fit = Math.Min(fitWidth, fitHeight);
            switch (settings.Scale)
            {
                case ScaleMode.ActualSize: return 1.0;
                case ScaleMode.FitToPage: return fit;
                case ScaleMode.ShrinkToFit: return Math.Min(1.0, fit);
                case ScaleMode.FitToWidth: return fitWidth;
                case ScaleMode.FitToHeight: return fitHeight;
                case ScaleMode.Custom:
                    double percent = settings.CustomScalePercent;
                    if (!(percent > 0) || double.IsInfinity(percent))
                        throw new InvalidOperationException("CustomScalePercent must be a positive finite number.");
                    return percent / 100.0;
                default:
                    throw new InvalidOperationException("Unknown scale mode: " + settings.Scale + ".");
            }
        }
    }
}
