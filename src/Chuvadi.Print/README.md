# Chuvadi.Print

Pure-BCL printing for .NET — zero NuGet dependencies, part of the [Chuvadi](https://github.com/arunshivab/chuvadi) family.

What v0.2 gives you:

- **Device-independent print settings** with every choice exposed: page selection, copies, collation, duplex, colour, all paper sizes, all four orientations, six scale modes, nine alignments, margins, auto-rotate, silent printing.
- **`PrintJob`**: plans the job, renders one page at a time (memory does not grow with document length), lays each page out on the sheet, converts colour, and sends it to a print target. One audit event per job.
- **Spool envelope**: a checksummed binary package of PDF + settings + job metadata, for handing a job from a web host to a print agent.
- **`ImageFileTarget`**: writes each printed sheet as a PNG — print preview, archiving, and visual verification.

Rasterisation is pluggable through `IPageRasterizer`; the PDF renderer plugs in through the separate **Chuvadi.Print.Pdf** adapter. The Windows spooler (`Chuvadi.Print.Windows`) arrives in a later layer.

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

MIT licensed.
