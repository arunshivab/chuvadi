# Chuvadi.Print

Pure-BCL printing primitives for .NET — zero NuGet dependencies, part of the [Chuvadi](https://github.com/arunshivab/chuvadi) family.

This package (v0.1) is the **portable foundation**: device-independent print settings, the full set of paper sizes / orientations / duplex / colour / scale / alignment options, page selection, and the self-describing **spool envelope** used to carry a document + its settings across a network.

Windows spooling (`Chuvadi.Print.Windows`) and PDF rasterisation (via `Chuvadi.Pdf`) arrive in later layers.

```csharp
using Chuvadi.Print;

var settings = new PrintSettings
{
    Pages = PageSelection.Range("1-4, 7"),
    Copies = 2,
    Duplex = Duplex.Vertical,
    Paper = PaperSize.A4,
    Orientation = PageOrientation.Portrait,
    Scale = ScaleMode.FitToPage,
    Alignment = ContentAlignment.Center,
    Margins = Margins.Default
};

// Carry a document + settings across the wire:
var envelope = new SpoolEnvelope(pdfBytes, settings);
byte[] onTheWire = envelope.ToArray();
SpoolEnvelope received = SpoolEnvelope.FromArray(onTheWire);
```

MIT licensed.
