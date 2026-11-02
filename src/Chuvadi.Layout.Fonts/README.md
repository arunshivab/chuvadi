# Chuvadi.Layout.Fonts

An offline font pack for [Chuvadi.Layout](https://github.com/arunshivab/chuvadi). It contains 42
SIL OFL 1.1 faces, embedded as resources and exposed as an `IFontSource`. Fonts are read from the
assembly only when a face is first used. Nothing is read from disk or the network.

```csharp
using Chuvadi.Layout;
using Chuvadi.Layout.Fonts;

var options = new LayoutOptions();
options.FontSources.Add(BundledFontSource.Instance);
FontResolver fonts = options.CreateFontResolver();
FontResolution calibri = fonts.Resolve(new FontRequest("Calibri"));   // → Carlito, MetricCompatibleSubstitute
```

## Contents

- **Liberation Sans / Serif / Mono:** metric-compatible with Arial / Times New Roman / Courier New.
  Four styles each.
- **Carlito:** metric-compatible with Calibri. Four styles.
- **Caladea:** metric-compatible with Cambria. Four styles.
- **LiPi Sans:** Latin, four styles.
- **LiPi Sans Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Tamil, Telugu, Kannada, Malayalam:**
  Regular and Bold.

Provenance and the exact conversion steps are in `FONTS.md`; the licences are in `licenses/`.
