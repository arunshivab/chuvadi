# Chuvadi.Print.Windows

Part of [Chuvadi.Print](../Chuvadi.Print/README.md): pure-BCL printing for .NET, zero NuGet dependencies, in the [Chuvadi](https://github.com/arunshivab/chuvadi) family. It needs the Chuvadi.Print package.

- **Windows printing** (`Chuvadi.Print.Windows`): list printers, read every driver paper, tray, media type and resolution, and print through the GDI spooler with full control of the driver settings. Plain P/Invoke — no native binaries, and the rest of the library still runs everywhere.

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
```

Full guide: [docs/Chuvadi.Print/GUIDE.md](https://github.com/arunshivab/chuvadi/blob/main/docs/Chuvadi.Print/GUIDE.md).

MIT licensed.
