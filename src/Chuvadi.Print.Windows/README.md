# Chuvadi.Print.Windows

Part of [Chuvadi.Print](../Chuvadi.Print/README.md): pure-BCL printing for .NET, zero NuGet dependencies, in the [Chuvadi](https://github.com/arunshivab/chuvadi) family. It needs the Chuvadi.Print package.

- **Windows printing** (`Chuvadi.Print.Windows`): list printers, read every driver paper, tray, media type and resolution, and print through the GDI spooler with full control of the driver settings, or keep the driver's own default paper, duplex, colour or copies (`PrintSettings.PrinterDefaults`). Plain P/Invoke — no native binaries, and the rest of the library still runs everywhere.
- **XPS documents**: `XpsWriter` writes vector pages (`VectorPage` from Chuvadi.Print) as an XPS package (ECMA-388), with TrueType fonts embedded. Pure managed code; it runs on any OS.
- **Vector printing to Windows queues**: `WindowsXpsPrinterTarget` prints vector pages as XPS through the Print Document Package API, with the job PrintTicket made from the same driver settings as GDI printing, and job status reported back. `WindowsQueueRoute` puts it in a `PrintRouter`.

```csharp
using Chuvadi.Print.Windows;

foreach (PrinterInfo p in WindowsPrinters.List())
    Console.WriteLine(p.Name + (p.IsDefault ? " (default)" : ""));

WindowsPrinterCapabilities caps = WindowsPrinters.GetCapabilities("Ward-3 Laser", job.Settings);
var target = new WindowsPrinterTarget("Ward-3 Laser")
    .WithBin(caps.Bins.FirstOrDefault(b => b.Name == "Tray 2"))
    .WithQuality(PrintQuality.High);

PrintResult printed = job.PrintTo(target);          // target.LastSpoolJobId = spooler job id

// "Microsoft Print to PDF" with an output file writes a PDF, no dialog.
job.PrintTo(new WindowsPrinterTarget("Microsoft Print to PDF").WithOutputFile(@"C:\out\summary.pdf"));

// Bytes unchanged to a queue with the RAW datatype (for example a USB label printer reading ZPL).
new WindowsRawPrinterTarget("Label Printer").Send(File.ReadAllBytes("labels.zpl"), "labels", CancellationToken.None);

// Vector pages as an XPS file.
var page = new VectorPage(595.28, 841.89);
page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(72, 72, 200, 100)) { Fill = new VectorSolidBrush(new VectorColor(40, 120, 220)) });
if (XpsWriter.CanWrite(page, out string? why)) XpsWriter.Write(new[] { page }, @"C:\out\page.xps");

// The same pages printed as vectors through the queue's driver.
new WindowsXpsPrinterTarget("Ward-3 Laser").PrintPages(new[] { page }, new PrintSettings());
```

Full guide: [docs/Chuvadi.Print/GUIDE.md](https://github.com/arunshivab/chuvadi/blob/main/docs/Chuvadi.Print/GUIDE.md).

MIT licensed.
