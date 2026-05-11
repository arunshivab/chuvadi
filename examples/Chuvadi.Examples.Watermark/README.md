# Chuvadi.Examples.Watermark

Stamps a diagonal text watermark across every page of a PDF.

## Run

```bash
dotnet run --project examples/Chuvadi.Examples.Watermark -- \
  input.pdf output.pdf DRAFT
```

## What it shows

- Configuring `TextWatermarkOptions` (font size, opacity, rotation, colour).
- Calling `WatermarkStamper.Apply` to write a new PDF without mutating the input.

The output is byte-for-byte the original document with one extra content stream
per page drawing the rotated text.
