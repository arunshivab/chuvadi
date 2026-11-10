using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.Remote;
using Chuvadi.Print.Windows;

// Chuvadi.Print in the trim and NativeAOT smoke app: the JSON files the core and Remote packages write (source-generated
// since D-217 item 1), the page layout and vector placement, the XPS writer (pure managed, any OS), the IPP spool
// options, and the Windows targets behind an OS check. Nothing here prints or opens a network connection.
internal static class PrintChecks
{
    private static void Require(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException("Print: " + what);
    }

    public static string Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "chuvadi-trim-print-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Registry(folder);
            Remote(folder);
            int xpsBytes = Xps();
            Ipp();
            Windows();
            return "Print registry, remote stores, XPS " + xpsBytes + " bytes";
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void Registry(string folder)
    {
        var registry = new PrinterRegistry();
        registry.Set(new SavedPrinter
        {
            Name = "Ward-3",
            Connection = PrinterConnection.Ipp,
            Address = "ipps://10.0.0.21/ipp/print",
            ForcedRoute = PrintRouteKind.Raster,
            Capabilities = new byte[] { 1, 2, 3 },
            CapabilitiesReadAt = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero)
        });
        string path = Path.Combine(folder, "printers.json");
        registry.SaveTo(path);
        Require(File.ReadAllText(path).Contains("\"forcedRoute\": \"Raster\"", StringComparison.Ordinal), "registry JSON writes enums as names");
        PrinterRegistry loaded = PrinterRegistry.Load(path);
        Require(loaded.TryGet("Ward-3", out SavedPrinter? p) && p!.Address == "ipps://10.0.0.21/ipp/print"
                && p.ForcedRoute == PrintRouteKind.Raster && p.Capabilities!.SequenceEqual(new byte[] { 1, 2, 3 }), "registry round trip");
    }

    private static void Remote(string folder)
    {
        var jobs = new FileJobStore(Path.Combine(folder, "jobs"));
        var job = new StoredJob
        {
            JobId = Guid.NewGuid(),
            AgentId = "branch-1",
            Printer = "Ward-3",
            Title = "Discharge summary",
            Copies = 2,
            SubmittedAt = DateTimeOffset.UtcNow
        };
        Require(jobs.Add(job, new byte[] { 37, 80, 68, 70 }) is null, "job store add (null means added)");
        Require(jobs.Add(job, new byte[] { 1 }) is not null, "job store add is idempotent");
        StoredJob? back = new FileJobStore(Path.Combine(folder, "jobs")).Get(job.JobId);
        Require(back is not null && back.Title == "Discharge summary" && back.Copies == 2 && back.State == HubJobState.Queued, "job store round trip");

        string tokens = Path.Combine(folder, "tokens.json");
        new FileAgentTokenStore(tokens).Set(new AgentTokenRecord { AgentId = "branch-1", TokenHash = "abc", IssuedAt = DateTimeOffset.UtcNow });
        Require(new FileAgentTokenStore(tokens).Get("branch-1")?.TokenHash == "abc", "token store round trip");
    }

    private static int Xps()
    {
        var page = new VectorPage(300, 200);
        page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(20, 20, 100, 60)) { Fill = new VectorSolidBrush(new VectorColor(220, 40, 40)) });
        var settings = new PrintSettings { AutoRotate = true };
        PagePlacement placement = PageLayout.Compute(new PageDimensions(page.Width, page.Height), settings, PrintTargetCapabilities.Default);
        VectorPage sheet = VectorLayout.Place(page, placement);
        Require(XpsWriter.CanWrite(new List<VectorPage> { sheet }, out string? why), "XPS CanWrite: " + why);
        using var output = new MemoryStream();
        XpsWriter.Write(new List<VectorPage> { sheet }, output);
        Require(output.Length > 0 && output.GetBuffer()[0] == (byte)'P' && output.GetBuffer()[1] == (byte)'K', "XPS is a zip package");
        return (int)output.Length;
    }

    private static void Ipp()
    {
        using var target = new IppPrinterTarget("ipp://127.0.0.1:1/ipp/print").WithSpool(IppSpoolMode.Memory, 64L * 1024 * 1024);
        Require(target.Spool == IppSpoolMode.Memory && target.MemorySpoolLimit == 64L * 1024 * 1024, "IPP spool options");
    }

    private static void Windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        Require(WindowsPrinters.List() is not null, "Windows printer list");
        var target = new WindowsXpsPrinterTarget("Microsoft Print to PDF");
        Require(new WindowsQueueRoute(target).Kind == PrintRouteKind.WindowsQueue, "Windows queue route");
    }
}
