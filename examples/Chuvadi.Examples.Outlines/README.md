# Chuvadi.Examples.Outlines

Prints the document outline (bookmarks) tree.

## Run

```bash
dotnet run --project examples/Chuvadi.Examples.Outlines -- input.pdf
```

## Output

```
- Chapter 1: Introduction  → page 1
  - 1.1 Background  → page 3
  - 1.2 Scope  → page 7
- Chapter 2: Method  → page 12
  ...
```

## What it shows

- `OutlineReader.GetOutline(document)` — returns the top-level items.
- Each `OutlineItem` has `Title`, `DestinationPageIndex`, and `Children`.
- Destinations are resolved from PDF named destinations, GoTo actions, and
  explicit /Dest arrays.
