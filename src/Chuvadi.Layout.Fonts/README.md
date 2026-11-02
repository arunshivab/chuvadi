# Chuvadi.Layout.Fonts

An offline font pack for [Chuvadi.Layout](https://github.com/arunshivab/chuvadi). It contains 29
SIL OFL 1.1 faces, embedded as resources and exposed as an `IFontSource`: metric-compatible substitutes for the
Office core fonts, and closer designs for Georgia and Segoe UI. Fonts are read from the
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
- **Gelasio:** for Georgia (its designer states metric compatibility; Layout treats it as non-metric until measured).
  Four styles.
- **Selawik:** for Segoe UI, a replacement design without Segoe UI's kerning. Regular, Bold, Light, Semilight and
  Semibold; "Segoe UI Light", "Segoe UI Semilight" and "Segoe UI Semibold" map to the face of the same name.

The LiPi Sans fonts (Latin and nine Indic scripts) moved to the Chuvadi.Fonts package, which Chuvadi.Layout uses as
its built-in fonts (`LayoutOptions.UseBuiltInFonts`); they no longer need to be added as a font source.

Provenance and the exact conversion steps are in `FONTS.md`; the licences are in `licenses/`.
