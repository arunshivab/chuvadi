using Chuvadi.Print;

namespace Chuvadi.Print.Tests;

public class A2PrimitiveTests
{
    [Theory]
    [InlineData(PageRotation.None, PageRotation.None, PageRotation.None)]
    [InlineData(PageRotation.Clockwise90, PageRotation.Clockwise90, PageRotation.Rotate180)]
    [InlineData(PageRotation.Rotate180, PageRotation.Clockwise90, PageRotation.Clockwise270)]
    [InlineData(PageRotation.Clockwise270, PageRotation.Clockwise90, PageRotation.None)]
    public void Rotations_combine_clockwise(PageRotation a, PageRotation b, PageRotation expected)
        => Assert.Equal(expected, PageLayout.Combine(a, b));

    [Theory]
    [InlineData(RasterPixelFormat.Bgra32, 10, 40)]
    [InlineData(RasterPixelFormat.Rgb24, 10, 30)]
    [InlineData(RasterPixelFormat.Gray8, 10, 10)]
    [InlineData(RasterPixelFormat.BlackWhite1, 10, 2)]
    [InlineData(RasterPixelFormat.BlackWhite1, 8, 1)]
    public void Minimum_stride_matches_format(RasterPixelFormat format, int width, int expected)
        => Assert.Equal(expected, RasterPage.MinimumStride(width, format));

    [Fact]
    public void Raster_page_validates_and_releases()
    {
        Assert.Throws<ArgumentException>(() => new RasterPage(10, 10, 10, RasterPixelFormat.Gray8, 72, 72, new byte[50]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterPage(10, 10, 5, RasterPixelFormat.Gray8, 72, 72, new byte[100]));

        RasterPage page = RasterPage.Rent(3, 2, RasterPixelFormat.Rgb24, 72, 72);
        Assert.Equal(18, page.Pixels.Length);
        Assert.All(page.Pixels.ToArray(), b => Assert.Equal(0, b));
        page.Dispose();
        Assert.True(page.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => page.Pixels.Length);
        page.Dispose(); // idempotent
    }

    [Fact]
    public void Value_types_reject_invalid_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageDimensions(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageDimensions(10, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectanglePoints(0, 0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterRequest { DpiX = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrintTargetCapabilities { DpiY = -5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrintJobOptions { RenderDpiX = 0 });
    }

    [Theory]
    [InlineData(255, 255, 255, 255, 255)]   // opaque white
    [InlineData(0, 0, 0, 255, 0)]           // opaque black
    [InlineData(0, 0, 0, 0, 255)]           // fully transparent black composites to white paper
    [InlineData(0, 0, 255, 255, 76)]        // pure red (B,G,R order) -> BT.601 luma
    public void Bgra_to_gray_composites_over_white(byte b, byte g, byte r, byte a, byte expected)
    {
        using RasterPage src = new(1, 1, 4, RasterPixelFormat.Bgra32, 72, 72, new[] { b, g, r, a });
        using RasterPage gray = PixelConverter.Convert(src, RasterPixelFormat.Gray8, MonochromeConversion.Threshold, 128);
        Assert.Equal(expected, gray.Pixels[0]);
    }

    [Fact]
    public void Threshold_sets_ink_bits_msb_first()
    {
        byte[] row = { 0, 255, 0, 255, 255, 255, 255, 255, 0 };
        using RasterPage src = new(9, 1, 9, RasterPixelFormat.Gray8, 72, 72, row);
        using RasterPage bw = PixelConverter.Convert(src, RasterPixelFormat.BlackWhite1, MonochromeConversion.Threshold, 128);
        Assert.Equal(2, bw.Stride);
        Assert.Equal(0b1010_0000, bw.Pixels[0]);
        Assert.Equal(0b1000_0000, bw.Pixels[1]);
    }

    [Fact]
    public void Dithering_preserves_average_tone()
    {
        const int size = 64;
        byte[] mid = new byte[size * size];
        Array.Fill(mid, (byte)128);
        using RasterPage src = new(size, size, size, RasterPixelFormat.Gray8, 72, 72, mid);
        foreach (MonochromeConversion mode in new[] { MonochromeConversion.OrderedDither, MonochromeConversion.ErrorDiffusion })
        {
            using RasterPage bw = PixelConverter.Convert(src, RasterPixelFormat.BlackWhite1, mode, 128);
            int black = 0;
            foreach (byte value in bw.Pixels) black += System.Numerics.BitOperations.PopCount(value);
            double fraction = black / (double)(size * size);
            Assert.InRange(fraction, 0.45, 0.55);
        }
    }

    [Fact]
    public void Black_white_expands_back_to_color()
    {
        using RasterPage src = new(2, 1, 1, RasterPixelFormat.BlackWhite1, 72, 72, new byte[] { 0b1000_0000 });
        using RasterPage rgb = PixelConverter.Convert(src, RasterPixelFormat.Rgb24, MonochromeConversion.Threshold, 128);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255 }, rgb.Pixels.ToArray());
    }

    [Fact]
    public void Crc32_matches_known_vector()
        => Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));

    [Fact]
    public void Plan_rejects_invalid_settings()
    {
        Assert.Throws<InvalidOperationException>(() => PrintPlan.Create(3, new PrintSettings { Copies = 0 }, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrintPlan.Create(-1, new PrintSettings(), false));
    }
}
