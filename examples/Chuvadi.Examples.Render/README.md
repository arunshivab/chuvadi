# Chuvadi.Examples.Render

Rasterizes every page of a PDF to PNG using Chuvadi's scanline rasterizer.

## Run

```bash
dotnet run --project examples/Chuvadi.Examples.Render -- input.pdf pages/ 150
```

The third argument is DPI (default 96).

## What it shows

- Zero-dependency rasterization. No SkiaSharp, no native libraries, no
  PDFium. Every pixel computed in managed C#.
- Standard PDF fonts (Helvetica, Times, Courier) plus embedded TrueType.
- Output: one PNG per page in the output directory.

## DPI guidance

- **96** — fast, screen-quality. ~600×776 px for a US Letter page.
- **150** — readable text, decent thumbnails. ~937×1212 px.
- **300** — print quality. ~1875×2425 px. Slow on large documents.
