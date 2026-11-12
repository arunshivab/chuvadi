# Chuvadi.Print

Pure-BCL printing for .NET — zero NuGet dependencies, part of the [Chuvadi](https://github.com/arunshivab/chuvadi) family.

What v1.0 gives you:

- **Device-independent print settings** with every choice exposed: page selection, copies, collation, duplex, colour, all paper sizes, all four orientations, six scale modes, nine alignments, margins, auto-rotate, silent printing, and leaving paper, sides, colour or copies to the printer's own defaults (`PrinterDefaults`).
- **`PrintJob`**: plans the job, renders one page at a time (memory does not grow with document length), lays each page out on the sheet, converts colour, and sends it to a print target. One audit event per job.
- **Spool envelope**: a checksummed binary package of PDF + settings + job metadata, for handing a job from a web host to a print agent.
- **`ImageFileTarget`**: writes each printed sheet as a PNG — print preview, archiving, and visual verification.
- **`PrintRouter`**: chooses how each job reaches a printer (the PDF as-is, CUPS, or rendered pages), in a fixed
  order, and writes the route and any fallback to the audit trail.
- **`PrinterRegistry`**: the printers an application has set up, with their last-read capabilities, saved as JSON.
- **`VectorPage`**: a page as vector content (paths, glyph runs, images, groups) for vector print routes; Chuvadi.Print.Windows writes it as XPS.

Devices and transports are separate packages, so an application takes only what it uses:

| Package | What it adds |
|---|---|
| [Chuvadi.Print.Windows](../Chuvadi.Print.Windows/README.md) | Windows printers through the GDI spooler, and the XPS writer |
| [Chuvadi.Print.Ipp](../Chuvadi.Print.Ipp/README.md) | Driverless IPP printing on any OS |
| [Chuvadi.Print.Remote](../Chuvadi.Print.Remote/README.md) | An embeddable print hub and dial-out print agents |

Rasterisation is pluggable through `IPageRasterizer`; the PDF renderer plugs in through the separate **Chuvadi.Print.Pdf.Raster** adapter.

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
    .WithUser("user1").WithTenant("tenant-a")
    .WithRasterizer(rasterizer)                       // e.g. from Chuvadi.Print.Pdf.Raster
    .WithAuditSink(new FileAuditSink("print-audit.log"));

// Print it (here: to PNG files of each sheet).
PrintResult result = job.PrintTo(new ImageFileTarget("preview").WithDpi(150));

// Know what will print before printing it.
PrintPlan plan = job.Plan();          // pages, impressions, sheets, blank backs

// Hand the job to another machine, and pick it up there.
job.SaveSpoolTo("job.cvpr");
PrintJob received = PrintJob.LoadSpool("job.cvpr").WithRasterizer(rasterizer);
```

Full guide: [docs/Chuvadi.Print/GUIDE.md](https://github.com/arunshivab/chuvadi/blob/main/docs/Chuvadi.Print/GUIDE.md).

MIT licensed.
