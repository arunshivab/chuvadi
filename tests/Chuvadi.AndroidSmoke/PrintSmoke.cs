using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.Remote;
using Chuvadi.Print.Windows;

namespace Chuvadi.AndroidSmoke;

/// <summary>
/// Uses Chuvadi.Print, Chuvadi.Print.Ipp and Chuvadi.Print.Remote (and Chuvadi.Print.Windows behind an OS check) so that
/// compiling this assembly proves the Android target can reference them. Like the rest of this project, nothing runs.
/// </summary>
public static class PrintSmoke
{
    /// <summary>Builds a job, an IPP target with a memory spool, a router, a hub job record and an XPS page.</summary>
    public static int Build()
    {
        var settings = new PrintSettings { Paper = PaperSize.A4, Copies = 2, Duplex = Duplex.Vertical };
        var target = new IppPrinterTarget("ipps://10.0.0.21/ipp/print").WithSpool(IppSpoolMode.Memory, 64L * 1024 * 1024);
        using PrintRouter router = IppRoutes.CreateRouter("ipps://10.0.0.21/ipp/print");
        var stored = new StoredJob { JobId = Guid.NewGuid(), AgentId = "branch-1", Printer = "Ward-3", Copies = settings.Copies };

        var page = new VectorPage(595.28, 841.89);
        page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(72, 72, 200, 100)) { Fill = new VectorSolidBrush(new VectorColor(40, 120, 220)) });
        using var xps = new MemoryStream();
        XpsWriter.Write(new List<VectorPage> { page }, xps);

        string windows = OperatingSystem.IsWindows() ? new WindowsXpsPrinterTarget("Microsoft Print to PDF").Name : "not Windows";
        target.Dispose();
        return (int)xps.Length + stored.Copies + windows.Length + (router.FontInspector is null ? 0 : 1)
               + (FontEmbedding.NeedsGeneratedAppearances == FontEmbedding.Unknown ? 1 : 0);
    }
}
