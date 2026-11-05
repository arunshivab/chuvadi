# Chuvadi.Layout.Fonts

Substitute fonts for documents, as a pure data pack. It contains 29 SIL OFL 1.1 faces, embedded as resources:
metric-compatible substitutes for fonts documents often name, and closer designs for Georgia and Segoe UI. The
package lists the files and hands out their bytes and licences. It parses nothing, references no layout engine (only
Chuvadi.Fonts, for the `IFontFilePack` interface), and reads nothing from disk or the network. So
[Chuvadi.Layout](https://github.com/arunshivab/chuvadi), a PDF library or a print library can all use the same files.

With Chuvadi.Layout, add the pack as a font source:

```csharp
using Chuvadi.Layout;
using Chuvadi.Layout.Fonts;

var options = new LayoutOptions();
options.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));
FontResolver fonts = options.CreateFontResolver();
FontResolution calibri = fonts.Resolve(new FontRequest("Calibri"));   // → Carlito, MetricCompatibleSubstitute
```

`FontPackSource.For` (in Chuvadi.Layout) returns one shared source per pack, so each face is parsed once per process.

Without a layout engine, read the files directly:

```csharp
using Chuvadi.Fonts;
using Chuvadi.Layout.Fonts;

foreach (FontFileInfo f in SubstituteFontPack.FindFamily("Liberation Sans"))
{
    Console.WriteLine($"{f.FileName}: {f.StyleName}, {f.Weight}, {f.Length:N0} bytes");
}
byte[] carlito = SubstituteFontPack.ReadAllBytes("Carlito-Regular.ttf");   // a fresh copy
string licence = SubstituteFontPack.GetLicenseText("Carlito-Regular.ttf");  // the OFL text with Carlito's copyright
```

| Member | What it does |
|---|---|
| `SubstituteFontPack.Files` | every file (`FontFileInfo`), ordered by file name |
| `SubstituteFontPack.Find(fileName)` | one file, or null |
| `SubstituteFontPack.FindFamily(familyName)` | the faces of a family or typographic family ("Selawik" gives all five), or an empty list |
| `SubstituteFontPack.Open(fileName)` / `TryOpen` | a read-only stream over the file |
| `SubstituteFontPack.ReadAllBytes(fileName)` | a copy of the file's bytes |
| `SubstituteFontPack.GetLicenseText(fileName)` | the licence that applies to the file |
| `SubstituteFontPack.Instance` | the pack as an `IFontFilePack` (Chuvadi.Fonts) |

## Contents

- **Liberation Sans / Serif / Mono:** metric-compatible with Arial / Times New Roman / Courier New.
  Four styles each.
- **Carlito:** metric-compatible with Calibri. Four styles.
- **Caladea:** the nearest design to Cambria, but not metric-compatible with the Cambria Word uses (6.5% wider);
  reported as `Substitute`. Four styles.
- **Gelasio:** for Georgia (its designer states metric compatibility; Layout treats it as non-metric until measured).
  Four styles.
- **Selawik:** for Segoe UI, a replacement design without Segoe UI's kerning. Regular, Bold, Light, Semilight and
  Semibold; "Segoe UI Light", "Segoe UI Semilight" and "Segoe UI Semibold" map to the face of the same name.

The LiPi Sans fonts (Latin and nine Indic scripts) are in the Chuvadi.Fonts package, which Chuvadi.Layout uses as its
built-in fonts (`LayoutOptions.UseBuiltInFonts`); they do not need to be added as a font source.

Provenance and the exact conversion steps are in `FONTS.md`; the licences are in `licenses/`.
