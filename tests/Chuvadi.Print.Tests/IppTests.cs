using System.Buffers.Binary;
using System.Text;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.ManualTests;

namespace Chuvadi.Print.Tests;

public class IppCodecTests
{
    [Fact]
    public void Request_encodes_to_rfc8010_bytes()
    {
        IppMessage request = IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://h/p");
        var expected = new List<byte> { 0x02, 0x00, 0x00, 0x0B, 0x00, 0x00, 0x00, 0x01, 0x01 };
        void Attr(byte tag, string name, string value)
        {
            expected.Add(tag);
            expected.Add((byte)(name.Length >> 8)); expected.Add((byte)name.Length);
            expected.AddRange(Encoding.ASCII.GetBytes(name));
            expected.Add((byte)(value.Length >> 8)); expected.Add((byte)value.Length);
            expected.AddRange(Encoding.ASCII.GetBytes(value));
        }
        Attr(0x47, "attributes-charset", "utf-8");
        Attr(0x48, "attributes-natural-language", "en");
        Attr(0x45, "printer-uri", "ipp://h/p");
        expected.Add(0x03);
        Assert.Equal(expected.ToArray(), request.ToArray());
    }

    [Fact]
    public void Every_value_type_round_trips_byte_for_byte()
    {
        var message = new IppMessage((short)IppStatusCode.SuccessfulOkIgnoredOrSubstitutedAttributes, 77, 1, 1);
        message.AddGroup(IppTag.OperationAttributes)
            .Add("attributes-charset", IppValue.Charset("utf-8"))
            .Add("status-message", IppValue.TextWithLanguage("ta", "சுவடி அச்சு"));
        var mediaSize = new IppCollection().Add("x-dimension", IppValue.Integer(21000)).Add("y-dimension", IppValue.Integer(29700));
        var mediaCol = new IppCollection()
            .Add("media-size", IppValue.Collection(mediaSize))
            .Add("media-source", IppValue.Keyword("tray-1"))
            .Add("media-type", IppValue.Keyword("labels"), IppValue.Keyword("stationery"));
        message.AddGroup(IppTag.JobAttributes)
            .Add("copies", IppValue.Integer(-5))
            .Add("job-hold", IppValue.Boolean(true))
            .Add("job-state", IppValue.Enum(9))
            .Add("sides", IppValue.Keyword("one-sided"), IppValue.Keyword("two-sided-long-edge"))
            .Add("job-name", IppValue.NameWithLanguage("en", "Discharge summary"))
            .Add("job-originating-user-name", IppValue.Name("dr.arun"))
            .Add("blob", IppValue.OctetString(new byte[] { 0, 1, 255 }))
            .Add("time-at-creation", IppValue.DateTime(new DateTimeOffset(2026, 10, 28, 6, 42, 15, 300, TimeSpan.FromMinutes(330))))
            .Add("printer-resolution", IppValue.Resolution(118, 236, IppResolutionUnits.DotsPerCentimetre))
            .Add("page-ranges", IppValue.Range(1, 3), IppValue.Range(5, 5))
            .Add("media-col", IppValue.Collection(mediaCol))
            .Add("media-col-database", IppValue.Collection(mediaCol), IppValue.Collection(new IppCollection().Add("media-key", IppValue.Keyword("k"))))
            .Add("job-message", IppValue.OutOfBand(IppTag.NoValue))
            .Add("vendor-thing", IppValue.Raw((IppTag)0x4B, new byte[] { 9, 8, 7 }))
            .Add("vendor-ext", IppValue.Raw(IppTag.Extension, new byte[] { 1, 2 }, extensionTag: 0x40000001));
        message.AddGroup(IppTag.JobAttributes).Add("job-id", IppValue.Integer(2));

        byte[] first = message.ToArray();
        IppMessage back = IppMessage.Parse(first, out int length);
        Assert.Equal(first.Length, length);
        Assert.Equal(first, back.ToArray());
        Assert.Equal(77, back.RequestId);
        Assert.Equal(1, back.VersionMajor);
        Assert.Equal(3, back.Groups.Count);

        IppAttributeGroup job = back.Group(IppTag.JobAttributes)!;
        Assert.Equal("சுவடி அச்சு", back.Group(IppTag.OperationAttributes)!["status-message"]!.Value.AsString());
        Assert.Equal(-5, job["copies"]!.Value.AsInteger());
        Assert.Equal(2, job["sides"]!.Values.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 6, 42, 15, 300, TimeSpan.FromMinutes(330)), job["time-at-creation"]!.Value.AsDateTime());
        Assert.Equal(new IppResolution(118, 236, IppResolutionUnits.DotsPerCentimetre), job["printer-resolution"]!.Value.AsResolution());
        IppCollection col = job["media-col"]!.Value.AsCollection();
        Assert.Equal(29700, col["media-size"]!.Value.AsCollection()["y-dimension"]!.Value.AsInteger());
        Assert.Equal(2, col["media-type"]!.Values.Count);
        Assert.Equal(2, job["media-col-database"]!.Values.Count);
        Assert.True(job["job-message"]!.Value.IsOutOfBand);
        Assert.Equal(0x40000001, job["vendor-ext"]!.Value.ExtensionTag);
        Assert.Equal(2, back.GroupsOf(IppTag.JobAttributes).Count());
    }

    [Fact]
    public void Document_data_after_the_message_is_separated()
    {
        byte[] head = IppMessage.CreateRequest(IppOperation.PrintJob, 3, "ipp://x/y").ToArray();
        byte[] all = head.Concat(new byte[] { 0x25, 0x50, 0x44, 0x46 }).ToArray();
        IppMessage.ReadFrom(new MemoryStream(all), out byte[] data);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, data);
    }

    [Theory]
    [InlineData(new byte[] { 2, 0, 0, 11 })]                                  // truncated header
    [InlineData(new byte[] { 2, 0, 0, 11, 0, 0, 0, 1, 1, 0x21, 0, 1, 0x61, 0, 4, 0, 0 })] // truncated integer
    [InlineData(new byte[] { 2, 0, 0, 11, 0, 0, 0, 1, 0x21, 0, 1, 0x61, 0, 4, 0, 0, 0, 1, 3 })] // attribute before group
    [InlineData(new byte[] { 2, 0, 0, 11, 0, 0, 0, 1, 1, 0x21, 0, 1, 0x61, 0, 2, 0, 1, 3 })] // integer of 2 bytes
    public void Malformed_messages_are_rejected(byte[] bytes)
        => Assert.Throws<IppFormatException>(() => IppMessage.Parse(bytes));

    [Theory]
    [InlineData("ipp://printer.local/ipp/print", "http://printer.local:631/ipp/print")]
    [InlineData("ipps://printer.local/ipp/print", "https://printer.local:631/ipp/print")]
    [InlineData("ipp://10.0.0.5:8631/printers/ward3", "http://10.0.0.5:8631/printers/ward3")]
    [InlineData("http://10.0.0.5/ipp", "http://10.0.0.5/ipp")]
    public void Printer_uris_map_to_http(string printer, string expected)
        => Assert.Equal(new Uri(expected), IppClient.ToHttpUri(printer));

    [Fact]
    public void Unsupported_uri_scheme_is_rejected()
        => Assert.Throws<ArgumentException>(() => IppClient.ToHttpUri("lpd://printer/queue"));

    [Fact]
    public void Group_set_replaces_in_place_and_remove_deletes()
    {
        var g = new IppAttributeGroup(IppTag.PrinterAttributes).Add("a", IppValue.Integer(1)).Add("b", IppValue.Integer(2));
        g.Set("a", IppValue.Integer(9));
        Assert.Equal("a", g.Attributes[0].Name);
        Assert.Equal(9, g["a"]!.Value.AsInteger());
        Assert.True(g.Remove("b"));
        Assert.Single(g.Attributes);
    }
}

public class PwgRasterTests
{
    [Fact]
    public void Pages_round_trip_in_every_colour_space()
    {
        var pages = new List<(PwgPageHeader Header, byte[] Pixels)>();
        var rng = new Random(7);
        foreach (PwgColorSpace space in new[] { PwgColorSpace.SGray8, PwgColorSpace.Black1, PwgColorSpace.SRgb8 })
        {
            var header = new PwgPageHeader { ColorSpace = space, Width = 37, Height = 9, DpiX = 300, DpiY = 600,
                                             PageWidthPoints = 595, PageHeightPoints = 842, PageSizeName = "iso_a4_210x297mm",
                                             Duplex = true, Tumble = true, TotalPageCount = 3, FeedTransform = -1, PrintQuality = 5 };
            var pixels = new byte[header.BytesPerLine * header.Height];
            rng.NextBytes(pixels);
            pixels.AsSpan(header.BytesPerLine * 2, header.BytesPerLine * 3).Fill(header.ColorSpace == PwgColorSpace.Black1 ? (byte)0 : (byte)255); // white lines
            pixels.AsSpan(header.BytesPerLine * 6, header.BytesPerLine).Fill(0x33);                                                         // a uniform line
            if (space == PwgColorSpace.Black1)
                for (int y = 0; y < header.Height; y++) pixels[y * header.BytesPerLine + header.BytesPerLine - 1] &= 0xF8; // spare bits clear
            pages.Add((header, pixels));
        }

        var ms = new MemoryStream();
        var writer = new PwgRasterWriter(ms);
        foreach (var (header, pixels) in pages)
        {
            writer.BeginPage(header);
            for (int y = 0; y < header.Height; y++) writer.WriteLine(pixels.AsSpan(y * header.BytesPerLine, header.BytesPerLine));
            writer.EndPage();
        }
        Assert.Equal(3, writer.PagesWritten);

        ms.Position = 0;
        var read = new PwgRasterReader(ms).ReadAll();
        Assert.Equal(3, read.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(pages[i].Pixels, read[i].Pixels);
            PwgPageHeader h = read[i].Header;
            Assert.Equal(pages[i].Header.ColorSpace, h.ColorSpace);
            Assert.Equal((37, 9, 300, 600, 595, 842), (h.Width, h.Height, h.DpiX, h.DpiY, h.PageWidthPoints, h.PageHeightPoints));
            Assert.Equal("iso_a4_210x297mm", h.PageSizeName);
            Assert.True(h.Duplex && h.Tumble);
            Assert.Equal((3, 1, -1, 5), (h.TotalPageCount, h.CrossFeedTransform, h.FeedTransform, h.PrintQuality));
        }
    }

    [Fact]
    public void Header_fields_sit_at_pwg_5102_4_offsets()
    {
        byte[] h = new PwgPageHeader { ColorSpace = PwgColorSpace.SRgb8, Width = 100, Height = 50, DpiX = 300, DpiY = 300,
                                       PageWidthPoints = 612, PageHeightPoints = 792, PageSizeName = "na_letter_8.5x11in",
                                       MediaType = "stationery" }.Encode();
        Assert.Equal(1796, h.Length);
        uint U(int offset) => BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(offset));
        Assert.Equal("stationery", Encoding.ASCII.GetString(h, 128, 10));
        Assert.Equal((300u, 300u), (U(276), U(280)));
        Assert.Equal((612u, 792u), (U(352), U(356)));
        Assert.Equal((100u, 50u), (U(372), U(376)));
        Assert.Equal((8u, 24u, 300u), (U(384), U(388), U(392)));
        Assert.Equal((19u, 3u), (U(400), U(420)));
        Assert.Equal("na_letter_8.5x11in", Encoding.ASCII.GetString(h, 1732, 18));
    }

    [Fact]
    public void Line_encoding_uses_runs_literals_and_white_fill()
    {
        Span<byte> output = stackalloc byte[64];
        int n = PwgRasterWriter.EncodeLine(new byte[] { 5, 5, 5, 1, 2, 255, 255, 255 }, 1, 255, output);
        Assert.Equal(new byte[] { 2, 5, 255, 1, 2, 128 }, output[..n].ToArray()); // 3x5, literal [1,2], white fill
        n = PwgRasterWriter.EncodeLine(new byte[] { 9, 255 }, 1, 255, output);
        Assert.Equal(new byte[] { 0, 9, 128 }, output[..n].ToArray());             // single unit as 1-unit repeat
        n = PwgRasterWriter.EncodeLine(new byte[] { 255, 255, 255 }, 1, 255, output);
        Assert.Equal(new byte[] { 128 }, output[..n].ToArray());                   // all white
    }

    [Fact]
    public void Identical_lines_are_merged_with_a_repeat_count()
    {
        var ms = new MemoryStream();
        var writer = new PwgRasterWriter(ms);
        var header = new PwgPageHeader { ColorSpace = PwgColorSpace.SGray8, Width = 1000, Height = 1000, PageWidthPoints = 240, PageHeightPoints = 240 };
        writer.BeginPage(header);
        var line = new byte[1000];
        for (int y = 0; y < 1000; y++) writer.WriteLine(line);
        writer.EndPage();
        Assert.True(ms.Length < 4 + 1796 + 4 * 20, "1000 black lines should compress to a few bytes, got " + ms.Length);
        ms.Position = 0;
        Assert.All(new PwgRasterReader(ms).ReadPage()!.Value.Pixels, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Writer_enforces_page_geometry_and_reader_rejects_foreign_data()
    {
        var writer = new PwgRasterWriter(new MemoryStream());
        writer.BeginPage(new PwgPageHeader { Width = 4, Height = 2 });
        Assert.Throws<ArgumentException>(() => writer.WriteLine(new byte[3]));
        writer.WriteLine(new byte[4]);
        Assert.Throws<InvalidOperationException>(() => writer.EndPage());
        Assert.Throws<InvalidDataException>(() => new PwgRasterReader(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7"))));
    }
}

public class IppMappingTests
{
    [Theory]
    [InlineData(PaperSizeId.A4, "iso_a4_210x297mm")]
    [InlineData(PaperSizeId.Letter, "na_letter_8.5x11in")]
    [InlineData(PaperSizeId.Legal, "na_legal_8.5x14in")]
    [InlineData(PaperSizeId.Ledger, "na_ledger_11x17in")]
    [InlineData(PaperSizeId.Custom, "custom_100x150mm_100x150mm")]
    public void Paper_maps_to_pwg_media_name(PaperSizeId id, string expected)
    {
        PaperSize paper = id switch
        {
            PaperSizeId.A4 => PaperSize.A4,
            PaperSizeId.Letter => PaperSize.Letter,
            PaperSizeId.Legal => PaperSize.Legal,
            PaperSizeId.Ledger => PaperSize.Ledger,
            _ => PaperSize.FromMillimetres(150, 100)
        };
        Assert.Equal(expected, PwgMediaNames.NameFor(paper));
    }

    public enum PaperSizeId { A4, Letter, Legal, Ledger, Custom }

    [Fact]
    public void Printer_media_name_wins_when_size_matches()
    {
        Assert.Equal("om_a4-label_210x297mm", PwgMediaNames.NameFor(PaperSize.A4, new[] { "custom_min_10x10mm", "om_a4-label_210x297mm" }));
        Assert.True(PwgMediaNames.TryParseSize("na_letter_8.5x11in", out double w, out double h));
        Assert.Equal((215.9, 279.4), (Math.Round(w, 1), Math.Round(h, 1)));
        Assert.False(PwgMediaNames.TryParseSize("custom_max_215.9x355.6mm", out _, out _));
        Assert.False(PwgMediaNames.TryParseSize("auto", out _, out _));
    }

    private static IppPrinterCapabilities Caps(Action<IppAttributeGroup>? configure = null)
    {
        IppAttributeGroup g = FakeIppPrinter.DefaultAttributes("ipp://x/y");
        configure?.Invoke(g);
        return new IppPrinterCapabilities(g);
    }

    private static (IppAttributeGroup Job, List<string> Approx) Ticket(PrintSettings s, IppPrinterCapabilities? caps = null,
                                                                      bool documentMode = true, bool copies = true, int? pages = null,
                                                                      IppJobOptions? options = null)
    {
        var approx = new List<string>();
        IppAttributeGroup job = IppJobTicket.Build(s, caps ?? Caps(), options, documentMode, copies, pages, new IppResolution(300, 300), approx);
        return (job, approx);
    }

    [Fact]
    public void Capabilities_are_parsed()
    {
        IppPrinterCapabilities caps = Caps();
        Assert.Equal("Chuvadi Fake IPP", caps.PrinterName);
        Assert.Equal(IppPrinterState.Idle, caps.State);
        Assert.True(caps.SupportsFormat("image/pwg-raster") && caps.SupportsDuplex && caps.SupportsColor && caps.SupportsCollatedCopies);
        Assert.Equal(99, caps.MaxCopies);
        Assert.Equal(3, caps.PwgRasterResolutions.Count);
        Assert.Equal(PwgSheetBack.Normal, caps.PwgRasterSheetBack);
        Assert.Equal(new Margins(0, 4.23 / 25.4 * 72, 0, 4.23 / 25.4 * 72), caps.MinimumMargins);
    }

    [Fact]
    public void Document_ticket_maps_every_expressible_setting()
    {
        var s = new PrintSettings
        {
            Copies = 3, Collate = false, Duplex = Duplex.Horizontal, Color = ColorMode.Monochrome,
            Orientation = PageOrientation.ReverseLandscape, Scale = ScaleMode.ShrinkToFit, Pages = PageSelection.Range("1-3, 5, 7-8"),
            Paper = PaperSize.Letter
        };
        var (job, approx) = Ticket(s, options: new IppJobOptions { Quality = IppPrintQuality.High });
        Assert.Equal(3, job["copies"]!.Value.AsInteger());
        Assert.Equal("separate-documents-uncollated-copies", job["multiple-document-handling"]!.Value.AsString());
        Assert.Equal("two-sided-short-edge", job["sides"]!.Value.AsString());
        Assert.Equal("na_letter_8.5x11in", job["media"]!.Value.AsString());
        Assert.Equal("bi-level", job["print-color-mode"]!.Value.AsString());
        Assert.Equal(5, job["orientation-requested"]!.Value.AsInteger());
        Assert.Equal(new[] { new IppRange(1, 3), new IppRange(5, 5), new IppRange(7, 8) }, job["page-ranges"]!.Values.Select(v => v.AsRange()));
        Assert.Equal("auto-fit", job["print-scaling"]!.Value.AsString());
        Assert.Equal(5, job["print-quality"]!.Value.AsInteger());
        Assert.Null(job["printer-resolution"]);
        Assert.Empty(approx);
    }

    [Fact]
    public void Unexpressible_settings_are_reported_or_rejected_when_strict()
    {
        var s = new PrintSettings { Alignment = ContentAlignment.TopLeft, Margins = Margins.Wide, AutoRotate = true, Scale = ScaleMode.Custom, Pages = PageSelection.Odd };
        var (job, approx) = Ticket(s);
        Assert.Equal(5, approx.Count);
        Assert.Contains(approx, a => a.Contains("needs the page count"));
        Assert.Null(job["page-ranges"]);
        Assert.Equal("fit", job["print-scaling"]!.Value.AsString());

        var (withCount, _) = Ticket(s, pages: 6);
        Assert.Equal(new[] { new IppRange(1, 1), new IppRange(3, 3), new IppRange(5, 5) }, withCount["page-ranges"]!.Values.Select(v => v.AsRange()));

        Assert.Throws<NotSupportedException>(() => Ticket(s, options: new IppJobOptions { Strict = true }));
    }

    [Fact]
    public void Printer_limits_degrade_gracefully()
    {
        IppPrinterCapabilities simplex = Caps(g =>
        {
            g.Set("sides-supported", IppValue.Keyword("one-sided"));
            g.Set("print-color-mode-supported", IppValue.Keyword("monochrome"));
            g.Set("copies-supported", IppValue.Range(1, 2));
        });
        var (job, approx) = Ticket(new PrintSettings { Duplex = Duplex.Vertical, Color = ColorMode.Color, Copies = 5 }, simplex);
        Assert.Equal("one-sided", job["sides"]!.Value.AsString());
        Assert.Equal("monochrome", job["print-color-mode"]!.Value.AsString());
        Assert.Equal(2, job["copies"]!.Value.AsInteger());
        Assert.Equal(3, approx.Count);
    }

    [Fact]
    public void Tray_and_media_type_use_media_col()
    {
        var (job, _) = Ticket(new PrintSettings(), options: new IppJobOptions { MediaSource = "tray-1", MediaType = "labels" });
        Assert.Null(job["media"]);
        IppCollection col = job["media-col"]!.Value.AsCollection();
        IppCollection size = col["media-size"]!.Value.AsCollection();
        Assert.Equal((21000, 29700), (size["x-dimension"]!.Value.AsInteger(), size["y-dimension"]!.Value.AsInteger()));
        Assert.Equal("tray-1", col["media-source"]!.Value.AsString());
        Assert.Equal("labels", col["media-type"]!.Value.AsString());
        Assert.Equal("iso_a4_210x297mm", col["media-size-name"]!.Value.AsString());
    }

    [Fact]
    public void Raster_ticket_requests_resolution_but_not_layout()
    {
        var (job, approx) = Ticket(new PrintSettings { Alignment = ContentAlignment.BottomRight, Copies = 2 }, documentMode: false, copies: false);
        Assert.Equal(new IppResolution(300, 300), job["printer-resolution"]!.Value.AsResolution());
        Assert.Null(job["orientation-requested"]);
        Assert.Null(job["copies"]);
        Assert.Empty(approx);
    }

    [Theory]
    [InlineData(Duplex.Vertical, PwgSheetBack.Normal, 1, 1)]
    [InlineData(Duplex.Vertical, PwgSheetBack.Flipped, 1, -1)]
    [InlineData(Duplex.Horizontal, PwgSheetBack.Flipped, -1, 1)]
    [InlineData(Duplex.Vertical, PwgSheetBack.Rotated, -1, -1)]
    [InlineData(Duplex.Horizontal, PwgSheetBack.Rotated, 1, 1)]
    [InlineData(Duplex.Vertical, PwgSheetBack.ManualTumble, 1, 1)]
    [InlineData(Duplex.Horizontal, PwgSheetBack.ManualTumble, -1, -1)]
    public void Back_side_transform_follows_sheet_back(Duplex duplex, PwgSheetBack back, int crossFeed, int feed)
        => Assert.Equal((crossFeed, feed), IppPrinterTarget.BackTransform(duplex, back));

    [Fact]
    public void Resolution_choice_respects_the_cap()
    {
        var list = new[] { new IppResolution(150, 150), new IppResolution(300, 300), new IppResolution(600, 600) };
        Assert.Equal(300, IppPrinterTarget.ChooseResolution(list, 300).CrossFeed);
        Assert.Equal(150, IppPrinterTarget.ChooseResolution(list, 100).CrossFeed);
        Assert.Equal(600, IppPrinterTarget.ChooseResolution(list, null).CrossFeed);
        Assert.Equal(300, IppPrinterTarget.ChooseResolution(Array.Empty<IppResolution>(), 300).CrossFeed);
    }
}

public class IppPrintingTests
{
    private static readonly PageDimensions A4 = PageDimensions.FromPaper(PaperSize.A4);

    private static IReadOnlyList<(PwgPageHeader Header, byte[] Pixels)> Pages(byte[] document)
        => new PwgRasterReader(new MemoryStream(document)).ReadAll();

    /// <summary>Gray value at a fraction of the media (0..1, 0..1).</summary>
    private static byte GrayAt((PwgPageHeader Header, byte[] Pixels) page, double fx, double fy)
    {
        int x = (int)(fx * (page.Header.Width - 1)), y = (int)(fy * (page.Header.Height - 1));
        return page.Header.ColorSpace switch
        {
            PwgColorSpace.SGray8 => page.Pixels[y * page.Header.BytesPerLine + x],
            PwgColorSpace.SRgb8 => page.Pixels[y * page.Header.BytesPerLine + x * 3 + 1],
            _ => (page.Pixels[y * page.Header.BytesPerLine + (x >> 3)] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255
        };
    }

    /// <summary>True when the fake page's red top-left marker (gray ~87) appears inside the given media region.</summary>
    private static bool MarkerIn((PwgPageHeader Header, byte[] Pixels) page, double x0, double y0, double x1, double y1)
    {
        for (double fy = y0; fy <= y1; fy += 0.01)
            for (double fx = x0; fx <= x1; fx += 0.01)
            {
                byte g = GrayAt(page, fx, fy);
                if (g > 70 && g < 105) return true;
            }
        return false;
    }

    [Fact]
    public void Raster_job_reaches_the_printer_as_pwg_with_the_mapped_ticket()
    {
        using var printer = new FakeIppPrinter();
        using var target = new IppPrinterTarget(printer.Uri).WithMaxRasterDpi(150);
        var sink = new MemoryAuditSink();
        PrintResult result = new PrintJob(FakeDocument.Uniform(3, A4))
            .WithRasterizer(new FakeRasterizer()).WithDuplex(Duplex.Vertical).WithCopies(2, collate: true)
            .WithColor(ColorMode.Grayscale).WithTitle("Ward round").WithAuditSink(sink)
            .PrintTo(target);

        Assert.True(result.Status == PrintJobStatus.Printed, result.Error);
        Assert.Equal(101, target.LastJobId);
        Assert.Single(sink.Events);
        Assert.Equal(printer.Uri, sink.Events[0].PrinterName);

        var (request, document) = Assert.Single(printer.PrintJobs);
        IppAttributeGroup op = request.Group(IppTag.OperationAttributes)!;
        Assert.Equal("image/pwg-raster", op["document-format"]!.Value.AsString());
        Assert.Equal("Ward round", op["job-name"]!.Value.AsString());
        IppAttributeGroup job = request.Group(IppTag.JobAttributes)!;
        Assert.Equal(2, job["copies"]!.Value.AsInteger());
        Assert.Equal("separate-documents-collated-copies", job["multiple-document-handling"]!.Value.AsString());
        Assert.Equal("two-sided-long-edge", job["sides"]!.Value.AsString());
        Assert.Equal("monochrome", job["print-color-mode"]!.Value.AsString());
        Assert.Equal(new IppResolution(150, 150), job["printer-resolution"]!.Value.AsResolution());

        var pages = Pages(document);
        Assert.Equal(3, pages.Count); // the printer makes the second copy
        foreach (var page in pages)
        {
            Assert.Equal(PwgColorSpace.SGray8, page.Header.ColorSpace);
            Assert.Equal((1240, 1754), (page.Header.Width, page.Header.Height));
            Assert.Equal((595, 842), (page.Header.PageWidthPoints, page.Header.PageHeightPoints));
            Assert.Equal("iso_a4_210x297mm", page.Header.PageSizeName);
            Assert.True(page.Header.Duplex && !page.Header.Tumble);
            Assert.Equal(255, GrayAt(page, 0, 0));                        // paper outside the margins
            Assert.True(MarkerIn(page, 0.03, 0.03, 0.2, 0.15), "marker top-left");
        }
    }

    [Fact]
    public void Landscape_sheets_are_turned_to_portrait_feed()
    {
        using var printer = new FakeIppPrinter();
        using var target = new IppPrinterTarget(printer.Uri).WithMaxRasterDpi(72);
        PrintResult result = new PrintJob(FakeDocument.Create(A4.Swap()))
            .WithRasterizer(new FakeRasterizer()).WithOrientation(PageOrientation.Landscape).WithColor(ColorMode.Grayscale)
            .PrintTo(target);
        Assert.True(result.Status == PrintJobStatus.Printed, result.Error);
        var page = Assert.Single(Pages(printer.PrintJobs[0].Document));
        Assert.True(page.Header.Height > page.Header.Width, "media is fed portrait");
        // Landscape content rotated counter-clockwise: the page's top-left corner lands bottom-left.
        Assert.True(MarkerIn(page, 0.03, 0.8, 0.2, 0.97), "marker bottom-left");
        Assert.False(MarkerIn(page, 0.03, 0.03, 0.2, 0.2), "marker not top-left");
    }

    [Fact]
    public void Back_sides_are_transformed_as_the_printer_asks()
    {
        using var printer = new FakeIppPrinter(g => g.Set("pwg-raster-document-sheet-back", IppValue.Keyword("flipped")));
        using var target = new IppPrinterTarget(printer.Uri).WithMaxRasterDpi(72);
        new PrintJob(FakeDocument.Uniform(2, A4)).WithRasterizer(new FakeRasterizer())
            .WithDuplex(Duplex.Vertical).WithColor(ColorMode.Grayscale).PrintTo(target);
        var pages = Pages(printer.PrintJobs[0].Document);
        Assert.Equal((1, 1), (pages[0].Header.CrossFeedTransform, pages[0].Header.FeedTransform));
        Assert.Equal((1, -1), (pages[1].Header.CrossFeedTransform, pages[1].Header.FeedTransform));
        Assert.True(MarkerIn(pages[0], 0.03, 0.03, 0.2, 0.15), "front marker top-left");
        Assert.True(MarkerIn(pages[1], 0.03, 0.85, 0.2, 0.97), "back marker mirrored to bottom-left");
    }

    [Theory]
    [InlineData(ColorMode.Color, PwgColorSpace.SRgb8, "color")]
    [InlineData(ColorMode.Monochrome, PwgColorSpace.Black1, "bi-level")]
    public void Colour_modes_choose_the_pwg_type(ColorMode mode, PwgColorSpace expected, string colorMode)
    {
        using var printer = new FakeIppPrinter();
        using var target = new IppPrinterTarget(printer.Uri).WithMaxRasterDpi(72);
        PrintResult result = new PrintJob(FakeDocument.Create(A4)).WithRasterizer(new FakeRasterizer()).WithColor(mode).PrintTo(target);
        Assert.True(result.Status == PrintJobStatus.Printed, result.Error);
        Assert.Equal(expected, Assert.Single(Pages(printer.PrintJobs[0].Document)).Header.ColorSpace);
        Assert.Equal(colorMode, printer.PrintJobs[0].Request.Group(IppTag.JobAttributes)!["print-color-mode"]!.Value.AsString());
    }

    [Fact]
    public void Printer_without_pwg_raster_fails_with_guidance()
    {
        using var printer = new FakeIppPrinter(g => g.Set("document-format-supported", IppValue.MimeMediaType("application/pdf")));
        using var target = new IppPrinterTarget(printer.Uri);
        PrintResult result = new PrintJob(FakeDocument.Create(A4)).WithRasterizer(new FakeRasterizer()).PrintTo(target);
        Assert.Equal(PrintJobStatus.Failed, result.Status);
        Assert.Contains("IppDocumentTarget", result.Error);
        Assert.Empty(printer.PrintJobs);
    }

    [Fact]
    public void Pdf_passes_through_with_job_attributes()
    {
        using var printer = new FakeIppPrinter();
        using var target = new IppDocumentTarget(printer.Uri);
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% Chuvadi passthrough test\n%%EOF\n");
        var sink = new MemoryAuditSink();
        PrintResult result = new PrintJob(pdf)
            .WithPages(PageSelection.Range("2-3")).WithCopies(2).WithOrientation(PageOrientation.Landscape)
            .WithAlignment(ContentAlignment.TopLeft).WithTitle("Lab report").WithUser("arun").WithAuditSink(sink)
            .SubmitTo(target);

        Assert.True(result.Status == PrintJobStatus.Printed, result.Error);
        Assert.Equal(0, result.PagesPrinted); // no rasteriser: page count unknown
        var (request, document) = Assert.Single(printer.PrintJobs);
        Assert.Equal(pdf, document);
        Assert.Equal("application/pdf", request.Group(IppTag.OperationAttributes)!["document-format"]!.Value.AsString());
        IppAttributeGroup job = request.Group(IppTag.JobAttributes)!;
        Assert.Equal(4, job["orientation-requested"]!.Value.AsInteger());
        Assert.Equal(new IppRange(2, 3), job["page-ranges"]!.Value.AsRange());
        Assert.Equal(2, job["copies"]!.Value.AsInteger());
        Assert.Contains(target.LastApproximations, a => a.Contains("alignment"));
        PrintAuditEvent audit = Assert.Single(sink.Events);
        Assert.Equal(PrintJobStatus.Printed, audit.Status);
        Assert.Equal("Lab report", audit.DocumentTitle);
    }

    [Fact]
    public void Pdf_submission_counts_pages_when_a_rasterizer_is_configured()
    {
        using var printer = new FakeIppPrinter();
        using var target = new IppDocumentTarget(printer.Uri);
        PrintResult result = new PrintJob(FakeDocument.Uniform(6, A4)).WithRasterizer(new FakeRasterizer())
            .WithPages(PageSelection.Even).SubmitTo(target);
        Assert.True(result.Status == PrintJobStatus.Printed, result.Error);
        Assert.Equal(3, result.PagesPrinted);
        IppAttributeGroup job = printer.PrintJobs[0].Request.Group(IppTag.JobAttributes)!;
        Assert.Equal(3, job["page-ranges"]!.Values.Count);
    }

    [Fact]
    public void Printer_errors_fail_the_job_and_are_audited_once()
    {
        using var printer = new FakeIppPrinter { FailNextJobWith = IppStatusCode.ClientErrorDocumentFormatError };
        using var target = new IppDocumentTarget(printer.Uri);
        var sink = new MemoryAuditSink();
        PrintResult result = new PrintJob(new byte[] { 1, 2, 3 }).WithAuditSink(sink).SubmitTo(target);
        Assert.Equal(PrintJobStatus.Failed, result.Status);
        Assert.Contains("ClientErrorDocumentFormatError", result.Error);
        Assert.Contains("simulated failure", result.Error);
        Assert.Equal(PrintJobStatus.Failed, Assert.Single(sink.Events).Status);
    }

    [Fact]
    public void Strict_options_refuse_approximations()
    {
        using var printer = new FakeIppPrinter();
        using var target = new IppDocumentTarget(printer.Uri).WithJobOptions(o => o.Strict = true);
        PrintResult result = new PrintJob(new byte[] { 1 }).WithAlignment(ContentAlignment.BottomRight).SubmitTo(target);
        Assert.Equal(PrintJobStatus.Failed, result.Status);
        Assert.Contains("cannot honour", result.Error);
        Assert.Empty(printer.PrintJobs);
    }

    [Fact]
    public void Waiting_for_completion_reports_aborted_jobs()
    {
        using var printer = new FakeIppPrinter { JobState = IppJobState.Aborted };
        using var target = new IppDocumentTarget(printer.Uri) { WaitForCompletion = TimeSpan.FromSeconds(10), PollInterval = TimeSpan.FromMilliseconds(50) };
        PrintResult result = new PrintJob(new byte[] { 1 }).SubmitTo(target);
        Assert.Equal(PrintJobStatus.Failed, result.Status);
        Assert.Contains("aborted", result.Error);

        printer.JobState = IppJobState.Completed;
        Assert.Equal(PrintJobStatus.Printed, new PrintJob(new byte[] { 1 }).SubmitTo(target).Status);
    }

    [Fact]
    public void Unreachable_printer_is_reported()
    {
        using var target = new IppDocumentTarget("ipp://127.0.0.1:1/ipp/print", new IppClientOptions { Timeout = TimeSpan.FromSeconds(5) });
        PrintResult result = new PrintJob(new byte[] { 1 }).SubmitTo(target);
        Assert.Equal(PrintJobStatus.Failed, result.Status);
        Assert.Contains("Could not reach", result.Error);
    }

    [Fact]
    public void Client_operations_round_trip_against_the_fake_printer()
    {
        using var printer = new FakeIppPrinter();
        using var client = new IppClient(printer.Uri, new IppClientOptions { UserName = "nurse" });
        IppPrinterCapabilities caps = new(client.GetPrinterAttributes().Group(IppTag.PrinterAttributes)!);
        Assert.Equal("Chuvadi Test Printer", caps.MakeAndModel);
        int id = client.PrintJob(new MemoryStream(new byte[] { 1 }), "application/pdf");
        Assert.Equal(IppJobState.Completed, client.GetJobState(id));
        Assert.True(client.ValidateJob("application/pdf").IsSuccess);
        Assert.True(client.CancelJob(id).IsSuccess);
        Assert.Equal("nurse", printer.Requests[0].Request.Group(IppTag.OperationAttributes)!["requesting-user-name"]!.Value.AsString());
        var ex = Assert.Throws<IppException>(() => client.GetJobs());
        Assert.Equal((short)IppStatusCode.ServerErrorOperationNotSupported, ex.StatusCode);
    }
}
