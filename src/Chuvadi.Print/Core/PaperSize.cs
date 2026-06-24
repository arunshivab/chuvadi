using System;

namespace Chuvadi.Print
{
    /// <summary>
    /// A paper size in points (1/72 inch). The full set of common ISO A/B and North-American
    /// sizes is provided as presets, plus Custom / FromMillimetres / FromInches for anything else.
    /// </summary>
    public readonly struct PaperSize : IEquatable<PaperSize>
    {
        private const double MmPerInch = 25.4;
        private const double PointsPerInch = 72.0;

        public double WidthPoints { get; }
        public double HeightPoints { get; }
        public string? Name { get; }

        public PaperSize(double widthPoints, double heightPoints, string? name = null)
        {
            if (widthPoints <= 0) throw new ArgumentOutOfRangeException(nameof(widthPoints), "Width must be positive.");
            if (heightPoints <= 0) throw new ArgumentOutOfRangeException(nameof(heightPoints), "Height must be positive.");
            WidthPoints = widthPoints;
            HeightPoints = heightPoints;
            Name = name;
        }

        public static PaperSize Custom(double widthPoints, double heightPoints) => new(widthPoints, heightPoints);

        public static PaperSize FromMillimetres(double widthMm, double heightMm, string? name = null)
            => new(widthMm / MmPerInch * PointsPerInch, heightMm / MmPerInch * PointsPerInch, name);

        public static PaperSize FromInches(double widthInches, double heightInches, string? name = null)
            => new(widthInches * PointsPerInch, heightInches * PointsPerInch, name);

        // ISO 216 A series
        public static PaperSize A0 => FromMillimetres(841, 1189, "A0");
        public static PaperSize A1 => FromMillimetres(594, 841, "A1");
        public static PaperSize A2 => FromMillimetres(420, 594, "A2");
        public static PaperSize A3 => FromMillimetres(297, 420, "A3");
        public static PaperSize A4 => FromMillimetres(210, 297, "A4");
        public static PaperSize A5 => FromMillimetres(148, 210, "A5");
        public static PaperSize A6 => FromMillimetres(105, 148, "A6");

        // ISO 216 B series (common print sizes)
        public static PaperSize B4 => FromMillimetres(250, 353, "B4");
        public static PaperSize B5 => FromMillimetres(176, 250, "B5");

        // North American
        public static PaperSize Letter => FromInches(8.5, 11, "Letter");
        public static PaperSize Legal => FromInches(8.5, 14, "Legal");
        public static PaperSize Tabloid => FromInches(11, 17, "Tabloid");
        public static PaperSize Ledger => FromInches(17, 11, "Ledger");
        public static PaperSize Executive => FromInches(7.25, 10.5, "Executive");
        public static PaperSize Statement => FromInches(5.5, 8.5, "Statement");

        public double WidthMillimetres => WidthPoints / PointsPerInch * MmPerInch;
        public double HeightMillimetres => HeightPoints / PointsPerInch * MmPerInch;

        /// <summary>Returns the same size with width and height swapped.</summary>
        public PaperSize Rotate() => new(HeightPoints, WidthPoints, Name);

        public bool Equals(PaperSize other)
            => WidthPoints.Equals(other.WidthPoints) && HeightPoints.Equals(other.HeightPoints);

        public override bool Equals(object? obj) => obj is PaperSize p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(WidthPoints, HeightPoints);
        public static bool operator ==(PaperSize left, PaperSize right) => left.Equals(right);
        public static bool operator !=(PaperSize left, PaperSize right) => !left.Equals(right);
        public override string ToString()
            => Name ?? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##}x{1:0.##}pt", WidthPoints, HeightPoints);
    }
}
