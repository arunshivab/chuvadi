using System;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// A printer device context's geometry (from GetDeviceCaps): resolution, full sheet and printable
    /// region in device pixels. Converts sheet coordinates in points to device coordinates.
    /// </summary>
    internal readonly struct DeviceGeometry
    {
        public DeviceGeometry(int dpiX, int dpiY, int physicalWidth, int physicalHeight,
                              int offsetX, int offsetY, int printableWidth, int printableHeight)
        {
            if (dpiX <= 0) throw new ArgumentOutOfRangeException(nameof(dpiX));
            if (dpiY <= 0) throw new ArgumentOutOfRangeException(nameof(dpiY));
            if (physicalWidth <= 0) throw new ArgumentOutOfRangeException(nameof(physicalWidth));
            if (physicalHeight <= 0) throw new ArgumentOutOfRangeException(nameof(physicalHeight));
            DpiX = dpiX;
            DpiY = dpiY;
            PhysicalWidth = physicalWidth;
            PhysicalHeight = physicalHeight;
            OffsetX = Math.Max(0, offsetX);
            OffsetY = Math.Max(0, offsetY);
            PrintableWidth = Math.Max(1, printableWidth);
            PrintableHeight = Math.Max(1, printableHeight);
        }

        public int DpiX { get; }
        public int DpiY { get; }

        /// <summary>Full sheet, device pixels (PHYSICALWIDTH / PHYSICALHEIGHT).</summary>
        public int PhysicalWidth { get; }
        public int PhysicalHeight { get; }

        /// <summary>Top-left of the printable region relative to the sheet, device pixels (PHYSICALOFFSETX/Y).</summary>
        public int OffsetX { get; }
        public int OffsetY { get; }

        /// <summary>Printable region, device pixels (HORZRES / VERTRES).</summary>
        public int PrintableWidth { get; }
        public int PrintableHeight { get; }

        /// <summary>The sheet in points.</summary>
        public PageDimensions SheetSize => new(PhysicalWidth * 72.0 / DpiX, PhysicalHeight * 72.0 / DpiY);

        /// <summary>The non-printable edges in points.</summary>
        public Margins HardMargins => new(
            OffsetX * 72.0 / DpiX,
            OffsetY * 72.0 / DpiY,
            Math.Max(0, PhysicalWidth - PrintableWidth - OffsetX) * 72.0 / DpiX,
            Math.Max(0, PhysicalHeight - PrintableHeight - OffsetY) * 72.0 / DpiY);

        /// <summary>
        /// Maps a sheet rectangle in points to device coordinates. GDI's origin is the top-left of the
        /// printable region, so the physical offset is subtracted. Edges are rounded independently so
        /// adjacent rectangles never leave gaps.
        /// </summary>
        public (int X, int Y, int Width, int Height) ToDevice(RectanglePoints rect)
        {
            int left = (int)Math.Round(rect.Left * DpiX / 72.0);
            int top = (int)Math.Round(rect.Top * DpiY / 72.0);
            int right = (int)Math.Round(rect.Right * DpiX / 72.0);
            int bottom = (int)Math.Round(rect.Bottom * DpiY / 72.0);
            return (left - OffsetX, top - OffsetY, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        /// <summary>True when the device sheet matches <paramref name="expected"/> within 1.5 mm on each edge.</summary>
        public bool SheetMatches(PageDimensions expected)
        {
            const double tolerance = 1.5 / 25.4 * 72.0;
            PageDimensions actual = SheetSize;
            return Math.Abs(actual.WidthPoints - expected.WidthPoints) <= tolerance
                && Math.Abs(actual.HeightPoints - expected.HeightPoints) <= tolerance;
        }
    }
}
