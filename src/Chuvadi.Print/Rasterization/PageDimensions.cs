using System;
using System.Globalization;

namespace Chuvadi.Print
{
    /// <summary>
    /// The natural size of a document page in points (1/72 inch), as the page is meant to be
    /// viewed (any intrinsic page rotation already applied).
    /// </summary>
    public readonly struct PageDimensions : IEquatable<PageDimensions>
    {
        public double WidthPoints { get; }
        public double HeightPoints { get; }

        public PageDimensions(double widthPoints, double heightPoints)
        {
            if (!(widthPoints > 0) || double.IsInfinity(widthPoints))
                throw new ArgumentOutOfRangeException(nameof(widthPoints), "Width must be a positive finite number.");
            if (!(heightPoints > 0) || double.IsInfinity(heightPoints))
                throw new ArgumentOutOfRangeException(nameof(heightPoints), "Height must be a positive finite number.");
            WidthPoints = widthPoints;
            HeightPoints = heightPoints;
        }

        /// <summary>The page dimensions of a paper size.</summary>
        public static PageDimensions FromPaper(PaperSize paper) => new(paper.WidthPoints, paper.HeightPoints);

        /// <summary>True when the page is wider than it is tall.</summary>
        public bool IsLandscape => WidthPoints > HeightPoints;

        /// <summary>True when the page is taller than it is wide.</summary>
        public bool IsPortrait => HeightPoints > WidthPoints;

        /// <summary>Returns the same dimensions with width and height swapped.</summary>
        public PageDimensions Swap() => new(HeightPoints, WidthPoints);

        public bool Equals(PageDimensions other)
            => WidthPoints.Equals(other.WidthPoints) && HeightPoints.Equals(other.HeightPoints);
        public override bool Equals(object? obj) => obj is PageDimensions d && Equals(d);
        public override int GetHashCode() => HashCode.Combine(WidthPoints, HeightPoints);
        public static bool operator ==(PageDimensions left, PageDimensions right) => left.Equals(right);
        public static bool operator !=(PageDimensions left, PageDimensions right) => !left.Equals(right);
        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "{0:0.##}x{1:0.##}pt", WidthPoints, HeightPoints);
    }
}
