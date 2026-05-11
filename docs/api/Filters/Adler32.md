# Adler32

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Computes and verifies Adler-32 checksums as defined in RFC 1950. Used to validate the integrity of FlateDecode (zlib-wrapped DEFLATE) streams.

```csharp
public static class Adler32
```

## Remarks

Adler-32 is simpler and faster than CRC-32. The checksum consists of two 16-bit sums (S1 and S2) combined into a 32-bit value. RFC 1950 §8.

## Methods

### `Compute`

__static__

```csharp
static uint Compute(ReadOnlySpan<byte> data)
```

Computes the Adler-32 checksum of the given data.

**Parameters**

- `data` — The bytes to checksum.

**Returns:** The 32-bit Adler-32 checksum.

### `Update`

__static__

```csharp
static uint Update(uint checksum, ReadOnlySpan<byte> data)
```

Updates a running Adler-32 checksum with additional bytes. Allows incremental computation over a stream.

**Parameters**

- `checksum` — The current checksum (use 1 to start fresh).
- `data` — The next bytes to incorporate.

**Returns:** The updated checksum.

### `Verify`

__static__

```csharp
static bool Verify(ReadOnlySpan<byte> data, uint expected)
```

Verifies that the Adler-32 checksum of `data` matches the expected value.

**Parameters**

- `data` — The data to verify.
- `expected` — The expected checksum.

**Returns:** True if the checksum matches; false otherwise.

---

_Source: [`src/Chuvadi.Pdf.Filters/Adler32.cs`](../../../src/Chuvadi.Pdf.Filters/Adler32.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
