# Chuvadi.Examples.Redaction

Demonstrates Chuvadi's PHI-safe redaction (BASELINE B15) and verifies the
redacted string is byte-level absent from the output.

## Run

```bash
dotnet run --project examples/Chuvadi.Examples.Redaction -- \
  input.pdf output.pdf "123-45-6789"
```

The third argument is the literal string you expect to be redacted. The program
greps the output bytes for it and exits non-zero if it's still present — proving
the redaction was effective, not just visual.

## What it shows

- Constructing `RedactionOptions` with a list of rectangles in PDF user space.
- Calling `Redactor.Apply` to rewrite the content stream and exclude the
  original stream objects (BASELINE B16).
- Verifying byte-level absence — the gold standard for clinical and legal use.

## Coordinate tip

PDF user space puts the origin at the bottom-left corner with Y growing upward.
The default page size is 612×792 points (US Letter at 72 dpi). The example
redacts a rectangle near the top of the page; adjust `(x, y, width, height)`
to match where your PHI sits.
