# Chuvadi.Examples.Annotations

Reads existing PDF annotations and adds a sticky-note + "CONFIDENTIAL" stamp.

## Run

```bash
dotnet run --project examples/Chuvadi.Examples.Annotations -- input.pdf annotated.pdf
```

## What it shows

- `AnnotationReader.GetAllAnnotations(document)` — flat list of every annotation
  across every page, decoded into typed model objects (`TextAnnotation`,
  `LinkAnnotation`, `MarkupAnnotation`, `StampAnnotation`, etc.).
- `AnnotationWriter.Add(stream, document, annotations)` — writes a new PDF with
  the additional annotations merged into each page's `/Annots` array.

## Supported subtypes

Text, Link, FreeText, Highlight, Underline, Squiggly, StrikeOut, Stamp, Ink.
Other subtypes load as `GenericAnnotation` and preserve their raw `/Subtype`
name and rectangle.
