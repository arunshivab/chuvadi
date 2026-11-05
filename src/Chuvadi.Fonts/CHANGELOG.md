# Changelog — Chuvadi.Fonts

## 1.1.0 (unreleased)

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
