# Chuvadi.Print

Pure-BCL printing for .NET — zero NuGet dependencies, part of the [Chuvadi](https://github.com/arunshivab/chuvadi) family.

What v0.4 gives you:

- **Device-independent print settings** with every choice exposed: page selection, copies, collation, duplex, colour, all paper sizes, all four orientations, six scale modes, nine alignments, margins, auto-rotate, silent printing.
- **`PrintJob`**: plans the job, renders one page at a time (memory does not grow with document length), lays each page out on the sheet, converts colour, and sends it to a print target. One audit event per job.
- **Spool envelope**: a checksummed binary package of PDF + settings + job metadata, for handing a job from a web host to a print agent.
- **`ImageFileTarget`**: writes each printed sheet as a PNG — print preview, archiving, and visual verification.
- **Windows printing** (`Chuvadi.Print.Windows`): list printers, read every driver paper, tray, media type and resolution, and print through the GDI spooler with full control of the driver settings. Plain P/Invoke — no native binaries, and the rest of the library still runs everywhere.
- **Driverless IPP printing** (`Chuvadi.Print.Ipp`), on any OS: send rendered pages as PWG Raster to any IPP Everywhere printer, or pass the PDF itself through to printers that accept it. Includes a complete IPP encoder/decoder, client, and PWG Raster reader/writer.

Rasterisation is pluggable through `IPageRasterizer`; the PDF renderer plugs in through the separate **Chuvadi.Print.Pdf** adapter.

```csharp
using Chuvadi.Print;

// Describe the job, with full control over layout.
PrintJob job = new PrintJob("discharge-summary.pdf")
    .WithPages(PageSelection.Range("1-4, 7"))
    .WithCopies(2, collate: true)
    .WithDuplex(Duplex.Vertical)
    .WithPaper(PaperSize.A4)
    .WithScale(ScaleMode.FitToPage)
    .WithAlignment(ContentAlignment.Center)
    .WithColor(ColorMode.Monochrome)
    .WithMonochrome(MonochromeConversion.ErrorDiffusion)
    .WithUser("dr.arun").WithTenant("apulki-pune")
    .WithRasterizer(rasterizer)                       // e.g. from Chuvadi.Print.Pdf
    .WithAuditSink(new FileAuditSink("print-audit.log"));

// Print it (here: to PNG files of each sheet).
PrintResult result = job.PrintTo(new ImageFileTarget("preview").WithDpi(150));

// Know what will print before printing it.
PrintPlan plan = job.Plan();          // pages, impressions, sheets, blank backs

// Hand the job to another machine, and pick it up there.
job.SaveSpoolTo("job.cvpr");
PrintJob received = PrintJob.LoadSpool("job.cvpr").WithRasterizer(rasterizer);
```

Printing to a Windows printer:

```csharp
using Chuvadi.Print.Windows;

foreach (PrinterInfo p in WindowsPrinters.List())
    Console.WriteLine(p.Name + (p.IsDefault ? " (default)" : ""));

WindowsPrinterCapabilities caps = WindowsPrinters.GetCapabilities("Ward-3 LaserJet", job.Settings);
var target = new WindowsPrinterTarget("Ward-3 LaserJet")
    .WithBin(caps.Bins.FirstOrDefault(b => b.Name == "Tray 2"))
    .WithQuality(PrintQuality.High);

PrintResult printed = job.PrintTo(target);          // target.LastSpoolJobId = spooler job id

// "Microsoft Print to PDF" with an output file writes a PDF, no dialog.
job.PrintTo(new WindowsPrinterTarget("Microsoft Print to PDF").WithOutputFile(@"C:\out\summary.pdf"));
```

Printing over IPP (no driver, any OS):

```csharp
using Chuvadi.Print.Ipp;

// The printer receives the PDF and lays it out itself.
using var pdfPrinter = new IppDocumentTarget("ipp://ward3-printer.local/ipp/print");
PrintResult sent = job.SubmitTo(pdfPrinter);        // pdfPrinter.LastApproximations lists anything IPP cannot express

// Or send finished pages exactly as laid out here, as PWG Raster.
using var rasterPrinter = new IppPrinterTarget("ipps://10.0.0.21/ipp/print",
                                               new IppClientOptions { AcceptAnyServerCertificate = true })
    .WithJobOptions(o => { o.MediaSource = "tray-2"; o.Quality = IppPrintQuality.High; });
job.PrintTo(rasterPrinter);
```

MIT licensed.
