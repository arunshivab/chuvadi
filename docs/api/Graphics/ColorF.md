# ColorF

**Struct** in `Chuvadi.Pdf.Graphics` (Graphics)

An immutable colour value, with support for DeviceGray, DeviceRGB, and DeviceCMYK colour spaces. All component values are in the range [0, 1]. PDF 32000-1:2008 §8.6.4 — Device colour spaces.

```csharp
public readonly struct ColorF : IEquatable<ColorF>
```

## Properties

### `Space`

```csharp
ColorSpace Space
```

Gets the colour space of this colour.

### `Black`

__static__

```csharp
static ColorF Black
```

Opaque black (DeviceGray 0).

### `White`

__static__

```csharp
static ColorF White
```

Opaque white (DeviceGray 1).

### `Transparent`

__static__

```csharp
static ColorF Transparent
```

Fully transparent (DeviceGray 0, alpha 0).

### `C0`

```csharp
float C0 => _c0
```

Gray level for `ColorSpace.Gray`; Red for `ColorSpace.Rgb`; Cyan for `ColorSpace.Cmyk`.

### `C1`

```csharp
float C1 => _c1
```

Alpha for `ColorSpace.Gray`; Green for `ColorSpace.Rgb`; Magenta for `ColorSpace.Cmyk`.

### `C2`

```csharp
float C2 => _c2
```

Zero for `ColorSpace.Gray`; Blue for `ColorSpace.Rgb`; Yellow for `ColorSpace.Cmyk`.

### `C3`

```csharp
float C3 => _c3
```

Alpha for `ColorSpace.Gray` (stored separately); Alpha for `ColorSpace.Rgb`; Key (black) for `ColorSpace.Cmyk`.

### `R`

```csharp
float R => Space == ColorSpace.Rgb ? _c0 : 0f
```

Red component (DeviceRGB only).

### `G`

```csharp
float G => Space == ColorSpace.Rgb ? _c1 : 0f
```

Green component (DeviceRGB only).

### `B`

```csharp
float B => Space == ColorSpace.Rgb ? _c2 : 0f
```

Blue component (DeviceRGB only).

### `Alpha`

```csharp
float Alpha => Space == ColorSpace.Cmyk ? 1f : _c3
```

Alpha/opacity [0 = transparent, 1 = opaque]. Not applicable to CMYK.

### `Gray`

```csharp
float Gray => Space == ColorSpace.Gray ? _c0 : 0f
```

Gray level (DeviceGray only).

## Methods

### `FromGray`

__static__

```csharp
static ColorF FromGray(float gray, float alpha = 1f)
```

Creates a DeviceGray colour. PDF 32000-1:2008 §8.6.4.1 — DeviceGray.

**Parameters**

- `gray` — Gray level [0 = black, 1 = white].
- `alpha` — Opacity [0 = transparent, 1 = opaque].

### `FromRgb`

__static__

```csharp
static ColorF FromRgb(float r, float g, float b, float alpha = 1f)
```

Creates a DeviceRGB colour. PDF 32000-1:2008 §8.6.4.2 — DeviceRGB.

### `FromCmyk`

__static__

```csharp
static ColorF FromCmyk(float c, float m, float y, float k)
```

Creates a DeviceCMYK colour. PDF 32000-1:2008 §8.6.4.4 — DeviceCMYK.

### `FromRgb8`

__static__

```csharp
static ColorF FromRgb8(byte r, byte g, byte b, byte a = 255)
```

Creates a colour from 8-bit sRGB integers (0–255).

### `ToRgb`

```csharp
ColorF ToRgb()
```

Converts this colour to DeviceRGB for compositing purposes. CMYK → RGB uses the standard formula: R = (1-C)*(1-K) etc. PDF 32000-1:2008 §10.3 — Conversions between colour spaces.

### `ToArgb32`

```csharp
uint ToArgb32()
```

Returns this colour as a packed ARGB 32-bit integer (sRGB). Alpha in bits 31-24, Red in 23-16, Green in 15-8, Blue in 7-0.

### `ToString`

```csharp
override string ToString()
```

<inheritdoc/>

### `==`

__static__

```csharp
static bool operator ==(ColorF left, ColorF right) => left.Equals(right)
```

Equality operator.

### `!=`

__static__

```csharp
static bool operator !=(ColorF left, ColorF right) => !left.Equals(right)
```

Inequality operator.

---

_Source: [`src/Chuvadi.Pdf.Graphics/ColorF.cs`](../../../src/Chuvadi.Pdf.Graphics/ColorF.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
