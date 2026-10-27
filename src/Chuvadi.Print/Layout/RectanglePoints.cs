using System;
using System.Globalization;

namespace Chuvadi.Print
{
    /// <summary>
    /// A rectangle in points (1/72 inch), measured from the top-left corner of the sheet with
    /// X increasing to the right and Y increasing downwards.
    /// </summary>
    public readonly struct RectanglePoints : IEquatable<RectanglePoints>
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public RectanglePoints(double x, double y, double width, double height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width), "Width cannot be negative.");
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height), "Height cannot be negative.");
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double Left => X;
        public double Top => Y;
        public double Right => X + Width;
        public double Bottom => Y + Height;

        /// <summary>True when <paramref name="other"/> lies entirely inside this rectangle (within a small tolerance).</summary>
        public bool Contains(RectanglePoints other, double tolerance = 1e-6)
            => other.Left >= Left - tolerance && other.Top >= Top - tolerance
               && other.Right <= Right + tolerance && other.Bottom <= Bottom + tolerance;

        public bool Equals(RectanglePoints other)
            => X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);
        public override bool Equals(object? obj) => obj is RectanglePoints r && Equals(r);
        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
        public static bool operator ==(RectanglePoints left, RectanglePoints right) => left.Equals(right);
        public static bool operator !=(RectanglePoints left, RectanglePoints right) => !left.Equals(right);
        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##} {2:0.##}x{3:0.##}pt)", X, Y, Width, Height);
    }
}
