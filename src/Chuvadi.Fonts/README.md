# Chuvadi.Fonts

The LiPi Sans fonts as a pure-BCL .NET package: 22 static TrueType files embedded as resources, with
their names, styles, lengths and SHA-256 hashes. Latin in four styles (Regular, Bold, Italic, Bold Italic) and
nine Indic scripts in Regular and Bold: Bengali, Devanagari, Gujarati, Gurmukhi, Kannada, Malayalam, Odia, Tamil
and Telugu. About 3.9 MB.

The package lists the files and hands out their bytes. It parses nothing, caches nothing, and reads nothing from
disk or the network. Chuvadi.Layout uses it as its built-in fonts; any other library can read the same files.

```csharp
using Chuvadi.Fonts;

foreach (FontPackFile f in FontPack.FindFamily("LiPi Sans Tamil"))
{
    Console.WriteLine($"{f.FileName}: {f.StyleName}, {f.Weight}, {f.Length:N0} bytes");
}

using Stream tamil = FontPack.Open("LiPi-Sans-Tamil-Regular.ttf");   // read-only, positioned at 0
byte[] bold = FontPack.ReadAllBytes("LiPi-Sans-Tamil-Bold.ttf");      // a fresh copy
```

| Member | What it does |
|---|---|
| `FontPack.Files` | every file, ordered by file name |
| `FontPack.Find(fileName)` | one file, or null |
| `FontPack.FindFamily(familyName)` | the faces of a family, or an empty list |
| `FontPack.Open(fileName)` / `TryOpen` | a read-only stream over the file |
| `FontPack.ReadAllBytes(fileName)` | a copy of the file's bytes |
| `FontPack.LicenseText` | the SIL Open Font License 1.1 text |
| `FontPackFile` | file name, family, style, full name, PostScript name, weight, italic, script, length, SHA-256 |

## Licence

The code is MIT. The fonts are under the SIL Open Font License 1.1 (in `licenses/`), which allows them to be
used, embedded in documents (for example PDF) and redistributed with software. Provenance and how the static faces
were made are in `FONTS.md`.
