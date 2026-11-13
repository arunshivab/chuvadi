# Chuvadi.Print.Ipp

Part of [Chuvadi.Print](../Chuvadi.Print/README.md): pure-BCL printing for .NET, zero NuGet dependencies, in the [Chuvadi](https://github.com/arunshivab/chuvadi) family. It needs the Chuvadi.Print package.

This package covers **network printing**: IPP (and IPPS, and CUPS queues over IPP) plus the raw port 9100. Windows
print queues are in Chuvadi.Print.Windows.

- **Driverless IPP printing** (`Chuvadi.Print.Ipp`), on any OS: send rendered pages as PWG Raster to any IPP Everywhere printer, or pass the PDF itself through to printers that accept it. Includes a complete IPP encoder/decoder, client, and PWG Raster reader/writer.
- **Routing**: `IppRoutes` builds a `PrintRouter` that sends the PDF as-is when every font is embedded and the printer lists `application/pdf`, and PWG Raster otherwise. `IppCapabilityCache` keeps printer capabilities for 10 minutes.
- **Certificate pinning** for `ipps://` printers with their own certificates (`IppClientOptions.PinnedCertificates`, `IppCertificates`).
- **Discovery** (`NetworkPrinterDiscovery`): finds IPP, IPPS and raw-port printers on the local network with DNS-SD, only when you ask.
- **CUPS queues** (`CupsQueues`): lists a CUPS server's queues over IPP, or those of the server this computer is set up to use (`ListConfigured`, from `CUPS_SERVER` or `client.conf`).
- **One printer list** (`PrinterListing`): local CUPS, the configured CUPS server, other servers and DNS-SD, each printer once, with its CUPS queue first.
- **Printer defaults**: reads the printer's default paper, sides, colour and copies, and leaves out what `PrintSettings.PrinterDefaults` leaves to the printer.
- **Auto-rotation** when the printer lays out the PDF: with `AutoRotate`, the orientation is left to the printer or CUPS.
- **Raw port 9100** (`RawSocketTarget`): bytes unchanged to a network printer's raw port (PostScript, PCL, ZPL, ESC/POS).

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

Let a router choose: the PDF as-is when every font is embedded and the printer lists `application/pdf`,
otherwise PWG Raster. The choice and any fallback are written to the audit trail.

```csharp
using PrintRouter router = IppRoutes.CreateRouter("ipp://ward3-printer.local/ipp/print");
PrintRouteResult r = router.Print(job.WithFontsEmbedded());  // r.Route: PdfAsIs or Raster

// Trust a printer's own self-signed certificate by its SHA-256 fingerprint (certificate pinning).
var options = new IppClientOptions();
options.PinnedCertificates.Add(IppCertificates.Fingerprint(IppCertificates.GetServerCertificate("ipps://10.0.0.21/ipp/print")));

// Bytes unchanged to a printer's raw port 9100 (ZPL, PCL, PostScript).
new RawSocketTarget("10.0.0.30").Send(File.ReadAllBytes("labels.zpl"), "labels", CancellationToken.None);
```

Full guide: [docs/Chuvadi.Print/GUIDE.md](https://github.com/arunshivab/chuvadi/blob/main/docs/Chuvadi.Print/GUIDE.md).

MIT licensed.
