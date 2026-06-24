using System;

namespace Chuvadi.Print
{
    /// <summary>
    /// Page margins in points (1/72 inch). All four edges are independent;
    /// presets and unit helpers are provided.
    /// </summary>
    public readonly struct Margins : IEquatable<Margins>
    {
        private const double MmPerInch = 25.4;
        private const double PointsPerInch = 72.0;

        public double Left { get; }
        public double Top { get; }
        public double Right { get; }
        public double Bottom { get; }

        public Margins(double left, double top, double right, double bottom)
        {
            if (left < 0 || top < 0 || right < 0 || bottom < 0)
                throw new ArgumentOutOfRangeException(nameof(left), "Margins cannot be negative.");
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public static Margins Uniform(double points) => new(points, points, points, points);
        public static Margins FromInches(double left, double top, double right, double bottom)
            => new(left * PointsPerInch, top * PointsPerInch, right * PointsPerInch, bottom * PointsPerInch);
        public static Margins FromMillimetres(double left, double top, double right, double bottom)
            => new(left / MmPerInch * PointsPerInch, top / MmPerInch * PointsPerInch,
                   right / MmPerInch * PointsPerInch, bottom / MmPerInch * PointsPerInch);

        public static Margins None => new(0, 0, 0, 0);
        public static Margins Narrow => Uniform(0.25 * PointsPerInch);
        public static Margins Default => Uniform(0.5 * PointsPerInch);
        public static Margins Wide => Uniform(1.0 * PointsPerInch);

        public bool Equals(Margins other)
            => Left.Equals(other.Left) && Top.Equals(other.Top) && Right.Equals(other.Right) && Bottom.Equals(other.Bottom);
        public override bool Equals(object? obj) => obj is Margins m && Equals(m);
        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
        public static bool operator ==(Margins left, Margins right) => left.Equals(right);
        public static bool operator !=(Margins left, Margins right) => !left.Equals(right);
        public override string ToString()
            => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "L{0:0.##} T{1:0.##} R{2:0.##} B{3:0.##}", Left, Top, Right, Bottom);
    }
}
