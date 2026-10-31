using System.Buffers.Binary;
using Chuvadi.Print;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.Tests;

/// <summary>The OS-independent parts of Chuvadi.Print.Windows: DEVMODE editing, paper matching, geometry, DIB banding.</summary>
public class WindowsPortableTests
{
    private static readonly PrinterPaper Letter = new(1, "Letter", PaperSize.Letter);
    private static readonly PrinterPaper A4 = new(9, "A4", PaperSize.A4);
    private static readonly PrinterPaper A4Small = new(10, "A4 Small", PaperSize.A4);
    private static readonly PrinterPaper A5 = new(11, "A5", PaperSize.A5);
    private static readonly PrinterPaper[] Papers = { Letter, A4Small, A4, A5 };

    private static DevModeRequest Map(PrintSettings s, bool targetCopies = false, PaperMatching matching = PaperMatching.ExactOrCustom,
                                      PrinterResolution? resolution = null, PrintQuality? quality = null)
        => DevModeMapper.Map(s, Papers, matching, targetCopies, null, null, resolution, quality);

    // ------------------------------------------------------------------ paper matching

    [Fact]
    public void Paper_matches_driver_paper_by_size_preferring_name()
        => Assert.Equal((short)9, DevModeMapper.ResolvePaper(PaperSize.A4, Papers, PaperMatching.ExactOrCustom).PaperId);

    [Fact]
    public void Paper_match_ignores_orientation_of_the_preset()
        => Assert.Equal((short)9, DevModeMapper.ResolvePaper(PaperSize.A4.Rotate(), Papers, PaperMatching.ExactOrCustom).PaperId);

    [Fact]
    public void Unmatched_paper_becomes_custom_portrait_size_in_tenths_of_mm()
    {
        var result = DevModeMapper.ResolvePaper(PaperSize.FromMillimetres(150, 100), Papers, PaperMatching.ExactOrCustom);
        Assert.Equal((short)0, result.PaperId);
        Assert.Equal((short)1000, result.WidthTenthsMm);
        Assert.Equal((short)1500, result.LengthTenthsMm);
    }

    [Fact]
    public void Exact_only_rejects_unknown_paper_and_custom_only_skips_matching()
    {
        Assert.Throws<InvalidOperationException>(() => DevModeMapper.ResolvePaper(PaperSize.B5, Papers, PaperMatching.ExactOnly));
        var custom = DevModeMapper.ResolvePaper(PaperSize.A4, Papers, PaperMatching.CustomOnly);
        Assert.Equal((short)0, custom.PaperId);
        Assert.Equal((short)2100, custom.WidthTenthsMm);
        Assert.Equal((short)2970, custom.LengthTenthsMm);
    }

    // ------------------------------------------------------------------ settings -> request

    [Theory]
    [InlineData(PageOrientation.Portrait, 1)]
    [InlineData(PageOrientation.ReversePortrait, 1)]
    [InlineData(PageOrientation.Landscape, 2)]
    [InlineData(PageOrientation.ReverseLandscape, 2)]
    public void Orientation_maps_to_portrait_or_landscape(PageOrientation orientation, short expected)
        => Assert.Equal(expected, Map(new PrintSettings { Orientation = orientation }).Orientation);

    [Theory]
    [InlineData(Duplex.Simplex, 1)]
    [InlineData(Duplex.Vertical, 2)]
    [InlineData(Duplex.Horizontal, 3)]
    public void Duplex_maps_to_dmduplex(Duplex duplex, short expected)
        => Assert.Equal(expected, Map(new PrintSettings { Duplex = duplex }).Duplex);

    [Theory]
    [InlineData(ColorMode.Color, 2)]
    [InlineData(ColorMode.Grayscale, 1)]
    [InlineData(ColorMode.Monochrome, 1)]
    public void Colour_maps_to_dmcolor(ColorMode color, short expected)
        => Assert.Equal(expected, Map(new PrintSettings { Color = color }).Color);

    [Fact]
    public void Copies_go_to_driver_only_when_target_makes_them()
    {
        var s = new PrintSettings { Copies = 3, Collate = true };
        DevModeRequest software = Map(s, targetCopies: false);
        Assert.Equal((short)1, software.Copies);
        Assert.False(software.Collate);
        DevModeRequest driver = Map(s, targetCopies: true);
        Assert.Equal((short)3, driver.Copies);
        Assert.True(driver.Collate);
    }

    [Fact]
    public void Resolution_wins_over_quality_preset()
    {
        DevModeRequest explicitRes = Map(new PrintSettings(), resolution: new PrinterResolution(600, 300), quality: PrintQuality.Draft);
        Assert.Equal((short)600, explicitRes.PrintQuality);
        Assert.Equal((short)300, explicitRes.YResolution);
        DevModeRequest preset = Map(new PrintSettings(), quality: PrintQuality.High);
        Assert.Equal((short)-4, preset.PrintQuality);
        Assert.Null(preset.YResolution);
    }

    // ------------------------------------------------------------------ DEVMODE bytes

    [Fact]
    public void Apply_writes_fields_at_devmodew_offsets_and_sets_flags()
    {
        DevModeBuffer dm = DevModeBuffer.CreateBlank(driverExtra: 16);
        var bin = new PrinterBin(7, "Manual");
        var media = new PrinterMediaType(258, "Glossy");
        DevModeRequest request = DevModeMapper.Map(
            new PrintSettings { Orientation = PageOrientation.Landscape, Duplex = Duplex.Vertical, Copies = 2, Collate = true, Color = ColorMode.Grayscale },
            Papers, PaperMatching.ExactOrCustom, true, bin, media, new PrinterResolution(600, 600), null);
        DevModeMapper.Apply(request, dm);

        Assert.Equal(2, dm.ReadInt16(76));    // dmOrientation
        Assert.Equal(9, dm.ReadInt16(78));    // dmPaperSize (A4)
        Assert.Equal(100, dm.ReadInt16(84));  // dmScale
        Assert.Equal(2, dm.ReadInt16(86));    // dmCopies
        Assert.Equal(7, dm.ReadInt16(88));    // dmDefaultSource
        Assert.Equal(600, dm.ReadInt16(90));  // dmPrintQuality
        Assert.Equal(1, dm.ReadInt16(92));    // dmColor monochrome
        Assert.Equal(2, dm.ReadInt16(94));    // dmDuplex vertical
        Assert.Equal(600, dm.ReadInt16(96));  // dmYResolution
        Assert.Equal(1, dm.ReadInt16(100));   // dmCollate
        Assert.Equal(258u, dm.ReadUInt32(196)); // dmMediaType

        uint expected = DevModeBuffer.DM_ORIENTATION | DevModeBuffer.DM_PAPERSIZE | DevModeBuffer.DM_SCALE | DevModeBuffer.DM_COPIES
                        | DevModeBuffer.DM_DEFAULTSOURCE | DevModeBuffer.DM_PRINTQUALITY | DevModeBuffer.DM_COLOR | DevModeBuffer.DM_DUPLEX
                        | DevModeBuffer.DM_YRESOLUTION | DevModeBuffer.DM_COLLATE | DevModeBuffer.DM_MEDIATYPE;
        Assert.Equal(expected, dm.Fields);
    }

    [Fact]
    public void Custom_paper_sets_length_and_width_and_standard_paper_clears_them()
    {
        DevModeBuffer dm = DevModeBuffer.CreateBlank();
        DevModeMapper.Apply(Map(new PrintSettings { Paper = PaperSize.FromMillimetres(100, 150) }), dm);
        Assert.Equal(0, dm.ReadInt16(78));
        Assert.Equal(1000, dm.ReadInt16(82)); // dmPaperWidth
        Assert.Equal(1500, dm.ReadInt16(80)); // dmPaperLength
        Assert.NotEqual(0u, dm.Fields & (DevModeBuffer.DM_PAPERWIDTH | DevModeBuffer.DM_PAPERLENGTH));

        DevModeMapper.Apply(Map(new PrintSettings { Paper = PaperSize.A4 }), dm);
        Assert.Equal(9, dm.ReadInt16(78));
        Assert.Equal(0u, dm.Fields & (DevModeBuffer.DM_PAPERWIDTH | DevModeBuffer.DM_PAPERLENGTH));
    }

    [Fact]
    public void Older_short_devmode_keeps_fields_it_lacks_untouched()
    {
        DevModeBuffer dm = DevModeBuffer.CreateBlank(publicSize: 156, driverExtra: 8);
        Assert.False(dm.SetUInt32(DevModeBuffer.OffsetMediaType, DevModeBuffer.DM_MEDIATYPE, 5));
        Assert.True(dm.SetInt16(DevModeBuffer.OffsetDuplex, DevModeBuffer.DM_DUPLEX, 2));
        Assert.Equal(0u, dm.Fields & DevModeBuffer.DM_MEDIATYPE);
    }

    [Fact]
    public void Driver_private_bytes_survive_editing()
    {
        DevModeBuffer dm = DevModeBuffer.CreateBlank(driverExtra: 4);
        dm.Bytes[220] = 0xDE; dm.Bytes[221] = 0xAD; dm.Bytes[222] = 0xBE; dm.Bytes[223] = 0xEF;
        DevModeMapper.Apply(Map(new PrintSettings { Duplex = Duplex.Horizontal }), dm);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, dm.Bytes[220..224]);
    }

    [Fact]
    public void Devmode_rejects_truncated_buffers()
    {
        Assert.Throws<ArgumentException>(() => new DevModeBuffer(new byte[40]));
        var bytes = new byte[220];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(68), 220);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(70), 64); // claims 64 private bytes that are not there
        Assert.Throws<ArgumentException>(() => new DevModeBuffer(bytes));
    }

    // ------------------------------------------------------------------ geometry

    [Fact]
    public void Geometry_reports_hard_margins_and_maps_points_to_device()
    {
        var g = new DeviceGeometry(600, 600, 4960, 7016, 100, 120, 4760, 6776);
        Assert.Equal(new Margins(12, 14.4, 12, 14.4), g.HardMargins);
        Assert.True(g.SheetMatches(PageDimensions.FromPaper(PaperSize.A4)));
        Assert.False(g.SheetMatches(PageDimensions.FromPaper(PaperSize.Letter)));
        Assert.Equal((500, 480, 1200, 600), g.ToDevice(new RectanglePoints(72, 72, 144, 72)));
    }

    [Fact]
    public void Geometry_rounds_edges_so_adjacent_rectangles_tile()
    {
        var g = new DeviceGeometry(300, 300, 2480, 3508, 0, 0, 2480, 3508);
        var left = g.ToDevice(new RectanglePoints(10.1, 0, 33.33, 10));
        var right = g.ToDevice(new RectanglePoints(43.43, 0, 20, 10));
        Assert.Equal(left.X + left.Width, right.X);
    }

    // ------------------------------------------------------------------ DIB banding

    [Fact]
    public void Rgb24_rows_become_aligned_bgr()
    {
        byte[] pixels = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
        using var raster = new RasterPage(3, 2, 9, RasterPixelFormat.Rgb24, 72, 72, pixels);
        var dib = new DibBand(raster);
        Assert.Equal(24, dib.BitCount);
        Assert.Equal(12, dib.Stride);
        var band = new byte[24];
        dib.FillBand(0, 2, band);
        Assert.Equal(new byte[] { 3, 2, 1, 6, 5, 4, 9, 8, 7, 0, 0, 0, 12, 11, 10, 15, 14, 13, 18, 17, 16, 0, 0, 0 }, band);
    }

    [Fact]
    public void Bgra32_composites_transparency_over_white()
    {
        byte[] pixels = { 0, 0, 0, 0, 10, 20, 30, 255 };
        using var raster = new RasterPage(2, 1, 8, RasterPixelFormat.Bgra32, 72, 72, pixels);
        var band = new byte[8];
        new DibBand(raster).FillBand(0, 1, band);
        Assert.Equal(new byte[] { 255, 255, 255, 0, 10, 20, 30, 0 }, band);
    }

    [Fact]
    public void Bilevel_rows_are_aligned_and_spare_bits_cleared()
    {
        using var raster = new RasterPage(10, 1, 2, RasterPixelFormat.BlackWhite1, 72, 72, new byte[] { 0xFF, 0xFF });
        var dib = new DibBand(raster);
        Assert.Equal(4, dib.Stride);
        var band = new byte[4];
        dib.FillBand(0, 1, band);
        Assert.Equal(new byte[] { 0xFF, 0xC0, 0, 0 }, band);
    }

    [Fact]
    public void Bitmap_info_is_top_down_with_correct_palette()
    {
        using var gray = new RasterPage(5, 4, 5, RasterPixelFormat.Gray8, 300, 300, new byte[20]);
        byte[] g = new DibBand(gray).BuildBitmapInfo(4);
        Assert.Equal(40 + 256 * 4, g.Length);
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(g));
        Assert.Equal(5, BinaryPrimitives.ReadInt32LittleEndian(g.AsSpan(4)));
        Assert.Equal(-4, BinaryPrimitives.ReadInt32LittleEndian(g.AsSpan(8)));
        Assert.Equal(8, BinaryPrimitives.ReadInt16LittleEndian(g.AsSpan(14)));
        Assert.Equal(new byte[] { 128, 128, 128, 0 }, g[(40 + 128 * 4)..(40 + 129 * 4)]);

        using var bw = new RasterPage(8, 1, 1, RasterPixelFormat.BlackWhite1, 300, 300, new byte[1]);
        byte[] b = new DibBand(bw).BuildBitmapInfo(1);
        Assert.Equal(new byte[] { 255, 255, 255, 0, 0, 0, 0, 0 }, b[40..48]);

        using var color = new RasterPage(2, 2, 8, RasterPixelFormat.Bgra32, 300, 300, new byte[16]);
        Assert.Equal(40, new DibBand(color).BuildBitmapInfo(2).Length);
    }

    [Fact]
    public void Bands_tile_the_destination_exactly()
    {
        using var raster = RasterPage.Rent(100, 997, RasterPixelFormat.Gray8, 300, 300);
        var dib = new DibBand(raster, bandBytes: 100 * 64);
        Assert.Equal(64, dib.RowsPerBand);
        Assert.Equal(16, dib.BandCount);
        int expectedY = 50;
        for (int first = 0; first < 997; first += dib.RowsPerBand)
        {
            int rows = Math.Min(dib.RowsPerBand, 997 - first);
            (int y, int h) = dib.DestinationRows(first, rows, 50, 2311);
            Assert.Equal(expectedY, y);
            expectedY = y + h;
        }
        Assert.Equal(50 + 2311, expectedY);
    }

    [Fact]
    public void Capabilities_cap_render_dpi()
    {
        var caps = new WindowsPrinterCapabilities("x", Papers, Array.Empty<PrinterBin>(), Array.Empty<PrinterMediaType>(),
            Array.Empty<PrinterResolution>(), 99, true, true, false, 90, null, null,
            new DeviceGeometry(1200, 1200, 9921, 14031, 50, 50, 9821, 13931));
        PrintTargetCapabilities capped = caps.ToTargetCapabilities();
        Assert.Equal(300, capped.DpiX);
        Assert.True(capped.SupportsCopies && capped.SupportsCollate && capped.SupportsDuplex && !capped.SupportsColor);
        Assert.Equal(1200, caps.ToTargetCapabilities(null).DpiX);
        Assert.Equal(3.0, caps.HardMargins.Left);
    }
}
