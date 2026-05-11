# Chuvadi.Examples.FormFill

Reads and fills AcroForm fields in a PDF.

## Run — list mode

```bash
dotnet run --project examples/Chuvadi.Examples.FormFill -- input.pdf
```

Prints every form field with its type, fully-qualified name, and current value.

## Run — fill mode

```bash
dotnet run --project examples/Chuvadi.Examples.FormFill -- \
  input.pdf output.pdf "patient.name=Jane Doe" "patient.dob=1985-04-12"
```

Fills the named fields and writes a new PDF. `/NeedAppearances` is set to true
so PDF viewers regenerate appearance streams using their own renderers — this
is the simplest cross-viewer behaviour.

## What it shows

- `FormReader.GetFields(document)` — flat list of every field across all pages.
- Fully-qualified names handle nested field hierarchies (`patient.name`,
  `patient.address.street`).
- `FormFiller.Fill(stream, document, values)` — writes the modified PDF.
