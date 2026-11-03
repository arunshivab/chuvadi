# Chuvadi.Print.Ipp

Part of [Chuvadi.Print](../Chuvadi.Print/README.md): pure-BCL printing for .NET, zero NuGet dependencies, in the [Chuvadi](https://github.com/arunshivab/chuvadi) family. It needs the Chuvadi.Print package.

- **Driverless IPP printing** (`Chuvadi.Print.Ipp`), on any OS: send rendered pages as PWG Raster to any IPP Everywhere printer, or pass the PDF itself through to printers that accept it. Includes a complete IPP encoder/decoder, client, and PWG Raster reader/writer.

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

Full guide: [docs/Chuvadi.Print/GUIDE.md](https://github.com/arunshivab/chuvadi/blob/main/docs/Chuvadi.Print/GUIDE.md).

MIT licensed.
