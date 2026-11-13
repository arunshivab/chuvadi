using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.ManualTests;
using Chuvadi.Print.Remote;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.Tests;

/// <summary>PrintSettings.PrinterDefaults (L3, D-235 and D-257): settings left to the printer's own defaults.</summary>
public class PrinterDefaultsTests
{
    private static readonly PageDimensions A4Page = PageDimensions.FromPaper(PaperSize.A4);

    private static PrintJob Job(int pages, PrintSettings settings, MemoryAuditSink? sink = null)
    {
        var job = new PrintJob(FakeDocument.Uniform(pages, A4Page), settings).WithRasterizer(new FakeRasterizer());
        return sink is null ? job : job.WithAuditSink(sink);
    }

    // ------------------------------------------------------------------ core: resolution and layout

    [Fact]
    public void None_is_the_default_and_resolution_changes_nothing()
    {
        var settings = new PrintSettings { Paper = PaperSize.Letter, Copies = 3, Duplex = Duplex.Vertical, Color = ColorMode.Grayscale };
        Assert.Equal(PrinterDefault.None, settings.PrinterDefaults);
        var caps = new PrintTargetCapabilities { DefaultPaper = PaperSize.A5, DefaultDuplex = Duplex.Simplex, DefaultColor = ColorMode.Color, DefaultCopies = 2 };
        var notes = new List<string>();
        PrintSettings resolved = settings.ResolvePrinterDefaults(caps, notes);
        Assert.NotSame(settings, resolved);
        Assert.Equal(PaperSize.Letter, resolved.Paper);
        Assert.Equal(3, resolved.Copies);
        Assert.Equal(Duplex.Vertical, resolved.Duplex);
        Assert.Equal(ColorMode.Grayscale, resolved.Color);
        Assert.Empty(notes);
    }

    [Fact]
    public void Clone_keeps_the_printer_defaults()
        => Assert.Equal(PrinterDefault.Paper | PrinterDefault.Copies,
                        new PrintSettings { PrinterDefaults = PrinterDefault.Paper | PrinterDefault.Copies }.Clone().PrinterDefaults);

    [Fact]
    public void Reported_defaults_replace_the_flagged_settings_and_the_flags_stay_set()
    {
        var settings = new PrintSettings
        {
            Paper = PaperSize.A4, Duplex = Duplex.Simplex, Color = ColorMode.Color, Copies = 4,
            PrinterDefaults = PrinterDefault.Paper | PrinterDefault.Duplex | PrinterDefault.Color | PrinterDefault.Copies
        };
        var caps = new PrintTargetCapabilities
        {
            SupportsCopies = true, DefaultPaper = PaperSize.Letter, DefaultDuplex = Duplex.Horizontal,
            DefaultColor = ColorMode.Grayscale, DefaultCopies = 2
        };
        var notes = new List<string>();
        PrintSettings resolved = settings.ResolvePrinterDefaults(caps, notes);
        Assert.Equal(PaperSize.Letter, resolved.Paper);
        Assert.Equal(Duplex.Horizontal, resolved.Duplex);
        Assert.Equal(ColorMode.Grayscale, resolved.Color);
        Assert.Equal(1, resolved.Copies);
        Assert.Equal(settings.PrinterDefaults, resolved.PrinterDefaults);
        Assert.Equal(new[]
        {
            "paper: printer default (Letter)",
            "duplex: printer default (Horizontal)",
            "color: printer default (Grayscale)",
            "copies: printer default (2)"
        }, notes);
        // The original settings are untouched.
        Assert.Equal(PaperSize.A4, settings.Paper);
        Assert.Equal(4, settings.Copies);
    }

    [Fact]
    public void Unreported_defaults_keep_the_settings_value_and_say_so()
    {
        var settings = new PrintSettings
        {
            Paper = PaperSize.A4, Duplex = Duplex.Vertical, Color = ColorMode.Monochrome, Copies = 3,
            PrinterDefaults = PrinterDefault.Paper | PrinterDefault.Duplex | PrinterDefault.Color | PrinterDefault.Copies
        };
        var notes = new List<string>();
        PrintSettings resolved = settings.ResolvePrinterDefaults(new PrintTargetCapabilities { SupportsCopies = true }, notes);
        Assert.Equal(PaperSize.A4, resolved.Paper);
        Assert.Equal(Duplex.Vertical, resolved.Duplex);
        Assert.Equal(ColorMode.Monochrome, resolved.Color);
        Assert.Equal(1, resolved.Copies);
        Assert.Equal(new[]
        {
            "paper: printer default not reported; laid out for A4",
            "duplex: printer default not reported; laid out for Vertical",
            "color: printer default not reported; laid out for Monochrome",
            "copies: printer default (not reported)"
        }, notes);
    }

    [Fact]
    public void Copies_flag_means_one_copy_where_the_printer_makes_none()
    {
        var notes = new List<string>();
        PrintSettings resolved = new PrintSettings { Copies = 5, PrinterDefaults = PrinterDefault.Copies }
            .ResolvePrinterDefaults(new PrintTargetCapabilities { SupportsCopies = false, DefaultCopies = 3 }, notes);
        Assert.Equal(1, resolved.Copies);
        Assert.Equal("copies: 1 (the printer does not make copies)", Assert.Single(notes));
    }

    [Fact]
    public void Print_lays_out_on_the_printers_default_paper_and_audits_it()
    {
        var target = new RecordingTarget
        {
            Capabilities = new PrintTargetCapabilities { DpiX = 72, DpiY = 72, DefaultPaper = PaperSize.Letter }
        };
        var sink = new MemoryAuditSink();
        PrintResult result = Job(1, new PrintSettings { PrinterDefaults = PrinterDefault.Paper }, sink).PrintTo(target);

        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(PageDimensions.FromPaper(PaperSize.Letter), target.Pages[0].Placement!.SheetSize);
        Assert.Equal(PaperSize.Letter, target.Context!.Settings.Paper);
        Assert.Equal(PrinterDefault.Paper, target.Context.Settings.PrinterDefaults);
        Assert.Equal("paper: printer default (Letter)", Assert.Single(sink.Events).PrinterDefaults);
    }

    [Fact]
    public void Print_without_a_reported_default_lays_out_with_the_settings_and_still_prints()
    {
        var target = new RecordingTarget();
        var sink = new MemoryAuditSink();
        PrintResult result = Job(1, new PrintSettings { Paper = PaperSize.A5, PrinterDefaults = PrinterDefault.Paper }, sink).PrintTo(target);

        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(PageDimensions.FromPaper(PaperSize.A5), target.Pages[0].Placement!.SheetSize);
        Assert.Equal("paper: printer default not reported; laid out for A5", Assert.Single(sink.Events).PrinterDefaults);
    }

    [Fact]
    public void Print_uses_the_default_colour_and_two_sided_printing_and_makes_one_set()
    {
        var target = new RecordingTarget
        {
            Capabilities = new PrintTargetCapabilities
            {
                DpiX = 72, DpiY = 72, SupportsDuplex = true, SupportsCopies = true, SupportsCollate = true,
                DefaultDuplex = Duplex.Vertical, DefaultColor = ColorMode.Grayscale, DefaultCopies = 2
            }
        };
        var sink = new MemoryAuditSink();
        var settings = new PrintSettings
        {
            Copies = 3, Duplex = Duplex.Simplex, Color = ColorMode.Color,
            PrinterDefaults = PrinterDefault.Duplex | PrinterDefault.Color | PrinterDefault.Copies
        };
        PrintResult result = Job(2, settings, sink).PrintTo(target);

        Assert.Equal(PrintJobStatus.Printed, result.Status);
        Assert.Equal(2, target.Pages.Count);
        Assert.False(target.Pages[0].Impression.IsBackSide);
        Assert.True(target.Pages[1].Impression.IsBackSide);
        Assert.All(target.Pages, p => Assert.Equal(RasterPixelFormat.Gray8, p.Format));
        Assert.False(target.Context!.TargetHandlesCopies);
        PrintAuditEvent audit = Assert.Single(sink.Events);
        Assert.Equal(1, audit.Copies);
        Assert.Equal("duplex: printer default (Vertical); color: printer default (Grayscale); copies: printer default (2)", audit.PrinterDefaults);
    }

    [Fact]
    public void Plan_resolves_the_defaults_from_the_given_capabilities()
    {
        PrintPlan plan = Job(3, new PrintSettings { Copies = 2, PrinterDefaults = PrinterDefault.Copies })
            .Plan(new PrintTargetCapabilities { SupportsCopies = true });
        Assert.Equal(1, plan.Copies);
        Assert.Equal(3, plan.Impressions.Count);
    }

    [Fact]
    public void Without_printer_defaults_the_audit_has_no_printer_defaults_line()
    {
        var sink = new MemoryAuditSink();
        Job(1, new PrintSettings(), sink).PrintTo(new RecordingTarget());
        Assert.Null(Assert.Single(sink.Events).PrinterDefaults);
    }

    // ------------------------------------------------------------------ audit file, spool envelope, remote record

    [Fact]
    public void File_sink_adds_the_printer_defaults_column_only_when_set()
    {
        string path = Path.Combine(Path.GetTempPath(), "chuvadi-defaults-audit-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var sink = new FileAuditSink(path);
            sink.Record(new PrintAuditEvent { DocumentTitle = "Plain", Route = "Raster" });
            sink.Record(new PrintAuditEvent { DocumentTitle = "Defaults", Route = "Raster", PrinterDefaults = "paper: printer default (A4)" });
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(13, lines[0].Split('\t').Length);
            string[] second = lines[1].Split('\t');
            Assert.Equal(14, second.Length);
            Assert.Equal("Raster", second[12]);
            Assert.Equal("paper: printer default (A4)", second[13]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Spool_envelope_without_printer_defaults_is_written_as_version_2_byte_for_byte()
    {
        var envelope = new SpoolEnvelope(new byte[] { 1, 2, 3 }, new PrintSettings { Copies = 2, AutoRotate = true })
        {
            JobId = Guid.NewGuid(), DocumentTitle = "t", UserName = "u", TenantId = "x"
        };
        using var v2 = new MemoryStream();
        envelope.Serialize(v2, 2);
        Assert.Equal(v2.ToArray(), envelope.ToArray());
        Assert.Equal(2, envelope.ToArray()[4]);
    }

    [Fact]
    public void Spool_envelope_with_printer_defaults_is_version_3_and_round_trips()
    {
        var settings = new PrintSettings { Copies = 2, PrinterDefaults = PrinterDefault.Paper | PrinterDefault.Color };
        var envelope = new SpoolEnvelope(new byte[] { 9, 8, 7 }, settings) { JobId = Guid.NewGuid(), DocumentTitle = "t" };
        byte[] bytes = envelope.ToArray();
        Assert.Equal(3, bytes[4]);
        SpoolEnvelope back = SpoolEnvelope.FromArray(bytes);
        Assert.Equal(PrinterDefault.Paper | PrinterDefault.Color, back.Settings.PrinterDefaults);
        Assert.Equal(2, back.Settings.Copies);
        Assert.Equal("t", back.DocumentTitle);
        Assert.Equal(new byte[] { 9, 8, 7 }, back.PdfBytes);

        // An older version cannot carry the flags, so writing one is refused rather than dropping them.
        using var ms = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => envelope.Serialize(ms, 2));
    }

    [Fact]
    public void Spool_envelope_with_unknown_printer_default_flags_is_rejected()
    {
        var envelope = new SpoolEnvelope(new byte[] { 1 }, new PrintSettings { PrinterDefaults = (PrinterDefault)16 });
        Assert.Throws<InvalidDataException>(() => SpoolEnvelope.FromArray(envelope.ToArray()));
    }

    [Fact]
    public void Remote_audit_record_carries_the_printer_defaults_and_omits_them_when_null()
    {
        var withDefaults = new PrintAuditEvent { JobId = Guid.NewGuid(), PrinterDefaults = "copies: printer default (2)" };
        AuditRecord record = AuditRecord.From(withDefaults);
        Assert.Equal("copies: printer default (2)", record.PrinterDefaults);
        Assert.Equal("copies: printer default (2)", record.ToEvent().PrinterDefaults);

        string json = JsonSerializer.Serialize(new StatusReport { Audit = record }, RemoteJson.Default.StatusReport);
        Assert.Contains("\"printerDefaults\":\"copies: printer default (2)\"", json);
        Assert.Equal("copies: printer default (2)", JsonSerializer.Deserialize(json, RemoteJson.Default.StatusReport)!.Audit!.PrinterDefaults);

        string plain = JsonSerializer.Serialize(new StatusReport { Audit = AuditRecord.From(new PrintAuditEvent { JobId = Guid.NewGuid() }) },
                                                RemoteJson.Default.StatusReport);
        Assert.DoesNotContain("printerDefaults", plain);
    }

    // ------------------------------------------------------------------ IPP

    private static IppPrinterCapabilities Caps(Action<IppAttributeGroup>? configure = null)
    {
        IppAttributeGroup g = FakeIppPrinter.DefaultAttributes("ipp://x/y");
        configure?.Invoke(g);
        return new IppPrinterCapabilities(g);
    }

    private static IppAttributeGroup Ticket(PrintSettings s, IppPrinterCapabilities? caps = null, bool documentMode = true,
                                            IppJobOptions? options = null, List<string>? approx = null)
        => IppJobTicket.Build(s, caps ?? Caps(), options, documentMode, printerMakesCopies: true, null,
                              new IppResolution(300, 300), approx ?? new List<string>());

    [Fact]
    public void Ipp_capabilities_read_the_printer_defaults()
    {
        IppPrinterCapabilities caps = Caps(g =>
        {
            g.Set("media-default", IppValue.Keyword("na_letter_8.5x11in"));
            g.Set("sides-default", IppValue.Keyword("two-sided-long-edge"));
            g.Set("print-color-mode-default", IppValue.Keyword("monochrome"));
            g.Set("copies-default", IppValue.Integer(2));
        });
        Assert.Equal("na_letter_8.5x11in", caps.DefaultMedia);
        Assert.Equal(PaperSize.Letter.WidthPoints, caps.DefaultPaper!.Value.WidthPoints, 2);
        Assert.Equal(PaperSize.Letter.HeightPoints, caps.DefaultPaper!.Value.HeightPoints, 2);
        Assert.Equal("two-sided-long-edge", caps.DefaultSides);
        Assert.Equal(Duplex.Vertical, caps.DefaultDuplex);
        Assert.Equal("monochrome", caps.DefaultColorMode);
        Assert.Equal(ColorMode.Grayscale, caps.DefaultColor);
        Assert.Equal(2, caps.DefaultCopies);
    }

    [Fact]
    public void Ipp_default_paper_falls_back_to_media_col_default_and_auto_colour_is_not_a_default()
    {
        var size = new IppCollection().Add("x-dimension", IppValue.Integer(21000)).Add("y-dimension", IppValue.Integer(29700));
        IppPrinterCapabilities caps = Caps(g =>
        {
            g.Remove("media-default");
            g.Set("media-col-default", IppValue.Collection(new IppCollection().Add("media-size", IppValue.Collection(size))));
            g.Set("print-color-mode-default", IppValue.Keyword("auto"));
            g.Set("sides-default", IppValue.Keyword("one-sided"));
        });
        Assert.Null(caps.DefaultMedia);
        Assert.Equal(PaperSize.A4.WidthPoints, caps.DefaultPaper!.Value.WidthPoints, 2);
        Assert.Equal(PaperSize.A4.HeightPoints, caps.DefaultPaper!.Value.HeightPoints, 2);
        Assert.Null(caps.DefaultColor);
        Assert.Equal(Duplex.Simplex, caps.DefaultDuplex);

        IppPrinterCapabilities bare = Caps(g =>
        {
            g.Remove("media-default");
            g.Remove("media-col-default");
            g.Remove("sides-default");
            g.Remove("print-color-mode-default");
            g.Remove("copies-default");
        });
        Assert.Null(bare.DefaultPaper);
        Assert.Null(bare.DefaultDuplex);
        Assert.Null(bare.DefaultColor);
        Assert.Null(bare.DefaultCopies);
        Assert.Null(Caps(g => g.Set("copies-default", IppValue.Integer(0))).DefaultCopies);
    }

    [Fact]
    public void Ipp_ticket_leaves_out_each_flagged_setting()
    {
        var settings = new PrintSettings { Copies = 3, Duplex = Duplex.Vertical, Color = ColorMode.Monochrome, Paper = PaperSize.Letter };
        IppAttributeGroup full = Ticket(settings);
        Assert.NotNull(full["copies"]);
        Assert.NotNull(full["multiple-document-handling"]);
        Assert.NotNull(full["sides"]);
        Assert.NotNull(full["media"]);
        Assert.NotNull(full["print-color-mode"]);

        settings.PrinterDefaults = PrinterDefault.Copies;
        IppAttributeGroup copies = Ticket(settings);
        Assert.Null(copies["copies"]);
        Assert.Null(copies["multiple-document-handling"]);
        Assert.NotNull(copies["sides"]);

        settings.PrinterDefaults = PrinterDefault.Duplex;
        Assert.Null(Ticket(settings)["sides"]);

        settings.PrinterDefaults = PrinterDefault.Color;
        Assert.Null(Ticket(settings)["print-color-mode"]);

        settings.PrinterDefaults = PrinterDefault.Paper;
        IppAttributeGroup paper = Ticket(settings);
        Assert.Null(paper["media"]);
        Assert.Null(paper["media-col"]);
        Assert.NotNull(paper["sides"]);

        settings.PrinterDefaults = PrinterDefault.Paper | PrinterDefault.Duplex | PrinterDefault.Color | PrinterDefault.Copies;
        IppAttributeGroup all = Ticket(settings);
        foreach (string name in new[] { "copies", "multiple-document-handling", "sides", "media", "media-col", "print-color-mode" })
            Assert.Null(all[name]);
        // Everything else is still sent.
        Assert.NotNull(all["orientation-requested"]);
        Assert.NotNull(all["print-scaling"]);
    }

    [Fact]
    public void Ipp_ticket_with_the_paper_flag_still_sends_the_tray_and_media_type_without_a_size()
    {
        var options = new IppJobOptions { MediaSource = "tray-1", MediaType = "labels" };
        IppAttributeGroup job = Ticket(new PrintSettings { PrinterDefaults = PrinterDefault.Paper }, options: options);
        IppCollection col = job["media-col"]!.Value.AsCollection();
        Assert.Null(col["media-size"]);
        Assert.Null(col["media-size-name"]);
        Assert.Equal("tray-1", col["media-source"]!.Value.AsString());
        Assert.Equal("labels", col["media-type"]!.Value.AsString());
        Assert.Null(job["media"]);
    }

    [Fact]
    public void Ipp_ticket_with_flags_adds_no_approximations_so_strict_jobs_still_print()
    {
        var approx = new List<string>();
        var all = new PrintSettings { PrinterDefaults = PrinterDefault.Paper | PrinterDefault.Duplex | PrinterDefault.Color | PrinterDefault.Copies };
        Ticket(all, Caps(g => g.Remove("media-default")), options: new IppJobOptions { Strict = true }, approx: approx);
        Assert.Empty(approx);
    }

    [Fact]
    public void Ipp_raster_target_reports_the_printer_defaults()
    {
        using var printer = new FakeIppPrinter(g =>
        {
            g.Set("media-default", IppValue.Keyword("na_letter_8.5x11in"));
            g.Set("sides-default", IppValue.Keyword("one-sided"));
            g.Set("print-color-mode-default", IppValue.Keyword("color"));
            g.Set("copies-default", IppValue.Integer(1));
        });
        using var target = new IppPrinterTarget(printer.Uri);
        PrintTargetCapabilities caps = target.GetCapabilities(new PrintSettings());
        Assert.Equal(PaperSize.Letter.WidthPoints, caps.DefaultPaper!.Value.WidthPoints, 2);
        Assert.Equal(Duplex.Simplex, caps.DefaultDuplex);
        Assert.Equal(ColorMode.Color, caps.DefaultColor);
        Assert.Equal(1, caps.DefaultCopies);
    }

    [Fact]
    public void Ipp_raster_job_with_the_paper_flag_prints_on_the_default_media_and_sends_no_media()
    {
        using var printer = new FakeIppPrinter(g => g.Set("media-default", IppValue.Keyword("na_letter_8.5x11in")));
        using var target = new IppPrinterTarget(printer.Uri).WithMaxRasterDpi(150);
        var sink = new MemoryAuditSink();
        PrintResult result = Job(1, new PrintSettings { PrinterDefaults = PrinterDefault.Paper }, sink).PrintTo(target);

        Assert.True(result.Status == PrintJobStatus.Printed, result.Error);
        var (request, document) = Assert.Single(printer.PrintJobs);
        IppAttributeGroup job = request.Group(IppTag.JobAttributes)!;
        Assert.Null(job["media"]);
        Assert.Null(job["media-col"]);
        Assert.NotNull(job["sides"]);
        // The PWG Raster page header (after the 4-byte sync word) names the printer's default media and its size.
        PwgPageHeader header = PwgPageHeader.Decode(document.AsSpan(4));
        Assert.Equal("na_letter_8.5x11in", header.PageSizeName);
        Assert.Equal(612, header.PageWidthPoints);
        Assert.Equal(792, header.PageHeightPoints);
        Assert.Equal("paper: printer default (na_letter_8.5x11in)", Assert.Single(sink.Events).PrinterDefaults);
    }

    [Fact]
    public void Ipp_document_job_with_flags_sends_none_of_them_and_audits_printer_defaults()
    {
        using var printer = new FakeIppPrinter();
        using var target = new IppDocumentTarget(printer.Uri);
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% Chuvadi printer defaults test\n%%EOF\n");
        var sink = new MemoryAuditSink();
        PrintResult result = new PrintJob(pdf)
            .WithCopies(3).WithDuplex(Duplex.Vertical)
            .WithPrinterDefaults(PrinterDefault.Paper | PrinterDefault.Duplex | PrinterDefault.Color | PrinterDefault.Copies)
            .WithAuditSink(sink).SubmitTo(target);

        Assert.True(result.Status == PrintJobStatus.Printed, result.Error);
        var (request, _) = Assert.Single(printer.PrintJobs);
        IppAttributeGroup job = request.Group(IppTag.JobAttributes)!;
        foreach (string name in new[] { "copies", "multiple-document-handling", "sides", "media", "media-col", "print-color-mode" })
            Assert.Null(job[name]);
        PrintAuditEvent audit = Assert.Single(sink.Events);
        Assert.Equal(1, audit.Copies);
        Assert.Equal("paper: printer default; duplex: printer default; color: printer default; copies: printer default", audit.PrinterDefaults);
    }

    // ------------------------------------------------------------------ Windows (portable parts)

    private static readonly PrinterPaper WinLetter = new(1, "Letter", PaperSize.Letter);
    private static readonly PrinterPaper WinA4 = new(9, "A4", PaperSize.A4);
    private static readonly PrinterPaper[] WinPapers = { WinLetter, WinA4 };

    private static DevModeBuffer DriverDefault(short paperId, short duplex, short color, short copies, short collate)
    {
        DevModeBuffer dm = DevModeBuffer.CreateBlank();
        dm.SetInt16(DevModeBuffer.OffsetPaperSize, DevModeBuffer.DM_PAPERSIZE, paperId);
        dm.SetInt16(DevModeBuffer.OffsetDuplex, DevModeBuffer.DM_DUPLEX, duplex);
        dm.SetInt16(DevModeBuffer.OffsetColor, DevModeBuffer.DM_COLOR, color);
        dm.SetInt16(DevModeBuffer.OffsetCopies, DevModeBuffer.DM_COPIES, copies);
        dm.SetInt16(DevModeBuffer.OffsetCollate, DevModeBuffer.DM_COLLATE, collate);
        return dm;
    }

    [Fact]
    public void Windows_reads_the_drivers_defaults_from_its_devmode()
    {
        DevModeBuffer dm = DriverDefault(1, DevModeBuffer.DMDUP_HORIZONTAL, DevModeBuffer.DMCOLOR_MONOCHROME, 2, DevModeBuffer.DMCOLLATE_TRUE);
        DevModeDefaults d = DevModeDefaults.Read(dm, WinPapers);
        Assert.Equal(PaperSize.Letter, d.Paper);
        Assert.Equal(Duplex.Horizontal, d.Duplex);
        Assert.Equal(ColorMode.Grayscale, d.Color);
        Assert.Equal(2, d.Copies);
    }

    [Fact]
    public void Windows_custom_paper_in_the_default_devmode_is_read_and_missing_fields_are_not_defaults()
    {
        DevModeBuffer custom = DevModeBuffer.CreateBlank();
        custom.SetInt16(DevModeBuffer.OffsetPaperSize, DevModeBuffer.DM_PAPERSIZE, 0);
        custom.SetInt16(DevModeBuffer.OffsetPaperWidth, DevModeBuffer.DM_PAPERWIDTH, 1000);   // 100 mm
        custom.SetInt16(DevModeBuffer.OffsetPaperLength, DevModeBuffer.DM_PAPERLENGTH, 1500); // 150 mm
        DevModeDefaults d = DevModeDefaults.Read(custom, WinPapers);
        Assert.Equal(100.0, d.Paper!.Value.WidthMillimetres, 3);
        Assert.Equal(150.0, d.Paper!.Value.HeightMillimetres, 3);
        Assert.Null(d.Duplex);
        Assert.Null(d.Color);
        Assert.Null(d.Copies);

        // A paper id the driver does not list, and a short (old) DEVMODE without the colour and duplex fields.
        DevModeBuffer unknown = DevModeBuffer.CreateBlank(publicSize: 90);
        unknown.SetInt16(DevModeBuffer.OffsetPaperSize, DevModeBuffer.DM_PAPERSIZE, 77);
        unknown.Fields |= DevModeBuffer.DM_COLOR | DevModeBuffer.DM_DUPLEX;
        DevModeDefaults u = DevModeDefaults.Read(unknown, WinPapers);
        Assert.Null(u.Paper);
        Assert.Null(u.Color);
        Assert.Null(u.Duplex);
    }

    [Fact]
    public void Windows_keeps_the_drivers_fields_for_each_flagged_setting()
    {
        var settings = new PrintSettings { Paper = PaperSize.A4, Duplex = Duplex.Vertical, Color = ColorMode.Color, Copies = 1 };

        // Without flags every field is written, as before.
        DevModeBuffer plain = DriverDefault(1, DevModeBuffer.DMDUP_HORIZONTAL, DevModeBuffer.DMCOLOR_MONOCHROME, 4, DevModeBuffer.DMCOLLATE_TRUE);
        DevModeMapper.Apply(DevModeMapper.Map(settings, WinPapers, PaperMatching.ExactOrCustom, false, null, null, null, null), plain);
        Assert.Equal(9, plain.ReadInt16(DevModeBuffer.OffsetPaperSize));
        Assert.Equal(DevModeBuffer.DMDUP_VERTICAL, plain.ReadInt16(DevModeBuffer.OffsetDuplex));
        Assert.Equal(DevModeBuffer.DMCOLOR_COLOR, plain.ReadInt16(DevModeBuffer.OffsetColor));
        Assert.Equal(1, plain.ReadInt16(DevModeBuffer.OffsetCopies));
        Assert.Equal(DevModeBuffer.DMCOLLATE_FALSE, plain.ReadInt16(DevModeBuffer.OffsetCollate));

        settings.PrinterDefaults = PrinterDefault.Paper | PrinterDefault.Duplex | PrinterDefault.Color | PrinterDefault.Copies;
        DevModeBuffer kept = DriverDefault(1, DevModeBuffer.DMDUP_HORIZONTAL, DevModeBuffer.DMCOLOR_MONOCHROME, 4, DevModeBuffer.DMCOLLATE_TRUE);
        kept.Fields |= DevModeBuffer.DM_FORMNAME;
        byte[] before = (byte[])kept.Bytes.Clone();
        DevModeMapper.Apply(DevModeMapper.Map(settings, WinPapers, PaperMatching.ExactOrCustom, false, null, null, null, null), kept);
        Assert.Equal(1, kept.ReadInt16(DevModeBuffer.OffsetPaperSize));
        Assert.Equal(DevModeBuffer.DMDUP_HORIZONTAL, kept.ReadInt16(DevModeBuffer.OffsetDuplex));
        Assert.Equal(DevModeBuffer.DMCOLOR_MONOCHROME, kept.ReadInt16(DevModeBuffer.OffsetColor));
        Assert.Equal(4, kept.ReadInt16(DevModeBuffer.OffsetCopies));
        Assert.Equal(DevModeBuffer.DMCOLLATE_TRUE, kept.ReadInt16(DevModeBuffer.OffsetCollate));
        Assert.NotEqual(0u, kept.Fields & DevModeBuffer.DM_FORMNAME);
        // Only orientation and scale were written.
        Assert.Equal(BinaryPrimitives.ReadInt16LittleEndian(before.AsSpan(DevModeBuffer.OffsetPaperLength)),
                     kept.ReadInt16(DevModeBuffer.OffsetPaperLength));
    }

    [Fact]
    public void Windows_paper_flag_skips_paper_matching_so_an_exact_only_match_cannot_fail()
    {
        var settings = new PrintSettings { Paper = PaperSize.A0, PrinterDefaults = PrinterDefault.Paper };
        DevModeRequest request = DevModeMapper.Map(settings, WinPapers, PaperMatching.ExactOnly, false, null, null, null, null);
        Assert.Equal(PrinterDefault.Paper, request.Keep);
        settings.PrinterDefaults = PrinterDefault.None;
        Assert.Throws<InvalidOperationException>(() =>
            DevModeMapper.Map(settings, WinPapers, PaperMatching.ExactOnly, false, null, null, null, null));
    }

    [Fact]
    public void Windows_capabilities_pass_the_driver_defaults_to_the_planner()
    {
        var caps = new WindowsPrinterCapabilities("x", WinPapers, Array.Empty<PrinterBin>(), Array.Empty<PrinterMediaType>(),
            Array.Empty<PrinterResolution>(), 99, true, true, true, 90, null, null,
            new DeviceGeometry(600, 600, 4960, 7016, 50, 50, 4860, 6916))
        {
            Defaults = new DevModeDefaults(PaperSize.Letter, Duplex.Vertical, ColorMode.Grayscale, 3)
        };
        Assert.Equal(PaperSize.Letter, caps.DefaultPaper);
        PrintTargetCapabilities target = caps.ToTargetCapabilities();
        Assert.Equal(PaperSize.Letter, target.DefaultPaper);
        Assert.Equal(Duplex.Vertical, target.DefaultDuplex);
        Assert.Equal(ColorMode.Grayscale, target.DefaultColor);
        Assert.Equal(3, target.DefaultCopies);
    }
}
