# PdfTokenType

**Enum** in `Chuvadi.Pdf.Primitives` (Primitives)

Identifies the type of a token produced by `PdfTokenizer`.

```csharp
public enum PdfTokenType
```

## Remarks

The PDF specification defines a small set of token categories. The tokenizer classifies every byte sequence it reads into one of these. Higher-level parsers combine tokens into `PdfPrimitive` objects. PDF 32000-1:2008 §7.2 — Lexical conventions.

## Values

| Name | Description |
|---|---|
| `Integer` | A signed integer literal, e.g. `42` or `-7`. PDF 32000-1:2008 §7.3.3. |
| `Real` | A real number literal containing a decimal point, e.g. `3.14` or `-.5`. PDF 32000-1:2008 §7.3.3. |
| `Name` | A name token beginning with a solidus, e.g. `/FlateDecode`. The raw bytes do not include the leading solidus. PDF 32000-1:2008 §7.3.5. |
| `LiteralString` | A literal string enclosed in parentheses, e.g. `(Hello)`. The raw bytes include the surrounding parentheses. PDF 32000-1:2008 §7.3.4.2. |
| `HexString` | A hexadecimal string enclosed in angle brackets, e.g. `&lt;48656C6C6F&gt;`. The raw bytes include the surrounding angle brackets. PDF 32000-1:2008 §7.3.4.3. |
| `DictionaryStart` | The start of a dictionary: `&lt;&lt;`. PDF 32000-1:2008 §7.3.7. |
| `DictionaryEnd` | The end of a dictionary: `&gt;&gt;`. PDF 32000-1:2008 §7.3.7. |
| `ArrayStart` | The start of an array: `[`. PDF 32000-1:2008 §7.3.6. |
| `ArrayEnd` | The end of an array: `]`. PDF 32000-1:2008 §7.3.6. |
| `True` | The PDF keyword `true`. PDF 32000-1:2008 §7.3.2. |
| `False` | The PDF keyword `false`. PDF 32000-1:2008 §7.3.2. |
| `Null` | The PDF keyword `null`. PDF 32000-1:2008 §7.3.9. |
| `Reference` | An indirect object reference suffix: the keyword `R` following two integers, e.g. the `R` in `12 0 R`. PDF 32000-1:2008 §7.3.10. |
| `ObjectStart` | The keyword `obj` — begins an indirect object definition. PDF 32000-1:2008 §7.3.10. |
| `ObjectEnd` | The keyword `endobj` — ends an indirect object definition. PDF 32000-1:2008 §7.3.10. |
| `StreamStart` | The keyword `stream` — begins a stream body. The stream bytes follow immediately after the mandatory line ending. PDF 32000-1:2008 §7.3.8.1. |
| `StreamEnd` | The keyword `endstream` — ends a stream body. PDF 32000-1:2008 §7.3.8.1. |
| `XRef` | The keyword `xref` — begins a cross-reference table. PDF 32000-1:2008 §7.5.4. |
| `Trailer` | The keyword `trailer` — begins the trailer dictionary. PDF 32000-1:2008 §7.5.5. |
| `StartXRef` | The keyword `startxref` — precedes the byte offset of the xref table. PDF 32000-1:2008 §7.5.5. |
| `Keyword` | An unrecognised keyword or bare word, e.g. operator names in content streams such as `BT`, `ET`, `Tf`, `Tj`. The raw bytes contain the exact keyword bytes. |
| `EndOfFile` | The PDF end-of-file marker `%%EOF`. PDF 32000-1:2008 §7.5.5. |
| `EndOfStream` | The tokenizer has reached the end of the underlying stream and there are no more tokens to read. |

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfTokenType.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfTokenType.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
