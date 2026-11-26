# Changelog — Chuvadi.Fonts

## 1.2.0 (2026-11-26)

Additive (D-418, D-420); the 22 LiPi Sans files and every 1.1.0 member are unchanged. chuvadi-pdf 3.36.0 requires
this version.

- **New face: Noto Emoji** (monochrome outline emoji), `NotoEmoji-Regular.ttf`, version 3.002, a static weight-400
  instance of Google's variable font, added byte for byte as delivered (SHA-256 `1ada997c…`). Provenance, the pinned
  google/fonts commit and the instancing command are in FONTS.md. `FontPack.Files` lists 23 files; the new one comes
  last, in file-name order. `FindFamily("Noto Emoji")` finds it. Like the LiPi Sans files, it has no typographic
  family of its own and width class 5.
- **New `FontScript.Emoji`**, added at the end, so the ten existing values keep their numbers. Code that switches over
  every `FontScript` value now meets one more.
- **New `FontPack.GetLicenseText(string fileName)`:** the licence text that comes with a file. Since 1.2.0 the pack
  holds two OFL texts with different copyright lines. `FontPack.LicenseText` stays the LiPi Sans text, unchanged, and
  is now documented as such. `FontPack.Instance.GetLicenseText(fileName)` returns the same text as the new method, so
  for `NotoEmoji-Regular.ttf` it now returns Google's OFL text instead of the LiPi one.
- **Licence files:** new `Licenses/NotoEmoji-OFL.txt` (the upstream `OFL.txt`, byte for byte), embedded and packed in
  `licenses/`. Noto Emoji declares no Reserved Font Name, so the instance keeps its name.
- **Package:** about 2.5 MB (1.1.0: 1.9 MB); fonts 4,970,604 bytes (+886,628). New tags `emoji` and `noto`.
- **Trimming and NativeAOT (D-217, D-227, on main since 6 Nov):** the package is now marked `IsTrimmable` and
  `IsAotCompatible`, and the analyzers run on every build. There is no API change, and the embedded fonts are kept
  when trimming.

## 1.1.0 (6 Nov 2026, tag fonts-v1.1.0)

Additive only (D-195, D-197); nothing in 1.0.0 changes.

- New `IFontFilePack`: `Name`, `Files`, `Open(fileName)` and `GetLicenseText(fileName)`. A font pack that implements
  it can be read by any library the same way; Chuvadi.Layout turns one into a font source (`FontPackSource`), and
  Chuvadi.Layout.Fonts implements it for its substitute fonts.
- New record `FontFileInfo`: file name, family, typographic family (name ID 16, or the family), style, full name,
  PostScript name, weight, italic, width class, length and SHA-256. `FontPackFile` is unchanged.
- New `FontPack.Instance`: this pack as an `IFontFilePack` (name "Chuvadi.Fonts"; every LiPi Sans file has no
  typographic family of its own and width class 5, as its tables state).

## 1.0.0 (5 Nov 2026, tag fonts-v1.0.0)

- First release (D-101, D-113): the 22 LiPi Sans static TrueType files (Latin Regular, Bold, Italic, Bold Italic;
  Bengali, Devanagari, Gujarati, Gurmukhi, Kannada, Malayalam, Odia, Tamil, Telugu Regular and Bold), moved
  unchanged from Chuvadi.Layout.Fonts, with `FontPack` (`Files`, `Find`, `FindFamily`, `Open`, `TryOpen`,
  `ReadAllBytes`, `LicenseText`), `FontPackFile` and `FontScript`. Pure BCL, no dependencies.
