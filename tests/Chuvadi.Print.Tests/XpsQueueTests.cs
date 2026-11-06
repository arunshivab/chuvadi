using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Chuvadi.Print;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.Tests;

/// <summary>
/// Phase 4 part 3 (D-223 step 4, D-238): vector page placement, the XPS route's sheet plan, and the shape of the COM
/// declarations for the Print Document Package API. The submission itself is Windows-only and is checked on Arun's PC
/// (WINDOWS-VERIFICATION 11g); everything here runs on any OS.
/// </summary>
#pragma warning disable CA1416 // the parts under test are OS-independent; Windows-only calls are guarded
public class XpsQueueTests
{
    // ------------------------------------------------------------------ helpers

    private static VectorPage Page(double width, double height, byte marker = 0)
    {
        var page = new VectorPage(width, height);
        page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(0, 0, 10, 10))
        {
            Fill = new VectorSolidBrush(new VectorColor(marker, 0, 0))
        });
        return page;
    }

    private static (double X, double Y) Map(VectorMatrix m, double x, double y)
    {
        VectorPoint p = m.Transform(new VectorPoint(x, y));
        return (Math.Round(p.X, 6), Math.Round(p.Y, 6));
    }

    private static PrintSettings Settings(Action<PrintSettings>? configure = null)
    {
        var s = new PrintSettings { Margins = Margins.None };
        configure?.Invoke(s);
        return s;
    }

    private static byte Marker(VectorPage sheet)
    {
        var group = (VectorGroup)Assert.Single(sheet.Elements);
        var path = (VectorPath)group.Children[0];
        return ((VectorSolidBrush)path.Fill!).Color.R;
    }

    // ------------------------------------------------------------------ VectorLayout

    [Fact]
    public void Placement_without_rotation_scales_and_moves_the_page_into_the_content_bounds()
    {
        PrintSettings s = Settings(x => x.Scale = ScaleMode.FitToPage);
        PagePlacement placement = PageLayout.Compute(new PageDimensions(297.64, 420.945), s, PrintTargetCapabilities.Default);
        VectorMatrix m = VectorLayout.PageToSheet(297.64, 420.945, placement);
        RectanglePoints b = placement.ContentBounds;
        Assert.Equal((Math.Round(b.X, 6), Math.Round(b.Y, 6)), Map(m, 0, 0));
        Assert.Equal((Math.Round(b.X + b.Width, 6), Math.Round(b.Y + b.Height, 6)), Map(m, 297.64, 420.945));
    }

    [Theory]
    [InlineData(PageOrientation.ReversePortrait)]
    [InlineData(PageOrientation.ReverseLandscape)]
    public void Reverse_orientations_turn_the_page_half_way(PageOrientation orientation)
    {
        PrintSettings s = Settings(x => x.Orientation = orientation);
        PageDimensions sheet = PageLayout.SheetFor(s.Paper, orientation);
        PagePlacement placement = PageLayout.Compute(sheet, s, PrintTargetCapabilities.Default);
        Assert.Equal(PageRotation.Rotate180, placement.Rotation);
        VectorMatrix m = VectorLayout.PageToSheet(sheet.WidthPoints, sheet.HeightPoints, placement);
        RectanglePoints b = placement.ContentBounds;
        // The page's top-left corner lands at the bottom-right of the content bounds.
        Assert.Equal((Math.Round(b.X + b.Width, 6), Math.Round(b.Y + b.Height, 6)), Map(m, 0, 0));
        Assert.Equal((Math.Round(b.X, 6), Math.Round(b.Y, 6)), Map(m, sheet.WidthPoints, sheet.HeightPoints));
    }

    [Fact]
    public void Auto_rotate_turns_a_landscape_page_clockwise_onto_a_portrait_sheet()
    {
        PrintSettings s = Settings(x => x.AutoRotate = true);
        PagePlacement placement = PageLayout.Compute(new PageDimensions(400, 200), s, PrintTargetCapabilities.Default);
        Assert.Equal(PageRotation.Clockwise90, placement.Rotation);
        VectorMatrix m = VectorLayout.PageToSheet(400, 200, placement);
        RectanglePoints b = placement.ContentBounds;
        // Clockwise: the page's top-left corner goes to the top-right, its bottom-left to the top-left.
        Assert.Equal((Math.Round(b.X + b.Width, 6), Math.Round(b.Y, 6)), Map(m, 0, 0));
        Assert.Equal((Math.Round(b.X, 6), Math.Round(b.Y, 6)), Map(m, 0, 200));
        Assert.Equal((Math.Round(b.X, 6), Math.Round(b.Y + b.Height, 6)), Map(m, 400, 200));
    }

    [Fact]
    public void A_quarter_turn_the_other_way_maps_the_corners_counter_clockwise()
    {
        PrintSettings s = Settings(x =>
        {
            x.AutoRotate = true;
            x.Orientation = PageOrientation.ReversePortrait;
        });
        PagePlacement placement = PageLayout.Compute(new PageDimensions(400, 200), s, PrintTargetCapabilities.Default);
        Assert.Equal(PageRotation.Clockwise270, placement.Rotation);
        VectorMatrix m = VectorLayout.PageToSheet(400, 200, placement);
        RectanglePoints b = placement.ContentBounds;
        // Counter-clockwise: the page's top-left corner goes to the bottom-left, its top-right to the top-left.
        Assert.Equal((Math.Round(b.X, 6), Math.Round(b.Y + b.Height, 6)), Map(m, 0, 0));
        Assert.Equal((Math.Round(b.X, 6), Math.Round(b.Y, 6)), Map(m, 400, 0));
    }

    [Fact]
    public void Place_wraps_the_page_in_one_clipped_group_on_a_sheet_sized_page()
    {
        VectorPage page = Page(200, 300, marker: 7);
        PrintSettings s = Settings();
        PagePlacement placement = PageLayout.Compute(new PageDimensions(200, 300), s, PrintTargetCapabilities.Default);
        VectorPage sheet = VectorLayout.Place(page, placement);
        Assert.Equal(placement.SheetSize.WidthPoints, sheet.Width);
        Assert.Equal(placement.SheetSize.HeightPoints, sheet.Height);
        var group = (VectorGroup)Assert.Single(sheet.Elements);
        Assert.Same(page.Elements[0], Assert.Single(group.Children));
        Assert.NotNull(group.Clip);
        Assert.Equal(VectorLayout.PageToSheet(200, 300, placement), group.Transform);
        Assert.True(XpsWriter.CanWrite(sheet, out string? reason), reason);
    }

    [Fact]
    public void An_empty_page_becomes_an_empty_sheet()
    {
        PagePlacement placement = PageLayout.Compute(new PageDimensions(200, 300), Settings(), PrintTargetCapabilities.Default);
        Assert.Empty(VectorLayout.Place(new VectorPage(200, 300), placement).Elements);
    }

    // ------------------------------------------------------------------ sheet plan

    private static IReadOnlyList<VectorPage> Build(int pages, PrintSettings settings, bool targetCopies, bool skipBlanks = false)
    {
        var list = new List<VectorPage>();
        for (int i = 1; i <= pages; i++) list.Add(Page(595.28, 841.89, (byte)i));
        return XpsSheetBuilder.Build(new VectorPageList(list), settings, PrintTargetCapabilities.Default, MarginReference.SheetEdge,
                                     targetCopies, skipBlanks, CancellationToken.None);
    }

    [Fact]
    public void Software_copies_repeat_the_sheets_collated()
    {
        IReadOnlyList<VectorPage> sheets = Build(2, Settings(x => { x.Copies = 2; x.Collate = true; }), targetCopies: false);
        Assert.Equal(new byte[] { 1, 2, 1, 2 }, sheets.Select(Marker).ToArray());
        Assert.Same(sheets[0], sheets[2]); // each page is converted and placed once
    }

    [Fact]
    public void Software_copies_uncollated_repeat_each_page()
    {
        IReadOnlyList<VectorPage> sheets = Build(2, Settings(x => { x.Copies = 2; x.Collate = false; }), targetCopies: false);
        Assert.Equal(new byte[] { 1, 1, 2, 2 }, sheets.Select(Marker).ToArray());
    }

    [Fact]
    public void Copies_the_driver_makes_are_sent_once()
        => Assert.Equal(2, Build(2, Settings(x => x.Copies = 3), targetCopies: true).Count);

    [Fact]
    public void Duplex_copies_get_a_blank_back_so_each_copy_starts_on_a_new_sheet()
    {
        IReadOnlyList<VectorPage> sheets = Build(3, Settings(x => { x.Copies = 2; x.Duplex = Duplex.Vertical; }), targetCopies: false);
        // 1 2 | 3 blank | 1 2 | 3: the blank keeps the second copy on a new sheet; nothing follows the last page.
        Assert.Equal(7, sheets.Count);
        Assert.Empty(sheets[3].Elements);
        Assert.Equal(new byte[] { 1, 2, 3 }, new[] { sheets[4], sheets[5], sheets[6] }.Select(Marker).ToArray());
        Assert.Equal(sheets[0].Width, sheets[3].Width);
    }

    [Fact]
    public void Blank_backs_are_left_out_when_the_queue_prints_one_side()
        => Assert.Equal(6, Build(3, Settings(x => { x.Copies = 2; x.Duplex = Duplex.Vertical; }), targetCopies: false, skipBlanks: true).Count);

    [Fact]
    public void The_page_selection_picks_the_pages()
    {
        IReadOnlyList<VectorPage> sheets = Build(4, Settings(x => x.Pages = PageSelection.Explicit(3, 1)), targetCopies: false);
        Assert.Equal(new byte[] { 1, 3 }, sheets.Select(Marker).ToArray());
    }

    [Fact]
    public void A_job_with_no_pages_is_refused()
        => Assert.ThrowsAny<Exception>(() => Build(0, Settings(), targetCopies: false));

    [Fact]
    public void Pages_land_where_the_raster_route_puts_them()
    {
        var caps = new PrintTargetCapabilities { HardMargins = new Margins(18, 18, 18, 18) };
        PrintSettings s = Settings(x => { x.Margins = new Margins(36, 36, 36, 36); x.Alignment = ContentAlignment.Center; });
        var page = Page(300, 300, 1);
        IReadOnlyList<VectorPage> sheets = XpsSheetBuilder.Build(new VectorPageList(new[] { page }), s, caps, MarginReference.PrintableArea,
                                                                targetMakesCopies: false, skipBlankSides: false, CancellationToken.None);
        PagePlacement expected = PageLayout.Compute(new PageDimensions(300, 300), s, caps, MarginReference.PrintableArea);
        var group = (VectorGroup)Assert.Single(Assert.Single(sheets).Elements);
        Assert.Equal(VectorLayout.PageToSheet(300, 300, expected), group.Transform);
    }

    [Fact]
    public void The_laid_out_sheets_are_valid_xps()
    {
        IReadOnlyList<VectorPage> sheets = Build(2, Settings(x => { x.Copies = 2; x.Duplex = Duplex.Vertical; }), targetCopies: false);
        Assert.True(XpsWriter.CanWrite(sheets, out string? reason), reason);
        using var ms = new MemoryStream();
        XpsWriter.Write(sheets, ms);
        Assert.True(ms.Length > 0);
    }

    // ------------------------------------------------------------------ target and route without a queue

    [Fact]
    public void Target_and_route_basics()
    {
        Assert.Throws<ArgumentException>(() => new WindowsXpsPrinterTarget(""));
        var target = new WindowsXpsPrinterTarget("Queue");
        Assert.Equal(WindowsXpsPrinterTarget.DefaultStatusTimeout, target.StatusTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), WindowsXpsPrinterTarget.DefaultStatusTimeout);
        Assert.Equal(CopyHandling.Auto, target.CopyHandling);
        Assert.Equal(MarginReference.SheetEdge, target.MarginReference);
        Assert.Equal(PaperMatching.ExactOrCustom, target.PaperMatching);
        Assert.False(target.StrictCapabilities);
        Assert.Throws<ArgumentOutOfRangeException>(() => target.StatusTimeout = TimeSpan.FromSeconds(-1));
        target.StatusTimeout = Timeout.InfiniteTimeSpan;
        target.StatusTimeout = null;
        Assert.False(target.CanPrint(new byte[] { 1 }, new PrintSettings(), out string? reason));
        Assert.Equal("no vector page source is set", reason);

        var route = new WindowsQueueRoute(target);
        Assert.Equal(PrintRouteKind.WindowsQueue, route.Kind);
        Assert.Equal("Queue", route.Name);
        Assert.Throws<ArgumentNullException>(() => new WindowsQueueRoute(null!));
    }

    [Fact]
    public void CanPrint_explains_pages_xps_cannot_express()
    {
        var page = new VectorPage(100, 100);
        page.Elements.Add(new VectorGroup { Knockout = true, Children = { new VectorPath(VectorGeometry.FromRectangle(0, 0, 5, 5)) { Fill = new VectorSolidBrush(VectorColor.Black) } } });
        var target = new WindowsXpsPrinterTarget("Queue");
        Assert.False(target.CanPrint(new[] { Page(100, 100), page }, new PrintSettings(), out string? reason));
        Assert.StartsWith("page 2: ", reason);
        Assert.True(target.CanPrint(new[] { Page(100, 100), page }, new PrintSettings { Pages = PageSelection.First(1) }, out reason), reason);
        Assert.False(target.CanPrint(Array.Empty<VectorPage>(), new PrintSettings(), out reason));
    }

    [Fact]
    public void Printing_needs_windows()
    {
        if (OperatingSystem.IsWindows()) return;
        var target = new WindowsXpsPrinterTarget("Queue");
        Assert.Throws<PlatformNotSupportedException>(() => target.PrintPages(new[] { Page(100, 100) }, new PrintSettings()));
    }

    // ------------------------------------------------------------------ status sink

    [Fact]
    public void The_status_sink_keeps_the_latest_status_and_signals_when_the_job_is_done()
    {
        using var sink = new PackageStatusSink();
        Assert.Null(sink.Latest);
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<PrintDocumentPackageStatus>());
        try
        {
            Marshal.StructureToPtr(new PrintDocumentPackageStatus { JobId = 12, CurrentPageTotal = 1, Completion = 0 }, buffer, false);
            Assert.Equal(0, sink.PackageStatusUpdated(buffer));
            Assert.False(sink.Finished.WaitOne(0));
            Marshal.StructureToPtr(new PrintDocumentPackageStatus { JobId = 12, CurrentPageTotal = 2, Completion = 1 }, buffer, false);
            Assert.Equal(0, sink.PackageStatusUpdated(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        Assert.True(sink.Finished.WaitOne(0));
        Assert.Equal(2, sink.Latest!.Value.CurrentPageTotal);
        Assert.Equal(12u, sink.Latest!.Value.JobId);
        Assert.Equal(2, sink.Updates);
        Assert.NotEqual(0, sink.PackageStatusUpdated(0));
        Assert.NotEqual(0, sink.Invoke(1, Guid.Empty, 0, 1, 0, 0, 0, 0));
    }

    [Fact]
    public void Failed_and_cancelled_jobs_become_windows_print_exceptions()
    {
        XpsPackageSubmitter.ThrowIfUnsuccessful(new PrintDocumentPackageStatus { Completion = 1 }, "Q");
        XpsPackageSubmitter.ThrowIfUnsuccessful(new PrintDocumentPackageStatus { Completion = 0 }, "Q");
        Assert.Contains("cancelled", Assert.Throws<WindowsPrintException>(
            () => XpsPackageSubmitter.ThrowIfUnsuccessful(new PrintDocumentPackageStatus { Completion = 2 }, "Q")).Message);
        Assert.Contains("0x80070005", Assert.Throws<WindowsPrintException>(
            () => XpsPackageSubmitter.ThrowIfUnsuccessful(new PrintDocumentPackageStatus { Completion = 3, PackageStatus = unchecked((int)0x80070005) }, "Q")).Message);
    }

    // ------------------------------------------------------------------ COM declarations (D-238 provenance)

    private static Type Internal(string name)
        => typeof(WindowsPrinters).Assembly.GetType("Chuvadi.Print.Windows." + name, throwOnError: true)!;

    /// <summary>The interface's own methods in declaration order (the vtable order after IUnknown).</summary>
    private static string[] Slots(string name)
    {
        Type type = Internal(name);
        // The generator re-declares inherited methods on a derived interface; those are the base's slots, not this one's.
        var inherited = new HashSet<string>(type.GetInterfaces().SelectMany(i => i.GetMethods()).Select(m => m.Name));
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                   .Where(m => !inherited.Contains(m.Name))
                   .OrderBy(m => m.MetadataToken).Select(m => m.Name).ToArray();
    }

    [Theory]
    [InlineData("IComStream", "0000000c-0000-0000-c000-000000000046", "Read,Write,Seek")]
    [InlineData("IPrintDocumentPackageTargetFactory", "d2959bf7-b31b-4a3d-9600-712eb1335ba4", "CreateDocumentPackageTargetForPrintJob")]
    [InlineData("IPrintDocumentPackageTarget", "1b8efec4-3019-4c27-964e-367202156906", "GetPackageTargetTypes,GetPackageTarget,Cancel")]
    [InlineData("IXpsDocumentPackageTarget", "3b0b6d38-53ad-41da-b212-d37637a6714e", "GetXpsOMPackageWriter,GetXpsOMFactory")]
    [InlineData("IXpsOMPackage", "18c3df65-81e1-4674-91dc-fc452f5a416f", "GetDocumentSequence")]
    [InlineData("IXpsOMDocumentSequence", "56492eb4-d8d5-425e-8256-4c2b64ad0264", "GetPartName,Reserved01SetPartName,Reserved02GetOwner,GetDocuments")]
    [InlineData("IXpsOMDocumentCollection", "d1c87f0d-e947-4754-8a25-971478f7e83e", "GetCount,GetAt")]
    [InlineData("IXpsOMDocument", "2c2c94cb-ac5f-4254-8ee9-23948309d9f0", "GetPartName,Reserved01SetPartName,Reserved02GetOwner,GetPageReferences")]
    [InlineData("IXpsOMPageReferenceCollection", "ca16ba4d-e7b9-45c5-958b-f98022473745", "GetCount,GetAt")]
    [InlineData("IXpsOMPageReference", "ed360180-6f92-4998-890d-2f208531a0a0", "Reserved00GetOwner,GetPage,Reserved02SetPage,DiscardPage,Reserved04IsPageLoaded,GetAdvisoryPageDimensions")]
    [InlineData("IXpsOMPackageWriter", "4e2aa182-a443-42c6-b41b-4f8e9de73ff9", "StartNewDocument,AddPage,Reserved02AddResource,Close")]
    [InlineData("IConnectionPointContainer", "b196b284-bab4-101a-b69c-00aa00341d07", "Reserved00EnumConnectionPoints,FindConnectionPoint")]
    [InlineData("IConnectionPoint", "b196b286-bab4-101a-b69c-00aa00341d07", "Reserved00GetConnectionInterface,Reserved01GetConnectionPointContainer,Advise,Unadvise")]
    [InlineData("IDispatchSlots", "00020400-0000-0000-c000-000000000046", "GetTypeInfoCount,GetTypeInfo,GetIDsOfNames,Invoke")]
    [InlineData("IPrintDocumentPackageStatusEvent", "ed90c8ad-5c34-4d05-a1ec-0e8a9b3ad7af", "PackageStatusUpdated")]
    public void Com_interfaces_keep_their_ids_and_slot_order(string name, string iid, string slots)
    {
        Type type = Internal(name);
        Assert.NotNull(type.GetCustomAttribute<GeneratedComInterfaceAttribute>());
        Assert.Equal(new Guid(iid), type.GUID);
        Assert.Equal(slots.Split(','), Slots(name));
    }

    [Fact]
    public void The_object_factory_slots_put_the_two_calls_in_their_places()
    {
        string[] slots = Slots("IXpsOMObjectFactory");
        Assert.Equal(new Guid("f9b2a685-a50d-4fc2-b764-b56e093ea0ca"), Internal("IXpsOMObjectFactory").GUID);
        Assert.Equal(36, slots.Length);
        Assert.Equal("CreatePackageFromStream", slots[2]);
        Assert.Equal("CreatePartUri", slots[35]);
        Assert.All(slots.Where((s, i) => i != 2 && i != 35), s => Assert.StartsWith("Reserved", s));
    }

    [Fact]
    public void The_status_event_derives_from_idispatch_and_the_sink_is_a_generated_com_class()
    {
        Assert.Contains(Internal("IDispatchSlots"), Internal("IPrintDocumentPackageStatusEvent").GetInterfaces());
        Assert.NotNull(typeof(PackageStatusSink).GetCustomAttribute<GeneratedComClassAttribute>());
        Assert.Equal(24, Marshal.SizeOf<PrintDocumentPackageStatus>());
        Assert.Equal(8, Marshal.SizeOf<XpsSize>());
    }

    [Fact]
    public void Package_target_ids_are_as_documented()
    {
        Assert.Equal(new Guid("348ef17d-6c81-4982-92b4-ee188a43867a"), XpsComIds.PrintDocumentPackageTargetFactoryClass);
        Assert.Equal(new Guid("9cae40a8-ded1-41c9-a9fd-d735ef33aeda"), XpsComIds.TargetTypeMsXps);
        Assert.Equal(new Guid("0056bb72-8c9c-4612-bd0f-93012a87099d"), XpsComIds.TargetTypeOpenXps);
    }

    [Fact]
    public void Xps_entry_points_are_source_generated_hresult_calls()
    {
        MethodInfo[] methods = Internal("XpsNativeMethods")
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.Name.StartsWith("<", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(new[] { "CoCreateInstance", "CoInitializeEx", "CoUninitialize", "CreateStreamOnHGlobal", "PTCloseProvider",
                             "PTConvertDevModeToPrintTicket", "PTOpenProvider" },
                     methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        foreach (MethodInfo m in methods)
        {
            LibraryImportAttribute a = m.GetCustomAttribute<LibraryImportAttribute>()!;
            Assert.NotNull(a);
            Assert.Contains(a.LibraryName, new[] { "ole32.dll", "prntvpt.dll" });
            Assert.False(a.SetLastError, m.Name + " reports through its HRESULT");
            if (m.GetParameters().Any(p => p.ParameterType == typeof(string))) Assert.Equal(StringMarshalling.Utf16, a.StringMarshalling);
        }
    }
}
#pragma warning restore CA1416
