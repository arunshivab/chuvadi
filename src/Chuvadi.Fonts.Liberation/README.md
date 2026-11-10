# Chuvadi.Fonts.Liberation

The Liberation fonts 2.1.5 as a pure data pack: Liberation Sans, Serif and Mono in Regular, Bold, Italic and Bold Italic
(12 TrueType files, 4,359,164 bytes), embedded as resources. They are metric-compatible with Arial, Times New Roman and
Courier New, and so the stand-ins for the Helvetica, Times and Courier families of the PDF Standard 14 fonts.

The package lists the files and hands out their bytes and the licence. It parses nothing, references no layout engine
(only Chuvadi.Fonts, for the `IFontFilePack` interface) and reads nothing from disk or the network. So
[Chuvadi.Layout](https://github.com/arunshivab/chuvadi), a PDF library or a print library can all use the same files.
It is a separate package from Chuvadi.Fonts (the LiPi Sans fonts), so an app that does not need Liberation does not carry
its 4.4 MB.

```csharp
using Chuvadi.Fonts;
using Chuvadi.Fonts.Liberation;

foreach (FontFileInfo f in LiberationPack.FindFamily("Liberation Sans"))
{
    Console.WriteLine($"{f.FileName}: {f.StyleName}, {f.Weight}, {f.Length:N0} bytes");
}
byte[] regular = LiberationPack.ReadAllBytes("LiberationSans-Regular.ttf");   // a fresh copy
string licence = LiberationPack.LicenseText;                                   // the OFL text with the Liberation copyright lines
```

With Chuvadi.Layout, add the pack as a font source:

```csharp
options.FontSources.Add(FontPackSource.For(LiberationPack.Instance));
```

| Member | What it does |
|---|---|
| `LiberationPack.Files` | the 12 files (`FontFileInfo`), ordered by file name |
| `LiberationPack.Find(fileName)` | one file (case-insensitive), or null |
| `LiberationPack.FindFamily(familyName)` | the faces of "Liberation Sans", "Liberation Serif" or "Liberation Mono", or an empty list |
| `LiberationPack.Open(fileName)` / `TryOpen` | a read-only stream over the file |
| `LiberationPack.ReadAllBytes(fileName)` | a copy of the file's bytes |
| `LiberationPack.LicenseText` | the SIL Open Font License 1.1 text that applies to every file |
| `LiberationPack.Instance` | the pack as an `IFontFilePack` (name "Chuvadi.Fonts.Liberation") |

## Licence

SIL Open Font License 1.1 (`licenses/Liberation-OFL.txt`). The Reserved Font Name is "Liberation"; the fonts are
shipped unmodified. The OFL allows the fonts to be bundled and redistributed with software but not sold by themselves.
Provenance and the SHA-256 of every file are in `FONTS.md`. Symbol and ZapfDingbats are not included.
