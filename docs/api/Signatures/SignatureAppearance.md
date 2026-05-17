# SignatureAppearance

**Class** in `Chuvadi.Pdf.Signatures.Signing` (Signatures)

Visible appearance for a signature field. When supplied on `PdfSigningOptions.Appearance`, the signature field is also marked as a Widget annotation, placed on a specific page within the given rectangle, and gets an appearance stream that PDF readers render in the page view.

```csharp
public sealed class SignatureAppearance
```

## Remarks

A signature is cryptographically complete without an appearance, and most automated workflows don't need one. Visible appearances matter for human-reviewed documents (contracts, agreements) where a reader expects to see "signed by …" on the page.  

 Chuvadi generates a minimal default appearance — a thin black border with the signer's common name and the signing time as a label — unless `PreRenderedAppearanceStream` is supplied, in which case those bytes are used verbatim as the Form XObject content stream and the caller is responsible for whatever fonts / colors / images they reference.

## Properties

### `PageIndex`

```csharp
required int PageIndex
```

Zero-based index of the page on which to place the widget.

### `Rectangle`

```csharp
required double[] Rectangle
```

Rectangle in default user space: [llx, lly, urx, ury].

### `PreRenderedAppearanceStream`

```csharp
byte[]? PreRenderedAppearanceStream
```

Optional pre-rendered Form XObject content stream. When null, a default appearance is generated with the signer's common name and signing time as text.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Signing/SignatureAppearance.cs`](../../../src/Chuvadi.Pdf.Signatures/Signing/SignatureAppearance.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
