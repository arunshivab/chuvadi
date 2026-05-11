# PdfTokenizer

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

A forward-only, byte-level tokenizer for PDF streams.

```csharp
public sealed class PdfTokenizer : IDisposable
```

## Remarks

The tokenizer reads bytes from a `Stream` and produces a sequence of `PdfToken` values. It is the lowest layer of the Chuvadi parser stack — it knows nothing about PDF object structure, only about lexical tokens. Calling conventions: 
 
- Call `Read` to advance to the next token. 
- Call `Unread` to push the last token back; the next `Read` will return it again. Only one token of pushback is supported. 
- The tokenizer is not thread-safe.  The tokenizer is allocation-conscious: it reuses an internal buffer for token bytes. Callers that need to keep token bytes beyond the next `Read` call must copy `PdfToken.RawBytes`. PDF 32000-1:2008 §7.2 — Lexical conventions.

## Constructors

### `PdfTokenizer(Stream stream, bool leaveOpen = false)`

Initialises a new `PdfTokenizer` over the given stream.

**Parameters**

- `stream` — The PDF byte stream to tokenize. Must be readable.
- `leaveOpen` — True to leave `stream` open when this tokenizer is disposed; false to close it. <exception cref="ArgumentNullException"> Thrown when `stream` is null. </exception> <exception cref="ArgumentException"> Thrown when `stream` is not readable. </exception>

## Properties

### `Position`

```csharp
long Position => _bufferStreamOffset + _bufferPos
```

Gets the current byte offset in the underlying stream. This is the position of the byte that will be read next.

## Methods

### `Read`

```csharp
PdfToken Read()
```

Reads and returns the next token from the stream. Returns `PdfToken.EndOfStream` when there are no more tokens. <exception cref="PdfTokenizerException"> Thrown when the stream contains bytes that cannot form a valid token. </exception>

### `Unread`

```csharp
void Unread(PdfToken token)
```

Pushes the given token back so that the next call to `Read` returns it again.

**Parameters**

- `token` — The token to push back. <exception cref="InvalidOperationException"> Thrown when a token has already been pushed back without being consumed. </exception>

### `ReadUntil`

```csharp
PdfToken ReadUntil(PdfTokenType type)
```

Reads tokens until a token of the given type is found, then returns it. Skips all intervening tokens. Returns `PdfToken.EndOfStream` if the type is not found.

### `Seek`

```csharp
void Seek(long offset)
```

Seeks the underlying stream to the given byte offset and resets the tokenizer's internal buffer.

**Parameters**

- `offset` — The byte offset to seek to. <exception cref="NotSupportedException"> Thrown when the underlying stream does not support seeking. </exception>

### `Dispose`

```csharp
void Dispose()
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfTokenizer.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfTokenizer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
