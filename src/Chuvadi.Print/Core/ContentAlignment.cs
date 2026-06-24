using System;

namespace Chuvadi.Print
{
    /// <summary>
    /// Two-axis placement of content on the sheet. Every combination is available
    /// (Left/Center/Right x Top/Center/Bottom = nine positions), with named presets.
    /// </summary>
    public readonly struct ContentAlignment : IEquatable<ContentAlignment>
    {
        public HorizontalAlignment Horizontal { get; }
        public VerticalAlignment Vertical { get; }

        public ContentAlignment(HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            Horizontal = horizontal;
            Vertical = vertical;
        }

        public static ContentAlignment TopLeft => new(HorizontalAlignment.Left, VerticalAlignment.Top);
        public static ContentAlignment TopCenter => new(HorizontalAlignment.Center, VerticalAlignment.Top);
        public static ContentAlignment TopRight => new(HorizontalAlignment.Right, VerticalAlignment.Top);
        public static ContentAlignment CenterLeft => new(HorizontalAlignment.Left, VerticalAlignment.Center);
        public static ContentAlignment Center => new(HorizontalAlignment.Center, VerticalAlignment.Center);
        public static ContentAlignment CenterRight => new(HorizontalAlignment.Right, VerticalAlignment.Center);
        public static ContentAlignment BottomLeft => new(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        public static ContentAlignment BottomCenter => new(HorizontalAlignment.Center, VerticalAlignment.Bottom);
        public static ContentAlignment BottomRight => new(HorizontalAlignment.Right, VerticalAlignment.Bottom);

        public bool Equals(ContentAlignment other) => Horizontal == other.Horizontal && Vertical == other.Vertical;
        public override bool Equals(object? obj) => obj is ContentAlignment a && Equals(a);
        public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical);
        public static bool operator ==(ContentAlignment left, ContentAlignment right) => left.Equals(right);
        public static bool operator !=(ContentAlignment left, ContentAlignment right) => !left.Equals(right);
        public override string ToString() => Vertical + "-" + Horizontal;
    }
}
