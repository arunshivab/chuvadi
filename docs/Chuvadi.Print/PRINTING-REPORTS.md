# Printing Docs and Sheets reports

LiPi prints Word (docx) and Excel (xlsx) reports by composing three libraries, with no new adapter:

```
Chuvadi.Docs / Chuvadi.Sheets   build the report (docx / xlsx)
        |
Chuvadi.Layout                  lays it out and renders it to PDF bytes     (owned by the Layout chat)
        |
Chuvadi.Print                   PrintJob: settings, plan, audit, spool envelope
        |-- PdfPageRasterizer (Chuvadi.Print.Pdf) --> WindowsPrinterTarget / IppPrinterTarget / ImageFileTarget
        |-- PdfFileTarget     (Chuvadi.Print.Pdf) --> a PDF file, pages placed as vectors
        |-- IppDocumentTarget                      --> an IPP printer that accepts PDF (no rendering)
        '-- PrintHub.Submit   (Chuvadi.Print.Remote) --> a PrintAgent at the branch, which prints with any of the above
```

The only contract between Layout and Print is **a PDF**: `PrintJob` takes the bytes, a stream or a file path.
Layout embeds the fonts and shapes the text (Tamil, Devanagari, ...) into glyphs, so whatever prints the PDF
needs no fonts installed and reproduces the text exactly.

The Layout call below is a placeholder (`layout.ToPdf(...)`): its exact name and options belong to
Chuvadi.Layout. Everything after it is Chuvadi.Print API.

## To a printer on this machine

```csharp
byte[] pdf = layout.ToPdf(dischargeSummaryDocx);          // Chuvadi.Layout (placeholder)

PrintResult result = new PrintJob(pdf)
    .WithTitle("Discharge summary").WithUser("dr.arun").WithTenant("apulki")
    .WithCopies(2, collate: true).WithDuplex(Duplex.Vertical).WithColor(ColorMode.Monochrome)
    .WithRasterizer(new PdfPageRasterizer())                // Chuvadi.Print.Pdf
    .WithAuditSink(new FileAuditSink(@"D:\lipi\print-audit.log"))
    .PrintTo(new WindowsPrinterTarget("Ward-3 LaserJet"));  // or new IppPrinterTarget("ipp://...")
```

## To a PDF file ("print to PDF")

```csharp
new PrintJob(pdf).WithPages(PageSelection.Range("1, 3")).WithPaper(PaperSize.A5).WithScale(ScaleMode.FitToPage)
    .SubmitTo(new PdfFileTarget(@"D:\out\discharge-A5.pdf"));
```
The result is a new PDF in the printed layout (sheet size, scaling, copies, duplex blank backs). Text stays
text, because the pages are placed as vectors and never rasterised.

## To a printer at a branch (LiPi Connect)

```csharp
// Web host: lay out, then queue for the branch agent (idempotent per JobId).
hub.Submit("ward3-pc", "Ward-3 LaserJet", new PrintJob(pdf).WithCopies(2).WithTitle("Discharge summary"));

// Branch agent (PrintAgentOptions): Rasterizer = new PdfPageRasterizer(),
//   PrinterResolver = name => new WindowsPrinterTarget(name)
```
The spool envelope carries the PDF, the settings and the job metadata (P04); the agent renders at the printer's
own resolution. Exactly one audit event is recorded per job (P06).

## Packages

| Need | Packages |
|---|---|
| Print to a Windows or IPP printer (rendered) | Chuvadi.Print, Chuvadi.Print.Pdf |
| Print to a PDF file | Chuvadi.Print, Chuvadi.Print.Pdf |
| PDF passthrough to an IPP printer | Chuvadi.Print |
| Remote printing | Chuvadi.Print (hub and agent); the agent also needs Chuvadi.Print.Pdf to render |

## Verified

The tests in `staging/Chuvadi.Print.Pdf.Tests/LayoutCompositionTests.cs` run this composition. A stand-in for
Layout (`StubLayout`, which writes the PDF with the Chuvadi PDF authoring API) produces the report, which is then:
- printed to a device: 2 copies, double-sided, monochrome, one audit event;
- printed to an A5 PDF file with a page selection;
- carried through the spool envelope and printed on the other side.

The test also writes a preview PNG. Replace `StubLayout` with the real Chuvadi.Layout call once Layout is in
the monorepo.
